namespace AcKrovy.Core.Models;

/// <summary>Údaje prvku spolu s neutrálnou dĺžkou načítanou z CAD geometrie.</summary>
public sealed record TimberElementSnapshot(
    TimberElementData Data,
    double PlanLengthMm);
