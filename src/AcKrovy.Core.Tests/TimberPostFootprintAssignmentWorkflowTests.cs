using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberPostFootprintAssignmentWorkflowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void SelectedLightweightPolyline_IsAcceptedBySelectionRules()
    {
        Assert.Equal(
            TimberPostFootprintSelectionDecision.AcceptEntity,
            TimberPostFootprintSelectionRules.Evaluate(
                TimberPostFootprintPromptOutcome.Selected,
                isLightweightPolyline: true));
    }

    [Fact]
    public void SelectedUnsupportedEntity_IsRejectedBySelectionRules()
    {
        Assert.Equal(
            TimberPostFootprintSelectionDecision.RejectEntity,
            TimberPostFootprintSelectionRules.Evaluate(
                TimberPostFootprintPromptOutcome.Selected,
                isLightweightPolyline: false));
    }

    [Theory]
    [InlineData(TimberPostFootprintPromptOutcome.Cancelled)]
    [InlineData(TimberPostFootprintPromptOutcome.None)]
    [InlineData(TimberPostFootprintPromptOutcome.Error)]
    public void NonSuccessfulPromptOutcome_StopsWithoutValidSelection(
        TimberPostFootprintPromptOutcome outcome)
    {
        Assert.Equal(
            TimberPostFootprintSelectionDecision.Stop,
            TimberPostFootprintSelectionRules.Evaluate(outcome, isLightweightPolyline: true));
    }

    [Fact]
    public void AcceptedValidRectangle_ContinuesToSegmentResolver()
    {
        var selectionDecision = TimberPostFootprintSelectionRules.Evaluate(
            TimberPostFootprintPromptOutcome.Selected,
            isLightweightPolyline: true);
        var geometry = ValidGeometry(Rectangle());
        var segment = TimberPolylineSegmentPickResolver.Resolve(geometry, P(70d, 4d));

        Assert.Equal(TimberPostFootprintSelectionDecision.AcceptEntity, selectionDecision);
        Assert.Equal(TimberPolylineSegmentPickStatus.Success, segment.Status);
        Assert.Equal(0, segment.EdgeIndex);
    }

    [Fact]
    public void ClickNearEdgeZero_ResolvesEdgeZero()
    {
        var result = Resolve(P(70d, 4d));

        Assert.Equal(TimberPolylineSegmentPickStatus.Success, result.Status);
        Assert.Equal(0, result.EdgeIndex);
    }

    [Fact]
    public void ClickNearEdgeOne_ResolvesEdgeOne()
    {
        var result = Resolve(P(136d, 100d));

        Assert.Equal(TimberPolylineSegmentPickStatus.Success, result.Status);
        Assert.Equal(1, result.EdgeIndex);
    }

    [Fact]
    public void ClickNearSharedCorner_IsAmbiguous()
    {
        var result = Resolve(P(0.2d, 0.2d));

        Assert.Equal(TimberPolylineSegmentPickStatus.Ambiguous, result.Status);
        Assert.Null(result.EdgeIndex);
    }

    [Fact]
    public void ClearlyNearestEdge_IsSelectedForRotatedRectangle()
    {
        var geometry = ValidGeometry(Rotate(Rectangle(), 30d));
        var midpoint = Midpoint(geometry.Segments[2]);
        var result = TimberPolylineSegmentPickResolver.Resolve(geometry, midpoint);

        Assert.Equal(TimberPolylineSegmentPickStatus.Success, result.Status);
        Assert.Equal(2, result.EdgeIndex);
    }

    [Fact]
    public void ClosedFourVertexRectangle_NormalizesWithoutChangingVertices()
    {
        Assert.True(TryNormalize(isClosed: true, Rectangle(), out var normalized, out var error));
        Assert.Equal(TimberPostFootprintPolylineValidationError.None, error);
        Assert.Equal(4, normalized.Count);
        Assert.True(TimberRectangularFootprintValidator.Validate(normalized).IsValid);
    }

    [Fact]
    public void GeometricallyClosedFiveVertexPolyline_NormalizesToFourVertices()
    {
        var vertices = Rectangle().Append(P(0d, 0d)).ToArray();

        Assert.True(TryNormalize(isClosed: false, vertices, out var normalized, out var error));
        Assert.Equal(TimberPostFootprintPolylineValidationError.None, error);
        Assert.Equal(Rectangle(), normalized);
        Assert.True(TimberRectangularFootprintValidator.Validate(normalized).IsValid);
    }

    [Fact]
    public void GeometricClosure_UsesTolerance()
    {
        var vertices = Rectangle().Append(P(0.005d, -0.005d)).ToArray();

        Assert.True(TryNormalize(isClosed: false, vertices, out var normalized, out _));
        Assert.Equal(4, normalized.Count);
    }

    [Fact]
    public void OpenPolylineWithoutCoincidentEndPoint_IsRejected()
    {
        Assert.False(TryNormalize(isClosed: false, Rectangle(), out _, out var error));
        Assert.Equal(TimberPostFootprintPolylineValidationError.NotClosed, error);
    }

    [Fact]
    public void BulgedGeometry_IsRejected()
    {
        Assert.False(TimberPostFootprintPolylineRules.TryNormalizeVertices(
            isClosed: true,
            Rectangle(),
            hasCurvedSegments: true,
            isSupportedPlanarGeometry: true,
            out _,
            out var error));
        Assert.Equal(TimberPostFootprintPolylineValidationError.CurvedSegment, error);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void ClosedPolylineWithOtherThanFourVertices_IsRejected(int vertexCount)
    {
        var vertices = Rectangle().Take(vertexCount).ToList();
        while (vertices.Count < vertexCount)
        {
            vertices.Add(P(vertices.Count * 10d, vertices.Count * 5d));
        }

        Assert.False(TryNormalize(isClosed: true, vertices, out _, out var error));
        Assert.Equal(TimberPostFootprintPolylineValidationError.WrongSegmentCount, error);
    }

    [Fact]
    public void WidthThenHeightClick_ProducesOneHundredFortyByTwoHundred()
    {
        var dimensions = ResolveDimensions(0);

        Assert.Equal(140d, dimensions.WidthMm);
        Assert.Equal(200d, dimensions.HeightMm);
    }

    [Fact]
    public void ReversedClickOrder_ProducesTwoHundredByOneHundredForty()
    {
        var dimensions = ResolveDimensions(1);

        Assert.Equal(200d, dimensions.WidthMm);
        Assert.Equal(140d, dimensions.HeightMm);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OneClickAlwaysPersistsNormalizedWidthEdgeIndex(int widthEdgeIndex)
    {
        var dimensions = ResolveDimensions(widthEdgeIndex);

        Assert.Equal(widthEdgeIndex, dimensions.WidthEdgeIndex);
        Assert.InRange(
            dimensions.WidthEdgeIndex,
            TimberRectangularFootprintEdgeRules.MinimumEdgeIndex,
            TimberRectangularFootprintEdgeRules.MaximumEdgeIndex);
    }

    [Fact]
    public void OneClickedEdgeIsSufficientToResolveBothDimensions()
    {
        var dimensions = TimberRectangularFootprintEdgeRules.ResolveDimensions(
            ValidGeometry(Rectangle()),
            widthEdgeIndex: 0);

        Assert.Equal(140d, dimensions.WidthMm);
        Assert.Equal(200d, dimensions.HeightMm);
    }

    [Fact]
    public void CreatedMetadata_UsesPostType()
    {
        Assert.Equal(TimberElementType.Post, CreateMetadata().ElementType);
    }

    [Fact]
    public void CreatedMetadata_UsesManualLengthMode()
    {
        Assert.Equal(LengthCalculationMode.ManualLength, CreateMetadata().LengthCalculationMode);
    }

    [Fact]
    public void CreatedMetadata_DefaultsManualLengthToTwoThousandFiveHundred()
    {
        Assert.Equal(2500d, CreateMetadata().ManualLengthMm);
    }

    [Fact]
    public void CreatedMetadata_PersistsWidthEdgeIndex()
    {
        var source = CreateMetadata();
        var loaded = Assert.IsType<TimberElementData>(JsonSerializer.Deserialize<TimberElementData>(
            JsonSerializer.Serialize(source, JsonOptions),
            JsonOptions));

        Assert.Equal(0, loaded.FootprintWidthEdgeIndex);
    }

    [Fact]
    public void CreatedMetadata_UsesSchemaVersionTwo()
    {
        Assert.Equal(2, CreateMetadata().SchemaVersion);
    }

    [Fact]
    public void UnsupportedPlane_IsRejected()
    {
        Assert.False(TimberPostFootprintPolylineRules.TryNormalizeVertices(
            isClosed: true,
            Rectangle(),
            hasCurvedSegments: false,
            isSupportedPlanarGeometry: false,
            out _,
            out var error));
        Assert.Equal(TimberPostFootprintPolylineValidationError.UnsupportedPlane, error);
    }

    [Fact]
    public void Trapezoid_FailsCoreRectangleValidation()
    {
        var result = TimberRectangularFootprintValidator.Validate(
            new[] { P(0d, 0d), P(140d, 0d), P(120d, 200d), P(20d, 200d) });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidRotatedRectangle_PassesCoreRectangleValidation()
    {
        Assert.True(TimberRectangularFootprintValidator.Validate(
            Rotate(Rectangle(), 37d)).IsValid);
    }

    [Fact]
    public void PolylinePerimeter_IsNotUsedAsPostActualLength()
    {
        var metadata = CreateMetadata();
        var rectanglePerimeter = 2d * (metadata.WidthMm + metadata.HeightMm);

        var measurement = TimberCalculator.Measure(metadata, rectanglePerimeter);

        Assert.Equal(2500d, measurement.ActualLengthMm);
        Assert.NotEqual(rectanglePerimeter, measurement.ActualLengthMm);
    }

    [Fact]
    public void ExistingLegacyLinePost_RemainsWithoutFootprintIndex()
    {
        var legacy = TimberElementDefaults.For(TimberElementType.Post) with
        {
            SchemaVersion = 1,
            FootprintWidthEdgeIndex = null,
            ManualLengthMm = 2700d,
        };

        var normalized = TimberElementDataVersioning.Normalize(legacy);

        Assert.Equal(1, normalized.SchemaVersion);
        Assert.Null(normalized.FootprintWidthEdgeIndex);
        Assert.Equal(2700d, TimberCalculator.CalculateActualLengthMm(normalized, 1000d));
    }

    [Fact]
    public void NonPostPatchingWorkflow_RemainsUnchanged()
    {
        var rafter = TimberElementDefaults.For(TimberElementType.Rafter);
        var patch = new TimberElementPatch(
            ElementType: null,
            WidthMm: 90d,
            HeightMm: null,
            SlopeDegrees: null,
            RoofPlaneId: null,
            CuttingAllowanceMm: null,
            LengthCalculationMode: null,
            ManualLengthMm: null,
            Material: null,
            Note: null);

        var updated = TimberElementPatcher.Apply(rafter, patch);

        Assert.Equal(TimberElementType.Rafter, updated.ElementType);
        Assert.Equal(90d, updated.WidthMm);
        Assert.Null(updated.FootprintWidthEdgeIndex);
    }

    private static TimberPolylineSegmentPickResult Resolve(TimberRectangularFootprintPoint point) =>
        TimberPolylineSegmentPickResolver.Resolve(ValidGeometry(Rectangle()), point);

    private static TimberRectangularFootprintDimensions ResolveDimensions(int widthEdgeIndex) =>
        TimberRectangularFootprintEdgeRules.ResolveDimensions(
            ValidGeometry(Rectangle()),
            widthEdgeIndex);

    private static TimberElementData CreateMetadata() =>
        TimberPostFootprintAssignmentRules.CreateMetadata(
            TimberElementDefaults.For(TimberElementType.Post) with
            {
                ManualLengthMm = null,
            },
            ResolveDimensions(0));

    private static bool TryNormalize(
        bool isClosed,
        IReadOnlyList<TimberRectangularFootprintPoint> vertices,
        out IReadOnlyList<TimberRectangularFootprintPoint> normalized,
        out TimberPostFootprintPolylineValidationError error) =>
        TimberPostFootprintPolylineRules.TryNormalizeVertices(
            isClosed,
            vertices,
            hasCurvedSegments: false,
            isSupportedPlanarGeometry: true,
            out normalized,
            out error);

    private static TimberRectangularFootprintGeometry ValidGeometry(
        IReadOnlyList<TimberRectangularFootprintPoint> points) =>
        Assert.IsType<TimberRectangularFootprintGeometry>(
            TimberRectangularFootprintValidator.Validate(points).Geometry);

    private static TimberRectangularFootprintPoint[] Rectangle() =>
    [
        P(0d, 0d),
        P(140d, 0d),
        P(140d, 200d),
        P(0d, 200d),
    ];

    private static TimberRectangularFootprintPoint[] Rotate(
        IReadOnlyList<TimberRectangularFootprintPoint> points,
        double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return points
            .Select(point => P(
                point.X * cosine - point.Y * sine,
                point.X * sine + point.Y * cosine))
            .ToArray();
    }

    private static TimberRectangularFootprintPoint Midpoint(
        TimberRectangularFootprintSegment segment) =>
        P((segment.Start.X + segment.End.X) / 2d, (segment.Start.Y + segment.End.Y) / 2d);

    private static TimberRectangularFootprintPoint P(double x, double y) => new(x, y);
}
