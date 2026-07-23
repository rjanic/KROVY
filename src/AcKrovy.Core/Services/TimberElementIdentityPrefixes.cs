using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementIdentityPrefixes
{
    public static string GetPrefix(TimberElementType type) => type switch
    {
        TimberElementType.Rafter => "K",
        TimberElementType.WallPlate => "P",
        TimberElementType.Purlin => "V",
        TimberElementType.Post => "S",
        TimberElementType.CollarTie => "KL",
        TimberElementType.Brace => "W",
        TimberElementType.TieBeam => "VT",
        TimberElementType.Custom => throw new InvalidOperationException(
            "Custom element prefix must be resolved from its persistent definition."),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
