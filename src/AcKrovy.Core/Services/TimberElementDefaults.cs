using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Pracovné prednastavenia zobrazené po kliknutí na ikonku typu prvku.
/// Nie sú to statické odporúčania. Používateľ ich pred potvrdením v dialógu upraví
/// podľa konkrétneho projektu a statiky.
/// </summary>
public static class TimberElementDefaults
{
    public static TimberElementData For(TimberElementType type)
    {
        var common = new TimberElementData
        {
            ElementType = type,
            RoofPlaneId = "R1",
            CuttingAllowanceMm = 100,
            Material = "Smrek C24",
            LengthCalculationMode = LengthCalculationMode.AutoByElementType,
        };

        return type switch
        {
            TimberElementType.Rafter => common with
            {
                WidthMm = 80,
                HeightMm = 160,
                SlopeDegrees = 35,
            },
            TimberElementType.WallPlate => common with
            {
                WidthMm = 140,
                HeightMm = 140,
                SlopeDegrees = 0,
            },
            TimberElementType.Purlin => common with
            {
                WidthMm = 160,
                HeightMm = 220,
                SlopeDegrees = 0,
            },
            TimberElementType.Post => common with
            {
                WidthMm = 140,
                HeightMm = 140,
                SlopeDegrees = 0,
                ManualLengthMm = 2500,
            },
            TimberElementType.CollarTie => common with
            {
                WidthMm = 80,
                HeightMm = 180,
                SlopeDegrees = 0,
            },
            TimberElementType.Brace => common with
            {
                WidthMm = 80,
                HeightMm = 120,
                SlopeDegrees = 35,
            },
            TimberElementType.TieBeam => common with
            {
                WidthMm = 160,
                HeightMm = 200,
                SlopeDegrees = 0,
            },
            _ => common,
        };
    }
}
