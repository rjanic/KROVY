using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class SimpleGableRoofGeometrySolverTests
{
    [Fact]
    public void AxisAlignedRectangle_ProducesCenteredRidgeAndTwoBoundedFaces()
    {
        var result = Solve(
            [P(0, 0), P(10000, 0), P(10000, 6000), P(0, 6000)],
            slopeDegrees: 30,
            directionX: 1,
            directionY: 0);

        Assert.True(result.IsValid);
        Assert.Equal(SimpleGableRoofGeometryError.None, result.Error);
        var geometry = result.Geometry!;
        AssertPoint(new RoofPoint3D(0, 3000, 3000 * Math.Tan(Math.PI / 6)), geometry.Ridge.Start);
        AssertPoint(new RoofPoint3D(10000, 3000, 3000 * Math.Tan(Math.PI / 6)), geometry.Ridge.End);
        Assert.Equal(10000d, geometry.RidgeLengthMm, 9);
        Assert.Equal(3000d, geometry.RunMm, 9);
        Assert.Equal(3000 * Math.Tan(Math.PI / 6), geometry.RiseMm, 9);
        Assert.Equal(30d, geometry.SlopeDegrees);
        Assert.Equal(2, geometry.Faces.Count);
        Assert.Equal([0, 1], geometry.Faces.Select(face => face.Index));
        Assert.All(geometry.Faces, face => Assert.Equal(4, face.BoundaryPoints.Count));
        Assert.All(geometry.Faces.SelectMany(face => face.Eave is { } eave
            ? new[] { eave.Start, eave.End }
            : []), point => Assert.Equal(0d, point.Z));
        Assert.Equal(SimpleGableRoofFaceSide.NegativeTransverse, geometry.Faces[0].Side);
        Assert.Equal(SimpleGableRoofFaceSide.PositiveTransverse, geometry.Faces[1].Side);
    }

    [Fact]
    public void AxisAlignedRectangle_FacePointOrderingIsCanonicalAndUpwardFacing()
    {
        var geometry = SolveRectangle().Geometry!;
        var rise = geometry.RiseMm;

        AssertPoints(
            [P3(0, 3000, rise), P3(0, 0, 0), P3(10000, 0, 0), P3(10000, 3000, rise)],
            geometry.Faces[0].BoundaryPoints);
        AssertPoints(
            [P3(0, 3000, rise), P3(10000, 3000, rise), P3(10000, 6000, 0), P3(0, 6000, 0)],
            geometry.Faces[1].BoundaryPoints);
        Assert.All(geometry.Faces, face => Assert.True(ImpliedNormalZ(face) > 0d));
    }

    [Fact]
    public void RectangleRotatedThirtySevenDegrees_RemainsCorrectInWorldXy()
    {
        const double angle = 37d * Math.PI / 180d;
        var axis = (X: Math.Cos(angle), Y: Math.Sin(angle));
        var transverse = (X: -axis.Y, Y: axis.X);
        var center = P(1250, -875);
        var vertices = Rectangle(center, axis, transverse, 10000, 6000);

        var geometry = Solve(vertices, 35, axis.X, axis.Y).Geometry!;

        AssertPoint(
            P3(center.X - axis.X * 5000, center.Y - axis.Y * 5000, geometry.RiseMm),
            geometry.Ridge.Start,
            0.00000001);
        AssertPoint(
            P3(center.X + axis.X * 5000, center.Y + axis.Y * 5000, geometry.RiseMm),
            geometry.Ridge.End,
            0.00000001);
        Assert.Equal(3000d, geometry.RunMm, 8);
        Assert.Equal(3000 * Math.Tan(35d * Math.PI / 180d), geometry.RiseMm, 8);
        Assert.NotEqual(geometry.Ridge.Start.Y, geometry.Ridge.End.Y);
    }

    [Fact]
    public void ReversedRequestedDirection_ProducesIdenticalCanonicalGeometry()
    {
        var positive = SolveRectangle(37, 1, 0).Geometry!;
        var negative = SolveRectangle(37, -1, 0).Geometry!;

        Assert.Equal(positive.Signature, negative.Signature);
        Assert.Equal(positive.Ridge, negative.Ridge);
        AssertFaceGeometryEqual(positive, negative);
    }

    [Fact]
    public void SquareWithExplicitDirection_IsDeterministic()
    {
        var alongX = Solve(
            [P(0, 0), P(5000, 0), P(5000, 5000), P(0, 5000)],
            40,
            1,
            0).Geometry!;
        var reversed = Solve(
            [P(5000, 5000), P(5000, 0), P(0, 0), P(0, 5000)],
            40,
            -1,
            0).Geometry!;

        Assert.Equal(alongX.Signature, reversed.Signature);
        AssertPoint(P3(0, 2500, alongX.RiseMm), alongX.Ridge.Start);
        AssertPoint(P3(5000, 2500, alongX.RiseMm), alongX.Ridge.End);
    }

    [Fact]
    public void ClockwiseCounterClockwiseAndCyclicStarts_ProduceIdenticalGeometry()
    {
        var counterClockwise = Solve(
            [P(0, 0), P(10000, 0), P(10000, 6000), P(0, 6000)], 30, 1, 0).Geometry!;
        var clockwise = Solve(
            [P(0, 6000), P(10000, 6000), P(10000, 0), P(0, 0)], 30, 1, 0).Geometry!;
        var shifted = Solve(
            [P(10000, 6000), P(0, 6000), P(0, 0), P(10000, 0)], 30, 1, 0).Geometry!;

        Assert.Equal(counterClockwise.Signature, clockwise.Signature);
        Assert.Equal(counterClockwise.Signature, shifted.Signature);
        AssertFaceGeometryEqual(counterClockwise, clockwise);
        AssertFaceGeometryEqual(counterClockwise, shifted);
    }

    [Fact]
    public void EquivalentRotatedRepresentations_ProduceIdenticalGeometry()
    {
        const double angle = 30d * Math.PI / 180d;
        var axis = (X: Math.Cos(angle), Y: Math.Sin(angle));
        var transverse = (X: -axis.Y, Y: axis.X);
        var vertices = Rectangle(P(200, 300), axis, transverse, 8000, 4000);
        var first = Solve(vertices, 25, axis.X, axis.Y).Geometry!;
        var representedDifferently = Solve(
            [vertices[2], vertices[1], vertices[0], vertices[3]],
            25,
            -axis.X,
            -axis.Y).Geometry!;

        Assert.Equal(first.Signature, representedDifferently.Signature);
        Assert.Equal(first.Ridge, representedDifferently.Ridge);
        AssertFaceGeometryEqual(first, representedDifferently);
    }

    [Fact]
    public void NonRectangularQuadrilateral_IsRejectedWithoutRepair()
    {
        var result = Solve(
            [P(0, 0), P(10000, 0), P(9000, 6000), P(0, 6000)], 30, 1, 0);

        AssertInvalid(result, SimpleGableRoofGeometryError.FootprintIsNotRectangular);
    }

    [Fact]
    public void MoreThanFourVertices_IsRejectedSpecifically()
    {
        var result = Solve(
            [P(0, 0), P(5000, 0), P(10000, 3000), P(5000, 6000), P(0, 3000)],
            30,
            1,
            0);

        AssertInvalid(result, SimpleGableRoofGeometryError.FootprintIsNotFourSided);
    }

    [Fact]
    public void FewerThanFourVertices_IsRejectedSpecifically()
    {
        var result = Solve([P(0, 0), P(10000, 0), P(0, 6000)], 30, 1, 0);

        AssertInvalid(result, SimpleGableRoofGeometryError.FootprintIsNotFourSided);
    }

    [Fact]
    public void MinimumS1EdgeDimension_IsRejectedAsDegenerateForS2()
    {
        var dimension = SimpleGableRoofGeometryTolerance.MinimumDimensionMm;
        var result = Solve(
            [P(0, 0), P(100, 0), P(100, dimension), P(0, dimension)], 30, 1, 0);

        AssertInvalid(result, SimpleGableRoofGeometryError.DegenerateDimensions);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(90d)]
    [InlineData(91d)]
    public void InvalidSlope_IsRejected(double slope)
    {
        var result = SolveRectangle(slope, 1, 0);

        AssertInvalid(result, SimpleGableRoofGeometryError.InvalidSlope);
    }

    [Fact]
    public void MissingSlope_IsRejected()
    {
        Assert.True(RoofDirection2D.TryCreate(1, 0, out var direction));
        var definition = new RoofDefinition(
            ValidFootprint([P(0, 0), P(10000, 0), P(10000, 6000), P(0, 6000)]),
            new RoofParameters(RidgeDirection: direction));

        AssertInvalid(
            SimpleGableRoofGeometrySolver.Solve(definition),
            SimpleGableRoofGeometryError.InvalidSlope);
    }

    [Fact]
    public void MissingOrInvalidDirection_IsRejected()
    {
        var footprint = ValidFootprint([P(0, 0), P(10000, 0), P(10000, 6000), P(0, 6000)]);
        var missing = new RoofDefinition(footprint, new RoofParameters(SlopeDegrees: 30));
        var defaultDirection = new RoofDefinition(
            footprint,
            new RoofParameters(30, default(RoofDirection2D)));

        AssertInvalid(
            SimpleGableRoofGeometrySolver.Solve(missing),
            SimpleGableRoofGeometryError.RidgeDirectionCannotBeResolved);
        AssertInvalid(
            SimpleGableRoofGeometrySolver.Solve(defaultDirection),
            SimpleGableRoofGeometryError.RidgeDirectionCannotBeResolved);
    }

    [Fact]
    public void DirectionNotParallelToRectangleAxis_IsRejected()
    {
        var result = SolveRectangle(30, 1, 1);

        AssertInvalid(result, SimpleGableRoofGeometryError.RidgeDirectionCannotBeResolved);
    }

    [Fact]
    public void RepeatedSolve_IsBitwiseDeterministic()
    {
        var expected = SolveRectangle(33, 1, 0).Geometry!;

        for (var iteration = 0; iteration < 100; iteration++)
        {
            var actual = SolveRectangle(33, 1, 0).Geometry!;
            Assert.Equal(expected.Signature, actual.Signature);
            Assert.Equal(expected.Ridge, actual.Ridge);
            AssertFaceGeometryEqual(expected, actual);
        }
    }

    [Fact]
    public void SuccessfulGeometry_ContainsOnlyFinitePublicCoordinatesAndDimensions()
    {
        var geometry = SolveRectangle(89.9, 0, 1).Geometry!;
        var values = new[]
        {
            geometry.RunMm,
            geometry.RiseMm,
            geometry.RidgeLengthMm,
            geometry.Ridge.Start.X,
            geometry.Ridge.Start.Y,
            geometry.Ridge.Start.Z,
            geometry.Ridge.End.X,
            geometry.Ridge.End.Y,
            geometry.Ridge.End.Z,
        }.Concat(geometry.Faces.SelectMany(face =>
            face.BoundaryPoints.SelectMany(point => new[] { point.X, point.Y, point.Z })));

        Assert.All(values, value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public void TolerancePolicy_IsCentralizedAndHasPhysicalBounds()
    {
        Assert.True(SimpleGableRoofGeometryTolerance.CoordinateToleranceMm > 0d);
        Assert.True(SimpleGableRoofGeometryTolerance.RelativeLengthTolerance > 0d);
        Assert.True(SimpleGableRoofGeometryTolerance.AngularTolerance > 0d);
        Assert.True(SimpleGableRoofGeometryTolerance.MinimumDimensionMm > 0d);
        Assert.Equal(0d, SimpleGableRoofGeometryTolerance.MinimumSlopeDegrees);
        Assert.Equal(90d, SimpleGableRoofGeometryTolerance.MaximumSlopeDegrees);
    }

    private static SimpleGableRoofGeometryResult SolveRectangle(
        double slopeDegrees = 30,
        double directionX = 1,
        double directionY = 0) =>
        Solve(
            [P(0, 0), P(10000, 0), P(10000, 6000), P(0, 6000)],
            slopeDegrees,
            directionX,
            directionY);

    private static SimpleGableRoofGeometryResult Solve(
        IReadOnlyList<RoofPoint2D> vertices,
        double slopeDegrees,
        double directionX,
        double directionY)
    {
        Assert.True(RoofDirection2D.TryCreate(directionX, directionY, out var direction));
        return SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            ValidFootprint(vertices),
            new RoofParameters(slopeDegrees, direction)));
    }

    private static RoofFootprint ValidFootprint(IReadOnlyList<RoofPoint2D> vertices)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(vertices, IsClosed: true));
        Assert.True(validation.IsValid, $"S1 fixture failed: {validation.Error}");
        return validation.Footprint!;
    }

    private static RoofPoint2D[] Rectangle(
        RoofPoint2D center,
        (double X, double Y) axis,
        (double X, double Y) transverse,
        double length,
        double width)
    {
        RoofPoint2D Corner(double along, double across) => new(
            center.X + axis.X * along + transverse.X * across,
            center.Y + axis.Y * along + transverse.Y * across);
        return
        [
            Corner(-length / 2, -width / 2),
            Corner(length / 2, -width / 2),
            Corner(length / 2, width / 2),
            Corner(-length / 2, width / 2),
        ];
    }

    private static double ImpliedNormalZ(SimpleGableRoofFace face)
    {
        var first = face.BoundaryPoints[0];
        var second = face.BoundaryPoints[1];
        var third = face.BoundaryPoints[2];
        return (second.X - first.X) * (third.Y - first.Y) -
               (second.Y - first.Y) * (third.X - first.X);
    }

    private static void AssertFaceGeometryEqual(
        SimpleGableRoofGeometry expected,
        SimpleGableRoofGeometry actual)
    {
        Assert.Equal(expected.Faces.Count, actual.Faces.Count);
        for (var index = 0; index < expected.Faces.Count; index++)
        {
            Assert.Equal(expected.Faces[index].Index, actual.Faces[index].Index);
            Assert.Equal(expected.Faces[index].Side, actual.Faces[index].Side);
            Assert.Equal(expected.Faces[index].Eave, actual.Faces[index].Eave);
            Assert.Equal(expected.Faces[index].BoundaryPoints, actual.Faces[index].BoundaryPoints);
        }
    }

    private static void AssertPoints(
        IReadOnlyList<RoofPoint3D> expected,
        IReadOnlyList<RoofPoint3D> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            AssertPoint(expected[index], actual[index]);
        }
    }

    private static void AssertPoint(
        RoofPoint3D expected,
        RoofPoint3D actual,
        double tolerance = 0.000000001)
    {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
        Assert.Equal(expected.Z, actual.Z, tolerance);
    }

    private static void AssertInvalid(
        SimpleGableRoofGeometryResult result,
        SimpleGableRoofGeometryError error)
    {
        Assert.False(result.IsValid);
        Assert.Null(result.Geometry);
        Assert.Equal(error, result.Error);
    }

    private static RoofPoint2D P(double x, double y) => new(x, y);

    private static RoofPoint3D P3(double x, double y, double z) => new(x, y, z);
}
