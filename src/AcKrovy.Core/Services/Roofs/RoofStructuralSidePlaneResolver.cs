using System;
using System.Collections.Generic;
using System.Linq;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Resolves the facing plumb side of the exact Hip/Valley topology edge.</summary>
public static class RoofStructuralSidePlaneResolver
{
    private const double Tolerance = RoofFaceRafterLayoutService.CoordinateToleranceMm;

    /// <summary>
    /// Shared Hip/Valley apex: ordinary endpoint equals the common topology node of
    /// multiple same-role edges. There is no unique structural side plane.
    /// </summary>
    public const string SharedNodeNoSideCutReason = "StructuralSharedNodeNoSideCut";

    private enum ContactKind
    {
        Interior = 0,
        Endpoint = 1,
    }

    public static bool TryResolve(
        RoofTopology topology,
        int sourceFaceIndex,
        RoofRafterBoundaryRole role,
        RoofPoint2D canonicalEndpoint,
        RoofPoint2D ordinaryInteriorPoint,
        IReadOnlyList<RoofStructuralRafterTrimSource> sources,
        out RoofStructuralRafterSideCut? cut,
        out string failureReason)
    {
        cut = null;
        failureReason = "StructuralTargetNotFound";
        if (topology is null || sources is null ||
            role is not (RoofRafterBoundaryRole.Hip or RoofRafterBoundaryRole.Valley))
        {
            return false;
        }

        var kind = role == RoofRafterBoundaryRole.Hip
            ? RoofTopologyEdgeKind.Hip : RoofTopologyEdgeKind.Valley;
        var contacts = new List<(RoofStructuralRafterTrimSource Source, ContactKind Kind, int? NodeIndex)>();
        foreach (var source in sources)
        {
            if (source.Role != role ||
                source.TopologyEdgeIndex < 0 ||
                source.TopologyEdgeIndex >= topology.Edges.Count)
            {
                continue;
            }

            var edge = topology.Edges[source.TopologyEdgeIndex];
            if (edge.Kind != kind ||
                !edge.FaceIndices.Contains(sourceFaceIndex))
            {
                continue;
            }

            var topologicalAxis = topology.Segment(edge);
            if (!TryClassifyContact(topologicalAxis, edge, canonicalEndpoint,
                    out var contactKind, out var nodeIndex))
            {
                continue;
            }

            contacts.Add((source, contactKind, nodeIndex));
        }

        if (contacts.Count == 0)
        {
            failureReason = "StructuralTargetNotFound";
            return false;
        }

        var interiors = contacts.Where(item => item.Kind == ContactKind.Interior).ToArray();
        if (interiors.Length == 1)
        {
            return ResolveSingleTarget(
                topology, interiors[0].Source, ordinaryInteriorPoint, out cut, out failureReason);
        }

        if (interiors.Length > 1)
        {
            failureReason = "StructuralTargetAmbiguous";
            return false;
        }

        // No interior contacts — endpoint-only selection.
        var endpoints = contacts.Where(item => item.Kind == ContactKind.Endpoint).ToArray();
        if (endpoints.Length == 1)
        {
            return ResolveSingleTarget(
                topology, endpoints[0].Source, ordinaryInteriorPoint, out cut, out failureReason);
        }

        if (endpoints.Length > 1 &&
            AllShareSameTopologyNode(endpoints))
        {
            // Shared apex/junction: leave StructuralCut unset; not ambiguous corruption.
            cut = null;
            failureReason = SharedNodeNoSideCutReason;
            return true;
        }

        failureReason = "StructuralTargetAmbiguous";
        return false;
    }

