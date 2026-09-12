using System.Globalization;
using AcKrovy.Core.Models;

namespace AcKrovy.Localization;

public static class TimberElementTypeDisplayNameProvider
{
    public static string GetDisplayName(TimberElementType type, CultureInfo? culture = null) =>
        type switch
        {
            TimberElementType.Rafter => UiStrings.GetString("ElementType_Rafter", culture),
            TimberElementType.WallPlate => UiStrings.GetString("ElementType_WallPlate", culture),
            TimberElementType.Purlin => UiStrings.GetString("ElementType_Purlin", culture),
            TimberElementType.Post => UiStrings.GetString("ElementType_Post", culture),
            TimberElementType.CollarTie => UiStrings.GetString("ElementType_CollarTie", culture),
            TimberElementType.Brace => UiStrings.GetString("ElementType_Brace", culture),
            TimberElementType.TieBeam => UiStrings.GetString("ElementType_TieBeam", culture),
            TimberElementType.Custom => UiStrings.GetString("ElementType_Custom", culture),
            TimberElementType.HipRafter => UiStrings.GetString("ElementType_HipRafter", culture),
            TimberElementType.ValleyRafter => UiStrings.GetString("ElementType_ValleyRafter", culture),
            _ => type.ToString(),
        };
}
