#if DEBUG
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

internal static class RoofExternalImportC1DbmodTimeline
{
    internal const string Marker = "ROOF_IMPORT_C1_DBMOD_TIMELINE";

    private static Document? _armedDocument;
    private static bool _isArmed;
    private static RoofExternalImportFaultMode _armedFaultMode;
    private static string _armedCommand = string.Empty;
    private static RollbackProof? _rollbackProof;

    internal static void Arm(Document? document, RoofExternalImportFaultMode faultMode)
    {
        Disarm();
        if (document is null || faultMode == RoofExternalImportFaultMode.None)
        {
            return;
        }

        _armedDocument = document;
        _armedFaultMode = faultMode;
        _armedCommand = faultMode == RoofExternalImportFaultMode.AfterMappingValidation
            ? AutoCadRoofExternalImportAbortProofCommands.AfterInsertCommand
            : AutoCadRoofExternalImportAbortProofCommands.AfterSanitationCommand;
        _isArmed = true;
        document.CommandEnded += CommandEnded;
        document.CommandCancelled += CommandCancelledOrFailed;
        document.CommandFailed += CommandCancelledOrFailed;
    }

    internal static void Write(
        Document document,
        RoofExternalImportFaultMode faultMode,
        string phase)
    {
        if (faultMode != RoofExternalImportFaultMode.AfterMappingValidation)
        {
            return;
        }

        Write(document.Editor, phase);
    }

    internal static void RecordRollbackProof(
        Document document,
        RoofExternalImportFaultMode faultMode,
        bool transactionCommitted,
        bool rootAbsent,
        bool importedObjectsAbsent,
        bool topLevelReferenceAbsent,
        bool supportBtrsUnchanged,
        int dbmodBefore)
    {
        if (faultMode == RoofExternalImportFaultMode.None ||
            !_isArmed ||
            faultMode != _armedFaultMode ||
            !ReferenceEquals(document, _armedDocument))
        {
            return;
        }

        _rollbackProof = new(
            transactionCommitted,
            rootAbsent,
            importedObjectsAbsent,
            topLevelReferenceAbsent,
            supportBtrsUnchanged,
            dbmodBefore,
            faultMode);
    }

    private static void CommandEnded(object? sender, CommandEventArgs e)
    {
        if (!_isArmed ||
            !ReferenceEquals(sender, _armedDocument) ||
            !string.Equals(
                e.GlobalCommandName ?? string.Empty,
                _armedCommand,
                StringComparison.Ordinal))
        {
            return;
        }

        var document = _armedDocument;
        var rollbackProof = _rollbackProof;
        Disarm();
        if (document is not null)
        {
            WriteCommandEnded(document.Editor, rollbackProof);
        }
    }

    private static void CommandCancelledOrFailed(object? sender, CommandEventArgs e)
    {
        if (_isArmed &&
            ReferenceEquals(sender, _armedDocument) &&
            string.Equals(
                e.GlobalCommandName ?? string.Empty,
                _armedCommand,
                StringComparison.Ordinal))
        {
            Disarm();
        }
    }

    private static void Disarm()
    {
        var document = _armedDocument;
        _isArmed = false;
        _armedDocument = null;
        _armedFaultMode = RoofExternalImportFaultMode.None;
        _armedCommand = string.Empty;
        _rollbackProof = null;
        if (document is null)
        {
            return;
        }

        document.CommandEnded -= CommandEnded;
        document.CommandCancelled -= CommandCancelledOrFailed;
        document.CommandFailed -= CommandCancelledOrFailed;
    }

    private static void Write(Editor editor, string phase)
    {
        try
        {
            var value = Convert.ToInt32(
                AcApplication.GetSystemVariable("DBMOD"),
                CultureInfo.InvariantCulture);
            Write(editor, phase, value);
        }
        catch
        {
            // A DEBUG observation must never change the command failure path.
        }
    }

    private static void WriteCommandEnded(Editor editor, RollbackProof? proof)
    {
        try
        {
            var dbmodCommandEnded = Convert.ToInt32(
                AcApplication.GetSystemVariable("DBMOD"),
                CultureInfo.InvariantCulture);
            Write(editor, "command-ended", dbmodCommandEnded);
            if (proof is null)
            {
                return;
            }

            var dbmodEquivalent = proof.DbmodBefore == dbmodCommandEnded;
            var objectRollbackPass = !proof.TransactionCommitted &&
                proof.RootAbsent &&
                proof.ImportedObjectsAbsent &&
                proof.TopLevelReferenceAbsent &&
                proof.SupportBtrsUnchanged;
            editor.WriteMessage(
                "\nROOF_IMPORT_C1_ABORT_PROOF " +
                $"mode={proof.FaultMode} " +
                $"transactionCommitted={Bool(proof.TransactionCommitted)} " +
                $"rootAbsent={Bool(proof.RootAbsent)} " +
                $"importedObjectsAbsent={Bool(proof.ImportedObjectsAbsent)} " +
                $"topLevelReferenceAbsent={Bool(proof.TopLevelReferenceAbsent)} " +
                $"supportBtrsUnchanged={Bool(proof.SupportBtrsUnchanged)} " +
                $"dbmodBefore={proof.DbmodBefore} dbmodCommandEnded={dbmodCommandEnded} " +
                $"dbmodEquivalent={Bool(dbmodEquivalent)} " +
                $"result={(objectRollbackPass ? "pass" : "fail")}");
        }
        catch
        {
            // A DEBUG observation must never change the command failure path.
        }
    }

    private static void Write(Editor editor, string phase, int value) =>
        editor.WriteMessage($"\n{Marker} phase={phase} value={value}");

    private static string Bool(bool value) => value ? "true" : "false";

    private sealed record RollbackProof(
        bool TransactionCommitted,
        bool RootAbsent,
        bool ImportedObjectsAbsent,
        bool TopLevelReferenceAbsent,
        bool SupportBtrsUnchanged,
        int DbmodBefore,
        RoofExternalImportFaultMode FaultMode);
}
#endif
