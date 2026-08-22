using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofSplitAttachedManualSourceContractTests
{
    private static readonly string ManualEdit = Read("RoofGeneratedMemberManualEditService.cs");
    private static readonly string AttachedStore = Read("RoofAttachedManualTimberStore.cs");
    private static readonly string Lifecycle = Read("RoofAttachedManualLifecycleService.cs");
    private static readonly string ChildPolicy = Read("RoofSourceResizeChildPolicyService.cs");
    private static readonly string Diag = Read("RoofGeneratedMemberManualEditDiag.cs");

    [Fact]
    public void SplitFragments_WriteAttachedManual_NotStandaloneDetach()
    {
        Assert.Contains("TryAttachManualSplitFragment", ManualEdit);
        Assert.Contains("RoofAttachedManualLifecycleService.CreateAnchoredData", ManualEdit);
        Assert.Contains("RefreshModifiedAttachedManualRelatives", Read("RoofGeneratedMemberManualEditService.cs"));
        Assert.DoesNotContain("TryDetachStandaloneLine", ManualEdit);
    }

    [Fact]
    public void ModifiedAttachedManual_RefreshesNumberingAndAnnotations()
    {
        Assert.Contains("RefreshModifiedAttachedManualNumberingAndAnnotations", ManualEdit);
        Assert.Contains("ElementLabelService.UpdateInCurrentTransaction", ManualEdit);
    }

    [Fact]
    public void SplitPromotion_ClearsGeneratedKeyBeforeAttachedManualWrite()
    {
        var method = Member(ManualEdit, "private static bool TryAttachManualSplitFragment", "private static bool TryOpenSnapshotLine");
        Assert.Contains("RoofGeneratedTimberStore.TryClear", method);
        Assert.Contains("RoofTimberChildRole.AttachedManual", method);
    }

    [Fact]
    public void AttachedManualRegApp_UnchangedForSplit()
    {
        Assert.Contains("DECORAIR_ACADKROVY_ROOF_ATTACHED_MANUAL", AttachedStore);
        Assert.Contains("CurrentVersion = 3", File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.Core",
            "Models",
            "Roofs",
            "RoofAttachedManualTimberDataSchema.cs")));
    }

    [Fact]
    public void SplitResize_ReplaysInPlace_PreservingOriginChildIdentityAnchor()
    {
        // ReplayAnchoredChildrenForOwner recomputes WCS from the persisted RelativeSegment
        // against the exact rebuilt anchor; it must NOT call CreateAnchoredData or Write,
        // so Origin.Split, ChildIdentity, anchor key and RelativeSegment are all preserved.
        var replay = Member(
            Lifecycle,
            "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner",
            "private static void MakeCopyChildDormant");
        Assert.Contains("TryReplay(", replay);
        Assert.Contains("stored.Data.RelativeSegment", replay);
        Assert.Contains("stored.Data.AnchorGeneratedMemberKey", replay);
        Assert.DoesNotContain("CreateAnchoredData(", replay);
        Assert.DoesNotContain("RoofAttachedManualTimberStore.Write(", replay);
    }

    [Fact]
    public void SplitResize_UsesExactPersistedAnchor_NotNearestStationRemap()
    {
        // Replay resolves the EXACT persisted anchor key via TryFindGeneratedAnchorLine.
        Assert.Contains("TryFindGeneratedAnchorLine", Lifecycle);
        Assert.DoesNotContain("SelectNearestAnchor", ChildPolicy);
    }

    [Fact]
    public void SplitResize_Dormancy_HidesAndRemovesAnnotations_RetainsMetadata()
    {
        // Dormant Split (anchor temporarily missing): Visible=false + annotations removed;
        // the entity is NOT erased and its XData (Origin.Split, ChildIdentity, anchor,
        // RelativeSegment) survives SAVE/REOPEN and U/REDO.
        var dormant = Member(
            Lifecycle,
            "private static void MakeCopyChildDormant",
            "public static void RefreshModifiedAttachedManualRelatives");
        Assert.Contains("childLine.Visible = false;", dormant);
        Assert.Contains("TimberAnnotationService.DeleteForSourceHandle", dormant);
        Assert.DoesNotContain("childLine.Erase()", dormant);
        Assert.DoesNotContain("TryClear", dormant);
    }

    [Fact]
    public void SplitResize_IsRoutedThroughAnchoredPolicy_NotLegacyPermanentDelete()
    {
        // Split fragments are anchor-replayed (and dormancy-handled) on SupportedResize,
        // and are excluded from the keep-in-place/delete-outside legacy deletion rule.
        Assert.Contains("originFilter: RoofAttachedManualOrigin.Split", ChildPolicy);
        Assert.Contains("origin == RoofAttachedManualOrigin.Split", ChildPolicy);
    }

    [Fact]
    public void SplitResize_ExactAnchorReturns_ReactivatesSameFragment()
    {
        // When the exact anchor returns, ReplayAnchoredChildrenForOwner flips Visible back
        // true and replays; the reactivation counter is reported separately.
        Assert.Contains("childLine.Visible = true;", Lifecycle);
        Assert.Contains("reactivated++", Lifecycle);
        Assert.Contains("attachedManualSplitReactivated", ChildPolicy);
    }

    [Fact]
    public void SplitResize_ExactAnchorMissing_DormantNotDeleted_OnTemporaryShrink()
    {
        // Shrink where the exact anchor station disappears: Split becomes dormant (never
        // permanently deleted); expansion must reactivate it.
        Assert.Contains("MakeCopyChildDormant(document, transaction, childLine)", Lifecycle);
        Assert.Contains("anchor-missing", Lifecycle);
        Assert.Contains("attachedManualSplitDormant", ChildPolicy);
    }

    [Fact]
    public void BreakOfSplitSource_IsRoleAware_NotAssumedGenerated()
    {
        // TryAttachManualSplitFragment must detect that the source is an AttachedManual
        // Origin.Split child (not Generated) and NOT relabel it as a Generated fragment.
        Assert.Contains("RoofAttachedManualTimberStore.Read(anchorLine).Data is", ManualEdit);
        Assert.Contains("sourceRole = attachedSource.Origin", ManualEdit);
        Assert.Contains(": \"AttachedManual\"", ManualEdit);
    }

    [Fact]
    public void BreakOfSplitSource_ReusesExactSourceAnchor_NotNearestReanchor()
    {
        // BREAK is not MOVE: both fragments keep the source Split's EXACT persisted anchor
        // key through physical-first/logical-suppressed resolution; no nearest remap.
        Assert.Contains("attachedSource.AnchorGeneratedMemberKey is { } attachedAnchorKey", ManualEdit);
        Assert.Contains("anchorResolutionContext?.Resolve(attachedAnchorKey)", ManualEdit);
        Assert.DoesNotContain("TryFindGeneratedAnchorLine", Member(
            ManualEdit,
            "private static bool TryAttachManualSplitFragment",
            "private static bool TryOpenSnapshotLine"));
        Assert.DoesNotContain("SelectNearestAnchor", ManualEdit);
    }

    [Fact]
    public void BreakOfSplitSource_BothFragmentsStayOriginSplit_IndependentChildIdentity()
    {
        // Source A keeps Origin.Split and its own handle/ChildIdentity; the appended
        // fragment B gets Origin.Split with its own ChildIdentity (attachedManualHandle),
        // each with an independently captured RelativeSegment.
        Assert.Contains("RoofAttachedManualOrigin.Split);", ManualEdit);
        Assert.Contains("attachedManualHandle", ManualEdit);
    }

    [Fact]
    public void BreakOfSplitSource_GeneratedBranch_Unchanged()
    {
        // BREAK of a Generated member still resolves the surviving Generated fragment as
        // the anchor (existing HOST PASS path).
        Assert.Contains("RoofGeneratedTimberStore.Read(anchorLine).Data", ManualEdit);
        Assert.Contains("sourceRole = \"Generated\"", ManualEdit);
    }

    [Fact]
    public void BreakOfSplitSource_EmitsAttachedManualSplitDiagnostic_NotGenerated()
    {
        Assert.Contains("WriteAttachedManualSplit", ManualEdit);
        Assert.Contains("ROOF_ATTACHED_MANUAL_SPLIT", Diag);
        Assert.Contains("sourceRole=AttachedManual", Diag);
        Assert.Contains("origin={Token(origin)}", Diag);
        Assert.Contains("anchor=", Diag);
        Assert.Contains("resolution={Token(resolution)}", Diag);
    }

    [Fact]
    public void BreakOfSplitSource_NeverCreatesGeneratedMember()
    {
        // Neither resulting fragment enters the Generated recipe; no new Generated key.
        Assert.DoesNotContain("RoofGeneratedTimberStore.Write(", ManualEdit);
    }

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure", fileName));

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

    private static string Member(string source, string start, string end) => Segment(source, start, end);

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start token '{start}' not found.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End token '{end}' not found after '{start}'.");
        return source.Substring(startIndex, endIndex - startIndex);
    }
}
