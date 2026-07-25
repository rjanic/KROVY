using System.Globalization;
using System.Xml.Linq;
using AcKrovy.Core.Models;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

[Collection(LocalizationCultureCollection.CollectionName)]
public sealed class SettingsRuntimeLocalizationTests
{
    [Fact]
    public void GermanCatalog_LocalizesEveryDynamicSettingsCollection()
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");

        var annotation = SettingsLocalizedOptionCatalog.AnnotationModes(culture);
        var styles = SettingsLocalizedOptionCatalog.ItemNumberLeaderStyles(culture);
        var applyModes = SettingsLocalizedOptionCatalog.ApplyModes(culture);

        Assert.Equal(
            "Vollständige Beschriftung",
            annotation.Single(item => item.Value == TimberAnnotationMode.FullLabel).DisplayName);
        Assert.Equal(
            "Ohne Rahmen",
            styles.Single(item => item.Value == ItemNumberLeaderStyle.Plain).DisplayName);
        Assert.Equal(
            UiStrings.GetString("SettingsWindow_SaveNewElementsOnly", culture),
            applyModes.Single(item => item.Value == SettingsSaveMode.NewElementsOnly).DisplayName);
    }

    [Theory]
    [InlineData("sk-SK")]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("pl-PL")]
    [InlineData("fr-FR")]
    public void InitialSelectionSet_HasNonemptyStableSelectionsInEveryLanguage(
        string cultureName)
    {
        var selection = SettingsLocalizedSelectionSet.Create(
            CultureInfo.GetCultureInfo(cultureName),
            TimberAnnotationMode.FullLabel,
            ItemNumberLeaderStyle.Plain,
            SettingsSaveMode.NewElementsOnly);

        Assert.Contains(
            selection.AnnotationModes,
            option => option.Value == selection.SelectedAnnotationMode &&
                !string.IsNullOrWhiteSpace(option.DisplayName));
        Assert.Contains(
            selection.ItemNumberLeaderStyles,
            option => option.Value == selection.SelectedItemNumberLeaderStyle &&
                !string.IsNullOrWhiteSpace(option.DisplayName));
        Assert.Contains(
            selection.ApplyModes,
            option => option.Value == selection.SelectedApplyMode &&
                !string.IsNullOrWhiteSpace(option.DisplayName));
        Assert.Equal(SettingsSaveMode.NewElementsOnly, selection.SelectedApplyMode);
    }

    [Fact]
    public void RecreatedSelectionSet_PreservesEnumsWithoutReusingLocalizedObjects()
    {
        var slovak = SettingsLocalizedSelectionSet.Create(
            CultureInfo.GetCultureInfo("sk-SK"),
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain,
            SettingsSaveMode.SelectedElements);
        var german = SettingsLocalizedSelectionSet.Create(
            CultureInfo.GetCultureInfo("de-DE"),
            slovak.SelectedAnnotationMode,
            slovak.SelectedItemNumberLeaderStyle,
            slovak.SelectedApplyMode);
        var czech = SettingsLocalizedSelectionSet.Create(
            CultureInfo.GetCultureInfo("cs-CZ"),
            german.SelectedAnnotationMode,
            german.SelectedItemNumberLeaderStyle,
            german.SelectedApplyMode);

        Assert.Equal(TimberAnnotationMode.ItemNumberLeader, german.SelectedAnnotationMode);
        Assert.Equal(TimberAnnotationMode.ItemNumberLeader, czech.SelectedAnnotationMode);
        Assert.Equal(ItemNumberLeaderStyle.Plain, german.SelectedItemNumberLeaderStyle);
        Assert.Equal(SettingsSaveMode.SelectedElements, czech.SelectedApplyMode);
        Assert.NotSame(slovak.AnnotationModes[0], german.AnnotationModes[0]);
        Assert.NotSame(german.AnnotationModes[0], czech.AnnotationModes[0]);
    }

    [Fact]
    public void InvalidPersistedSelections_UseSafeWorkingFallbacks()
    {
        var selection = SettingsLocalizedSelectionSet.Create(
            CultureInfo.GetCultureInfo("en-US"),
            (TimberAnnotationMode)999,
            (ItemNumberLeaderStyle)999,
            (SettingsSaveMode)999);

        Assert.Equal(TimberAnnotationMode.FullLabel, selection.SelectedAnnotationMode);
        Assert.Equal(ItemNumberLeaderStyle.Plain, selection.SelectedItemNumberLeaderStyle);
        Assert.Equal(SettingsSaveMode.NewElementsOnly, selection.SelectedApplyMode);
    }

    [Fact]
    public void LocalizedSelectionRebuild_DoesNotCreateAnApplyChange()
    {
        var tracker = new SettingsApplyChangeTracker();
        tracker.AcceptProfile("stable-profile");

        _ = SettingsLocalizedSelectionSet.Create(
            CultureInfo.GetCultureInfo("de-DE"),
            TimberAnnotationMode.DimensionsLeader,
            ItemNumberLeaderStyle.Plain,
            SettingsSaveMode.AllElements);

        Assert.False(tracker.HasProfileChanged("stable-profile"));
    }

    [Theory]
    [InlineData("sk-SK", "de-DE")]
    [InlineData("de-DE", "sk-SK")]
    [InlineData("sk-SK", "cs-CZ")]
    [InlineData("cs-CZ", "en-US")]
    [InlineData("en-US", "fr-FR")]
    [InlineData("fr-FR", "pl-PL")]
    public void RebuiltCatalog_ChangesOnlyDisplayTextAndPreservesStableValues(
        string fromCultureName,
        string toCultureName)
    {
        var from = CultureInfo.GetCultureInfo(fromCultureName);
        var to = CultureInfo.GetCultureInfo(toCultureName);

        AssertStableValuesAndChangedText(
            SettingsLocalizedOptionCatalog.AnnotationModes(from),
            SettingsLocalizedOptionCatalog.AnnotationModes(to));
        AssertStableValuesAndChangedText(
            SettingsLocalizedOptionCatalog.ItemNumberLeaderStyles(from),
            SettingsLocalizedOptionCatalog.ItemNumberLeaderStyles(to));
        AssertStableValuesAndChangedText(
            SettingsLocalizedOptionCatalog.ApplyModes(from),
            SettingsLocalizedOptionCatalog.ApplyModes(to));
    }

    [Theory]
    [InlineData("sk-SK")]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("pl-PL")]
    [InlineData("fr-FR")]
    public void Banner_ResolvesCurrentLanguageFromStableResourceKey(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var banner = new StatusBannerState();

        banner.Show("SettingsWindow_SettingsApplied", StatusBannerSeverity.Success);

        Assert.Equal(UiStrings.GetString("SettingsWindow_SettingsApplied", culture), banner.Resolve(culture));
    }

    [Theory]
    [InlineData("sk-SK")]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("pl-PL")]
    [InlineData("fr-FR")]
    public void RepeatedApplyBanners_ResolveInEverySupportedLanguage(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var banner = new StatusBannerState();
        var keys = new[]
        {
            "SettingsWindow_SelectedElementsApplied",
            "SettingsWindow_SelectedElementsAlreadyMatch",
            "SettingsWindow_NoSmartElementsSelected",
            "SettingsWindow_SelectionCancelled",
            "SettingsWindow_AllElementsApplied",
            "SettingsWindow_AllElementsAlreadyMatch",
        };

        foreach (var key in keys)
        {
            banner.Show(key, StatusBannerSeverity.Information);

            Assert.Equal(UiStrings.GetString(key, culture), banner.Resolve(culture));
            Assert.False(string.IsNullOrWhiteSpace(banner.Resolve(culture)));
        }
    }

    [Fact]
    public void NewerBannerVersion_CannotBeHiddenByOlderTimer()
    {
        var banner = new StatusBannerState();
        var firstVersion = banner.Show(
            "SettingsWindow_SettingsApplied",
            StatusBannerSeverity.Success);
        var secondVersion = banner.Show(
            "SettingsWindow_NoChanges",
            StatusBannerSeverity.Information);

        Assert.False(banner.TryHide(firstVersion));
        Assert.True(banner.IsVisible);
        Assert.True(banner.TryHide(secondVersion));
        Assert.False(banner.IsVisible);
    }

    [Fact]
    public void Clear_InvalidatesPendingTimerAndDropsStatusPayload()
    {
        var banner = new StatusBannerState();
        var version = banner.Show(
            "SettingsWindow_ExistingLayerInfoFormat",
            StatusBannerSeverity.Warning,
            "AK_KROVY");

        banner.Clear();

        Assert.False(banner.TryHide(version));
        Assert.False(banner.IsVisible);
        Assert.Null(banner.ResourceKey);
        Assert.Empty(banner.Arguments);
    }

    [Fact]
    public void SettingsBanner_IsOverlayAndCannotAffectLayoutOrHitTesting()
    {
        var xaml = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "UI",
            "LayerSettingsWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var banner = xaml
            .Descendants(presentation + "Border")
            .Single(element => (string?)element.Attribute(x + "Name") == "StatusBanner");

        Assert.Equal("7", (string?)banner.Attribute("Grid.RowSpan"));
        Assert.Equal("100", (string?)banner.Attribute("Panel.ZIndex"));
        Assert.Equal("False", (string?)banner.Attribute("IsHitTestVisible"));
        Assert.Equal("Collapsed", (string?)banner.Attribute("Visibility"));
        Assert.Equal("Stretch", (string?)banner.Attribute("HorizontalAlignment"));
        Assert.Equal("Center", (string?)banner.Attribute("VerticalAlignment"));
    }

    [Fact]
    public void SettingsComboBoxes_BindSelectedValueToStableProperties()
    {
        var xaml = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "UI",
            "LayerSettingsWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var comboBoxes = xaml.Descendants(presentation + "ComboBox").ToList();

        Assert.Contains(comboBoxes, combo =>
            (string?)combo.Attribute("SelectedValuePath") == "Mode" &&
            ((string?)combo.Attribute("SelectedValue"))?.Contains("SelectedAnnotationMode") == true);
        Assert.Contains(comboBoxes, combo =>
            (string?)combo.Attribute("SelectedValuePath") == "Style" &&
            ((string?)combo.Attribute("SelectedValue"))?.Contains("SelectedItemNumberLeaderStyle") == true);
        Assert.Contains(comboBoxes, combo =>
            (string?)combo.Attribute("SelectedValuePath") == "Mode" &&
            ((string?)combo.Attribute("SelectedValue"))?.Contains("SelectedApplyMode") == true);
    }

    private static void AssertStableValuesAndChangedText<T>(
        IReadOnlyList<LocalizedOption<T>> from,
        IReadOnlyList<LocalizedOption<T>> to)
    {
        Assert.Equal(from.Select(item => item.Value), to.Select(item => item.Value));
        Assert.Contains(
            from.Zip(to, (left, right) => left.DisplayName != right.DisplayName),
            changed => changed);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate AcKrovy.sln.");
    }
}
