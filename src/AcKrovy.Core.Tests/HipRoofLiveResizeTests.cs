using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class HipRoofLiveResizeTests
{
    [Fact]
    public void RectangleStretch_RebasesDescriptorAndReloadsWithOriginalSlope()
    {
        var originalInput = Input(Rectangle(10000d, 6000d));
        var original = Validate(originalInput);
        var stored = RoofDefinitionPersistence.Create(
            originalInput,
            original,
            Solve(original, 37.25d));
        var changedInput = Input(Rectangle(12000d, 6000d));
        var changed = Validate(changedInput);

        var classification = RoofDefinitionPersistence.Classify(
            changedInput,
            changed,
            stored);

        Assert.Equal(RoofSourceChangeKind.SupportedResize, classification.Kind);
        var geometry = Assert.IsType<HipRoofGeometry>(classification.Geometry);
        Assert.Equal(37.25d, geometry.PrimarySlopeDegrees);
        Assert.Contains(geometry.Topology.Nodes, node => Math.Abs(node.X - 9000d) < 0.001d);

        var updated = RoofDefinitionPersistence.UpdateGeometry(stored, changedInput, geometry);
        var restored = RoofDefinitionPersistence.Restore(changedInput, changed, updated);

        Assert.Equal(RoofKind.Hip, updated.Kind);
        Assert.Equal(37.25d, updated.SlopeDegrees);
        Assert.Null(updated.RidgeEdgeFamily);
        Assert.Null(updated.RidgeDirectionX);
        Assert.Null(updated.RidgeDirectionY);
        Assert.Equal(12000d, updated.RigidFootprint!.Edge01LengthMm);
        Assert.Equal(6000d, updated.RigidFootprint.Edge12LengthMm);
        Assert.True(restored.IsValid, restored.Error.ToString());
        Assert.Equal(geometry.Signature, Assert.IsType<HipRoofGeometry>(restored.Geometry).Signature);
    }

    [Theory]
    [MemberData(nameof(ValidShapeChanges))]
    public void GenericValidShapeChange_RecomputesHipTopologyAndPhysicalDisplayRoles(
        string name,
        RoofPoint2D[] originalPoints,
        RoofPoint2D[] changedPoints)
    {
        var originalInput = Input(originalPoints);
        var original = Validate(originalInput);
        var stored = RoofDefinitionPersistence.Create(
            originalInput,
            original,
            Solve(original, 30d));
        var changedInput = Input(changedPoints);
        var changed = Validate(changedInput);

        var classification = RoofDefinitionPersistence.Classify(
            changedInput,
            changed,
            stored);

        Assert.Equal(RoofSourceChangeKind.SupportedResize, classification.Kind);
        var geometry = Assert.IsType<HipRoofGeometry>(classification.Geometry);
        var display = HipRoofWireframe.Create(geometry, 0d);
        Assert.NotEmpty(display);
        Assert.All(display, edge => Assert.True(HipRoofWireframe.IsHipTopologyRole(edge.Role), name));
        Assert.Equal(
            geometry.Topology.Edges.Count(edge =>
                edge.Kind is RoofTopologyEdgeKind.Ridge or
                    RoofTopologyEdgeKind.Hip or
                    RoofTopologyEdgeKind.Valley),
            display.Count);
        Assert.DoesNotContain(
            geometry.Topology.Edges.Where(edge =>
                edge.Kind is RoofTopologyEdgeKind.Eave or RoofTopologyEdgeKind.CoplanarSeam),
            topologyEdge => display.Any(displayEdge =>
                SameSegment(geometry.Topology, topologyEdge, displayEdge.Segment)));
    }

    [Fact]
    public void RectangleToConcaveL_AllowsDisplayEdgeCountToChangeAndAddsValley()
    {
        var originalInput = Input(Rectangle(10000d, 6000d));
        var original = Validate(originalInput);
        var originalGeometry = Solve(original, 30d);
        var stored = RoofDefinitionPersistence.Create(originalInput, original, originalGeometry);
        var changedInput = Input(LShape(8000d, 9000d));
        var changed = Validate(changedInput);

        var classification = RoofDefinitionPersistence.Classify(changedInput, changed, stored);
        var changedGeometry = Assert.IsType<HipRoofGeometry>(classification.Geometry);
        var originalDisplay = HipRoofWireframe.Create(originalGeometry, 0d);
        var changedDisplay = HipRoofWireframe.Create(changedGeometry, 0d);

        Assert.Equal(RoofSourceChangeKind.SupportedResize, classification.Kind);
        Assert.NotEqual(originalDisplay.Count, changedDisplay.Count);
        Assert.Contains(changedGeometry.Topology.Edges, edge => edge.Kind == RoofTopologyEdgeKind.Valley);
        Assert.Contains(changedDisplay, edge =>
            edge.Role is >= RoofDisplayEdgeRole.HipValley00 and <= RoofDisplayEdgeRole.HipValley31);

        var updated = RoofDefinitionPersistence.UpdateGeometry(
            stored,
            changedInput,
            changedGeometry);
        Assert.Equal(6, updated.RigidFootprint!.VertexCount);
        Assert.True(RoofDefinitionPersistence.Restore(changedInput, changed, updated).IsValid);
    }

    [Fact]
    public void CompactDescriptorCollision_StillChangesTheExpectedDisplaySignature()
    {
        var originalInput = Input(LShape(8000d, 8000d));
        var original = Validate(originalInput);
        var originalGeometry = Solve(original, 30d);
        var stored = RoofDefinitionPersistence.Create(originalInput, original, originalGeometry);
        var changedInput = Input(LShape(8000d, 9000d));
        var changed = Validate(changedInput);

        var classification = RoofDefinitionPersistence.Classify(changedInput, changed, stored);
        var changedGeometry = Assert.IsType<HipRoofGeometry>(classification.Geometry);
        var oldSignature = RoofWireframe.BuildGenerationSignature(
            HipRoofWireframe.Create(originalGeometry, 0d));
        var newSignature = RoofWireframe.BuildGenerationSignature(
            HipRoofWireframe.Create(changedGeometry, 0d));

        // The compact schema-5 descriptor intentionally stores only the first two edge
        // lengths. Host live refresh therefore also compares the complete expected
        // permanent-display signature before deciding that a modified Hip is unchanged.
        Assert.Equal(RoofSourceChangeKind.RigidEquivalent, classification.Kind);
        Assert.NotEqual(oldSignature, newSignature);
    }

    public static IEnumerable<object[]> ValidShapeChanges()
    {
        yield return [
            "convex trapezoid",
            Rectangle(10000d, 6000d),
            new RoofPoint2D[] { new(0, 0), new(10000, 0), new(9000, 6000), new(1000, 6000) },
        ];
        yield return ["concave L", LShape(8000d, 8000d), LShape(9000d, 9000d)];
    }

    private static RoofPoint2D[] Rectangle(double width, double height) =>
        [new(0, 0), new(width, 0), new(width, height), new(0, height)];

    private static RoofPoint2D[] LShape(double width, double upperHeight) =>
        [new(0, 0), new(width, 0), new(width, 3000), new(3000, 3000), new(3000, upperHeight), new(0, upperHeight)];

    private static RoofFootprintInput Input(IReadOnlyList<RoofPoint2D> points) =>
        new(points, true, false, true);

    private static RoofFootprint Validate(RoofFootprintInput input)
    {
        var validation = RoofFootprintValidator.Validate(input);
        Assert.True(validation.IsValid, validation.Error.ToString());
        return validation.Footprint!;
    }

    private static HipRoofGeometry Solve(RoofFootprint footprint, double slope)
    {
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(slope),
            RoofKind.Hip));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<HipRoofGeometry>(result.Geometry);
    }

    private static bool SameSegment(
        RoofTopology topology,
        RoofTopologyEdge edge,
        RoofSegment3D segment)
    {
        var start = topology.Nodes[edge.StartNodeIndex];
        var end = topology.Nodes[edge.EndNodeIndex];
        return (SamePoint(start, segment.Start) && SamePoint(end, segment.End)) ||
               (SamePoint(start, segment.End) && SamePoint(end, segment.Start));
    }

    private static bool SamePoint(RoofPoint3D node, RoofPoint3D point) =>
        Math.Abs(node.X - point.X) < 0.001d &&
        Math.Abs(node.Y - point.Y) < 0.001d &&
        Math.Abs(node.Z - point.Z) < 0.001d;
}
