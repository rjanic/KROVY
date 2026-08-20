namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// DEBUG/test details for a failed canonical→observed rigid compose.
/// Not persisted; host maps this to <c>ROOF_MANUAL_EDIT_COMPOSE_FAIL</c>.
/// </summary>
public sealed record RoofGeneratedMemberComposeFailure(
    string Stage,
    string Reason,
    RoofGeneratedMemberGeometry Canonical,
    RoofGeneratedMemberGeometry Observed,
    double ExistingRotationRadians,
    double ExistingAlongMm,
    double ExistingLateralMm,
    double ExistingStartOffsetMm,
    double ExistingEndOffsetMm,
    double CandidateRotationRadians,
    double CandidateAlongMm,
    double CandidateLateralMm,
    RoofGeneratedMemberGeometry Replay,
    bool HasReplay,
    double MaxErrorMm);
