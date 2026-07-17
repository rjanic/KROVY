namespace AcKrovy.Core.Models;

/// <summary>Spôsob určenia reálnej dĺžky prvku z 2D geometrie.</summary>
public enum LengthCalculationMode
{
    /// <summary>Vyberie predvolené správanie podľa typu prvku.</summary>
    AutoByElementType,

    /// <summary>Použije dĺžku čiary/polyline priamo z pôdorysu.</summary>
    PlanLength,

    /// <summary>Predĺži pôdorysnú projekciu podľa zadaného sklonu strešnej roviny.</summary>
    SlopeCorrected,

    /// <summary>Použije ručne zadanú skutočnú dĺžku, napríklad pri stĺpiku.</summary>
    ManualLength,
}
