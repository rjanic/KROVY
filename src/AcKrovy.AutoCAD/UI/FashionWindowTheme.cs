using System.Windows;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

internal static class FashionWindowTheme
{
    public static void Apply(Window window, SettingsTheme theme)
    {
        ArgumentNullException.ThrowIfNull(window);
        var source = theme == SettingsTheme.Dark
            ? "Design/SettingsColors.Dark.xaml"
            : "Design/SettingsColors.Light.xaml";
        window.Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri(
                $"/AcKrovy.AutoCAD;component/UI/{source}",
                UriKind.RelativeOrAbsolute),
        };
    }
}
