using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public sealed record RoofOrdinaryFreeformGripAcceptance(
    RoofGeneratedMemberGeometry Before, RoofGeneratedMemberGeometry Native,
    RoofGeneratedMemberGeometry Geometry, string Endpoint, RoofPoint3D AxisBefore,
    RoofPoint3D AxisAfter, double PlanYawDeltaDegrees, double MinimumLengthMm, bool Clamped);

/// <summary>Native endpoint XY is authoritative. Only the longitudinal gap is
/// clamped when needed for minimum length or directed endpoint semantics.</summary>
public static class RoofOrdinaryFreeformGripRules
{
    public static bool TryAccept(RoofGeneratedMemberGeometry before, RoofGeneratedMemberGeometry native,
        out RoofOrdinaryFreeformGripAcceptance? acceptance, out RoofGeneratedMemberManualEditReason reason)
    {
        acceptance = null;
        reason = RoofGeneratedMemberManualEditReason.BasisFailed;
        if (!Finite(native.Start) || !Finite(native.End) ||
            before.Start.Z != 0 || before.End.Z != 0 ||
            !RoofGeneratedMemberOverrideMath.TryCreateBasis(before,
                RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, out var basis)) return false;
        var xy = new RoofGeneratedMemberGeometry(new(native.Start.X, native.Start.Y, 0), new(native.End.X, native.End.Y, 0));
        var startMoved = xy.Start.DistanceTo(before.Start) > RoofGeneratedMemberOverrideMath.LengthToleranceMm;
        var endMoved = xy.End.DistanceTo(before.End) > RoofGeneratedMemberOverrideMath.LengthToleranceMm;
        if (startMoved && endMoved) { reason = RoofGeneratedMemberManualEditReason.BothEndpointsChanged; return false; }
        var minimum = Math.Min(before.LengthMm, RoofRafterLengthRules.DefaultMinimumAutomaticLengthMm);
        if (!startMoved && !endMoved)
        {
            reason = RoofGeneratedMemberManualEditReason.NeitherEndpointChanged;
            acceptance = new(before, native, before, "None", basis.AxisU, basis.AxisU, 0, minimum, false);
            return true;
        }
        var delta = startMoved ? Subtract(xy.Start, before.Start) : Subtract(xy.End, before.End);
        var along = Dot(delta, basis.AxisU);
        var lateral = Dot(delta, basis.AxisV);
        var gap = before.LengthMm + (startMoved ? -along : along);
        // Retain lateral XY. Even a large lateral length must have a positive
        // longitudinal gap so Start/End never silently exchange meaning.
        var minimumGap = Math.Max(2 * RoofGeneratedMemberOverrideMath.LengthToleranceMm,
            Math.Sqrt(Math.Max(0, minimum * minimum - lateral * lateral)));
        var clamped = gap < minimumGap;
        var accepted = startMoved ? new RoofGeneratedMemberGeometry(xy.Start, before.End) : new(before.Start, xy.End);
        if (clamped)
        {
            var correction = (startMoved ? -1 : 1) * (minimumGap - gap);
            var point = startMoved ? accepted.Start : accepted.End;
            point = new(point.X + correction * basis.AxisU.X, point.Y + correction * basis.AxisU.Y, 0);
            accepted = startMoved ? accepted with { Start = point } : accepted with { End = point };
        }
        if (!Finite(accepted.Start) || !Finite(accepted.End) ||
            !RoofGeneratedMemberOverrideMath.TryCreateBasis(accepted,
                RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, out var afterBasis)) return false;
        var yaw = Math.Atan2(basis.AxisU.X * afterBasis.AxisU.Y - basis.AxisU.Y * afterBasis.AxisU.X,
            Dot(basis.AxisU, afterBasis.AxisU)) * 180 / Math.PI;
        reason = RoofGeneratedMemberOverrideMath.GeometryEquals(before, accepted)
            ? RoofGeneratedMemberManualEditReason.NeitherEndpointChanged : RoofGeneratedMemberManualEditReason.Accepted;
        acceptance = new(before, native, accepted, startMoved ? "Start" : "End", basis.AxisU,
            afterBasis.AxisU, yaw, minimum, clamped);
        return true;
    }

    public static bool TryComposeGenerated(RoofGeneratedMemberGeometry canonical,
        RoofOrdinaryFreeformGripAcceptance grip, RoofGeneratedMemberKey key,
        RoofGeneratedMemberOverride? existing, string? elementId, out RoofGeneratedMemberOverride? edit)
    {
        edit = null;
        if (key.MemberKind != RoofGeneratedTimberKind.Rafter || existing is { Suppressed: true } ||
            !RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical,
                RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, out var basis) ||
            !RoofAttachedManualRelativeGeometryRules.TryCapture(canonical.Start, canonical.End,
                grip.Geometry.Start, grip.Geometry.End, out var reference)) return false;
        var axis = grip.AxisAfter;
        var rotation = Math.Atan2(basis.AxisU.X * axis.Y - basis.AxisU.Y * axis.X, Dot(basis.AxisU, axis));
        var translation = Subtract(grip.Geometry.Start, canonical.Start);
        edit = RoofGeneratedMemberOverrideMath.Normalize(new(key, false,
            Dot(translation, basis.AxisU), Dot(translation, basis.AxisV), rotation,
            0, grip.Geometry.LengthMm - canonical.LengthMm, existing?.ReservedElementId ?? elementId)
        { PhysicalReferenceSegment = reference });
        return RoofGeneratedMemberOverrideMath.TryApply(canonical,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, edit, out var replayed) &&
            RoofGeneratedMemberOverrideMath.GeometryEquals(replayed, grip.Geometry);
    }

    private static RoofPoint3D Subtract(RoofPoint3D a, RoofPoint3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static double Dot(RoofPoint3D a, RoofPoint3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static bool Finite(RoofPoint3D p) => !double.IsNaN(p.X) && !double.IsInfinity(p.X) &&
        !double.IsNaN(p.Y) && !double.IsInfinity(p.Y) && !double.IsNaN(p.Z) && !double.IsInfinity(p.Z);
}
