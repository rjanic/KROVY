using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class HipRoofGeometrySolverTests
{
    [Theory]
    [InlineData(0d)]
    [InlineData(90d)]
    [InlineData(37d)]
    [InlineData(143d)]
    public void Rectangle_HasCenteredMathematicalRidgeAndFourPlanes(double angle)
    {
        var geometry = Geometry(Rectangle(10000, 6000, angle), Direction(angle));
        Assert.Equal(RoofKind.Hip, geometry.Kind);
        Assert.False(geometry.IsPyramidal);
        Assert.Null(geometry.Apex);
        var ridge = Assert.IsType<RoofSegment3D>(geometry.Ridge);
        var axis = geometry.OrientationDirection;
        var rise = 3000d * Math.Tan(Math.PI / 6d);
        AssertPoint(new(250 - 2000 * axis.X, -900 - 2000 * axis.Y, rise), ridge.Start);
        AssertPoint(new(250 + 2000 * axis.X, -900 + 2000 * axis.Y, rise), ridge.End);
        Assert.Equal(4000d, geometry.RidgeLengthMm, 7);
        Assert.Equal(rise, geometry.RiseMm, 7);
        Assert.Equal(new[] { 4, 3, 4, 3 }, geometry.Faces.Select(face => face.BoundaryPoints.Count));
        Assert.All(geometry.Faces, face => Assert.Equal(30d, face.SlopeDegrees, 8));
        AssertTopologyAndPlanes(geometry, 10000d * 6000d);
    }

    [Theory]
    [InlineData(5d)]
    [InlineData(25d)]
    [InlineData(75d)]
    public void EqualPitchPlaneIntersections_UseHalfWidthInsetAtBothEnds(double slope)
    {
        var geometry = Geometry(Rectangle(14000, 4000), Direction(0), slope);
        var ridge = geometry.Ridge!;
        var tangent = Math.Tan(slope * Math.PI / 180d);
        Assert.Equal(10000d, ridge.LengthMm, 8);
        Assert.Equal(2000d * tangent, geometry.RiseMm, 8);
        // h / tan(alpha) is the end-plane inset, independent of the chosen pitch.
        Assert.Equal(2000d, (ridge.Start.X - (250d - 7000d)), 8);
        Assert.Equal(2000d, ((250d + 7000d) - ridge.End.X), 8);
        AssertTopologyAndPlanes(geometry, 14000d * 4000d);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(37d)]
    [InlineData(90d)]
    [InlineData(143d)]
    public void Square_HasOneApexAndNoRidgeForEitherEquivalentEdgeAxis(double angle)
    {
        var vertices = Rectangle(6000, 6000, angle);
        var geometry = Geometry(vertices, Direction(angle));
        var otherAxis = Geometry(vertices, Direction(angle + 90));
        Assert.True(geometry.IsPyramidal);
        Assert.Null(geometry.Ridge);
        Assert.Equal(0d, geometry.RidgeLengthMm);
        AssertPoint(new(250, -900, 3000 * Math.Tan(Math.PI / 6)), geometry.Apex!.Value);
        Assert.All(geometry.Faces, face => Assert.Equal(3, face.BoundaryPoints.Count));
        Assert.All(geometry.Hips, hip => Assert.Equal(geometry.Apex.Value, hip.End));
        Assert.All(geometry.Faces, face => Assert.Equal(30d, face.SlopeDegrees, 8));
        AssertEquivalent(geometry, otherAxis);
        AssertTopologyAndPlanes(geometry, 36000000d);
    }

    [Theory]
    [InlineData(6000d, 0.0000004d, true)]
    [InlineData(6000d, -0.0000004d, true)]
    [InlineData(6000d, 0.000004d, false)]
    [InlineData(100000d, 0.000004d, true)]
    [InlineData(100000d, 0.00004d, false)]
    public void NearlySquare_UsesAbsoluteAndRelativeCollapseTolerance(
        double width, double delta, bool collapsed)
    {
        var geometry = Geometry(Rectangle(width + delta, width), Direction(0));
        Assert.Equal(collapsed, geometry.IsPyramidal);
        if (collapsed)
        {
            Assert.Null(geometry.Ridge);
            Assert.Equal(0d, geometry.RidgeLengthMm);
            AssertPoint(new(250, -900, Math.Min(width + delta, width) / 2 * Math.Tan(Math.PI / 6)),
                geometry.Apex!.Value);
            AssertEquivalent(geometry, Geometry(Rectangle(width + delta, width), Direction(90)));
        }
        else
        {
            Assert.NotNull(geometry.Ridge);
            Assert.Equal(delta, geometry.RidgeLengthMm, 8);
        }
        AssertTopologyAndPlanes(geometry, (width + delta) * width);
    }

    [Theory]
    [InlineData(10000d, 6000d, 0d)]
    [InlineData(10000d, 6000d, 37d)]
    [InlineData(6000d, 6000d, 0d)]
    [InlineData(6000d, 6000d, 37d)]
    [InlineData(6000.0000004d, 6000d, 37d)]
    public void WindingCyclicStartsAndHalfTurnDirection_KeepCanonicalOutput(
        double length, double width, double angle)
    {
        var vertices = Rectangle(length, width, angle);
        var baseline = Geometry(vertices, Direction(angle));
        foreach (var reversed in new[] { false, true })
        {
            var ordered = reversed ? vertices.Reverse().ToArray() : vertices;
            for (var start = 0; start < 4; start++)
            {
                var shifted = Enumerable.Range(0, 4).Select(i => ordered[(start + i) % 4]).ToArray();
                AssertEquivalent(baseline, Geometry(shifted, Direction(angle + 180)));
            }
        }
        // Direction noise within the existing angular tolerance snaps to the same axis.
        AssertEquivalent(baseline, Geometry(vertices, Direction(angle + 1e-10)));
    }

    [Fact]
    public void AxisAlignedOrdering_IsCounterClockwiseFromNegativeTransverseEave()
    {
        var geometry = Geometry(Rectangle(10000, 6000), Direction(0));
        var corners = Rectangle(10000, 6000).Select(p => new RoofPoint3D(p.X, p.Y, 0)).ToArray();
        Assert.Equal(new[] { 0, 1, 2, 3 }, geometry.Faces.Select(face => face.Index));
        Assert.Equal(corners, geometry.Hips.Select(hip => hip.Start));
        Assert.Equal(new[] { geometry.Ridge!.Start, geometry.Ridge.End, geometry.Ridge.End, geometry.Ridge.Start },
            geometry.Hips.Select(hip => hip.End));
    }

    [Fact]
    public void Signature_IsInvariantUnderCultureAndRepeatedSolving()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var baseline = Geometry(Rectangle(10000, 6000, 37), Direction(37));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sk-SK");
            for (var i = 0; i < 10; i++)
            {
                AssertEquivalent(baseline, Geometry(Rectangle(10000, 6000, 37), Direction(217)));
            }
            Assert.NotEqual(baseline.Signature,
                Geometry(Rectangle(10001, 6000, 37), Direction(37)).Signature);
            Assert.NotEqual(baseline.Signature,
                Geometry(Rectangle(10000, 6000, 37), Direction(37), 31).Signature);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(90d)]
    [InlineData(100d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidSlope_IsRejected(double slope) =>
        AssertInvalid(Solve(Rectangle(10000, 6000), new(slope, Direction(0))),
            SimpleGableRoofGeometryError.InvalidSlope);

    [Fact]
    public void MissingSlope_IsRejected() =>
        AssertInvalid(Solve(Rectangle(10000, 6000), new(RidgeDirection: Direction(0))),
            SimpleGableRoofGeometryError.InvalidSlope);

    [Fact]
    public void MissingDirection_IsInferredButInvalidLegacyRectangleConstraintsAreRejected()
    {
        Assert.True(Solve(Rectangle(10000, 6000), new(30)).IsValid);
        foreach (var direction in new RoofDirection2D?[] { default(RoofDirection2D), Direction(45), Direction(90) })
        {
            AssertInvalid(Solve(Rectangle(10000, 6000), new(30, direction)),
                SimpleGableRoofGeometryError.RidgeDirectionCannotBeResolved);
        }
        AssertInvalid(Solve(Rectangle(6000, 6000.000004), new(30, Direction(0))),
            SimpleGableRoofGeometryError.RidgeDirectionCannotBeResolved);
    }

    [Theory]
    [InlineData(35d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void UnequalOrInvalidSecondSlope_IsRejected(double slope) =>
        AssertInvalid(Solve(Rectangle(10000, 6000), new(30, Direction(0), Face1SlopeDegrees: slope)),
            SimpleGableRoofGeometryError.InvalidSlope);

    [Theory]
    [InlineData(1d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void UnequalOrNonFiniteEaves_AreRejected(double difference) =>
        AssertInvalid(Solve(Rectangle(10000, 6000), new(30, Direction(0), EaveHeightDifferenceMm: difference)),
            SimpleGableRoofGeometryError.InvalidEaveHeightDifference);

    [Fact]
    public void PreviouslyRejectedConvexFootprints_NowHaveOneFacePerSourceEdge()
    {
        var polygons = new RoofPoint2D[][]
        {
            [new(0, 0), new(10000, 0), new(9000, 6000), new(0, 6000)],
            [new(0, 0), new(10000, 0), new(0, 6000)],
            [new(0, 0), new(5000, 0), new(10000, 3000), new(5000, 6000), new(0, 3000)],
        };
        foreach (var polygon in polygons)
        {
            var result = Solve(polygon, new(30));
            Assert.True(result.IsValid, result.Error.ToString());
            Assert.Equal(polygon.Length, Assert.IsType<HipRoofGeometry>(result.Geometry).Faces.Count);
        }
    }

    [Fact]
    public void MinimumDimension_IsRejectedBySolver()
    {
        AssertInvalid(Solve([new(0, 0), new(100, 0), new(100, 0.01), new(0, 0.01)], new(30, Direction(0))),
            SimpleGableRoofGeometryError.DegenerateDimensions);
    }

    [Fact]
    public void OpenFootprint_IsRejectedBeforeRoofDefinitionCanBeCreated()
    {
        var result = RoofFootprintValidator.Validate(new(Rectangle(10000, 6000), false));
        Assert.False(result.IsValid);
        Assert.Null(result.Footprint);
        Assert.Equal(RoofValidationError.OpenLoop, result.Error);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteCoordinates_AreRejectedAtFootprintBoundary(double coordinate)
    {
        var vertices = Rectangle(10000, 6000);
        vertices[0] = new(coordinate, vertices[0].Y);
        var result = RoofFootprintValidator.Validate(new(vertices, true));
        Assert.False(result.IsValid);
        Assert.Null(result.Footprint);
        Assert.Equal(RoofValidationError.NonFiniteCoordinate, result.Error);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(0.001d)]
    public void ZeroOrSubminimumWidth_CannotProduceAValidatedFootprint(double width)
    {
        var result = RoofFootprintValidator.Validate(new(Rectangle(10000, width), true));
        Assert.False(result.IsValid);
        Assert.Null(result.Footprint);
    }

    [Fact]
    public void SignedCoordinatesAndReversedExtents_AreNotNegativeDimensions()
    {
        // Input dimensions are edge distances, not signed width/height fields.
        var vertices = Rectangle(10000, 6000).Select(p => new RoofPoint2D(-p.X - 20000, -p.Y - 20000)).ToArray();
        var geometry = Geometry(vertices, Direction(180));
        Assert.Equal(4000d, geometry.RidgeLengthMm, 8);
        AssertTopologyAndPlanes(geometry, 60000000d);
    }

    [Fact]
    public void OverflowingGeometry_FailsDeterministically()
    {
        // Finite coordinates can still overflow edge/area arithmetic in the input validator.
        var input = new RoofFootprintInput(
            [new(0, 0), new(1e200, 0), new(1e200, 1e200), new(0, 1e200)], true);
        var validated = RoofFootprintValidator.Validate(input);
        if (validated.IsValid)
        {
            AssertInvalid(HipRoofGeometrySolver.Solve(new(validated.Footprint!, new(30, Direction(0)), RoofKind.Hip)),
                SimpleGableRoofGeometryError.NonFiniteGeometry);
        }
        else
        {
            Assert.Null(validated.Footprint);
        }
    }

    [Fact]
    public void NullAndWrongKind_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => HipRoofGeometrySolver.Solve(null!));
        var footprint = Validate(Rectangle(10000, 6000));
        AssertInvalid(HipRoofGeometrySolver.Solve(new(footprint, new(30, Direction(0)), RoofKind.SimpleGable)),
            SimpleGableRoofGeometryError.InvalidRoofKind);
    }

    [Fact]
    public void SharedDispatch_ExposesHipWithoutEnablingPersistenceOrDisplay()
    {
        var vertices = Rectangle(10000, 6000);
        var definition = new RoofDefinition(Validate(vertices), new(30, Direction(0)), RoofKind.Hip);
        var direct = HipRoofGeometrySolver.Solve(definition);
        var dispatched = RoofGeometrySolver.Solve(definition);
        Assert.True(dispatched.IsValid);
        var geometry = Assert.IsType<HipRoofGeometry>(dispatched.Geometry);
        Assert.Equal(direct.Geometry!.Signature, geometry.Signature);
        Assert.Throws<ArgumentException>(() => RoofDefinitionPersistence.Create(new(vertices, true), definition.Footprint, geometry));
        Assert.False(RoofWireframe.TryGetTopology(RoofKind.Hip, out _));
        Assert.Throws<ArgumentException>(() => RoofWireframe.Create(geometry, 0));
    }

    [Fact]
    public void ExistingEnumValuesAndSchema_RemainStableAndHipCannotBeEncoded()
    {
        Assert.Equal(1, (int)RoofKind.SimpleGable);
        Assert.Equal(2, (int)RoofKind.AsymmetricGable);
        Assert.Equal(3, (int)RoofKind.Monopitch);
        Assert.Equal(4, (int)RoofKind.Hip);
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
        for (var schema = 1; schema <= RoofDefinitionDataSchema.CurrentVersion; schema++)
        {
            var data = new RoofDefinitionData(schema, RoofKind.Hip, 30);
            Assert.False(RoofDefinitionDataCodec.TryValidate(data, out var error));
            Assert.Equal(RoofDefinitionDataDecodeError.UnsupportedRoofKind, error);
            Assert.Throws<ArgumentException>(() => RoofDefinitionDataCodec.Encode(data));
        }
        const string currentGable = "5|SimpleGable|30|30|0|Edge01|4|CCW|10000|6000|Locked|";
        Assert.True(RoofDefinitionDataCodec.TryDecode(currentGable, out var original, out _));
        Assert.Equal(currentGable, RoofDefinitionDataCodec.Encode(original!));
        Assert.False(RoofDefinitionDataCodec.TryDecode(currentGable.Replace("SimpleGable", "Hip"), out _, out var decodeError));
        Assert.Equal(RoofDefinitionDataDecodeError.UnsupportedRoofKind, decodeError);
    }

    [Fact]
    public void CoreAssembly_HasNoCadOrUiDependencies()
    {
        var forbidden = new[] { "Autodesk", "AcMgd", "AcDbMgd", "AcCoreMgd", "Brics", "ZwSoft", "ZWCAD", "ODA", "Teigha", "PresentationFramework", "WindowsBase" };
        foreach (var reference in typeof(HipRoofGeometrySolver).Assembly.GetReferencedAssemblies())
        {
            Assert.DoesNotContain(forbidden, name => reference.Name!.Contains(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void AssertTopologyAndPlanes(HipRoofGeometry geometry, double footprintArea)
    {
        Assert.Equal(4, geometry.Faces.Count);
        Assert.Equal(4, geometry.Hips.Count);
        var area = 0d;
        foreach (var face in geometry.Faces)
        {
            var points = face.BoundaryPoints;
            Assert.Equal(points.Count, points.Distinct().Count());
            Assert.All(points, point => Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z)));
            var a = Subtract(points[1], points[0]);
            var b = Subtract(points[2], points[0]);
            var normal = new RoofPoint3D(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            var normalLength = Math.Sqrt(Dot(normal, normal));
            Assert.True(normal.Z > 0);
            Assert.Equal(face.SlopeDegrees, Math.Atan2(Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y), normal.Z) * 180 / Math.PI, 7);
            foreach (var point in points)
            {
                Assert.InRange(Math.Abs(Dot(normal, Subtract(point, points[0]))) / normalLength, 0, 1e-7);
            }
            for (var i = 1; i < points.Count - 1; i++)
            {
                var p = Subtract(points[i], points[0]);
                var q = Subtract(points[i + 1], points[0]);
                area += (p.X * q.Y - p.Y * q.X) / 2;
            }
            Assert.Equal(face.Eave.Start, geometry.Hips[face.Index].Start);
            Assert.Equal(0d, face.Eave.Start.Z);
            Assert.Equal(0d, face.Eave.End.Z);
        }
        Assert.InRange(Math.Abs(area - footprintArea), 0, Math.Max(1e-5, footprintArea * 1e-12));
        Assert.Equal(geometry.Faces[0].SlopeDegrees, geometry.Faces[2].SlopeDegrees, 8);
        Assert.Equal(geometry.Faces[1].SlopeDegrees, geometry.Faces[3].SlopeDegrees, 8);
        Assert.All(geometry.Hips, hip => Assert.True(double.IsFinite(hip.LengthMm) && hip.LengthMm > 0));
    }

    private static void AssertEquivalent(HipRoofGeometry expected, HipRoofGeometry actual)
    {
        Assert.Equal(expected.Signature, actual.Signature);
        Assert.Equal(expected.IsPyramidal, actual.IsPyramidal);
        Assert.Equal(expected.OrientationDirection, actual.OrientationDirection);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(expected.Faces[i].Index, actual.Faces[i].Index);
            Assert.Equal(expected.Faces[i].BoundaryPoints.Count, actual.Faces[i].BoundaryPoints.Count);
            for (var j = 0; j < expected.Faces[i].BoundaryPoints.Count; j++)
            {
                AssertPoint(expected.Faces[i].BoundaryPoints[j], actual.Faces[i].BoundaryPoints[j]);
            }
            AssertPoint(expected.Hips[i].Start, actual.Hips[i].Start);
            AssertPoint(expected.Hips[i].End, actual.Hips[i].End);
        }
    }

    private static void AssertPoint(RoofPoint3D expected, RoofPoint3D actual) =>
        Assert.InRange(expected.DistanceTo(actual), 0d, 1e-7d);

    private static RoofPoint3D Subtract(RoofPoint3D a, RoofPoint3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static double Dot(RoofPoint3D a, RoofPoint3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static void AssertInvalid(RoofGeometryResult result, SimpleGableRoofGeometryError error)
    {
        Assert.False(result.IsValid);
        Assert.Null(result.Geometry);
        Assert.Equal(error, result.Error);
    }

    private static HipRoofGeometry Geometry(RoofPoint2D[] vertices, RoofDirection2D direction, double slope = 30)
    {
        var result = Solve(vertices, new(slope, direction));
        Assert.True(result.IsValid, result.Error.ToString());
        Assert.Equal(SimpleGableRoofGeometryError.None, result.Error);
        return Assert.IsType<HipRoofGeometry>(result.Geometry);
    }

    private static RoofGeometryResult Solve(RoofPoint2D[] vertices, RoofParameters parameters) =>
        HipRoofGeometrySolver.Solve(new(Validate(vertices), parameters, RoofKind.Hip));

    private static RoofFootprint Validate(RoofPoint2D[] vertices)
    {
        var validated = RoofFootprintValidator.Validate(new(vertices, true));
        Assert.True(validated.IsValid, validated.Error.ToString());
        return validated.Footprint!;
    }

    private static RoofDirection2D Direction(double degrees)
    {
        var angle = degrees * Math.PI / 180;
        Assert.True(RoofDirection2D.TryCreate(Math.Cos(angle), Math.Sin(angle), out var direction));
        return direction;
    }

    private static RoofPoint2D[] Rectangle(double length, double width, double degrees = 0)
    {
        var axis = Direction(degrees);
        return new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }.Select(sign => new RoofPoint2D(
            250 + sign.Item1 * length / 2 * axis.X - sign.Item2 * width / 2 * axis.Y,
            -900 + sign.Item1 * length / 2 * axis.Y + sign.Item2 * width / 2 * axis.X)).ToArray();
    }
}
