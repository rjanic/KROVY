namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Per-face ordinary-rafter complete-coverage diagnostic against the full face
/// projection on the station axis (independent of Ridge-component extent).
/// </summary>
public sealed record RoofFaceRafterFaceCoverageReport(
    int FaceId,
    double StationAxisX,
    double StationAxisY,
    double FullFaceMinT,
    double FullFaceMaxT,
    double? RidgeFamilyMinT,
    double? RidgeFamilyMaxT,
    double EnumerationMinT,
    double EnumerationMaxT,
    double PhaseT,
    double? FirstGeneratedStationT,
    double? LastGeneratedStationT,
    int StationCount,
    int EmittedSegmentCount,
    double MinPlanLengthMm,
    double MaxPlanLengthMm,
    string FirstTerminationRoles,
    string LastTerminationRoles,
    double UncoveredStartMm,
    double UncoveredEndMm,
    RoofFaceRafterFaceCoverageResult Result);

public enum RoofFaceRafterFaceCoverageResult
{
    Pass = 0,
    FailUncoveredStart = 1,
    FailUncoveredEnd = 2,
    FailMissingLatticeStation = 3,
    FailEmptyFace = 4,
}
