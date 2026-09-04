#if DEBUG
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG-only control for measuring object and DBMOD rollback of one ordinary
/// uncommitted target transaction.
/// </summary>
public sealed class AutoCadRoofImportNormalTransactionAbortProofCommand
{
    internal const string CommandName = "AK_DEBUG_C1_NORMAL_TX_ABORT";
    internal const string TimelineMarker = "ROOF_IMPORT_C1_NORMAL_TX_ABORT";
    internal const string ResultMarker = "ROOF_IMPORT_C1_NORMAL_TX_ABORT_RESULT";

    [CommandMethod(CommandName, CommandFlags.Modal)]
    public void Execute()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var temporaryBtrName = $"AK_C1_TX_ABORT_{Guid.NewGuid():N}";
        var dbmodBefore = ReadDbmod();
        WriteCheckpoint(document.Editor, "before-target-transaction", dbmodBefore);

        var observer = new NormalTransactionAbortObserver(
            document,
            dbmodBefore);

        try
        {
            AddTemporaryBtrAndAbort(
                document.Database,
                document.Editor,
                temporaryBtrName);

            var dbmodAfterDispose = ReadDbmod();
            observer.RecordAfterTransactionDispose(dbmodAfterDispose);
            WriteCheckpoint(
                document.Editor,
                "after-target-transaction-dispose",
                dbmodAfterDispose);

            var temporaryBtrAbsent = AuditTemporaryBtrAbsent(
                document.Database,
                temporaryBtrName);
            var dbmodAfterAuditDispose = ReadDbmod();
            observer.RecordReadonlyAudit(
                temporaryBtrAbsent);
            WriteCheckpoint(
                document.Editor,
                "after-readonly-audit-dispose",
                dbmodAfterAuditDispose,
                $"temporaryBtrAbsent={Bool(temporaryBtrAbsent)}");
        }
        catch (System.Exception exception)
        {
            observer.RecordCommandBodyFailure();
            WriteSafe(
                document.Editor,
                $"{TimelineMarker} phase=command-body-failed " +
                $"error={Token(exception.GetType().Name)} " +
                $"message={Token(exception.Message)}");
        }
    }

    private static void AddTemporaryBtrAndAbort(
        Database database,
        Editor editor,
        string temporaryBtrName)
    {
        using (var transaction = database.TransactionManager.StartTransaction())
        {
            var blockTable = (BlockTable)transaction.GetObject(
                database.BlockTableId,
                OpenMode.ForWrite);
            if (blockTable.Has(temporaryBtrName))
            {
                throw new InvalidOperationException(
                    "The generated temporary block name already exists.");
            }

            var temporaryBtr = new BlockTableRecord
            {
                Name = temporaryBtrName,
            };
            blockTable.Add(temporaryBtr);
            transaction.AddNewlyCreatedDBObject(temporaryBtr, add: true);

            WriteCheckpoint(editor, "after-btr-add", ReadDbmod());
        }
    }

    private static bool AuditTemporaryBtrAbsent(
        Database database,
        string temporaryBtrName)
    {
        using var transaction =
            database.TransactionManager.StartOpenCloseTransaction();
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        return !blockTable.Has(temporaryBtrName);
    }

    private static int ReadDbmod() =>
        Convert.ToInt32(
            AcApplication.GetSystemVariable("DBMOD"),
            CultureInfo.InvariantCulture);

    private static void WriteCheckpoint(
        Editor editor,
        string phase,
        int value,
        string? suffix = null)
    {
        WriteSafe(
            editor,
            $"{TimelineMarker} phase={phase} value={value}" +
            (string.IsNullOrEmpty(suffix) ? string.Empty : $" {suffix}"));
    }

    private static void WriteSafe(Editor editor, string message)
    {
        try
        {
            editor.WriteMessage($"\n{message}");
        }
        catch
        {
            // A diagnostic write must not escape into the measured command lifecycle.
        }
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Token(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Replace('\r', ' ').Replace('\n', ' ').Replace(' ', '_');

    private sealed class NormalTransactionAbortObserver
    {
        private const int UnavailableDbmod = -1;

        private readonly Document _document;
        private readonly Editor _editor;
        private readonly int _dbmodBefore;
        private int _dbmodAfterDispose = UnavailableDbmod;
        private bool _temporaryBtrAbsent;
        private bool _auditCompleted;
        private bool _commandBodyFailed;
        private bool _isArmed = true;

        internal NormalTransactionAbortObserver(
            Document document,
            int dbmodBefore)
        {
            _document = document;
            _editor = document.Editor;
            _dbmodBefore = dbmodBefore;

            document.CommandEnded += CommandEnded;
            document.CommandCancelled += CommandCancelled;
            document.CommandFailed += CommandFailed;
        }

        internal void RecordAfterTransactionDispose(int value)
        {
            _dbmodAfterDispose = value;
        }

        internal void RecordReadonlyAudit(bool temporaryBtrAbsent)
        {
            _temporaryBtrAbsent = temporaryBtrAbsent;
            _auditCompleted = true;
        }

        internal void RecordCommandBodyFailure()
        {
            _commandBodyFailed = true;
        }

        private void CommandEnded(object? sender, CommandEventArgs e)
        {
            try
            {
                if (!Matches(sender, e))
                {
                    return;
                }

                Disarm();
                var dbmodCommandEnded = ReadDbmod();
                WriteCheckpoint(_editor, "command-ended", dbmodCommandEnded);

                var passed =
                    !_commandBodyFailed &&
                    _auditCompleted &&
                    _temporaryBtrAbsent &&
                    dbmodCommandEnded == _dbmodBefore;
                WriteSafe(
                    _editor,
                    $"{ResultMarker} " +
                    $"temporaryBtrAbsent={Bool(_temporaryBtrAbsent)} " +
                    $"dbmodBefore={_dbmodBefore} " +
                    $"dbmodAfterDispose={_dbmodAfterDispose} " +
                    $"dbmodCommandEnded={dbmodCommandEnded} " +
                    $"result={(passed ? "pass" : "fail")}");
            }
            catch (System.Exception exception)
            {
                DisarmSafely();
                WriteSafe(
                    _editor,
                    $"{ResultMarker} " +
                    $"temporaryBtrAbsent={Bool(_temporaryBtrAbsent)} " +
                    $"dbmodBefore={_dbmodBefore} " +
                    $"dbmodAfterDispose={_dbmodAfterDispose} " +
                    $"dbmodCommandEnded={UnavailableDbmod} result=fail " +
                    $"error={Token(exception.GetType().Name)}");
            }
        }

        private void CommandCancelled(object? sender, CommandEventArgs e)
        {
            CompleteWithoutMeasurement(sender, e, "cancelled");
        }

        private void CommandFailed(object? sender, CommandEventArgs e)
        {
            CompleteWithoutMeasurement(sender, e, "failed");
        }

        private void CompleteWithoutMeasurement(
            object? sender,
            CommandEventArgs e,
            string terminal)
        {
            try
            {
                if (!Matches(sender, e))
                {
                    return;
                }

                Disarm();
                WriteSafe(
                    _editor,
                    $"{TimelineMarker} phase=command-{terminal} cleanup=true");
            }
            catch
            {
                DisarmSafely();
            }
        }

        private bool Matches(object? sender, CommandEventArgs e) =>
            _isArmed &&
            ReferenceEquals(sender, _document) &&
            string.Equals(
                e.GlobalCommandName ?? string.Empty,
                CommandName,
                StringComparison.Ordinal);

        private void Disarm()
        {
            if (!_isArmed)
            {
                return;
            }

            _isArmed = false;
            _document.CommandEnded -= CommandEnded;
            _document.CommandCancelled -= CommandCancelled;
            _document.CommandFailed -= CommandFailed;
        }

        private void DisarmSafely()
        {
            try
            {
                Disarm();
            }
            catch
            {
                _isArmed = false;
            }
        }
    }
}
#endif
