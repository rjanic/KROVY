using Autodesk.AutoCAD.ApplicationServices;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Production pre-mutation brake for the HOST-proven native INSERT entries.</summary>
internal static class RoofImportCommandEntryProtection
{
    private static readonly HashSet<string> BlockedCommands = new(StringComparer.Ordinal)
    {
        "-INSERT",
        "INSERT",
        "CLASSICINSERT",
    };

    private static bool _started;

    public static void Start()
    {
        if (_started)
        {
            return;
        }

        AcApplication.DocumentManager.DocumentLockModeChanged += DocumentLockModeChanged;
        AcApplication.DocumentManager.DocumentLockModeChangeVetoed += DocumentLockModeChangeVetoed;
        _started = true;
    }

    public static void Stop()
    {
        if (!_started)
        {
            return;
        }

        AcApplication.DocumentManager.DocumentLockModeChanged -= DocumentLockModeChanged;
        AcApplication.DocumentManager.DocumentLockModeChangeVetoed -= DocumentLockModeChangeVetoed;
        _started = false;
    }

    private static void DocumentLockModeChanged(
        object? sender,
        DocumentLockModeChangedEventArgs e)
    {
        try
        {
            var command = e.GlobalCommandName ?? string.Empty;
            if (e.Document is null || !BlockedCommands.Contains(command))
            {
                return;
            }

#if DEBUG
            Write(e.Document,
                $"ROOF_IMPORT_PRODUCTION_VETO command={Token(command)} " +
                $"currentMode={e.CurrentMode} myPreviousMode={e.MyPreviousMode} " +
                $"myCurrentMode={e.MyCurrentMode} action=veto");
#endif
            e.Veto();
        }
        catch (System.Exception exception)
        {
#if DEBUG
            Write(e.Document,
                $"ROOF_IMPORT_PRODUCTION_VETO_ERROR command={Token(e.GlobalCommandName)} " +
                $"error={Token(exception.GetType().Name)}");
#endif
            _ = exception;
        }
    }

    private static void DocumentLockModeChangeVetoed(
        object? sender,
        DocumentLockModeChangeVetoedEventArgs e)
    {
        try
        {
            var command = e.GlobalCommandName ?? string.Empty;
            if (e.Document is not null && BlockedCommands.Contains(command))
            {
#if DEBUG
                Write(e.Document,
                    $"ROOF_IMPORT_PRODUCTION_VETOED command={Token(command)} action=confirmed");
#endif
            }
        }
        catch
        {
            // Production protection must never propagate from a HOST callback.
        }
    }

    private static void Write(Document? document, string message)
    {
        try
        {
            document?.Editor.WriteMessage("\n" + message);
        }
        catch
        {
            // Diagnostics are best-effort and never alter the veto decision.
        }
    }

    private static string Token(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Replace(' ', '_');
}
