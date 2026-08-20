using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class SimpleGableRoofResizeTests
{
    [Fact]
    public void AxisAlignedGableEndResize_ChangesRidgeAndEaveLengthAndKeepsSpan()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var resized = StretchGableEnd(original, 2000d);
        var before = Restore(original, data);
        var after = Restore(resized, data);

        Assert.Equal(RoofSourceChangeKind.SupportedResize, Classify(resized, data).Kind);
        Assert.Equal(before.SlopeDegrees, after.SlopeDegrees);
        Assert.Equal(before.RunMm, after.RunMm, 7);
        Assert.Equal(before.RidgeLengthMm + 2000d, after.RidgeLengthMm, 7);
        AssertParallel(after.RidgeDirection, EdgeDirection(resized, 0));
        AssertFixedGable(original, resized);
    }

    [Fact]
    public void AxisAlignedEaveSideResize_KeepsRidgeCenteredAndRecomputesRise()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var resized = StretchEaveSide(original, 2000d);
        var before = Restore(original, data);
        var after = Restore(resized, data);

        Assert.Equal(RoofSourceChangeKind.SupportedResize, Classify(resized, data).Kind);
        Assert.Equal(before.SlopeDegrees, after.SlopeDegrees);
        Assert.Equal(before.RidgeLengthMm, after.RidgeLengthMm, 7);
        Assert.Equal(before.RunMm + 1000d, after.RunMm, 7);
        Assert.Equal(after.RunMm * Math.Tan(35d * Math.PI / 180d), after.RiseMm, 7);
        AssertRidgeCentered(after);
        AssertParallel(after.RidgeDirection, EdgeDirection(resized, 0));
    }

    [Fact]
    public void RotatedThirtyDegreeGableEndResize_PreservesFamilyAndRectangle()
    {
        var original = Transform(Rectangle(), 30d, 400d, -250d);
        var data = Create(original, 33d, RoofRidgeEdgeFamily.SourceEdge01);
        var resized = StretchGableEnd(original, 1800d);
        var after = Restore(resized, data);

        Assert.Equal(33d, after.SlopeDegrees);
        AssertParallel(after.RidgeDirection, EdgeDirection(resized, 0));
        Assert.Equal(Restore(original, data).RunMm, after.RunMm, 6);
        AssertFinite(after);
    }

    [Fact]
    public void RotatedThirtyDegreeEaveSideResize_PreservesFamilyAndCentersRidge()
    {
        var original = Transform(Rectangle(), 30d, 400d, -250d);
        var data = Create(original, 33d, RoofRidgeEdgeFamily.SourceEdge01);
        var resized = StretchEaveSide(original, 1800d);
        var after = Restore(resized, data);

        Assert.Equal(33d, after.SlopeDegrees);
        AssertParallel(after.RidgeDirection, EdgeDirection(resized, 0));
        AssertRidgeCentered(after);
        AssertFinite(after);
    }

    [Fact]
    public void SquareToRectangle_DoesNotFlipRetainedRidgeFamily()
    {
        var square = Rectangle(8000d, 8000d);
        var data = Create(square, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var resized = StretchEaveSide(square, 5000d);
        var after = Restore(resized, data);

        AssertParallel(after.RidgeDirection, EdgeDirection(resized, 0));
        AssertPerpendicular(after.RidgeDirection, EdgeDirection(resized, 1));
        Assert.True(resized.Vertices![1].DistanceTo(resized.Vertices[2]) >
                    resized.Vertices[0].DistanceTo(resized.Vertices[1]));
    }

    [Fact]
    public void RectangularAspectRatioCrossover_DoesNotFlipRetainedRidgeFamily()
    {
        var original = Rectangle(10000d, 6000d);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge12);
        var resized = StretchEaveSide(original, 8000d);
        var after = Restore(resized, data);

        AssertParallel(after.RidgeDirection, EdgeDirection(resized, 1));
        AssertPerpendicular(after.RidgeDirection, EdgeDirection(resized, 0));
        Assert.True(resized.Vertices![1].DistanceTo(resized.Vertices[2]) >
                    resized.Vertices[0].DistanceTo(resized.Vertices[1]));
    }

    [Fact]
    public void CanonicalFirstVertexAndWinding_RemainStableAfterResize()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var resized = StretchGableEnd(original, 1250d);
        var first = RoofFootprintValidator.Validate(resized);
        var second = RoofFootprintValidator.Validate(resized);

        Assert.Equal(first.Footprint!.Signature, second.Footprint!.Signature);
        Assert.Equal(first.SourceOrientation, second.SourceOrientation);
        Assert.Equal(Restore(resized, data).Signature, Restore(resized, data).Signature);
    }

    [Fact]
    public void RepeatedEquivalentResize_IsDeterministicAndUpdatesPersistedDescriptor()
    {
        var original = Rectangle();
        var data = Create(original, 31.75d, RoofRidgeEdgeFamily.SourceEdge12);
        var resized = StretchGableEnd(original, 2000d);
        var first = Restore(resized, data);
        var second = Restore(resized, data);
        var updated = RoofDefinitionPersistence.Create(
            resized,
            Validate(resized),
            first);

        Assert.Equal(first.Signature, second.Signature);
        Assert.Equal(RoofRidgeEdgeFamily.SourceEdge12, updated.RidgeEdgeFamily);
        Assert.Equal(31.75d, updated.SlopeDegrees);
        Assert.Equal(3, updated.SchemaVersion);
        Assert.NotEqual(data.RigidFootprint!.Edge01LengthMm, updated.RigidFootprint!.Edge01LengthMm);
        Assert.Equal(
            RoofDefinitionDataCodec.Encode(updated),
            RoofDefinitionDataCodec.Encode(
                RoofDefinitionPersistence.Create(resized, Validate(resized), second)));
        Assert.Equal(
            RoofSourceChangeKind.RigidEquivalent,
            RoofDefinitionPersistence.Classify(resized, Validate(resized), updated).Kind);
    }

    [Fact]
    public void DisplayWireframeAfterResize_HasExactlySevenRoles()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var edges = SimpleGableRoofWireframe.Create(
            Restore(StretchEaveSide(original, 2000d), data),
            0d);

        Assert.Equal(7, edges.Count);
        Assert.Equal(7, edges.Select(edge => edge.Role).Distinct().Count());
        Assert.Single(edges, edge => edge.Role == RoofDisplayEdgeRole.Ridge);
        Assert.Equal(2, edges.Count(edge => edge.Role is RoofDisplayEdgeRole.Eave0 or RoofDisplayEdgeRole.Eave1));
    }

    [Fact]
    public void UnsupportedTrapezoid_IsRejectedWithoutGeometry()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var trapezoid = Input([
            new(0d, 0d), new(10000d, 0d), new(9000d, 6000d), new(1000d, 6000d)]);
        var result = RestoreResult(trapezoid, data);

        Assert.Equal(RoofSourceChangeKind.Unsupported, Classify(trapezoid, data).Kind);
        Assert.False(result.IsValid);
        Assert.Null(result.Geometry);
        Assert.Equal(RoofDefinitionRestoreError.StaleFootprint, result.Error);
    }

    [Fact]
    public void UnsupportedSkewedParallelogram_IsRejectedWithoutGeometry()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var skewed = Input([
            new(0d, 0d), new(10000d, 0d), new(10500d, 6000d), new(500d, 6000d)]);
        var result = RestoreResult(skewed, data);

        Assert.False(result.IsValid);
        Assert.Null(result.Geometry);
        Assert.Equal(RoofDefinitionRestoreError.StaleFootprint, result.Error);
        Assert.Equal(RoofSourceChangeKind.Unsupported, Classify(skewed, data).Kind);
    }

    [Fact]
    public void DegenerateRectangle_IsRejectedWithoutGeometry()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var degenerate = Input([
            new(0d, 0d), new(10000d, 0d), new(10000d, 0.001d), new(0d, 0.001d)]);
        var validation = RoofFootprintValidator.Validate(degenerate);
        if (validation.IsValid && validation.Footprint is not null)
        {
            var result = RoofDefinitionPersistence.Restore(degenerate, validation.Footprint, data);
            Assert.False(result.IsValid);
            Assert.Null(result.Geometry);
        }
        else
        {
            Assert.False(validation.IsValid);
        }
    }

    [Fact]
    public void FutureSchema_RemainsRejected()
    {
        Assert.False(RoofDefinitionDataCodec.TryDecode(
            "4|SimpleGable|35|Edge01|4|CCW|10000|6000",
            out _,
            out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.UnsupportedFutureSchema, error);
        Assert.Equal(3, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(7, AcKrovy.Core.Models.TimberElementDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
        Assert.Equal(1, AcKrovy.Core.Models.TimberDrawingSettings.DrawingSettingsSchemaVersion);
    }

    [Fact]
    public void RigidMove_IsNotClassifiedAsResize()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var moved = Transform(original, 0d, 1500d, -800d);
        Assert.Equal(RoofSourceChangeKind.RigidEquivalent, Classify(moved, data).Kind);
        Assert.True(RestoreResult(moved, data).IsValid);
    }

    [Fact]
    public void GeneratedLayoutFreshness_DetectsStaleSignatureAfterResize()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var before = Restore(original, data);
        var after = Restore(StretchGableEnd(original, 2000d), data);
        var layout = SimpleGableRafterLayoutSolver.Solve(
            before,
            new RafterLayoutParameters(800d, 80d)).Layout!;

        Assert.True(RoofGeneratedTimberFreshness.IsLayoutCurrent(layout.Signature, before.Signature));
        Assert.False(RoofGeneratedTimberFreshness.IsLayoutCurrent(layout.Signature, after.Signature));
        Assert.NotEqual(before.Signature, after.Signature);
    }

    [Theory]
    [InlineData("STRETCH", true, false)]
    [InlineData("_STRETCH", true, false)]
    [InlineData("GRIP_STRETCH", true, false)]
    [InlineData("MOVE", false, false)]
    [InlineData("ROTATE", false, false)]
    [InlineData("U", false, true)]
    public void StretchCommands_AreUndoGroupedAndNotTreatedAsUndoRedo(
        string commandName,
        bool grouped,
        bool undoRedo)
    {
        Assert.Equal(grouped, AcKrovy.Core.Services.LiveGeometryCommandRules.IsUndoGroupingSourceCommand(commandName));
        Assert.Equal(undoRedo, AcKrovy.Core.Services.LiveGeometryCommandRules.IsUndoRedoCommand(commandName));
    }

    private static RoofSourceChangeClassification Classify(
        RoofFootprintInput source,
        RoofDefinitionData data) =>
        RoofDefinitionPersistence.Classify(source, Validate(source), data);

    private static RoofDefinitionData Create(
        RoofFootprintInput source,
        double slope,
        RoofRidgeEdgeFamily family)
    {
        var footprint = Validate(source);
        return RoofDefinitionPersistence.Create(
            source,
            footprint,
            Solve(source, footprint, slope, family));
    }

    private static SimpleGableRoofGeometry Restore(
        RoofFootprintInput source,
        RoofDefinitionData data)
    {
        var result = RestoreResult(source, data);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<SimpleGableRoofGeometry>(result.Geometry);
    }

    private static RoofDefinitionRestoreResult RestoreResult(
        RoofFootprintInput source,
        RoofDefinitionData data)
    {
        var validation = RoofFootprintValidator.Validate(source);
        if (!validation.IsValid || validation.Footprint is null)
        {
            return new RoofDefinitionRestoreResult(
                false,
                null,
                RoofDefinitionRestoreError.InvalidDefinition);
        }

        return RoofDefinitionPersistence.Restore(source, validation.Footprint, data);
    }

    private static SimpleGableRoofGeometry Solve(
        RoofFootprintInput source,
        RoofFootprint footprint,
        double slope,
        RoofRidgeEdgeFamily family)
    {
        var direction = EdgeDirection(source, family == RoofRidgeEdgeFamily.SourceEdge01 ? 0 : 1);
        var result = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(slope, direction)));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<SimpleGableRoofGeometry>(result.Geometry);
    }

    private static RoofDirection2D EdgeDirection(RoofFootprintInput source, int edgeIndex)
    {
        var vertices = source.Vertices!;
        var first = vertices[edgeIndex];
        var second = vertices[(edgeIndex + 1) % vertices.Count];
        Assert.True(RoofDirection2D.TryCreate(second.X - first.X, second.Y - first.Y, out var direction));
        return direction;
    }

    private static RoofFootprint Validate(RoofFootprintInput source)
    {
        var result = RoofFootprintValidator.Validate(source);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<RoofFootprint>(result.Footprint);
    }

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

    private static void AssertFixedGable(RoofFootprintInput original, RoofFootprintInput resized)
    {
        Assert.Equal(original.Vertices![0].X, resized.Vertices![0].X, 7);
        Assert.Equal(original.Vertices[0].Y, resized.Vertices[0].Y, 7);
        Assert.Equal(original.Vertices[3].X, resized.Vertices[3].X, 7);
        Assert.Equal(original.Vertices[3].Y, resized.Vertices[3].Y, 7);
    }

    private static void AssertRidgeCentered(SimpleGableRoofGeometry geometry)
    {
        var first = Mid(geometry.Faces[0].Eave.Start, geometry.Faces[0].Eave.End);
        var second = Mid(geometry.Faces[1].Eave.Start, geometry.Faces[1].Eave.End);
        var ridge = Mid(geometry.Ridge.Start, geometry.Ridge.End);
        Assert.Equal((first.X + second.X) / 2d, ridge.X, 6);
        Assert.Equal((first.Y + second.Y) / 2d, ridge.Y, 6);
    }

    private static RoofPoint3D Mid(RoofPoint3D first, RoofPoint3D second) =>
        new((first.X + second.X) / 2d, (first.Y + second.Y) / 2d, (first.Z + second.Z) / 2d);

    private static void AssertParallel(RoofDirection2D first, RoofDirection2D second) =>
        Assert.True(Math.Abs(first.X * second.Y - first.Y * second.X) < 1e-9d);

    private static void AssertPerpendicular(RoofDirection2D first, RoofDirection2D second) =>
        Assert.True(Math.Abs(first.X * second.X + first.Y * second.Y) < 1e-9d);

    private static void AssertFinite(SimpleGableRoofGeometry geometry)
    {
        var values = new[]
        {
            geometry.Ridge.Start.X, geometry.Ridge.Start.Y, geometry.Ridge.Start.Z,
            geometry.Ridge.End.X, geometry.Ridge.End.Y, geometry.Ridge.End.Z,
            geometry.RunMm, geometry.RiseMm, geometry.SlopeDegrees,
        };
        Assert.All(values, value => Assert.True(double.IsFinite(value)));
    }
}
