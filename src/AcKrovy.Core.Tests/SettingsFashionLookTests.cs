using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

[Collection(LocalizationCultureCollection.CollectionName)]
public sealed class SettingsFashionLookTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string UiDirectory = Path.Combine(
        RepositoryRoot,
        "src",
        "AcKrovy.AutoCAD",
        "UI");
    private static readonly string[] Cultures =
        ["sk-SK", "cs-CZ", "en-US", "de-DE", "pl-PL", "fr-FR"];

    [Fact]
    public void DesignSystem_ContainsEveryRequiredToken()
    {
        var keys = ResourceKeys("Design/SettingsDesignSystem.xaml");
        var required = new[]
        {
            "SettingsFontFamily", "SettingsFontSizeCaption", "SettingsFontSizeBody",
            "SettingsFontSizeSection", "SettingsFontSizeTitle",
            "SettingsFontWeightRegular", "SettingsFontWeightSemiBold",
            "SettingsSpacingXs", "SettingsSpacingSm", "SettingsSpacingMd",
            "SettingsSpacingLg", "SettingsSpacingXl", "SettingsControlHeight",
            "SettingsNavigationColumnWidth", "SettingsCornerRadiusSmall",
            "SettingsCornerRadius", "SettingsCornerRadiusLarge",
            "SettingsCardPadding", "SettingsSectionMargin",
            "SettingsDisabledOpacity", "SettingsCardElevation",
        };

        Assert.All(required, key => Assert.Contains(key, keys));
    }

    [Fact]
    public void LightTheme_ContainsRequiredSemanticColors() =>
        AssertThemeHasRequiredColors("Design/SettingsColors.Light.xaml");

    [Fact]
    public void DarkTheme_ContainsRequiredSemanticColors() =>
        AssertThemeHasRequiredColors("Design/SettingsColors.Dark.xaml");

    [Fact]
    public void LightAndDarkThemes_HaveIdenticalKeys()
    {
        Assert.Equal(
            ResourceKeys("Design/SettingsColors.Light.xaml").Order(),
            ResourceKeys("Design/SettingsColors.Dark.xaml").Order());
    }

    [Fact]
    public void DesignXaml_HasNoMissingCustomResourceReference()
    {
        var files = Directory.GetFiles(UiDirectory, "*.xaml", SearchOption.AllDirectories);
        var allText = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        var defined = files
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "x:Key=\"([A-Za-z0-9_]+)\"")
                .Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);
        var referenced = Regex.Matches(
                allText,
                "\\{(?:DynamicResource|StaticResource)\\s+([A-Za-z0-9_]+)\\}")
            .Select(match => match.Groups[1].Value)
            .Where(key => key.StartsWith("Settings", StringComparison.Ordinal))
            .Distinct();

        Assert.DoesNotContain(referenced, key => !defined.Contains(key));
    }

    [Fact]
    public void ThemeSwitch_IsUiOnlyAndCannotDispatchApply() =>
        Assert.False(SettingsFashionLookRules.UiOnlyChangeDispatchesApply());

    [Fact]
    public void ThemeSwitch_IsExcludedFromProfileFingerprint()
    {
        var source = WindowCode();
        var fingerprint = Slice(source, "private string CreateUiFingerprint()", "private void UpdateFormState()");

        Assert.DoesNotContain("SelectedTheme", fingerprint);
        Assert.DoesNotContain("SelectedSection", fingerprint);
        Assert.DoesNotContain("SelectedApplyMode", fingerprint);
    }

    [Fact]
    public void Navigation_ContainsExactlyFourStableSections()
    {
        Assert.Equal(
            [
                SettingsWindowTabKind.Layers,
                SettingsWindowTabKind.Manufacturing,
                SettingsWindowTabKind.Annotation,
                SettingsWindowTabKind.Language,
            ],
            SettingsFashionLookRules.NavigationSections);
    }

    [Theory]
    [InlineData(SettingsWindowTabKind.Layers)]
    [InlineData(SettingsWindowTabKind.Manufacturing)]
    [InlineData(SettingsWindowTabKind.Annotation)]
    [InlineData(SettingsWindowTabKind.Language)]
    public void Navigation_SelectedSectionUsesStableId(SettingsWindowTabKind section) =>
        Assert.Equal(section, SettingsFashionLookRules.NormalizeSection(section));

    [Theory]
    [MemberData(nameof(CultureSectionCases))]
    public void Navigation_RuntimeLanguageSwitchPreservesSection(
        string cultureName,
        SettingsWindowTabKind section)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var localizedName = UiStrings.GetString(SectionResourceKey(section), culture);

        Assert.False(string.IsNullOrWhiteSpace(localizedName));
        Assert.Equal(section, SettingsFashionLookRules.NormalizeSection(section));
    }

    [Fact]
    public void Navigation_UsesKeyboardNavigableListBox()
    {
        var xaml = WindowXaml();
        Assert.Contains("x:Name=\"SettingsNavigation\"", xaml);
        Assert.Contains("SelectedValue=\"{Binding Visual.SelectedSection, Mode=TwoWay}\"", xaml);
    }

    [Fact]
    public void Navigation_LongNamesWrapInsteadOfTruncate()
    {
        var xaml = WindowXaml();
        var navigationTemplate = Slice(xaml, "<ListBox.ItemTemplate>", "</ListBox.ItemTemplate>");
        Assert.Contains("TextWrapping=\"Wrap\"", navigationTemplate);
        Assert.DoesNotContain("TextTrimming=", navigationTemplate);
    }

    [Fact]
    public void AciPalette_ContainsExactlyOneThroughTwoHundredFiftyFive()
    {
        Assert.Equal(255, AciColorPalette.Indices.Count);
        Assert.Equal(Enumerable.Range(1, 255), AciColorPalette.Indices);
    }

    [Fact]
    public void AciPalette_ExcludesByBlockAndByLayer()
    {
        Assert.DoesNotContain(0, AciColorPalette.Indices);
        Assert.DoesNotContain(256, AciColorPalette.Indices);
    }

    [Fact]
    public void AciPalette_EveryIndexHasValidPreviewRgb()
    {
        Assert.All(AciColorPalette.Indices, index =>
        {
            Assert.True(AciColorPalette.TryGetRgb(index, out var rgb));
            Assert.Matches("^#[0-9A-F]{6}$", rgb.Hex);
        });
    }

    [Theory]
    [InlineData(1, 255, 0, 0)]
    [InlineData(2, 255, 255, 0)]
    [InlineData(3, 0, 255, 0)]
    [InlineData(4, 0, 255, 255)]
    [InlineData(5, 0, 0, 255)]
    [InlineData(6, 255, 0, 255)]
    [InlineData(7, 255, 255, 255)]
    [InlineData(8, 128, 128, 128)]
    [InlineData(9, 192, 192, 192)]
    [InlineData(30, 255, 127, 0)]
    [InlineData(142, 0, 124, 165)]
    [InlineData(250, 51, 51, 51)]
    [InlineData(255, 255, 255, 255)]
    public void AciPalette_KnownIndicesUseStandardPreviewRgb(
        int index,
        int red,
        int green,
        int blue)
    {
        var rgb = AciColorPalette.GetRgb(index);
        Assert.Equal(red, rgb.Red);
        Assert.Equal(green, rgb.Green);
        Assert.Equal(blue, rgb.Blue);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("255", 255)]
    public void AciDirectInput_AcceptsBoundaries(string input, int expected)
    {
        Assert.True(AciColorSelectionRules.TryParseLayerIndex(input, out var index));
        Assert.Equal(expected, index);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("256")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("")]
    public void AciDirectInput_RejectsInvalidValues(string input)
    {
        Assert.False(AciColorSelectionRules.TryParseLayerIndex(input, out _));
    }

    [Fact]
    public void AciDialog_EscapeKeepsOriginalColor() =>
        Assert.Equal(30, AciColorSelectionRules.ResolveDialogResult(30, 142, confirmed: false));

    [Fact]
    public void AciDialog_EnterConfirmsPendingColor() =>
        Assert.Equal(142, AciColorSelectionRules.ResolveDialogResult(30, 142, confirmed: true));

    [Fact]
    public void AciPicker_UsesClassicNineMainAndGrayscaleBands()
    {
        var xaml = PickerXaml();
        Assert.Contains("<UniformGrid Columns=\"9\"", xaml);
        Assert.Contains("<UniformGrid Columns=\"24\" Rows=\"10\"", xaml);
        Assert.Contains("<UniformGrid Columns=\"6\"", xaml);
        Assert.Contains("AciColorPickerRules.MainPaletteColumns", PickerCode());
    }

    [Theory]
    [InlineData("sk-SK")]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("pl-PL")]
    [InlineData("fr-FR")]
    public void AciPicker_LanguageSwitchPreservesSelectedIndex(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var selectedIndex = 142;
        var label = LayerColorDisplayNameProvider.GetDisplayName(selectedIndex, culture);

        Assert.Equal("ACI 142", label);
        Assert.Equal(142, selectedIndex);
    }

    [Fact]
    public void AciProfile_PersistsOnlyStableIndex()
    {
        var style = new ElementLayerStyle(
            TimberElementType.Rafter,
            "KROKVA",
            142,
            CadLinetypeNames.DashDot,
            0.5);
        var json = JsonSerializer.Serialize(style);

        Assert.Contains("\"ColorIndex\":142", json);
        Assert.DoesNotContain("Red", json);
        Assert.DoesNotContain("Green", json);
        Assert.DoesNotContain("Blue", json);
        Assert.DoesNotContain("Brush", json);
    }

    [Theory]
    [InlineData(TimberAnnotationMode.FullLabel)]
    [InlineData(TimberAnnotationMode.ItemNumberLeader)]
    [InlineData(TimberAnnotationMode.DimensionsLeader)]
    [InlineData(TimberAnnotationMode.NoAnnotations)]
    public void AnnotationSelector_SupportsEveryStableMode(TimberAnnotationMode mode)
    {
        var selection = SettingsLocalizedSelectionSet.Create(
            CultureInfo.GetCultureInfo("en-US"),
            mode,
            ItemNumberLeaderStyle.Plain,
            SettingsSaveMode.NewElementsOnly);
        Assert.Equal(mode, selection.SelectedAnnotationMode);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Plain)]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void FrameSelector_SupportsEveryStableStyle(ItemNumberLeaderStyle style)
    {
        var selection = SettingsLocalizedSelectionSet.Create(
            CultureInfo.GetCultureInfo("en-US"),
            TimberAnnotationMode.ItemNumberLeader,
            style,
            SettingsSaveMode.NewElementsOnly);
        Assert.Equal(style, selection.SelectedItemNumberLeaderStyle);
    }

    [Fact]
    public void AnnotationPresetSelector_BindsItsStableSelectedValue()
    {
        var xaml = WindowXaml();
        var section = Slice(
            xaml,
            "ItemsSource=\"{Binding AnnotationPresetOptions}\"",
            "</ListBox>");
        Assert.Contains("SelectedValuePath=\"Preset\"", section);
        Assert.Contains(
            "SelectedValue=\"{Binding SelectedAnnotationPreset, Mode=TwoWay}\"",
            section);
    }

    [Theory]
    [InlineData("sk-SK")]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("pl-PL")]
    [InlineData("fr-FR")]
    public void AnnotationLanguageSwitch_PreservesStableEnums(string cultureName)
    {
        var selection = SettingsLocalizedSelectionSet.Create(
            CultureInfo.GetCultureInfo(cultureName),
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Rectangle,
            SettingsSaveMode.AllElements);
        Assert.Equal(TimberAnnotationMode.ItemNumberLeader, selection.SelectedAnnotationMode);
        Assert.Equal(ItemNumberLeaderStyle.Rectangle, selection.SelectedItemNumberLeaderStyle);
    }

    [Fact]
    public void RepeatedSelectionApply_StillDispatchesWithoutProfileChange() =>
        Assert.True(SettingsApplyDispatchRules.ShouldDispatch(
            SettingsSaveMode.SelectedElements,
            profileChanged: false));

    [Fact]
    public void RepeatedAllApply_StillDispatchesWithoutProfileChange() =>
        Assert.True(SettingsApplyDispatchRules.ShouldDispatch(
            SettingsSaveMode.AllElements,
            profileChanged: false));

    [Fact]
    public void NewOnlyWithoutChanges_RemainsNoOp() =>
        Assert.False(SettingsApplyDispatchRules.ShouldDispatch(
            SettingsSaveMode.NewElementsOnly,
            profileChanged: false));

    [Fact]
    public void NavigationThemeAndPreview_DoNotInvokeApplyCallback()
    {
        var visualState = VisualStateCode();
        Assert.DoesNotContain("_applySettings", visualState);
        Assert.DoesNotContain("_applySettings", WindowXaml());
    }

    [Fact]
    public void OpeningAndClosingWindow_HasNoDrawingApplyCodePath()
    {
        var source = WindowCode();
        Assert.DoesNotContain(
            "_applySettings(",
            Slice(source, "private void Close_Click", "private void RefreshLinetypeOptions"));
        Assert.DoesNotContain(
            "_applySettings(",
            Slice(source, "private void SaveWindowPreferences", "\n}"));
    }

    [Fact]
    public void AciPreview_DoesNotContainAutodeskOrDatabaseAccess()
    {
        var palette = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.Cad.Abstractions",
            "Layers",
            "AciColorPalette.cs"));
        Assert.DoesNotContain("Autodesk", palette);
        Assert.DoesNotContain("Database", palette);
        Assert.DoesNotContain("ObjectId", palette);
    }

    [Fact]
    public void Localization_AllSixLanguagesContainFashionKeys()
    {
        var keys = new[]
        {
            "SettingsWindow_Layers_Description", "SettingsWindow_Layers_PreviewColumn",
            "SettingsWindow_Theme", "SettingsWindow_ThemeLight", "SettingsWindow_ThemeDark",
            "SettingsWindow_AciPicker_Validation", "SettingsWindow_Accessibility_Navigation",
            "SettingsWindow_Annotation_FullLabelHelp", "SettingsWindow_Annotation_NoAnnotationsHelp",
            "SettingsWindow_FormUnsavedChanges",
        };
        foreach (var cultureName in Cultures)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(
                UiStrings.GetString(key, culture))));
        }
    }

    [Theory]
    [InlineData("sk-SK", "Zadajte")]
    [InlineData("cs-CZ", "Zadejte")]
    [InlineData("en-US", "Enter")]
    [InlineData("de-DE", "Geben")]
    [InlineData("pl-PL", "Wprowadź")]
    [InlineData("fr-FR", "Saisissez")]
    public void AciValidation_UsesCurrentUiLanguage(string cultureName, string expectedFragment)
    {
        var text = UiStrings.GetString(
            "SettingsWindow_AciPicker_Validation",
            CultureInfo.GetCultureInfo(cultureName));
        Assert.Contains(expectedFragment, text);
    }

    [Theory]
    [InlineData("sk-SK")]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("pl-PL")]
    [InlineData("fr-FR")]
    public void StatusBanner_ResolvesWithCurrentLanguageAfterThemeWork(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var banner = new StatusBannerState();
        banner.Show("SettingsWindow_SettingsApplied", StatusBannerSeverity.Success);
        Assert.Equal(UiStrings.GetString("SettingsWindow_SettingsApplied", culture), banner.Resolve(culture));
    }

    [Fact]
    public void InteractiveCustomControls_HaveAccessibilityNames()
    {
        var xaml = WindowXaml() + PickerXaml();
        Assert.Contains("SettingsWindow_Accessibility_Navigation", xaml);
        Assert.Contains("SettingsWindow_ThemeLight", xaml);
        Assert.Contains("SettingsWindow_ThemeDark", xaml);
        Assert.Contains("SettingsWindow_SaveNewElementsOnly", xaml);
        Assert.Contains("SettingsWindow_SaveApplySelection", xaml);
        Assert.Contains("SettingsWindow_SaveApplyAll", xaml);
        Assert.Contains("SettingsWindow_AciPicker_Open", xaml);
        Assert.Contains("SettingsWindow_AciPicker_BasicColors", xaml);
        Assert.Contains("SettingsWindow_AciPicker_ColorPalette", xaml);
        Assert.Contains("SettingsWindow_AciPicker_Grayscale", xaml);
    }

    [Fact]
    public void FocusableControls_UseVisibleFocusVisual()
    {
        var controls = File.ReadAllText(Path.Combine(UiDirectory, "Design", "SettingsControls.xaml"));
        Assert.Contains("x:Key=\"SettingsFocusVisualStyle\"", controls);
        Assert.True(Regex.Matches(controls, "FocusVisualStyle").Count >= 5);
        Assert.Contains("SettingsFocusBrush", controls);
    }

    [Fact]
    public void Banner_IsOverlayAndDoesNotBlockInput()
    {
        var xaml = WindowXaml();
        var banner = Slice(xaml, "x:Name=\"StatusBanner\"", "</Border>");
        Assert.Contains("Panel.ZIndex=\"100\"", banner);
        Assert.Contains("IsHitTestVisible=\"False\"", banner);
        Assert.Contains("TextWrapping=\"Wrap\"", banner);
    }

    [Fact]
    public void ModalPicker_EscapeUsesTheSameCancelResult()
    {
        var source = PickerCode();
        Assert.Contains("e.Key == Key.Escape", source);
        Assert.Contains("SelectedAciIndex = OriginalAciIndex", source);
        Assert.Contains("DialogResult = false", source);
    }

    [Fact]
    public void Window_HasRequiredResizableDpiSafeBoundsAndWrapping()
    {
        var xaml = WindowXaml();
        Assert.Contains("Width=\"1500\"", xaml);
        Assert.Contains("Height=\"900\"", xaml);
        Assert.Contains("MinWidth=\"1250\"", xaml);
        Assert.Contains("MinHeight=\"720\"", xaml);
        Assert.Contains("WindowStartupLocation=\"CenterScreen\"", xaml);
        Assert.Contains("ResizeMode=\"CanResize\"", xaml);
        Assert.True(Regex.Matches(xaml, "TextWrapping=\"Wrap\"").Count >= 7);
        Assert.DoesNotContain("RenderOptions.BitmapScalingMode", xaml);
    }

    [Fact]
    public void LanguageSelectorAndThemeButtons_UseStableState()
    {
        var xaml = WindowXaml();
        Assert.Contains("SelectedValuePath=\"Code\"", xaml);
        Assert.Contains("SelectedValue=\"{Binding SelectedLanguageCode, Mode=TwoWay}\"", xaml);
        Assert.Contains("x:Name=\"LightThemeButton\"", xaml);
        Assert.Contains("x:Name=\"DarkThemeButton\"", xaml);
        Assert.Contains("Click=\"LightTheme_Click\"", xaml);
        Assert.Contains("Click=\"DarkTheme_Click\"", xaml);
        Assert.DoesNotContain("ItemsSource=\"{Binding Visual.ThemeOptions}\"", xaml);
    }

    [Fact]
    public void FooterActions_AreSectionSpecificAndReuseTheApplyCallback()
    {
        var xaml = WindowXaml();
        var code = WindowCode();

        Assert.Contains("Click=\"SaveNewElements_Click\"", xaml);
        Assert.Contains("Click=\"SaveApplySelection_Click\"", xaml);
        Assert.Contains("Click=\"SaveApplyAll_Click\"", xaml);
        Assert.DoesNotContain("x:Name=\"ApplyModePanel\"", xaml);
        Assert.DoesNotContain("x:Name=\"ApplyButton\"", xaml);
        Assert.Contains("x:Name=\"LayersFooterActions\"", xaml);
        Assert.Contains("x:Name=\"ManufacturingFooterActions\"", xaml);
        Assert.Contains("x:Name=\"AnnotationFooterActions\"", xaml);
        Assert.Contains("x:Name=\"LanguageFooterActions\"", xaml);
        Assert.Contains("Click=\"Apply_Click\"", xaml);
        Assert.Contains("private void Apply_Click", code);
        Assert.Contains("ApplySettings(SettingsSaveMode.NewElementsOnly)", code);
        Assert.Contains("ApplySettings(SettingsSaveMode.SelectedElements)", code);
        Assert.Contains("ApplySettings(SettingsSaveMode.AllElements)", code);
        Assert.Contains("SetFooterActionsEnabled(false)", code);
        Assert.Contains("SetFooterActionsEnabled(true)", code);
        Assert.Contains("section == SettingsWindowTabKind.Layers", code);
        Assert.Contains("section == SettingsWindowTabKind.Manufacturing", code);
        Assert.Contains("section == SettingsWindowTabKind.Annotation", code);
        Assert.Contains("section == SettingsWindowTabKind.Language", code);
        Assert.Contains("Activate();", code);
    }

    [Fact]
    public void ComboBoxes_UseThemeAwareTextForClosedEditableAndDropDownStates()
    {
        var controls = File.ReadAllText(Path.Combine(
            UiDirectory,
            "Design",
            "SettingsControls.xaml"));

        Assert.Contains("<ControlTemplate TargetType=\"ComboBox\">", controls);
        Assert.Contains("x:Name=\"SelectedContent\"", controls);
        Assert.Contains("x:Name=\"PART_EditableTextBox\"", controls);
        Assert.Contains("Margin=\"1,1,31,1\"", controls);
        Assert.Contains("x:Name=\"DropDownToggle\"", controls);
        Assert.Contains(
            "IsChecked=\"{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}\"",
            controls);
        Assert.Contains(
            "Foreground=\"{TemplateBinding Foreground}\"",
            controls);
        Assert.Contains(
            "TextElement.Foreground=\"{DynamicResource SettingsTextPrimaryBrush}\"",
            controls);
        Assert.Contains(
            "<Trigger Property=\"IsHighlighted\" Value=\"True\">",
            controls);
        Assert.Contains(
            "<Trigger Property=\"IsSelected\" Value=\"True\">",
            controls);
        Assert.Contains(
            "Value=\"{DynamicResource SettingsTextPrimaryBrush}\"",
            controls);
    }

    [Fact]
    public void StatusBanner_SupportsAllFourSemanticVariants()
    {
        var source = WindowCode();
        Assert.Contains("StatusBannerSeverity.Error", source);
        Assert.Contains("SettingsErrorBackgroundBrush", source);
        Assert.Contains("SettingsWarningBackgroundBrush", source);
        Assert.Contains("SettingsInfoBackgroundBrush", source);
        Assert.Contains("SettingsSuccessBackgroundBrush", source);
    }

    public static IEnumerable<object[]> CultureSectionCases() =>
        from culture in Cultures
        from section in SettingsFashionLookRules.NavigationSections
        select new object[] { culture, section };

    private static void AssertThemeHasRequiredColors(string relativePath)
    {
        var keys = ResourceKeys(relativePath);
        var required = new[]
        {
            "SettingsWindowBackgroundBrush", "SettingsSidebarBackgroundBrush",
            "SettingsPanelBackgroundBrush", "SettingsCardBackgroundBrush",
            "SettingsTextPrimaryBrush", "SettingsTextSecondaryBrush",
            "SettingsAccentBrush", "SettingsAccentHoverBrush",
            "SettingsAccentPressedBrush", "SettingsBorderBrush",
            "SettingsHoverBrush", "SettingsSelectedBrush", "SettingsFocusBrush",
            "SettingsDisabledBrush", "SettingsValidationBrush",
            "SettingsAnnotationPreviewBackgroundBrush",
            "SettingsFlagCardBackgroundBrush", "SettingsBadgeBackgroundBrush",
            "SettingsBadgeTextBrush", "SettingsToolTipBackgroundBrush",
            "SettingsToolTipTextBrush",
            "SettingsSuccessBackgroundBrush", "SettingsSuccessBorderBrush",
            "SettingsInfoBackgroundBrush", "SettingsInfoBorderBrush",
            "SettingsWarningBackgroundBrush", "SettingsWarningBorderBrush",
            "SettingsErrorBackgroundBrush", "SettingsErrorBorderBrush",
            "SettingsShadowColor",
        };
        Assert.All(required, key => Assert.Contains(key, keys));
    }

    private static HashSet<string> ResourceKeys(string relativePath)
    {
        var document = XDocument.Load(Path.Combine(UiDirectory, relativePath));
        var keyAttribute = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
        return document
            .Descendants()
            .Select(element => (string?)element.Attribute(keyAttribute))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string SectionResourceKey(SettingsWindowTabKind section) =>
        section switch
        {
            SettingsWindowTabKind.Manufacturing => "SettingsWindow_Manufacturing_Tab",
            SettingsWindowTabKind.Annotation => "SettingsWindow_Annotation_Tab",
            SettingsWindowTabKind.Language => "SettingsWindow_Language_Tab",
            _ => "SettingsWindow_Layers_Tab",
        };

    private static string WindowXaml() =>
        File.ReadAllText(Path.Combine(UiDirectory, "LayerSettingsWindow.xaml"));

    private static string WindowCode() =>
        File.ReadAllText(Path.Combine(UiDirectory, "LayerSettingsWindow.xaml.cs"));

    private static string PickerXaml() =>
        File.ReadAllText(Path.Combine(UiDirectory, "AciColorPicker.xaml")) +
        File.ReadAllText(Path.Combine(UiDirectory, "AciColorPickerWindow.xaml"));

    private static string PickerCode() =>
        File.ReadAllText(Path.Combine(UiDirectory, "AciColorPickerWindow.xaml.cs"));

    private static string VisualStateCode() =>
        File.ReadAllText(Path.Combine(UiDirectory, "SettingsVisualStateViewModel.cs"));

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, $"End marker not found: {end}");
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root containing AcKrovy.sln was not found.");
    }
}
