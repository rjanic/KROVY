using AcKrovy.Core.Services.Roofs;

namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// CAD-neutral result of the Structural Member Spatial Axis Resolver.
/// Contains enough information for future JOIN, OFFSET, ARRAY, reports, 2.5D,
/// and spatial-axis compatibility checks.
///
/// PlanAxis is always Z=0. SpatialAxis carries the 3D position of the neutral axis (OS).
/// SectionHeightAxisZ is the Z component of the section HeightAxis unit vector — used for
/// SH/OS/VH datum conversions without accessing the full section frame.
/// </summary>
public sealed record StructuralMemberSpatialAxisResult(
    /// <summary>Plan (2D) axis. Always Z=0. Authoritative for XY.</summary>
    RoofSegment3D PlanAxis,

    /// <summary>Spatial (3D) neutral axis. Z is the axis elevation at each endpoint.</summary>
    RoofSegment3D SpatialAxis,

    /// <summary>Unit direction vector along the longitudinal spatial axis (from Start to End).</summary>
    RoofPoint3D DirectionUnit,

    /// <summary>Signed slope in degrees. Positive = rising from Start to End. Range (-90, 90).</summary>
    double SlopeDegrees,

    /// <summary>Z component of the section HeightAxis unit vector. Used for SH/OS/VH conversion.
    /// For a horizontal member this is 1.0. For a 45° rafter this is cos(45°) ≈ 0.707.</summary>
    double SectionHeightAxisZ,

    /// <summary>Section width in mm.</summary>
    double WidthMm,

    /// <summary>Section height in mm.</summary>
    double HeightMm,

    /// <summary>Full section frame, if available from Build State. May be null for legacy members.</summary>
    RoofOrdinarySectionFrame? SectionFrame);
