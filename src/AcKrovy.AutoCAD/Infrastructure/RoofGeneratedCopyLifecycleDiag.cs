#if DEBUG
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG-only COPY lifecycle traces for single generated-rafter detach and post-COPY resize.
/// </summary>
internal static class RoofGeneratedCopyLifecycleDiag
{
    private const string CopyPrefix = "ROOF_COPY_TRACE";
    private const string ResizePrefix = "ROOF_COPY_RESIZE_TRACE";

    public static void WriteAppended(
        Editor? editor,
        string? source,
        string? clone,
        string? owner)
    {
        WriteCopy(editor, "appended", source, clone, owner, claimed: null, extra: null);
    }

    public static void WriteClassify(
        Editor? editor,
        string? clone,
        bool claimed,
        string? generatedOwnerBefore,
        string? keyBefore)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"{CopyPrefix} stage=classify clone={Token(clone)} claimed={claimed.ToString().ToLowerInvariant()}" +
            $" generatedOwnerBefore={Token(generatedOwnerBefore)} keyBefore={Token(keyBefore)}";
        WriteLine(editor, line);
    }

    public static void WriteDetach(
        Editor? editor,
        string? clone,
        string clearResult,
        string? generatedOwnerAfter,
        string? keyAfter)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"{CopyPrefix} stage=detach clone={Token(clone)} clearResult={Token(clearResult)}" +
            $" generatedOwnerAfter={Token(generatedOwnerAfter)} keyAfter={Token(keyAfter)}";
        WriteLine(editor, line);
    }

    public static void WriteProcessSummary(
        Editor? editor,
        string? command,
        int appendedCount,
        int orphanCount,
        bool committed)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"{CopyPrefix} stage=summary command={Token(command)}" +
            $" appendedCount={appendedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" orphanCount={orphanCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" committed={committed.ToString().ToLowerInvariant()}";
        WriteLine(editor, line);
    }

    public static void WriteResizeTrace(
        Editor? editor,
        string? owner,
        int findByOwnerCount,
        int expected,
        bool uniqueStations,
        string duplicateKeys,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"{ResizePrefix} owner={Token(owner)}" +
            $" findByOwnerCount={findByOwnerCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" expected={expected.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" uniqueStations={uniqueStations.ToString().ToLowerInvariant()}" +
            $" duplicateKeys={Token(duplicateKeys)} result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteCopyClassifyStage(
        Editor? editor,
        IReadOnlyCollection<string> preCommandHandles,
        IReadOnlyCollection<string> appendedHandles,
        IReadOnlyCollection<string> claimedHandles,
        IReadOnlyCollection<string> detachHandles)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"{CopyPrefix} stage=classify-summary" +
            $" preCommandCount={preCommandHandles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" appendedCount={appendedHandles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" claimedCount={claimedHandles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" detachCount={detachHandles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" detach={Token(string.Join(",", detachHandles))}";
        WriteLine(editor, line);
    }

    public static void WriteCopyInvariant(
        Editor? editor,
        string owner,
        int generatedExpected,
        int generatedActual,
        bool uniqueStations,
        string attachedManualHandles,
        string missingKeys,
        string duplicateKeys,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_COPY_INVARIANT" +
            $" owner={Token(owner)}" +
            $" generatedExpected={generatedExpected.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" generatedActual={generatedActual.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" uniqueStations={uniqueStations.ToString().ToLowerInvariant()}" +
            $" attachedManual={Token(attachedManualHandles)}" +
            $" missingKeys={Token(missingKeys)}" +
            $" duplicateKeys={Token(duplicateKeys)}" +
            $" result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteRollback(Editor? editor, string? clone, string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"{CopyPrefix} stage=rollback clone={Token(clone)} result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteWholeCopyDetect(
        Editor? editor,
        string? oldOwner,
        string? newOwner,
        int generatedClones,
        int attachedManualClones,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_WHOLE_COPY_DETECT" +
            $" oldOwner={Token(oldOwner)}" +
            $" newOwner={Token(newOwner)}" +
            $" generatedClones={generatedClones.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" attachedManualClones={attachedManualClones.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteWholeCopyRebind(
        Editor? editor,
        string? oldOwner,
        string? newOwner,
        int generatedRebuilt,
        int attachedManualRebound,
        int annotationsRebuilt,
        string? stage,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_WHOLE_COPY_REBIND" +
            $" oldOwner={Token(oldOwner)}" +
            $" newOwner={Token(newOwner)}" +
            $" generatedRebuilt={generatedRebuilt.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" attachedManualRebound={attachedManualRebound.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" annotationsRebuilt={annotationsRebuilt.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" stage={Token(stage)}" +
            $" result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteError(
        Editor? editor,
        string operation,
        System.Exception ex)
    {
        if (editor is null)
        {
            return;
        }

        var message = ex.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (message.Length > 120)
        {
            message = message[..120];
        }

        var line =
            $"{CopyPrefix} stage=error type={Token(ex.GetType().Name)}" +
            $" operation={Token(operation)} message={Token(message)}";
        WriteLine(editor, line);
    }

    public static string DescribeDuplicateStations(IReadOnlyList<RoofGeneratedTimberData> members)
    {
        if (members is null || members.Count == 0)
        {
            return "-";
        }

        var parts = members
            .GroupBy(member => (member.MemberKind, member.RoofFace, member.StationIndex))
            .Where(group => group.Count() > 1)
            .Select(group =>
                $"{group.Key.RoofFace}:s{group.Key.StationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                $"x{group.Count().ToString(System.Globalization.CultureInfo.InvariantCulture)}")
            .ToArray();
        return parts.Length == 0 ? "-" : string.Join("|", parts);
    }

    public static string FormatGeneratedKey(RoofGeneratedTimberData? data) =>
        data is null
            ? "-"
            : $"{data.MemberKind}:{data.RoofFace}:s{data.StationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static void WriteCopy(
        Editor? editor,
        string stage,
        string? source,
        string? clone,
        string? owner,
        bool? claimed,
        string? extra)
    {
        if (editor is null)
        {
            return;
        }

        var line = $"{CopyPrefix} stage={Token(stage)} source={Token(source)} clone={Token(clone)} owner={Token(owner)}";
        if (claimed is not null)
        {
            line += $" claimed={claimed.Value.ToString().ToLowerInvariant()}";
        }

        if (!string.IsNullOrWhiteSpace(extra))
        {
            line += $" {extra}";
        }

        WriteLine(editor, line);
    }

    private static void WriteLine(Editor editor, string line)
    {
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    private static string Token(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return value.Trim()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(' ', '_');
    }
}
#endif