    public static bool TryCreatePlane(
        RoofStructuralRafterTrimSource target,
        RoofPoint2D ordinaryInteriorPoint,
        out RoofStructuralRafterSideCut? cut,
        out string failureReason)
    {
        cut = null;
        failureReason = "StructuralTargetInvalid";
        if (target is null || target.Role is not
            (RoofRafterBoundaryRole.Hip or RoofRafterBoundaryRole.Valley))
        {
            return false;
        }
        if (!Finite(target.WidthMm) || target.WidthMm <= Tolerance)
        {
            failureReason = "StructuralWidthInvalid";
            return false;
        }
        var dx = target.Axis.End.X - target.Axis.Start.X;
        var dy = target.Axis.End.Y - target.Axis.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (!Finite(length) || length <= Tolerance)
        {
            failureReason = "StructuralPlanDirectionDegenerate";
            return false;
        }
        var left = new RoofPoint3D(-dy / length, dx / length, 0d);
        var signedSide = left.X * (ordinaryInteriorPoint.X - target.Axis.Start.X) +
            left.Y * (ordinaryInteriorPoint.Y - target.Axis.Start.Y);
        if (!Finite(signedSide) || Math.Abs(signedSide) <= Tolerance)
        {
            failureReason = "StructuralFacingSideDegenerate";
            return false;
        }
        var sign = Math.Sign(signedSide);
        var normal = new RoofPoint3D(sign * left.X, sign * left.Y, 0d);
        var halfWidth = target.WidthMm / 2d;
        cut = new RoofStructuralRafterSideCut(
            target.TopologyEdgeIndex,
            target.Role,
            target.WidthMm,
            new RoofPoint3D(
                target.Axis.Start.X + normal.X * halfWidth,
                target.Axis.Start.Y + normal.Y * halfWidth,
                target.Axis.Start.Z),
            normal,
            Array.Empty<RoofPoint3D>(),
            Array.Empty<RoofPoint3D>(),
            Array.Empty<RoofPoint3D>());
        failureReason = string.Empty;
        return true;
    }

    private static bool ResolveSingleTarget(
        RoofTopology topology,
        RoofStructuralRafterTrimSource target,
        RoofPoint2D ordinaryInteriorPoint,
        out RoofStructuralRafterSideCut? cut,
        out string failureReason)
    {
        cut = null;
        var topologicalAxis = topology.Segment(topology.Edges[target.TopologyEdgeIndex]);
        if (!SamePlanAxis(topologicalAxis, target.Axis))
        {
            failureReason = "StructuralAxisMismatch";
            return false;
        }
        return TryCreatePlane(target, ordinaryInteriorPoint, out cut, out failureReason);
    }

    private static bool TryClassifyContact(
        RoofSegment3D edge,
        RoofTopologyEdge topologyEdge,
        RoofPoint2D point,
        out ContactKind kind,
        out int? nodeIndex)
    {
        kind = ContactKind.Endpoint;
        nodeIndex = null;
        var dx = edge.End.X - edge.Start.X;
        var dy = edge.End.Y - edge.Start.Y;
        var squared = dx * dx + dy * dy;
        if (!Finite(squared) || squared <= Tolerance * Tolerance)
        {
            return false;
        }

        var fraction = ((point.X - edge.Start.X) * dx +
            (point.Y - edge.Start.Y) * dy) / squared;
        var missX = point.X - (edge.Start.X + fraction * dx);
        var missY = point.Y - (edge.Start.Y + fraction * dy);
        if (fraction < -Tolerance || fraction > 1d + Tolerance ||
            Math.Sqrt(missX * missX + missY * missY) > Tolerance)
        {
            return false;
        }

        // Relative interior has priority over endpoint-only shared-node contacts.
        if (fraction > Tolerance && fraction < 1d - Tolerance)
        {
            kind = ContactKind.Interior;
            nodeIndex = null;
            return true;
        }

        kind = ContactKind.Endpoint;
        if (fraction <= Tolerance)
            nodeIndex = topologyEdge.StartNodeIndex;
        else
            nodeIndex = topologyEdge.EndNodeIndex;
        return true;
    }

    private static bool AllShareSameTopologyNode(
        IReadOnlyList<(RoofStructuralRafterTrimSource Source, ContactKind Kind, int? NodeIndex)> endpoints)
    {
        if (endpoints.Count < 2)
            return false;
        int? shared = null;
        foreach (var item in endpoints)
        {
            if (item.NodeIndex is not { } node)
                return false;
            if (shared is null)
                shared = node;
            else if (shared.Value != node)
                return false;
        }

        return shared is not null;
    }

    private static bool SamePlanAxis(RoofSegment3D a, RoofSegment3D b) =>
        (SamePlanPoint(a.Start, b.Start) && SamePlanPoint(a.End, b.End)) ||
        (SamePlanPoint(a.Start, b.End) && SamePlanPoint(a.End, b.Start));

    private static bool SamePlanPoint(RoofPoint3D a, RoofPoint3D b) =>
        Math.Abs(a.X - b.X) <= Tolerance && Math.Abs(a.Y - b.Y) <= Tolerance;

    private static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
