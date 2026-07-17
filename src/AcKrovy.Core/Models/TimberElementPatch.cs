namespace AcKrovy.Core.Models;

/// <summary>Čiastočná zmena údajov prvku. Null znamená „ponechať pôvodnú hodnotu“.</summary>
public sealed record TimberElementPatch(
    TimberElementType? ElementType,
    double? WidthMm,
    double? HeightMm,
    double? SlopeDegrees,
    string? RoofPlaneId,
    double? CuttingAllowanceMm,
    LengthCalculationMode? LengthCalculationMode,
    double? ManualLengthMm,
    string? Material,
    string? Note);
