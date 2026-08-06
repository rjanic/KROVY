using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentOrientationRulesTests
{
    private const double Tol =
        TimberFramedBlockContentDefinitionRules.GeometryToleranceMm;

    public static IEnumerable<object[]> CardinalAndNearAngles()
    {
        // Raw element-axis degrees exercised by create TransformBy / classifier.
        yield return [0d];
        yield return [90d];
        yield return [180d];
        yield return [270d];
        yield return [89.999d];
        yield return [90.001d];
        yield return [179.999d];
        yield return [180.001d];
        yield return [269.999d];
        yield return [270.001d];
        yield return [35d];
    }

    [Theory]
    [MemberData(nameof(CardinalAndNearAngles))]
    public void EffectiveRotation_MatchesCreateReadabilityFold(double degrees)
    {
        var raw = degrees * Math.PI / 180d;
        var expected =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(raw);
        var effective =
            TimberFramedBlockContentOrientationRules
                .ResolveEffectiveBlockContentRotationRadians(raw);
        Assert.Equal(expected, effective, 12);
    }

    [Theory]
    [InlineData(0d, 1d, 0d)]
    [InlineData(90d, 0d, 1d)]
    [InlineData(180d, 1d, 0d)] // readable 0 → +X
    [InlineData(270d, 0d, 1d)] // readable +90 → +Y (create contract)
    [InlineData(35d, /* cos35 */ 0.8191520442889918d, /* sin35 */ 0.5735764363510461d)]
    public void EffectiveLocalXAxis_MatchesReadableOrientation(
        double degrees,
        double expectedX,
        double expectedY)
    {
        var effective =
            TimberFramedBlockContentOrientationRules
                .ResolveEffectiveBlockContentRotationRadians(degrees * Math.PI / 180d);
        var axis = TimberFramedBlockContentOrientationRules
            .ResolveEffectiveBlockLocalXAxis(effective);
        Assert.Equal(expectedX, axis.X, 9);
        Assert.Equal(expectedY, axis.Y, 9);
    }

    [Fact]
    public void PreferAttrRefRotation_WhenBlockRotationStaysZeroAt90()
    {
        var effective =
            TimberFramedBlockContentOrientationRules
                .ResolveEffectiveBlockContentRotationRadians(
                    blockRotationRadians: 0d,
                    attributeRotationRadians: Math.PI / 2d);
        Assert.Equal(Math.PI / 2d, effective, 12);

        var axis = TimberFramedBlockContentOrientationRules
            .ResolveEffectiveBlockLocalXAxis(0d, Math.PI / 2d);
        Assert.Equal(0d, axis.X, 12);
        Assert.Equal(1d, axis.Y, 12);
    }

    [Theory]
    [MemberData(nameof(CardinalAndNearAngles))]
    public void LandingAlongEffectiveLocalX_IsClassifiable(double degrees)
    {
        var theta = TimberFramedBlockContentOrientationRules
            .ResolveEffectiveBlockContentRotationRadians(degrees * Math.PI / 180d);
        var axis = TimberFramedBlockContentOrientationRules
            .ResolveEffectiveBlockLocalXAxis(theta);

        // Landing along effective +local X (create / correct dogleg).
        var landingX = 550d * axis.X;
        var landingY = 550d * axis.Y;

        Assert.True(
            TimberFramedBlockContentOrientationRules
                .TryClassifyRequiredDimensionColumnSide(
                    landingX,
                    landingY,
                    axis.X,
                    axis.Y,
                    Tol,
                    out var required,
                    out var contentLocalX,
                    out var length,
                    out var failure));
        Assert.Equal(
            TimberFramedBlockContentLandingClassifyFailure.None,
            failure);
        Assert.Equal(550d, length, 9);
        Assert.Equal(550d, contentLocalX, 6);
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX,
            required);
    }

    [Fact]
    public void Exact90_WithWrongBlockRotationAxis_WouldBeMismatch_ButEffectiveAxisWorks()
    {
        // Host no-go: BP−knee=(0,550), BlockRotation=0 → world +X projection ~0.
        Assert.False(
            TimberFramedBlockContentOrientationRules
                .TryClassifyRequiredDimensionColumnSide(
                    landingWorldX: 0d,
                    landingWorldY: 550d,
                    effectiveLocalXAxisX: 1d,
                    effectiveLocalXAxisY: 0d,
                    Tol,
                    out _,
                    out _,
                    out var length,
                    out var failure));
        Assert.Equal(550d, length, 12);
        Assert.Equal(
            TimberFramedBlockContentLandingClassifyFailure.EffectiveOrientationMismatch,
            failure);
        Assert.Equal(
            "Effective content orientation mismatch.",
            TimberFramedBlockContentOrientationRules.DescribeClassifyFailure(failure));

        // Effective AttrRef orientation π/2 → classifiable.
        Assert.True(
            TimberFramedBlockContentOrientationRules
                .TryClassifyRequiredDimensionColumnSide(
                    landingWorldX: 0d,
                    landingWorldY: 550d,
                    effectiveLocalXAxisX: 0d,
                    effectiveLocalXAxisY: 1d,
                    Tol,
                    out var required,
                    out var contentLocalX,
                    out _,
                    out _));
        Assert.Equal(550d, contentLocalX, 12);
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX,
            required);
    }

    [Fact]
    public void TrueDegeneracy_OnlyWhenLandingLengthNearZero()
    {
        Assert.False(
            TimberFramedBlockContentOrientationRules
                .TryClassifyRequiredDimensionColumnSide(
                    0d,
                    0d,
                    1d,
                    0d,
                    Tol,
                    out _,
                    out _,
                    out var length,
                    out var failure));
        Assert.Equal(0d, length, 12);
        Assert.Equal(
            TimberFramedBlockContentLandingClassifyFailure.DegenerateLandingLength,
            failure);
        Assert.Contains(
            "landing length",
            TimberFramedBlockContentOrientationRules.DescribeClassifyFailure(failure),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(CardinalAndNearAngles))]
    public void WrongLandingSide_RequiresOppositeColumn(double degrees)
    {
        var effective =
            TimberFramedBlockContentOrientationRules
                .ResolveEffectiveBlockContentRotationRadians(degrees * Math.PI / 180d);
        var axis = TimberFramedBlockContentOrientationRules
            .ResolveEffectiveBlockLocalXAxis(effective);

        // Landing along −local X → PositiveLocalX dimensions.
        Assert.True(
            TimberFramedBlockContentOrientationRules
                .TryClassifyRequiredDimensionColumnSide(
                    -550d * axis.X,
                    -550d * axis.Y,
                    axis.X,
                    axis.Y,
                    Tol,
                    out var required,
                    out var contentLocalX,
                    out _,
                    out _));
        Assert.True(contentLocalX < 0d);
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnSide.PositiveLocalX,
            required);
    }

    [Theory]
    [InlineData(TimberFramedBlockContentKind.Plain)]
    [InlineData(TimberFramedBlockContentKind.Circle)]
    [InlineData(TimberFramedBlockContentKind.Rectangle)]
    [InlineData(TimberFramedBlockContentKind.Slot)]
    public void CombinedSwapPolicy_DimnxDimpx_IdempotentAcrossKinds(
        TimberFramedBlockContentKind kind)
    {
        foreach (var degrees in new[]
                 {
                     0d, 90d, 180d, 270d, 35d,
                     89.999d, 90.001d, 179.999d, 180.001d, 269.999d, 270.001d,
                 })
        {
            var effective =
                TimberFramedBlockContentOrientationRules
                    .ResolveEffectiveBlockContentRotationRadians(
                        degrees * Math.PI / 180d);
            var axis = TimberFramedBlockContentOrientationRules
                .ResolveEffectiveBlockLocalXAxis(effective);
            Assert.True(
                TimberFramedBlockContentOrientationRules
                    .TryClassifyRequiredDimensionColumnSide(
                        550d * axis.X,
                        550d * axis.Y,
                        axis.X,
                        axis.Y,
                        Tol,
                        out var required,
                        out _,
                        out _,
                        out _));

            var currentCorrect = required;
            var currentWrong =
                TimberFramedBlockContentStretchNormalizeRules.OppositeColumnSide(
                    required);

            Assert.True(
                TimberFramedBlockContentStretchNormalizeRules.IsContentSideNoOp(
                    TimberFramedBlockContentDimensionColumnMirrorDecision.NoOp),
                $"{kind} @ {degrees}° correct → changed=False");
            Assert.True(
                TimberFramedBlockContentStretchNormalizeRules.ShouldSwapContentSide(
                    TimberFramedBlockContentDimensionColumnMirrorDecision.Swap),
                $"{kind} @ {degrees}° wrong → changed=True");
            Assert.False(
                TimberFramedBlockContentStretchNormalizeRules.IsContentSideNoOp(
                    TimberFramedBlockContentDimensionColumnMirrorDecision.Swap),
                $"{kind} @ {degrees}° wrong is not a no-op");

            // Second evaluation after swap: world-space correct → NoOp.
            Assert.True(
                TimberFramedBlockContentStretchNormalizeRules.IsContentSideNoOp(
                    TimberFramedBlockContentDimensionColumnMirrorDecision.NoOp),
                $"{kind} @ {degrees}° second → changed=False");

            // Parsed R2 names still encode DIMNX/DIMPX for Ensure targets.
            var nameCorrect = CombinedName(kind, currentCorrect);
            var nameWrong = CombinedName(kind, currentWrong);
            Assert.True(
                TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                    nameCorrect,
                    out var parseCorrect));
            Assert.Equal(currentCorrect, parseCorrect.DimensionColumnSide);
            Assert.True(
                TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                    nameWrong,
                    out var parseWrong));
            Assert.Equal(currentWrong, parseWrong.DimensionColumnSide);
            Assert.True(
                TimberFramedBlockContentStretchNormalizeRules.IsEligibleBlockContent(
                    nameCorrect,
                    hasItemNo: true,
                    hasWidth: true,
                    hasHeight: true));
        }
    }

    [Fact]
    public void ItemOnly_IsNotContentSideNormalizeTarget()
    {
        var itemOnly = TimberFramedBlockContentVariantRules.CreateSafeBlockName(
            TimberFramedBlockContentVariantRules.CreateRawKey(
                TimberFramedBlockContentKind.Circle,
                "MEDIUM",
                "Standard",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.ItemOnly));
        Assert.False(
            TimberFramedBlockContentStretchNormalizeRules.IsEligibleBlockContent(
                itemOnly,
                hasItemNo: true,
                hasWidth: false,
                hasHeight: false));
    }

    private static string CombinedName(
        TimberFramedBlockContentKind kind,
        TimberFramedBlockContentDimensionColumnSide side)
    {
        var size = kind == TimberFramedBlockContentKind.Plain ? "NONE" : "MEDIUM";
        return TimberFramedBlockContentVariantRules.CreateSafeBlockName(
            TimberFramedBlockContentVariantRules.CreateRawKey(
                kind,
                size,
                "Standard",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.Combined,
                side));
    }
}
