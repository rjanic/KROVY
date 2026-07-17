using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementLabels
{
    public static string ToSlovak(TimberElementType type) => type switch
    {
        TimberElementType.Rafter => "Krokva",
        TimberElementType.WallPlate => "Pomúrnica",
        TimberElementType.Purlin => "Väznica",
        TimberElementType.Post => "Stĺpik",
        TimberElementType.CollarTie => "Klieština / hambálok",
        TimberElementType.Brace => "Vzpera",
        TimberElementType.TieBeam => "Väzný trám",
        _ => type.ToString(),
    };

    public static string Prefix(TimberElementType type) => type switch
    {
        TimberElementType.Rafter => "K",
        TimberElementType.WallPlate => "P",
        TimberElementType.Purlin => "V",
        TimberElementType.Post => "S",
        TimberElementType.CollarTie => "KL",
        TimberElementType.Brace => "W",
        TimberElementType.TieBeam => "VT",
        _ => "X",
    };
}
