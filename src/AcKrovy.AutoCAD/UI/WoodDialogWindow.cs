using System.Windows;
using WpfButton = System.Windows.Controls.Button;
using AcKrovy.AutoCAD.Settings;

namespace AcKrovy.AutoCAD.UI;

/// <summary>Small shared shell for modal KROVY dialogs using the approved wood resources.</summary>
internal abstract class WoodDialogWindow : Window
{
    protected WoodDialogWindow(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = System.Windows.Media.Brushes.Transparent;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = ThemeUri("SettingsColors.Light.xaml"),
        });
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = ThemeUri("SettingsDesignSystem.xaml"),
        });
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = ThemeUri("SettingsControls.xaml"),
        });
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = ThemeUri("RoofGeometryVisuals.xaml"),
        });
        FashionWindowTheme.Apply(this, SettingsUiPreferencesStore.Load().Theme);
        SetResourceReference(BackgroundProperty, "WoodOakBrush");
        SetResourceReference(ForegroundProperty, "SettingsTextPrimaryBrush");
    }

    private static Uri ThemeUri(string resourceName) => new(
        $"/AcKrovy.AutoCAD;component/UI/Design/{resourceName}",
        UriKind.RelativeOrAbsolute);

    protected static void ApplyWoodButtonStyle(WpfButton button, bool primary)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.SetResourceReference(StyleProperty,
            primary ? "SettingsPrimaryButtonStyle" : "SettingsSecondaryButtonStyle");
        button.SetResourceReference(BackgroundProperty,
            primary ? "RoofGeometryPrimaryButtonBrush" : "RoofGeometrySecondaryButtonBrush");
        button.SetResourceReference(BorderBrushProperty,
            primary ? "RoofGeometryPrimaryButtonBorderBrush" : "RoofGeometrySecondaryButtonBorderBrush");
    }
}
