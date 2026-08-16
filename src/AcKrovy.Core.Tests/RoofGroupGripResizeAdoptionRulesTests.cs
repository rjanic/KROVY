using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGroupGripResizeAdoptionRulesTests
{
    [Fact]
    public void GableEndEnlarge_AdoptsSemanticSource()
    {
        AssertAdopts(
            Rectangle(),
            StretchGableEnd(Rectangle(), 2000d),
            RoofRidgeEdgeFamily.SourceEdge01,
            RoofGroupGripSideResizeKind.GableEnd);
    }

    [Fact]
    public void EaveSideEnlarge_AdoptsSemanticSource()
    {
        AssertAdopts(
            Rectangle(),
            StretchEaveSide(Rectangle(), 2000d),
            RoofRidgeEdgeFamily.SourceEdge01,
            RoofGroupGripSideResizeKind.EaveSide);
    }

    [Fact]
    public void GableEndShrink_AdoptsSemanticSource()
    {
        AssertAdopts(
            Rectangle(),
            StretchGableEnd(Rectangle(), -1500d),
            RoofRidgeEdgeFamily.SourceEdge01,
            RoofGroupGripSideResizeKind.GableEnd);
    }

    [Fact]
    public void EaveSideShrink_AdoptsSemanticSource()
    {
        AssertAdopts(
            Rectangle(),
            StretchEaveSide(Rectangle(), -1000d),
            RoofRidgeEdgeFamily.SourceEdge01,
            RoofGroupGripSideResizeKind.EaveSide);
    }

    [Fact]
    public void RotatedRoof_GableEndAdopts()
    {
        var original = Transform(Rectangle(), 30d, 400d, -250d);
        AssertAdopts(
            original,
            StretchGableEnd(original, 1800d),
            RoofRidgeEdgeFamily.SourceEdge01,
            RoofGroupGripSideResizeKind.GableEnd);
    }

    [Fact]
    public void RotatedRoof_EaveSideAdopts()
    {
        var original = Transform(Rectangle(), 30d, 400d, -250d);
        AssertAdopts(
            original,
            StretchEaveSide(original, 1800d),
            RoofRidgeEdgeFamily.SourceEdge01,
            RoofGroupGripSideResizeKind.EaveSide);
    }

    [Fact]
    public void SquareRoof_EaveSideAdoptsWithoutFlippingFamily()
    {
        var square = Rectangle(8000d, 8000d);
        var result = AssertAdopts(
            square,
            StretchEaveSide(square, 5000d),
            RoofRidgeEdgeFamily.SourceEdge01,
            RoofGroupGripSideResizeKind.EaveSide);
        var data = Create(square, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var geometry = Restore(new RoofFootprintInput(result.AdoptedVertices!, true), data);
        AssertParallel(geometry.RidgeDirection, EdgeDirection(result.AdoptedVertices!, 0));
    }

    [Fact]
    public void AspectCrossover_PreservesRidgeFamily()
    {
        var original = Rectangle(10000d, 6000d);
        // With SourceEdge12, StretchEaveSide lengthens the ridge-parallel family.
        var result = AssertAdopts(
            original,
            StretchEaveSide(original, 8000d),
            RoofRidgeEdgeFamily.SourceEdge12,
            RoofGroupGripSideResizeKind.GableEnd);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge12);
        var geometry = Restore(new RoofFootprintInput(result.AdoptedVertices!, true), data);
        AssertParallel(geometry.RidgeDirection, EdgeDirection(result.AdoptedVertices!, 1));
        Assert.Equal(35d, geometry.SlopeDegrees);
    }

    [Fact]
    public void SlopeUnchanged_AndSourceVertexCountPreserved()
    {
        var original = Rectangle();
        var result = AssertAdopts(
            original,
            StretchGableEnd(original, 1250d),
            RoofRidgeEdgeFamily.SourceEdge01,
            RoofGroupGripSideResizeKind.GableEnd);
        Assert.Equal(4, result.AdoptedVertices!.Count);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        Assert.Equal(35d, Restore(new RoofFootprintInput(result.AdoptedVertices, true), data).SlopeDegrees);
    }

    [Fact]
    public void SingleDisplayLineMutation_IsRejectedAsAmbiguous()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var expected = ToMap(Wireframe(original, data));
        var observed = expected.ToDictionary(pair => pair.Key, pair => pair.Value);
        var ridge = observed[RoofDisplayEdgeRole.Ridge];
        observed[RoofDisplayEdgeRole.Ridge] = new RoofSegment3D(
            new RoofPoint3D(ridge.Start.X + 500d, ridge.Start.Y, ridge.Start.Z),
            ridge.End);

        var result = RoofGroupGripResizeAdoptionRules.TryDeriveSupportedSideResize(
            original.Vertices!,
            data,
            expected,
            observed);
        Assert.False(result.CanAdopt);
    }

    [Fact]
    public void CornerStretchChangingBothSides_IsRejected()
    {
        var original = Rectangle();
        var corner = StretchEaveSide(StretchGableEnd(original, 2000d), 2000d);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var result = RoofGroupGripResizeAdoptionRules.TryDeriveSupportedSideResize(
            original.Vertices!,
            data,
            ToMap(Wireframe(original, data)),
            ToMap(Wireframe(corner, data)));
        Assert.False(result.CanAdopt);
        Assert.Equal("not-unique-side-resize", result.RejectionReason);
    }

    [Fact]
    public void IdenticalDisplay_IsRejected()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var map = ToMap(Wireframe(original, data));
        var result = RoofGroupGripResizeAdoptionRules.TryDeriveSupportedSideResize(
            original.Vertices!,
            data,
            map,
            map);
        Assert.False(result.CanAdopt);
    }

    [Fact]
    public void MissingRole_IsRejected()
    {
        var original = Rectangle();
        var resized = StretchGableEnd(original, 2000d);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var expected = ToMap(Wireframe(original, data));
        var observed = ToMap(Wireframe(resized, data));
        observed.Remove(RoofDisplayEdgeRole.GableSlope11);
        var result = RoofGroupGripResizeAdoptionRules.TryDeriveSupportedSideResize(
            original.Vertices!,
            data,
            expected,
            observed);
        Assert.False(result.CanAdopt);
        Assert.Equal("missing-display-roles", result.RejectionReason);
    }

    private static RoofGroupGripResizeAdoptionResult AssertAdopts(
        RoofFootprintInput original,
        RoofFootprintInput resized,
        RoofRidgeEdgeFamily family,
        RoofGroupGripSideResizeKind expectedKind)
    {
        var data = Create(original, 35d, family);
        var result = RoofGroupGripResizeAdoptionRules.TryDeriveSupportedSideResize(
            original.Vertices!,
            data,
            ToMap(Wireframe(original, data)),
            ToMap(Wireframe(resized, data)));
        Assert.True(result.CanAdopt, result.RejectionReason);
        Assert.Equal(expectedKind, result.Kind);
        Assert.NotNull(result.AdoptedVertices);
        Assert.Equal(4, result.AdoptedVertices!.Count);
        for (var i = 0; i < 4; i++)
        {
            Assert.True(
                result.AdoptedVertices[i].DistanceTo(resized.Vertices![i]) <=
                RoofGroupGripResizeAdoptionRules.GripAdoptionToleranceMm);
        }

        return result;
    }

    private static Dictionary<RoofDisplayEdgeRole, RoofSegment3D> ToMap(
        IReadOnlyList<RoofDisplayEdge> edges) =>
        edges.ToDictionary(edge => edge.Role, edge => edge.Segment);

    private static IReadOnlyList<RoofDisplayEdge> Wireframe(
        RoofFootprintInput source,
        RoofDefinitionData data) =>
        SimpleGableRoofWireframe.Create(Restore(source, data), 0d);

    private static RoofDefinitionData Create(
        RoofFootprintInput source,
        double slope,
        RoofRidgeEdgeFamily family)
    {
        var footprint = Validate(source);
        var direction = EdgeDirection(
            source.Vertices!,
            family == RoofRidgeEdgeFamily.SourceEdge01 ? 0 : 1);
        var solved = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(slope, direction)));
        Assert.True(solved.IsValid);
        return RoofDefinitionPersistence.Create(source, footprint, solved.Geometry!);
    }

    private static SimpleGableRoofGeometry Restore(
        RoofFootprintInput source,
        RoofDefinitionData data)
    {
        var result = RoofDefinitionPersistence.Restore(source, Validate(source), data);
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Geometry!;
    }

    private static RoofFootprint Validate(RoofFootprintInput source)
    {
        var result = RoofFootprintValidator.Validate(source);
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Footprint!;
    }

    private static RoofDirection2D EdgeDirection(IReadOnlyList<RoofPoint2D> vertices, int edgeIndex)
    {
        var first = vertices[edgeIndex];
        var second = vertices[(edgeIndex + 1) % vertices.Count];
        Assert.True(RoofDirection2D.TryCreate(second.X - first.X, second.Y - first.Y, out var direction));
        return direction;
    }

    private static void AssertParallel(RoofDirection2D first, RoofDirection2D second) =>
        Assert.True(Math.Abs(first.X * second.Y - first.Y * second.X) < 1e-9d);

    private static RoofFootprintInput Rectangle(double width = 10000d, double height = 6000d) =>
        Input([
            new(1000d, -2000d),
            new(1000d + width, -2000d),
            new(1000d + width, -2000d + height),
            new(1000d, -2000d + height)]);

    private static RoofFootprintInput StretchGableEnd(RoofFootprintInput source, double delta)
    {
        var vertices = source.Vertices!;
        var axis = Unit(vertices[0], vertices[1]);
        return Input([
            vertices[0],
            Offset(vertices[1], axis, delta),
            Offset(vertices[2], axis, delta),
            vertices[3]]);
    }

    private static RoofFootprintInput StretchEaveSide(RoofFootprintInput source, double delta)
    {
        var vertices = source.Vertices!;
        var axis = Unit(vertices[1], vertices[2]);
        return Input([
            vertices[0],
            vertices[1],
            Offset(vertices[2], axis, delta),
            Offset(vertices[3], axis, delta)]);
    }

    private static RoofFootprintInput Transform(
        RoofFootprintInput source,
        double angleDegrees,
        double translateX,
        double translateY)
    {
        var radians = angleDegrees * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return Input(source.Vertices!.Select(point => new RoofPoint2D(
            point.X * cosine - point.Y * sine + translateX,
            point.X * sine + point.Y * cosine + translateY)).ToArray());
    }

    private static RoofFootprintInput Input(IReadOnlyList<RoofPoint2D> vertices) =>
        new(vertices, true, false, true);

    private static (double X, double Y) Unit(RoofPoint2D start, RoofPoint2D end)
    {
        var length = start.DistanceTo(end);
        return ((end.X - start.X) / length, (end.Y - start.Y) / length);
    }

    private static RoofPoint2D Offset(RoofPoint2D point, (double X, double Y) axis, double delta) =>
        new(point.X + axis.X * delta, point.Y + axis.Y * delta);
}
