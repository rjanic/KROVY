using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Physical lower-end half-spaces shared by the boundary model and CAD construction.</summary>
internal static class RoofStructuralLowerEndProfile
{
    private const double Tolerance = 1e-6;

    internal static bool TryResolve(
        RoofSegment3D upperAxis,
        double physicalEaveElevationMm,
        double physicalVerticalHeightMm,
        LowerEndCutMode mode,
        IEnumerable<RoofAutomaticRafterPhysicalMember> ordinaryContacts,
        out IReadOnlyList<RoofStructuralRafterClipPlane> planes)
    {
        planes = Array.Empty<RoofStructuralRafterClipPlane>();
        if (!Enum.IsDefined(typeof(LowerEndCutMode), mode) ||
            !Finite(physicalEaveElevationMm) ||
            !Finite(physicalVerticalHeightMm) ||
            physicalVerticalHeightMm <= Tolerance) return false;

        var dx = upperAxis.End.X - upperAxis.Start.X;
        var dy = upperAxis.End.Y - upperAxis.Start.Y;
        var dz = upperAxis.End.Z - upperAxis.Start.Z;
        var planLength = Math.Sqrt(dx * dx + dy * dy);
        var trueLength = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (!Finite(planLength) || planLength <= Tolerance ||
            !Finite(trueLength) || trueLength <= Tolerance) return false;

        if (mode == LowerEndCutMode.Horizontal)
        {
            // For Hip, upperAxis is the physical top-face midpoint axis: its
            // eave Z is below the canonical roof-plane intersection. For
            // Valley the existing axis still begins at the roof eave Z.
            // Anchoring the horizontal trim here preserves the Hip eave
            // corner in plan without changing the WCS-horizontal cut normal.
            planes = [new RoofStructuralRafterClipPlane(
                new RoofPoint3D(upperAxis.Start.X, upperAxis.Start.Y,
                    upperAxis.Start.Z), new RoofPoint3D(0d, 0d, 1d))];
            return true;
        }

        var normal = mode == LowerEndCutMode.Vertical
            ? new RoofPoint3D(dx / planLength, dy / planLength, 0d)
            : new RoofPoint3D(dx / trueLength, dy / trueLength, dz / trueLength);
        var sideX = -dy / planLength;
        var sideY = dx / planLength;
        var candidates = new List<(int Side, double Station, RoofSegment3D Edge,
            RoofAutomaticRafterPhysicalMember Member)>();
        foreach (var member in ordinaryContacts)
        {
            var cut = member.StructuralCut;
            if (cut?.LowerContactEdge is not { } edge ||
                !Finite(edge.Start) || !Finite(edge.End) ||
                edge.Start.DistanceTo(edge.End) <= Tolerance ||
                member.SolidVertices is not { Count: >= 4 } ||
                member.SolidVertices.Any(point => !Finite(point))) return false;
            var offset = (cut.PlanePoint.X - upperAxis.Start.X) * sideX +
                (cut.PlanePoint.Y - upperAxis.Start.Y) * sideY;
            if (!Finite(offset) || Math.Abs(offset) <= Tolerance) return false;
            var midX = (edge.Start.X + edge.End.X) / 2d - upperAxis.Start.X;
            var midY = (edge.Start.Y + edge.End.Y) / 2d - upperAxis.Start.Y;
            var station = (midX * dx + midY * dy) / planLength;
            if (!Finite(station)) return false;
            candidates.Add((Math.Sign(offset), station, edge, member));
        }
        if (candidates.Count == 0) return false;

        // Only the eave-nearest real contact on each structural side defines
        // the lower end. The edge is the bottom-face/cut-face intersection,
        // never an arbitrary low vertex from the whole cut-face polygon.
        var nearest = candidates.GroupBy(candidate => candidate.Side)
            .SelectMany(group =>
            {
                var first = group.Min(candidate => candidate.Station);
                return group.Where(candidate =>
                    candidate.Station <= first + Tolerance);
            }).ToArray();
        // The horizontal trim follows the lowest point of the entire final
        // ordinary solid, not the structural-contact edge used for the end
        // face. These are different physical references.
        var ordinaryLowestZ = nearest.SelectMany(candidate =>
            candidate.Member.SolidVertices).Min(point => point.Z);
        // The end-face orientation follows the cut mode, but its longitudinal
        // anchor is the canonical eave corner. Neither the ordinary contact
        // nor the horizontal bottom elevation is allowed to shorten the Hip.
        // At the corner the two eave boundaries may reduce this face to an
        // edge/point; the eave planes still own the physical plan outline.
        planes =
        [
            new RoofStructuralRafterClipPlane(upperAxis.Start, normal),
            new RoofStructuralRafterClipPlane(
                new RoofPoint3D(upperAxis.Start.X, upperAxis.Start.Y,
                    ordinaryLowestZ), new RoofPoint3D(0d, 0d, 1d)),
        ];
        return true;
    }

    private static bool Finite(RoofPoint3D point) =>
        Finite(point.X) && Finite(point.Y) && Finite(point.Z);
    private static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
