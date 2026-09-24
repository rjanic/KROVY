using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Blocks generic timber assignment/edit from mutating ordinary generated-rafter
/// recipe fields. Geometry override lifecycle (GRIP/TRIM/ERASE) is unaffected.
/// </summary>
internal static class RoofGeneratedOrdinaryRafterMetadataGuard
{
    internal const string LocalizationKey = "Command_GeneratedRafter_UseRoofRafters";

    public static bool IsOrdinaryGeneratedRafter(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var generated = RoofGeneratedTimberStore.Read(entity);
        return generated.Data is not null &&
               generated.Data.MemberKind == RoofGeneratedTimberKind.Rafter;
    }

    public static bool BlocksRecipeDefiningWrite(
        Entity entity,
        TimberElementData original,
        TimberElementData proposed) =>
        IsOrdinaryGeneratedRafter(entity) &&
        RoofGeneratedRafterRecipeMetadataRules.MutatesRecipeDefiningFields(
            original,
            proposed);
}
