using System.Globalization;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

/// <summary>
/// HOST reproduction for lost manual rafter W×H: draft → validated schema-3 layout →
/// independent restore into a NEW ViewModel. Encode/Decode TypedValue itself requires
/// Acdbmgd at runtime; the production Apply skip gate and Core stored-form round-trip
/// are asserted here together with VM restoration.
/// </summary>
[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class AutomaticPurlinManualProfileStoreRoundTripTests
{
    [Fact]
    public void HostReproduction_Schema3ManualProfile_SurvivesPersistedFormAndNewViewModel()
    {
        AppLanguageService.Apply("en");
        var originalDraft = BuildHostManualDraft(100d, 125d);

        // Production Apply writes ValidateForWrite(layout); this is the CAD-neutral
        // stored form that RoofPurlinLayoutStore.EncodePayload serializes as schema-3.
        var persisted = PersistLikeProductionApply(originalDraft);
        Assert.True(RoofPurlinLayoutPersistenceRules.HasPersistedRafterProfile(persisted));
        Assert.Equal(100d, persisted.ManualRafterWidthMm);
        Assert.Equal(125d, persisted.ManualRafterHeightMm);
        Assert.Equal(
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            persisted.RafterSourcePolicy);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
            persisted.AcknowledgedActualKind);

        // Dispose original draft semantics: NEW ViewModel from recovered layout only.
        var restored = CreateViewModel(
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed,
            persisted,
            layoutExists: true);

        Assert.Equal(100d, restored.RafterWidthMm);
        Assert.Equal(125d, restored.RafterHeightMm);
        Assert.Equal(AutomaticPurlinRafterDimensionSource.ExplicitManual, restored.RafterDimensionSource);
        Assert.Equal(
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            restored.DraftRafterSourcePolicy);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
            restored.DraftAcknowledgedActualKind);
        Assert.False(restored.RequiresManualRafterInput);
        Assert.False(restored.ShowNoRafterFooterWarning);
        Assert.True(restored.HasExplicitManualRafterProfile);
        Assert.True(restored.CanApply);
    }

    [Fact]
    public void Schema3Manual_RemainsAfterActualRafterAppears_AndRequiresNewConflict()
    {
        AppLanguageService.Apply("en");
        var stored = PersistLikeProductionApply(BuildHostManualDraft(100d, 125d));

        Assert.Equal(100d, stored.ManualRafterWidthMm);
        Assert.Equal(125d, stored.ManualRafterHeightMm);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
            stored.AcknowledgedActualKind);

        var withActual = CreateViewModel(
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            stored,
            layoutExists: true,
            rafterWidthMm: 120d,
            rafterHeightMm: 160d);

        Assert.Equal(100d, withActual.RecoverableManualRafterWidthMm);
        Assert.Equal(125d, withActual.RecoverableManualRafterHeightMm);
        Assert.Equal(100d, withActual.RafterWidthMm);
        Assert.Equal(125d, withActual.RafterHeightMm);
        Assert.True(withActual.ShowRafterSourceConflictWarning);
        Assert.False(withActual.CanApply);
        Assert.Equal(AutomaticPurlinRafterDimensionSource.ExplicitManual, withActual.RafterDimensionSource);
        Assert.True(withActual.ShowManualRafterFooterInfo);
        Assert.Equal(120d, withActual.ConflictActualRafterWidthMm);
        Assert.Equal(160d, withActual.ConflictActualRafterHeightMm);
    }

    [Fact]
    public void LegacySchema1AndSchema2_StillLoad()
    {
        var schema1 = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.Version1,
            ridgeEnabledValue: 1,
            items: Array.Empty<RoofPurlinLayoutStoredItem>(),
            wallPlateEnabledValue: 1);
        Assert.True(schema1.IsValid, schema1.Error.ToString());
        Assert.Equal(
            RoofAutomaticPurlinRafterSourcePolicy.Unset,
            schema1.Layout!.RafterSourcePolicy);
        Assert.Null(schema1.Layout.ManualRafterWidthMm);

        var schema2 = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.Version2,
            ridgeEnabledValue: 1,
            items: Array.Empty<RoofPurlinLayoutStoredItem>(),
            wallPlateEnabledValue: 1,
            wallPlateLowerEdgeHeightMm: 0d,
            rafterProfile: new RoofPurlinLayoutStoredRafterProfile(
                (int)RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
                100d,
                125d));
        Assert.True(schema2.IsValid, schema2.Error.ToString());
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.Unspecified,
            schema2.Layout!.AcknowledgedActualKind);
        Assert.Equal(100d, schema2.Layout.ManualRafterWidthMm);

        var schema3 = PersistLikeProductionApply(BuildHostManualDraft(100d, 125d));
        Assert.Equal(RoofAutomaticPurlinRafterSourcePolicy.PreferManual, schema3.RafterSourcePolicy);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
            schema3.AcknowledgedActualKind);
    }

    [Fact]
    public void PriorSchema1Layout_PlusManualOnlyChange_ForcesOwnerWrite()
    {
        var priorSchema1 = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults();
        var afterManualConfirm = priorSchema1 with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
        };

        // HOST root cause: LayoutsEqual omitted rafter fields → DecideLayoutWrite Unchanged
        // → schema-3 trailer never written → reopen asked for manual again.
        Assert.False(
            RoofPurlinLayoutPersistenceRules.AreEquivalentForOwnerWrite(
                priorSchema1,
                afterManualConfirm));
    }

    private static RoofAutomaticPurlinLayout BuildHostManualDraft(double widthMm, double heightMm)
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        Assert.True(viewModel.TryApplyManualRafterDimensions(widthMm, heightMm));
        Assert.True(viewModel.TryCreateDraft(out var draft, out _));
        Assert.NotNull(draft);
        return draft!;
    }

    private static RoofAutomaticPurlinLayout PersistLikeProductionApply(
        RoofAutomaticPurlinLayout draft)
    {
        var validated = RoofPurlinLayoutPersistenceRules.ValidateForWrite(draft);
        Assert.True(validated.IsValid, validated.Error.ToString());
        Assert.NotNull(validated.Layout);
        // EncodePayload starts with the same ValidateForWrite canonical layout.
        return validated.Layout!;
    }

    private static AutomaticPurlinDialogViewModel CreateViewModel(
        AutomaticPurlinRafterDimensionSource source,
        RoofAutomaticPurlinLayout? layout = null,
        bool layoutExists = false,
        double? rafterWidthMm = null,
        double? rafterHeightMm = null)
    {
        var solved = Solve(RectanglePoints());
        var rafterDefaults = TimberElementDefaults.For(TimberElementType.Rafter);
        if (rafterWidthMm is { } width)
        {
            rafterDefaults = rafterDefaults with { WidthMm = width };
        }

        if (rafterHeightMm is { } height)
        {
            rafterDefaults = rafterDefaults with { HeightMm = height };
        }

        return new AutomaticPurlinDialogViewModel(
            solved.Geometry,
            solved.Provenance,
            layout,
            layoutExists,
            null,
            false,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            rafterDefaults,
            CultureInfo.GetCultureInfo("en-US"),
            AutomaticPurlinDialogMode.ProductionEdit,
            existingAutomaticPurlinCount: 0,
            storedDatumLoadError: null,
            rafterDimensionSource: source);
    }

    private static SolvedFixture Solve(RoofPoint2D[] points)
    {
        var input = new RoofFootprintInput(points, true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid, normalized.Validation.Error.ToString());
        var identity = RoofBoundaryIdentityRules.Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            normalized.EdgeProvenance.Count,
            RoofBoundaryIdentityRules.FormatWinding(normalized.Validation.SourceOrientation),
            Enumerable.Range(1, normalized.EdgeProvenance.Count).ToArray()).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        Assert.True(provenance.IsValid);
        var geometry = HipRoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(geometry.IsValid, geometry.Error.ToString());
        return new SolvedFixture(Assert.IsType<HipRoofGeometry>(geometry.Geometry), provenance);
    }

    private static RoofPoint2D[] RectanglePoints() =>
        [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)];

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
