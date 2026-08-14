using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofFootprintFoundationTests
{
    [Fact]
    public void CounterClockwiseRectangle_IsAcceptedWithoutChangingOrientation()
    {
        var result = Validate(P(0, 0), P(10000, 0), P(10000, 6000), P(0, 6000));

        Assert.True(result.IsValid);
        Assert.Equal(RoofPolygonOrientation.CounterClockwise, result.SourceOrientation);
        Assert.Equal(RoofPolygonOrientation.CounterClockwise, result.Footprint!.Orientation);
    }

    [Fact]
    public void ClockwiseRectangle_NormalizesToSameCanonicalSequence()
    {
        var counterClockwise = Validate(P(0, 0), P(10000, 0), P(10000, 6000), P(0, 6000));
        var clockwise = Validate(P(0, 0), P(0, 6000), P(10000, 6000), P(10000, 0));

        Assert.True(clockwise.IsValid);
        Assert.Equal(RoofPolygonOrientation.Clockwise, clockwise.SourceOrientation);
        Assert.Equal(counterClockwise.Footprint!.Signature, clockwise.Footprint!.Signature);
        Assert.Equal(counterClockwise.Footprint.Vertices, clockwise.Footprint.Vertices);
    }

    [Fact]
    public void CanonicalFirstVertex_IgnoresMeasuredRigidTransformResidueOnNominallyEqualX()
    {
        const double measuredHostMaxComponentDeltaMm = 2.9103830456733704e-11d;
        var result = Validate(
            P(2000d, 1000d),
            P(2000d, 9000d),
            P(-6000d - measuredHostMaxComponentDeltaMm, 9000d),
            P(-6000d, 1000d));

        Assert.True(result.IsValid);
        Assert.Equal(P(-6000d, 1000d), result.Footprint!.Vertices[0]);
    }

    [Fact]
    public void IrregularConvexPolygon_IsAccepted()
    {
        var result = Validate(P(1, 1), P(8, 0), P(11, 5), P(7, 9), P(0, 6));

        Assert.True(result.IsValid);
        Assert.Equal(5, result.Footprint!.Vertices.Count);
    }

    [Fact]
    public void SimpleConcavePolygon_IsAccepted()
    {
        var result = Validate(P(0, 0), P(8, 0), P(8, 8), P(4, 4), P(0, 8));

        Assert.True(result.IsValid);
        Assert.Equal(48d, result.Footprint!.AreaMm2, 9);
    }

    [Fact]
    public void RepeatedClosingVertex_IsRemovedDeterministically()
    {
        var result = Validate(P(0, 0), P(10, 0), P(10, 5), P(0, 5), P(0, 0));

        Assert.True(result.IsValid);
        Assert.Equal(4, result.Footprint!.Vertices.Count);
        Assert.Equal(P(0, 0), result.Footprint.Vertices[0]);
    }

    [Fact]
    public void SourceOpenWithExactRepeatedClosingVertex_IsEffectivelyClosed()
    {
        var result = ValidateSource(
            isClosed: false,
            P(0, 0), P(10, 0), P(10, 5), P(0, 5), P(0, 0));

        Assert.True(result.IsValid);
        Assert.Equal(4, result.Footprint!.Vertices.Count);
    }

    [Fact]
    public void SourceOpenWithinClosingTolerance_IsEffectivelyClosed()
    {
        var withinTolerance = RoofFootprintValidator.ClosingPointToleranceMm * 0.5d;
        var result = ValidateSource(
            isClosed: false,
            P(0, 0), P(10, 0), P(10, 5), P(0, 5), P(0, withinTolerance));

        Assert.True(result.IsValid);
        Assert.Equal(4, result.Footprint!.Vertices.Count);
    }

    [Fact]
    public void SourceOpenJustOutsideClosingTolerance_IsRejectedAsOpen()
    {
        var outsideTolerance = RoofFootprintValidator.ClosingPointToleranceMm * 1.01d;
        var result = ValidateSource(
            isClosed: false,
            P(0, 0), P(10, 0), P(10, 5), P(0, 5), P(0, outsideTolerance));

        AssertInvalid(result, RoofValidationError.OpenLoop);
    }

    [Fact]
    public void EffectiveClosedClockwise_PreservesSourceAndCanonicalOrientations()
    {
        var result = ValidateSource(
            isClosed: false,
            P(0, 0), P(0, 5), P(10, 5), P(10, 0), P(0, 0));

        Assert.True(result.IsValid);
        Assert.Equal(RoofPolygonOrientation.Clockwise, result.SourceOrientation);
        Assert.Equal(RoofPolygonOrientation.CounterClockwise, result.Footprint!.Orientation);
    }

    [Fact]
    public void EffectiveClosedCounterClockwise_PreservesSourceAndCanonicalOrientations()
    {
        var result = ValidateSource(
            isClosed: false,
            P(0, 0), P(10, 0), P(10, 5), P(0, 5), P(0, 0));

        Assert.True(result.IsValid);
        Assert.Equal(RoofPolygonOrientation.CounterClockwise, result.SourceOrientation);
        Assert.Equal(RoofPolygonOrientation.CounterClockwise, result.Footprint!.Orientation);
    }

    [Fact]
    public void EffectiveClosedAndNativeClosedGeometry_HaveEqualCanonicalResult()
    {
        var nativeClosed = ValidateSource(
            isClosed: true,
            P(0, 0), P(10, 0), P(10, 5), P(0, 5));
        var effectiveClosed = ValidateSource(
            isClosed: false,
            P(0, 0), P(10, 0), P(10, 5), P(0, 5), P(0, 0));

        Assert.True(nativeClosed.IsValid);
        Assert.True(effectiveClosed.IsValid);
        Assert.Equal(nativeClosed.Footprint!.Vertices, effectiveClosed.Footprint!.Vertices);
        Assert.Equal(nativeClosed.Footprint.Edges, effectiveClosed.Footprint.Edges);
        Assert.Equal(nativeClosed.Footprint.AreaMm2, effectiveClosed.Footprint.AreaMm2);
        Assert.Equal(nativeClosed.Footprint.Signature, effectiveClosed.Footprint.Signature);
    }

    [Fact]
    public void NearClosingVertex_IsNotSilentlyRepaired()
    {
        var result = Validate(P(0, 0), P(10, 0), P(10, 5), P(0, 5), P(0, 0.005));

        AssertInvalid(result, RoofValidationError.ZeroLengthEdge);
    }

    [Fact]
    public void CyclicSourceStart_DoesNotChangeSignatureOrEdges()
    {
        var first = Validate(P(0, 0), P(10, 0), P(10, 5), P(0, 5));
        var shifted = Validate(P(10, 5), P(0, 5), P(0, 0), P(10, 0));

        Assert.Equal(first.Footprint!.Signature, shifted.Footprint!.Signature);
        Assert.Equal(first.Footprint.Edges, shifted.Footprint.Edges);
    }

    [Fact]
    public void Geometry_ProvidesAreaBoundsCentroidAndClosingEdge()
    {
        var footprint = Validate(P(0, 0), P(10, 0), P(10, 6), P(0, 6)).Footprint!;

        Assert.True(footprint.IsClosed);
        Assert.Equal(60d, footprint.SignedAreaMm2, 9);
        Assert.Equal(60d, footprint.AreaMm2, 9);
        Assert.Equal(new RoofBoundingBox2D(0, 0, 10, 6), footprint.Bounds);
        Assert.Equal(P(5, 3), footprint.Centroid);
        Assert.Equal(4, footprint.Edges.Count);
        Assert.Equal(3, footprint.Edges[3].Index);
        Assert.Equal(P(0, 6), footprint.Edges[3].Start);
        Assert.Equal(P(0, 0), footprint.Edges[3].End);
        Assert.Equal(6d, footprint.Edges[3].LengthMm, 9);
    }

    [Fact]
    public void OpenInputWithoutCoincidentEndpoints_IsRejected()
    {
        var result = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [P(0, 0), P(10, 0), P(0, 10)],
            IsClosed: false));

        AssertInvalid(result, RoofValidationError.OpenLoop);
    }

    [Fact]
    public void FewerThanThreeUniqueVertices_IsRejected()
    {
        var result = Validate(P(0, 0), P(10, 0), P(0, 0));

        AssertInvalid(result, RoofValidationError.FewerThanThreeUniqueVertices);
    }

    [Fact]
    public void ExactConsecutiveDuplicate_IsRejectedExplicitly()
    {
        var result = Validate(P(0, 0), P(10, 0), P(10, 0), P(0, 10));

        AssertInvalid(result, RoofValidationError.DuplicateConsecutiveVertex);
    }

    [Fact]
    public void NearZeroEdge_IsRejectedExplicitly()
    {
        var shortEdge = RoofFootprintValidator.MinimumEdgeLengthMm / 2d;
        var result = Validate(P(0, 0), P(shortEdge, 0), P(10, 5), P(0, 5));

        AssertInvalid(result, RoofValidationError.ZeroLengthEdge);
    }

    [Fact]
    public void CollinearZeroAreaPolygon_IsRejected()
    {
        var result = Validate(P(0, 0), P(10, 0), P(20, 0));

        AssertInvalid(result, RoofValidationError.DegenerateArea);
    }

    [Fact]
    public void RedundantCollinearVertex_IsRejectedWithoutSilentRepair()
    {
        var result = Validate(P(0, 0), P(5, 0), P(10, 0), P(10, 5), P(0, 5));

        AssertInvalid(result, RoofValidationError.RedundantCollinearVertex);
    }

    [Fact]
    public void SelfIntersectingBowTie_IsRejected()
    {
        var result = Validate(P(0, 0), P(10, 10), P(0, 10), P(10, 0));

        AssertInvalid(result, RoofValidationError.SelfIntersection);
    }

    [Fact]
    public void NonAdjacentTouchingVertex_IsRejectedAsSelfIntersection()
    {
        var result = Validate(P(0, 0), P(10, 0), P(5, 5), P(10, 10), P(0, 10), P(5, 5));

        AssertInvalid(result, RoofValidationError.SelfIntersection);
    }

    [Fact]
    public void UnsupportedCurveFlag_IsRejected()
    {
        var result = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [P(0, 0), P(10, 0), P(0, 10)],
            IsClosed: true,
            HasCurvedSegments: true));

        AssertInvalid(result, RoofValidationError.UnsupportedCurvedSegment);
    }

    [Fact]
    public void NonPlanarFlag_IsRejected()
    {
        var result = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [P(0, 0), P(10, 0), P(0, 10)],
            IsClosed: true,
            IsPlanar: false));

        AssertInvalid(result, RoofValidationError.NonPlanar);
    }

    [Fact]
    public void NonFiniteCoordinate_IsRejected()
    {
        var result = Validate(P(0, 0), P(double.NaN, 0), P(0, 10));

        AssertInvalid(result, RoofValidationError.NonFiniteCoordinate);
    }

    [Fact]
    public void NearDegenerateArea_UsesCentralTolerance()
    {
        var height = RoofFootprintValidator.MinimumAreaMm2 / 20d;
        var result = Validate(P(0, 0), P(20, 0), P(10, height));

        AssertInvalid(result, RoofValidationError.DegenerateArea);
    }

    [Fact]
    public void Direction_NormalizesAndUsesCounterClockwiseXyAngle()
    {
        Assert.True(RoofDirection2D.TryCreate(3, 4, out var direction));

        Assert.Equal(0.6d, direction.X, 12);
        Assert.Equal(0.8d, direction.Y, 12);
        Assert.Equal(Math.Atan2(4, 3), direction.AngleRadians, 12);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(double.NaN, 1d)]
    [InlineData(1d, double.PositiveInfinity)]
    public void InvalidDirection_IsRejected(double x, double y)
    {
        Assert.False(RoofDirection2D.TryCreate(x, y, out _));
    }

    [Fact]
    public void Definition_DefaultsToUnspecifiedParameters()
    {
        var footprint = Validate(P(0, 0), P(10, 0), P(0, 10)).Footprint!;
        var definition = new RoofDefinition(footprint);

        Assert.Same(RoofParameters.Unspecified, definition.Parameters);
        Assert.Null(definition.Parameters.SlopeDegrees);
        Assert.Null(definition.Parameters.RidgeDirection);
    }

    [Fact]
    public void ValidatorTolerances_AreCentralizedAndPositive()
    {
        Assert.True(RoofFootprintValidator.ClosingPointToleranceMm > 0d);
        Assert.Equal(1e-9d, RoofFootprintValidator.ClosingPointToleranceMm);
        Assert.True(RoofFootprintValidator.DuplicateVertexToleranceMm > 0d);
        Assert.True(RoofFootprintValidator.MinimumEdgeLengthMm > 0d);
        Assert.True(RoofFootprintValidator.MinimumAreaMm2 > 0d);
        Assert.True(RoofFootprintValidator.CollinearityTolerance > 0d);
    }

    private static RoofValidationResult Validate(params RoofPoint2D[] vertices) =>
        RoofFootprintValidator.Validate(new RoofFootprintInput(vertices, IsClosed: true));

    private static RoofValidationResult ValidateSource(
        bool isClosed,
        params RoofPoint2D[] vertices) =>
        RoofFootprintValidator.Validate(new RoofFootprintInput(vertices, isClosed));

    private static RoofPoint2D P(double x, double y) => new(x, y);

    private static void AssertInvalid(RoofValidationResult result, RoofValidationError expected)
    {
        Assert.False(result.IsValid);
        Assert.Null(result.Footprint);
        Assert.Equal(expected, result.Error);
        Assert.Equal(RoofPolygonOrientation.Undefined, result.SourceOrientation);
    }
}
