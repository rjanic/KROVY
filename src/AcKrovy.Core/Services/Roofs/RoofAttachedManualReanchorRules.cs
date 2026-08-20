using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>One candidate Generated anchor line available for COPY re-anchoring.</summary>
public sealed record RoofReanchorCandidate(
    RoofGeneratedMemberKey Key,
    RoofPoint3D Start,
    RoofPoint3D End);

/// <summary>
/// Selects the Generated station a moved COPY-origin AttachedManual child now
/// logically belongs to. The child may sit between two stations; the chosen anchor
/// is only the reference frame — the child's exact WCS geometry is preserved via a
/// RelativeSegment recomputed against that anchor (never snapped onto it).
/// </summary>
public static class RoofAttachedManualReanchorRules
{
    public static RoofReanchorCandidate? SelectNearestAnchor(
        RoofGeneratedMemberKey currentAnchorKey,
        IReadOnlyList<RoofReanchorCandidate> candidates,
        RoofPoint3D childStart,
        RoofPoint3D childEnd)
    {
        RoofReanchorCandidate? best = null;
        var bestAbsV = double.PositiveInfinity;

        foreach (var candidate in candidates)
        {
            if (candidate.Key.MemberKind != currentAnchorKey.MemberKind ||
                candidate.Key.RoofFace != currentAnchorKey.RoofFace)
            {
                continue;
            }

            if (!RoofAttachedManualRelativeGeometryRules.TryCapture(
                    candidate.Start,
                    candidate.End,
                    childStart,
                    childEnd,
                    out var relative))
            {
                continue;
            }

            // Station direction is the anchor basis V axis (lateral, in-plane,
            // perpendicular to the member). The child's signed lateral offset from a
            // candidate is its midpoint V coordinate; the nearest station minimizes it.
            var midV = (relative.V0Mm + relative.V1Mm) / 2d;
            var absV = Math.Abs(midV);

            if (best is null ||
                absV < bestAbsV - RoofGeneratedMemberOverrideMath.LengthToleranceMm ||
                (Math.Abs(absV - bestAbsV) <= RoofGeneratedMemberOverrideMath.LengthToleranceMm &&
                 candidate.Key.StationIndex < best.Key.StationIndex))
            {
                best = candidate;
                bestAbsV = absV;
            }
        }

        return best;
    }

    /// <summary>
    /// Selects the Generated station a MIRROR-produced clone now belongs to, without
    /// assuming the source face survived the mirror. Face is recovered from the clone's
    /// own orientation: a compatible anchor is one whose U axis points the same way the
    /// clone spans (eave → ridge). A negative U span means the candidate is on the
    /// opposite face and is rejected. Station distance still uses the lateral V axis.
    /// </summary>
    public static RoofReanchorCandidate? SelectNearestMirrorAnchor(
        RoofGeneratedTimberKind memberKind,
        IReadOnlyList<RoofReanchorCandidate> candidates,
        RoofPoint3D childStart,
        RoofPoint3D childEnd)
    {
        RoofReanchorCandidate? best = null;
        var bestAbsV = double.PositiveInfinity;

        foreach (var candidate in candidates)
        {
            if (candidate.Key.MemberKind != memberKind)
            {
                continue;
            }

            if (!RoofAttachedManualRelativeGeometryRules.TryCapture(
                    candidate.Start,
                    candidate.End,
                    childStart,
                    childEnd,
                    out var relative))
            {
                continue;
            }

            // Face/orientation compatibility: the mirrored child must span positively
            // along the candidate's U axis (eave → ridge). A non-positive span means
            // the candidate belongs to the opposite roof face.
            if (relative.U1Mm <= relative.U0Mm)
            {
                continue;
            }

            var midV = (relative.V0Mm + relative.V1Mm) / 2d;
            var absV = Math.Abs(midV);

            if (best is null ||
                absV < bestAbsV - RoofGeneratedMemberOverrideMath.LengthToleranceMm ||
                (Math.Abs(absV - bestAbsV) <= RoofGeneratedMemberOverrideMath.LengthToleranceMm &&
                 candidate.Key.StationIndex < best.Key.StationIndex))
            {
                best = candidate;
                bestAbsV = absV;
            }
        }

        return best;
    }
}
