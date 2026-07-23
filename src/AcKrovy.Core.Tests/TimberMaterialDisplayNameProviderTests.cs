using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberMaterialDisplayNameProviderTests
{
    private static readonly string[] SupportedCultureNames =
        ["sk-SK", "cs-CZ", "en-US", "de-DE", "pl-PL", "fr-FR"];

    [Fact]
    public void Catalog_ContainsExactlyTheStableCanonicalValues()
    {
        Assert.Equal(
            [
                "Smrek C24",
                "Smrek C16",
                "Smrekovec C30",
                "KVH C24 NSi",
                "KVH C24 Si",
                "BSH GL24h",
            ],
            TimberMaterialCatalog.Items.Select(item => item.StoredValue));
    }

    [Theory]
    [MemberData(nameof(LocalizedMaterialData))]
    public void CanonicalMaterial_UsesLocalizedDisplayName(
        string cultureName,
        string storedValue,
        string expectedDisplayName)
    {
        Assert.Equal(
            expectedDisplayName,
            TimberMaterialDisplayNameProvider.GetDisplayName(
                storedValue,
                CultureInfo.GetCultureInfo(cultureName)));
    }

    [Theory]
    [MemberData(nameof(LocalizedMaterialData))]
    public void LocalizedCatalogSelection_RetainsCanonicalStoredValue(
        string cultureName,
        string storedValue,
        string expectedDisplayName)
    {
        var options = TimberMaterialDisplayNameProvider.GetOptions(
            storedValue,
            CultureInfo.GetCultureInfo(cultureName));
        var selection = Assert.Single(options, option =>
            string.Equals(option.StoredValue, storedValue, StringComparison.Ordinal));

        Assert.Equal(6, options.Count);
        Assert.True(selection.IsCatalogItem);
        Assert.Equal(expectedDisplayName, selection.DisplayName);
        Assert.Equal(
            storedValue,
            TimberMaterialEditRules.ResolvePatchValue(
                isMaterialChangeEnabled: true,
                selection.StoredValue));
    }

    [Fact]
    public void LegacySmrekC24_RemainsTheCanonicalStoredValue()
    {
        var option = Assert.Single(
            TimberMaterialDisplayNameProvider.GetOptions(
                "Smrek C24",
                CultureInfo.GetCultureInfo("en-US")),
            item => item.StoredValue == "Smrek C24");

        Assert.Equal("C24 – Spruce / Fir", option.DisplayName);
        Assert.Equal("Smrek C24", option.StoredValue);
    }

    [Fact]
    public void UnknownLegacyMaterial_IsDisplayedAndPreservedExactly()
    {
        const string legacyMaterial = "KVH SPECIAL";
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var options = TimberMaterialDisplayNameProvider.GetOptions(legacyMaterial, culture);
        var option = Assert.Single(options, item => item.StoredValue == legacyMaterial);

        Assert.Equal(7, options.Count);
        Assert.False(option.IsCatalogItem);
        Assert.Equal(legacyMaterial, option.DisplayName);
        Assert.Equal(
            legacyMaterial,
            TimberMaterialDisplayNameProvider.GetDisplayName(legacyMaterial, culture));
        Assert.Equal(
            legacyMaterial,
            TimberMaterialEditRules.ResolvePatchValue(true, option.StoredValue));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void NullOrEmptyMaterial_HasSafeDisplayValue(
        string? storedMaterial,
        string expected)
    {
        Assert.Equal(
            expected,
            TimberMaterialDisplayNameProvider.GetDisplayName(
                storedMaterial,
                CultureInfo.GetCultureInfo("sk-SK")));
    }

    [Theory]
    [InlineData(false, "Smrek C24", "Smrek C24", false)]
    [InlineData(false, "KVH C24 NSi", "Smrek C24", true)]
    [InlineData(true, "KVH C24 NSi", "Smrek C24", false)]
    public void SelectionChange_ActivatesMaterialApplyFlagOnlyForUserSelection(
        bool isInitializing,
        string selectedStoredValue,
        string originalStoredValue,
        bool expected)
    {
        Assert.Equal(
            expected,
            TimberMaterialEditRules.ShouldActivateApplyFlag(
                isInitializing,
                selectedStoredValue,
                originalStoredValue));
    }

    [Fact]
    public void DisabledBatchMaterialChange_DoesNotCreatePatch()
    {
        Assert.Null(TimberMaterialEditRules.ResolvePatchValue(
            isMaterialChangeEnabled: false,
            TimberMaterialCatalog.KvhC24Nsi));
    }

    [Fact]
    public void CatalogSelection_CreatesCanonicalPatchAndPatcherPersistsIt()
    {
        var original = new TimberElementData
        {
            ElementId = "K1",
            ElementType = TimberElementType.Rafter,
            Material = TimberMaterialCatalog.SpruceC24,
        };
        var selectedStoredValue = TimberMaterialDisplayNameProvider
            .GetOptions(original.Material, CultureInfo.GetCultureInfo("en-US"))
            .Single(option => option.StoredValue == TimberMaterialCatalog.KvhC24Nsi)
            .StoredValue;
        var applyFlag = TimberMaterialEditRules.ShouldActivateApplyFlag(
            isInitializing: false,
            selectedStoredValue,
            original.Material);
        var patch = EmptyPatch() with
        {
            Material = TimberMaterialEditRules.ResolvePatchValue(
                applyFlag,
                selectedStoredValue),
        };

        Assert.True(applyFlag);
        Assert.Equal("KVH C24 NSi", patch.Material);

        var updated = TimberElementPatcher.Apply(original, patch);

        Assert.Equal("KVH C24 NSi", updated.Material);
        Assert.Equal("Smrek C24", original.Material);
    }

    [Fact]
    public void ChangingDisplayCulture_DoesNotChangeStoredMaterialOrSignature()
    {
        foreach (var catalogItem in TimberMaterialCatalog.Items)
        {
            var measurement = Measurement("K9", catalogItem.StoredValue, 5000);
            var signature = TimberElementSignature.FromMeasurement(measurement);

            foreach (var cultureName in SupportedCultureNames)
            {
                _ = TimberMaterialDisplayNameProvider.GetDisplayName(
                    measurement.Data.Material,
                    CultureInfo.GetCultureInfo(cultureName));

                Assert.Equal(catalogItem.StoredValue, measurement.Data.Material);
                Assert.Equal(signature, TimberElementSignature.FromMeasurement(measurement));
            }
        }
    }

    [Fact]
    public void Renumbering_IsIndependentOfLocalizedMaterialDisplay()
    {
        var measurements = TimberMaterialCatalog.Items
            .Select((item, index) => Measurement(
                $"K{index + 10}",
                item.StoredValue,
                4000 + (index * 100)))
            .Append(Measurement("K99", "KVH SPECIAL", 5000))
            .ToArray();
        var expected = Renumber(measurements);

        foreach (var cultureName in SupportedCultureNames)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            foreach (var measurement in measurements)
            {
                _ = TimberMaterialDisplayNameProvider.GetDisplayName(
                    measurement.Data.Material,
                    culture);
            }

            Assert.Equal(expected, Renumber(measurements));
        }
    }

    public static TheoryData<string, string, string> LocalizedMaterialData()
    {
        var data = new TheoryData<string, string, string>();

        AddCulture(data, "sk-SK",
            "C24 – Smrek / Jedľa",
            "C16 – Smrek / Jedľa",
            "C30 – Smrekovec / Červený smrek",
            "KVH C24 (NSi) – sušený hobľovaný hranol",
            "KVH C24 (Si) – pohľadový hranol",
            "BSH GL24h – lepené lamelové drevo");
        AddCulture(data, "cs-CZ",
            "C24 – Smrk / Jedle",
            "C16 – Smrk / Jedle",
            "C30 – Modřín / Červený smrk",
            "KVH C24 (NSi) – sušený hoblovaný hranol",
            "KVH C24 (Si) – pohledový hranol",
            "BSH GL24h – lepené lamelové dřevo");
        AddCulture(data, "en-US",
            "C24 – Spruce / Fir",
            "C16 – Spruce / Fir",
            "C30 – Larch / Red spruce",
            "KVH C24 (NSi) – dried planed timber",
            "KVH C24 (Si) – visual-grade timber",
            "BSH GL24h – glued laminated timber");
        AddCulture(data, "de-DE",
            "C24 – Fichte / Tanne",
            "C16 – Fichte / Tanne",
            "C30 – Lärche / Rotfichte",
            "KVH C24 (NSi) – getrocknetes gehobeltes Kantholz",
            "KVH C24 (Si) – Sichtkantholz",
            "BSH GL24h – Brettschichtholz");
        AddCulture(data, "pl-PL",
            "C24 – Świerk / Jodła",
            "C16 – Świerk / Jodła",
            "C30 – Modrzew / Świerk czerwony",
            "KVH C24 (NSi) – suszony strugany kantownik",
            "KVH C24 (Si) – kantownik do zastosowań widocznych",
            "BSH GL24h – drewno klejone warstwowo");
        AddCulture(data, "fr-FR",
            "C24 – Épicéa / Sapin",
            "C16 – Épicéa / Sapin",
            "C30 – Mélèze / Épicéa rouge",
            "KVH C24 (NSi) – bois équarri séché raboté",
            "KVH C24 (Si) – bois équarri de qualité visible",
            "BSH GL24h – bois lamellé-collé");

        return data;
    }

    private static void AddCulture(
        TheoryData<string, string, string> data,
        string cultureName,
        params string[] displayNames)
    {
        Assert.Equal(TimberMaterialCatalog.Items.Count, displayNames.Length);
        for (var index = 0; index < TimberMaterialCatalog.Items.Count; index++)
        {
            data.Add(
                cultureName,
                TimberMaterialCatalog.Items[index].StoredValue,
                displayNames[index]);
        }
    }

    private static (string ElementId, TimberElementSignature Signature)[] Renumber(
        IEnumerable<TimberElementMeasurement> measurements) =>
        TimberElementItemNumbering
            .RenumberElementIdsByCuttingLength(measurements)
            .Select(item => (item.ElementId, item.Signature))
            .ToArray();

    private static TimberElementPatch EmptyPatch() => new(
        ElementType: null,
        WidthMm: null,
        HeightMm: null,
        SlopeDegrees: null,
        RoofPlaneId: null,
        CuttingAllowanceMm: null,
        LengthCalculationMode: null,
        ManualLengthMm: null,
        Material: null,
        Note: null);

    private static TimberElementMeasurement Measurement(
        string elementId,
        string material,
        double cuttingLengthMm)
    {
        var data = new TimberElementData
        {
            ElementId = elementId,
            ElementType = TimberElementType.Rafter,
            WidthMm = 80,
            HeightMm = 160,
            Material = material,
        };

        return new TimberElementMeasurement(
            data,
            PlanLengthMm: cuttingLengthMm,
            ActualLengthMm: cuttingLengthMm,
            CuttingLengthMm: cuttingLengthMm,
            VolumeM3: 0.064);
    }
}
