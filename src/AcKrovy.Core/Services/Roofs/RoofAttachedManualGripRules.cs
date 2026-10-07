using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public sealed record RoofAttachedManualGripAcceptance(
    RoofAttachedManualTimberData Metadata,
    RoofOrdinaryFreeformGripAcceptance Grip)
{
    public RoofGeneratedMemberGeometry Geometry => Grip.Geometry;
    public string Endpoint => Grip.Endpoint;
}

/// <summary>Ordinary Copy children share Generated freeform XY acceptance,
/// retaining their identity/anchor and rebuilding the edited roof-plane shape.</summary>
public static class RoofAttachedManualGripRules
{
    public static bool TryAccept(
        RoofAttachedManualTimberData data,
        RoofPoint3D anchorStart,
        RoofPoint3D anchorEnd,
        RoofGeneratedMemberGeometry before,
        RoofGeneratedMemberGeometry native,
        out RoofAttachedManualGripAcceptance? acceptance)
    {
        acceptance = null;
        if (data.Origin != RoofAttachedManualOrigin.Copy ||
            data.AnchorGeneratedMemberKey?.MemberKind != RoofGeneratedTimberKind.Rafter ||
            data.RelativeSegment is null ||
            !RoofOrdinaryFreeformGripRules.TryAccept(before, native, out var grip, out _) || grip is null ||
            !RoofAttachedManualRelativeGeometryRules.TryCapture(
                anchorStart, anchorEnd, grip.Geometry.Start, grip.Geometry.End, out var relative))
            return false;

        // A shape edit is rebuilt from its accepted current XY on the roof plane.
        // Later COPY/MOVE can carry this new reference through their existing paths.
        // An actual no-op retains the prior carried physical placement.
        var reference = RoofGeneratedMemberOverrideMath.GeometryEquals(before, grip.Geometry)
            ? data.PhysicalReferenceSegment : relative;
        acceptance = new(data with
        {
            RelativeSegment = relative,
            PhysicalReferenceSegment = reference,
        }, grip);
        return true;
    }
}
