using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>One candidate Generated anchor line available for COPY re-anchoring.</summary>
public sealed record RoofReanchorCandidate(
    RoofGeneratedMemberKey Key,
    RoofPoint3D Start,
    RoofPoint3D End);

/// <summary>
/// COPY/MOVE retain the exact source frame. Legacy nearest and MIRROR selection
/// remain separate policies; preserving Plan endpoints alone does not prove that
/// a change of anchor preserves the physical body's elevation/section frame.
/// </summary>
public static class RoofAttachedManualReanchorRules
{
    /// <summary>COPY/MOVE retain semantic provenance even when another frame is
    /// closer. Missing/degenerate provenance is not permission to change the body's
    /// physical frame; callers must use their existing recovery/detach policy.</summary>
    public static RoofReanchorCandidate? SelectRetainedAnchor(
        RoofGeneratedMemberKey sourceKey,
        IReadOnlyList<RoofReanchorCandidate> candidates,
        RoofPoint3D childStart,
        RoofPoint3D childEnd) => candidates.FirstOrDefault(candidate =>
            candidate.Key == sourceKey &&
            RoofAttachedManualRelativeGeometryRules.TryCapture(candidate.Start, candidate.End,
                childStart, childEnd, out var relative) &&
            new[] { relative.U0Mm, relative.V0Mm, relative.W0Mm,
                relative.U1Mm, relative.V1Mm, relative.W1Mm }.All(value =>
                    !double.IsNaN(value) && !double.IsInfinity(value)));

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
