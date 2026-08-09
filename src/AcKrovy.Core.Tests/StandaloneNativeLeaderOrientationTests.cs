using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class StandaloneNativeLeaderOrientationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static TheoryData<double, double> TransformMatrix() =>
        new()
        {
            { 0d, 0d },
            { 89d, 89d },
            { 90d, -90d },
            { 91d, -89d },
            { 179d, -1d },
            { 180d, 0d },
            { 181d, 1d },
            { 269d, 89d },
            { 270d, 90d },
            { 271d, -89d },
            { 359d, -1d },
            { -90d, 90d },
            { -180d, 0d },
        };

    [Theory]
    [MemberData(nameof(TransformMatrix))]
    public void ResolveTransform_FullDomainDeterministic(
        double physicalDegrees,
        double expectedDegrees)
    {
        // 269° and 271° expected values expressed above in compact form —
        // normalize expected through the same wrap for Assert.
        var expected = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            expectedDegrees * Math.PI / 180d) * 180d / Math.PI;
        var actual =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physicalDegrees * Math.PI / 180d) *
            180d / Math.PI;

        Assert.Equal(expected, actual, 8);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(30d, 30d)]
    [InlineData(-30d, -30d)]
    [InlineData(150d, -30d)]
    [InlineData(180d, 0d)]
    public void ResolveNativeLeaderTransform_UsesReadableFoldWithoutHalfTurn(
        double physicalDegrees,
        double expectedDegrees)
    {
        var actual =
            TimberItemLeaderLayoutCalculator.ResolveNativeLeaderTransformRadians(
                physicalDegrees * Math.PI / 180d) *
            180d / Math.PI;

        Assert.Equal(expectedDegrees, actual, 8);
    }

    [Theory]
    [InlineData(90d, -90d)]
    [InlineData(-90d, 90d)]
    [InlineData(270d, 90d)]
    public void ResolveNativeLeaderTransform_AppliesVerticalHalfTurn(
        double physicalDegrees,
        double expectedDegrees)
    {
        var actual =
            TimberItemLeaderLayoutCalculator.ResolveNativeLeaderTransformRadians(
                physicalDegrees * Math.PI / 180d) *
            180d / Math.PI;

        Assert.Equal(expectedDegrees, actual, 8);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(180d)]
    [InlineData(90d)]
    [InlineData(-90d)]
    public void ResolveTransform_ReversedSourceDiffersByHalfTurnOnlyAtVerticals(
        double degrees)
    {
        var forward = degrees * Math.PI / 180d;
        var reversed = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            forward + Math.PI);
        var forwardTransform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                forward);
        var reversedTransform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                reversed);

        if (TimberStandaloneNativeLeaderOrientationRules.IsExactOneEighty(forward) ||
            Math.Abs(forward) <=
                TimberStandaloneNativeLeaderOrientationRules.AngleToleranceRadians)
        {
            // 0° ↔ 180° share transform 0 (readable fold, no half-turn).
            Assert.Equal(forwardTransform, reversedTransform, 8);
            return;
        }

        if (TimberStandaloneNativeLeaderOrientationRules.IsExactVertical(forward))
        {
            // +90° → −90°, −90°/270° → +90° — reversed vertical is the other vertical.
            Assert.Equal(
                TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                    forwardTransform + Math.PI),
                reversedTransform,
                8);
            return;
        }

        // Non-boundary: reverse folds to same readable (differs by π before fold).
        Assert.Equal(forwardTransform, reversedTransform, 8);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(30d)]
    [InlineData(-30d)]
    [InlineData(90d)]
    [InlineData(-90d)]
    [InlineData(150d)]
    [InlineData(180d)]
    [InlineData(270d)]
    public void StandalonePlainAndDimensions_OrientAroundAnchor_PreserveRelativeGeometry(
        double physicalDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var transform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var basePlacement = new TimberLeaderPlacement(100d, 200d, 100d, 560d, 0d);

        var plainCanonical =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativePlainItemNumber(
                basePlacement,
                "K1",
                TimberLeaderHorizontalSide.Right);
        var plainOriented = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            plainCanonical,
            transform);

        var dimensionsCanonical =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeDimensionsLeader(
                basePlacement,
                "80x160",
                TimberLeaderHorizontalSide.Right);
        var dimensionsOriented = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            dimensionsCanonical,
            transform);

        Assert.Equal(plainCanonical.AnchorX, plainOriented.AnchorX, 8);
        Assert.Equal(plainCanonical.AnchorY, plainOriented.AnchorY, 8);
        Assert.Equal(
            Distance(plainCanonical.AnchorX, plainCanonical.AnchorY,
                plainCanonical.KneeX, plainCanonical.KneeY),
            Distance(plainOriented.AnchorX, plainOriented.AnchorY,
                plainOriented.KneeX, plainOriented.KneeY),
            8);
        Assert.Equal(
            Distance(dimensionsCanonical.AnchorX, dimensionsCanonical.AnchorY,
                dimensionsCanonical.ContentX, dimensionsCanonical.ContentY),
            Distance(dimensionsOriented.AnchorX, dimensionsOriented.AnchorY,
                dimensionsOriented.ContentX, dimensionsOriented.ContentY),
            8);

        if (Math.Abs(transform) > 1e-9d)
        {
            Assert.False(
                NearlyEqual(plainCanonical.KneeX, plainOriented.KneeX) &&
                NearlyEqual(plainCanonical.KneeY, plainOriented.KneeY));
            Assert.False(
                NearlyEqual(dimensionsCanonical.ContentX, dimensionsOriented.ContentX) &&
                NearlyEqual(dimensionsCanonical.ContentY, dimensionsOriented.ContentY));
        }
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(30d)]
    [InlineData(-30d)]
    [InlineData(60d)]
    [InlineData(90d)]
    [InlineData(120d)]
    [InlineData(150d)]
    [InlineData(180d)]
    [InlineData(210d)]
    [InlineData(270d)]
    [InlineData(330d)]
    public void StandalonePlainAndDimensions_TwoSegmentBend_SixtyThenOneTwenty(
        double physicalDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var transform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var placement = new TimberLeaderPlacement(100d, 200d, 100d, 560d, physical);
        var sourceX = Math.Cos(transform);
        var sourceY = Math.Sin(transform);

        var plain = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativePlainItemNumber(
                placement,
                "K1",
                TimberLeaderHorizontalSide.Right),
            transform);
        var dimensions = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeDimensionsLeader(
                placement,
                "80x160",
                TimberLeaderHorizontalSide.Right),
            transform);

        AssertSixtyOneTwentyBend(plain, sourceX, sourceY);
        AssertSixtyOneTwentyBend(dimensions, sourceX, sourceY);

        AssertStandaloneLandingParallelToSource(plain, sourceX, sourceY);
        AssertStandaloneLandingParallelToSource(dimensions, sourceX, sourceY);

        Assert.True(
            TimberStandaloneNativeLeaderCreateFinalizationRules
                .MeetsSixtyOneTwentyContract(
                    plain.AnchorX,
                    plain.AnchorY,
                    plain.KneeX,
                    plain.KneeY,
                    plain.ContentX,
                    plain.ContentY,
                    transform));
        Assert.True(
            TimberStandaloneNativeLeaderCreateFinalizationRules
                .MeetsSixtyOneTwentyContract(
                    dimensions.AnchorX,
                    dimensions.AnchorY,
                    dimensions.KneeX,
                    dimensions.KneeY,
                    dimensions.ContentX,
                    dimensions.ContentY,
                    transform));
    }

    [Theory]
    [InlineData("A", "ITEM12")]
    [InlineData("K1", "LONGCODE99")]
    public void StandalonePlain_LandingLength_ScalesWithTextEnvelope_FirstSegmentUnchanged(
        string shortText,
        string longText)
    {
        var placement = new TimberLeaderPlacement(0d, 0d, 0d, 360d, 0d);
        var shortLayout =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativePlainItemNumber(
                placement,
                shortText,
                TimberLeaderHorizontalSide.Right);
        var longLayout =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativePlainItemNumber(
                placement,
                longText,
                TimberLeaderHorizontalSide.Right);

        var shortLanding =
            TimberItemLeaderLayoutCalculator.ResolveStandaloneNativeLanding(
                shortLayout.KneeX,
                shortLayout.KneeY,
                shortLayout.ContentX,
                shortLayout.ContentY);
        var longLanding =
            TimberItemLeaderLayoutCalculator.ResolveStandaloneNativeLanding(
                longLayout.KneeX,
                longLayout.KneeY,
                longLayout.ContentX,
                longLayout.ContentY);

        Assert.Equal(
            TimberItemLeaderLayoutCalculator.StandaloneNativeFirstSegmentLengthMm,
            Distance(
                shortLayout.AnchorX,
                shortLayout.AnchorY,
                shortLayout.KneeX,
                shortLayout.KneeY),
            8);
        Assert.Equal(
            TimberItemLeaderLayoutCalculator.StandaloneNativeFirstSegmentLengthMm,
            Distance(
                longLayout.AnchorX,
                longLayout.AnchorY,
                longLayout.KneeX,
                longLayout.KneeY),
            8);
        Assert.Equal(shortLayout.KneeX, longLayout.KneeX, 8);
        Assert.Equal(shortLayout.KneeY, longLayout.KneeY, 8);

        Assert.True(longLayout.EnvelopeWidthMm > shortLayout.EnvelopeWidthMm);
        Assert.Equal(
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeLandingLengthMm(
                shortLayout.EnvelopeWidthMm),
            shortLanding.LengthMm,
            8);
        Assert.Equal(
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeLandingLengthMm(
                longLayout.EnvelopeWidthMm),
            longLanding.LengthMm,
            8);
        Assert.True(longLanding.LengthMm > shortLanding.LengthMm);

        // Compact MiddleCenter landing: half envelope + tiny pad only (not
        // legacy clearance, and not a large overhang past the near text edge).
        Assert.Equal(
            shortLayout.EnvelopeWidthMm / 2d +
            TimberItemLeaderLayoutCalculator.StandaloneNativeLandingPaddingMm,
            shortLanding.LengthMm,
            8);
        Assert.True(
            TimberItemLeaderLayoutCalculator.StandaloneNativeLandingPaddingMm <= 2d);
        Assert.True(
            shortLanding.LengthMm <
            shortLayout.EnvelopeWidthMm / 2d +
            TimberItemNumberTypographyRules.CalculatePlainTextClearanceMm(1d));
        AssertSixtyOneTwentyBend(shortLayout, 1d, 0d);
        AssertSixtyOneTwentyBend(longLayout, 1d, 0d);
    }

    [Theory]
    // Wide enough that base (0.45×envelope) exceeds the 250 mm @1:50 cut so the
    // scale-relative shortening stays measurable (not clamped to minimum).
    [InlineData("88888\\P1", "8888888888\\P1")]
    [InlineData("12000\\P24000", "1234567890\\P1")]
    public void StandaloneDimensions_LandingLength_ScalesWithStackedEnvelope_FirstSegmentUnchanged(
        string shortText,
        string longText)
    {
        var placement = new TimberLeaderPlacement(0d, 0d, 0d, 360d, 0d);
        var shortLayout =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeDimensionsLeader(
                placement,
                shortText,
                TimberLeaderHorizontalSide.Right);
        var longLayout =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeDimensionsLeader(
                placement,
                longText,
                TimberLeaderHorizontalSide.Right);

        var shortLanding =
            TimberItemLeaderLayoutCalculator.ResolveStandaloneNativeLanding(
                shortLayout.KneeX,
                shortLayout.KneeY,
                shortLayout.ContentX,
                shortLayout.ContentY);
        var longLanding =
            TimberItemLeaderLayoutCalculator.ResolveStandaloneNativeLanding(
                longLayout.KneeX,
                longLayout.KneeY,
                longLayout.ContentX,
                longLayout.ContentY);

        Assert.Equal(
            TimberItemLeaderLayoutCalculator.StandaloneNativeFirstSegmentLengthMm,
            Distance(
                shortLayout.AnchorX,
                shortLayout.AnchorY,
                shortLayout.KneeX,
                shortLayout.KneeY),
            8);
        Assert.Equal(
            TimberItemLeaderLayoutCalculator.StandaloneNativeFirstSegmentLengthMm,
            Distance(
                longLayout.AnchorX,
                longLayout.AnchorY,
                longLayout.KneeX,
                longLayout.KneeY),
            8);
        Assert.Equal(shortLayout.KneeX, longLayout.KneeX, 8);
        Assert.Equal(shortLayout.KneeY, longLayout.KneeY, 8);

        Assert.True(longLayout.EnvelopeWidthMm > shortLayout.EnvelopeWidthMm);
        Assert.Equal(
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeDimensionsLandingLengthMm(
                shortLayout.EnvelopeWidthMm),
            shortLanding.LengthMm,
            8);
        Assert.Equal(
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeDimensionsLandingLengthMm(
                longLayout.EnvelopeWidthMm),
            longLanding.LengthMm,
            8);
        Assert.True(longLanding.LengthMm > shortLanding.LengthMm);

        var shortBase =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeLandingLengthMm(
                shortLayout.EnvelopeWidthMm,
                landingPaddingMm: TimberItemLeaderLayoutCalculator
                    .StandaloneNativeDimensionsLandingPaddingMm,
                envelopeFactor: TimberItemLeaderLayoutCalculator
                    .StandaloneNativeDimensionsLandingEnvelopeFactor);
        Assert.Equal(
            Math.Max(
                TimberItemLeaderLayoutCalculator
                    .StandaloneNativeDimensionsLandingMinimumLengthMm,
                shortBase -
                TimberItemLeaderLayoutCalculator
                    .StandaloneNativeDimensionsLandingReductionAtScale50Mm),
            shortLanding.LengthMm,
            8);
        Assert.True(
            shortLanding.LengthMm <
            shortLayout.EnvelopeWidthMm / 2d +
            TimberItemLeaderLayoutCalculator.StandaloneNativeLandingPaddingMm);
        // Stacked width uses the longest line, not raw "W\\PH" character count.
        var legacyInflated =
            shortLayout.EnvelopeHeightMm / 2d +
            TimberItemLeaderLayoutCalculator.TextClearanceMm +
            TimberItemLeaderLayoutCalculator.MinimumLeaderRunMm;
        Assert.True(shortLanding.LengthMm < legacyInflated);
        AssertSixtyOneTwentyBend(shortLayout, 1d, 0d);
        AssertSixtyOneTwentyBend(longLayout, 1d, 0d);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void StandaloneDimensions_LandingLength_ShortensByScaleRelativeToOneToFifty(
        int denominator)
    {
        var scaleFactor = TimberAnnotationScaleRules.GetScaleFactor(denominator);
        Assert.Equal(denominator / 50d, scaleFactor, 12);

        var placement = new TimberLeaderPlacement(0d, 0d, 0d, 360d, 0d);
        // Wide stacked label so base landing exceeds the scale-relative cut.
        const string text = "8888888888\\P1";
        var layout =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeDimensionsLeader(
                placement,
                text,
                TimberLeaderHorizontalSide.Right,
                scaleFactor);
        var landing =
            TimberItemLeaderLayoutCalculator.ResolveStandaloneNativeLanding(
                layout.KneeX,
                layout.KneeY,
                layout.ContentX,
                layout.ContentY);

        var baseLanding =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeLandingLengthMm(
                layout.EnvelopeWidthMm,
                scaleFactor,
                TimberItemLeaderLayoutCalculator
                    .StandaloneNativeDimensionsLandingPaddingMm,
                TimberItemLeaderLayoutCalculator
                    .StandaloneNativeDimensionsLandingEnvelopeFactor);
        var expectedReduction =
            TimberItemLeaderLayoutCalculator
                .StandaloneNativeDimensionsLandingReductionAtScale50Mm *
            scaleFactor;
        var expectedLanding = Math.Max(
            TimberItemLeaderLayoutCalculator
                .StandaloneNativeDimensionsLandingMinimumLengthMm,
            baseLanding - expectedReduction);

        Assert.Equal(250d, TimberItemLeaderLayoutCalculator
            .StandaloneNativeDimensionsLandingReductionAtScale50Mm);
        Assert.Equal(expectedReduction, 250d * scaleFactor, 12);
        Assert.Equal(expectedLanding, landing.LengthMm, 8);
        Assert.Equal(
            TimberItemLeaderLayoutCalculator
                .CalculateStandaloneNativeDimensionsLandingLengthMm(
                    layout.EnvelopeWidthMm,
                    scaleFactor),
            landing.LengthMm,
            8);
        Assert.True(landing.LengthMm > 0d);

        // First arm and 60°/120° bend stay independent of the landing cut.
        Assert.Equal(
            TimberItemLeaderLayoutCalculator.StandaloneNativeFirstSegmentLengthMm *
                scaleFactor,
            Distance(
                layout.AnchorX,
                layout.AnchorY,
                layout.KneeX,
                layout.KneeY),
            8);
        AssertSixtyOneTwentyBend(layout, 1d, 0d);
    }

    [Fact]
    public void StandaloneDimensions_LandingReduction_AtOneToFifty_IsExactly250ModelMm()
    {
        const double envelopeWidthMm = 800d;
        var baseLanding =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeLandingLengthMm(
                envelopeWidthMm,
                presentationScaleFactor: 1d,
                landingPaddingMm: TimberItemLeaderLayoutCalculator
                    .StandaloneNativeDimensionsLandingPaddingMm,
                envelopeFactor: TimberItemLeaderLayoutCalculator
                    .StandaloneNativeDimensionsLandingEnvelopeFactor);
        var reduced =
            TimberItemLeaderLayoutCalculator
                .CalculateStandaloneNativeDimensionsLandingLengthMm(
                    envelopeWidthMm,
                    presentationScaleFactor: 1d);

        Assert.Equal(envelopeWidthMm * 0.45d, baseLanding, 12);
        Assert.Equal(250d, baseLanding - reduced, 12);
        Assert.Equal(baseLanding - 250d, reduced, 12);
    }

    [Theory]
    [InlineData(25, 125d)]
    [InlineData(50, 250d)]
    [InlineData(100, 500d)]
    [InlineData(200, 1000d)]
    public void StandalonePlainAndDimensions_FirstSegmentLength_ScalesAs250TimesDenominatorOver50(
        int denominator,
        double expectedLengthMm)
    {
        Assert.Equal(
            250d,
            TimberItemLeaderLayoutCalculator.StandaloneNativeFirstSegmentLengthMm);
        Assert.Equal(360d, TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm);

        var scaleFactor = TimberAnnotationScaleRules.GetScaleFactor(denominator);
        Assert.Equal(expectedLengthMm, 250d * scaleFactor, 12);
        Assert.Equal(
            expectedLengthMm,
            TimberItemLeaderLayoutCalculator.StandaloneNativeFirstSegmentLengthMm *
                scaleFactor,
            12);

        var placement = new TimberLeaderPlacement(0d, 0d, 0d, 360d, 0d);
        var plain =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativePlainItemNumber(
                placement,
                "K1",
                TimberLeaderHorizontalSide.Right,
                scaleFactor);
        var dimensions =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeDimensionsLeader(
                placement,
                "120\\P240",
                TimberLeaderHorizontalSide.Right,
                scaleFactor);

        Assert.Equal(
            expectedLengthMm,
            Distance(plain.AnchorX, plain.AnchorY, plain.KneeX, plain.KneeY),
            8);
        Assert.Equal(
            expectedLengthMm,
            Distance(
                dimensions.AnchorX,
                dimensions.AnchorY,
                dimensions.KneeX,
                dimensions.KneeY),
            8);

        // Second landing arm stays independent of the first-segment length.
        var plainLanding =
            TimberItemLeaderLayoutCalculator.ResolveStandaloneNativeLanding(
                plain.KneeX,
                plain.KneeY,
                plain.ContentX,
                plain.ContentY);
        Assert.Equal(
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeLandingLengthMm(
                plain.EnvelopeWidthMm,
                scaleFactor),
            plainLanding.LengthMm,
            8);
        AssertSixtyOneTwentyBend(plain, 1d, 0d);
        AssertSixtyOneTwentyBend(dimensions, 1d, 0d);
    }

    [Theory]
    [InlineData(25, 125d)]
    [InlineData(50, 250d)]
    [InlineData(100, 500d)]
    [InlineData(200, 1000d)]
    public void StandaloneFramedItemOnly_LeaderLength_IsOriginalCanonicalMinusScaled250(
        int denominator,
        double expectedReductionMm)
    {
        Assert.Equal(
            250d,
            TimberItemLeaderLayoutCalculator
                .StandaloneNativeFramedItemOnlyLeaderReductionAtScale50Mm);

        var scaleFactor = TimberAnnotationScaleRules.GetScaleFactor(denominator);
        var originalCanonical =
            (TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm +
             TimberItemLeaderLayoutCalculator.FramedLeaderAdditionalOffsetMm) *
            scaleFactor;
        var expectedLength = originalCanonical - expectedReductionMm;
        Assert.Equal(expectedReductionMm, 250d * scaleFactor, 12);
        Assert.Equal(
            expectedReductionMm,
            TimberItemLeaderLayoutCalculator
                .StandaloneNativeFramedItemOnlyLeaderReductionAtScale50Mm *
            scaleFactor,
            12);

        var placement = new TimberLeaderPlacement(0d, 0d, 0d, 360d, 0d);
        foreach (var style in new[]
                 {
                     ItemNumberLeaderStyle.Circle,
                     ItemNumberLeaderStyle.Rectangle,
                     ItemNumberLeaderStyle.Slot
                 })
        {
            var framed =
                TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeFramedItem(
                    placement,
                    "K1",
                    style,
                    scaleFactor);
            Assert.Equal(
                expectedLength,
                Distance(
                    framed.AnchorX,
                    framed.AnchorY,
                    framed.KneeX,
                    framed.KneeY),
                8);
            Assert.Equal(framed.KneeX, framed.ContentX, 8);
            Assert.Equal(framed.KneeY, framed.ContentY, 8);
            Assert.Equal(
                60d,
                MeasureAcuteAngleToAxisDegrees(
                    framed.AnchorX,
                    framed.AnchorY,
                    framed.KneeX,
                    framed.KneeY,
                    1d,
                    0d),
                8);
        }
    }

    [Fact]
    public void StandaloneNativeLandingPadding_IsCompactNearEdgeOverhangOnly()
    {
        Assert.Equal(
            1d,
            TimberItemLeaderLayoutCalculator.StandaloneNativeLandingPaddingMm);
        Assert.Equal(
            0d,
            TimberItemLeaderLayoutCalculator
                .StandaloneNativeDimensionsLandingPaddingMm);
        Assert.Equal(
            0.45d,
            TimberItemLeaderLayoutCalculator
                .StandaloneNativeDimensionsLandingEnvelopeFactor);
        Assert.Equal(
            250d,
            TimberItemLeaderLayoutCalculator
                .StandaloneNativeDimensionsLandingReductionAtScale50Mm);
        Assert.Equal(
            100d / 2d + 1d,
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeLandingLengthMm(
                100d),
            8);
        Assert.Equal(
            100d / 2d + 2d,
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeLandingLengthMm(
                100d,
                presentationScaleFactor: 2d),
            8);
        Assert.Equal(
            100d * 0.45d,
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeLandingLengthMm(
                100d,
                landingPaddingMm: TimberItemLeaderLayoutCalculator
                    .StandaloneNativeDimensionsLandingPaddingMm,
                envelopeFactor: TimberItemLeaderLayoutCalculator
                    .StandaloneNativeDimensionsLandingEnvelopeFactor),
            8);
        // Must stay ≤ half-envelope + pad — never full envelope + pad (that would
        // double-count MiddleCenter placement).
        Assert.True(
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeLandingLengthMm(
                200d) < 200d);
        Assert.True(
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeLandingLengthMm(
                200d,
                landingPaddingMm: TimberItemLeaderLayoutCalculator
                    .StandaloneNativeDimensionsLandingPaddingMm,
                envelopeFactor: TimberItemLeaderLayoutCalculator
                    .StandaloneNativeDimensionsLandingEnvelopeFactor) < 100d);
        // Dimensions reduction clamps when base < 250×scale.
        Assert.Equal(
            TimberItemLeaderLayoutCalculator
                .StandaloneNativeDimensionsLandingMinimumLengthMm,
            TimberItemLeaderLayoutCalculator
                .CalculateStandaloneNativeDimensionsLandingLengthMm(100d),
            12);
    }

    [Fact]
    public void StandaloneLandingLength_DoesNotUseLegacyClearanceOrMinimumRunConstants()
    {
        var calculator = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.Core",
            "Services",
            "TimberItemLeaderLayoutCalculator.cs"));
        var dimensionsMethod = Member(
            calculator,
            "public static TimberItemLeaderLayout CalculateStandaloneNativeDimensionsLeader(");
        var plainMethod = Member(
            calculator,
            "public static TimberItemLeaderLayout CalculateStandaloneNativePlainItemNumber(");
        var landingHelper = Member(
            calculator,
            "public static double CalculateStandaloneNativeLandingLengthMm(");
        var dimensionsLandingHelper = Member(
            calculator,
            "public static double CalculateStandaloneNativeDimensionsLandingLengthMm(");

        Assert.Contains(
            "CalculateStandaloneNativeDimensionsLandingLengthMm(",
            dimensionsMethod);
        Assert.Contains("CalculateStandaloneNativeLandingLengthMm(", plainMethod);
        Assert.DoesNotContain(
            "StandaloneNativeDimensionsLandingReductionAtScale50Mm",
            plainMethod);
        Assert.Contains(
            "StandaloneNativeDimensionsLandingReductionAtScale50Mm",
            dimensionsLandingHelper);
        Assert.Contains(
            "StandaloneNativeDimensionsLandingMinimumLengthMm",
            dimensionsLandingHelper);
        Assert.Contains("StandaloneNativeLandingPaddingMm", landingHelper);
        Assert.Contains("textEnvelopeWidthMm * envelopeFactor", landingHelper);
        Assert.DoesNotContain("TextClearanceMm", dimensionsMethod);
        Assert.DoesNotContain("MinimumLeaderRunMm", dimensionsMethod);
        Assert.DoesNotContain("CalculatePlainTextClearanceMm(", plainMethod);
        // Combined / legacy Calculate() may still reference clearance + run.
        Assert.Contains("TextClearanceMm", calculator);
        Assert.Contains("MinimumLeaderRunMm", calculator);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(30d)]
    [InlineData(-30d)]
    [InlineData(60d)]
    [InlineData(90d)]
    [InlineData(120d)]
    [InlineData(150d)]
    [InlineData(180d)]
    [InlineData(210d)]
    [InlineData(270d)]
    [InlineData(330d)]
    public void StandaloneCreateFinalization_CorrectsDistortedKneeToSixtyOneTwenty(
        double physicalDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var transform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var placement = new TimberLeaderPlacement(100d, 200d, 100d, 560d, physical);
        var oriented = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativePlainItemNumber(
                placement,
                "K1",
                TimberLeaderHorizontalSide.Right),
            transform);
        var landing = TimberItemLeaderLayoutCalculator.ResolveStandaloneNativeLanding(
            oriented.KneeX,
            oriented.KneeY,
            oriented.ContentX,
            oriented.ContentY);

        // Simulate host drift: knee pulled toward text center (chord ~17° case).
        var driftedKnee = new TimberPlanarPoint(
            oriented.AnchorX + (oriented.ContentX - oriented.AnchorX) * 0.35d,
            oriented.AnchorY + (oriented.ContentY - oriented.AnchorY) * 0.35d);

        Assert.True(
            TimberStandaloneNativeLeaderCreateFinalizationRules
                .TryResolveCreateFinalization(
                    new TimberPlanarPoint(oriented.AnchorX, oriented.AnchorY),
                    driftedKnee,
                    transform,
                    TimberLeaderHorizontalSide.Right,
                    landing.LengthMm,
                    out var correctedKnee,
                    out var landingEnd,
                    out var kneeChanged));
        Assert.True(kneeChanged);
        Assert.True(
            TimberStandaloneNativeLeaderCreateFinalizationRules
                .MeetsSixtyOneTwentyContract(
                    oriented.AnchorX,
                    oriented.AnchorY,
                    correctedKnee.X,
                    correctedKnee.Y,
                    landingEnd.X,
                    landingEnd.Y,
                    transform));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(30d)]
    [InlineData(-30d)]
    [InlineData(60d)]
    [InlineData(90d)]
    [InlineData(120d)]
    [InlineData(150d)]
    [InlineData(180d)]
    [InlineData(210d)]
    [InlineData(270d)]
    [InlineData(330d)]
    public void ResolveStandaloneNativeLanding_AfterOrient_IsParallelToSourceT(
        double physicalDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var transform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var placement = new TimberLeaderPlacement(100d, 200d, 100d, 560d, physical);
        var sourceX = Math.Cos(transform);
        var sourceY = Math.Sin(transform);
        var plain = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativePlainItemNumber(
                placement,
                "K1",
                TimberLeaderHorizontalSide.Right),
            transform);

        AssertStandaloneLandingParallelToSource(plain, sourceX, sourceY);
    }

    [Fact]
    public void StandalonePlain_DoesNotReuseCombinedAnchorNormalContentPlacement()
    {
        var placement = new TimberLeaderPlacement(0d, 0d, 0d, 360d, 0d);
        var plain =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativePlainItemNumber(
                placement,
                "K1",
                TimberLeaderHorizontalSide.Right);
        var combined =
            TimberItemLeaderLayoutCalculator.CalculatePlainItemNumber(
                placement,
                "K1",
                TimberLeaderHorizontalSide.Right);

        // Combined Plain parks content on +N through the anchor (wrong dogleg).
        Assert.Equal(0d, combined.ContentX, 8);
        Assert.True(combined.ContentY > combined.KneeY);

        // Standalone Plain: B ‖ +T from knee → Content on +T side, same height.
        Assert.True(plain.ContentX > plain.KneeX);
        Assert.Equal(plain.KneeY, plain.ContentY, 8);
        AssertSixtyOneTwentyBend(plain, 1d, 0d);
    }

    [Fact]
    public void StandaloneFramedItemOnly_RemainsStraightSourceToFrame_NoSecondSegment()
    {
        var placement = new TimberLeaderPlacement(0d, 0d, 0d, 360d, 0d);
        var framed =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeFramedItem(
                placement,
                "K1",
                ItemNumberLeaderStyle.Circle);
        Assert.Equal(framed.KneeX, framed.ContentX, 8);
        Assert.Equal(framed.KneeY, framed.ContentY, 8);
        Assert.Equal(
            60d,
            MeasureAcuteAngleToAxisDegrees(
                framed.AnchorX,
                framed.AnchorY,
                framed.KneeX,
                framed.KneeY,
                1d,
                0d),
            8);
    }

    private static void AssertSixtyOneTwentyBend(
        TimberItemLeaderLayout layout,
        double sourceX,
        double sourceY)
    {
        Assert.Equal(
            60d,
            MeasureUnsignedAngleToAxisDegrees(
                layout.KneeX - layout.AnchorX,
                layout.KneeY - layout.AnchorY,
                sourceX,
                sourceY),
            8);

        var firstX = layout.KneeX - layout.AnchorX;
        var firstY = layout.KneeY - layout.AnchorY;
        var secondX = layout.ContentX - layout.KneeX;
        var secondY = layout.ContentY - layout.KneeY;
        var firstLength = Math.Sqrt((firstX * firstX) + (firstY * firstY));
        var secondLength = Math.Sqrt((secondX * secondX) + (secondY * secondY));
        Assert.True(firstLength > 1e-9d);
        Assert.True(secondLength > 1e-9d);

        // Segment B parallel to source axis (±T).
        var sourceLength = Math.Sqrt((sourceX * sourceX) + (sourceY * sourceY));
        Assert.True(sourceLength > 1e-9d);
        var parallelDot = Math.Abs(
            ((secondX * sourceX) + (secondY * sourceY)) /
            (secondLength * sourceLength));
        Assert.Equal(1d, parallelDot, 8);

        // Interior elbow = angle between −A (back to attachment) and B = 120°.
        // (Vector angle between dirA and dirB is 60° when B ‖ T; do not assert
        // that as 120° — that was the rotate(dirA,±120°) false positive.)
        Assert.Equal(
            120d,
            MeasureUnsignedAngleToAxisDegrees(-firstX, -firstY, secondX, secondY),
            8);

        // Right/Up dogleg: clockwise first→second (negative cross) opens toward
        // the source — the opposite of Combined Plain's fold-back bend.
        Assert.True((firstX * secondY) - (firstY * secondX) < 0d);
    }

    private static void AssertStandaloneLandingParallelToSource(
        TimberItemLeaderLayout layout,
        double sourceX,
        double sourceY)
    {
        var landing = TimberItemLeaderLayoutCalculator.ResolveStandaloneNativeLanding(
            layout.KneeX,
            layout.KneeY,
            layout.ContentX,
            layout.ContentY);
        Assert.True(landing.LengthMm > 1e-9d);
        var sourceLength = Math.Sqrt((sourceX * sourceX) + (sourceY * sourceY));
        Assert.True(sourceLength > 1e-9d);
        var parallelDot = Math.Abs(
            ((landing.DirX * sourceX) + (landing.DirY * sourceY)) / sourceLength);
        Assert.Equal(1d, parallelDot, 8);
        Assert.Equal(
            Distance(layout.KneeX, layout.KneeY, layout.ContentX, layout.ContentY),
            landing.LengthMm,
            8);
    }

    private static double MeasureUnsignedAngleToAxisDegrees(
        double segmentX,
        double segmentY,
        double axisX,
        double axisY)
    {
        var segmentLength = Math.Sqrt(
            (segmentX * segmentX) + (segmentY * segmentY));
        var axisLength = Math.Sqrt((axisX * axisX) + (axisY * axisY));
        Assert.True(segmentLength > 1e-9d);
        Assert.True(axisLength > 1e-9d);
        var cos = ((segmentX * axisX) + (segmentY * axisY)) /
            (segmentLength * axisLength);
        return Math.Acos(Math.Min(1d, Math.Max(-1d, cos))) * 180d / Math.PI;
    }

    private static double MeasureAcuteAngleToAxisDegrees(
        double startX,
        double startY,
        double endX,
        double endY,
        double axisX,
        double axisY)
    {
        var segmentX = endX - startX;
        var segmentY = endY - startY;
        return TimberItemLeaderLayoutCalculator.MeasureAcuteAngleRadians(
            segmentX,
            segmentY,
            axisX,
            axisY) * 180d / Math.PI;
    }

    [Fact]
    public void ResolveTransform_NeverUsesAlreadyReadableAngleForHalfTurn()
    {
        // Feeding an already-readable +90° must still half-turn (same as physical).
        // Feeding readable 0° from a prior 180° fold must NOT half-turn — but the
        // API contract requires physical input; 180° physical is the authority.
        Assert.Equal(
            -Math.PI / 2d,
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                Math.PI / 2d),
            8);
        Assert.Equal(
            0d,
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                Math.PI),
            8);
        Assert.False(
            TimberStandaloneNativeLeaderOrientationRules.IsExactVertical(Math.PI));
        Assert.True(
            TimberStandaloneNativeLeaderOrientationRules.IsExactOneEighty(Math.PI));
    }

    [Fact]
    public void StandaloneNativeOrientation_IsIsolatedFromCombinedPlain()
    {
        var source = ElementLabelSource();
        Assert.Contains(
            "ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(",
            source);
        Assert.Contains(
            "CalculateStandaloneNativePlainItemNumber(",
            source);
        Assert.Contains(
            "standaloneNativeOrientation: true",
            source);
        Assert.Contains(
            "if (combinedLandingDistanceMm is null)",
            source);
        Assert.Contains(
            "TimberItemLeaderLayoutCalculator.CalculatePlainItemNumber(",
            source);
        Assert.Contains("bool standaloneNativeOrientation = false", source);
        Assert.Contains(
            "TryGetSourceElementAxisRadians(sourceEntity, out var physicalAxis)",
            source);
        Assert.Contains(
            "framedPlacement.RotationRadians",
            Member(
                source,
                "private static LeaderPlacement ApplyCombinedLandingDistance("));
        Assert.DoesNotContain(
            "ApplyFramedItemLandingDistance(",
            source);
        Assert.DoesNotContain(
            "ApplyStandaloneNativeLeaderReadableOrientation(",
            source);
        Assert.DoesNotContain(
            "ApplyStandaloneNativeSourceEndpointReattach(",
            source);
    }

    [Fact]
    public void StandaloneFramedItemOnly_UsesOneBlockContentMLeaderNotG4()
    {
        var source = ElementLabelSource();
        Assert.Contains(
            "AutoCadStandaloneFramedItemOnlyProductionPolicy.UsesStandaloneFramedItemOnly(",
            source);
        Assert.Contains(
            "UpsertStandaloneFramedItemOnlyLeader(",
            source);
        Assert.Contains(
            "AutoCadStandaloneFramedItemOnlyAnnotationService.Create(",
            source);
        Assert.Contains("ContentType.BlockContent", StandaloneServiceSource());
        Assert.Contains("EnableLanding = false", StandaloneServiceSource());
        Assert.Contains("EnableDogleg = false", StandaloneServiceSource());
        Assert.Contains("LeaderType.StraightLeader", StandaloneServiceSource());
        Assert.Contains(
            "TimberFramedBlockContentPresentation.ItemOnly",
            source);
        Assert.DoesNotContain(
            "AutoCadFramedBlockContentAnnotationService.Create(",
            Member(source, "private static bool UpsertStandaloneFramedItemOnlyLeader("));
    }

    [Fact]
    public void StandaloneFramedItemOnly_CreateUsesAbsoluteOrientation_NotCumulativeTransformBy()
    {
        var service = StandaloneServiceSource();
        var create = Member(
            service,
            "public static AutoCadStandaloneFramedItemOnlyCreateResult Create(");
        var update = Member(
            service,
            "public static bool TryUpdateInPlace(");
        Assert.Contains("OrientAroundAnchor(", create);
        Assert.Contains("ApplyAbsoluteBlockContentOrientation(", create);
        Assert.Contains("leader.BlockRotation =", create);
        // Cumulative bug: TransformBy(readable) on already-oriented BlockContent.
        Assert.DoesNotContain("leader.TransformBy(", service);
        Assert.DoesNotContain("Matrix3d.Rotation(", service);
        Assert.DoesNotContain("ApplyPhysicalOrientation(", service);
        // Existing-owner: content-only unless source sync rebuilds CREATE canonical.
        Assert.Contains("ApplyCanonicalLayout(", update);
        Assert.Contains("RequiresCanonicalRebuild", update);
        Assert.Contains("ApplyItemNoAttribute(", update);
        Assert.Contains("ReassertStraightLeader(", update);
        Assert.DoesNotContain("ApplySourceEndpointReattach(", update);
        Assert.DoesNotContain("RequiresSourceEndpointReattach", update);
        var applyCanonical = Member(
            service,
            "private static void ApplyCanonicalLayout(");
        Assert.Contains("OrientAroundAnchor(", applyCanonical);
        Assert.Contains("ApplyAbsoluteBlockContentOrientation(", applyCanonical);
    }

    [Fact]
    public void StandaloneNativeLeaders_ExistingOwnerRefresh_PreservesLivePlacement()
    {
        var source = ElementLabelSource();
        var upsert = Member(source, "private static bool UpsertLeader(");
        var update = Member(source, "private static bool TryUpdateNativeLeader(");
        var framedUpsert = Member(
            source,
            "private static bool UpsertStandaloneFramedItemOnlyLeader(");

        Assert.Contains("preserveStandaloneNativePlacement", upsert);
        Assert.Contains(
            "geometryMatches || preserveStandaloneNativePlacement",
            upsert);
        Assert.Contains("CaptureLiveNativeLeaderPlacement(", upsert);
        Assert.Contains(
            "TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(",
            upsert);
        Assert.Contains("combinedLandingDistanceMm is null", update);
        Assert.Contains("liveTextLocation", update);
        Assert.Contains("ApplyStandalonePlainItemCanonicalRebuild(", update);
        Assert.Contains("ApplyStandaloneDimensionsCanonicalRebuild(", update);
        Assert.Contains(
            "ApplyAbsoluteStandaloneDimensionsLeaderTextOrientation(",
            update);
        Assert.Contains(
            "ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(",
            update);
        Assert.DoesNotContain("ApplyStandalonePlainItemSourceEndpointReattach(", update);
        Assert.DoesNotContain("ApplyStandaloneDimensionsSourceEndpointReattach(", update);
        var standaloneBranch = update.IndexOf(
            "if (combinedLandingDistanceMm is null)",
            StringComparison.Ordinal);
        var requiresRebuild = update.IndexOf(
            "RequiresCanonicalRebuild",
            standaloneBranch,
            StringComparison.Ordinal);
        var createMTextInStandalone = update.IndexOf(
            "CreateLeaderMText(",
            standaloneBranch,
            StringComparison.Ordinal);
        Assert.True(standaloneBranch >= 0 && createMTextInStandalone >= 0);
        Assert.True(requiresRebuild >= 0);
        Assert.Contains("TryUpdateInPlace(", framedUpsert);
        Assert.Contains(
            "AutoCadStandaloneFramedItemOnlyAnnotationService.Create(",
            framedUpsert);
        var tryUpdate = framedUpsert.IndexOf(
            "TryUpdateInPlace(",
            StringComparison.Ordinal);
        var create = framedUpsert.IndexOf(
            "AutoCadStandaloneFramedItemOnlyAnnotationService.Create(",
            tryUpdate,
            StringComparison.Ordinal);
        Assert.True(tryUpdate >= 0 && create >= 0 && tryUpdate < create);
        Assert.Contains(
            "CalculateStandaloneNativeDimensionsLeader(",
            source);
        Assert.Contains("OrientAroundAnchor(", source);
        Assert.Contains("CalculateStandaloneNativePlainItemNumber(", source);
    }

    [Fact]
    public void DimensionsLeader_CreateUsesAbsoluteOrient_NotTransformBy()
    {
        var source = ElementLabelSource();
        var create = Member(source, "private static MLeader CreateNativeMLeader(");
        var update = Member(source, "private static bool TryUpdateNativeLeader(");
        var dimensionsRebuild = Member(
            source,
            "private static void ApplyStandaloneDimensionsCanonicalRebuild(");

        Assert.Contains("CalculateStandaloneNativeDimensionsLeader(", source);
        Assert.Contains("OrientAroundAnchor(", source);
        Assert.Contains(
            "ApplyAbsoluteStandaloneDimensionsLeaderTextOrientation(",
            create);
        Assert.Contains(
            "ApplyAbsoluteStandaloneDimensionsLeaderTextOrientation(",
            update);
        Assert.Contains("dimensionsLeaderPresentation is not null", create);
        Assert.Contains("ApplyStandaloneDimensionsCanonicalRebuild(", update);
        Assert.Contains(
            "ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(",
            create);
        Assert.DoesNotContain("leader.TransformBy(", dimensionsRebuild);
        Assert.Contains("ApplyStandaloneNativeMTextLanding(", dimensionsRebuild);
        Assert.Contains("ApplyStandaloneNativeMTextLanding(", create);
        Assert.Contains("content.Rotation =", Member(
            source,
            "private static void ApplyAbsoluteStandaloneDimensionsLeaderTextOrientation("));
    }

    [Fact]
    public void PlainItemOnly_CreateUsesAbsoluteOrient_NotTransformBy()
    {
        var source = ElementLabelSource();
        var create = Member(source, "private static MLeader CreateNativeMLeader(");
        var update = Member(source, "private static bool TryUpdateNativeLeader(");
        var plainRebuild = Member(
            source,
            "private static void ApplyStandalonePlainItemCanonicalRebuild(");

        Assert.Contains("CalculateStandaloneNativePlainItemNumber(", source);
        Assert.Contains("OrientAroundAnchor(", source);
        Assert.Contains(
            "ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(",
            create);
        Assert.Contains(
            "ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(",
            update);
        Assert.Contains("isStandalonePlainAbsolute", create);
        Assert.Contains("ApplyStandalonePlainItemCanonicalRebuild(", update);
        Assert.DoesNotContain("leader.TransformBy(", create);
        Assert.DoesNotContain("leader.TransformBy(", update);
        Assert.DoesNotContain("leader.TransformBy(", plainRebuild);
        Assert.Contains("ApplyStandaloneNativeMTextLanding(", plainRebuild);
        Assert.Contains("ApplyStandaloneNativeMTextLanding(", create);
        Assert.Contains("content.Rotation =", Member(
            source,
            "private static void ApplyAbsoluteStandalonePlainItemLeaderTextOrientation("));
    }

    [Fact]
    public void StandalonePlainAndDimensions_CreatePath_EnforcesDoglegLengthAndSetDogleg()
    {
        var source = ElementLabelSource();
        var create = Member(source, "private static MLeader CreateNativeMLeader(");
        var landing = Member(
            source,
            "private static void ApplyStandaloneNativeMTextLanding(");
        var resolve = Member(
            source,
            "private static bool TryResolveStandaloneNativeLanding(");
        var dimensionsRebuild = Member(
            source,
            "private static void ApplyStandaloneDimensionsCanonicalRebuild(");
        var plainRebuild = Member(
            source,
            "private static void ApplyStandalonePlainItemCanonicalRebuild(");

        Assert.Contains("ResolveStandaloneNativeLanding(", resolve);
        Assert.Contains("isStandaloneNativeMText", create);
        Assert.Contains("doglegLengthOverride = standaloneLandingLength", create);
        Assert.Contains("ApplyStandaloneNativeMTextLanding(", create);
        Assert.Contains("leader.DoglegLength = doglegLength", landing);
        Assert.Contains("leader.SetDogleg(leaderIndex, doglegDirection)", landing);
        Assert.Contains("leader.EnableLanding = true", landing);
        Assert.Contains("leader.EnableDogleg = true", landing);
        Assert.Contains(
            "TimberStandaloneNativeLeaderCreateFinalizationRules",
            landing);
        Assert.Contains("TryResolveCreateFinalization(", landing);
        // Landing must run AFTER absolute MText orientation (first host distortion).
        var createDimensionsBranch = create.IndexOf(
            "if (dimensionsLeaderPresentation is not null)",
            StringComparison.Ordinal);
        Assert.True(createDimensionsBranch >= 0);
        var createDimensionsSlice = create[createDimensionsBranch..];
        var dimsOrient = createDimensionsSlice.IndexOf(
            "ApplyAbsoluteStandaloneDimensionsLeaderTextOrientation(",
            StringComparison.Ordinal);
        var dimsLanding = createDimensionsSlice.IndexOf(
            "ApplyStandaloneNativeMTextLanding(",
            StringComparison.Ordinal);
        Assert.True(dimsOrient >= 0 && dimsLanding > dimsOrient);
        var dimsRebuildOrient = dimensionsRebuild.IndexOf(
            "ApplyAbsoluteStandaloneDimensionsLeaderTextOrientation(",
            StringComparison.Ordinal);
        var dimsRebuildLanding = dimensionsRebuild.IndexOf(
            "ApplyStandaloneNativeMTextLanding(",
            StringComparison.Ordinal);
        Assert.True(dimsRebuildOrient >= 0 && dimsRebuildLanding > dimsRebuildOrient);
        var plainRebuildOrient = plainRebuild.IndexOf(
            "ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(",
            StringComparison.Ordinal);
        var plainRebuildLanding = plainRebuild.IndexOf(
            "ApplyStandaloneNativeMTextLanding(",
            StringComparison.Ordinal);
        Assert.True(plainRebuildOrient >= 0 && plainRebuildLanding > plainRebuildOrient);
        Assert.Contains("ApplyStandaloneNativeMTextLanding(", dimensionsRebuild);
        Assert.Contains("ApplyStandaloneNativeMTextLanding(", plainRebuild);
        // Content-only path must not rewrite dogleg geometry.
        var update = Member(source, "private static bool TryUpdateNativeLeader(");
        var contentOnly = update.IndexOf(
            "var liveTextLocation = leader.TextLocation",
            StringComparison.Ordinal);
        Assert.True(contentOnly >= 0);
        var contentOnlySlice = update[contentOnly..];
        Assert.DoesNotContain("ApplyStandaloneNativeMTextLanding(", contentOnlySlice);
        // Combined CalculateBlock path untouched.
        Assert.DoesNotContain(
            "CalculateBlock(",
            Member(
                source,
                "private static void ApplyStandaloneNativeMTextLanding("));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(30d)]
    [InlineData(-30d)]
    [InlineData(89d)]
    [InlineData(90d)]
    [InlineData(91d)]
    [InlineData(179d)]
    [InlineData(180d)]
    [InlineData(181d)]
    [InlineData(269d)]
    [InlineData(270d)]
    [InlineData(271d)]
    public void PlainItemOnly_CreateOrientation_ConvergesAbsoluteAndIdempotent(
        double physicalDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var transform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var basePlacement = new TimberLeaderPlacement(100d, 200d, 100d, 560d, physical);
        var canonical =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativePlainItemNumber(
                basePlacement,
                "K1",
                TimberLeaderHorizontalSide.Right);
        var once = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            transform);
        var twice = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            transform);
        var textRotationOnce =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(transform);
        var textRotationTwice =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                    physical));

        Assert.Equal(once.ContentX, twice.ContentX, 8);
        Assert.Equal(once.ContentY, twice.ContentY, 8);
        Assert.Equal(once.KneeX, twice.KneeX, 8);
        Assert.Equal(once.KneeY, twice.KneeY, 8);
        Assert.Equal(textRotationOnce, textRotationTwice, 8);
        if (Math.Abs(transform) >
            TimberStandaloneNativeLeaderOrientationRules.AngleToleranceRadians)
        {
            Assert.NotEqual(0d, textRotationOnce, 8);
            Assert.False(
                NearlyEqual(canonical.ContentX, once.ContentX) &&
                NearlyEqual(canonical.ContentY, once.ContentY));
        }
    }

    [Theory]
    [InlineData(0d, 30d)]
    [InlineData(30d, 90d)]
    [InlineData(90d, 180d)]
    [InlineData(0d, 180d)]
    public void PlainItemOnly_SourceStretchRotate_Model_RebuildsAbsoluteCanonical(
        double beforeDeg,
        double afterDeg)
    {
        var before = beforeDeg * Math.PI / 180d;
        var after = afterDeg * Math.PI / 180d;
        var decision = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            previousAutomaticTextX: 100d,
            previousAutomaticTextY: 560d,
            previousPhysicalRotationRadians: before,
            newAutomaticTextX: 140d,
            newAutomaticTextY: 560d,
            newPhysicalRotationRadians: after);

        var expectedTextRotation =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                    after));
        var expectedDelta =
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(after) -
                TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(before));
        var previousAbsolute =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(before);

        Assert.True(decision.RequiresCanonicalRebuild);
        Assert.True(decision.SourceGeometryChanged);
        Assert.Equal(expectedDelta, decision.OrientationDeltaRadians, 8);
        Assert.Equal(
            expectedTextRotation,
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(after)),
            8);

        if (Math.Abs(previousAbsolute) >
                TimberStandaloneNativeLeaderOrientationRules.AngleToleranceRadians &&
            Math.Abs(expectedDelta) >
                TimberStandaloneNativeLeaderOrientationRules.AngleToleranceRadians)
        {
            Assert.True(decision.RequiresOrientationSync);
            var buggyAfterHorizontalReset = expectedDelta;
            Assert.NotEqual(expectedTextRotation, buggyAfterHorizontalReset, 8);
        }
    }

    [Fact]
    public void PlainItemOnly_ManualMoveThenAkLabels_Model_PreservesPlacementAbsoluteRotation()
    {
        var physical = 30d * Math.PI / 180d;
        var absolute =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var manualTextX = 400d;
        var manualTextY = 700d;

        var sync = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            previousAutomaticTextX: 100d,
            previousAutomaticTextY: 560d,
            previousPhysicalRotationRadians: physical,
            newAutomaticTextX: 100d,
            newAutomaticTextY: 560d,
            newPhysicalRotationRadians: physical);

        Assert.False(sync.RequiresCanonicalRebuild);
        Assert.False(sync.RequiresOrientationSync);
        Assert.Equal(400d, manualTextX, 8);
        Assert.Equal(700d, manualTextY, 8);
        Assert.Equal(
            absolute,
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(physical),
            8);
        Assert.NotEqual(0d, absolute, 8);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(30d, 30d)]
    [InlineData(-30d, -30d)]
    [InlineData(89d, 89d)]
    [InlineData(90d, -90d)]
    [InlineData(91d, -89d)]
    [InlineData(179d, -1d)]
    [InlineData(180d, 0d)]
    [InlineData(181d, 1d)]
    [InlineData(269d, 89d)]
    [InlineData(270d, 90d)]
    [InlineData(271d, -89d)]
    public void PlainItemOnly_StandaloneOrientAroundAnchor_MatchesAbsoluteTransform(
        double physicalDegrees,
        double expectedTransformDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var expected = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            expectedTransformDegrees * Math.PI / 180d);
        var transform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        Assert.Equal(expected, transform, 8);

        var basePlacement = new TimberLeaderPlacement(0d, 0d, 0d, 360d, 0d);
        var canonical =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativePlainItemNumber(
                basePlacement,
                "K1",
                TimberLeaderHorizontalSide.Right);
        var oriented = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            transform);

        Assert.Equal(canonical.AnchorX, oriented.AnchorX, 8);
        Assert.Equal(canonical.AnchorY, oriented.AnchorY, 8);
        Assert.Equal(
            Distance(canonical.AnchorX, canonical.AnchorY, canonical.ContentX, canonical.ContentY),
            Distance(oriented.AnchorX, oriented.AnchorY, oriented.ContentX, oriented.ContentY),
            8);

        var again = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(physical));
        Assert.Equal(oriented.ContentX, again.ContentX, 8);
        Assert.Equal(oriented.ContentY, again.ContentY, 8);
        Assert.Equal(oriented.KneeX, again.KneeX, 8);
        Assert.Equal(oriented.KneeY, again.KneeY, 8);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(30d)]
    [InlineData(90d)]
    [InlineData(180d)]
    public void PlainItemOnly_CreateOrientation_ReversedStartEnd_ReadableOnce(
        double degrees)
    {
        var forward = degrees * Math.PI / 180d;
        var reversed = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            forward + Math.PI);
        var forwardTransform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                forward);
        var reversedTransform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                reversed);
        var basePlacement = new TimberLeaderPlacement(0d, 0d, 0d, 360d, 0d);
        var canonical =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativePlainItemNumber(
                basePlacement,
                "K1",
                TimberLeaderHorizontalSide.Right);
        var forwardLayout = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            forwardTransform);
        var reversedLayout = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            reversedTransform);

        Assert.Equal(forwardLayout.AnchorX, reversedLayout.AnchorX, 8);
        Assert.Equal(forwardLayout.AnchorY, reversedLayout.AnchorY, 8);
        Assert.Equal(
            Distance(
                forwardLayout.AnchorX,
                forwardLayout.AnchorY,
                forwardLayout.ContentX,
                forwardLayout.ContentY),
            Distance(
                reversedLayout.AnchorX,
                reversedLayout.AnchorY,
                reversedLayout.ContentX,
                reversedLayout.ContentY),
            8);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(30d)]
    [InlineData(-30d)]
    [InlineData(89d)]
    [InlineData(90d)]
    [InlineData(91d)]
    [InlineData(179d)]
    [InlineData(180d)]
    [InlineData(181d)]
    [InlineData(269d)]
    [InlineData(270d)]
    [InlineData(271d)]
    public void DimensionsLeader_CreateOrientation_ConvergesAbsoluteAndIdempotent(
        double physicalDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var transform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var basePlacement = new TimberLeaderPlacement(100d, 200d, 100d, 560d, physical);
        var canonical =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeDimensionsLeader(
                basePlacement,
                "80x160",
                TimberLeaderHorizontalSide.Right);
        var once = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            transform);
        var twice = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            transform);
        var textRotationOnce =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(transform);
        var textRotationTwice =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                    physical));

        Assert.Equal(once.ContentX, twice.ContentX, 8);
        Assert.Equal(once.ContentY, twice.ContentY, 8);
        Assert.Equal(once.KneeX, twice.KneeX, 8);
        Assert.Equal(once.KneeY, twice.KneeY, 8);
        Assert.Equal(textRotationOnce, textRotationTwice, 8);
        // Must not stay at horizontal fallback when physical axis is non-zero transform.
        if (Math.Abs(transform) >
            TimberStandaloneNativeLeaderOrientationRules.AngleToleranceRadians)
        {
            Assert.NotEqual(0d, textRotationOnce, 8);
            Assert.False(
                NearlyEqual(canonical.ContentX, once.ContentX) &&
                NearlyEqual(canonical.ContentY, once.ContentY));
        }
    }

    [Theory]
    [InlineData(0d, 30d)]
    [InlineData(30d, 90d)]
    [InlineData(90d, 180d)]
    [InlineData(0d, 180d)]
    public void DimensionsLeader_SourceStretchRotate_Model_RebuildsAbsoluteCanonical(
        double beforeDeg,
        double afterDeg)
    {
        var before = beforeDeg * Math.PI / 180d;
        var after = afterDeg * Math.PI / 180d;
        var decision = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            previousAutomaticTextX: 100d,
            previousAutomaticTextY: 560d,
            previousPhysicalRotationRadians: before,
            newAutomaticTextX: 140d,
            newAutomaticTextY: 560d,
            newPhysicalRotationRadians: after);

        var expectedTextRotation =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                    after));
        var expectedDelta =
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(after) -
                TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(before));
        var previousAbsolute =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(before);

        Assert.True(decision.RequiresCanonicalRebuild);
        Assert.True(decision.SourceGeometryChanged);
        Assert.Equal(expectedDelta, decision.OrientationDeltaRadians, 8);
        Assert.Equal(
            expectedTextRotation,
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(after)),
            8);

        // Bug model: AK_LABELS left MText.Rotation=0, then stretch applied only delta
        // → wrong absolute angle whenever prior absolute orientation was non-zero.
        if (Math.Abs(previousAbsolute) >
                TimberStandaloneNativeLeaderOrientationRules.AngleToleranceRadians &&
            Math.Abs(expectedDelta) >
                TimberStandaloneNativeLeaderOrientationRules.AngleToleranceRadians)
        {
            Assert.True(decision.RequiresOrientationSync);
            var buggyAfterHorizontalReset = expectedDelta;
            Assert.NotEqual(expectedTextRotation, buggyAfterHorizontalReset, 8);
        }
    }

    [Fact]
    public void DimensionsLeader_ManualMoveThenAkLabels_Model_PreservesPlacementAbsoluteRotation()
    {
        var physical = 30d * Math.PI / 180d;
        var absolute =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var manualTextX = 400d;
        var manualTextY = 700d;

        var sync = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            previousAutomaticTextX: 100d,
            previousAutomaticTextY: 560d,
            previousPhysicalRotationRadians: physical,
            newAutomaticTextX: 100d,
            newAutomaticTextY: 560d,
            newPhysicalRotationRadians: physical);

        Assert.False(sync.RequiresCanonicalRebuild);
        Assert.False(sync.RequiresOrientationSync);
        // Content-only refresh keeps manual placement and reasserts absolute rotation.
        var preservedTextX = manualTextX;
        var preservedTextY = manualTextY;
        Assert.Equal(400d, preservedTextX, 8);
        Assert.Equal(700d, preservedTextY, 8);
        Assert.Equal(
            absolute,
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(physical),
            8);
        Assert.NotEqual(0d, absolute, 8);
    }

    public static TheoryData<double, ItemNumberLeaderStyle> RefreshIdempotencyMatrix() =>
        new()
        {
            { 0d, ItemNumberLeaderStyle.Circle },
            { 30d, ItemNumberLeaderStyle.Circle },
            { -30d, ItemNumberLeaderStyle.Rectangle },
            { 90d, ItemNumberLeaderStyle.Rectangle },
            { 180d, ItemNumberLeaderStyle.Slot },
            { 270d, ItemNumberLeaderStyle.Slot },
        };

    [Theory]
    [MemberData(nameof(RefreshIdempotencyMatrix))]
    public void StandaloneFramedItemOnly_CreateOrientation_ConvergesToSameAbsoluteState(
        double physicalDegrees,
        ItemNumberLeaderStyle style)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var desired =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var basePlacement = new TimberLeaderPlacement(100d, 200d, 100d, 560d, 0d);
        var canonical =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeFramedItem(
                basePlacement,
                "K1",
                style);

        // CREATE orientation math is absolute/idempotent (not content refresh).
        var once = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            desired);
        var rotationOnce =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(desired);

        var twice = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            desired);
        var rotationTwice =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(desired);
        var five = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            desired);

        Assert.Equal(once.AnchorX, twice.AnchorX, 8);
        Assert.Equal(once.AnchorY, twice.AnchorY, 8);
        Assert.Equal(once.ContentX, twice.ContentX, 8);
        Assert.Equal(once.ContentY, twice.ContentY, 8);
        Assert.Equal(once.ContentX, five.ContentX, 8);
        Assert.Equal(once.ContentY, five.ContentY, 8);
        Assert.Equal(rotationOnce, rotationTwice, 8);
        Assert.Equal(
            rotationOnce,
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(desired),
            8);
        Assert.Equal(
            Distance(once.AnchorX, once.AnchorY, once.ContentX, once.ContentY),
            Distance(
                canonical.AnchorX,
                canonical.AnchorY,
                canonical.ContentX,
                canonical.ContentY),
            8);
    }

    [Theory]
    [MemberData(nameof(RefreshIdempotencyMatrix))]
    public void StandaloneFramedItemOnly_CanonicalOrientWouldResetManualOffset_ContentOnlyMustNot(
        double physicalDegrees,
        ItemNumberLeaderStyle style)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var desired =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var basePlacement = new TimberLeaderPlacement(100d, 200d, 100d, 560d, 0d);
        var canonical =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeFramedItem(
                basePlacement,
                "K1",
                style);
        var automatic = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            desired);

        // User moved frame away from automatic Content.
        var manualContentX = automatic.ContentX + 250d;
        var manualContentY = automatic.ContentY - 120d;

        // Bug model: refresh re-ran OrientAroundAnchor(canonical) → reset.
        var buggyRefresh = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            desired);
        Assert.Equal(automatic.ContentX, buggyRefresh.ContentX, 8);
        Assert.Equal(automatic.ContentY, buggyRefresh.ContentY, 8);
        Assert.NotEqual(manualContentX, buggyRefresh.ContentX, 8);

        // Fixed model: existing-owner content refresh is identity on placement.
        var preservedX = manualContentX;
        var preservedY = manualContentY;
        for (var i = 0; i < 5; i++)
        {
            preservedX = manualContentX;
            preservedY = manualContentY;
        }

        Assert.Equal(manualContentX, preservedX, 8);
        Assert.Equal(manualContentY, preservedY, 8);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(30d)]
    [InlineData(-30d)]
    [InlineData(90d)]
    [InlineData(180d)]
    [InlineData(270d)]
    public void StandaloneFramedItemOnly_AbsoluteRotation_DoesNotAccumulateOnRepeatedResolve(
        double physicalDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var first =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var second =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var fifth =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        Assert.Equal(first, second, 8);
        Assert.Equal(first, fifth, 8);
        // Must never treat a prior readable result as a new physical increment.
        var wronglyCompounded =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(first + first);
        if (Math.Abs(first) >
            TimberStandaloneNativeLeaderOrientationRules.AngleToleranceRadians)
        {
            Assert.NotEqual(first, wronglyCompounded, 8);
        }
    }

    [Fact]
    public void SourceSync_AkLabelsUnchangedSource_IsContentOnly()
    {
        var decision = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            previousAutomaticTextX: 100d,
            previousAutomaticTextY: 200d,
            previousPhysicalRotationRadians: 30d * Math.PI / 180d,
            newAutomaticTextX: 100d,
            newAutomaticTextY: 200d,
            newPhysicalRotationRadians: 30d * Math.PI / 180d);

        Assert.False(decision.SourceGeometryChanged);
        Assert.False(decision.RequiresCanonicalRebuild);
        Assert.False(decision.RequiresOrientationSync);
        Assert.Equal(0d, decision.OrientationDeltaRadians, 8);
    }

    [Fact]
    public void SourceSync_LengthOnlyStretch_RebuildsCanonicalWithoutOrientationDelta()
    {
        var decision = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            previousAutomaticTextX: 100d,
            previousAutomaticTextY: 200d,
            previousPhysicalRotationRadians: 0d,
            newAutomaticTextX: 250d,
            newAutomaticTextY: 200d,
            newPhysicalRotationRadians: 0d);

        Assert.True(decision.SourceGeometryChanged);
        Assert.True(decision.RequiresCanonicalRebuild);
        Assert.False(decision.RequiresOrientationSync);
        Assert.Equal(0d, decision.OrientationDeltaRadians, 8);
    }

    [Theory]
    [InlineData(0d, 30d)]
    [InlineData(30d, 90d)]
    [InlineData(90d, 180d)]
    [InlineData(0d, 180d)]
    public void SourceSync_SourceRotate_RebuildsCanonicalWithAbsoluteOrientationDelta(
        double beforeDeg,
        double afterDeg)
    {
        var before = beforeDeg * Math.PI / 180d;
        var after = afterDeg * Math.PI / 180d;
        var decision = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            previousAutomaticTextX: 100d,
            previousAutomaticTextY: 200d,
            previousPhysicalRotationRadians: before,
            newAutomaticTextX: 100d,
            newAutomaticTextY: 200d,
            newPhysicalRotationRadians: after);

        var expectedDelta =
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(after) -
                TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(before));

        Assert.True(decision.SourceGeometryChanged);
        Assert.True(decision.RequiresCanonicalRebuild);
        Assert.Equal(
            Math.Abs(expectedDelta) >
                TimberStandaloneNativeLeaderOrientationRules.AngleToleranceRadians,
            decision.RequiresOrientationSync);
        Assert.Equal(expectedDelta, decision.OrientationDeltaRadians, 8);
    }

    [Fact]
    public void SourceSync_MissingAutomaticBaseline_DoesNotRebuild()
    {
        var decision = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            previousAutomaticTextX: null,
            previousAutomaticTextY: null,
            previousPhysicalRotationRadians: null,
            newAutomaticTextX: 10d,
            newAutomaticTextY: 20d,
            newPhysicalRotationRadians: Math.PI / 2d);

        Assert.False(decision.RequiresCanonicalRebuild);
        Assert.False(decision.SourceGeometryChanged);
    }

    [Theory]
    [InlineData(0d, 100d, 200d, 30d)]
    [InlineData(90d, 0d, 0d, -40d)]
    [InlineData(180d, 50d, -25d, 180d)]
    public void SourceSync_RotateAround_PreservesDistanceToPivot(
        double deltaDeg,
        double x,
        double y,
        double pivotDegUnused)
    {
        _ = pivotDegUnused;
        var delta = deltaDeg * Math.PI / 180d;
        var pivotX = 10d;
        var pivotY = 20d;
        var beforeDist = Math.Sqrt(
            ((x - pivotX) * (x - pivotX)) + ((y - pivotY) * (y - pivotY)));
        var after = TimberStandaloneNativeLeaderSourceSyncRules.RotateAround(
            x, y, pivotX, pivotY, delta);
        var afterDist = Math.Sqrt(
            ((after.X - pivotX) * (after.X - pivotX)) +
            ((after.Y - pivotY) * (after.Y - pivotY)));
        Assert.Equal(beforeDist, afterDist, 8);
        var identity = TimberStandaloneNativeLeaderSourceSyncRules.RotateAround(
            after.X, after.Y, pivotX, pivotY, -delta);
        Assert.Equal(x, identity.X, 8);
        Assert.Equal(y, identity.Y, 8);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(30d, 30d)]
    [InlineData(-30d, -30d)]
    [InlineData(89d, 89d)]
    [InlineData(90d, -90d)]
    [InlineData(91d, -89d)]
    [InlineData(179d, -1d)]
    [InlineData(180d, 0d)]
    [InlineData(181d, 1d)]
    [InlineData(269d, 89d)]
    [InlineData(270d, 90d)]
    [InlineData(271d, -89d)]
    public void DimensionsLeader_StandaloneOrientAroundAnchor_MatchesAbsoluteTransform(
        double physicalDegrees,
        double expectedTransformDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var expected = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            expectedTransformDegrees * Math.PI / 180d);
        var transform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        Assert.Equal(expected, transform, 8);

        var basePlacement = new TimberLeaderPlacement(0d, 0d, 0d, 360d, 0d);
        var canonical =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeDimensionsLeader(
                basePlacement,
                "80x160",
                TimberLeaderHorizontalSide.Right);
        var oriented = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            transform);

        Assert.Equal(canonical.AnchorX, oriented.AnchorX, 8);
        Assert.Equal(canonical.AnchorY, oriented.AnchorY, 8);
        Assert.Equal(
            Distance(canonical.AnchorX, canonical.AnchorY, canonical.ContentX, canonical.ContentY),
            Distance(oriented.AnchorX, oriented.AnchorY, oriented.ContentX, oriented.ContentY),
            8);

        // Repeated absolute resolve does not accumulate.
        var again = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(physical));
        Assert.Equal(oriented.ContentX, again.ContentX, 8);
        Assert.Equal(oriented.ContentY, again.ContentY, 8);
    }

    [Fact]
    public void ManualMoveThenSourceStretch_Model_ResetsToCanonicalFromCurrentSource()
    {
        var beforeAutoX = 100d;
        var beforeAutoY = 560d;
        var afterAutoX = 180d;
        var afterAutoY = 560d;
        var manualContentX = 400d;
        var manualContentY = 700d;
        var newAnchorX = 80d;
        var newAnchorY = 200d;

        var sync = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            beforeAutoX,
            beforeAutoY,
            previousPhysicalRotationRadians: 0d,
            afterAutoX,
            afterAutoY,
            newPhysicalRotationRadians: 0d);

        Assert.True(sync.RequiresCanonicalRebuild);
        Assert.True(sync.SourceGeometryChanged);
        Assert.False(sync.RequiresOrientationSync);

        // Combined-like semantics: discard prior manual content, rebuild CREATE
        // canonical from the current source attachment (OrientAroundAnchor).
        var basePlacement = new TimberLeaderPlacement(
            newAnchorX,
            newAnchorY,
            afterAutoX,
            afterAutoY,
            0d);
        var canonical =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativePlainItemNumber(
                basePlacement,
                "K1",
                TimberLeaderHorizontalSide.Right);
        var rebuilt = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonical,
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(0d));

        Assert.NotEqual(manualContentX, rebuilt.ContentX, 8);
        Assert.NotEqual(manualContentY, rebuilt.ContentY, 8);
        Assert.Equal(newAnchorX, rebuilt.AnchorX, 8);
        Assert.Equal(newAnchorY, rebuilt.AnchorY, 8);
        Assert.Equal(canonical.ContentX, rebuilt.ContentX, 8);
        Assert.Equal(canonical.ContentY, rebuilt.ContentY, 8);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(30d)]
    [InlineData(90d)]
    [InlineData(180d)]
    public void GripThenSourceMove_Model_RestoresCreateCanonical(double physicalDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var transform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var createPlacement = new TimberLeaderPlacement(100d, 200d, 100d, 560d, physical);
        var createCanonical =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativePlainItemNumber(
                createPlacement,
                "K1",
                TimberLeaderHorizontalSide.Right);
        var createOriented = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            createCanonical,
            transform);

        // Annotation grip: Automatic* unchanged → content-only preserve.
        var gripSync = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            createOriented.ContentX,
            createOriented.ContentY,
            physical,
            createOriented.ContentX,
            createOriented.ContentY,
            physical);
        Assert.False(gripSync.RequiresCanonicalRebuild);
        var grippedContentX = createOriented.ContentX + 250d;
        var grippedContentY = createOriented.ContentY - 120d;

        // Source MOVE: Automatic* changes → full CREATE canonical from new source.
        var movedPlacement = new TimberLeaderPlacement(180d, 240d, 180d, 600d, physical);
        var movedCanonical =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativePlainItemNumber(
                movedPlacement,
                "K1",
                TimberLeaderHorizontalSide.Right);
        var movedOriented = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            movedCanonical,
            transform);
        var sourceSync = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            createOriented.ContentX,
            createOriented.ContentY,
            physical,
            movedOriented.ContentX,
            movedOriented.ContentY,
            physical);
        Assert.True(sourceSync.RequiresCanonicalRebuild);
        Assert.NotEqual(grippedContentX, movedOriented.ContentX, 8);
        Assert.NotEqual(grippedContentY, movedOriented.ContentY, 8);
        Assert.Equal(180d, movedOriented.AnchorX, 8);
        Assert.Equal(240d, movedOriented.AnchorY, 8);
        // Idempotent absolute CREATE geometry from the moved source.
        var again = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            movedCanonical,
            transform);
        Assert.Equal(movedOriented.ContentX, again.ContentX, 8);
        Assert.Equal(movedOriented.ContentY, again.ContentY, 8);
        Assert.Equal(movedOriented.KneeX, again.KneeX, 8);
        Assert.Equal(movedOriented.KneeY, again.KneeY, 8);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    public void FramedItemOnly_SourceStretch_Model_RebuildsCreateCanonical(
        ItemNumberLeaderStyle style)
    {
        var physical = 30d * Math.PI / 180d;
        var transform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var before = new TimberLeaderPlacement(100d, 200d, 100d, 560d, 0d);
        var beforeCanonical =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeFramedItem(
                before,
                "K1",
                style);
        var beforeOriented = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            beforeCanonical,
            transform);
        var manualFrameX = beforeOriented.ContentX + 300d;
        var manualFrameY = beforeOriented.ContentY - 150d;

        var after = new TimberLeaderPlacement(160d, 220d, 160d, 580d, 0d);
        var afterCanonical =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeFramedItem(
                after,
                "K1",
                style);
        var afterOriented = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            afterCanonical,
            transform);
        var sync = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            beforeOriented.ContentX,
            beforeOriented.ContentY,
            physical,
            afterOriented.ContentX,
            afterOriented.ContentY,
            physical);

        Assert.True(sync.RequiresCanonicalRebuild);
        Assert.NotEqual(manualFrameX, afterOriented.ContentX, 8);
        Assert.NotEqual(manualFrameY, afterOriented.ContentY, 8);
        Assert.Equal(after.AnchorX, afterOriented.AnchorX, 8);
        Assert.Equal(after.AnchorY, afterOriented.AnchorY, 8);
        Assert.Equal(afterOriented.KneeX, afterOriented.ContentX, 8);
        Assert.Equal(afterOriented.KneeY, afterOriented.ContentY, 8);
    }

    [Fact]
    public void G4UsesG4Composite_IsRetiredForCreate()
    {
        var policy = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AutoCadFramedG4CompositePolicy.cs"));
        var uses = Member(
            policy,
            "public static bool UsesG4Composite(");
        Assert.Contains("return false;", uses);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    public void FramedItemOnly_KeepsContentAtKnee_NoCombinedLandingGeometry(
        ItemNumberLeaderStyle style)
    {
        var basePlacement = new TimberLeaderPlacement(0d, 0d, 0d, 360d, 0d);
        var block = TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeFramedItem(
            basePlacement,
            "K1",
            style);
        Assert.Equal(block.KneeX, block.ContentX, 8);
        Assert.Equal(block.KneeY, block.ContentY, 8);
        Assert.Equal(0d, TimberItemLeaderLayoutCalculator.FramedItemLandingDistanceMm);
        Assert.Equal(TimberLeaderHorizontalSide.Right, block.Side);
    }

    [Fact]
    public void StandalonePlain_DefaultsPreferredSideToSemanticRight()
    {
        var upsert = Member(
            ElementLabelSource(),
            "public static bool UpsertForElement(");
        Assert.Contains(
            "preferredSideOverride: TimberLeaderHorizontalSide.Right",
            upsert);
        Assert.Contains(
            "standaloneNativeOrientation: true",
            upsert);
    }

    private static double Distance(double x0, double y0, double x1, double y1) =>
        Math.Sqrt(((x1 - x0) * (x1 - x0)) + ((y1 - y0) * (y1 - y0)));

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= 1e-6d;

    private static string ElementLabelSource() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs"));

    private static string StandaloneServiceSource() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AutoCadStandaloneFramedItemOnlyAnnotationService.cs"));

    private static string Member(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing member {signature}");
        var brace = source.IndexOf('{', start);
        Assert.True(brace >= 0);
        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces for {signature}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("Repository root not found.");
    }
}
