using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticPurlinRafterSourceRulesTests
{
    [Fact]
    public void SchemaCurrentVersion_IsThree_AndSupportsLegacy()
    {
        Assert.Equal(1, RoofPurlinLayoutSchema.Version1);
        Assert.Equal(2, RoofPurlinLayoutSchema.Version2);
        Assert.Equal(3, RoofPurlinLayoutSchema.CurrentVersion);
        Assert.True(RoofPurlinLayoutSchema.IsSupported(1));
        Assert.True(RoofPurlinLayoutSchema.IsSupported(2));
        Assert.True(RoofPurlinLayoutSchema.IsSupported(3));
        Assert.False(RoofPurlinLayoutSchema.IsSupported(4));
    }

    [Fact]
    public void LegacySchemaOne_WithoutRafterTrailer_LeavesPolicyUnset()
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.Version1,
            ridgeEnabledValue: 0,
            Array.Empty<RoofPurlinLayoutStoredItem>());

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.Null(result.Layout!.ManualRafterWidthMm);
        Assert.Equal(RoofAutomaticPurlinRafterSourcePolicy.Unset, result.Layout.RafterSourcePolicy);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.Unspecified,
            result.Layout.AcknowledgedActualKind);
    }

    [Fact]
    public void LegacySchemaTwo_DoesNotInventAcknowledgedActual()
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.Version2,
            ridgeEnabledValue: 0,
            Array.Empty<RoofPurlinLayoutStoredItem>(),
            rafterProfile: new RoofPurlinLayoutStoredRafterProfile(
                (int)RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
                90d,
                200d));

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.Unspecified,
            result.Layout!.AcknowledgedActualKind);
        Assert.Null(result.Layout.AcknowledgedActualWidthMm);
    }

    [Fact]
    public void SchemaThree_NonePresentManual_PersistsThroughValidateForWrite()
    {
        var layout = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults() with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
        };

        var written = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);
        Assert.True(written.IsValid, written.Error.ToString());
        Assert.True(RoofPurlinLayoutPersistenceRules.HasPersistedRafterProfile(written.Layout!));
        Assert.Equal(100d, written.Layout!.ManualRafterWidthMm);
        Assert.Equal(125d, written.Layout.ManualRafterHeightMm);
        Assert.Equal(
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            written.Layout.RafterSourcePolicy);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
            written.Layout.AcknowledgedActualKind);

        // Reconstruct via the same StoredRafterProfile shape EncodePayload writes.
        var reconstructed = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            ridgeEnabledValue: written.Layout.RidgeEnabled ? 1 : 0,
            items: Array.Empty<RoofPurlinLayoutStoredItem>(),
            wallPlateEnabledValue: written.Layout.WallPlateEnabled ? 1 : 0,
            wallPlateLowerEdgeHeightMm: written.Layout.WallPlateLowerEdgeHeightMm,
            rafterProfile: new RoofPurlinLayoutStoredRafterProfile(
                (int)written.Layout.RafterSourcePolicy,
                written.Layout.ManualRafterWidthMm!.Value,
                written.Layout.ManualRafterHeightMm!.Value,
                (int)written.Layout.AcknowledgedActualKind,
                0d,
                0d));
        Assert.True(reconstructed.IsValid, reconstructed.Error.ToString());
        Assert.Equal(100d, reconstructed.Layout!.ManualRafterWidthMm);
        Assert.Equal(125d, reconstructed.Layout.ManualRafterHeightMm);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
            reconstructed.Layout.AcknowledgedActualKind);
    }

    [Fact]
    public void SchemaThree_Roundtrip_PersistsAcknowledgementAtomically()
    {
        var layout = Layout(
            90d,
            200d,
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            80d,
            125d);

        var written = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);
        Assert.True(written.IsValid, written.Error.ToString());
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            written.Layout!.AcknowledgedActualKind);
        Assert.Equal(80d, written.Layout.AcknowledgedActualWidthMm);
        Assert.Equal(125d, written.Layout.AcknowledgedActualHeightMm);
        Assert.True(RoofPurlinLayoutPersistenceRules.HasPersistedRafterProfile(written.Layout));
    }

    [Fact]
    public void PreferManual_AcknowledgedActualUnchanged_RespectsDecision()
    {
        var layout = Layout(
            90d,
            200d,
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            80d,
            125d);

        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            actualAvailable: true,
            actualWidthMm: 80d,
            actualHeightMm: 125d,
            actualAmbiguous: false);

        Assert.Equal(RoofAutomaticPurlinRafterSourceRules.Outcome.UseManual, resolved.Outcome);
        Assert.Equal(90d, resolved.WidthMm);
        Assert.Equal(200d, resolved.HeightMm);
    }

    [Fact]
    public void PreferManual_AcknowledgedActualChanged_RaisesNewConflict()
    {
        var layout = Layout(
            90d,
            200d,
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            80d,
            125d);

        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            actualAvailable: true,
            actualWidthMm: 100d,
            actualHeightMm: 180d,
            actualAmbiguous: false);

        Assert.Equal(RoofAutomaticPurlinRafterSourceRules.Outcome.Conflict, resolved.Outcome);
    }

    [Fact]
    public void PreferManual_NonePresent_ThenActualAppears_RaisesConflict()
    {
        var layout = Layout(
            90d,
            200d,
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent);

        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            actualAvailable: true,
            actualWidthMm: 80d,
            actualHeightMm: 125d,
            actualAmbiguous: false);

        Assert.Equal(RoofAutomaticPurlinRafterSourceRules.Outcome.Conflict, resolved.Outcome);
    }

    [Fact]
    public void LegacySchemaTwoPreferManual_Unspecified_RequiresConfirmationWhenDiffer()
    {
        var layout = Layout(
            90d,
            200d,
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            RoofAutomaticPurlinAcknowledgedActualKind.Unspecified);

        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            actualAvailable: true,
            actualWidthMm: 80d,
            actualHeightMm: 125d,
            actualAmbiguous: false);

        Assert.Equal(RoofAutomaticPurlinRafterSourceRules.Outcome.Conflict, resolved.Outcome);
    }

    [Fact]
    public void PreferManual_NonePresent_WithoutActual_UsesManual()
    {
        var layout = Layout(
            90d,
            200d,
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent);

        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            actualAvailable: false,
            actualWidthMm: 0d,
            actualHeightMm: 0d,
            actualAmbiguous: false);

        Assert.Equal(RoofAutomaticPurlinRafterSourceRules.Outcome.UseManual, resolved.Outcome);
    }

    [Fact]
    public void PreferActual_WhenActualRemoved_RequiresExplicitConfirmation()
    {
        var layout = Layout(
            90d,
            200d,
            RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            80d,
            125d);

        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            actualAvailable: false,
            actualWidthMm: 0d,
            actualHeightMm: 0d,
            actualAmbiguous: false);

        Assert.Equal(
            RoofAutomaticPurlinRafterSourceRules.Outcome.MissingActualWithRecoverableManual,
            resolved.Outcome);
        Assert.Equal(90d, resolved.WidthMm);
        Assert.Equal(200d, resolved.HeightMm);
        Assert.Equal(
            RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            resolved.EffectivePolicy);
        Assert.True(resolved.HasRecoverableManualProfile);
    }

    [Fact]
    public void PreferActual_WhenActualRemoved_WithoutManual_IsUnresolved()
    {
        var layout = RoofAutomaticPurlinLayout.Empty with
        {
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            AcknowledgedActualWidthMm = 120d,
            AcknowledgedActualHeightMm = 160d,
        };

        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            actualAvailable: false,
            actualWidthMm: 0d,
            actualHeightMm: 0d,
            actualAmbiguous: false);

        Assert.Equal(
            RoofAutomaticPurlinRafterSourceRules.Outcome.UnresolvedNoSource,
            resolved.Outcome);
        Assert.False(resolved.HasRecoverableManualProfile);
    }

    [Fact]
    public void PreferActual_AcknowledgedActualChanged_WhileManualDiffers_RaisesConflict()
    {
        var layout = Layout(
            90d,
            200d,
            RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            80d,
            125d);

        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            actualAvailable: true,
            actualWidthMm: 100d,
            actualHeightMm: 180d,
            actualAmbiguous: false);

        Assert.Equal(RoofAutomaticPurlinRafterSourceRules.Outcome.Conflict, resolved.Outcome);
    }

    [Fact]
    public void PreferActual_AcknowledgedActualUnchanged_UsesActual()
    {
        var layout = Layout(
            90d,
            200d,
            RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            80d,
            125d);

        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            actualAvailable: true,
            actualWidthMm: 80d,
            actualHeightMm: 125d,
            actualAmbiguous: false);

        Assert.Equal(RoofAutomaticPurlinRafterSourceRules.Outcome.UseActual, resolved.Outcome);
        Assert.Equal(80d, resolved.WidthMm);
    }

    [Fact]
    public void CreateAcknowledgement_DistinguishesNonePresentAndExplicit()
    {
        var none = RoofAutomaticPurlinRafterSourceRules.CreateAcknowledgement(false, 0d, 0d);
        Assert.Equal(RoofAutomaticPurlinAcknowledgedActualKind.NonePresent, none.Kind);
        Assert.Null(none.WidthMm);

        var explicitProfile = RoofAutomaticPurlinRafterSourceRules.CreateAcknowledgement(
            true,
            80d,
            125d);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            explicitProfile.Kind);
        Assert.Equal(80d, explicitProfile.WidthMm);
        Assert.Equal(125d, explicitProfile.HeightMm);
    }

    [Fact]
    public void MatchingManualAndActual_NonePresent_StillConflictsOnFirstAppearance()
    {
        var layout = Layout(
            80d,
            125d,
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent);

        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            actualAvailable: true,
            actualWidthMm: 80d,
            actualHeightMm: 125d,
            actualAmbiguous: false);

        Assert.Equal(RoofAutomaticPurlinRafterSourceRules.Outcome.Conflict, resolved.Outcome);
        Assert.Equal(80d, resolved.WidthMm);
        Assert.Equal(125d, resolved.HeightMm);
    }

    [Fact]
    public void PreferManual_StaleAck_WhenActualMatchesManual_StillConflicts()
    {
        // Keep Manual acknowledged 120×160; later actual becomes 100×125 (== manual).
        // Must NOT suppress the NEW conflict via a manual==actual short-circuit.
        var layout = Layout(
            100d,
            125d,
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            120d,
            160d);

        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            actualAvailable: true,
            actualWidthMm: 100d,
            actualHeightMm: 125d,
            actualAmbiguous: false);

        Assert.Equal(RoofAutomaticPurlinRafterSourceRules.Outcome.Conflict, resolved.Outcome);
    }

    [Fact]
    public void PreferManual_ExplicitProfileMatchingActual_EvenWhenManualDiffers_UsesManual()
    {
        var layout = Layout(
            100d,
            125d,
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            120d,
            160d);

        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            actualAvailable: true,
            actualWidthMm: 120d,
            actualHeightMm: 160d,
            actualAmbiguous: false);

        Assert.Equal(RoofAutomaticPurlinRafterSourceRules.Outcome.UseManual, resolved.Outcome);
        Assert.Equal(100d, resolved.WidthMm);
        Assert.Equal(125d, resolved.HeightMm);
    }

    [Fact]
    public void OwnerWriteEquivalence_TreatsManualProfileOnlyChangeAsDifferent()
    {
        var withoutManual = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults();
        var withManual = withoutManual with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
        };

        Assert.False(
            RoofPurlinLayoutPersistenceRules.AreEquivalentForOwnerWrite(withoutManual, withManual));
        Assert.True(
            RoofPurlinLayoutPersistenceRules.AreEquivalentForOwnerWrite(withManual, withManual));
    }

    [Fact]
    public void OwnerWriteEquivalence_TreatsAcknowledgementChangeAsDifferent()
    {
        var nonePresent = Layout(
            100d,
            125d,
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent);
        var acknowledged = nonePresent with
        {
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            AcknowledgedActualWidthMm = 120d,
            AcknowledgedActualHeightMm = 160d,
        };

        Assert.False(
            RoofPurlinLayoutPersistenceRules.AreEquivalentForOwnerWrite(nonePresent, acknowledged));
    }

    private static RoofAutomaticPurlinLayout Layout(
        double widthMm,
        double heightMm,
        RoofAutomaticPurlinRafterSourcePolicy policy,
        RoofAutomaticPurlinAcknowledgedActualKind acknowledgedKind =
            RoofAutomaticPurlinAcknowledgedActualKind.Unspecified,
        double? acknowledgedWidthMm = null,
        double? acknowledgedHeightMm = null) =>
        RoofAutomaticPurlinLayout.Empty with
        {
            ManualRafterWidthMm = widthMm,
            ManualRafterHeightMm = heightMm,
            RafterSourcePolicy = policy,
            AcknowledgedActualKind = acknowledgedKind,
            AcknowledgedActualWidthMm = acknowledgedWidthMm,
            AcknowledgedActualHeightMm = acknowledgedHeightMm,
        };
}
