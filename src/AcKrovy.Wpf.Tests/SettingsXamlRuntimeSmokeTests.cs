using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class SettingsXamlRuntimeSmokeTests
{
    [Fact]
    [Trait("Feature", "FashionLook")]
    [Trait("Feature", "AciPickerRuntime")]
    [Trait("Feature", "LanguageFlags")]
    public void FashionLook_CompiledBaml_LoadsWindowThemesAndAciPicker()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                AppLanguageService.Apply("en");
                LoadResourceDictionaries();
                Assert.Equal(6, LanguageFlagResourceMap.All.Count);
                foreach (var flagResource in LanguageFlagResourceMap.All)
                {
                    var color = new BitmapImage(
                        new Uri(flagResource.ColorPackUri, UriKind.RelativeOrAbsolute));
                    var black = new BitmapImage(
                        new Uri(flagResource.BlackPackUri, UriKind.RelativeOrAbsolute));
                    Assert.True(color.PixelWidth > 0);
                    Assert.True(color.PixelHeight > 0);
                    Assert.True(black.PixelWidth > 0);
                    Assert.True(black.PixelHeight > 0);
                }
                Assert.Equal(10, AnnotationPreviewResourceMap.AllPackUris.Count);
                foreach (var previewUri in AnnotationPreviewResourceMap.AllPackUris)
                {
                    var preview = new BitmapImage(
                        new Uri(previewUri, UriKind.Absolute));
                    Assert.Equal(501, preview.PixelWidth);
                    Assert.Equal(321, preview.PixelHeight);
                }

                var applyCallCount = 0;
                var appliedModes = new List<SettingsSaveMode>();
                var appliedRequests = new List<SettingsApplyRequest>();
                var languageApplyCount = 0;
                var languageSaveCount = 0;
                var languageRefreshCount = 0;
                var languageWorkflow = new ApplicationLanguageWorkflow(
                    () => AppLanguageService.CurrentLanguageCode,
                    languageCode =>
                    {
                        languageApplyCount++;
                        AppLanguageService.Apply(languageCode);
                    },
                    _ => languageSaveCount++,
                    () => languageRefreshCount++);
                var openingLayerProfile = ElementLayerProfile.CreateDefault();
                openingLayerProfile.GetStyle(TimberElementType.Rafter).LayerName =
                    "ROMAN_KROKVA";
                var window = new LayerSettingsWindow(
                    openingLayerProfile,
                    TimberElementDefaultProfile.CreateDefault(),
                    "en",
                    CadLinetypeNames.SupportedStandardNames,
                    [
                        new CadLayerPreset("0", 7, CadLinetypeNames.Continuous),
                        new CadLayerPreset("Defpoints", 7, CadLinetypeNames.Continuous),
                    ],
                    request =>
                    {
                        applyCallCount++;
                        appliedModes.Add(request.SaveMode);
                        appliedRequests.Add(request);
                        return new SettingsApplyResponse(
                            Success: true,
                            ProfileAccepted: true,
                            Severity: StatusBannerSeverity.Success,
                            ResourceKey: "SettingsWindow_Applied",
                            ResourceArguments: [],
                            AvailableLinetypeNames: CadLinetypeNames.SupportedStandardNames,
                            AvailableLayerPresets:
                            [
                                new CadLayerPreset("0", 7, CadLinetypeNames.Continuous),
                                new CadLayerPreset("Defpoints", 7, CadLinetypeNames.Continuous),
                            ]);
                    },
                    languageWorkflow,
                    annotationScaleState: new AnnotationScaleSettingsState(
                        HasDrawingOverride: true,
                        DrawingDenominator: 25,
                        EffectiveDenominator: 25));

                Assert.All(
                    window.Rows,
                    row => Assert.False(string.IsNullOrWhiteSpace(row.LayerName)));
                Assert.Equal(
                    "ROMAN_KROKVA",
                    window.Rows.Single(row =>
                        row.ElementType == TimberElementType.Rafter).LayerName);
                window.Visual.SelectedSection = SettingsWindowTabKind.Layers;

                window.Left = -30000;
                window.Top = -30000;
                window.ShowInTaskbar = false;
                window.WindowStyle = WindowStyle.None;
                window.Show();
                window.UpdateLayout();
                Assert.All(
                    window.Rows,
                    row => Assert.False(string.IsNullOrWhiteSpace(row.LayerName)));
                var layerNameEditors = FindVisualChildren<ComboBox>(
                        window.StylesDataGrid)
                    .Where(combo => combo.DataContext is LayerSettingsRow)
                    .ToArray();
                Assert.Equal(window.Rows.Count, layerNameEditors.Length);
                Assert.All(
                    layerNameEditors,
                    editor => Assert.Equal(
                        ((LayerSettingsRow)editor.DataContext).LayerName,
                        editor.Text));
                window.StylesDataGrid.CommitEdit();
                window.StylesDataGrid.CommitEdit();
                Assert.All(
                    window.Rows,
                    row => Assert.False(string.IsNullOrWhiteSpace(row.LayerName)));
                window.Visual.SelectedSection = SettingsWindowTabKind.Annotation;
                window.UpdateLayout();
                Assert.Equal(0, languageApplyCount);
                Assert.Equal(0, languageSaveCount);
                Assert.Equal(0, languageRefreshCount);
                Assert.NotNull(window.FindName("SettingsNavigation"));
                var originalPreset = window.SelectedAnnotationPreset;
                window.Visual.SelectedSection = SettingsWindowTabKind.Annotation;
                window.UpdateLayout();
                Assert.Equal(6, window.AnnotationCategoryTabs.Items.Count);
                Assert.Equal(window.AnnotationScaleTab, window.AnnotationCategoryTabs.SelectedItem);
                var annotationTabPanel = Assert.Single(
                    FindVisualChildren<System.Windows.Controls.Primitives.TabPanel>(
                        window.AnnotationCategoryTabs));
                Assert.True(annotationTabPanel.IsVisible);
                var annotationTabs = window.AnnotationCategoryTabs.Items
                    .Cast<TabItem>()
                    .ToArray();
                Assert.Equal(6, annotationTabs.Length);
                Assert.All(annotationTabs, tab =>
                {
                    Assert.True(tab.IsVisible);
                    var headerPresenter = Assert.Single(
                        FindVisualChildren<ContentPresenter>(tab));
                    Assert.Same(tab.Header, headerPresenter.Content);
                    Assert.True(headerPresenter.IsVisible);
                });
                var selectedContentPresenter = Assert.Single(
                    FindVisualChildren<ContentPresenter>(window.AnnotationCategoryTabs),
                    presenter => ReferenceEquals(
                        presenter.TemplatedParent,
                        window.AnnotationCategoryTabs));
                Assert.Same(window.AnnotationScaleTab.Content, selectedContentPresenter.Content);
                Assert.Equal(
                    TimberAnnotationScalePreset.Scale50,
                    window.SelectedDrawingScalePreset);
                Assert.Equal(
                    TimberAnnotationScalePreset.Scale50,
                    window.DrawingAnnotationScaleSelector.SelectedValue);
                Assert.All(
                    annotationTabs.Skip(2),
                    tab => Assert.False(((FrameworkElement)tab.Content).IsVisible));
                window.AnnotationCategoryTabs.SelectedIndex = 0;
                window.UpdateLayout();
                Assert.Same(annotationTabs[0].Content, selectedContentPresenter.Content);
                Assert.True(window.AnnotationPresetSelector.IsVisible);
                Assert.Equal(10, window.AnnotationPresetSelector.Items.Count);
                var pickerImages =
                    FindVisualChildren<Image>(window.AnnotationPresetSelector).ToArray();
                Assert.Equal(10, pickerImages.Length);
                Assert.False(window.DrawingAnnotationScaleSelector.IsVisible);
                window.AnnotationPresetSelector.SelectedValue =
                    SettingsAnnotationPreset.ItemCircle;
                Assert.Equal(
                    TimberAnnotationMode.ItemNumberLeader,
                    window.SelectedAnnotationMode);
                Assert.Equal(
                    ItemNumberLeaderStyle.Circle,
                    window.SelectedItemNumberLeaderStyle);
                window.AnnotationCategoryTabs.SelectedIndex = 1;
                window.UpdateLayout();
                Assert.Same(window.AnnotationScaleTab.Content, selectedContentPresenter.Content);
                Assert.False(window.AnnotationPresetSelector.IsVisible);
                Assert.True(window.DrawingAnnotationScaleSelector.IsVisible);
                Assert.Equal(5, window.DrawingAnnotationScaleSelector.Items.Count);
                var previewDimensionAt25 = window.PreviewDimensionText;
                var scaleContent = Assert.IsAssignableFrom<FrameworkElement>(
                    window.AnnotationScaleTab.Content);
                Assert.True(
                    scaleContent.DesiredSize.Height <= scaleContent.ActualHeight + 1d,
                    $"Scale content requires {scaleContent.DesiredSize.Height:0.##} px " +
                    $"but only {scaleContent.ActualHeight:0.##} px is available.");
                var scaleCards = FindVisualChildren<ListBoxItem>(
                    window.DrawingAnnotationScaleSelector).ToArray();
                Assert.Equal(5, scaleCards.Length);
                Assert.True(scaleCards.Zip(scaleCards.Skip(1))
                    .All(pair => pair.First.TranslatePoint(
                        new Point(),
                        window.DrawingAnnotationScaleSelector).Y <
                        pair.Second.TranslatePoint(
                            new Point(),
                            window.DrawingAnnotationScaleSelector).Y));

                for (var tabIndex = 2; tabIndex < annotationTabs.Length; tabIndex++)
                {
                    window.AnnotationCategoryTabs.SelectedIndex = tabIndex;
                    window.UpdateLayout();
                    Assert.Same(
                        annotationTabs[tabIndex].Content,
                        selectedContentPresenter.Content);
                    for (var placeholderIndex = 2;
                         placeholderIndex < annotationTabs.Length;
                         placeholderIndex++)
                    {
                        Assert.Equal(
                            placeholderIndex == tabIndex,
                            ((FrameworkElement)annotationTabs[placeholderIndex].Content)
                                .IsVisible);
                    }
                    Assert.Empty(
                        FindVisualChildren<Button>(
                            (DependencyObject)annotationTabs[tabIndex].Content));
                }
                window.AnnotationCategoryTabs.SelectedIndex = 1;
                window.UpdateLayout();

                window.DrawingAnnotationScaleSelector.SelectedValue =
                    TimberAnnotationScalePreset.Scale100;
                Assert.Equal(
                    TimberAnnotationScalePreset.Scale100,
                    window.SelectedDrawingScalePreset);
                window.DrawingAnnotationScaleSelector.SelectedValue =
                    TimberAnnotationScalePreset.Custom;
                window.DrawingCustomScaleText = "125";
                window.UpdateLayout();
                Assert.True(window.IsDrawingCustomScale);
                Assert.True(window.DrawingCustomScaleTextBox.IsVisible);
                Assert.False(window.HasDrawingScaleError);
                Assert.Equal(1.35d, window.PreviewPresentationScale);
                Assert.True(
                    window.AnnotationScalePreviewImage.RenderTransform.Value.IsIdentity);
                Assert.NotEqual(previewDimensionAt25, window.PreviewDimensionText);
                Assert.Contains("312", window.PreviewDimensionModelText);
                Assert.Contains("2", window.PreviewDimensionPaperText);
                Assert.Contains("×2", window.PreviewBlockModelText);

                window.SelectedAnnotationPreset =
                    SettingsAnnotationPreset.DimensionsWithItemRectangle;
                window.UpdateLayout();
                Assert.Equal(Stretch.Uniform, window.AnnotationScalePreviewImage.Stretch);
                Assert.EndsWith(
                    "/09_rozmery_obdlznik.png",
                    window.AnnotationScalePreviewImage.Source.ToString(),
                    StringComparison.OrdinalIgnoreCase);
                Assert.Single(
                    FindVisualChildren<Image>(
                        (DependencyObject)window.AnnotationScalePreviewImage.Parent));
                Assert.DoesNotContain(
                    FindVisualChildren<System.Windows.Controls.Primitives.ScrollBar>(
                        window.DrawingAnnotationScaleSelector),
                    scrollBar => scrollBar.IsVisible);
                window.DarkThemeButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(SettingsTheme.Dark, window.Visual.SelectedTheme);
                Assert.Equal(
                    Colors.Black,
                    ((SolidColorBrush)((Border)window.AnnotationScalePreviewImage.Parent)
                        .Background).Color);
                window.AnnotationCategoryTabs.SelectedIndex = 0;
                window.UpdateLayout();
                Assert.All(
                    pickerImages,
                    image => Assert.Equal(
                        Colors.Black,
                        ((SolidColorBrush)((Border)image.Parent).Background).Color));
                window.AnnotationCategoryTabs.SelectedIndex = 1;
                window.UpdateLayout();
                Assert.Equal(
                    SettingsAnnotationPreset.DimensionsWithItemRectangle,
                    window.SelectedAnnotationPreset);
                window.LightThemeButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(SettingsTheme.Light, window.Visual.SelectedTheme);
                Assert.Equal(
                    Colors.White,
                    ((SolidColorBrush)((Border)window.AnnotationScalePreviewImage.Parent)
                        .Background).Color);
                window.AnnotationCategoryTabs.SelectedIndex = 0;
                window.UpdateLayout();
                Assert.All(
                    pickerImages,
                    image => Assert.Equal(
                        Colors.White,
                        ((SolidColorBrush)((Border)image.Parent).Background).Color));
                window.AnnotationCategoryTabs.SelectedIndex = 1;
                window.UpdateLayout();
                Assert.Equal(
                    SettingsAnnotationPreset.DimensionsWithItemRectangle,
                    window.SelectedAnnotationPreset);

                var picker = new AciColorPicker
                {
                    Options = LayerColorOption.CreateAll(CultureInfo.GetCultureInfo("en-US")),
                };
                var row = window.Rows.Single(candidate =>
                    candidate.ElementType == TimberElementType.Rafter);
                var originalLayerName = row.LayerName;
                Assert.Equal("ROMAN_KROKVA", originalLayerName);
                var originalScaleText = row.LinetypeScaleText;
                var languageCards = window.LanguageOptions.ToArray();
                row.LayerName = "PENDING_LAYER";
                row.LinetypeScaleText = "0.75";
                window.Visual.SelectedSection = SettingsWindowTabKind.Language;
                foreach (var languageCode in new[] { "sk", "cs", "en", "de", "pl", "fr" })
                {
                    window.LanguageSelector.SelectedValue = languageCode;
                    window.UpdateLayout();
                    Assert.Equal(languageCode, window.SelectedLanguageCode);
                    Assert.Equal(languageCode, AppLanguageService.CurrentLanguageCode);
                    Assert.Equal("PENDING_LAYER", row.LayerName);
                    Assert.Equal("0.75", row.LinetypeScaleText);
                    Assert.Equal(SettingsWindowTabKind.Language, window.Visual.SelectedSection);
                    Assert.Equal(
                        SettingsAnnotationPreset.DimensionsWithItemRectangle,
                        window.SelectedAnnotationPreset);
                    Assert.Equal(0, applyCallCount);
                    Assert.Equal(languageCards, window.LanguageOptions);
                    foreach (var option in languageCards)
                    {
                        var container = Assert.IsType<ListBoxItem>(
                            window.LanguageSelector.ItemContainerGenerator
                                .ContainerFromItem(option));
                        var image = FindVisualChild<Image>(container);
                        Assert.NotNull(image);
                        Assert.True(
                            image.ActualWidth >= 300d,
                            $"Language flag width {image.ActualWidth} did not fill its card.");
                        Assert.True(
                            image.ActualHeight >= 216d,
                            $"Language flag height {image.ActualHeight} did not fill its card.");
                        Assert.Equal(
                            [option.UpperCode],
                            FindVisualChildren<TextBlock>(container)
                                .Select(text => text.Text)
                                .Where(text => !string.IsNullOrWhiteSpace(text)));
                        var expectedUri = option.Code == languageCode
                            ? option.ColorFlagUri
                            : option.BlackFlagUri;
                        var expectedFileName = expectedUri.Split('/').Last();
                        Assert.True(
                            image.Source.ToString()!.EndsWith(
                                expectedFileName,
                                StringComparison.OrdinalIgnoreCase),
                            $"Language '{option.Code}' used '{image.Source}' instead of '{expectedFileName}'.");
                    }
                }
                Assert.Equal(6, languageApplyCount);
                Assert.Equal(6, languageSaveCount);
                Assert.Equal(6, languageRefreshCount);
                Assert.Equal(6, window.LanguageOptions.Count);
                Assert.All(
                    window.LanguageOptions,
                    option => Assert.False(string.IsNullOrWhiteSpace(option.AccessibilityName)));
                row.LayerName = originalLayerName;
                row.LinetypeScaleText = originalScaleText;
                window.SelectedAnnotationPreset = originalPreset;
                window.LanguageSelector.SelectedValue = "en";
                window.UpdateLayout();
                Assert.Equal(7, languageApplyCount);
                Assert.Equal(7, languageSaveCount);
                Assert.Equal(7, languageRefreshCount);
                window.Visual.SelectedSection = SettingsWindowTabKind.Layers;
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, window.LayersFooterActions.Visibility);
                Assert.Equal(Visibility.Collapsed, window.AnnotationFooterActions.Visibility);
                Assert.All(
                    window.Rows,
                    candidate => Assert.False(
                        string.IsNullOrWhiteSpace(candidate.LayerName)));
                row.LayerName = "KROVY_TEST_LAYER_APPLY";
                window.LayersApplyButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));

                window.Visual.SelectedSection = SettingsWindowTabKind.Manufacturing;
                window.UpdateLayout();
                Assert.Equal(
                    Visibility.Visible,
                    window.ManufacturingFooterActions.Visibility);
                window.DefaultRows[0].CuttingAllowanceMmText = "125";
                window.ManufacturingApplyButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));

                window.Visual.SelectedSection = SettingsWindowTabKind.Annotation;
                window.UpdateLayout();
                Assert.Equal(
                    Visibility.Visible,
                    window.AnnotationFooterActions.Visibility);
                window.SelectedAnnotationPreset =
                    SettingsAnnotationPreset.DimensionsOnly;
                window.SaveNewElementsButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(
                    TimberAnnotationSettingsApplyScope.NewElementsOnly,
                    appliedRequests[^1].AnnotationSettings!.ApplyScope);
                var acceptedLayerProfile = appliedRequests[^1].Profile.Normalize();
                row.LayerName = string.Empty;
                Assert.True(window.LayersDirty);
                window.DrawingAnnotationScaleSelector.SelectedValue =
                    TimberAnnotationScalePreset.Custom;
                window.DrawingCustomScaleText = "4";
                Assert.True(window.HasDrawingScaleError);
                var requestsBeforeInvalidScale = appliedRequests.Count;
                window.SaveApplySelectionButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(requestsBeforeInvalidScale, appliedRequests.Count);
                window.DrawingCustomScaleText = "251";
                Assert.True(window.HasDrawingScaleError);
                window.SaveApplySelectionButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(requestsBeforeInvalidScale, appliedRequests.Count);
                window.DrawingCustomScaleText = "5";
                Assert.False(window.HasDrawingScaleError);
                window.SaveNewElementsButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(5, appliedRequests[^1].AnnotationSettings!.ScaleDenominator);
                window.DrawingCustomScaleText = "250";
                Assert.False(window.HasDrawingScaleError);
                window.SaveNewElementsButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(250, appliedRequests[^1].AnnotationSettings!.ScaleDenominator);
                window.DrawingAnnotationScaleSelector.SelectedValue =
                    TimberAnnotationScalePreset.Scale25;
                window.SaveApplySelectionButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                window.DrawingAnnotationScaleSelector.SelectedValue =
                    TimberAnnotationScalePreset.Scale100;
                window.SaveApplySelectionButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                Assert.All(
                    appliedRequests
                        .Where(request => request.SaveMode ==
                            SettingsSaveMode.SelectedElements),
                    request =>
                    {
                        Assert.False(request.LayerProfileChanged);
                        Assert.Equal(
                            acceptedLayerProfile.Styles.Select(style => (
                                style.ElementType,
                                style.LayerName,
                                style.ColorIndex,
                                style.LinetypeName,
                                style.LinetypeScale)),
                            request.Profile.Normalize().Styles.Select(style => (
                                style.ElementType,
                                style.LayerName,
                                style.ColorIndex,
                                style.LinetypeName,
                                style.LinetypeScale)));
                    });
                Assert.Equal(string.Empty, row.LayerName);
                row.LayerName = acceptedLayerProfile
                    .GetStyle(TimberElementType.Rafter).LayerName;
                window.SaveApplyAllButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                window.SaveApplyAllButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                window.DrawingAnnotationScaleSelector.SelectedValue =
                    TimberAnnotationScalePreset.Scale75;
                window.SaveApplyAllButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(
                    [
                        SettingsSaveMode.NewElementsOnly,
                        SettingsSaveMode.NewElementsOnly,
                        SettingsSaveMode.NewElementsOnly,
                        SettingsSaveMode.NewElementsOnly,
                        SettingsSaveMode.NewElementsOnly,
                        SettingsSaveMode.SelectedElements,
                        SettingsSaveMode.SelectedElements,
                        SettingsSaveMode.AllElements,
                        SettingsSaveMode.AllElements,
                        SettingsSaveMode.AllElements,
                    ],
                    appliedModes);
                Assert.All(
                    appliedRequests.Where(request =>
                        request.SaveMode == SettingsSaveMode.SelectedElements),
                    request =>
                    {
                        Assert.Equal(
                            TimberAnnotationSettingsApplyScope.SelectedElements,
                            request.AnnotationSettings!.ApplyScope);
                    });
                Assert.Equal(
                    [25, 100],
                    appliedRequests
                        .Where(request => request.SaveMode ==
                            SettingsSaveMode.SelectedElements)
                        .Select(request =>
                            request.AnnotationSettings!.ScaleDenominator));
                Assert.All(
                    appliedRequests.Where(request =>
                        request.SaveMode == SettingsSaveMode.AllElements),
                    request =>
                    {
                        Assert.Equal(
                            TimberAnnotationSettingsApplyScope.AllElements,
                            request.AnnotationSettings!.ApplyScope);
                    });
                Assert.Equal(75, appliedRequests[^1].AnnotationSettings!.ScaleDenominator);
                Assert.Equal(
                    TimberAnnotationScalePreset.Scale75,
                    window.SelectedDrawingScalePreset);
                Assert.True(window.IsVisible);
                Assert.True(window.IsEnabled);
                Assert.Equal(2, row.AciColorIndex);
                Assert.Equal(SettingsFormState.Applied, window.Visual.CurrentFormState);
                BindingOperations.SetBinding(
                    picker,
                    AciColorPicker.SelectedAciIndexProperty,
                    new Binding(nameof(LayerSettingsRow.AciColorIndex))
                    {
                        Source = row,
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    });
                var rowChanged = false;
                row.PropertyChanged += (_, args) =>
                    rowChanged |= args.PropertyName == nameof(LayerSettingsRow.AciColorIndex);
                var pickerHost = new Window
                {
                    Width = 800,
                    Height = 700,
                    Left = -30000,
                    Top = -30000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = picker,
                };
                pickerHost.Show();
                pickerHost.UpdateLayout();
                Assert.True(picker.IsInitialized);

                bool? confirmResult = null;
                ScheduleModalAction(pickerHost, dialog =>
                {
                    dialog.Closed += (_, _) => confirmResult = dialog.DialogResult;
                    Assert.Equal(2, dialog.OriginalAciIndex);
                    Assert.Equal(2, dialog.PendingAciIndex);
                    Assert.True(dialog.IsVisible);

                    var option90 = picker.Options
                        .Cast<LayerColorOption>()
                        .Single(option => option.Index == 90);
                    dialog.MainColorList.SelectedItem = option90;
                    dialog.MainColorList.ScrollIntoView(option90);
                    dialog.UpdateLayout();
                    Assert.Equal(90, dialog.PendingAciIndex);
                    Assert.Equal(2, row.AciColorIndex);

                    AssertClickDoesNotClose(
                        dialog,
                        (UIElement?)dialog.MainColorList.ItemContainerGenerator
                            .ContainerFromItem(option90) ?? dialog.MainColorList);
                    AssertClickDoesNotClose(dialog, dialog.IndexInput);
                    dialog.PaletteScrollViewer.ScrollToVerticalOffset(10);
                    AssertClickDoesNotClose(
                        dialog,
                        (UIElement?)FindVisualChild<System.Windows.Controls.Primitives.ScrollBar>(
                            dialog.PaletteScrollViewer) ??
                        dialog.PaletteScrollViewer);
                    AssertClickDoesNotClose(dialog, dialog.PaletteEmptySpace);
                    AssertClickDoesNotClose(dialog, dialog.PaletteSectionTitle);
                    AssertClickDoesNotClose(dialog, dialog.ColorPreview);

                    dialog.ConfirmButton.RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent));
                });
                picker.OpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(confirmResult);
                Assert.Equal(90, row.AciColorIndex);
                Assert.Equal(90, picker.SelectedAciIndex);
                Assert.Equal(90, picker.SelectedColor!.Index);
                Assert.True(rowChanged);
                Assert.Equal(
                    SettingsFormState.UnsavedChanges,
                    window.Visual.CurrentFormState);

                rowChanged = false;
                bool? cancelResult = null;
                ScheduleModalAction(pickerHost, dialog =>
                {
                    dialog.Closed += (_, _) => cancelResult = dialog.DialogResult;
                    Assert.Equal(90, dialog.OriginalAciIndex);
                    dialog.PendingAciIndex = 142;
                    Assert.Equal(90, row.AciColorIndex);
                    dialog.CancelButton.RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent));
                });
                picker.OpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(cancelResult);
                Assert.Equal(90, row.AciColorIndex);
                Assert.False(rowChanged);

                bool? escapeResult = null;
                ScheduleModalAction(pickerHost, dialog =>
                {
                    dialog.Closed += (_, _) => escapeResult = dialog.DialogResult;
                    dialog.PendingAciIndex = 142;
                    RaisePreviewKey(dialog, Key.Escape);
                });
                picker.OpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(escapeResult);
                Assert.Equal(90, row.AciColorIndex);
                Assert.False(rowChanged);

                bool? enterResult = null;
                ScheduleModalAction(pickerHost, dialog =>
                {
                    dialog.Closed += (_, _) => enterResult = dialog.DialogResult;
                    dialog.PendingAciIndex = 91;
                    RaisePreviewKey(dialog, Key.Enter);
                });
                picker.OpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(enterResult);
                Assert.Equal(91, row.AciColorIndex);
                Assert.True(rowChanged);

                rowChanged = false;
                ScheduleModalAction(pickerHost, dialog =>
                {
                    dialog.PendingAciIndex = 142;
                    dialog.Close();
                });
                picker.OpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(91, row.AciColorIndex);
                Assert.False(rowChanged);
                pickerHost.Close();
                window.Close();
                AppLanguageService.Apply("en");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF smoke test timed out.");
        Assert.Null(failure);
    }

    private static void ScheduleModalAction(
        Window owner,
        Action<AciColorPickerWindow> action) =>
        owner.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                var dialog = owner.OwnedWindows
                    .OfType<AciColorPickerWindow>()
                    .Single();
                action(dialog);
            }));

    private static void AssertClickDoesNotClose(
        AciColorPickerWindow dialog,
        UIElement element)
    {
        RaisePreviewMouseDown(element);
        Assert.True(dialog.IsVisible);
    }

    private static void RaisePreviewMouseDown(UIElement target)
    {
        var source = PresentationSource.FromVisual(target);
        Assert.NotNull(source);
        target.RaiseEvent(new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent,
            Source = target,
        });
    }

    private static void RaisePreviewKey(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target);
        Assert.NotNull(source);
        target.RaiseEvent(new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            Environment.TickCount,
            key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void LoadResourceDictionaries()
    {
        var sources = new[]
        {
            "SettingsColors.Light.xaml",
            "SettingsColors.Dark.xaml",
            "SettingsDesignSystem.xaml",
            "SettingsControls.xaml",
        };

        foreach (var source in sources)
        {
            var dictionary = new ResourceDictionary
            {
                Source = new Uri(
                    $"/AcKrovy.AutoCAD;component/UI/Design/{source}",
                    UriKind.RelativeOrAbsolute),
            };
            Assert.NotEmpty(dictionary.Keys.Cast<object>());

            if (source == "SettingsDesignSystem.xaml")
            {
                Assert.IsType<GridLength>(
                    dictionary["SettingsNavigationColumnWidth"]);
            }

        }
    }

    [Fact]
    public void SettingsOwner_CentersOnOwnerAndFallsBackToScreen()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                var owner = new Window
                {
                    Width = 400,
                    Height = 300,
                    Left = SystemParameters.WorkArea.Left + 120,
                    Top = SystemParameters.WorkArea.Top + 100,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };
                owner.Show();
                var ownerHandle = new WindowInteropHelper(owner).Handle;
                var owned = new Window
                {
                    Width = 200,
                    Height = 120,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };

                Assert.True(SettingsWindowOwner.TryAssign(owned, ownerHandle));
                Assert.Equal(WindowStartupLocation.Manual, owned.WindowStartupLocation);
                Assert.Equal(ownerHandle, new WindowInteropHelper(owned).Owner);
                Assert.Equal(1d, owned.Opacity);
                owned.Loaded += (_, _) =>
                {
                    // Simulate a modal host overriding WPF's initial placement.
                    owned.Left = SystemParameters.WorkArea.Left;
                    owned.Top = SystemParameters.WorkArea.Top;
                };
                owned.Show();
                owned.UpdateLayout();
                owned.Dispatcher.Invoke(
                    DispatcherPriority.Render,
                    new Action(() => { }));
                owned.Dispatcher.Invoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => { }));
                Assert.Equal(WindowStartupLocation.Manual, owned.WindowStartupLocation);
                Assert.Equal(1d, owned.Opacity);
                Assert.True(SettingsWindowOwner.TryGetWindowBounds(owner, out var ownerBounds));
                Assert.True(SettingsWindowOwner.TryGetWindowBounds(owned, out var ownedBounds));
                Assert.InRange(
                    Math.Abs(
                        (ownedBounds.Left + (ownedBounds.Width / 2)) -
                        (ownerBounds.Left + (ownerBounds.Width / 2))),
                    0,
                    2);
                Assert.InRange(
                    Math.Abs(
                        (ownedBounds.Top + (ownedBounds.Height / 2)) -
                        (ownerBounds.Top + (ownerBounds.Height / 2))),
                    0,
                    2);

                var preservedBounds = ownedBounds;
                using (SettingsWindowOwner.PreserveCurrentPosition(owned))
                {
                    owned.Left = SystemParameters.WorkArea.Left + 10;
                    owned.Top = SystemParameters.WorkArea.Top + 10;
                    owned.UpdateLayout();
                    Assert.True(SettingsWindowOwner.TryGetWindowBounds(
                        owned,
                        out var guardedBounds));
                    Assert.Equal(preservedBounds.Left, guardedBounds.Left);
                    Assert.Equal(preservedBounds.Top, guardedBounds.Top);
                }

                var placement = SettingsWindowOwner.CapturePlacement(owned);
                Assert.True(placement.IsValid);
                void AssertPlacementRestored()
                {
                    Assert.Equal(WindowStartupLocation.Manual, owned.WindowStartupLocation);
                    Assert.Equal(placement.Left, owned.Left);
                    Assert.Equal(placement.Top, owned.Top);
                    Assert.Equal(placement.Width, owned.Width);
                    Assert.Equal(placement.Height, owned.Height);
                    Assert.Equal(placement.WindowState, owned.WindowState);
                }
                void DrainDeferredPlacementRestore(Window target)
                {
                    target.Dispatcher.Invoke(
                        DispatcherPriority.ApplicationIdle,
                        new Action(() => { }));
                    target.Dispatcher.Invoke(
                        DispatcherPriority.ContextIdle,
                        new Action(() => { }));
                }

                var cancelled = SettingsWindowOwner.RunWithPreservedPlacement(
                    owned,
                    () =>
                    {
                        owned.Left += 75d;
                        owned.Top += 60d;
                        owned.Width += 40d;
                        owned.Height += 30d;
                        return false;
                    });
                Assert.False(cancelled);
                DrainDeferredPlacementRestore(owned);
                AssertPlacementRestored();

                var succeeded = SettingsWindowOwner.RunWithPreservedPlacement(
                    owned,
                    () =>
                    {
                        owned.Left -= 55d;
                        owned.Top -= 45d;
                        owned.Width += 20d;
                        owned.Height += 10d;
                        return true;
                    });
                Assert.True(succeeded);
                owned.Left = 0d;
                owned.Top = 0d;
                owned.Width += 50d;
                owned.Height += 40d;
                owned.UpdateLayout();
                DrainDeferredPlacementRestore(owned);
                AssertPlacementRestored();

                Assert.Throws<InvalidOperationException>(() =>
                    SettingsWindowOwner.RunWithPreservedPlacement<object>(
                        owned,
                        () =>
                        {
                            owned.Left = 0d;
                            owned.Top = 0d;
                            owned.Width += 10d;
                            throw new InvalidOperationException("selection failed");
                        }));
                DrainDeferredPlacementRestore(owned);
                AssertPlacementRestored();

                var workflowWindow = new Window
                {
                    Width = 240,
                    Height = 160,
                    Left = SystemParameters.WorkArea.Left + 300,
                    Top = SystemParameters.WorkArea.Top + 220,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };
                workflowWindow.Show();
                workflowWindow.UpdateLayout();
                var workflowSnapshot = SettingsWindowOwner.CapturePlacement(
                    workflowWindow);
                Assert.True(workflowSnapshot.IsValid);
                _ = SettingsWindowOwner.RunWithPreservedPlacement(
                    workflowWindow,
                    () => true);
                workflowWindow.Left = 0d;
                workflowWindow.Top = 0d;
                workflowWindow.Width = 100d;
                workflowWindow.Height = 100d;
                workflowWindow.UpdateLayout();
                Assert.True(SettingsWindowOwner.TryGetWindowBounds(
                    workflowWindow,
                    out var lockedBounds));
                Assert.Equal(workflowSnapshot.NativeBounds.Left, lockedBounds.Left);
                Assert.Equal(workflowSnapshot.NativeBounds.Top, lockedBounds.Top);
                Assert.Equal(workflowSnapshot.NativeBounds.Width, lockedBounds.Width);
                Assert.Equal(workflowSnapshot.NativeBounds.Height, lockedBounds.Height);
                DrainDeferredPlacementRestore(workflowWindow);
                Assert.Equal(workflowSnapshot.Left, workflowWindow.Left);
                Assert.Equal(workflowSnapshot.Top, workflowWindow.Top);
                Assert.Equal(workflowSnapshot.Width, workflowWindow.Width);
                Assert.Equal(workflowSnapshot.Height, workflowWindow.Height);

                workflowWindow.Left = SystemParameters.WorkArea.Left + 50;
                workflowWindow.Top = SystemParameters.WorkArea.Top + 50;
                workflowWindow.UpdateLayout();
                Assert.True(SettingsWindowOwner.TryGetWindowBounds(
                    workflowWindow,
                    out var manuallyMovedBounds));
                Assert.NotEqual(workflowSnapshot.NativeBounds.Left, manuallyMovedBounds.Left);
                Assert.NotEqual(workflowSnapshot.NativeBounds.Top, manuallyMovedBounds.Top);
                workflowWindow.Close();

                var fallback = new Window
                {
                    Width = 200,
                    Height = 120,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };
                Assert.False(SettingsWindowOwner.TryAssign(fallback, IntPtr.Zero));
                Assert.Equal(WindowStartupLocation.Manual, fallback.WindowStartupLocation);
                Assert.Equal(1d, fallback.Opacity);
                fallback.Show();
                fallback.UpdateLayout();
                fallback.Dispatcher.Invoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => { }));
                Assert.Equal(WindowStartupLocation.Manual, fallback.WindowStartupLocation);
                Assert.Equal(1d, fallback.Opacity);
                fallback.Close();
                owned.Close();
                owner.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }

    [Theory]
    [InlineData(100, 100, 900, 700, 400, 300, 300, 250)]
    [InlineData(-1600, 0, 0, 900, 600, 400, -1100, 250)]
    [InlineData(0, 0, 500, 400, 800, 600, 0, 0)]
    public void SettingsOwner_CalculatesCenteredPositionInsideMonitorWorkArea(
        int ownerLeft,
        int ownerTop,
        int ownerRight,
        int ownerBottom,
        int windowWidth,
        int windowHeight,
        int expectedLeft,
        int expectedTop)
    {
        var workArea = new SettingsWindowOwner.NativeRect(
            ownerLeft,
            ownerTop,
            ownerRight,
            ownerBottom);

        var position = SettingsWindowOwner.CalculateCenteredPosition(
            workArea,
            workArea,
            windowWidth,
            windowHeight);

        Assert.Equal(expectedLeft, position.X);
        Assert.Equal(expectedTop, position.Y);
    }

    [Theory]
    [InlineData(-1500, 100, -1000, 600, -1920, 0, 0, 1080, -1500, 100)]
    [InlineData(-2500, -500, -2000, 0, -1920, 0, 0, 1080, -1920, 0)]
    [InlineData(2100, 100, 2600, 600, 1920, 0, 3840, 1080, 2100, 100)]
    [InlineData(3700, 900, 4200, 1400, 1920, 0, 3840, 1080, 3340, 580)]
    public void SettingsOwner_ClampsOnlyOffScreenPlacementToItsMonitor(
        int left,
        int top,
        int right,
        int bottom,
        int workLeft,
        int workTop,
        int workRight,
        int workBottom,
        int expectedLeft,
        int expectedTop)
    {
        var position = SettingsWindowOwner.CalculateClampedPosition(
            new SettingsWindowOwner.NativeRect(left, top, right, bottom),
            new SettingsWindowOwner.NativeRect(
                workLeft,
                workTop,
                workRight,
                workBottom));

        Assert.Equal(expectedLeft, position.X);
        Assert.Equal(expectedTop, position.Y);
    }

    [Theory]
    [InlineData(TimberAnnotationMode.NoAnnotations, ItemNumberLeaderStyle.Plain, "01_bez_popisov.png")]
    [InlineData(TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Plain, "02_polozka_plain.png")]
    [InlineData(TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Circle, "03_polozka_kruh.png")]
    [InlineData(TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Rectangle, "04_polozka_obdlznik.png")]
    [InlineData(TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Slot, "05_polozka_slot.png")]
    [InlineData(TimberAnnotationMode.DimensionsLeader, ItemNumberLeaderStyle.Plain, "06_iba_rozmery.png")]
    [InlineData(TimberAnnotationMode.FullLabel, ItemNumberLeaderStyle.Plain, "07_kompletny_popis.png")]
    [InlineData(TimberAnnotationMode.DimensionsWithItemNumber, ItemNumberLeaderStyle.Circle, "08_rozmery_kruh.png")]
    [InlineData(TimberAnnotationMode.DimensionsWithItemNumber, ItemNumberLeaderStyle.Rectangle, "09_rozmery_obdlznik.png")]
    [InlineData(TimberAnnotationMode.DimensionsWithItemNumber, ItemNumberLeaderStyle.Slot, "10_rozmery_slot.png")]
    public void AnnotationModeAndStyle_MapToExistingPreviewAsset(
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style,
        string expectedFileName)
    {
        var preset = SettingsAnnotationPresetRules.FromProduction(mode, style);

        Assert.EndsWith(
            expectedFileName,
            AnnotationPreviewResourceMap.GetPackUri(preset),
            StringComparison.OrdinalIgnoreCase);
    }
}
