#if DEBUG
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG-only Unsupported STRETCH recovery fallback telemetry.
/// Emits at most one primary fallback line per failed owner/attempt plus an optional
/// compact probe summary. No DWG writes.
/// </summary>
internal static class RoofUnsupportedStretchRecoveryDiag
{
    private const string FallbackPrefix = "ROOF_UNSUPPORTED_STRETCH_RECOVERY_FALLBACK";
    private const string ProbePrefix = "ROOF_UNSUPPORTED_STRETCH_RECOVERY_PROBE";
    private const string MLeaderWriteFailPrefix =
        "ROOF_UNSUPPORTED_STRETCH_RECOVERY_MLEADER_WRITE_FAIL";

    public static void WriteFallback(
        Editor? editor,
        string stage,
        string reason,
        string? owner = null,
        string? handle = null,
        string? kind = null,
        string? detail = null)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"{FallbackPrefix} stage={stage} reason={reason}" +
            $" owner={Token(owner)} handle={Token(handle)} kind={Token(kind)}";
        if (!string.IsNullOrWhiteSpace(detail))
        {
            line += $" detail={Sanitize(detail!)}";
        }

        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
            // Diagnostics must never affect recovery.
        }
    }

    public static void WriteProbe(
        Editor? editor,
        string? owner,
        int roof,
        int timber,
        int annotations,
        string result,
        string? kindCounts = null)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"{ProbePrefix} owner={Token(owner)} roof={roof} timber={timber}" +
            $" annotations={annotations} result={result}";
        if (!string.IsNullOrWhiteSpace(kindCounts))
        {
            line += $" kinds={Sanitize(kindCounts!)}";
        }

        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    public static void WriteMLeaderWriteFail(
        Editor? editor,
        string? handle,
        string step,
        int leaderIndex,
        int lineIndex,
        Autodesk.AutoCAD.Runtime.Exception exception)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"{MLeaderWriteFailPrefix} handle={Token(handle)} step={Sanitize(step)}" +
            $" leader={leaderIndex} line={lineIndex}" +
            $" exception={exception.GetType().Name} status={exception.ErrorStatus}";

        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    public static void WriteMLeaderTopology(
        Editor? editor,
        string? handle,
        string snapshotSummary,
        string liveSummary)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_UNSUPPORTED_STRETCH_RECOVERY_MLEADER_TOPOLOGY" +
            $" handle={Token(handle)}" +
            $" snapshot={Sanitize(snapshotSummary)}" +
            $" live={Sanitize(liveSummary)}";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    public static void WriteFallbackFromDocument(
        Document? document,
        string stage,
        string reason,
        string? owner = null,
        string? handle = null,
        string? kind = null,
        string? detail = null) =>
        WriteFallback(document?.Editor, stage, reason, owner, handle, kind, detail);

    private static string Token(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : Sanitize(value!);

    private static string Sanitize(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return "-";
        }

        return trimmed
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(' ', '_');
    }
}
#endif
