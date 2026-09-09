using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Adapts CAD-neutral Hip face-rafter layout into the existing generated-rafter
/// materialization model without inventing a second timber persistence schema.
/// Face identity is flattened to Face0 with sequential station indices so schema-1
/// generated metadata remains valid; the authoritative geometry signature stays the
/// R1 <see cref="RoofFaceRafterLayout.Signature"/>.
/// </summary>
public static class RoofFaceRafterMaterializationAdapter
{
    public static bool TryCreateMaterializationLayout(
        HipRoofGeometry geometry,
        RoofFaceRafterLayout faceLayout,
        double rafterPlanWidthMm,
        out RoofRafterLayout layout)
    {
        layout = null!;
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }
        if (faceLayout is null)
        {
            throw new ArgumentNullException(nameof(faceLayout));
        }
        if (!IsFinite(rafterPlanWidthMm) || rafterPlanWidthMm <= 0d)
        {
            return false;
        }
        if (faceLayout.Segments.Count < 2 ||
            !IsFinite(faceLayout.RequestedSpacingMm) ||
            faceLayout.RequestedSpacingMm <= 0d)
        {
            return false;
        }

        var facesBySourceEdge = geometry.Faces.ToDictionary(face => face.SourceEdgeIndex);
        var rafters = new List<RoofRafterGeometry>(faceLayout.Segments.Count);
        for (var index = 0; index < faceLayout.Segments.Count; index++)
        {
            var segment = faceLayout.Segments[index];
            if (!facesBySourceEdge.TryGetValue(segment.SourceFaceIndex, out var face))
            {
                return false;
            }

            var planLength = segment.PlanStart.DistanceTo(segment.PlanEnd);
            if (!IsFinite(planLength) ||
                planLength <= RoofFaceRafterLayoutService.CoordinateToleranceMm ||
                !NearlyEqual(planLength, segment.PlanLengthMm))
            {
                return false;
            }

            var slopeDegrees = face.SlopeDegrees;
            if (!IsFinite(slopeDegrees) || Math.Abs(slopeDegrees) >= 90d)
            {
                return false;
            }

            var runX = segment.PlanEnd.X - segment.PlanStart.X;
            var runY = segment.PlanEnd.Y - segment.PlanStart.Y;
            if (!RoofDirection2D.TryCreate(runX, runY, out var runDirection))
            {
                return false;
            }

            var slopeRadians = slopeDegrees * Math.PI / 180d;
            var trueLength = planLength / Math.Cos(slopeRadians);
            if (!IsFinite(trueLength) || trueLength <= 0d)
            {
                return false;
            }

            rafters.Add(new RoofRafterGeometry(
                RafterRoofFace.Face0,
                index,
                faceLayout.Segments.Count,
                index / Math.Max(1d, faceLayout.Segments.Count - 1d),
                segment.StationDistanceMm,
                segment.PlanStart,
                segment.PlanEnd,
                runDirection,
                planLength,
                trueLength,
                slopeDegrees));
        }

        if (!RoofDirection2D.TryCreate(1d, 0d, out var stationDirection))
        {
            return false;
        }

        var first = geometry.Faces[0].Eave;
        var plane = new RoofRafterPlane(
            RafterRoofFace.Face0,
            first,
            first,
            geometry.PrimarySlopeDegrees);
        var stationCount = rafters.Count;
        layout = new RoofRafterLayout(
            faceLayout.RequestedSpacingMm,
            rafterPlanWidthMm,
            Math.Max(faceLayout.RequestedSpacingMm, (stationCount - 1d) * faceLayout.RequestedSpacingMm),
            Math.Max(faceLayout.RequestedSpacingMm, (stationCount - 1d) * faceLayout.RequestedSpacingMm),
            Math.Max(1, stationCount - 1),
            stationCount,
            faceLayout.RequestedSpacingMm,
            stationDirection,
            [plane],
            rafters,
            faceLayout.Signature,
            RoofRafterDomainPolygon.FromHip(geometry));
        return true;
    }

    private static bool NearlyEqual(double first, double second)
    {
        var scale = Math.Max(1d, Math.Max(Math.Abs(first), Math.Abs(second)));
        return Math.Abs(first - second) <=
               RoofFaceRafterLayoutService.CoordinateToleranceMm * scale;
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
