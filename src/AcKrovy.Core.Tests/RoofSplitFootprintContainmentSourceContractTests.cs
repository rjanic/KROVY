using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Focused source-contract coverage for the PRODUCT RULE:
/// SOURCE ROOF RESIZE spatial validity applies to persistent AttachedManual children of
/// BOTH Origin.Copy and Origin.Split. Exact anchor existence is necessary but not
/// sufficient: after replay, the final child segment must be contained by the current
/// source roof footprint. Outside-footprint children become dormant and reactivate when
/// a later footprint contains their persisted replay geometry. Origin-specific edit,
/// anchor and ERASE semantics remain unchanged.
/// </summary>
public sealed class RoofSplitFootprintContainmentSourceContractTests
{
    private const string Infra = "src/AcKrovy.AutoCAD/Infrastructure/";
    private static readonly string Lifecycle = Read(Infra + "RoofAttachedManualLifecycleService.cs");
    private static readonly string Policy = Read(Infra + "RoofSourceResizeChildPolicyService.cs");
    private static readonly string Containment = Read("src/AcKrovy.Core/Services/Roofs/RoofFootprintContainmentRules.cs");

    private static string Replay => Member(Lifecycle, "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner", "private static void MakeCopyChildDormant");
    private static string Dormancy => Member(Lifecycle, "private static void MakeCopyChildDormant", "public static void RefreshModifiedAttachedManualRelatives");
    private static string MovePath => Member(Lifecycle, "public static void RefreshModifiedAttachedManualRelatives", "private static bool TrySelectNearestCopyAnchor");

    // 1-4: Split surviving anchor + segment inside/on-boundary → replay; partially/fully outside → dormant.
    [Fact]
    public void SplitReplay_ValidatesFinalSegmentAgainstCurrentFootprint()
    {
        // Split replay now supplies the current source footprint, and the shared replay
        // method applies IsSegmentInsideOrOnBoundary to BOTH origins after exact-anchor
        // replay (contained → replay; otherwise → dormant).
        var splitCall = Member(Policy, "originFilter: RoofAttachedManualOrigin.Split", "var (attachedKept, attachedDeleted)");
        Assert.Contains("sourceFootprintVertices:", splitCall);
        Assert.Contains("RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary", Replay);
        Assert.Contains("MakeCopyChildDormant(document, transaction, childLine)", Replay);
    }

    // 2: exactly on boundary is active (inclusive semantics).
    [Fact]
    public void Containment_BoundaryIsInclusive_InsideOrOnBoundary()
    {
        Assert.Contains("IsSegmentInsideOrOnBoundary", Containment);
        Assert.Contains("InsideOrOnBoundary", Replay);
    }

    // 5: near vs far outside behave identically — containment only, no distance/proximity/bbox.
    [Fact]
    public void SplitContainment_IsPureContainment_NoProximityOrBbox()
    {
        Assert.Contains("IsSegmentInsideOrOnBoundary", Replay);
        Assert.DoesNotContain("DistanceTo", Replay);
        Assert.DoesNotContain("GetBoundingBox", Replay);
        Assert.DoesNotContain("GeometricExtents", Replay);
    }

    // 6: missing Split anchor still causes dormancy (origin-agnostic).
    [Fact]
    public void SplitReplay_MissingAnchor_GoesDormant()
    {
        Assert.Contains("anchorLine is null", Replay);
        Assert.Contains("MakeCopyChildDormant(document, transaction, childLine)", Replay);
        Assert.Contains("\"anchor-missing\"", Replay);
    }

    // 7-10: outside-footprint dormancy preserves Origin.Split, ChildIdentity, anchor, RelativeSegment.
    [Fact]
    public void SplitDormancy_PreservesOriginSplitIdentityAnchorRelative()
    {
        // MakeCopyChildDormant only sets Visible=false + removes annotations; it never
        // writes AttachedManual XData or erases, so Origin.Split/ChildIdentity/exact
        // anchor/RelativeSegment all survive for SAVE/REOPEN and U/REDO.
        Assert.Contains("childLine.Visible = false;", Dormancy);
        Assert.Contains("DeleteForSourceHandle", Dormancy);
        Assert.DoesNotContain("RoofAttachedManualTimberStore.Write", Dormancy);
        Assert.DoesNotContain("childLine.Erase()", Dormancy);
        Assert.DoesNotContain("CreateAnchoredData(", Replay);
        Assert.DoesNotContain("RoofAttachedManualTimberStore.Write(", Replay);
    }

    // 11: later expand → reactivated from persisted metadata, no nearest-remap.
    [Fact]
    public void SplitReplay_Reactivation_RestoresSameFragment()
    {
        Assert.Contains("var wasDormant = !childLine.Visible", Replay);
        Assert.Contains("childLine.Visible = true;", Replay);
        Assert.Contains("reactivated++", Replay);
        Assert.Contains("TryReplay(", Replay);
    }

