namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Right-handed local basis of one generated member's working plane.
/// AxisU is along the canonical member, AxisV in-plane perpendicular, AxisW the plane normal.
/// Does not assume a centered ridge or equal pitches.
/// </summary>
public readonly record struct RoofPlaneBasis(
    RoofPoint3D Origin,
    RoofPoint3D AxisU,
    RoofPoint3D AxisV,
    RoofPoint3D AxisW);
