using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberPostFootprintPerpendicularAnnotationTests
{
    [Fact]
    public void LocalGeometry_ContainsPerpendicularSymbolAndNinetyDegreeText()
    {
        var local = TimberPostFootprintPerpendicularGeometryCalculator.CreateLocal();

        Assert.Equal("90°", local.Text);
        Assert.Equal(local.CapStart.Y, local.CapEnd.Y);
        Assert.Equal(local.StemStart.X, local.StemEnd.X);
        Assert.Equal((local.CapStart.X + local.CapEnd.X) / 2d, local.StemStart.X);
        Assert.True(local.StemEnd.Y < local.StemStart.Y);
        Assert.Equal(local.CapStart.Y, local.StemEnd.Y);
    }

    [Fact]
    public void LocalGeometry_UsesCentralizedDimensionsAndTextGap()
    {
        var local = TimberPostFootprintPerpendicularGeometryCalculator.CreateLocal();

        Assert.Equal(
            TimberPostFootprintPerpendicularGeometryCalculator.CapLengthMm,
            local.CapEnd.X - local.CapStart.X);
        Assert.Equal(
            TimberPostFootprintPerpendicularGeometryCalculator.StemLengthMm,
            local.StemStart.Y - local.StemEnd.Y);
        Assert.Equal(
            TimberPostFootprintPerpendicularGeometryCalculator.HorizontalGapMm,
            local.TextPosition.X - local.CapEnd.X);
        Assert.True(local.TextPosition.X > local.CapEnd.X);
    }

    [Fact]
    public void LocalGeometry_DrawsHorizontalCapBelowUpwardStem()
    {
        var local = TimberPostFootprintPerpendicularGeometryCalculator.CreateLocal();

        Assert.Equal(-TimberPostFootprintPerpendicularGeometryCalculator.StemLengthMm, local.CapStart.Y);
        Assert.Equal(local.CapStart.Y, local.CapEnd.Y);
        Assert.Equal(local.CapStart.Y, local.StemEnd.Y);
        Assert.Equal(0, local.StemStart.Y);
        Assert.True(local.StemStart.Y > local.StemEnd.Y);
    }

    [Fact]
    public void Placement_UsesBoundsCenterAndMinimumYMinusBottomGap()
    {
        var geometry = Rectangle(140, 200);

        var placement = TimberPostFootprintPerpendicularGeometryCalculator.CalculatePlacement(geometry.Bounds);

        Assert.Equal(70, placement.AnchorX);
        Assert.Equal(
            -TimberPostFootprintPerpendicularGeometryCalculator.BottomAnnotationGapMm,
            placement.AnchorY);
        Assert.Equal(0, placement.RotationRadians);
    }

    [Fact]
    public void RotatedFootprint_AnnotationRemainsHorizontalBelowWorldBounds()
    {
        var geometry = Geometry(Rotate(Vertices(140, 200), 37));

        var placement = TimberPostFootprintPerpendicularGeometryCalculator.CalculatePlacement(geometry.Bounds);

        Assert.Equal((geometry.Bounds.MinX + geometry.Bounds.MaxX) / 2d, placement.AnchorX, precision: 6);
        Assert.Equal(
            geometry.Bounds.MinY - TimberPostFootprintPerpendicularGeometryCalculator.BottomAnnotationGapMm,
            placement.AnchorY,
            precision: 6);
        Assert.Equal(0, placement.RotationRadians);
    }

    [Fact]
    public void MoveRefresh_TranslatesAnnotationAnchor()
    {
        var original = Rectangle(140, 200);
        var moved = Geometry(Vertices(140, 200).Select(point => new TimberRectangularFootprintPoint(
            point.X + 500,
            point.Y - 300)));

        var first = TimberPostFootprintPerpendicularGeometryCalculator.CalculatePlacement(original.Bounds);
        var second = TimberPostFootprintPerpendicularGeometryCalculator.CalculatePlacement(moved.Bounds);

        Assert.Equal(first.AnchorX + 500, second.AnchorX, precision: 6);
        Assert.Equal(first.AnchorY - 300, second.AnchorY, precision: 6);
    }

    [Fact]
    public void RotateRefresh_ChangesAnchorButNotAnnotationRotation()
    {
        var first = TimberPostFootprintPerpendicularGeometryCalculator.CalculatePlacement(
            Rectangle(140, 200).Bounds);
        var second = TimberPostFootprintPerpendicularGeometryCalculator.CalculatePlacement(
            Geometry(Rotate(Vertices(140, 200), -37)).Bounds);

        Assert.NotEqual(first.AnchorY, second.AnchorY);
        Assert.Equal(0, second.RotationRadians);
    }

    [Fact]
    public void StretchRefresh_UsesNewBoundsWithoutChangingLocalSymbol()
    {
        var originalLocal = TimberPostFootprintPerpendicularGeometryCalculator.CreateLocal();
        var placement = TimberPostFootprintPerpendicularGeometryCalculator.CalculatePlacement(
            Rectangle(240, 320).Bounds);

        Assert.Equal(120, placement.AnchorX);
        Assert.Equal(
            -TimberPostFootprintPerpendicularGeometryCalculator.BottomAnnotationGapMm,
            placement.AnchorY);
        Assert.Equal(originalLocal, TimberPostFootprintPerpendicularGeometryCalculator.CreateLocal());
    }

    [Fact]
    public void FootprintRouting_DoesNotUseSlopeArrowOrAngleTextPipeline()
    {
        var plan = TimberAnnotationRefreshPlanner.Create(Data(), isRectangularFootprintPost: true);

        Assert.False(plan.ReconcileSlopeArrow);
        Assert.False(plan.ShouldSlopeArrowExist);
        Assert.False(plan.ShouldPostPerpendicularMarkerExist);
        Assert.False(plan.ReconcileSlopeAngleText);
        Assert.False(plan.ShouldSlopeAngleTextExist);
    }

    [Fact]
    public void CopySourceHandle_IsDistinctAndRequiresOwnAnnotation()
    {
        Assert.False(TimberSlopeAnnotationRules.HasSameSourceHandle("POST-OLD", "POST-NEW"));
        Assert.True(TimberSlopeAnnotationRules.HasSameSourceHandle("POST-NEW", "post-new"));
    }

    [Fact]
    public void DuplicateCleanup_KeepsExactlyOneAnnotationPerSourceHandle()
    {
        var deleted = TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(
            [Candidate("first", "POST"), Candidate("duplicate", "POST")],
            ["POST"]);

        Assert.Equal(["duplicate"], deleted);
    }

    [Fact]
    public void OrphanCleanup_RemovesAnnotationWhenFootprintSourceIsDeleted()
    {
        var deleted = TimberElementLabelCleanupRules.SelectLabelsWithoutExistingSourceHandleToDelete(
            [Candidate("annotation", "DELETED-POST")],
            []);

        Assert.Equal(["annotation"], deleted);
    }

    [Fact]
    public void FlipSlopeGuard_DoesNotMutateFootprintPostMetadata()
    {
        var data = Data() with { IsSlopeDirectionReversed = true, FootprintWidthEdgeIndex = 2 };

        var canFlip = TimberSlopeAnnotationRules.CanFlipDirection(data.ElementType, data.SlopeDegrees);

        Assert.False(canFlip);
        Assert.True(data.IsSlopeDirectionReversed);
        Assert.Equal(2, data.FootprintWidthEdgeIndex);
    }

    [Fact]
    public void MainLabelAndBottomAnnotationUseIndependentEightyMillimetreGaps()
    {
        Assert.Equal(80, TimberPostFootprintLabelPlacementCalculator.VerticalGapMm);
        Assert.Equal(80, TimberPostFootprintPerpendicularGeometryCalculator.BottomAnnotationGapMm);
    }

    [Fact]
    public void LegacyRafterSlopeArrowRulesRemainUnchanged()
    {
        Assert.Equal(
            TimberSlopeGlyphKind.DirectionalArrow,
            TimberSlopeAnnotationRules.ResolveGlyphKind(TimberElementType.Rafter, 35));
        Assert.True(TimberSlopeAnnotationRules.CanFlipDirection(TimberElementType.Rafter, 35));
    }

    private static TimberElementData Data() => TimberElementDefaults.For(TimberElementType.Post) with
    {
        ElementId = "S1",
        WidthMm = 140,
        HeightMm = 140,
        FootprintWidthEdgeIndex = 0,
        LengthCalculationMode = LengthCalculationMode.ManualLength,
        ManualLengthMm = 2500,
    };

    private static TimberElementLabelCandidate Candidate(string key, string sourceHandle) => new()
    {
        LabelKey = key,
        SourceHandle = sourceHandle,
    };

    private static TimberRectangularFootprintGeometry Rectangle(double widthMm, double heightMm) =>
        Geometry(Vertices(widthMm, heightMm));

    private static TimberRectangularFootprintGeometry Geometry(
        IEnumerable<TimberRectangularFootprintPoint> vertices) =>
        TimberRectangularFootprintValidator.Validate(vertices.ToArray()).Geometry!;

    private static IReadOnlyList<TimberRectangularFootprintPoint> Vertices(double widthMm, double heightMm) =>
    [
        new(0, 0),
        new(widthMm, 0),
        new(widthMm, heightMm),
        new(0, heightMm),
    ];

    private static IReadOnlyList<TimberRectangularFootprintPoint> Rotate(
        IEnumerable<TimberRectangularFootprintPoint> points,
        double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return points.Select(point => new TimberRectangularFootprintPoint(
            point.X * cosine - point.Y * sine,
            point.X * sine + point.Y * cosine)).ToArray();
    }
}
