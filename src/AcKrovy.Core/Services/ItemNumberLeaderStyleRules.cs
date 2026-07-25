using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class ItemNumberLeaderStyleRules
{
    public static ItemNumberLeaderStyle Normalize(ItemNumberLeaderStyle style) =>
        Enum.IsDefined(typeof(ItemNumberLeaderStyle), style)
            ? style
            : ItemNumberLeaderStyle.Plain;
}
