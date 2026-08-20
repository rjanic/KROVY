using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Focused source-contract coverage for the hardened MIRROR No annotation cleanup.
/// AutoCAD MIRROR appends cloned annotations alongside the mirrored Line; each inherits the
/// SOURCE handle and a mirrored rotation residue. The mirror service must erase ONLY those
/// annotation clones APPENDED by this MIRROR command and bound to the source identity —
/// using deterministic command-lifecycle identity, NEVER geometry proximity. A KROVY timber
/// owns multiple legitimate annotations (item label, dimension, slope/auxiliary) and every
/// pre-existing source annotation must survive. Generated -> MIRROR No, Split, COPY
/// footprint containment, T2 and zero-DB U/REDO are unchanged.
/// </summary>
public sealed class RoofMirrorCloneAnnotationSourceContractTests
{
    private const string Infra = "src/AcKrovy.AutoCAD/Infrastructure/";
    private static readonly string Mirror = Read(Infra + "RoofMirrorCloneDetachService.cs");
    private static readonly string Sync = Read(Infra + "LiveGeometrySynchronizationService.cs");
    private static readonly string Labels = Read(Infra + "ElementLabelService.cs");
    private static readonly string Lifecycle = Read(Infra + "RoofAttachedManualLifecycleService.cs");

    [Fact]
    public void MirrorProcess_AcceptsAppendedAnnotationIds()
    {
        // The mirror service receives the union of annotation ids appended by THIS command.
        Assert.Contains("IReadOnlyCollection<ObjectId> appendedAnnotationIds", Mirror);
    }

    [Fact]
    public void AttachedManualClone_DeletesMirroredCloneAnnotations_BeforeCanonicalRefresh()
    {
        // The AttachedManual clone path must remove the appended mirrored annotation clones
        // before the canonical refresh so no mirrored rotation residue is inherited.
        var attachedBranch = Segment(
            Mirror,
            "MIRROR of an existing AttachedManual child",
            "private static bool TrySuppressErasedGeneratedSource");
        Assert.Contains("DeleteMirroredCloneAnnotations", attachedBranch);
        var deleteIndex = attachedBranch.IndexOf("DeleteMirroredCloneAnnotations", StringComparison.Ordinal);
        var refreshIndex = attachedBranch.IndexOf("RefreshClonePresentation", StringComparison.Ordinal);
        Assert.True(deleteIndex >= 0, "DeleteMirroredCloneAnnotations call not found.");
        Assert.True(refreshIndex > deleteIndex, "DeleteMirroredCloneAnnotations must run before RefreshClonePresentation.");
    }

    [Fact]
    public void Cleanup_UsesAppendedIdentity_NotMidpointHeuristic()
    {
        // The cleanup iterates the appended annotation ids and erases only those bound to the
        // source identity via the source-handle resolver. It MUST NOT keep/delete by midpoint
        // distance, bounding-box proximity, nearest entity, or visual location.
        var helper = Segment(Mirror, "private static void DeleteMirroredCloneAnnotations", "private static void RefreshClonePresentation");
        Assert.Contains("appendedAnnotationIds", helper);
        Assert.Contains("RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle", helper);
        Assert.Contains("sourceIdentity", helper);
        Assert.DoesNotContain("sourceMid", helper);
        Assert.DoesNotContain("DistanceTo", helper);
        Assert.DoesNotContain("keepId", helper);
        Assert.DoesNotContain("GetAnnotationMidpoint", helper);
        Assert.DoesNotContain("midpoint", helper);
        Assert.DoesNotContain("GeometricExtents", helper);
        Assert.DoesNotContain("modelSpace", helper);
    }

    [Fact]
    public void Cleanup_TargetsAllAppendedClones_NotOnlyNearest()
    {
        // A timber may own multiple annotations; ALL appended source-identity clones are
        // erased (each mirrored annotation must not become authoritative), never "all but one".
        var helper = Segment(Mirror, "private static void DeleteMirroredCloneAnnotations", "private static void RefreshClonePresentation");
        Assert.Contains("foreach (var id in appendedAnnotationIds)", helper);
        Assert.Contains("writable.Erase()", helper);
    }

    [Fact]
    public void LiveGeometry_PassesUnionOfAppendedAnnotationIds()
    {
        // The command lifecycle collects appended labels + slope arrows + slope angle text and
        // passes their union to the mirror service.
        Assert.Contains("appendedAnnotationIds = appendedLabelIds", Sync);
        Assert.Contains(".Concat(appendedSlopeArrowIds)", Sync);
        Assert.Contains(".Concat(appendedSlopeAngleTextIds)", Sync);
        Assert.Contains("appendedAnnotationIds);", Sync);
    }

    [Fact]
    public void CanonicalRefresh_ReusesExistingAnnotationRefreshPath()
    {
        Assert.Contains("ElementLabelService.UpdateInCurrentTransaction", Mirror);
        Assert.Contains("TimberStandaloneNativeLeaderOrientationRules", Labels);
        Assert.Contains("ResolveTextPresentationRadians", Labels);
    }

    [Fact]
    public void SourceAnnotationPreserved_NoWholesaleDeleteBySourceHandle()
    {
        // Erase source = No: the source's complete annotation set must survive. The fix only
        // removes APPENDED clones — it never deletes annotations wholesale by source handle.
        Assert.DoesNotContain("DeleteForSourceHandle(", Mirror);
    }

    [Fact]
    public void GeneratedMirrorAndSplit_CopyFootprintAndT2_Unchanged()
    {
        // Split and Copy footprint containment remain unchanged in the shared replay path;
        // zero-DB U/REDO guard unchanged. Generated -> MIRROR No path is untouched.
        Assert.Contains("RoofAttachedManualOrigin.Split", Lifecycle);
        Assert.Contains("RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary", Lifecycle);
        Assert.Contains("action=skip-write", Read(Infra + "RoofLiveResizeService.cs"));
    }

    [Fact]
    public void SourceResolver_HandlesMultipleAnnotationTypesPerTimber()
    {
        // A KROVY timber owns multiple annotation entities; the shared source-handle resolver
        // reads main label + slope arrow + slope angle text + post-footprint perpendicular.
        var collector = Read(Infra + "RoofAssemblyGroupMemberCollector.cs");
        Assert.Contains("ElementLabelStore.TryRead", collector);
        Assert.Contains("SlopeArrowStore.TryRead", collector);
        Assert.Contains("SlopeAngleTextStore.TryRead", collector);
        Assert.Contains("PostFootprintPerpendicularAnnotationStore.TryRead", collector);
    }

    [Fact]
    public void NoUnconditionalPiOrMirrorSpecificAngleAlgorithm()
    {
        Assert.DoesNotContain("Math.PI", Mirror);
        Assert.DoesNotContain("MirrorAngle", Mirror);
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
