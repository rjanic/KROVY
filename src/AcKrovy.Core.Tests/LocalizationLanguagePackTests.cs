using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

[Collection(LocalizationCultureCollection.CollectionName)]
public sealed partial class LocalizationLanguagePackTests
{
    private const string DefaultResourceFile = "UiStrings.resx";
    private static readonly string[] SatelliteResourceFiles =
    [
        "UiStrings.cs.resx", "UiStrings.en.resx", "UiStrings.de.resx", "UiStrings.pl.resx", "UiStrings.fr.resx",
    ];
    private static readonly (string CultureName, string Rafter)[] SupportedCultures =
    [
        ("sk-SK", "Krokva"),
        ("cs-CZ", "Krokev"),
        ("en-US", "Rafter"),
        ("de-DE", "Sparren"),
        ("pl-PL", "Krokiew"),
        ("fr-FR", "Chevron"),
    ];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void SatelliteResourceFiles_MatchDefaultKeysAndContainNoEmptyValues()
    {
        var defaultResources = LoadResources(DefaultResourceFile);
        Assert.Equal(202, defaultResources.Count);
        Assert.All(defaultResources, item => Assert.False(string.IsNullOrWhiteSpace(item.Value)));

        foreach (var fileName in SatelliteResourceFiles)
        {
            var localized = LoadResources(fileName);

            Assert.Equal(defaultResources.Keys.Order(), localized.Keys.Order());
            Assert.Equal(defaultResources.Count, localized.Count);
            Assert.All(localized, item => Assert.False(string.IsNullOrWhiteSpace(item.Value)));
        }
    }

    [Fact]
    public void SatelliteResourcePlaceholders_MatchDefaultExactly()
    {
        var defaultResources = LoadResources(DefaultResourceFile);

        foreach (var fileName in SatelliteResourceFiles)
        {
            var localized = LoadResources(fileName);

            Assert.All(defaultResources, item => Assert.Equal(
                ExtractPlaceholders(item.Value),
                ExtractPlaceholders(localized[item.Key])));
        }
    }

    [Fact]
    public void SatelliteResources_PreserveTechnicalCommandAndLayerNames()
    {
        var defaultResources = LoadResources(DefaultResourceFile);

        foreach (var fileName in SatelliteResourceFiles)
        {
            var localized = LoadResources(fileName);

            Assert.All(defaultResources, item => Assert.Equal(
                ExtractTechnicalNames(item.Value),
                ExtractTechnicalNames(localized[item.Key])));
        }
    }

