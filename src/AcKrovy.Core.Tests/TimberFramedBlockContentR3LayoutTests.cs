using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentR3LayoutTests
{
    private const double FrameWidthMm = 800d;
    private const double DimensionPaperHeightMm = 2.5d;
    private const double LandingLengthMm = 1200d;

    [Fact]
    public void A_R3Right_PassLayoutSnapshot_UsesNegativeOffset()
    {
        var right = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            TimberFramedBlockContentKind.Circle,
            FrameWidthMm,
            DimensionPaperHeightMm);
        var offset =
            TimberFramedBlockContentDefinitionRules.CalculateDimensionColumnOffsetMm(
                TimberFramedBlockContentKind.Circle,
                FrameWidthMm,
                DimensionPaperHeightMm);

        Assert.Equal(0d, right.FrameCenter.X, 9);
        Assert.Equal(0d, right.FrameCenter.Y, 9);
        Assert.Equal(right.ItemNo, right.FrameCenter);
        Assert.Equal(-offset, right.DimensionColumnLocalX, 9);
        Assert.Equal(-offset, right.Width.X, 9);
        Assert.Equal(-offset, right.Height.X, 9);
        Assert.Equal(0d, right.TextRotationRadians, 9);
        Assert.True(right.WidthLocalY > 0d);
        Assert.True(right.HeightLocalY < 0d);
        Assert.Equal(right.WidthLocalY, -right.HeightLocalY, 9);
    }

    [Fact]
    public void B_R3Left_IsExactMirroredLocalXOfRight()
    {
        var right = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            TimberFramedBlockContentKind.Circle,
            FrameWidthMm,
            DimensionPaperHeightMm);
        var left = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
            TimberFramedBlockContentKind.Circle,
            FrameWidthMm,
            DimensionPaperHeightMm);

        Assert.Equal(right.FrameCenter, left.FrameCenter);
        Assert.Equal(right.ItemNo, left.ItemNo);
        Assert.Equal(-right.DimensionColumnLocalX, left.DimensionColumnLocalX, 9);
        Assert.Equal(-right.Width.X, left.Width.X, 9);
        Assert.Equal(-right.Height.X, left.Height.X, 9);
        Assert.Equal(right.Width.Y, left.Width.Y, 9);
        Assert.Equal(right.Height.Y, left.Height.Y, 9);
        Assert.NotEqual(right.Side, left.Side);
    }

    [Fact]
    public void C_AbsOffsetsEqual()
    {
        var rightX =
            TimberFramedBlockContentDefinitionRules.CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Circle,
                FrameWidthMm,
                DimensionPaperHeightMm,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide);
        var leftX =
            TimberFramedBlockContentDefinitionRules.CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Circle,
                FrameWidthMm,
                DimensionPaperHeightMm,
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide);
        var offset =
            TimberFramedBlockContentDefinitionRules.CalculateDimensionColumnOffsetMm(
                TimberFramedBlockContentKind.Circle,
                FrameWidthMm,
                DimensionPaperHeightMm);

        Assert.Equal(offset, Math.Abs(rightX), 9);
        Assert.Equal(offset, Math.Abs(leftX), 9);
        Assert.Equal(Math.Abs(rightX), Math.Abs(leftX), 9);
        Assert.True(rightX < 0d);
        Assert.True(leftX > 0d);
    }

    [Fact]
    public void D_TextRotationIdenticalAndReadable()
    {
        var right = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            TimberFramedBlockContentKind.Slot,
            FrameWidthMm,
            DimensionPaperHeightMm);
        var left = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
            TimberFramedBlockContentKind.Slot,
            FrameWidthMm,
            DimensionPaperHeightMm);

        Assert.Equal(0d, right.TextRotationRadians, 9);
        Assert.Equal(0d, left.TextRotationRadians, 9);
        Assert.Equal(right.TextRotationRadians, left.TextRotationRadians, 9);
    }

    [Fact]
    public void E_FrameAndItemNoIdentical()
    {
        var right = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            TimberFramedBlockContentKind.Rectangle,
            FrameWidthMm,
            DimensionPaperHeightMm);
        var left = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
            TimberFramedBlockContentKind.Rectangle,
            FrameWidthMm,
            DimensionPaperHeightMm);

        Assert.Equal(right.FrameCenter, left.FrameCenter);
        Assert.Equal(right.ItemNo, left.ItemNo);
        Assert.Equal(0d, right.FrameCenter.X, 9);
        Assert.Equal(0d, right.FrameCenter.Y, 9);
        Assert.Equal(right.ItemNo, right.FrameCenter);
        Assert.Equal(left.ItemNo, left.FrameCenter);
        Assert.Equal(right.WidthLocalY, left.WidthLocalY, 9);
        Assert.Equal(right.HeightLocalY, left.HeightLocalY, 9);
    }

    [Fact]
    public void F_RightLeftRight_ReturnsExactOriginalContentLayout()
    {
        var original = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            TimberFramedBlockContentKind.Circle,
            FrameWidthMm,
            DimensionPaperHeightMm);
        var viaLeft = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
            TimberFramedBlockContentKind.Circle,
            FrameWidthMm,
            DimensionPaperHeightMm);
        var restored = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            TimberFramedBlockContentKind.Circle,
            FrameWidthMm,
            DimensionPaperHeightMm);

        Assert.NotEqual(original.DimensionColumnLocalX, viaLeft.DimensionColumnLocalX);
        Assert.Equal(original, restored);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        Assert.Equal(original.DimensionColumnLocalX, restored.DimensionColumnLocalX, 9);
    }

    [Fact]
    public void G_LeaderVerticesUnchangedDuringVariantSwap_Contract()
    {
        // Variant swap changes only BTR AttrDef column X; leader K→D→I world
        // vertices are ModelSpace geometry and must stay untouched by layout bake.
        var right = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            TimberFramedBlockContentKind.Circle,
            FrameWidthMm,
            DimensionPaperHeightMm);
        var left = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
            TimberFramedBlockContentKind.Circle,
            FrameWidthMm,
            DimensionPaperHeightMm);

        Assert.Equal(right.FrameCenter, left.FrameCenter);
        Assert.Equal(right.ItemNo, left.ItemNo);
        Assert.Equal(
            Math.Abs(right.DimensionColumnLocalX),
            Math.Abs(left.DimensionColumnLocalX),
            9);
        Assert.Equal(
            -TimberFramedBlockContentDefinitionRules.ResolveDimensionColumnLocalXSign(
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide),
            TimberFramedBlockContentDefinitionRules.ResolveDimensionColumnLocalXSign(
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide),
            9);
    }

    [Fact]
    public void CreateR3Layout_MatchesAttrDefHelpers_NoIndependentMagicNumbers()
    {
        foreach (var side in new[]
                 {
                     TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
                     TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
                 })
        {
            var layout = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
                side,
                TimberFramedBlockContentKind.Circle,
                FrameWidthMm,
                DimensionPaperHeightMm);
            Assert.Equal(
                TimberFramedBlockContentDefinitionRules.WidthAttributeLocalPoint(
                    TimberFramedBlockContentKind.Circle,
                    FrameWidthMm,
                    DimensionPaperHeightMm,
                    side),
                layout.Width);
            Assert.Equal(
                TimberFramedBlockContentDefinitionRules.HeightAttributeLocalPoint(
                    TimberFramedBlockContentKind.Circle,
                    FrameWidthMm,
                    DimensionPaperHeightMm,
                    side),
                layout.Height);
        }
    }

    [Fact]
    public void WorldSpace_RightAndLeft_DimensionsTowardKnee_EqualAbsDistance()
    {
        // BlockRotation=0: world = BlockPosition + local.
        // RIGHT PASS: knee on −X of frame → −offset column.
        var right = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            TimberFramedBlockContentKind.Circle,
            FrameWidthMm,
            DimensionPaperHeightMm);
        var frame = new TimberPlanarPoint(5000d, 3000d);
        var rightKnee = new TimberPlanarPoint(frame.X - LandingLengthMm, frame.Y);
        var rightDims = TimberFramedBlockContentDefinitionRules.ToWorldFromBlockLocal(
            new TimberPlanarPoint(right.DimensionColumnLocalX, 0d),
            frame,
            blockRotationRadians: 0d);

        Assert.True(
            TimberFramedBlockContentDefinitionRules.AreDimensionsTowardKnee(
                frame,
                rightKnee,
                rightDims));
        Assert.True(
            TimberFramedBlockContentDefinitionRules.TryEvaluateDimensionsTowardKneeDot(
                frame,
                rightKnee,
                rightDims,
                out var rightDot));
        Assert.True(rightDot > 0d);

        // LEFT: knee on +X of frame → +offset column (same |frame→dims|).
        var left = TimberFramedBlockContentDefinitionRules.CreateR3Layout(
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
            TimberFramedBlockContentKind.Circle,
            FrameWidthMm,
            DimensionPaperHeightMm);
        var leftKnee = new TimberPlanarPoint(frame.X + LandingLengthMm, frame.Y);
        var leftDims = TimberFramedBlockContentDefinitionRules.ToWorldFromBlockLocal(
            new TimberPlanarPoint(left.DimensionColumnLocalX, 0d),
            frame,
            blockRotationRadians: 0d);

        Assert.True(
            TimberFramedBlockContentDefinitionRules.AreDimensionsTowardKnee(
                frame,
                leftKnee,
                leftDims));
        Assert.True(
            TimberFramedBlockContentDefinitionRules.TryEvaluateDimensionsTowardKneeDot(
                frame,
                leftKnee,
                leftDims,
                out var leftDot));
        Assert.True(leftDot > 0d);

        var rightDist = Math.Abs(rightDims.X - frame.X);
        var leftDist = Math.Abs(leftDims.X - frame.X);
        Assert.Equal(rightDist, leftDist, 9);
        Assert.Equal(
            Math.Abs(right.DimensionColumnLocalX),
            Math.Abs(left.DimensionColumnLocalX),
            9);

        // Broken LEFT with −offset (old world-L/R mistake): D ---- F ---- K.
        var brokenLeftDims =
            TimberFramedBlockContentDefinitionRules.ToWorldFromBlockLocal(
                new TimberPlanarPoint(right.DimensionColumnLocalX, 0d),
                frame,
                blockRotationRadians: 0d);
        Assert.False(
            TimberFramedBlockContentDefinitionRules.AreDimensionsTowardKnee(
                frame,
                leftKnee,
                brokenLeftDims));
    }

    [Fact]
    public void ResolveRequiredVariant_FromKneeFrameLanding_NotWorldSide()
    {
        // Landing along +local X (create / PASS): requires R3_RIGHT (−offset),
        // even if annotation sits on world Left of the source.
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryResolveRequiredContentVariant(
                kneeX: 0d,
                kneeY: 0d,
                frameCenterX: 550d,
                frameCenterY: 0d,
                effectiveLocalXAxisX: 1d,
                effectiveLocalXAxisY: 0d,
                out var rightRequired,
                out var contentLocalX,
                out _));
        Assert.True(contentLocalX > 0d);
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            rightRequired);

        // Knee reflected through frame: landing along −local X → R3_LEFT.
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryResolveRequiredContentVariant(
                kneeX: 1100d,
                kneeY: 0d,
                frameCenterX: 550d,
                frameCenterY: 0d,
                effectiveLocalXAxisX: 1d,
                effectiveLocalXAxisY: 0d,
                out var leftRequired,
                out var leftContentLocalX,
                out _));
        Assert.True(leftContentLocalX < 0d);
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
            leftRequired);
    }

    [Fact]
    public void RightCreate_OnLanding_UsesNegativeColumn_LeftMirrorPositive()
    {
        var rightKnee = new TimberPlanarPoint(0d, 0d);
        var rightItem = new TimberPlanarPoint(1200d, 0d);
        var columnX =
            TimberFramedBlockContentDefinitionRules.CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Circle,
                FrameWidthMm,
                DimensionPaperHeightMm,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide);
        var rightDims = new TimberPlanarPoint(rightItem.X + columnX, 0d);
        Assert.True(columnX < 0d);
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryEvaluateLandingDimensionColumn(
                rightKnee,
                rightItem,
                rightDims,
                out var rightT,
                out var rightOnLanding));
        Assert.True(rightOnLanding);
        Assert.True(rightT > 0d && rightT < 1d);

        var leftColumnX =
            TimberFramedBlockContentDefinitionRules.CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Circle,
                FrameWidthMm,
                DimensionPaperHeightMm,
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide);
        Assert.Equal(-columnX, leftColumnX, 9);
        Assert.True(leftColumnX > 0d);

        var leftKnee = new TimberPlanarPoint(2400d, 0d);
        var leftDims = new TimberPlanarPoint(rightItem.X + leftColumnX, 0d);
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryEvaluateLandingDimensionColumn(
                leftKnee,
                rightItem,
                leftDims,
                out var leftT,
                out var leftOnLanding));
        Assert.True(leftOnLanding);
        Assert.True(leftT > 0d && leftT < 1d);
        Assert.True(
            TimberFramedBlockContentDefinitionRules.AreDimensionsTowardKnee(
                rightItem,
                leftKnee,
                leftDims));
    }
}
