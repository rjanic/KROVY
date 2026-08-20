using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Focused source-contract coverage for the MIRROR No role-transition bug: an
/// AttachedManual Origin.Split source must be preserved as Split while its newly mirrored
/// clone is re-initialized as an independent AttachedManual Origin.Copy child (never a
/// stale Split clone that is annotation-less and un-erasable). Every appended clone is
/// classified from its OWN inherited metadata — no batch-level role, no length/order/position
/// inference. Generated -> MIRROR No and Origin.Copy -> MIRROR No (incl. the deterministic
/// appended-annotation cleanup) are unchanged.
/// </summary>
public sealed class RoofMirrorSplitCloneSourceContractTests
{
    private const string Infra = "src/AcKrovy.AutoCAD/Infrastructure/";
    private static readonly string Mirror = Read(Infra + "RoofMirrorCloneDetachService.cs");
    private static readonly string Lifecycle = Read(Infra + "RoofAttachedManualLifecycleService.cs");

    [Fact]
    public void AttachedManualBranch_RecognizesOriginSplit_NotOnlyCopy()
    {
        // The clone re-initialization guard must accept BOTH Origin.Copy and Origin.Split.
        // A Split clone must not be skipped (which left it malformed/unclassified). The
        // Copy-only exclusion check is scoped to the appended-clone branch: the separate
        // in-place MIRROR Yes branch (Origin.Copy only) uses the same substring for a
        // different, correct guard.
        var cloneLoop = Segment(Mirror, "foreach (var id in appendedTimberIds)", "// MIRROR Yes (Generated)");
        Assert.DoesNotContain("attached.Data.Origin != RoofAttachedManualOrigin.Copy", cloneLoop);
        Assert.Contains("sourceOrigin != RoofAttachedManualOrigin.Copy &&", cloneLoop);
        Assert.Contains("sourceOrigin != RoofAttachedManualOrigin.Split", cloneLoop);
        Assert.Contains("isSplitSource", cloneLoop);
    }

    [Fact]
    public void SplitClone_ReinitializedAsOriginCopy()
    {
        // The promotion path writes the clone as Origin.Copy with the clone handle as its
        // ChildIdentity — never Origin.Split, never the source ChildIdentity. Scope to the
        // shared promote helper only (the in-place handler's guard references Split for a
        // different, correct classification).
        var promote = Segment(Mirror, "private static bool TryPromoteFromMirroredGeometry", "private static bool TryReanchorInPlaceAttachedManual");
        Assert.Contains("RoofAttachedManualOrigin.Copy", promote);
        Assert.Contains("cloneLine.Handle.ToString()", promote);
        Assert.DoesNotContain("RoofAttachedManualOrigin.Split)", promote);
    }

    [Fact]
    public void SplitClone_UsesProvenMirrorAnchorRule()
    {
        // Compatible Generated anchor is chosen from the FINAL mirrored WCS geometry via the
        // existing proven MIRROR anchor-selection rule.
        Assert.Contains("SelectNearestMirrorAnchor", Mirror);
    }

    [Fact]
    public void Clone_HasNoGeneratedMetadata()
    {
        // A mirrored AttachedManual clone carries no Generated metadata (only Generated
        // sources go through TryDetachAndPromote / TryClear).
        Assert.Contains("RoofGeneratedTimberStore.Read(cloneLine).Data", Mirror);
        Assert.Contains("RoofAttachedManualTimberStore.Read(cloneLine)", Mirror);
    }

    [Fact]
    public void PerCloneRole_FromOwnInheritedMetadata_NoBatchRole()
    {
        // Each appended clone is classified independently from its own inherited XData:
        // Generated XData -> Generated branch; AttachedManual XData -> AttachedManual branch.
        // No batch-level role, no length/position/order inference.
        var cloneLoop = Segment(Mirror, "foreach (var id in appendedTimberIds)", "// MIRROR Yes (Generated)");
        Assert.Contains("RoofGeneratedTimberStore.Read(cloneLine)", cloneLoop);
        Assert.Contains("RoofAttachedManualTimberStore.Read(cloneLine)", cloneLoop);
        Assert.DoesNotContain("Length", cloneLoop);
    }

    [Fact]
    public void SplitClone_DiagnosticExists()
    {
        Assert.Contains("ROOF_MIRROR_SPLIT_CLONE", Mirror);
        Assert.Contains("sourceOrigin=Split", Mirror);
        Assert.Contains("originAfter=", Mirror);
    }

    [Fact]
    public void DeterministicAppendedAnnotationCleanup_Unchanged()
    {
        // The hardened deterministic appended-annotation cleanup is still invoked for the
        // AttachedManual clone path (Split-derived clones now share it).
        Assert.Contains("DeleteMirroredCloneAnnotations", Mirror);
        Assert.Contains("appendedAnnotationIds", Mirror);
        Assert.Contains("RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle", Mirror);
    }

    [Fact]
    public void SourcePreservation_CloneOnly_NoSourceMutation()
    {
        // The service re-initializes only the appended cloneLine; the surviving source Split
        // child is never written. CreateAnchoredData is only applied to the clone.
        var promote = Segment(Mirror, "private static bool TryPromoteFromMirroredGeometry", "private static bool TryParseHandle");
        Assert.Contains("WriteAnchored(cloneLine, transaction, attachedData)", promote);
    }

    [Fact]
    public void SplitAndCopyLifecycle_Unchanged()
    {
        // Split dormancy/reactivation and Copy footprint containment remain intact; zero-DB
        // U/REDO guard unchanged.
        Assert.Contains("RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary", Lifecycle);
        Assert.Contains("MakeCopyChildDormant", Lifecycle);
        Assert.Contains("action=skip-write", Read(Infra + "RoofLiveResizeService.cs"));
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

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start token '{start}' not found.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End token '{end}' not found after '{start}'.");
        return source.Substring(startIndex, endIndex - startIndex);
    }
}
