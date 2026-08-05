using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentDoglegRulesTests
{
    private static readonly TimberPlanarVector PlusX = new(1d, 0d);

    [Fact]
    public void GoodRight_IsPureNoOp()
    {
        // Host-observed good RIGHT (diag handle family / scale 2 circle).
        var attachment = new TimberPlanarPoint(25000d, 5000d);
        var knee = new TimberPlanarPoint(25576.72d, 4216.43d);
        var blockPosition = new TimberPlanarPoint(26809.53d, 4216.43d);
        const double doglegLength = 832.81d;

        Assert.True(
            TimberFramedBlockContentDoglegRules.TryResolveLeaderKneeSide(
                attachment,
                knee,
                PlusX,
                out var kneeSide));
        Assert.Equal(TimberLeaderTangentSign.PositiveT, kneeSide);

        Assert.True(
            TimberFramedBlockContentDoglegRules.TryResolveContentDoglegSide(
                knee,
                blockPosition,
                PlusX,
                out var contentSide));
        Assert.Equal(TimberLeaderTangentSign.PositiveT, contentSide);

        Assert.False(
            TimberFramedBlockContentDoglegRules.LandingPointsTowardAttachment(
                attachment,
                knee,
                blockPosition));

        Assert.True(
            TimberFramedBlockContentDoglegRules.TryNormalizeDoglegGeometry(
                attachment,
                knee,
                blockPosition,
                out var direction,
                out var normalizedBp,
                out var mirrored));

        Assert.False(mirrored);
        Assert.Equal(blockPosition.X, normalizedBp.X, 9);
        Assert.Equal(blockPosition.Y, normalizedBp.Y, 9);
        Assert.Equal(1d, direction.X, 9);
        Assert.Equal(0d, direction.Y, 9);

        // ConnectBase: |BP−knee| − DoglegLength == half-frame×scale (400), not zero.
        var offset =
            TimberFramedBlockContentDoglegRules.MeasureConnectBaseContentOffsetMm(
                knee,
                blockPosition,
                doglegLength);
        Assert.Equal(400d, offset, 2);
        Assert.NotEqual(knee.X + doglegLength, blockPosition.X);
    }

    [Fact]
    public void GoodRight_WithContentOnMinusT_IsStillNoOp()
    {
        // Host NO-GO case shape: LeaderKneeSide PositiveT can coexist with
        // DoglegDirection −T when landing does NOT point toward attachment.
        var attachment = new TimberPlanarPoint(15000d, 15642.2693d);
        var knee = new TimberPlanarPoint(14775.3694d, 15642.2693d);
        var blockPosition = new TimberPlanarPoint(13675.3694d, 15642.2693d);

        Assert.True(
            TimberFramedBlockContentDoglegRules.TryResolveLeaderKneeSide(
                attachment,
                knee,
                PlusX,
                out var kneeSide));
        // knee.X < attachment.X → NegativeT on +X T; use −X so this matches
        // PositiveT when T was taken from content ray.
        Assert.Equal(TimberLeaderTangentSign.NegativeT, kneeSide);

        var minusX = new TimberPlanarVector(-1d, 0d);
        Assert.True(
            TimberFramedBlockContentDoglegRules.TryResolveLeaderKneeSide(
                attachment,
                knee,
                minusX,
                out var kneeSideOnContentT));
        Assert.Equal(TimberLeaderTangentSign.PositiveT, kneeSideOnContentT);

        Assert.False(
            TimberFramedBlockContentDoglegRules.LandingPointsTowardAttachment(
                attachment,
                knee,
                blockPosition));

        Assert.True(
            TimberFramedBlockContentDoglegRules.TryNormalizeDoglegGeometry(
                attachment,
                knee,
                blockPosition,
                out var direction,
                out var normalizedBp,
                out var mirrored));

        Assert.False(mirrored);
        Assert.Equal(blockPosition.X, normalizedBp.X, 9);
        Assert.Equal(-1d, direction.X, 9);
    }

    [Fact]
    public void BadLeft_MirrorsBlockPositionAcrossKnee_PreservingDistance()
    {
        // Host-observed bad LEFT: knee left of attachment, dogleg still +X.
        var attachment = new TimberPlanarPoint(25000d, 5000d);
        var knee = new TimberPlanarPoint(24160d, 4376.46d);
        var blockPosition = new TimberPlanarPoint(26060d, 4376.46d);
        var distance = Math.Sqrt(
            Math.Pow(blockPosition.X - knee.X, 2d) +
            Math.Pow(blockPosition.Y - knee.Y, 2d));

        Assert.True(
            TimberFramedBlockContentDoglegRules.LandingPointsTowardAttachment(
                attachment,
                knee,
                blockPosition));

        Assert.True(
            TimberFramedBlockContentDoglegRules.TryNormalizeDoglegGeometry(
                attachment,
                knee,
                blockPosition,
                out var direction,
                out var normalizedBp,
                out var mirrored));

        Assert.True(mirrored);
        Assert.Equal(-1d, direction.X, 9);
        Assert.Equal(0d, direction.Y, 9);
        Assert.Equal(knee.X - (blockPosition.X - knee.X), normalizedBp.X, 9);
        Assert.Equal(knee.Y, normalizedBp.Y, 9);

        var newDistance = Math.Sqrt(
            Math.Pow(normalizedBp.X - knee.X, 2d) +
            Math.Pow(normalizedBp.Y - knee.Y, 2d));
        Assert.Equal(distance, newDistance, 9);

        // Landing must not cross back through the attachment along +X.
        Assert.True(normalizedBp.X < knee.X);
        Assert.True(knee.X < attachment.X);
        Assert.False(
            TimberFramedBlockContentDoglegRules.LandingPointsTowardAttachment(
                attachment,
                knee,
                normalizedBp));

        // Second normalize is idempotent.
        Assert.True(
            TimberFramedBlockContentDoglegRules.TryNormalizeDoglegGeometry(
                attachment,
                knee,
                normalizedBp,
                out _,
                out var secondBp,
                out var secondMirrored));
        Assert.False(secondMirrored);
        Assert.Equal(normalizedBp.X, secondBp.X, 9);
    }

    [Fact]
    public void LeaderKneeSide_DoesNotDictateDoglegDirection()
    {
        var attachment = new TimberPlanarPoint(0d, 0d);
        var knee = new TimberPlanarPoint(500d, -100d);
        var blockPosition = new TimberPlanarPoint(100d, -100d); // content on −T

        Assert.True(
            TimberFramedBlockContentDoglegRules.TryResolveLeaderKneeSide(
                attachment,
                knee,
                PlusX,
                out var kneeSide));
        Assert.Equal(TimberLeaderTangentSign.PositiveT, kneeSide);

        Assert.True(
            TimberFramedBlockContentDoglegRules.TryResolveContentDoglegSide(
                knee,
                blockPosition,
                PlusX,
                out var contentSide));
        Assert.Equal(TimberLeaderTangentSign.NegativeT, contentSide);

        Assert.True(
            TimberFramedBlockContentDoglegRules.TryResolveContentDoglegDirection(
                knee,
                blockPosition,
                out var direction));
        Assert.Equal(-1d, direction.X, 9);
    }

    [Theory]
    [InlineData(500d, -500d, true)]
    [InlineData(-500d, 500d, true)]
    [InlineData(500d, 500d, false)]
    [InlineData(-500d, -500d, false)]
    public void CrossingDetection_MatchesSameSideOfKnee(
        double kneeDeltaX,
        double blockDeltaFromKneeX,
        bool expectTowardAttachment)
    {
        var attachment = new TimberPlanarPoint(0d, 0d);
        var knee = new TimberPlanarPoint(kneeDeltaX, -100d);
        var block = new TimberPlanarPoint(knee.X + blockDeltaFromKneeX, knee.Y);

        Assert.Equal(
            expectTowardAttachment,
            TimberFramedBlockContentDoglegRules.LandingPointsTowardAttachment(
                attachment,
                knee,
                block));
    }

    [Fact]
    public void NearZeroProjection_IsAmbiguous()
    {
        var attachment = new TimberPlanarPoint(0d, 0d);
        var knee = new TimberPlanarPoint(0.5d, 400d);

        Assert.False(
            TimberFramedBlockContentDoglegRules.TryResolveLeaderKneeSide(
                attachment,
                knee,
                PlusX,
                out _));
    }

    [Fact]
    public void CreateLayout_UsesLandingEndNotKneeSide()
    {
        var left = TimberFramedBlockContentLayoutCalculator.Calculate(
            new TimberFramedBlockContentLayoutRequest(
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
                1000d,
                800d,
                200d,
                TimberFramedBlockContentPresentation.Combined));
        var right = TimberFramedBlockContentLayoutCalculator.Calculate(
            new TimberFramedBlockContentLayoutRequest(
                0d,
                0d,
                0d,
                TimberLeaderHorizontalSide.Right,
                TimberFramedBlockContentKind.Circle,
                TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
                TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
                50,
                2.7d,
                2.5d,
                1000d,
                800d,
                200d,
                TimberFramedBlockContentPresentation.Combined));

        Assert.True(left.KneeLocal.X > left.AttachmentLocal.X);
        Assert.True(right.KneeLocal.X > right.AttachmentLocal.X);

        Assert.True(
            TimberFramedBlockContentDoglegRules.TryResolveCreateDoglegGeometry(
                left.KneeLocal,
                left.LandingEndLocal,
                out var leftDir,
                out var leftBlock));
        Assert.True(
            TimberFramedBlockContentDoglegRules.TryResolveCreateDoglegGeometry(
                right.KneeLocal,
                right.LandingEndLocal,
                out var rightDir,
                out var rightBlock));

        Assert.Equal(1d, leftDir.X, 9);
        Assert.Equal(1d, rightDir.X, 9);
        Assert.Equal(left.LandingEndLocal.X, leftBlock.X, 9);
        Assert.Equal(right.LandingEndLocal.X, rightBlock.X, 9);
        Assert.False(
            TimberFramedBlockContentDoglegRules.LandingPointsTowardAttachment(
                left.AttachmentLocal,
                left.KneeLocal,
                leftBlock));
        Assert.False(
            TimberFramedBlockContentDoglegRules.LandingPointsTowardAttachment(
                right.AttachmentLocal,
                right.KneeLocal,
                rightBlock));

        Assert.Equal(left.DimensionColumnLocalX, right.DimensionColumnLocalX, 9);
        Assert.True(left.DimensionColumnLocalX < 0d);
    }

    [Fact]
    public void Layout_PositiveColumnSide_MirrorsDimensionLocalX()
    {
        var negative = TimberFramedBlockContentLayoutCalculator.Calculate(
            new TimberFramedBlockContentLayoutRequest(
                0d,
                0d,
                0d,
                TimberLeaderHorizontalSide.Right,
                TimberFramedBlockContentKind.Circle,
                TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
                TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
                50,
                2.7d,
                2.5d,
                1000d,
                800d,
                200d,
                TimberFramedBlockContentPresentation.Combined,
                TimberFramedBlockContentDimensionColumnSide.NegativeLocalX));
        var positive = TimberFramedBlockContentLayoutCalculator.Calculate(
            new TimberFramedBlockContentLayoutRequest(
                0d,
                0d,
                0d,
                TimberLeaderHorizontalSide.Right,
                TimberFramedBlockContentKind.Circle,
                TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
                TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
                50,
                2.7d,
                2.5d,
                1000d,
                800d,
                200d,
                TimberFramedBlockContentPresentation.Combined,
                TimberFramedBlockContentDimensionColumnSide.PositiveLocalX));

        Assert.True(negative.DimensionColumnLocalX < 0d);
        Assert.True(positive.DimensionColumnLocalX > 0d);
        Assert.Equal(
            -negative.DimensionColumnLocalX,
            positive.DimensionColumnLocalX,
            9);
        Assert.Equal(negative.LandingEndLocal, positive.LandingEndLocal);
        Assert.Equal(
            negative.WidthCenterLocal!.Value.Y,
            positive.WidthCenterLocal!.Value.Y,
            9);
    }

    [Fact]
    public void VariantKey_UsesR2ColumnSideNotScreenLeftRight()
    {
        var key = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Circle,
            "SMALL",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);

        Assert.DoesNotContain("_L_", key, StringComparison.Ordinal);
        Assert.DoesNotContain("_LEFT", key, StringComparison.Ordinal);
        Assert.DoesNotContain("_RIGHT", key, StringComparison.Ordinal);
        Assert.Contains("_R2_", key, StringComparison.Ordinal);
        Assert.Contains(
            TimberFramedBlockContentVariantRules.DimensionsNegativeXToken,
            key,
            StringComparison.Ordinal);
        Assert.StartsWith("AK_KROVY_FBC_R2_CIR_", key, StringComparison.Ordinal);
    }
}
