using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Adapter source guards; real Solid3d and lifecycle need HOST proof.</summary>
public sealed class RoofStructuralRafterSolidSourceContractTests
{
    private static string Read(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName,
            "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }

    [Fact]
    public void StructuralSolid_HasOwnRole_ButSharesExistingPhysicalOwnerAndGroup()
    {
        var physical = Read("RoofPhysical3DMaterializationService.cs");
        var lifecycle = Read("RoofPhysical3DLifecycleService.cs");
        var group = Read("RoofAssemblyGroupMemberCollector.cs");
        Assert.Contains("RoofPhysical3DGeneratedRole.StructuralRafterSolid", physical);
        Assert.Contains("RoofPhysical3DGeneratedRole.StructuralRafterSolid", lifecycle);
        Assert.Contains("RoofPhysical3DGeneratedRole.StructuralRafterSolid", group);
        Assert.Contains("roofSurfaceOnly && (ordinary || structural)", physical);
    }

    [Fact]
    public void Audit_SeparatesStructuralTimberFromRoofSurfaceEdges()
    {
        var audit = Read("RoofPhysical3DHostDiagnostics.cs");
        Assert.Contains("hipRafterSolids=", audit);
        Assert.Contains("valleyRafterSolids=", audit);
        Assert.Contains("roofSurfaceTotal=", audit);
        Assert.Contains("combinedPhysicalTotal=", audit);
        Assert.Contains("uniqueKeys=", audit);
        Assert.Contains("STRUCTURAL_SOLID phase=", audit);
        Assert.Contains("resolvedHeightMm=", audit);
        Assert.Contains("structuralKey=", audit);
        Assert.Contains("STRUCTURAL_REFERENCE phase=", audit);
        Assert.Contains("planZZero=", audit);
    }

    [Fact]
    public void StructuralSolid_ReusesLogicalLineKeyAndCorePhysicalGeometry()
    {
        var adapter = Read("RoofStructuralRafterSolidMaterializationService.cs");
        var structural = Read("RoofAutomaticStructuralRafterMaterializationService.cs");
        var ordinary = Read("RoofOrdinaryRafterSolidMaterializationService.cs");
        Assert.Contains("RoofStructuralRafterPolyhedronService.TryBuild", adapter);
        Assert.Contains("RoofOrdinaryRafterSolidMaterializationService.TryBuildExistingModelInTransaction", adapter);
        Assert.Contains("RoofStructuralGeneratedStore.FindByOwner", adapter);
        Assert.Contains("RoofPhysical3DGeneratedRole.StructuralRafterSolid", adapter);
        Assert.Contains("key.ToString()", adapter);
        Assert.Contains("new Solid3d()", adapter);
        Assert.Contains("BooleanOperationType.BoolUnite", adapter);
        Assert.Contains("model.Geometry.Role == RoofStructuralRole.Hip ? 1 : 2", adapter);
        Assert.Contains("if (halves.Count == 2)", adapter);
        Assert.Contains("RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction", structural);
        Assert.Contains("RoofPhysicalElevationRules.ToState", structural);
        Assert.Contains("RoofPhysicalElevationRules.ToState", ordinary);
        Assert.Contains("MapPlanPoint(item.Segment3D.Start)", structural);
        Assert.Contains("new(point.X, point.Y, 0d)", structural);
        Assert.DoesNotContain("MapPlanPoint", adapter);
        Assert.Contains("geometry.Topology, edge", adapter);
        Assert.Contains("RoofStructuralUpperNodeMiterResolver.TryResolve", adapter);
        Assert.Contains("model.UpperNodeMiterPlane", adapter);
        Assert.Contains("elevation.LowerEndCutMode", adapter);
        Assert.Contains("model.LowerEndClipPlanes", adapter);
        Assert.Contains("SliceLowerEndPlane(solid, vertices, model, index)", adapter);
        Assert.Contains("RoofConvexPrismPlaneClipper.TryClip(sourceVertices, prior", adapter);
        Assert.Contains("model.RoofEnvelopeClipPlanes", adapter);
        Assert.Contains("if (model.UpperNodeMiterPlane is null)", adapter);
        Assert.Contains("model.EaveClipPlanes", adapter);
    }
}
