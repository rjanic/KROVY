using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// G5 Combined NEW create: final world-space Right + first segment ≈ 60°.
/// Refresh must stay idempotent and must not reapply create defaults.
/// </summary>
public sealed class TimberFramedCombinedG5CreatePlacementRulesTests
{
    public static TheoryData<double> CreateAnglesDeg { get; } = new()
    {
        0d,
        35d,
        -35d,
        90d,
    };

    public static TheoryData<double, double, double, double> ReverseEndpointCases { get; } =
        new()
        {
            { 0d, 0d, 1000d, 0d },
            { 1000d, 0d, 0d, 0d },
            { 0d, 0d, 1000d, Math.Tan(35d * Math.PI / 180d) * 1000d },
            { 1000d, Math.Tan(35d * Math.PI / 180d) * 1000d, 0d, 0d },
            { 0d, 0d, 1000d, Math.Tan(-35d * Math.PI / 180d) * 1000d },
            { 1000d, Math.Tan(-35d * Math.PI / 180d) * 1000d, 0d, 0d },
            { 0d, 0d, 0d, 1000d },
            { 0d, 1000d, 0d, 0d },
        };

    [Fact]
    public void DefaultCreateSide_IsLocalRight()
    {
        Assert.Equal(
            TimberLeaderHorizontalSide.Right,
            TimberFramedCombinedG5RefreshPlacementRules.DefaultCreateSide);
        Assert.Equal(
            TimberLeaderHorizontalSide.Right,
            TimberFramedCombinedG5CreatePlacementRules.DesiredWorldSide);
        Assert.Equal(
            1d,
            TimberFramedBlockContentLayoutCalculator.SideSign(
                TimberFramedCombinedG5RefreshPlacementRules.DefaultCreateSide));
    }

    [Theory]
    [MemberData(nameof(CreateAnglesDeg))]
    public void NewCreate_WorldSideRight_FirstSegmentSixtyDegrees(double angleDeg)
    {
        var angle = angleDeg * Math.PI / 180d;
        var startX = 0d;
        var startY = 0d;
        var endX = 1000d * Math.Cos(angle);
        var endY = 1000d * Math.Sin(angle);
        AssertCreateWorldContract(startX, startY, endX, endY);
    }

    [Theory]
    [MemberData(nameof(ReverseEndpointCases))]
    public void ReverseStartEnd_ActualWorldSideRight_AndSixtyDegrees(
        double startX,
        double startY,
        double endX,
        double endY) =>
        AssertCreateWorldContract(startX, startY, endX, endY);

    [Theory]
    [MemberData(nameof(CreateAnglesDeg))]
    public void NewCreate_RefreshTwice_NoDrift(double angleDeg)
    {
        var angle = angleDeg * Math.PI / 180d;
        var create = BuildCreateLayout(angle);
        var refresh1 = BuildCreateLayout(angle);
        var refresh2 = BuildCreateLayout(angle);

        Assert.True(
            TimberFramedCombinedG5RefreshPlacementRules.LayoutsMatch(create, refresh1));
        Assert.True(
            TimberFramedCombinedG5RefreshPlacementRules.LayoutsMatch(refresh1, refresh2));
        Assert.True(
            TimberFramedCombinedG5RefreshPlacementRules.ShouldPreserveExistingPlacement(
                create.AttachmentLocal.X,
                create.AttachmentLocal.Y,
                refresh2.AttachmentLocal.X,
                refresh2.AttachmentLocal.Y,
                liveBlockScale: 50d,
                canonicalBlockScale: 50d));
    }

    [Fact]
    public void ExistingLeftPlacement_RefreshDoesNotFlipToDefaultRight()
    {
        var left = BuildLayout(
            35d * Math.PI / 180d,
            TimberLeaderHorizontalSide.Left);
        var rightDefault = BuildCreateLayout(35d * Math.PI / 180d);

        Assert.Equal(TimberLeaderHorizontalSide.Left, left.Side);
        Assert.Equal(-1d, left.SideSign);
        Assert.False(
            TimberFramedCombinedG5RefreshPlacementRules.LayoutsMatch(
                left,
                rightDefault));

        var refreshedLeft = BuildLayout(
            35d * Math.PI / 180d,
            TimberLeaderHorizontalSide.Left);
        Assert.True(
            TimberFramedCombinedG5RefreshPlacementRules.LayoutsMatch(
                left,
                refreshedLeft));
        Assert.True(
            TimberFramedCombinedG5RefreshPlacementRules.ShouldPreserveExistingPlacement(
                left.AttachmentLocal.X,
                left.AttachmentLocal.Y,
                refreshedLeft.AttachmentLocal.X,
                refreshedLeft.AttachmentLocal.Y,
                liveBlockScale: 50d,
                canonicalBlockScale: 50d));
    }

