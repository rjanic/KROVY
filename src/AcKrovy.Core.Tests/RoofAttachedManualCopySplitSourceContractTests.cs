using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Regression coverage for BREAK/TRIM on an AttachedManual Origin.Copy child.
/// The split promotion must keep the modified original fragment Origin.Copy with its
/// exact persisted anchor and a recomputed RelativeSegment, and must write the
/// appended fragment as Origin.Split with the SAME anchor — never the schema-1
/// no-anchor fallback that made both fragments permanently dormant on resize.
/// Existing Generated / Origin.Split branches and the malformed-metadata safety
/// fallback must remain unchanged.
/// </summary>
public sealed class RoofAttachedManualCopySplitSourceContractTests
{
    private static readonly string ManualEdit = Read("RoofGeneratedMemberManualEditService.cs");
    private static readonly string Lifecycle = Read("RoofAttachedManualLifecycleService.cs");
    private static readonly string ChildPolicy = Read("RoofSourceResizeChildPolicyService.cs");

    [Fact]
    public void BreakOfCopySource_IsRoleAware_NotAssumedGenerated()
    {
        // TryAttachManualSplitFragment must detect an AttachedManual Origin.Copy source
        // before resolving its persisted logical anchor through the owner context.
        Assert.Contains("RoofAttachedManualTimberStore.Read(anchorLine).Data is { } attachedSource", ManualEdit);
        Assert.Contains("attachedSource.AnchorGeneratedMemberKey is { } attachedAnchorKey", ManualEdit);
        Assert.Contains("anchorResolutionContext?.Resolve(attachedAnchorKey)", ManualEdit);
        Assert.Contains("? \"AttachedManualCopy\"", ManualEdit);
    }

    [Fact]
    public void BreakOfCopySource_ModifiedOriginal_PreservesCopyOrigin()
    {
        // The modified original fragment (generatedHandle == attachedManualHandle) must
        // keep Origin.Copy with the exact persisted anchor and a fresh RelativeSegment —
        // never downgraded to Split and never the schema-1 fallback.
        var preserveBranch = Segment(
            ManualEdit,
            "if (sourceRole == \"AttachedManualCopy\" &&",
            "else if (resolvedAnchorKey is { } resolvedAnchor)");
        Assert.Contains("generatedHandle,", preserveBranch);
        Assert.Contains("attachedManualHandle,", preserveBranch);
        Assert.Contains("StringComparison.OrdinalIgnoreCase", preserveBranch);
        Assert.Contains("RoofAttachedManualOrigin.Copy);", preserveBranch);
        Assert.Contains("preservedCopyAnchor", preserveBranch);
    }

    [Fact]
    public void BreakOfCopySource_AppendedFragment_BecomesSplitWithSameAnchor()
    {
        // The appended BREAK fragment (generatedHandle != attachedManualHandle) falls to
        // the existing anchored write: Origin.Split with the SAME exact anchor key
        // inherited from the Copy source.
        Assert.Contains("RoofAttachedManualOrigin.Split);", ManualEdit);
        Assert.Contains("resolvedAnchorKey = attachedAnchorKey;", ManualEdit);
        Assert.DoesNotContain("SelectNearestAnchor", ManualEdit);
    }

    [Fact]
    public void BreakOfCopySource_GeneratedAndSplitBranchesRemainUnchanged()
    {
        // Generated remains distinct while both AttachedManual origins share one
        // classification-first branch whose result is independent of anchor resolution.
        Assert.Contains("sourceRole = \"Generated\";", ManualEdit);
        Assert.Contains("sourceRole = attachedSource.Origin", ManualEdit);
        Assert.Contains("? \"AttachedManualCopy\"", ManualEdit);
        Assert.Contains(": \"AttachedManual\"", ManualEdit);
    }

    [Fact]
    public void BreakOfCopySource_UnresolvedValidAnchorFailsBeforeSchemaOneFallback()
    {
        // Legacy schema-1 compatibility remains, but an identified anchored AttachedManual
        // source returns before clear/write when its exact anchor cannot be resolved.
        var unresolved = Segment(
            ManualEdit,
            "if (!anchorResolution.IsResolved)",
            "resolvedAnchorKey = attachedAnchorKey;");
        Assert.Contains("return false;", unresolved);
        Assert.DoesNotContain("WriteAnchored", unresolved);
        Assert.DoesNotContain("RoofGeneratedTimberStore.TryClear", unresolved);
        Assert.Contains("new RoofAttachedManualTimberData(", ManualEdit);
        Assert.Contains("RoofTimberChildRole.AttachedManual);", ManualEdit);
    }

    [Fact]
    public void BreakOfCopySource_EmitsTheAttachedManualSplitDiagnosticFamily()
    {
        // The Copy source routes through the AttachedManual diag family (not
        // ROOF_GENERATED_SPLIT); the diagnostic contract text stays intact.
        Assert.Contains("string.Equals(sourceRole, \"AttachedManualCopy\", StringComparison.OrdinalIgnoreCase)", ManualEdit);
        Assert.Contains("WriteAttachedManualSplit", ManualEdit);
        Assert.Contains("resolution: resolution", ManualEdit);
    }

    [Fact]
    public void CopyOfCopySource_UsesTheSameCopyBranch()
    {
        // A Copy-of-Copy child is also Origin.Copy, so the same role-aware branch
        // applies: origin-agnostic exact-anchor reuse, no Generated assumption.
        Assert.Contains("attachedSource.Origin == RoofAttachedManualOrigin.Copy", ManualEdit);
        Assert.DoesNotContain("RoofGeneratedTimberStore.Write(", ManualEdit);
    }

    [Fact]
    public void TrimIsASplitCommand_AndReachesTheSameRoleAwarePath()
    {
        // TRIM is classified as a split command, so the Copy TRIM case is handled by
        // the very same TryAttachManualSplitFragment branch (modified original only).
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSplitCommand("TRIM"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSplitCommand("BREAK"));
        Assert.Contains("IsSplitCommand(globalCommandName)", ManualEdit);
    }

    [Fact]
    public void ReplayAndResizePolicyRemainUnchanged_ExactAnchorOnly()
    {
        // The replay layer is untouched: exact-anchor replay for both origins,
        // malformed-child dormancy retained, no nearest remap inside the resize replay
        // (MOVE may still re-anchor; that path is unchanged and unrelated).
        var replay = Segment(
            Lifecycle,
            "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner(",
            "public static void RefreshModifiedAttachedManualRelatives(");
        Assert.Contains("TryReplay(", replay);
        Assert.Contains("stored.Data.RelativeSegment", replay);
        Assert.Contains("TryFindGeneratedAnchorLine(", replay);
        Assert.DoesNotContain("SelectNearestAnchor", replay);
        Assert.Contains("MakeCopyChildDormant(document, transaction, childLine)", Lifecycle);
        Assert.Contains("originFilter: RoofAttachedManualOrigin.Copy", ChildPolicy);
        Assert.Contains("originFilter: RoofAttachedManualOrigin.Split", ChildPolicy);
        Assert.DoesNotContain("SelectNearestAnchor", ChildPolicy);
    }

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure", fileName));

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start token '{start}' not found.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End token '{end}' not found after '{start}'.");
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
