namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Canonical elevation state for one structural member. Schema v1.
///
/// Canonical representation: two independent axis endpoint elevations (at Line.StartPoint
/// and Line.EndPoint). Slope is always derived, never persisted.
/// DisplayReference controls which datum is shown in the UI (SH / OS / VH).
/// Changing DisplayReference alone does not move the physical member.
///
/// Plan2D Z remains zero. The elevation state lives beside the Plan2D Line.
///
/// This is the foundation for KROVY 2.5D: spatial geometry can be derived from
/// Plan2D XY (Z=0) + StructuralMemberElevationState + StructuralMemberSpatialAxisRules.
/// </summary>
public sealed record StructuralMemberElevationState(
    int SchemaVersion,
    StructuralMemberElevationBehavior Behavior,
    /// <summary>Axis (OS/centerline) elevation in mm at Line.StartPoint.</summary>
    double AxisStartElevationMm,
    /// <summary>Axis (OS/centerline) elevation in mm at Line.EndPoint.</summary>
    double AxisEndElevationMm,
    /// <summary>Display datum reference. Changing this does not move the member.</summary>
    StructuralMemberElevationReferenceKind DisplayReference,
    /// <summary>
    /// Which pair of fields is authoritative for Plan2D XY edits (GRIP/STRETCH/LENGTHEN).
    /// Added additively; legacy v1 payloads without the field default to LowerUpper.
    /// </summary>
    StructuralMemberElevationCalculationMode CalculationMode = StructuralMemberElevationCalculationMode.LowerUpper);

/// <summary>Schema constants for StructuralMemberElevationState.</summary>
public static class StructuralMemberElevationStateSchema
{
    /// <summary>Current XRecord schema version (v2 adds CalculationMode).</summary>
    public const int CurrentVersion = 2;

    /// <summary>Legacy payloads without CalculationMode.</summary>
    public const int LegacyVersionWithoutCalculationMode = 1;
}
