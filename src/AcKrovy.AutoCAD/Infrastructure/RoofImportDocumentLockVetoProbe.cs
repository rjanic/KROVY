#if DEBUG
using Autodesk.AutoCAD.ApplicationServices;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG-only, explicitly armed probe for the documented document-lock veto boundary.
/// It performs no database access and is one-shot for one exact document and command.
/// </summary>
internal static class RoofImportDocumentLockVetoProbe
{
    internal const string DashInsertGlobalCommandName = "-INSERT";
    internal const string PaletteInsertGlobalCommandName = "INSERT";
    internal const string ClassicInsertGlobalCommandName = "CLASSICINSERT";

    private static bool _isStarted;
    private static bool _isArmed;
    private static Document? _armedDocument;
    private static string? _armedGlobalCommandName;
    private static Document? _pendingVetoDocument;
    private static string? _pendingVetoCommand;

    public static void Start()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        var documents = AcApplication.DocumentManager;
        documents.DocumentLockModeChanged += DocumentLockModeChanged;
        documents.DocumentLockModeChangeVetoed += DocumentLockModeChangeVetoed;
        documents.DocumentToBeDestroyed += DocumentToBeDestroyed;
    }

    public static void Stop()
    {
        if (_isStarted)
        {
            var documents = AcApplication.DocumentManager;
            documents.DocumentLockModeChanged -= DocumentLockModeChanged;
            documents.DocumentLockModeChangeVetoed -= DocumentLockModeChangeVetoed;
            documents.DocumentToBeDestroyed -= DocumentToBeDestroyed;
            _isStarted = false;
        }

        RoofImportApprovedInsertProofSession.ClearAll("plugin-stop");
        RoofImportDirectInsertProofSession.ClearAll("plugin-stop");
        ClearState();
    }

    public static void ArmDashInsert(Document document) =>
        Arm(document, DashInsertGlobalCommandName);

    public static void ArmPaletteInsert(Document document) =>
        Arm(document, PaletteInsertGlobalCommandName);

    public static void ArmClassicInsert(Document document) =>
        Arm(document, ClassicInsertGlobalCommandName);

    private static void Arm(Document document, string globalCommandName)
    {
        ClearState();
        _armedDocument = document;
        _armedGlobalCommandName = globalCommandName;
        _isArmed = true;
        Write(
            document,
            $"ROOF_IMPORT_LOCK_VETO_ARMED command={globalCommandName} " +
            "mode=one-shot state=on");
    }

    public static void Disarm(Document? document)
    {
        if (document is not null)
        {
            RoofImportApprovedInsertProofSession.ClearDocument(document, "veto-disarmed");
            RoofImportDirectInsertProofSession.ClearDocument(document, "veto-disarmed");
        }

        ClearState();
        Write(document, "ROOF_IMPORT_LOCK_VETO_ARMED state=off");
    }

    private static void DocumentToBeDestroyed(
        object? sender,
        DocumentCollectionEventArgs e)
    {
        if (e.Document is not { } document)
        {
            return;
        }

        RoofImportApprovedInsertProofSession.ClearDocument(
            document,
            "document-destroyed");
        RoofImportDirectInsertProofSession.ClearDocument(
            document,
            "document-destroyed");
        if (ReferenceEquals(document, _armedDocument) ||
            ReferenceEquals(document, _pendingVetoDocument))
        {
            ClearState();
        }
    }

    private static void DocumentLockModeChanged(
        object? sender,
        DocumentLockModeChangedEventArgs e)
    {
        try
        {
            if (!_isArmed || !ReferenceEquals(e.Document, _armedDocument))
            {
                return;
            }

            var command = e.GlobalCommandName ?? string.Empty;
            Write(
                e.Document,
                $"ROOF_IMPORT_LOCK_OBSERVED command={Token(command)} " +
                $"currentMode={e.CurrentMode} " +
                $"myPreviousMode={e.MyPreviousMode} " +
                $"myCurrentMode={e.MyCurrentMode} action=observe");

            if (!string.Equals(
                    command,
                    _armedGlobalCommandName,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (RoofImportApprovedInsertProofSession.TryConsume(
                    e.Document,
                    command,
                    out var sessionId))
            {
                Write(
                    e.Document,
                    $"ROOF_IMPORT_APPROVED_INSERT session={sessionId:N} " +
                    $"command={Token(command)} action=allow token=consumed");
                return;
            }

            _isArmed = false;
            _armedDocument = null;
            _armedGlobalCommandName = null;
            _pendingVetoDocument = e.Document;
            _pendingVetoCommand = command;

            Write(
                e.Document,
                $"ROOF_IMPORT_LOCK_VETO command={Token(command)} " +
                $"currentMode={e.CurrentMode} " +
                $"myPreviousMode={e.MyPreviousMode} " +
                $"myCurrentMode={e.MyCurrentMode} action=veto");

            e.Veto();
        }
        catch (System.Exception exception)
        {
            RoofImportApprovedInsertProofSession.ClearDocument(
                e.Document,
                "lock-callback-error");
            ClearState();
            Write(
                e.Document,
                $"ROOF_IMPORT_LOCK_VETO_ERROR command={Token(e.GlobalCommandName)} " +
                $"error={Token(exception.GetType().Name)} action=disabled");
        }
    }

    private static void DocumentLockModeChangeVetoed(
        object? sender,
        DocumentLockModeChangeVetoedEventArgs e)
    {
        try
        {
            var command = e.GlobalCommandName ?? string.Empty;
            if (!ReferenceEquals(e.Document, _pendingVetoDocument) ||
                !string.Equals(
                    command,
                    _pendingVetoCommand,
                    StringComparison.Ordinal))
            {
                return;
            }

            Write(
                e.Document,
                $"ROOF_IMPORT_LOCK_VETOED command={Token(command)} action=confirmed");
            _pendingVetoDocument = null;
            _pendingVetoCommand = null;
        }
        catch
        {
            ClearState();
        }
    }

    private static void ClearState()
    {
        _isArmed = false;
        _armedDocument = null;
        _armedGlobalCommandName = null;
        _pendingVetoDocument = null;
        _pendingVetoCommand = null;
    }

    private static void Write(Document? document, string message)
    {
        try
        {
            document?.Editor.WriteMessage("\n" + message);
        }
        catch
        {
            // A diagnostic write must never escape an AutoCAD callback.
        }
    }

    private static string Token(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Trim()
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Replace(' ', '_');
}
#endif
