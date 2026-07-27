using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
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
                var window = new LayerSettingsWindow(
                    ElementLayerProfile.CreateDefault(),
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
                    languageWorkflow);

                window.Left = -30000;
                window.Top = -30000;
                window.ShowInTaskbar = false;
                window.WindowStyle = WindowStyle.None;
                window.Show();
                window.UpdateLayout();
                Assert.Equal(0, languageApplyCount);
                Assert.Equal(0, languageSaveCount);
                Assert.Equal(0, languageRefreshCount);
                Assert.NotNull(window.FindName("SettingsNavigation"));
                Assert.Equal(10, window.AnnotationPresetOptions.Count);
                var originalPreset = window.SelectedAnnotationPreset;
                window.Visual.SelectedSection = SettingsWindowTabKind.Annotation;
                window.SelectedAnnotationPreset =
                    SettingsAnnotationPreset.DimensionsWithItemRectangle;
                window.UpdateLayout();
                Assert.Equal(
                    1,
                    window.AnnotationPresetSelector.Items
                        .Cast<object>()
                        .Select(item =>
                            window.AnnotationPresetSelector.ItemContainerGenerator
                                .ContainerFromItem(item))
                        .OfType<ListBoxItem>()
                        .Count(item => item.IsSelected));
                var previewImages =
                    FindVisualChildren<Image>(window.AnnotationPresetSelector).ToArray();
                Assert.Equal(10, previewImages.Length);
                Assert.All(
                    previewImages,
                    image =>
                    {
                        Assert.Equal(Stretch.Uniform, image.Stretch);
                        Assert.Contains(
                            "/UI/Assets/AnnotationPreviews/",
                            image.Source.ToString(),
                            StringComparison.OrdinalIgnoreCase);
                    });
                Assert.DoesNotContain(
                    FindVisualChildren<System.Windows.Controls.Primitives.ScrollBar>(
                        window.AnnotationPresetSelector),
                    scrollBar => scrollBar.IsVisible);
                window.DarkThemeButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(SettingsTheme.Dark, window.Visual.SelectedTheme);
                Assert.Equal(
                    SettingsAnnotationPreset.DimensionsWithItemRectangle,
                    window.SelectedAnnotationPreset);
                window.LightThemeButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(SettingsTheme.Light, window.Visual.SelectedTheme);
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
                window.SaveApplySelectionButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                window.SaveApplySelectionButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                window.SaveApplyAllButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                window.SaveApplyAllButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(
                    [
                        SettingsSaveMode.NewElementsOnly,
                        SettingsSaveMode.NewElementsOnly,
                        SettingsSaveMode.NewElementsOnly,
                        SettingsSaveMode.SelectedElements,
                        SettingsSaveMode.SelectedElements,
                        SettingsSaveMode.AllElements,
                        SettingsSaveMode.AllElements,
                    ],
                    appliedModes);
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
}
