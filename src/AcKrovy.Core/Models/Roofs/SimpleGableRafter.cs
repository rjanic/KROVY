namespace AcKrovy.Core.Models.Roofs;

public sealed record SimpleGableRafter(
    RafterRoofFace Face,
    int StationIndex,
    int StationCount,
    double StationFraction,
    RoofPoint2D PlanStart,
    RoofPoint2D PlanEnd,
    double SlopeDegrees);
