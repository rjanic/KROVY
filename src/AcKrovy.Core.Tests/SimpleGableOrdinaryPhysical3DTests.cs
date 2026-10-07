using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Ordinary Physical3D for SimpleGable (Sedlová) via Hip ordinary pipeline adapters.
/// No Structural Hip/Valley members.
/// </summary>
public sealed class SimpleGableOrdinaryPhysical3DTests
{
    [Fact]
    public void Topology_SimpleGable30_HasTwoFacesOneRidgeNoHipValley_UpwardNormals()
    {
        var geometry = SolveGable(10000d, 6000d, 30d);
        Assert.True(SimpleGableRoofTopologyAdapter.TryCreate(
            geometry, out var topology, out var reason), reason);

        Assert.Equal(2, topology.Faces.Count);
        Assert.Equal(1, topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Ridge));
        Assert.Equal(2, topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Eave));
        Assert.Equal(0, topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Hip));
        Assert.Equal(0, topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Valley));

        var ridge = Assert.Single(topology.Edges, edge => edge.Kind == RoofTopologyEdgeKind.Ridge);
        Assert.Equal(2, ridge.FaceIndices.Count);
        Assert.Contains(0, ridge.FaceIndices);
        Assert.Contains(1, ridge.FaceIndices);

        foreach (var face in topology.Faces)
        {
            Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                topology, face, out var normal));
            Assert.True(normal.Z > 0d);
        }
    }

    [Fact]
    public void AsymmetricGable_IsRejectedByTopologyAdapter()
    {
        var geometry = SolveAsymmetric(10000d, 6000d, 30d, 45d);
        Assert.Equal(RoofKind.AsymmetricGable, geometry.Kind);
        Assert.False(SimpleGableRoofTopologyAdapter.TryCreate(geometry, out _, out var reason));
        Assert.Equal("UnsupportedGableKindOrFaces", reason);
    }

    [Theory]
    [InlineData(15d)]
    [InlineData(30d)]
    [InlineData(45d)]
    [InlineData(60d)]
    public void PitchMatrix_BuildsValidPhysicalMembersOnBothFaces(double pitch)
    {
        var (geometry, topology, faceLayout, generated) = BuildPhysicalInputs(pitch);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "SG", topology, faceLayout, generated, 0d, 80d, 160d,
            new RoofAutomaticRafterPhysicalSettings(), null, out var model, out var reason), reason);
        Assert.NotNull(model);
        Assert.Equal(generated.Rafters.Count, model!.Members.Count);
        Assert.Contains(model.Members, m => m.MemberKey.RoofFace == RafterRoofFace.Face0);
        Assert.Contains(model.Members, m => m.MemberKey.RoofFace == RafterRoofFace.Face1);
        Assert.All(model.Members, member =>
        {
            Assert.True(member.PlanAxis.Start.DistanceTo(member.PlanAxis.End) > 1d);
            Assert.Equal(0d, member.PlanAxis.Start.Z);
            Assert.Equal(0d, member.PlanAxis.End.Z);
            Assert.True(member.SolidVertices.Count >= 8);
            AssertUpperFaceInRoofPlane(topology, member, 0d);
        });
    }

    [Theory]
    [InlineData(80d, 160d)]
    [InlineData(100d, 200d)]
    [InlineData(60d, 120d)]
    public void SectionMatrix_PreservesWidthAndHeight(double width, double height)
    {
        var (geometry, topology, faceLayout, generated) = BuildPhysicalInputs(30d);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "SG", topology, faceLayout, generated, 0d, width, height,
            new RoofAutomaticRafterPhysicalSettings(), null, out var model, out var reason), reason);
        foreach (var member in model!.Members)
        {
            Assert.Equal(width, member.WidthMm, 6);
            Assert.Equal(height, member.HeightMm, 6);
            AssertUpperFaceInRoofPlane(topology, member, 0d);
        }
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical)]
    [InlineData(LowerEndCutMode.Perpendicular)]
    [InlineData(LowerEndCutMode.Horizontal)]
    public void LowerEndCutModes_BuildOnBothFaces(LowerEndCutMode cut)
    {
        var (_, topology, faceLayout, generated) = BuildPhysicalInputs(30d);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "SG", topology, faceLayout, generated, 0d, 80d, 160d,
            new RoofAutomaticRafterPhysicalSettings(cut, RidgeJoinMode.Meet),
            null, out var model, out var reason), reason);
        Assert.Contains(model!.Members, m => m.MemberKey.RoofFace == RafterRoofFace.Face0);
        Assert.Contains(model.Members, m => m.MemberKey.RoofFace == RafterRoofFace.Face1);
        Assert.All(model.Members, m => Assert.True(m.SolidVertices.Count >= 8));
    }

    [Theory]
    [InlineData(30d, RidgeJoinMode.Meet)]
    [InlineData(30d, RidgeJoinMode.Overlap)]
    [InlineData(45d, RidgeJoinMode.Meet)]
    [InlineData(45d, RidgeJoinMode.Overlap)]
    public void RidgeModes_BothFaces_MeetOrOverlap(double pitch, RidgeJoinMode mode)
    {
        var (_, topology, faceLayout, generated) = BuildPhysicalInputs(pitch);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "SG", topology, faceLayout, generated, 0d, 80d, 160d,
            new RoofAutomaticRafterPhysicalSettings(LowerEndCutMode.Vertical, mode),
            null, out var model, out var reason), reason);

        var ridgeMembers = model!.Members.Where(m =>
            m.EndBoundaryRole == RoofRafterBoundaryRole.Ridge).ToArray();
        Assert.NotEmpty(ridgeMembers);
        Assert.Contains(ridgeMembers, m => m.MemberKey.RoofFace == RafterRoofFace.Face0);
        Assert.Contains(ridgeMembers, m => m.MemberKey.RoofFace == RafterRoofFace.Face1);

        foreach (var ridge in ridgeMembers)
        {
            AssertUpperFaceInRoofPlane(topology, ridge, 0d);
            var endTop = Midpoint(ridge.SolidVertices[2], ridge.SolidVertices[3]);
            var endBottom = Midpoint(ridge.SolidVertices[6], ridge.SolidVertices[7]);
            var planDelta = Math.Sqrt(
                Math.Pow(endTop.X - endBottom.X, 2) +
                Math.Pow(endTop.Y - endBottom.Y, 2));
            if (mode == RidgeJoinMode.Meet)
            {
                Assert.True(planDelta < 1d, $"Meet ridge plan delta={planDelta}");
            }
            else
            {
                Assert.True(planDelta > 1d, $"Overlap ridge plan delta={planDelta}");
            }
        }
    }

    [Fact]
    public void Face1_DoesNotInvertUpperFaceOrSection()
    {
        var (_, topology, faceLayout, generated) = BuildPhysicalInputs(30d);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "SG", topology, faceLayout, generated, 0d, 80d, 160d,
            new RoofAutomaticRafterPhysicalSettings(), null, out var model, out var reason), reason);

        var face1 = Assert.Single(topology.Faces, f => f.SourceEdgeIndex == 1);
        Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
            topology, face1, out var normal));
        Assert.True(normal.Z > 0d);

        var face1Members = model!.Members.Where(m => m.MemberKey.RoofFace == RafterRoofFace.Face1).ToArray();
        Assert.NotEmpty(face1Members);
        foreach (var member in face1Members)
        {
            Assert.Equal(RafterRoofFace.Face1, member.MemberKey.RoofFace);
            Assert.Equal(1, member.SourceFaceIndex);
            AssertUpperFaceInRoofPlane(topology, member, 0d);
            Assert.Equal(80d, member.WidthMm, 6);
            Assert.Equal(160d, member.HeightMm, 6);
            // Upper face (first 4) must sit above lower face (last 4) along roof normal.
            var origin = topology.Nodes[face1.BoundaryNodeIndices[0]];
            var upperZ = AveragePlaneOffset(member.SolidVertices.Take(4), origin, normal, 0d);
            var lowerZ = AveragePlaneOffset(member.SolidVertices.Skip(4).Take(4), origin, normal, 0d);
            Assert.True(upperZ > lowerZ);
        }
    }

    [Fact]
    public void Face0AndFace1_SameStation_HaveDistinctLogicalKeys()
    {
        var (_, _, faceLayout, generated) = BuildPhysicalInputs(30d);
        var face0 = generated.Rafters.First(r => r.Face == RafterRoofFace.Face0);
        var face1 = generated.Rafters.First(r =>
            r.Face == RafterRoofFace.Face1 && r.StationIndex == face0.StationIndex);
        Assert.NotEqual(face0.LogicalKey, face1.LogicalKey);
        Assert.Contains(faceLayout.Segments, s =>
            s.SourceFaceIndex == 0 && s.StationIndex == face0.StationIndex);
        Assert.Contains(faceLayout.Segments, s =>
            s.SourceFaceIndex == 1 && s.StationIndex == face1.StationIndex);
    }

    [Fact]
    public void PhysicalPair_Face0AndFace1_PlanZ0_UpperInPlane()
    {
        var (_, topology, faceLayout, generated) = BuildPhysicalInputs(30d);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "SG", topology, faceLayout, generated, 2500d, 80d, 160d,
            new RoofAutomaticRafterPhysicalSettings(), null, out var model, out var reason), reason);

        var face0 = Assert.Single(model!.Members, m =>
            m.MemberKey.RoofFace == RafterRoofFace.Face0 && m.MemberKey.StationIndex == 0);
        var face1 = Assert.Single(model.Members, m =>
            m.MemberKey.RoofFace == RafterRoofFace.Face1 && m.MemberKey.StationIndex == 0);
        foreach (var member in new[] { face0, face1 })
        {
            Assert.Equal(0d, member.PlanAxis.Start.Z);
            Assert.Equal(0d, member.PlanAxis.End.Z);
            Assert.True(member.PlanAxis.Start.DistanceTo(member.PlanAxis.End) > 1d);
            Assert.True(member.SolidVertices.Count >= 8);
            AssertUpperFaceInRoofPlane(topology, member, 2500d);
            Assert.Equal(RoofRafterBoundaryRole.Eave, member.StartBoundaryRole);
            Assert.Equal(RoofRafterBoundaryRole.Ridge, member.EndBoundaryRole);
        }
    }

    [Fact]
    public void OrdinaryPhysicalPath_DoesNotRequireStructuralTrimSources()
    {
        var (_, topology, faceLayout, generated) = BuildPhysicalInputs(30d);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "SG", topology, faceLayout, generated, 0d, 80d, 160d,
            new RoofAutomaticRafterPhysicalSettings(),
            null,
            Array.Empty<RoofStructuralRafterTrimSource>(),
            out var model, out var reason), reason);
        Assert.All(model!.Members, m => Assert.Null(m.StructuralCut));
        Assert.Equal(0, topology.Edges.Count(e =>
            e.Kind is RoofTopologyEdgeKind.Hip or RoofTopologyEdgeKind.Valley));
    }

    [Fact]
    public void SourceContract_HostGate_UsesExplicitSimpleGableAdapters_NotStructural()
    {
        var host = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofOrdinaryRafterSolidMaterializationService.cs");
        Assert.Contains("SimpleGableRoofTopologyAdapter", host);
        Assert.Contains("SimpleGableOrdinaryRafterPhysicalAdapter", host);
        Assert.Contains("RoofKind.SimpleGable", host);
        Assert.Contains("Array.Empty<RoofStructuralRafterTrimSource>()", host);

        var resolveStart = host.IndexOf(
            "TryResolveOrdinaryPhysicalBuildContext", StringComparison.Ordinal);
        Assert.True(resolveStart >= 0);
        var gableBranch = host.Substring(resolveStart, Math.Min(2500, host.Length - resolveStart));
        Assert.DoesNotContain("RoofAutomaticStructuralRafterPlanner", gableBranch);
        Assert.DoesNotContain("HipRafter", gableBranch);
        Assert.DoesNotContain("ValleyRafter", gableBranch);

        var topo = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "SimpleGableRoofTopologyAdapter.cs");
        Assert.Contains("RoofTopologyEdgeKind.Ridge", topo);
        Assert.Contains("CoplanarSeam", topo);
    }

    private static (
        SimpleGableRoofGeometry Geometry,
        RoofTopology Topology,
        RoofFaceRafterLayout FaceLayout,
        RoofRafterLayout Generated) BuildPhysicalInputs(double pitch)
    {
        var geometry = SolveGable(10000d, 6000d, pitch);
        Assert.True(SimpleGableRoofTopologyAdapter.TryCreate(
            geometry, out var topology, out var topoReason), topoReason);
        var solved = RoofRafterLayoutSolver.Solve(
            geometry, new RafterLayoutParameters(800d, 80d));
        Assert.True(solved.IsValid, solved.Error.ToString());
        Assert.NotNull(solved.Layout);
        Assert.True(SimpleGableOrdinaryRafterPhysicalAdapter.TryCreateFaceLayout(
            geometry, topology, solved.Layout!, out var faceLayout, out var layoutReason),
            layoutReason);
        return (geometry, topology, faceLayout, solved.Layout!);
    }

    private static void AssertUpperFaceInRoofPlane(
        RoofTopology topology,
        RoofAutomaticRafterPhysicalMember member,
        double eaveElevationMm)
    {
        var face = topology.Faces.Single(f => f.SourceEdgeIndex == member.SourceFaceIndex);
        Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
            topology, face, out var normal));
        var origin = topology.Nodes[face.BoundaryNodeIndices[0]];
        foreach (var vertex in member.SolidVertices.Take(4))
        {
            var offset = normal.X * (vertex.X - origin.X) +
                normal.Y * (vertex.Y - origin.Y) +
                normal.Z * (vertex.Z - origin.Z - eaveElevationMm);
            Assert.Equal(0d, offset, 5);
        }
    }

    private static double AveragePlaneOffset(
        IEnumerable<RoofPoint3D> points,
        RoofPoint3D origin,
        RoofFaceUnitNormal normal,
        double eaveElevationMm) =>
        points.Average(vertex =>
            normal.X * (vertex.X - origin.X) +
            normal.Y * (vertex.Y - origin.Y) +
            normal.Z * (vertex.Z - origin.Z - eaveElevationMm));

    private static RoofPoint3D Midpoint(RoofPoint3D a, RoofPoint3D b) =>
        new((a.X + b.X) / 2d, (a.Y + b.Y) / 2d, (a.Z + b.Z) / 2d);

    private static SimpleGableRoofGeometry SolveGable(double width, double depth, double slope)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [
                new(0d, 0d),
                new(width, 0d),
                new(width, depth),
                new(0d, depth),
            ],
            true));
        Assert.True(validation.IsValid, validation.Error.ToString());
        Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var direction));
        var result = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(slope, direction)));
        Assert.True(result.IsValid, result.Error.ToString());
        Assert.Equal(RoofKind.SimpleGable, result.Geometry!.Kind);
        return result.Geometry!;
    }

    private static SimpleGableRoofGeometry SolveAsymmetric(
        double width, double depth, double slope0, double slope1)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [
                new(0d, 0d),
                new(width, 0d),
                new(width, depth),
                new(0d, depth),
            ],
            true));
        Assert.True(validation.IsValid);
        Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var direction));
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(slope0, direction, Face1SlopeDegrees: slope1),
            RoofKind.AsymmetricGable));
        Assert.True(result.IsValid, result.Error.ToString());
        var geometry = Assert.IsType<SimpleGableRoofGeometry>(result.Geometry);
        Assert.Equal(RoofKind.AsymmetricGable, geometry.Kind);
        return geometry;
    }

    private static string Read(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine([root, .. path]));
    }
}
