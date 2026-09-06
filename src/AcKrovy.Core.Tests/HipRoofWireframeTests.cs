using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class HipRoofWireframeTests
{
    [Fact]
    public void Rectangle_MapsOneRidgeAndFourHips_OmitsEaveAndCoplanarSeam()
    {
        var geometry = Solve(Validate([
            new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000),
        ]));

        var edges = HipRoofWireframe.Create(geometry, 1250d);

        Assert.Equal(5, edges.Count);
        Assert.Equal(1, edges.Count(edge => HipRoofWireframe.IsRidgeRole(edge.Role)));
        Assert.Equal(4, edges.Count(edge =>
            edge.Role is >= RoofDisplayEdgeRole.Hip00 and <= RoofDisplayEdgeRole.Hip47));
        Assert.DoesNotContain(edges, edge =>
            edge.Role is >= RoofDisplayEdgeRole.HipValley00 and <= RoofDisplayEdgeRole.HipValley31);
        Assert.DoesNotContain(edges, edge =>
            edge.Role is RoofDisplayEdgeRole.Eave0 or RoofDisplayEdgeRole.Eave1 or RoofDisplayEdgeRole.Ridge);
        Assert.All(edges, edge =>
        {
            Assert.True(HipRoofWireframe.IsHipTopologyRole(edge.Role));
            Assert.True(edge.Segment.Start.Z >= 1250d);
            Assert.True(edge.Segment.End.Z >= 1250d);
        });
        Assert.True(RoofWireframe.IsCompleteRoleSet(edges.Select(edge => edge.Role)));
        Assert.Equal(
            RoofWireframe.BuildGenerationSignature(edges),
            RoofWireframe.BuildGenerationSignature(RoofWireframe.Create(geometry, 1250d)));
    }

    [Theory]
    [MemberData(nameof(ConcaveFootprints))]
    public void ConcaveFootprints_MapHipRidgeValleyOnly_WithoutHostHeuristics(
        string name,
        RoofPoint2D[] points)
    {
        var geometry = Solve(Validate(points));
        Assert.Contains(geometry.Topology.Edges, edge => edge.Kind == RoofTopologyEdgeKind.Valley);

        var edges = RoofWireframe.Create(geometry, 0d);

        Assert.True(edges.Count > 0, name);
        Assert.Contains(edges, edge => HipRoofWireframe.IsRidgeRole(edge.Role));
        Assert.Contains(edges, edge =>
            edge.Role is >= RoofDisplayEdgeRole.Hip00 and <= RoofDisplayEdgeRole.Hip47);
        Assert.Contains(edges, edge =>
            edge.Role is >= RoofDisplayEdgeRole.HipValley00 and <= RoofDisplayEdgeRole.HipValley31);
        Assert.Equal(
            geometry.Topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Ridge),
            edges.Count(edge => HipRoofWireframe.IsRidgeRole(edge.Role)));
        Assert.Equal(
            geometry.Topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Hip),
            edges.Count(edge =>
                edge.Role is >= RoofDisplayEdgeRole.Hip00 and <= RoofDisplayEdgeRole.Hip47));
        Assert.Equal(
            geometry.Topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Valley),
            edges.Count(edge =>
                edge.Role is >= RoofDisplayEdgeRole.HipValley00 and <= RoofDisplayEdgeRole.HipValley31));
        Assert.Equal(
            geometry.Topology.Edges.Count(edge =>
                edge.Kind is RoofTopologyEdgeKind.Hip or
                RoofTopologyEdgeKind.Ridge or
                RoofTopologyEdgeKind.Valley),
            edges.Count);
        Assert.All(edges, edge => Assert.True(HipRoofWireframe.IsHipTopologyRole(edge.Role)));
        Assert.True(RoofWireframe.IsCompleteRoleSet(edges.Select(edge => edge.Role)), name);
    }

    [Fact]
    public void SharedDispatch_KeepsGableAndMonopitchCompleteRoleSetsUnchanged()
    {
        Assert.True(RoofWireframe.IsCompleteRoleSet([
            RoofDisplayEdgeRole.Ridge,
            RoofDisplayEdgeRole.Eave0,
            RoofDisplayEdgeRole.Eave1,
            RoofDisplayEdgeRole.GableSlope00,
            RoofDisplayEdgeRole.GableSlope01,
            RoofDisplayEdgeRole.GableSlope10,
            RoofDisplayEdgeRole.GableSlope11,
        ]));
        Assert.True(RoofWireframe.IsCompleteRoleSet([
            RoofDisplayEdgeRole.MonopitchLowEave,
            RoofDisplayEdgeRole.MonopitchHighEave,
            RoofDisplayEdgeRole.MonopitchSlopeSide0,
            RoofDisplayEdgeRole.MonopitchSlopeSide1,
            RoofDisplayEdgeRole.MonopitchDirection,
            RoofDisplayEdgeRole.MonopitchDirectionWing0,
            RoofDisplayEdgeRole.MonopitchDirectionWing1,
        ]));
        Assert.False(RoofWireframe.TryGetTopology(RoofKind.Hip, out _));
    }

    public static IEnumerable<object[]> ConcaveFootprints()
    {
        yield return ["L", new RoofPoint2D[] { new(0, 0), new(8000, 0), new(8000, 3000), new(3000, 3000), new(3000, 8000), new(0, 8000) }];
        yield return ["U", new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 9000), new(7000, 9000), new(7000, 3000), new(3000, 3000), new(3000, 9000), new(0, 9000) }];
        yield return ["T", new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 3000), new(6500, 3000), new(6500, 9000), new(3500, 9000), new(3500, 3000), new(0, 3000) }];
    }

    private static HipRoofGeometry Solve(RoofFootprint footprint) =>
        Assert.IsType<HipRoofGeometry>(
            RoofGeometrySolver.Solve(new RoofDefinition(footprint, new RoofParameters(30d), RoofKind.Hip)).Geometry);

    private static RoofFootprint Validate(IReadOnlyList<RoofPoint2D> points)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(points, true));
        Assert.True(validation.IsValid, validation.Error.ToString());
        return validation.Footprint!;
    }
}