    [Theory]
    [MemberData(nameof(SupportedCultureData))]
    public void SpecificUiCultures_ResolveExpectedNeutralSatelliteResource(string cultureName, string expectedRafter)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);

        Assert.Equal(expectedRafter, UiStrings.GetString("ElementType_Rafter", culture));
        Assert.Equal(expectedRafter, TimberElementTypeDisplayNameProvider.GetDisplayName(TimberElementType.Rafter, culture));
    }

    [Fact]
    public void DisplayProviders_ReturnConsistentLocalizedElementTypesLengthModesAndSlopeDirections()
    {
        var expected = new Dictionary<string, (string[] Types, string[] Modes, string[] Directions)>
        {
            ["sk-SK"] = (["Krokva", "Pomúrnica", "Väznica", "Stĺpik", "Klieština / hambálok", "Vzpera", "Väzný trám"], ["Automaticky podľa typu", "Pôdorysná dĺžka", "Prepočítať podľa sklonu", "Ručne zadaná dĺžka"], ["Normálny (začiatok → koniec)", "Obrátený (koniec → začiatok)"]),
            ["cs-CZ"] = (["Krokev", "Pozednice", "Vaznice", "Sloupek", "Kleština / hambálek", "Vzpěra", "Vazný trám"], ["Automaticky podle typu", "Půdorysná délka", "Přepočítat podle sklonu", "Ručně zadaná délka"], ["Normální (začátek → konec)", "Obrácený (konec → začátek)"]),
            ["en-US"] = (["Rafter", "Wall plate", "Purlin", "Post", "Collar tie", "Brace", "Tie beam"], ["Automatic by element type", "Plan length", "Correct for slope", "Manually entered length"], ["Normal (start → end)", "Reversed (end → start)"]),
            ["de-DE"] = (["Sparren", "Mauerlatte", "Pfette", "Stütze", "Zange / Kehlbalken", "Strebe", "Bundbalken"], ["Automatisch nach Bauteiltyp", "Grundrisslänge", "Nach Neigung umrechnen", "Manuell eingegebene Länge"], ["Normal (Anfang → Ende)", "Umgekehrt (Ende → Anfang)"]),
            ["pl-PL"] = (["Krokiew", "Murłata", "Płatew", "Słup", "Jętka", "Zastrzał", "Belka wiązarowa"], ["Automatycznie według typu elementu", "Długość w rzucie", "Przelicz według nachylenia", "Długość wprowadzona ręcznie"], ["Normalny (początek → koniec)", "Odwrócony (koniec → początek)"]),
            ["fr-FR"] = (["Chevron", "Sablière", "Panne", "Poteau", "Entrait retroussé", "Contrefiche", "Entrait"], ["Automatique selon le type d'élément", "Longueur en projection horizontale", "Recalculer selon la pente", "Longueur saisie manuellement"], ["Normal (début → fin)", "Inversé (fin → début)"]),
        };

        foreach (var cultureName in expected.Keys)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            var values = expected[cultureName];

            Assert.Equal(values.Types, Enum.GetValues<TimberElementType>()
                .Select(type => TimberElementTypeDisplayNameProvider.GetDisplayName(type, culture)));
            Assert.Equal(values.Modes, Enum.GetValues<LengthCalculationMode>()
                .Select(mode => LengthCalculationModeDisplayNameProvider.GetDisplayName(mode, culture)));
            Assert.Equal(values.Directions,
            new[]
            {
                SlopeDirectionDisplayNameProvider.GetDisplayName(false, culture),
                SlopeDirectionDisplayNameProvider.GetDisplayName(true, culture),
            });
        }
    }

    [Fact]
    public void ChangingUiCulture_DoesNotChangeTechnicalDataOrBindings()
    {
        var data = new TimberElementData
        {
            SchemaVersion = TimberElementDataSchema.CurrentVersion,
            ElementId = "K12",
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
        var measurement = new TimberElementMeasurement(data, 4000, 4800, 5000, 0.064);

        var snapshots = SupportedCultures
            .Select(item => InCulture(item.CultureName, () => new TechnicalSnapshot(
                JsonSerializer.Serialize(data, JsonOptions),
                TimberElementIdentityRules.CreateElementId(data.ElementType, 12),
                TimberElementIdentityPrefixes.GetPrefix(data.ElementType),
                TimberElementSignature.FromMeasurement(measurement),
                data.Material,
                string.Join("|", Enum.GetValues<TimberElementType>()
                    .Select(type => ElementLayerProfile.CreateDefault().GetStyle(type).LayerName)),
                TimberSlopeAnnotationRules.HasSameSourceHandle("1A2B", "1a2b"),
                string.Join("|", AcKrovyCommandNames.All))))
            .ToList();

        Assert.Single(snapshots.Distinct());
        Assert.Contains("\"ElementType\":\"Rafter\"", snapshots[0].MetadataJson);
        Assert.DoesNotContain("Krokva", snapshots[0].MetadataJson, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> SupportedCultureData()
    {
        var data = new TheoryData<string, string>();
        foreach (var item in SupportedCultures)
        {
            data.Add(item.CultureName, item.Rafter);
        }

        return data;
    }

    private static IReadOnlyDictionary<string, string> LoadResources(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LocalizationResources", fileName);
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);

        return document.Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static string[] ExtractPlaceholders(string value) => PlaceholderRegex()
        .Matches(value)
        .Select(match => match.Value)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string[] ExtractTechnicalNames(string value) => TechnicalNameRegex()
        .Matches(value)
        .Select(match => match.Value)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

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

    [GeneratedRegex(@"\{\d+(?:,[^}:]+)?(?:\:[^}]+)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\b(?:AK_[A-Z]+|KROV_POPIS)\b", RegexOptions.CultureInvariant)]
    private static partial Regex TechnicalNameRegex();

    private sealed record TechnicalSnapshot(
        string MetadataJson,
        string ElementId,
        string ElementPrefix,
        TimberElementSignature Signature,
        string Material,
        string LayerNames,
        bool SourceHandleMatches,
        string CommandNames);
}
