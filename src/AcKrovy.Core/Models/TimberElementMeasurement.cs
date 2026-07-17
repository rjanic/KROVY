namespace AcKrovy.Core.Models;

/// <summary>Údaje jedného prvku spolu s aktuálne načítanou dĺžkou z AutoCAD geometrie.</summary>
public sealed record TimberElementMeasurement(
    TimberElementData Data,
    double PlanLengthMm,
    double ActualLengthMm,
    double CuttingLengthMm,
    double VolumeM3);
