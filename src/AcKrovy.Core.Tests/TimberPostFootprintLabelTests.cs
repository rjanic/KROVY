using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberPostFootprintLabelTests
{
    [Fact]
    public void Format_CreatesExactlyThreeRequiredLines()
    {
        var text = TimberPostFootprintLabelFormatter.Format(Data(), actualLengthMm: 2500);

        Assert.Equal(new[] { "S1", "140x140", "2500 mm" }, text.Split(["\\P"], StringSplitOptions.None));
    }

    [Fact]
    public void Format_UsesActualLengthAndNeverCuttingLength()
    {
        var data = Data() with { CuttingAllowanceMm = 100 };
        var measurement = TimberCalculator.Measure(data, planLengthMm: null);

        var text = TimberPostFootprintLabelFormatter.Format(data, measurement.ActualLengthMm);

        Assert.Equal(2500, measurement.ActualLengthMm);
        Assert.Equal(2600, measurement.CuttingLengthMm);
        Assert.Contains("2500 mm", text, StringComparison.Ordinal);
        Assert.DoesNotContain("2600", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Placement_UsesBoundsCenterAndTopPlusCentralGap()
    {
        var geometry = Rectangle(140, 200);

        var placement = TimberPostFootprintLabelPlacementCalculator.Calculate(geometry.Bounds);

        Assert.Equal(70, placement.AnchorX);
        Assert.Equal(200 + TimberPostFootprintLabelPlacementCalculator.VerticalGapMm, placement.AnchorY);
        Assert.Equal(0, placement.RotationRadians);
        Assert.Equal(180, TimberPostFootprintLabelPlacementCalculator.TextHeightMm);
        Assert.Equal(1, TimberPostFootprintLabelPlacementCalculator.LineSpacingFactor);
        Assert.Equal(80, TimberPostFootprintLabelPlacementCalculator.VerticalGapMm);
    }

    [Fact]
    public void RotatedRectangle_LabelRemainsHorizontalAboveWorldBounds()
    {
        var geometry = Geometry(Rotate(Vertices(140, 200), 37));

        var placement = TimberPostFootprintLabelPlacementCalculator.Calculate(geometry.Bounds);

        Assert.Equal((geometry.Bounds.MinX + geometry.Bounds.MaxX) / 2, placement.AnchorX, precision: 6);
        Assert.Equal(
            geometry.Bounds.MaxY + TimberPostFootprintLabelPlacementCalculator.VerticalGapMm,
            placement.AnchorY,
            precision: 6);
        Assert.Equal(0, placement.RotationRadians);
    }

    [Fact]
    public void MoveRefresh_TranslatesBoundsBasedAnchor()
    {
        var original = Rectangle(140, 200);
        var moved = Geometry(Vertices(140, 200).Select(point => new TimberRectangularFootprintPoint(
            point.X + 500,
            point.Y - 300)));

        var originalPlacement = TimberPostFootprintLabelPlacementCalculator.Calculate(original.Bounds);
        var movedPlacement = TimberPostFootprintLabelPlacementCalculator.Calculate(moved.Bounds);

        Assert.Equal(originalPlacement.AnchorX + 500, movedPlacement.AnchorX, precision: 6);
        Assert.Equal(originalPlacement.AnchorY - 300, movedPlacement.AnchorY, precision: 6);
    }

    [Fact]
    public void RotateRefresh_RecalculatesWorldBoundsWithoutRotatingText()
    {
        var original = Rectangle(140, 200);
        var rotated = Geometry(Rotate(Vertices(140, 200), 90));

        var originalPlacement = TimberPostFootprintLabelPlacementCalculator.Calculate(original.Bounds);
        var rotatedPlacement = TimberPostFootprintLabelPlacementCalculator.Calculate(rotated.Bounds);

        Assert.NotEqual(originalPlacement.AnchorY, rotatedPlacement.AnchorY);
        Assert.Equal(0, rotatedPlacement.RotationRadians);
    }

    [Fact]
    public void StretchRefresh_UpdatesDimensionsContentAndPlacement()
    {
        var stretchedGeometry = Rectangle(240, 320);
        var dimensions = TimberRectangularFootprintEdgeRules.ResolveDimensions(stretchedGeometry, 0);
        var stretchedData = Data() with
        {
            WidthMm = dimensions.WidthMm,
            HeightMm = dimensions.HeightMm,
        };

        var text = TimberPostFootprintLabelFormatter.Format(stretchedData, 2500);
        var placement = TimberPostFootprintLabelPlacementCalculator.Calculate(stretchedGeometry.Bounds);

        Assert.Contains("240x320", text, StringComparison.Ordinal);
        Assert.Equal(120, placement.AnchorX);
        Assert.Equal(320 + TimberPostFootprintLabelPlacementCalculator.VerticalGapMm, placement.AnchorY);
    }

    [Fact]
    public void ManualLengthEdit_RefreshesThirdLine()
    {
        var text = TimberPostFootprintLabelFormatter.Format(Data() with { ManualLengthMm = 2800 }, 2800);

        Assert.Equal("2800 mm", text.Split(["\\P"], StringSplitOptions.None)[2]);
    }

    [Fact]
    public void ItemNumberChange_RefreshesFirstLine()
    {
        var text = TimberPostFootprintLabelFormatter.Format(Data() with { ElementId = "S7" }, 2500);

        Assert.Equal("S7", text.Split(["\\P"], StringSplitOptions.None)[0]);
    }

    [Fact]
    public void CopyWithNewSourceHandle_CreatesDedicatedLabelInsteadOfStealingOriginal()
    {
        var selection = TimberElementLabelMatchRules.SelectLabelForUpsert(
            sourceHandle: "NEW",
            currentElementId: "S1",
            previousElementId: "S1",
            candidates: [Candidate("original-label", "S1", "OLD")],
            currentElementOwnerCount: 2,
            previousElementOwnerCount: 2);

        Assert.Null(selection.LabelKeyToUpdate);
        Assert.Empty(selection.LabelKeysToDelete);
    }

    [Fact]
    public void DuplicateCleanup_KeepsOneLabelPerFootprintSourceHandle()
    {
        var deleted = TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(
            [
                Candidate("first", "S1", "POST"),
                Candidate("duplicate", "S1", "POST"),
            ],
            ["POST"]);

        Assert.Equal(["duplicate"], deleted);
    }

    [Fact]
    public void LegacyLinePost_RemainsOnExistingLabelWorkflow()
    {
        var plan = TimberAnnotationRefreshPlanner.Create(Data() with { FootprintWidthEdgeIndex = null });

        Assert.True(plan.EnsureLabel);
    }

    [Fact]
    public void FootprintPost_DoesNotRouteToLegacyLineLabel()
    {
        var plan = TimberAnnotationRefreshPlanner.Create(Data(), isRectangularFootprintPost: true);

        Assert.False(plan.EnsureLabel);
    }

    private static TimberElementData Data() => TimberElementDefaults.For(TimberElementType.Post) with
    {
        ElementId = "S1",
        WidthMm = 140,
        HeightMm = 140,
        LengthCalculationMode = LengthCalculationMode.ManualLength,
        ManualLengthMm = 2500,
        FootprintWidthEdgeIndex = 0,
    };

    private static TimberElementLabelCandidate Candidate(string key, string elementId, string sourceHandle) => new()
    {
        LabelKey = key,
        ElementId = elementId,
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
