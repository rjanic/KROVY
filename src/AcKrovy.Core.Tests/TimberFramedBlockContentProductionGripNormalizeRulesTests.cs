using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentProductionGripNormalizeRulesTests
{
    [Fact]
    public void Applicability_R3CombinedAccepted_LegacyR2DimAccepted_ForeignRejected()
    {
        var r3 = TimberFramedBlockContentVariantRules.CreateSafeBlockName(
            TimberFramedBlockContentVariantRules.CreateRawKey(
                TimberFramedBlockContentKind.Circle,
                "MEDIUM",
                "Standard",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.Combined));
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsProductionApplicableBlockContent(
                    r3,
                    hasItemNo: true,
                    hasWidth: true,
                    hasHeight: true));
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsR3ContentVariantOnlyPath(r3));

        const string dimnx =
            "AK_KROVY_FBC_R2_CIR_MEDIUM_COMB_DIMNX_I2.7_D2.5_ISSTANDARD_DSSTANDARD";
        const string dimpx =
            "AK_KROVY_FBC_R2_CIR_MEDIUM_COMB_DIMPX_I2.7_D2.5_ISSTANDARD_DSSTANDARD";
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsProductionApplicableBlockContent(
                    dimnx,
                    hasItemNo: true,
                    hasWidth: true,
                    hasHeight: true));
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsProductionApplicableBlockContent(
                    dimpx,
                    hasItemNo: true,
                    hasWidth: true,
                    hasHeight: true));
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsLegacyR2FullNormalizePath(dimnx));

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
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsProductionApplicableBlockContent(
                    itemOnly,
                    hasItemNo: true,
                    hasWidth: false,
                    hasHeight: false));

        Assert.False(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsProductionApplicableBlockContent(
                    "FOREIGN_BLOCK",
                    hasItemNo: true,
                    hasWidth: true,
                    hasHeight: true));
        Assert.False(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsProductionApplicableBlockContent(
                    "AK_G4_LEGACY_COMB",
                    hasItemNo: true,
                    hasWidth: true,
                    hasHeight: true));
        Assert.False(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsProductionApplicableBlockContent(
                    dimnx,
                    hasItemNo: true,
                    hasWidth: false,
                    hasHeight: true));
    }

    [Fact]
    public void DebugMarkers_Recognized_AndYieldExclusivity()
    {
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules.IsDebugProofMarkerToken(
                "FBC_GRIP_NORMALIZE|P4B-CIRCLE-COMB-R-90-D50"));
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules.IsDebugProofRegAppName(
                "AK_DEV_FBC_GRIP_NORMALIZE"));
        Assert.False(
            TimberFramedBlockContentProductionGripNormalizeRules.IsDebugProofMarkerToken(
                "DECORAIR_ACADKROVY_LABEL"));
        Assert.False(
            TimberFramedBlockContentProductionGripNormalizeRules.IsDebugProofRegAppName(
                "DECORAIR_ACADKROVY_LABEL"));

        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules.ShouldYieldToDebugProof(
                debugProofArmed: true,
                entityHasDebugProofMarker: true));
        Assert.False(
            TimberFramedBlockContentProductionGripNormalizeRules.ShouldYieldToDebugProof(
                debugProofArmed: true,
                entityHasDebugProofMarker: false));
        Assert.False(
            TimberFramedBlockContentProductionGripNormalizeRules.ShouldYieldToDebugProof(
                debugProofArmed: false,
                entityHasDebugProofMarker: true));
    }

    [Fact]
    public void Registration_DuplicatePrevented_UnregisterIdempotent()
    {
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules.ShouldRegisterOverrule(
                alreadyRegistered: false));
        Assert.False(
            TimberFramedBlockContentProductionGripNormalizeRules.ShouldRegisterOverrule(
                alreadyRegistered: true));
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules.ShouldUnregisterOverrule(
                currentlyRegistered: true));
        Assert.False(
            TimberFramedBlockContentProductionGripNormalizeRules.ShouldUnregisterOverrule(
                currentlyRegistered: false));
    }

    [Fact]
    public void CallbackOrder_MatchesStageE()
    {
        Assert.Equal(
            TimberFramedBlockContentGripStageProofRules.NormalizeCallbackOrder,
            TimberFramedBlockContentProductionGripNormalizeRules.NormalizeCallbackOrder);
        Assert.Equal(
            TimberFramedBlockContentStretchNormalizeRules.NormalizeOperationOrder,
            new[]
            {
                TimberFramedBlockContentStretchNormalizeRules.DoglegStep,
                TimberFramedBlockContentStretchNormalizeRules.ContentSideStep,
            });
    }

    [Fact]
    public void Eligibility_R3Combined_IsContentVariantOnly_NotLegacyStretchNormalize()
    {
        var name = TimberFramedBlockContentVariantRules.CreateSafeBlockName(
            TimberFramedBlockContentVariantRules.CreateRawKey(
                TimberFramedBlockContentKind.Rectangle,
                "MEDIUM",
                "Standard",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.Combined));
        Assert.False(
            TimberFramedBlockContentStretchNormalizeRules.IsEligibleBlockContent(
                name,
                true,
                true,
                true));
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsProductionApplicableBlockContent(name, true, true, true));
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsR3ContentVariantOnlyPath(name));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(35d)]
    [InlineData(90d)]
    [InlineData(135d)]
    [InlineData(180d)]
    [InlineData(225d)]
    [InlineData(270d)]
    [InlineData(315d)]
    public void R3KneeGrip_UnchangedSource_PresentationFollowsFinalLanding(
        double sourceDeg)
    {
        var source = sourceDeg * Math.PI / 180d;
        Assert.True(
            TimberFramedBlockContentGripPresentationRules
                .MustSyncPresentationToFinalLandingAfterKneeGrip(source, source));
        var createPresentation =
            TimberFramedBlockContentGripPresentationRules
                .ExpectedCreatePresentationRadians(source);

        // Landing still on CREATE axis → presentation matches CREATE.
        Assert.True(
            TimberFramedBlockContentGripPresentationRules
                .TryResolvePresentationFromFinalLandingRadians(
                    0d,
                    0d,
                    Math.Cos(source) * 100d,
                    Math.Sin(source) * 100d,
                    out var afterAligned));
        Assert.True(
            TimberFramedBlockContentGripPresentationRules.PresentationPreserved(
                createPresentation,
                afterAligned));

        // Landing rotated +35° → presentation follows Decide(new landing), may change.
        var rotatedLanding = source + (35d * Math.PI / 180d);
        var expectedRotated =
            TimberFramedBlockContentReadableOrientationRules
                .Decide(rotatedLanding)
                .PresentationAngle;
        Assert.True(
            TimberFramedBlockContentGripPresentationRules
                .TryResolvePresentationFromFinalLandingRadians(
                    0d,
                    0d,
                    Math.Cos(rotatedLanding) * 100d,
                    Math.Sin(rotatedLanding) * 100d,
                    out var afterRotated));
        Assert.True(
            TimberFramedBlockContentGripPresentationRules.PresentationPreserved(
                expectedRotated,
                afterRotated));
    }
}
