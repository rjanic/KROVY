using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.Localization;

public static class ItemNumberLeaderStyleDisplayNameProvider
{
    public static string GetDisplayName(
        ItemNumberLeaderStyle style,
        CultureInfo? culture = null) =>
        UiStrings.GetString(
            ItemNumberLeaderStyleRules.Normalize(style) switch
            {
                ItemNumberLeaderStyle.Plain => "ItemNumberLeaderStyle_Plain",
                ItemNumberLeaderStyle.Circle => "ItemNumberLeaderStyle_Circle",
                ItemNumberLeaderStyle.Slot => "ItemNumberLeaderStyle_Slot",
                ItemNumberLeaderStyle.Rectangle => "ItemNumberLeaderStyle_Rectangle",
                _ => throw new InvalidOperationException(),
            },
            culture);
}
