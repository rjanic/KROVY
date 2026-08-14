using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class SimpleGableRoofWireframeTests
{
    [Fact]
    public void SolverGeometry_MapsToExactlySevenDeterministicSemanticEdges()
    {
        var geometry = Solve();

        var edges = SimpleGableRoofWireframe.Create(geometry, 125d);

        Assert.Equal(SimpleGableRoofWireframe.EdgeCount, edges.Count);
        Assert.Equal(Enum.GetValues<RoofDisplayEdgeRole>(), edges.Select(edge => edge.Role));
        Assert.All(edges.SelectMany(edge => new[] { edge.Segment.Start, edge.Segment.End }),
            point => Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z)));
        Assert.Equal(125d + geometry.RiseMm, edges[0].Segment.Start.Z, 9);
        Assert.Equal(125d, edges[1].Segment.Start.Z, 9);
        Assert.Equal(125d, edges[2].Segment.End.Z, 9);
    }

    [Fact]
    public void RolesReferenceOnlySolverRidgeAndFaceEaves()
    {
        var geometry = Solve();
        var edges = SimpleGableRoofWireframe.Create(geometry, 0d).ToDictionary(edge => edge.Role);

        Assert.Equal(geometry.Ridge, edges[RoofDisplayEdgeRole.Ridge].Segment);
        Assert.Equal(geometry.Faces[0].Eave, edges[RoofDisplayEdgeRole.Eave0].Segment);
        Assert.Equal(geometry.Faces[1].Eave, edges[RoofDisplayEdgeRole.Eave1].Segment);
        Assert.Equal(
            new RoofSegment3D(geometry.Ridge.Start, geometry.Faces[0].Eave.Start),
            edges[RoofDisplayEdgeRole.GableSlope00].Segment);
        Assert.Equal(
            new RoofSegment3D(geometry.Ridge.End, geometry.Faces[1].Eave.End),
            edges[RoofDisplayEdgeRole.GableSlope11].Segment);
    }

    [Fact]
    public void GenerationSignature_IsDeterministicAndIncludesWorldElevation()
    {
        var geometry = Solve();
        var first = SimpleGableRoofWireframe.Create(geometry, 0d);
        var repeated = SimpleGableRoofWireframe.Create(geometry, 0d);
        var movedInZ = SimpleGableRoofWireframe.Create(geometry, 50d);

        Assert.Equal(
            SimpleGableRoofWireframe.BuildGenerationSignature(first),
            SimpleGableRoofWireframe.BuildGenerationSignature(repeated));
        Assert.NotEqual(
            SimpleGableRoofWireframe.BuildGenerationSignature(first),
            SimpleGableRoofWireframe.BuildGenerationSignature(movedInZ));
    }

    [Fact]
    public void NonFiniteElevation_IsRejectedBeforeAnyCadEntityCanBeCreated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SimpleGableRoofWireframe.Create(Solve(), double.NaN));
    }

    [Fact]
    public void SignatureRejectsDuplicateOrNonFiniteEdgeSets()
    {
        var edges = SimpleGableRoofWireframe.Create(Solve(), 0d).ToArray();
        edges[6] = edges[0];
        Assert.Throws<ArgumentException>(() =>
            SimpleGableRoofWireframe.BuildGenerationSignature(edges));
    }

    private static SimpleGableRoofGeometry Solve()
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)],
            IsClosed: true));
        Assert.True(validation.IsValid);
        Assert.True(RoofDirection2D.TryCreate(1, 0, out var direction));
        var result = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(30, direction)));
        Assert.True(result.IsValid);
        return result.Geometry!;
    }
}
