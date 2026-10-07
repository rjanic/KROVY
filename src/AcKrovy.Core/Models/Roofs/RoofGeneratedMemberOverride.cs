namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Persistent relative override of one logical generated member.
/// Geometry is expressed in the canonical member's roof-plane local basis,
/// not as frozen WCS coordinates.
/// </summary>
public sealed record RoofGeneratedMemberOverride(
    RoofGeneratedMemberKey Key,
    bool Suppressed,
    double AlongMm,
    double LateralMm,
    double RotationRadians,
    double StartOffsetMm,
    double EndOffsetMm,
    string? ReservedElementId = null)
{
    /// <summary>Freeform grip shape in canonical U/V/W, rebuilt on the current
    /// roof plane. A later pure MOVE carries this reference without re-lifting.</summary>
    public RoofAttachedManualRelativeSegment? PhysicalReferenceSegment { get; init; }

    public static RoofGeneratedMemberOverride Suppress(
        RoofGeneratedMemberKey key,
        string? reservedElementId = null) =>
        new(key, true, 0d, 0d, 0d, 0d, 0d, reservedElementId);

    public bool HasGeometryOverride =>
        !Suppressed &&
        (AlongMm != 0d ||
         LateralMm != 0d ||
         RotationRadians != 0d ||
         StartOffsetMm != 0d ||
         EndOffsetMm != 0d);
}
