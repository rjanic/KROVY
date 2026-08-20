using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Focused source-contract coverage for the PRODUCT RULE:
/// AttachedManual Origin.Copy source-resize replay requires BOTH an exact persisted
/// anchor AND final replayed geometry contained by the current source roof footprint.
/// Anchor existence alone is not sufficient. The source closed Polyline is the only
/// spatial authority; Group and annotation extents never participate. Split is out of
/// scope (HOST PASS). MOVE re-anchor is unchanged.
/// </summary>
public sealed class RoofCopyFootprintContainmentSourceContractTests
{
    private const string Infra = "src/AcKrovy.AutoCAD/Infrastructure/";
    private static readonly string Lifecycle = Read(Infra + "RoofAttachedManualLifecycleService.cs");
    private static readonly string Policy = Read(Infra + "RoofSourceResizeChildPolicyService.cs");
    private static readonly string Resize = Read(Infra + "RoofLiveResizeService.cs");

    [Fact]
    public void Replay_AcceptsSourceFootprintVertices()
    {
        // ReplayAnchoredChildrenForOwner takes the current source footprint vertices so
        // it can validate the replayed segment. Signature includes the parameter.
        Assert.Contains("IReadOnlyList<RoofPoint2D>? sourceFootprintVertices", Lifecycle);
    }

    [Fact]
    public void Replay_OutsideFootprint_MakesCopyDormant_WithReason()
    {
        // After a successful exact-anchor replay, a Copy whose final replayed segment is
        // outside the source footprint goes dormant through the SAME mechanism as a
        // missing anchor, and increments the outside-footprint counter.
        var replay = Member(Lifecycle, "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner", "private static void MakeCopyChildDormant");
        Assert.Contains("RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary", replay);
        Assert.Contains("MakeCopyChildDormant(document, transaction, childLine)", replay);
        Assert.Contains("dormancyOutsideFootprint++;", replay);
        Assert.Contains("\"outside-footprint\"", replay);
    }

    [Fact]
    public void Replay_ContainmentAppliesToBothCopyAndSplit()
    {
        // The footprint containment check is NOT gated on Origin.Copy alone: it applies
        // to any persistent AttachedManual child (Copy OR Split) once the current source
        // footprint vertices are supplied. There is no Origin-only exclusion.
        var replay = Member(Lifecycle, "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner", "private static void MakeCopyChildDormant");
        Assert.Contains("RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary", replay);
        Assert.DoesNotContain("stored.Data.Origin == RoofAttachedManualOrigin.Copy &&", replay);
    }

    [Fact]
    public void Replay_AnchorMustResolveBeforeContainment()
    {
        // Decision order: exact anchor must resolve first; containment runs only after a
        // successful replay against that anchor (anchor-missing uses the existing dormancy
        // path, independent of containment).
        var replay = Member(Lifecycle, "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner", "private static void MakeCopyChildDormant");
        var anchorMissing = replay.IndexOf("anchorLine is null", StringComparison.Ordinal);
        var containment = replay.IndexOf("RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary", StringComparison.Ordinal);
        Assert.True(anchorMissing >= 0, "anchor-missing path not found.");
        Assert.True(containment > anchorMissing, "containment must run after anchor resolution.");
    }

    [Fact]
    public void Replay_NoNearestReanchorDuringResize()
    {
        // Source resize replay must never nearest-reanchor. The replay method only
        // resolves the EXACT persisted anchor (TryFindGeneratedAnchorLine).
        var replay = Member(Lifecycle, "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner", "private static void MakeCopyChildDormant");
        Assert.Contains("TryFindGeneratedAnchorLine", replay);
        Assert.DoesNotContain("SelectNearestAnchor", replay);
    }

    [Fact]
    public void ReplayResult_ReportsOutsideFootprintDormancy()
    {
        Assert.Contains("int DormantOutsideFootprint", Lifecycle);
    }

    [Fact]
    public void Policy_ExtractsSourceFootprintFromOwnerPolyline()
    {
        // The current source footprint passed to Copy replay comes from the owner
        // Polyline via RoofPolylineExtractor + RoofFootprintValidator — the only spatial
        // authority. No Group/annotation extents.
        Assert.Contains("sourceFootprintVertices", Policy);
        Assert.Contains("RoofPolylineExtractor.Extract(owner)", Policy);
        Assert.Contains("RoofFootprintValidator.Validate(footprintInput)", Policy);
        Assert.Contains("sourceFootprintVertices: sourceFootprintVertices", Policy);
    }

    [Fact]
    public void Policy_AppliesFootprintToCopyAndSplitReplay()
    {
        // The footprint is passed to BOTH the Origin.Copy and the Origin.Split replay
        // call, so source-resize spatial containment applies to both persistent origins.
        var copyCall = Member(Policy, "originFilter: RoofAttachedManualOrigin.Copy", "var splitReplay");
        var splitCall = Member(Policy, "originFilter: RoofAttachedManualOrigin.Split", "var (attachedKept, attachedDeleted)");
        Assert.Contains("sourceFootprintVertices:", copyCall);
        Assert.Contains("sourceFootprintVertices:", splitCall);
    }

    [Fact]
    public void PolicyResultAndDiag_ReportOutsideFootprintDormancy()
    {
        Assert.Contains("AttachedManualCopyDormantOutsideFootprint", Policy);
        Assert.Contains("attachedManualCopyDormantOutsideFootprint", Policy);
    }

    [Fact]
    public void MoveReanchor_RemainsUnchanged()
    {
        // Explicit MOVE may still select the nearest compatible Generated anchor via
        // SelectNearestAnchor in the MOVE re-anchor path. Source resize replay does not.
        var movePath = Member(
            Lifecycle,
            "public static void RefreshModifiedAttachedManualRelatives",
            "private static bool TrySelectNearestCopyAnchor");
        Assert.Contains("reanchor &&", movePath);
        Assert.Contains("Origin == RoofAttachedManualOrigin.Copy", movePath);
    }

    [Fact]
    public void GeneratedIdentitySync_RemainsUntouched()
    {
        // Generated ReservedElementId / identity synchronization must stay intact.
        Assert.Contains("SyncReservedElementIdsAfterRecalc", Read(Infra + "RoofGeneratedMemberManualEditService.cs"));
    }

    [Fact]
    public void ZeroDbUndoRedoGuard_RemainsUntouched()
    {
        Assert.Contains("RoofUndoGuardDiag.Write", Resize);
        Assert.Contains("action=skip-write", Resize);
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