    // 12: multiple Split children sharing one anchor evaluated independently (per-child).
    [Fact]
    public void SplitReplay_EvaluatesChildrenIndependently_NoAnchorWideDormancy()
    {
        Assert.Contains("foreach (var attachedId in", Replay);
        Assert.Contains("MakeCopyChildDormant(document, transaction, childLine)", Replay);
        Assert.DoesNotContain("GetAllEntityIds", Replay);
    }

    // 13: Split MOVE does NOT nearest-reanchor (only Origin.Copy may).
    [Fact]
    public void SplitMove_DoesNotNearestReanchor()
    {
        Assert.Contains("reanchor &&", MovePath);
        Assert.Contains("Origin == RoofAttachedManualOrigin.Copy", MovePath);
    }

    // 14-16: Split ROTATE/EXTEND/GRIP paths carry no source-resize containment (HOST PASS,
    // edit-time commands unaffected; containment runs only during source resize replay).
    [Fact]
    public void SplitEditCommands_HaveNoContainmentAtEditTime()
    {
        Assert.DoesNotContain("IsSegmentInsideOrOnBoundary", MovePath);
    }

    // 17: Split ERASE permanent-delete unchanged.
    [Fact]
    public void SplitErase_PermanentDeleteUnchanged()
    {
        var manual = Read(Infra + "RoofGeneratedMemberManualEditService.cs");
        var diag = Read(Infra + "RoofGeneratedMemberManualEditDiag.cs");
        Assert.Contains("split-delete", manual);
        Assert.Contains("WriteAttachedManualErase", manual);
        Assert.Contains("Origin != RoofAttachedManualOrigin.Copy &&", manual);
        Assert.Contains("Origin != RoofAttachedManualOrigin.Split", manual);
        Assert.Contains("ROOF_ATTACHED_MANUAL_ERASE", diag);
        Assert.Contains("action=permanent-delete", diag);
    }

    // 18-19: BREAK/repeated BREAK/split-TRIM remain role-aware Origin.Split.
    [Fact]
    public void SplitBreakTrim_RoleAwareUnchanged()
    {
        var manual = Read(Infra + "RoofGeneratedMemberManualEditService.cs");
        Assert.Contains("Origin: RoofAttachedManualOrigin.Split", manual);
        Assert.Contains("sourceRole = \"AttachedManual\"", manual);
    }

    // 20: Origin.Copy containment unchanged (Copy still reports outside-footprint dormancy).
    [Fact]
    public void CopyContainment_Unchanged()
    {
        var copyCall = Member(Policy, "originFilter: RoofAttachedManualOrigin.Copy", "var splitReplay");
        Assert.Contains("sourceFootprintVertices:", copyCall);
        Assert.Contains("AttachedManualCopyDormantOutsideFootprint", Policy);
        Assert.Contains("attachedManualCopyDormantOutsideFootprint", Policy);
    }

    // 21: GROUP untouched — footprint is the only spatial authority, never Group extents.
    [Fact]
    public void Containment_GroupIrrelevant()
    {
        Assert.DoesNotContain("Group", Replay);
        Assert.DoesNotContain("Selectable", Replay);
    }

    // 22: annotation dormancy removed on dormancy, ensured once on reactivation.
    [Fact]
    public void Annotation_DormancyRemoves_ReactivationEnsuresOnce()
    {
        Assert.Contains("TimberAnnotationService.DeleteForSourceHandle", Dormancy);
        Assert.Contains("TimberAnnotationService.EnsureForElement", Replay);
    }

    // 23: polygon/concave-safe containment via RoofFootprintContainmentRules + tolerance.
    [Fact]
    public void Containment_PolygonSafeWithTolerance()
    {
        Assert.Contains("ContainmentToleranceMm", Containment);
        Assert.Contains("IsPointInsideOrOnBoundary", Containment);
        Assert.Contains("IsSegmentInsideOrOnBoundary", Containment);
    }

    // 25: zero-DB U/REDO guard unchanged.
    [Fact]
    public void ZeroDbUndoRedoGuard_Unchanged()
    {
        var resize = Read(Infra + "RoofLiveResizeService.cs");
        Assert.Contains("RoofUndoGuardDiag.Write", resize);
        Assert.Contains("action=skip-write", resize);
    }

    // 26: Generated ReservedElementId sync unchanged.
    [Fact]
    public void GeneratedIdentitySync_Unchanged()
    {
        Assert.Contains("SyncReservedElementIdsAfterRecalc", Read(Infra + "RoofGeneratedMemberManualEditService.cs"));
    }

    // 27: MIRROR lifecycle unchanged (role-sensitive Split→Copy clone).
    [Fact]
    public void MirrorLifecycle_Unchanged()
    {
        Assert.Contains("Origin != RoofAttachedManualOrigin.Copy &&", Read(Infra + "RoofMirrorCloneDetachService.cs"));
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));

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

    private static string Member(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start token '{start}' not found.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End token '{end}' not found after '{start}'.");
        return source.Substring(startIndex, endIndex - startIndex);
    }
}
