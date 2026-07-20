using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class LocalizationCultureCollection
{
    public const string CollectionName = "Localization culture tests";
}

[Collection(LocalizationCultureCollection.CollectionName)]
public sealed class LocalizationFoundationTests
{
    private static readonly string[] CultureNames = ["sk-SK", "cs-CZ", "en-US", "de-DE", "pl-PL", "fr-FR"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    [Theory]
    [InlineData(TimberElementType.Rafter, "Krokva")]
    [InlineData(TimberElementType.WallPlate, "Pomúrnica")]
    [InlineData(TimberElementType.Purlin, "Väznica")]
    [InlineData(TimberElementType.Post, "Stĺpik")]
    [InlineData(TimberElementType.CollarTie, "Klieština / hambálok")]
    [InlineData(TimberElementType.Brace, "Vzpera")]
    [InlineData(TimberElementType.TieBeam, "Väzný trám")]
    public void ElementTypeProvider_SeparatesDisplayNameFromTechnicalEnum(
        TimberElementType type,
        string expectedDisplayName)
    {
        Assert.Equal(expectedDisplayName, TimberElementTypeDisplayNameProvider.GetDisplayName(type));
        Assert.NotEqual(expectedDisplayName, type.ToString());
    }

    [Theory]
    [InlineData(LengthCalculationMode.AutoByElementType, "Automaticky podľa typu")]
    [InlineData(LengthCalculationMode.PlanLength, "Pôdorysná dĺžka")]
    [InlineData(LengthCalculationMode.SlopeCorrected, "Prepočítať podľa sklonu")]
    [InlineData(LengthCalculationMode.ManualLength, "Ručne zadaná dĺžka")]
    public void LengthModeProvider_ReturnsResourceDisplayName(
        LengthCalculationMode mode,
        string expectedDisplayName)
    {
        Assert.Equal(expectedDisplayName, LengthCalculationModeDisplayNameProvider.GetDisplayName(mode));
    }

    [Fact]
    public void MetadataSerialization_IsIdenticalAcrossUiCultures()
    {
        var data = SampleData();

        var serialized = CultureNames
            .Select(culture => InCulture(culture, () => JsonSerializer.Serialize(data, JsonOptions)))
            .ToList();

        Assert.Single(serialized.Distinct(StringComparer.Ordinal));
        Assert.Contains("\"ElementType\":\"Rafter\"", serialized[0]);
        Assert.Contains("\"LengthCalculationMode\":\"SlopeCorrected\"", serialized[0]);
        Assert.DoesNotContain("Krokva", serialized[0], StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityAndSignature_AreIdenticalAcrossUiCultures()
    {
        var data = SampleData();
        var measurement = new TimberElementMeasurement(data, 4000, 4800, 5000, 0.064);

        var results = CultureNames
            .Select(culture => InCulture(culture, () => new
            {
                ElementId = TimberElementIdentityRules.CreateElementId(data.ElementType, 12),
                Signature = TimberElementSignature.FromMeasurement(measurement),
            }))
            .ToList();

        Assert.All(results, result => Assert.Equal("K12", result.ElementId));
        Assert.Single(results.Select(result => result.Signature).Distinct());
    }

    [Fact]
    public void LegacyMetadata_LoadsIdenticallyAcrossUiCultures()
    {
        const string json = """
            {
              "ElementId": "K9",
              "ElementType": "Rafter",
              "WidthMm": 90,
              "HeightMm": 170,
              "SlopeDegrees": 37,
              "RoofPlaneId": "R3",
              "CuttingAllowanceMm": 120,
              "LengthCalculationMode": "SlopeCorrected",
              "Material": "Smrek C24"
            }
            """;

        var loaded = CultureNames
            .Select(culture => InCulture(culture, () =>
            {
                var data = JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions);
                return TimberElementDataVersioning.Normalize(Assert.IsType<TimberElementData>(data));
            }))
            .ToList();

        Assert.All(loaded, data =>
        {
            Assert.Equal(TimberElementDataSchema.CurrentVersion, data.SchemaVersion);
            Assert.Equal("K9", data.ElementId);
            Assert.Equal(TimberElementType.Rafter, data.ElementType);
            Assert.Equal(LengthCalculationMode.SlopeCorrected, data.LengthCalculationMode);
        });
        Assert.Single(loaded.Distinct());
    }

    [Fact]
    public void DefaultCadLayerNames_AreIdenticalAcrossUiCultures()
    {
        var layerNames = CultureNames
            .Select(culture => InCulture(culture, () => Enum
                .GetValues<TimberElementType>()
                .Select(type => ElementLayerProfile.CreateDefault().GetStyle(type).LayerName)
                .ToArray()))
            .ToList();

        Assert.All(layerNames, names => Assert.Equal(
            ["KROKVA", "POMURNICA", "VAZNICA", "STLPIK", "KLIESTINA", "VZPERA", "VAZNY_TRAM"],
            names));
    }

    [Fact]
    public void ReportData_IsIdenticalAcrossUiCultures()
    {
        var data = SampleData();
        var measurement = new TimberElementMeasurement(data, 4000, 4800, 5000, 0.064);

        var reports = CultureNames
            .Select(culture => InCulture(culture, () => TimberReportBuilder.Build([measurement])))
            .ToList();

        Assert.All(reports, report =>
        {
            var line = Assert.Single(report.Lines);
            Assert.Equal("K1", line.ElementId);
            Assert.Equal(TimberElementType.Rafter, line.ElementType);
            Assert.Equal("Smrek C24", line.Material);
            Assert.Equal(0.064, report.TotalVolumeM3, 6);
        });
    }

    [Fact]
    public void PilotReportResources_PreserveCurrentSlovakText()
    {
        Assert.Equal("ACAD KROVY – výkaz reziva", UiStrings.ReportTitle);
        Assert.Equal("Položka", UiStrings.ReportColumnItem);
        Assert.Equal("Typ", UiStrings.ReportColumnType);
        Assert.Equal("Materiál", UiStrings.ReportColumnMaterial);
        Assert.Equal("Šírka [mm]", UiStrings.ReportColumnWidthMm);
        Assert.Equal("Výška [mm]", UiStrings.ReportColumnHeightMm);
        Assert.Equal("Dĺžka kusu [m]", UiStrings.ReportColumnPieceLengthM);
        Assert.Equal("Počet", UiStrings.ReportColumnCount);
        Assert.Equal("Celková dĺžka [m]", UiStrings.ReportColumnTotalLengthM);
        Assert.Equal("Kubatúra [m³]", UiStrings.ReportColumnVolumeM3);
        Assert.Equal("Spolu: {0} prvkov", UiStrings.ReportTotalFormat);
    }

    [Fact]
    public void FrenchUiCulture_UsesDefaultSlovakResourceFallback()
    {
        var result = InCulture("fr-FR", () => new
        {
            ElementType = TimberElementTypeDisplayNameProvider.GetDisplayName(TimberElementType.Rafter),
            LengthMode = LengthCalculationModeDisplayNameProvider.GetDisplayName(LengthCalculationMode.SlopeCorrected),
            ReportTitle = UiStrings.ReportTitle,
            ReportTotalFormat = UiStrings.ReportTotalFormat,
        });

        Assert.Equal("Krokva", result.ElementType);
        Assert.Equal("Prepočítať podľa sklonu", result.LengthMode);
        Assert.Equal("ACAD KROVY – výkaz reziva", result.ReportTitle);
        Assert.Equal("Spolu: {0} prvkov", result.ReportTotalFormat);
    }

    private static TimberElementData SampleData() => new()
    {
        SchemaVersion = TimberElementDataSchema.CurrentVersion,
        ElementId = "K1",
        ElementType = TimberElementType.Rafter,
        WidthMm = 80,
        HeightMm = 160,
        SlopeDegrees = 35,
        RoofPlaneId = "R1",
        CuttingAllowanceMm = 200,
        LengthCalculationMode = LengthCalculationMode.SlopeCorrected,
        Material = "Smrek C24",
        Note = "poznámka",
    };

    private static T InCulture<T>(string cultureName, Func<T> action)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
