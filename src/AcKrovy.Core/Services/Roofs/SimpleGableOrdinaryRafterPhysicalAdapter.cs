using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Builds a <see cref="RoofFaceRafterLayout"/> from existing SimpleGable Plan2D
/// ordinary rafters (Face0 + Face1) so <see cref="RoofAutomaticRafterPhysicalBuilder"/>
/// can lift them without regenerating stations or inventing Structural Hip/Valley.
/// </summary>
public static class SimpleGableOrdinaryRafterPhysicalAdapter
{
    private const double Tolerance = SimpleGableRoofGeometryTolerance.CoordinateToleranceMm;

    public static bool TryCreateFaceLayout(
        SimpleGableRoofGeometry geometry,
        RoofTopology topology,
        RoofRafterLayout generatedLayout,
        out RoofFaceRafterLayout faceLayout,
        out string failureReason)
    {
        faceLayout = null!;
        failureReason = "InvalidSimpleGablePhysicalLayout";
        if (geometry is null || topology is null || generatedLayout is null)
        {
            throw new ArgumentNullException(geometry is null ? nameof(geometry) :
                topology is null ? nameof(topology) : nameof(generatedLayout));
        }

        if (geometry.Kind != RoofKind.SimpleGable ||
            topology.Faces.Count != 2 ||
            generatedLayout.Rafters.Count == 0)
        {
            failureReason = "UnsupportedGablePhysicalInput";
            return false;
        }

        var eaveEdgeIndexByFace = new Dictionary<int, int>();
        for (var edgeIndex = 0; edgeIndex < topology.Edges.Count; edgeIndex++)
        {
            var edge = topology.Edges[edgeIndex];
            if (edge.Kind == RoofTopologyEdgeKind.Eave && edge.FaceIndices.Count == 1)
                eaveEdgeIndexByFace[edge.FaceIndices[0]] = edgeIndex;
        }
        if (eaveEdgeIndexByFace.Count != 2 ||
            !eaveEdgeIndexByFace.ContainsKey(0) ||
            !eaveEdgeIndexByFace.ContainsKey(1))
        {
            failureReason = "MissingGableEaveEdges";
            return false;
        }

        var segments = new List<RoofFaceRafterSegment>(generatedLayout.Rafters.Count);
        var seen = new HashSet<(RafterRoofFace Face, int Station)>();
        foreach (var rafter in generatedLayout.Rafters.OrderBy(item => item.Face)
                     .ThenBy(item => item.StationIndex))
        {
            if (rafter.Face is not (RafterRoofFace.Face0 or RafterRoofFace.Face1) ||
                !seen.Add((rafter.Face, rafter.StationIndex)))
            {
                failureReason = "DuplicateOrUnsupportedGableRafterFace";
                return false;
            }

            var sourceFaceIndex = rafter.Face == RafterRoofFace.Face0 ? 0 : 1;
            var eaveEdgeIndex = eaveEdgeIndexByFace[sourceFaceIndex];
            var planStart = rafter.PlanStart;
            var planEnd = rafter.PlanEnd;
            var planLength = planStart.DistanceTo(planEnd);
            if (!IsFinite(planLength) || planLength <= Tolerance)
            {
                failureReason = "DegenerateGableRafterPlan";
                return false;
            }

            segments.Add(new RoofFaceRafterSegment(
                sourceFaceIndex,
                eaveEdgeIndex,
                rafter.StationIndex,
                StationIntervalIndex: rafter.StationIndex,
                rafter.StationPositionMm,
                planStart,
                planEnd,
                RoofRafterBoundaryRole.Eave,
                RoofRafterBoundaryRole.Ridge,
                planLength));
        }

        if (segments.Count != generatedLayout.Rafters.Count)
        {
            failureReason = "GableSegmentCountMismatch";
            return false;
        }

        faceLayout = new RoofFaceRafterLayout(
            generatedLayout.RequestedMaximumSpacingMm,
            segments,
            generatedLayout.Signature);
        failureReason = string.Empty;
        return true;
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
