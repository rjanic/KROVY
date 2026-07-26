using System.Globalization;

namespace AcKrovy.Localization;

public static class LayerColorDisplayNameProvider
{
    public static string GetDisplayName(int colorIndex, CultureInfo? culture = null)
    {
        if (colorIndex is < 1 or > 255)
        {
            throw new ArgumentOutOfRangeException(
                nameof(colorIndex),
                colorIndex,
                "Layer color index must be from 1 to 255.");
        }

        return colorIndex switch
        {
            1 => UiStrings.GetString("LayerColor_Red", culture),
            2 => UiStrings.GetString("LayerColor_Yellow", culture),
            3 => UiStrings.GetString("LayerColor_Green", culture),
            4 => UiStrings.GetString("LayerColor_Cyan", culture),
            5 => UiStrings.GetString("LayerColor_Blue", culture),
            6 => UiStrings.GetString("LayerColor_Magenta", culture),
            8 => UiStrings.GetString("LayerColor_Gray", culture),
            9 => UiStrings.GetString("LayerColor_LightGray", culture),
            30 => UiStrings.GetString("LayerColor_Orange", culture),
            _ => $"ACI {colorIndex}",
        };
    }
}
