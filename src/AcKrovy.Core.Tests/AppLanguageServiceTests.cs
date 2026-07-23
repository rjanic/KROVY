using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

[Collection(LocalizationCultureCollection.CollectionName)]
public sealed class AppLanguageServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void SupportedLanguages_UseStableNeutralCodesAndNativeNames()
    {
        Assert.Equal(
            ["sk", "cs", "en", "de", "pl", "fr"],
            AppLanguageService.SupportedLanguages.Select(item => item.Code));
        Assert.Equal(
            ["Slovenčina", "Čeština", "English", "Deutsch", "Polski", "Français"],
            AppLanguageService.SupportedLanguages.Select(item => item.NativeName));
    }

    [Theory]
    [InlineData(null, "sk")]
    [InlineData("", "sk")]
    [InlineData("  ", "sk")]
    [InlineData("xx", "sk")]
    [InlineData("en-US", "en")]
    [InlineData("DE_de", "de")]
    [InlineData("fr", "fr")]
    public void NormalizeLanguageCode_UsesSafeNeutralFallback(string? value, string expected) =>
        Assert.Equal(expected, AppLanguageService.NormalizeLanguageCode(value));

    [Theory]
    [InlineData("sk")]
    [InlineData("cs")]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("pl")]
    [InlineData("fr")]
    public void SettingsSerialization_RoundTripsStableLanguageCode(string languageCode)
    {
        var json = AppLanguageSettingsSerializer.Serialize(new AppLanguageSettings
        {
            LanguageCode = languageCode,
        });
        var loaded = AppLanguageSettingsSerializer.Deserialize(json);

        Assert.Equal(languageCode, loaded.LanguageCode);
        Assert.Contains($"\"languageCode\":\"{languageCode}\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain(
            AppLanguageService.SupportedLanguages.Single(item => item.Code == languageCode).NativeName,
            json,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "sk")]
    [InlineData("", "sk")]
    [InlineData("{}", "sk")]
    [InlineData("{\"unrelatedOldSetting\":true}", "sk")]
    [InlineData("null", "sk")]
    [InlineData("{not-json", "sk")]
    [InlineData("{\"languageCode\":null}", "sk")]
    [InlineData("{\"languageCode\":\"\"}", "sk")]
    [InlineData("{\"languageCode\":\"xx\"}", "sk")]
    [InlineData("{\"languageCode\":\"en-US\"}", "en")]
    public void SettingsDeserialization_NormalizesMissingInvalidAndRegionalCodes(
        string? json,
        string expectedLanguageCode) =>
        Assert.Equal(expectedLanguageCode, AppLanguageSettingsSerializer.Deserialize(json).LanguageCode);

    [Fact]
    public void Apply_UnsupportedCodeFallsBackToSlovak()
    {
        WithRestoredCultures(() =>
        {
            Assert.Equal("sk", AppLanguageService.Apply("xx"));
            Assert.Equal("sk", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("Krokva", TimberElementTypeDisplayNameProvider.GetDisplayName(TimberElementType.Rafter));
        });
    }

    [Theory]
    [InlineData("sk", "Krokva")]
    [InlineData("cs", "Krokev")]
    [InlineData("en", "Rafter")]
    [InlineData("de", "Sparren")]
    [InlineData("pl", "Krokiew")]
    [InlineData("fr", "Chevron")]
    public void Apply_SetsUiCultureAndResolvesExpectedResourcesWithoutChangingCurrentCulture(
        string languageCode,
        string expectedRafter)
    {
        WithRestoredCultures(() =>
        {
            var technicalCulture = CultureInfo.GetCultureInfo("sk-SK");
            CultureInfo.CurrentCulture = technicalCulture;

            Assert.Equal(languageCode, AppLanguageService.Apply(languageCode));
            Assert.Equal(languageCode, AppLanguageService.CurrentLanguageCode);
            Assert.Equal(languageCode, CultureInfo.CurrentUICulture.Name);
            Assert.Equal(languageCode, CultureInfo.DefaultThreadCurrentUICulture?.Name);
            Assert.Equal(technicalCulture.Name, CultureInfo.CurrentCulture.Name);
            Assert.Equal(
                expectedRafter,
                TimberElementTypeDisplayNameProvider.GetDisplayName(TimberElementType.Rafter));
        });
    }

    [Fact]
    public void Apply_RefreshesSharedBindings()
    {
        WithRestoredCultures(() =>
        {
            var changedProperties = new List<string?>();
            PropertyChangedEventHandler handler = (_, args) => changedProperties.Add(args.PropertyName);
            UiStringBindingSource.Shared.PropertyChanged += handler;
            try
            {
                AppLanguageService.Apply("en");
                Assert.Contains("Item[]", changedProperties);
                Assert.Equal("Selected element data", UiStringBindingSource.Shared["EditWindow_Heading"]);
            }
            finally
            {
                UiStringBindingSource.Shared.PropertyChanged -= handler;
            }
        });
    }

    [Theory]
    [InlineData("sk", "ACAD KROVY – klasický panel")]
    [InlineData("cs", "ACAD KROVY – klasický panel")]
    [InlineData("en", "ACAD KROVY – classic toolbar")]
    [InlineData("de", "ACAD KROVY – klassische Symbolleiste")]
    [InlineData("pl", "ACAD KROVY – klasyczny pasek")]
    [InlineData("fr", "ACAD KROVY – barre d'outils classique")]
    public void ClassicToolbarTitle_UsesCurrentUiLanguage(string languageCode, string expectedTitle)
    {
        WithRestoredCultures(() =>
        {
            AppLanguageService.Apply(languageCode);
            string? synchronizedTitle = null;

            Assert.True(ClassicToolbarTitleSynchronizer.TrySynchronize(title => synchronizedTitle = title));
            Assert.Equal(expectedTitle, synchronizedTitle);
        });
    }

    [Fact]
    public void ClassicToolbarTitleSynchronization_RequiresAnExistingTitleTarget() =>
        Assert.False(ClassicToolbarTitleSynchronizer.TrySynchronize(null));

    [Fact]
    public void ClassicToolbarLocalizedContent_RefreshesTextAndPreservesTechnicalIdentity()
    {
        var slovak = CommandUiCatalog.GetLocalizedClassicToolbarContent(
            CultureInfo.GetCultureInfo("sk-SK"));
        var english = CommandUiCatalog.GetLocalizedClassicToolbarContent(
            CultureInfo.GetCultureInfo("en-US"));
        var german = CommandUiCatalog.GetLocalizedClassicToolbarContent(
            CultureInfo.GetCultureInfo("de-DE"));

        Assert.Equal(16, slovak.Count);
        Assert.Equal(slovak.Count, english.Count);
        Assert.Equal(slovak.Count, german.Count);
        Assert.Equal(
            slovak.Select(ItemIdentity),
            english.Select(ItemIdentity));
        Assert.Equal(
            slovak.Select(ItemIdentity),
            german.Select(ItemIdentity));

        var slovakPurlin = slovak.Single(item => item.CommandName == AcKrovyCommandNames.Purlin);
        var englishPurlin = english.Single(item => item.CommandName == AcKrovyCommandNames.Purlin);
        var germanPurlin = german.Single(item => item.CommandName == AcKrovyCommandNames.Purlin);

        Assert.Equal("Väznica", slovakPurlin.Title);
        Assert.Equal("Purlin", englishPurlin.Title);
        Assert.Equal("Pfette", germanPurlin.Title);
        Assert.NotEqual(slovakPurlin.Description, englishPurlin.Description);
        Assert.NotEqual(englishPurlin.Description, germanPurlin.Description);
    }

    [Fact]
    public void SettingsLanguageTab_ShowsOnlySaveAndCancelActions()
    {
        var actions = SettingsWindowActionRules.ForTab(SettingsWindowTabKind.Language);

        Assert.True(actions.ShowCancel);
        Assert.True(actions.ShowLanguageSave);
        Assert.False(actions.ShowApplyActions);
        Assert.False(actions.ShowRestoreDefaults);
    }

    [Theory]
    [InlineData(SettingsWindowTabKind.Layers)]
    [InlineData(SettingsWindowTabKind.Manufacturing)]
    public void SettingsElementTabs_PreserveOriginalApplyActions(SettingsWindowTabKind tab)
    {
        var actions = SettingsWindowActionRules.ForTab(tab);

        Assert.True(actions.ShowCancel);
        Assert.False(actions.ShowLanguageSave);
        Assert.True(actions.ShowApplyActions);
        Assert.True(actions.ShowRestoreDefaults);
    }

    [Fact]
    public void LanguageOnlySave_DoesNotApplyElementSettings()
    {
        Assert.False(SettingsWindowActionRules.AppliesElementSettings(SettingsSaveMode.LanguageOnly));
        Assert.True(SettingsWindowActionRules.AppliesElementSettings(SettingsSaveMode.NewElementsOnly));
        Assert.True(SettingsWindowActionRules.AppliesElementSettings(SettingsSaveMode.SelectedElements));
        Assert.True(SettingsWindowActionRules.AppliesElementSettings(SettingsSaveMode.AllElements));
    }

    [Theory]
    [InlineData("sk", "Uložiť")]
    [InlineData("cs", "Uložit")]
    [InlineData("en", "Save")]
    [InlineData("de", "Speichern")]
    [InlineData("pl", "Zapisz")]
    [InlineData("fr", "Enregistrer")]
    public void SettingsSaveLabel_IsLocalizedForEverySupportedLanguage(string languageCode, string expectedLabel)
    {
        WithRestoredCultures(() =>
        {
            AppLanguageService.Apply(languageCode);
            Assert.Equal(expectedLabel, UiStrings.SettingsWindowSave);
        });
    }

    [Fact]
    public void RepeatedSwitching_PreservesTechnicalDataAndUniqueRibbonIdentity()
    {
        WithRestoredCultures(() =>
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
            var languageSequence = new[] { "sk", "en", "de", "sk", "fr", "pl", "cs", "sk" };

            var snapshots = languageSequence.Select(languageCode =>
            {
                AppLanguageService.Apply(languageCode);
                return new TechnicalSnapshot(
                    JsonSerializer.Serialize(data, JsonOptions),
                    TimberElementIdentityRules.CreateElementId(data.ElementType, 12),
                    TimberElementIdentityPrefixes.GetPrefix(data.ElementType),
                    TimberElementSignature.FromMeasurement(measurement),
                    data.Material,
                    data.Note,
                    data.RoofPlaneId,
                    string.Join("|", Enum.GetValues<TimberElementType>()
                        .Select(type => ElementLayerProfile.CreateDefault().GetStyle(type).LayerName)),
                    TimberSlopeAnnotationRules.HasSameSourceHandle("1A2B", "1a2b"),
                    CommandUiCatalog.RibbonTabId,
                    CommandUiCatalog.RibbonCommands.Select(item => item.RibbonControlId).ToArray(),
                    CommandUiCatalog.RibbonCommands.Select(item => item.CommandName).ToArray(),
                    CommandUiCatalog.RibbonCommands.Select(item => item.IconKey).ToArray(),
                    CommandUiCatalog.ClassicToolbarCommands
                        .Select(item => CommandMacroBuilder.Build(item.CommandName)).ToArray());
            }).ToList();

            Assert.Single(snapshots.Distinct(TechnicalSnapshotComparer.Instance));
            Assert.All(snapshots, snapshot => Assert.Equal(
                snapshot.RibbonControlIds.Length,
                snapshot.RibbonControlIds.Distinct(StringComparer.OrdinalIgnoreCase).Count()));
            Assert.Equal("DECORAIR_ACAD_KROVY_TAB", snapshots[0].RibbonTabId);
            Assert.Contains("\"ElementType\":\"Rafter\"", snapshots[0].MetadataJson);
        });
    }

    private static void WithRestoredCultures(Action action)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var previousLanguageCode = AppLanguageService.CurrentLanguageCode;
        var previousBindingCulture = UiStringBindingSource.Shared.Culture;
        try
        {
            action();
        }
        finally
        {
            AppLanguageService.Apply(previousLanguageCode);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUiCulture;
            UiStringBindingSource.Shared.Culture = previousBindingCulture;
        }
    }

    private static string ItemIdentity(LocalizedCommandUiContent item) =>
        $"{item.CommandName}|{item.ControlId}|{item.IconKey}";

    private sealed record TechnicalSnapshot(
        string MetadataJson,
        string ElementId,
        string ElementPrefix,
        TimberElementSignature Signature,
        string Material,
        string Note,
        string RoofPlaneId,
        string LayerNames,
        bool SourceHandleMatches,
        string RibbonTabId,
        string[] RibbonControlIds,
        string[] CommandNames,
        string[] IconIds,
        string[] ToolbarMacros);

    private sealed class TechnicalSnapshotComparer : IEqualityComparer<TechnicalSnapshot>
    {
        public static TechnicalSnapshotComparer Instance { get; } = new();

        public bool Equals(TechnicalSnapshot? x, TechnicalSnapshot? y) =>
            x is not null &&
            y is not null &&
            x.MetadataJson == y.MetadataJson &&
            x.ElementId == y.ElementId &&
            x.ElementPrefix == y.ElementPrefix &&
            x.Signature == y.Signature &&
            x.Material == y.Material &&
            x.Note == y.Note &&
            x.RoofPlaneId == y.RoofPlaneId &&
            x.LayerNames == y.LayerNames &&
            x.SourceHandleMatches == y.SourceHandleMatches &&
            x.RibbonTabId == y.RibbonTabId &&
            x.RibbonControlIds.SequenceEqual(y.RibbonControlIds) &&
            x.CommandNames.SequenceEqual(y.CommandNames) &&
            x.IconIds.SequenceEqual(y.IconIds) &&
            x.ToolbarMacros.SequenceEqual(y.ToolbarMacros);

        public int GetHashCode(TechnicalSnapshot obj) => 0;
    }
}