    [Fact]
    public void ReadabilityFlip_UsesOppositeLayoutSide_ToKeepWorldRight()
    {
        var raw = Math.PI; // Start→End leftward; readable folds to 0.
        Assert.True(TimberAnnotationReadabilityRules.IsReadabilityFlipped(raw));
        Assert.Equal(
            TimberLeaderHorizontalSide.Left,
            TimberFramedCombinedG5CreatePlacementRules.ResolveCreateLayoutSide(raw));
        Assert.Equal(
            TimberLeaderHorizontalSide.Right,
            TimberFramedCombinedG5CreatePlacementRules.ResolveCreateLayoutSide(0d));
    }

    [Fact]
    public void DimensionColumn_CreateUsesRightOnLandingTowardKnee()
    {
        var layout = BuildCreateLayout(0d);
        Assert.True(layout.LandingEndLocal.X > layout.LandingStartLocal.X);
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            TimberFramedBlockContentDefinitionRules.DefaultCombinedDimensionColumnSide);
        Assert.True(layout.DimensionColumnLocalX < 0d);
    }

    private static void AssertCreateWorldContract(
        double startX,
        double startY,
        double endX,
        double endY)
    {
        var midX = (startX + endX) / 2d;
        var midY = (startY + endY) / 2d;
        var rawAxis = Math.Atan2(endY - startY, endX - startX);
        var layout = TimberFramedCombinedG5CreatePlacementRules.CalculateCreate(
            midX,
            midY,
            rawAxis,
            TimberFramedBlockContentKind.Circle,
            frameWidthMm: 350d,
            frameHeightMm: 350d,
            annotationScaleDenominator: 50,
            itemPaperHeightMm: 3.5d,
            dimensionPaperHeightMm: 2.5d,
            firstSegmentLengthModelMm:
                TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm *
                TimberAnnotationScaleRules.GetScaleFactor(50),
            landingLengthModelMm:
                TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
                TimberAnnotationScaleRules.GetScaleFactor(50),
            dimensionColumnEnvelopeWidthMm:
                TimberFramedBlockContentDefinitionRules
                    .CalculateReferenceDimensionEnvelopeWidthMm(2.5d) *
                TimberAnnotationScaleRules.GetScaleFactor(50),
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide);

        Assert.Equal(
            TimberFramedCombinedG5CreatePlacementRules.ResolveCreateLayoutSide(rawAxis),
            layout.Side);

        var attachment = layout.AttachmentLocal;
        var knee = TimberFramedCombinedG5CreatePlacementRules.WorldKnee(layout);
        var blockPosition =
            TimberFramedCombinedG5CreatePlacementRules.WorldBlockPosition(layout);

        Assert.True(
            TimberFramedCombinedG5CreatePlacementRules.TryMeasureSignedSide(
                attachment.X,
                attachment.Y,
                blockPosition.X,
                blockPosition.Y,
                startX,
                startY,
                endX,
                endY,
                out var signedSide,
                out var worldSide));
        Assert.Equal(TimberLeaderHorizontalSide.Right, worldSide);
        Assert.True(signedSide > 0d);

        Assert.True(
            TimberFramedCombinedG5CreatePlacementRules.TryMeasureFirstSegmentAngleDeg(
                attachment.X,
                attachment.Y,
                knee.X,
                knee.Y,
                startX,
                startY,
                endX,
                endY,
                out var angleDeg));
        Assert.True(
            TimberFramedCombinedG5CreatePlacementRules.FirstSegmentAngleIsSixtyDegrees(
                angleDeg),
            $"ActualFirstSegmentAngleToSourceDeg={angleDeg:R}");
        Assert.Equal(60d, angleDeg, 2);
    }

    private static TimberFramedBlockContentLayout BuildCreateLayout(
        double elementAxisRadians) =>
        BuildLayout(
            elementAxisRadians,
            TimberFramedCombinedG5CreatePlacementRules.ResolveCreateLayoutSide(
                elementAxisRadians));

    private static TimberFramedBlockContentLayout BuildLayout(
        double elementAxisRadians,
        TimberLeaderHorizontalSide side) =>
        TimberFramedCombinedG5RefreshPlacementRules.CalculateCanonical(
            attachmentX: 0d,
            attachmentY: 0d,
            elementAxisRadians,
            side,
            TimberFramedBlockContentKind.Circle,
            frameWidthMm: 350d,
            frameHeightMm: 350d,
            annotationScaleDenominator: 50,
            itemPaperHeightMm: 3.5d,
            dimensionPaperHeightMm: 2.5d,
            firstSegmentLengthModelMm:
                TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm *
                TimberAnnotationScaleRules.GetScaleFactor(50),
            landingLengthModelMm:
                TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
                TimberAnnotationScaleRules.GetScaleFactor(50),
            dimensionColumnEnvelopeWidthMm:
                TimberFramedBlockContentDefinitionRules
                    .CalculateReferenceDimensionEnvelopeWidthMm(2.5d) *
                TimberAnnotationScaleRules.GetScaleFactor(50),
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide);
}
