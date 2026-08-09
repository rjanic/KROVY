using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Production routing for standalone framed ItemOnly (Iba popis Circle/Rect/Slot)
/// → one native BlockContent MLeader. Combined framed stays on G5; Combined Plain
/// stays on MText composite. Does not own R3 Combined lifecycle.
/// </summary>
internal static class AutoCadStandaloneFramedItemOnlyProductionPolicy
{
    public static bool UsesStandaloneFramedItemOnly(
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style,
        TimberMainAnnotationComponentRole componentRole) =>
        TimberAnnotationModeRules.Normalize(mode) ==
            TimberAnnotationMode.ItemNumberLeader &&
        TimberAnnotationModeRules.IsFramedItemLeader(mode, style) &&
        (componentRole == TimberMainAnnotationComponentRole.Primary ||
         AutoCadFramedG4CompositePolicy.IsG4CompositeRole(componentRole) ||
         AutoCadFramedG4CompositePolicy.IsLegacyG2G3BlockLeaderRole(componentRole));
}
