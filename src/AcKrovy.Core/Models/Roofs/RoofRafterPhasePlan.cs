namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Topology-driven Ridge phase-constraint resolution snapshot.
/// </summary>
public sealed record RoofRafterPhasePlan(
    IReadOnlyList<RoofRafterPhaseComponent> Components,
    IReadOnlyList<RoofRafterFacePhaseDecision> FaceDecisions)
{
    public static RoofRafterPhasePlan Empty { get; } = new(
        Array.Empty<RoofRafterPhaseComponent>(),
        Array.Empty<RoofRafterFacePhaseDecision>());
}

/// <summary>
/// One phase-coupled Ridge family after constraint resolution.
/// </summary>
public sealed record RoofRafterPhaseComponent(
    int ComponentId,
    IReadOnlyList<int> RidgeEdgeIds,
    double StationAxisX,
    double StationAxisY,
    double CanonicalPhaseT,
    IReadOnlyList<int> IncidentFaceIds,
    IReadOnlyList<string> CouplingReasons,
    bool MembersCollinear,
    double OffsetDistanceMm,
    IReadOnlyList<int> FacesConsumingPhase);

/// <summary>
/// Per-face station-phase decision after Ridge family assignment.
/// </summary>
public sealed record RoofRafterFacePhaseDecision(
    int FaceId,
    double StationAxisX,
    double StationAxisY,
    IReadOnlyList<int> RidgeBoundaryIds,
    IReadOnlyList<double> CandidatePhases,
    double? SelectedPhaseT,
    bool FallbackUsed,
    string FallbackReason);
