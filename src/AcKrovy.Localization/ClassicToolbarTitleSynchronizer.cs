using System.Globalization;

namespace AcKrovy.Localization;

/// <summary>Resolves and applies the display title without knowing any AutoCAD API.</summary>
public static class ClassicToolbarTitleSynchronizer
{
    public static bool TrySynchronize(Action<string>? setTitle, CultureInfo? culture = null)
    {
        if (setTitle is null)
        {
            return false;
        }

        setTitle(UiStrings.GetString("Toolbar_Title", culture));
        return true;
    }
}
