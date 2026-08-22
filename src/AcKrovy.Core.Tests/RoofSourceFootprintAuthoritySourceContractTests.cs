using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Locks in the PRODUCT RULE that the roof source closed Polyline is the ONLY
/// authoritative geometric boundary of the roof. GROUP is organizational only;
/// annotation/timber/display/group extents must never substitute for footprint
/// containment or Copy/Split dormancy decisions. These are source-contract tests:
/// they read the AutoCAD infrastructure sources and assert the boundary decision
/// path has no group/annotation/extents dependency.
/// </summary>
public sealed class RoofSourceFootprintAuthoritySourceContractTests
{
    private const string Infra = "src/AcKrovy.AutoCAD/Infrastructure/";

    [Fact]
    public void NoRoofInfrastructureFile_UsesAutoCadGeometricExtentsForBoundary()
    {
        // Any boundary decision driven by AutoCAD GeometricExtents / GetBoundingBox /
        // Extents3d would violate "source Polyline is the only authority". No Roof*
        // infrastructure file may use these APIs.
        foreach (var file in RoofInfrastructureFiles())
        {
            var source = Read(Infra + file);
            Assert.DoesNotContain("GeometricExtents", source);
            Assert.DoesNotContain("GetBoundingBox", source);
            Assert.DoesNotContain("Extents3d", source);
        }
    }

    [Fact]
    public void ContainmentRules_ArePurePolygon_NoGroupOrAnnotationDependency()
    {
        // The inside/outside authority is a closed-polygon test (even-odd ray cast +
        // segment crossing). It has no Group, annotation, or extents input.
        var source = Read("src/AcKrovy.Core/Services/Roofs/RoofFootprintContainmentRules.cs");
        Assert.Contains("IsPointInsideOrOnBoundary", source);
        Assert.Contains("IsSegmentInsideOrOnBoundary", source);
        Assert.DoesNotContain("GeometricExtents", source);
        Assert.DoesNotContain("GetBoundingBox", source);
        Assert.DoesNotContain("Group", source);
        Assert.DoesNotContain("Annotation", source);
    }

    [Fact]
    public void KeepDeletePolicy_DerivesBoundaryFromSourcePolylineOnly()
    {
        // The legacy keep/delete rule extracts the footprint from the source Polyline
        // and evaluates containment against its FINAL footprint vertices — never group
        // or annotation extents.
        var policy = Read(Infra + "RoofSourceResizeChildPolicyService.cs");
        var method = Member(
            policy,
            "private static (int Kept, int Deleted) ApplyAttachedManualResizePolicy",
            "private static int CountAnnotationsForHandle");
        Assert.Contains("RoofPolylineExtractor.Extract(owner)", method);
        Assert.Contains("RoofFootprintValidator.Validate(input)", method);
        Assert.Contains("RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary", method);
        Assert.Contains("finalFootprintVertices", method);
        Assert.DoesNotContain("GeometricExtents", method);
        Assert.DoesNotContain("GetBoundingBox", method);
    }

    [Fact]
    public void CopySplitDormancy_IsExactAnchorKey_AndSourceFootprint_NoGroupOrAnnotationExtents()
    {
        // Origin.Copy dormancy depends on (a) whether the exact persisted Generated
        // anchor key resolves AND (b) whether the final replayed segment lies within the
        // source footprint. There is no "near roof" proximity tolerance, no
        // group-bounding-box enclosure check, and no annotation-overlap check. Group /
        // annotation / geometric extents never participate.
        var lifecycle = Read(Infra + "RoofAttachedManualLifecycleService.cs");
        var replay = Member(
            lifecycle,
            "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner",
            "private static void MakeCopyChildDormant");
        Assert.Contains("TryFindGeneratedAnchorLine", replay);
        Assert.Contains("if (!anchorResolution.IsResolved)", replay);
        Assert.Contains("MakeCopyChildDormant(document, transaction, childLine)", replay);
        // Source footprint containment for Origin.Copy replay (the only spatial authority).
        Assert.Contains("RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary", replay);
        Assert.DoesNotContain("GeometricExtents", replay);
        Assert.DoesNotContain("GetBoundingBox", replay);
    }

    [Fact]
    public void SourceResizeClassification_IsPolylinePure()
    {
        // SupportedResize / RigidEquivalent / Unsupported is classified purely from the
        // source Polyline's extracted footprint vs the persisted descriptor — never from
        // group/annotation/selection extents.
        var resize = Read(Infra + "RoofLiveResizeService.cs");
        var method = Member(
            resize,
            "private static RoofSourceChangeClassification ClassifyOwner",
            "private static bool TryInvokeUndoMark");
        Assert.Contains("RoofPolylineExtractor.Extract(polyline)", method);
        Assert.Contains("RoofFootprintValidator.Validate(input)", method);
        Assert.Contains("RoofDefinitionPersistence.Classify", method);
        Assert.DoesNotContain("GeometricExtents", method);
        Assert.DoesNotContain("GetBoundingBox", method);
    }

    [Fact]
    public void GroupIsOrganizationalMembershipOnly()
    {
        // GROUP membership and selectability are the only group concerns. The group
        // service reads member ObjectIds and the Selectable flag; it never reads group
        // geometric extents as a roof boundary.
        var group = Read(Infra + "RoofDisplayGroupService.cs");
        var collector = Read(Infra + "RoofAssemblyGroupMemberCollector.cs");
        Assert.Contains("GetAllEntityIds()", group);
        Assert.Contains("Selectable", group);
        Assert.DoesNotContain("GeometricExtents", group);
        Assert.DoesNotContain("GetBoundingBox", group);
        Assert.DoesNotContain("GeometricExtents", collector);
        Assert.DoesNotContain("GetBoundingBox", collector);
    }

    private static IEnumerable<string> RoofInfrastructureFiles()
    {
        var directory = Path.Combine(RepositoryRoot(), Infra);
        return Directory
            .EnumerateFiles(directory, "Roof*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)!;
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
