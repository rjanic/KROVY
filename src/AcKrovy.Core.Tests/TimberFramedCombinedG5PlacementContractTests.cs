using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Production Combined placement contracts (create 60°, user placement,
/// no re-force 60°, reverse Start/End, frozen frame).
/// </summary>
public sealed class TimberFramedCombinedG5PlacementContractTests
{
    [Theory]
    [InlineData(0d)]
    [InlineData(35d)]
    [InlineData(-35d)]
    [InlineData(90d)]
    public void A_Create_FinalWorldFirstSegmentIsSixtyDegrees(double angleDeg)
    {
        var angle = angleDeg * Math.PI / 180d;
        var startX = 0d;
        var startY = 0d;
        var endX = 1000d * Math.Cos(angle);
        var endY = 1000d * Math.Sin(angle);
        var layout = TimberFramedCombinedG5CreatePlacementRules.CalculateCreate(
            attachmentX: (startX + endX) / 2d,
            attachmentY: (startY + endY) / 2d,
            rawElementAxisRadians: Math.Atan2(endY - startY, endX - startX),
            contentKind: TimberFramedBlockContentKind.Circle,
            frameWidthMm: TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
            frameHeightMm: TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
            annotationScaleDenominator: 50,
            itemPaperHeightMm: 2.7d,
            dimensionPaperHeightMm: 2.5d,
            firstSegmentLengthModelMm:
                TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm,
            landingLengthModelMm:
                TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm,
            dimensionColumnEnvelopeWidthMm: 100d,
            dimensionColumnSide: TimberFramedBlockContentDefinitionRules
                .DefaultCombinedDimensionColumnSide);

        var worldKnee = TimberFramedCombinedG5CreatePlacementRules.WorldKnee(layout);
        Assert.True(
            TimberFramedCombinedG5CreatePlacementRules.TryMeasureFirstSegmentAngleDeg(
                layout.AttachmentLocal.X,
                layout.AttachmentLocal.Y,
                worldKnee.X,
                worldKnee.Y,
                startX,
                startY,
                endX,
                endY,
                out var angleMeasuredDeg));
        Assert.True(
            TimberFramedCombinedG5CreatePlacementRules.FirstSegmentAngleIsSixtyDegrees(
                angleMeasuredDeg),
            $"Expected 60±0.01°, got {angleMeasuredDeg}");
    }

    [Fact]
    public void B_UserKneeOffset_SurvivesContentRefreshPreserve()
    {
        Assert.False(TimberFramedCombinedG5RefreshPlacementRules.ManualOffsetMayMoveAnchor);
        Assert.True(
            TimberFramedCombinedG5RefreshPlacementRules.ShouldPreserveExistingPlacement(
                liveAttachmentX: 100d,
                liveAttachmentY: 200d,
                canonicalAttachmentX: 100.1d,
                canonicalAttachmentY: 200d,
                liveBlockScale: 50d,
                canonicalBlockScale: 50d));
        Assert.False(
            TimberFramedCombinedG5RefreshPlacementRules.ShouldPreserveExistingPlacement(
                liveAttachmentX: 100d,
                liveAttachmentY: 200d,
                canonicalAttachmentX: 100d,
                canonicalAttachmentY: 200d,
                liveBlockScale: 50d,
                canonicalBlockScale: 25d));
    }

    [Fact]
    public void D_SixtyDegrees_NotReappliedWhenRefreshPreservesPlacement()
    {
        // Create defaults are for new create only; preserve path never forces
        // DefaultCreateSide / 60° onto an existing Left user placement.
        var left = TimberFramedCombinedG5RefreshPlacementRules.CalculateCanonical(
            0d,
            0d,
            0d,
            TimberLeaderHorizontalSide.Left,
            TimberFramedBlockContentKind.Circle,
            TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
            TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
            50,
            2.7d,
            2.5d,
            TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm,
            TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm,
            100d,
            TimberFramedBlockContentDefinitionRules.DefaultCombinedDimensionColumnSide);
        var createDefault = TimberFramedCombinedG5CreatePlacementRules.CalculateCreate(
            0d,
            0d,
            0d,
            TimberFramedBlockContentKind.Circle,
            TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
            TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
            50,
            2.7d,
            2.5d,
            TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm,
            TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm,
            100d,
            TimberFramedBlockContentDefinitionRules.DefaultCombinedDimensionColumnSide);

        Assert.Equal(TimberLeaderHorizontalSide.Left, left.Side);
        Assert.Equal(TimberLeaderHorizontalSide.Right, createDefault.Side);
        Assert.False(
            TimberFramedCombinedG5RefreshPlacementRules.LayoutsMatch(left, createDefault));
        Assert.True(
            TimberFramedCombinedG5RefreshPlacementRules.ShouldPreserveExistingPlacement(
                left.AttachmentLocal.X,
                left.AttachmentLocal.Y,
                createDefault.AttachmentLocal.X,
                createDefault.AttachmentLocal.Y,
                50d,
                50d));
    }

    [Fact]
    public void E_ReverseStartEnd_SameReadable_PreservesPresentation()
    {
        // Physical line unchanged; readable rotation identical → no mirror.
        var oldRot = TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(0d);
        var newRot = TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(Math.PI);
        Assert.False(
            TimberFramedCombinedG5SourceRotationRules.RotationChanged(oldRot, newRot));

        const double livePresentation = 0.35d;
        Assert.Equal(
            livePresentation,
            TimberFramedCombinedG5SourceRotationRules.ResolveRefreshPresentationRadians(
                oldRot,
                newRot,
                livePresentation),
            12);
    }

    [Fact]
    public void F_FrozenFrameSizing_IndependentOfTextStyleHeight()
    {
        var smallA = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle,
            "K1");
        var smallB = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle,
            "W3");
        Assert.Equal(smallA.WidthMm, smallB.WidthMm);
        Assert.Equal(
            TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
            smallA.WidthMm);

        // Linear frames size from FramedGeometrySizingTextHeightMm (175), not
        // from Text Settings paper height.
        var slot = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Slot,
            "KL2");
        Assert.Equal(TimberItemLeaderBlockDefinitionRules.FrameHeightMm, slot.HeightMm);
        Assert.True(slot.WidthMm >= TimberItemLeaderBlockDefinitionRules.SmallFrameWidthMm);
        Assert.Equal(175d, TimberItemLeaderBlockDefinitionRules.FramedGeometrySizingTextHeightMm);
    }

    [Fact]
    public void ProductionR3Combined_UsesContentVariantOnlyGripPath()
    {
        var r3 = TimberFramedBlockContentVariantRules.CreateSafeBlockName(
            TimberFramedBlockContentVariantRules.CreateRawKey(
                TimberFramedBlockContentKind.Circle,
                "SMALL",
                "Standard",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.Combined));
        Assert.Contains("R3", r3, StringComparison.Ordinal);
        Assert.Contains("RIGHT", r3, StringComparison.Ordinal);
        Assert.DoesNotContain("DIMNX", r3, StringComparison.Ordinal);
        Assert.DoesNotContain("DIMPX", r3, StringComparison.Ordinal);
        Assert.True(TimberFramedBlockContentVariantRules.IsProductionR3Combined(r3));
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsProductionApplicableBlockContent(r3, true, true, true));
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsR3ContentVariantOnlyPath(r3));
        Assert.False(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsLegacyR2FullNormalizePath(r3));
    }
}
