namespace AcKrovy.Core.Models;

/// <summary>
/// Trvalé údaje uložené priamo pri grafickom objekte v DWG.
/// Dĺžka z geometrie sa neukladá ako zdroj pravdy: vždy sa prepočíta z aktuálnej čiary.
/// </summary>
public sealed record TimberElementData
{
    public int SchemaVersion { get; init; }
    public string ElementId { get; init; } = string.Empty;
    public TimberElementType ElementType { get; init; } = TimberElementType.Rafter;
    public double WidthMm { get; init; } = 80;
    public double HeightMm { get; init; } = 160;
    public int? FootprintWidthEdgeIndex { get; init; }
    public double SlopeDegrees { get; init; } = 35;
    public bool IsSlopeDirectionReversed { get; init; }
    public string RoofPlaneId { get; init; } = "R1";
    public double CuttingAllowanceMm { get; init; } = TimberElementDefaultProfile.FactoryCuttingAllowanceMm;
    public LengthCalculationMode LengthCalculationMode { get; init; } = LengthCalculationMode.AutoByElementType;
    public double? ManualLengthMm { get; init; }
    public string Material { get; init; } = "Smrek C24";
    public string Note { get; init; } = string.Empty;
}
