using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Resolves structural topology edges to owner-scoped BoundaryEdgeId identities
/// and distinguishes boundary lineage from physical boundary anchoring.
/// It does not generate entities, annotations, metadata, or an alternative topology.
/// </summary>
public static class RoofStructuralEdgeIdentityResolver
{
    public static RoofStructuralEdgeResolutionResult Resolve(
        HipRoofGeometry? geometry,
        RoofBoundaryIdentityProvenanceResult? boundaryProvenance)
    {
        if (geometry is null)
        {
            return Invalid(RoofStructuralEdgeResolutionError.InvalidGeometry);
        }

        if (boundaryProvenance is null || !boundaryProvenance.IsValid)
        {
            return Invalid(RoofStructuralEdgeResolutionError.InvalidBoundaryProvenance);
        }

        var topology = geometry.Topology;
        if (boundaryProvenance.EdgeProvenance.Count != topology.BoundaryVertexCount ||
            boundaryProvenance.EdgeProvenance
                .Select(edge => edge.NormalizedBoundaryEdgeIndex)
                .OrderBy(index => index)
                .SequenceEqual(Enumerable.Range(0, topology.BoundaryVertexCount)) is false)
        {
            return Invalid(RoofStructuralEdgeResolutionError.BoundaryProvenanceCountMismatch);
        }

        var physicalPathAnchors = ResolvePhysicalPathAnchors(topology);
        var resolved = new List<ResolvedRoofStructuralEdge>();
        for (var topologyEdgeIndex = 0; topologyEdgeIndex < topology.Edges.Count; topologyEdgeIndex++)
        {
            var edge = topology.Edges[topologyEdgeIndex];
            if (edge.Kind is RoofTopologyEdgeKind.Eave or RoofTopologyEdgeKind.CoplanarSeam)
            {
                continue;
            }

            if (!TryMapRole(topology, edge, out var role))
            {
                return Invalid(
                    RoofStructuralEdgeResolutionError.UnsupportedTopologyEdgeRole,
                    topologyEdgeIndex);
            }

            if (edge.FaceIndices.Count != 2)
            {
                return Invalid(
                    RoofStructuralEdgeResolutionError.StructuralEdgeFaceCountMismatch,
                    topologyEdgeIndex);
            }

            if (edge.FaceIndices.Any(index => index < 0 || index >= topology.Faces.Count))
            {
                return Invalid(
                    RoofStructuralEdgeResolutionError.StructuralEdgeFaceIndexOutOfRange,
                    topologyEdgeIndex);
            }

            var firstSourceEdge = topology.Faces[edge.FaceIndices[0]].SourceEdgeIndex;
            var secondSourceEdge = topology.Faces[edge.FaceIndices[1]].SourceEdgeIndex;
            if (!RoofBoundaryIdentityProvenanceResolver.TryResolveBoundaryPair(
                    boundaryProvenance,
                    firstSourceEdge,
                    secondSourceEdge,
                    out var pair))
            {
                return Invalid(
                    RoofStructuralEdgeResolutionError.UnresolvedFaceBoundaryIdentity,
                    topologyEdgeIndex);
            }

            if (!RoofStructuralIdentityRules.TryCreate(
                    role,
                    pair.LowerBoundaryEdgeId,
                    pair.UpperBoundaryEdgeId,
                    out var identity,
                    out _) ||
                identity is null)
            {
                return Invalid(
                    RoofStructuralEdgeResolutionError.InvalidStructuralIdentity,
                    topologyEdgeIndex);
            }

            var segment = topology.Segment(edge);
            if (!IsFinite(segment.LengthMm) || segment.LengthMm <= 0d)
            {
                return Invalid(
                    RoofStructuralEdgeResolutionError.InvalidStructuralSegment,
                    topologyEdgeIndex);
            }

            var memberInclinationDegrees = 0d;
            if (role is RoofStructuralRole.Hip or RoofStructuralRole.Valley)
            {
                if (!RoofPhysicalStructuralFold.TryMemberInclinationDegreesAboveHorizontal(
                        topology,
                        edge,
                        out memberInclinationDegrees))
                {
                    return Invalid(
                        RoofStructuralEdgeResolutionError.InvalidStructuralSegment,
                        topologyEdgeIndex);
                }
            }

            resolved.Add(new ResolvedRoofStructuralEdge(
                identity,
                topologyEdgeIndex,
                segment,
                edge.OriginatingBoundaryVertexIndex,
                ResolvePhysicalBoundaryAnchor(topology, edge, role),
                physicalPathAnchors.TryGetValue(topologyEdgeIndex, out var pathAnchor)
                    ? pathAnchor
                    : null,
                RoofPhysicalStructuralFold.IsTimberEligibleFold(topology, edge, role),
                memberInclinationDegrees));
        }

        if (RoofStructuralIdentityRules.TryFindDuplicate(
                resolved.Select(edge => edge.StructuralIdentity),
                out var duplicate))
        {
            return new RoofStructuralEdgeResolutionResult(
                false,
                Array.Empty<ResolvedRoofStructuralEdge>(),
                RoofStructuralEdgeResolutionError.DuplicateStructuralIdentity,
                null,
                duplicate);
        }

        var ordered = resolved
            .OrderBy(edge => edge.StructuralRole)
            .ThenBy(edge => edge.BoundaryEdgeIdA)
            .ThenBy(edge => edge.BoundaryEdgeIdB)
            .ToArray();
        return new RoofStructuralEdgeResolutionResult(
            true,
            Array.AsReadOnly(ordered),
            RoofStructuralEdgeResolutionError.None,
            null,
            null);
    }

    private static bool TryMapRole(
        RoofTopology topology,
        RoofTopologyEdge edge,
        out RoofStructuralRole role)
    {
        if (edge.Kind == RoofTopologyEdgeKind.Hip)
        {
            role = RoofStructuralRole.Hip;
            return true;
        }

        if (edge.Kind == RoofTopologyEdgeKind.Valley)
        {
            role = RoofStructuralRole.Valley;
            return true;
        }

        if (edge.Kind == RoofTopologyEdgeKind.Ridge)
        {
            // True horizontal Ridge stays topology/reference only.
            if (RoofPhysicalStructuralFold.IsHorizontalRidge(topology, edge))
            {
                role = RoofStructuralRole.Ridge;
                return true;
            }

            // Inclined Ridge becomes Hip only as a Valley-junction Hip continuation
            // (reflex lineage). Ordinary inclined ridge spines remain Ridge/non-timber.
            if (edge.OriginatingBoundaryVertexIndex is { } origin &&
                IsCompatibleBoundaryCorner(topology, origin, RoofStructuralRole.Valley) &&
                RoofPhysicalStructuralFold.TryClassify(topology, edge, out var fold) &&
                fold == RoofPhysicalStructuralFoldClass.ConvexHip)
            {
                role = RoofStructuralRole.Hip;
                return true;
            }

            role = RoofStructuralRole.Ridge;
            return true;
        }

        role = RoofStructuralRole.Undefined;
        return false;
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static int? ResolvePhysicalBoundaryAnchor(
        RoofTopology topology,
        RoofTopologyEdge edge,
        RoofStructuralRole role)
    {
        var origin = edge.OriginatingBoundaryVertexIndex;
        if (!origin.HasValue ||
            origin.Value < 0 ||
            origin.Value >= topology.BoundaryVertexCount ||
            edge.StartNodeIndex != origin.Value && edge.EndNodeIndex != origin.Value)
        {
            return null;
        }

        return IsCompatibleBoundaryCorner(topology, origin.Value, role)
            ? origin
            : null;
    }

    private static IReadOnlyDictionary<int, int> ResolvePhysicalPathAnchors(
        RoofTopology topology)
    {
        var anchors = new Dictionary<int, int>();
        var candidates = topology.Edges
            .Select((edge, index) => (Edge: edge, Index: index))
            .Select(item => (
                item.Edge,
                item.Index,
                Role: TryMapRole(topology, item.Edge, out var role)
                    ? role
                    : RoofStructuralRole.Undefined))
            .Where(item => item.Role is RoofStructuralRole.Hip or RoofStructuralRole.Valley)
            .Where(item => item.Edge.OriginatingBoundaryVertexIndex.HasValue)
            .GroupBy(item => (
                item.Role,
                Origin: item.Edge.OriginatingBoundaryVertexIndex!.Value));

        foreach (var group in candidates)
        {
            var role = group.Key.Role;
            var origin = group.Key.Origin;
            if (origin < 0 || origin >= topology.BoundaryVertexCount ||
                !IsCompatibleBoundaryCorner(topology, origin, role))
            {
                continue;
            }

            var component = ConnectedComponentFromAnchor(
                group.Select(item => (item.Edge, item.Index)).ToArray(),
                origin);
            if (!IsUnambiguousPhysicalPath(component, origin, topology.BoundaryVertexCount))
            {
                continue;
            }

            foreach (var item in component)
            {
                anchors.Add(item.Index, origin);
            }
        }

        ResolveHipTransitionPathAnchors(topology, anchors);

        return anchors;
    }

    private static void ResolveHipTransitionPathAnchors(
        RoofTopology topology,
        IDictionary<int, int> anchors)
    {
        var transitions = topology.Edges
            .Select((edge, index) => (Edge: edge, Index: index))
            .Where(item =>
                item.Edge.Kind == RoofTopologyEdgeKind.Ridge &&
                item.Edge.OriginatingBoundaryVertexIndex is { } origin &&
                IsCompatibleBoundaryCorner(topology, origin, RoofStructuralRole.Valley))
            .ToArray();

        foreach (var transition in transitions)
        {
            var reflexOrigin = transition.Edge.OriginatingBoundaryVertexIndex!.Value;
            var valleyContacts = topology.Edges
                .Select((edge, index) => (Edge: edge, Index: index))
                .Where(item =>
                    item.Edge.Kind == RoofTopologyEdgeKind.Valley &&
                    item.Edge.OriginatingBoundaryVertexIndex == reflexOrigin &&
                    SharesEndpoint(item.Edge, transition.Edge))
                .ToArray();
            if (valleyContacts.Length != 1 ||
                !TrySharedEndpoint(valleyContacts[0].Edge, transition.Edge, out var valleyNode))
            {
                continue;
            }

            var hipNode = transition.Edge.StartNodeIndex == valleyNode
                ? transition.Edge.EndNodeIndex
                : transition.Edge.StartNodeIndex;
            var hipContacts = topology.Edges
                .Select((edge, index) => (Edge: edge, Index: index))
                .Where(item =>
                    item.Edge.Kind == RoofTopologyEdgeKind.Hip &&
                    anchors.ContainsKey(item.Index) &&
                    (item.Edge.StartNodeIndex == hipNode || item.Edge.EndNodeIndex == hipNode))
                .ToArray();
            if (hipContacts.Length != 1)
            {
                continue;
            }

            var valleyFace = transition.Edge.FaceIndices
                .Intersect(valleyContacts[0].Edge.FaceIndices)
                .ToArray();
            var hipFace = transition.Edge.FaceIndices
                .Intersect(hipContacts[0].Edge.FaceIndices)
                .ToArray();
            if (valleyFace.Length != 1 || hipFace.Length != 1 ||
                valleyFace[0] == hipFace[0])
            {
                continue;
            }

            var hipAnchor = anchors[hipContacts[0].Index];
            var path = topology.Edges
                .Select((edge, index) => (Edge: edge, Index: index))
                .Where(item => anchors.TryGetValue(item.Index, out var anchor) &&
                               anchor == hipAnchor &&
                               TryMapRole(topology, item.Edge, out var role) &&
                               role == RoofStructuralRole.Hip)
                .Append(transition)
                .ToArray();
            if (!IsUnambiguousPhysicalPath(path, hipAnchor, topology.BoundaryVertexCount))
            {
                continue;
            }

            anchors.Add(transition.Index, hipAnchor);
        }
    }

    private static bool SharesEndpoint(RoofTopologyEdge first, RoofTopologyEdge second) =>
        first.StartNodeIndex == second.StartNodeIndex ||
        first.StartNodeIndex == second.EndNodeIndex ||
        first.EndNodeIndex == second.StartNodeIndex ||
        first.EndNodeIndex == second.EndNodeIndex;

    private static bool TrySharedEndpoint(
        RoofTopologyEdge first,
        RoofTopologyEdge second,
        out int node)
    {
        var shared = new[] { first.StartNodeIndex, first.EndNodeIndex }
            .Intersect(new[] { second.StartNodeIndex, second.EndNodeIndex })
            .ToArray();
        node = shared.Length == 1 ? shared[0] : -1;
        return shared.Length == 1;
    }

    private static IReadOnlyList<(RoofTopologyEdge Edge, int Index)> ConnectedComponentFromAnchor(
        IReadOnlyList<(RoofTopologyEdge Edge, int Index)> candidates,
        int origin)
    {
        var reachedNodes = new HashSet<int> { origin };
        var reachedEdges = new HashSet<int>();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var candidate in candidates)
            {
                if (reachedEdges.Contains(candidate.Index) ||
                    !reachedNodes.Contains(candidate.Edge.StartNodeIndex) &&
                    !reachedNodes.Contains(candidate.Edge.EndNodeIndex))
                {
                    continue;
                }

                reachedEdges.Add(candidate.Index);
                reachedNodes.Add(candidate.Edge.StartNodeIndex);
                reachedNodes.Add(candidate.Edge.EndNodeIndex);
                changed = true;
            }
        }

        return candidates.Where(candidate => reachedEdges.Contains(candidate.Index)).ToArray();
    }

    private static bool IsUnambiguousPhysicalPath(
        IReadOnlyList<(RoofTopologyEdge Edge, int Index)> component,
        int origin,
        int boundaryVertexCount)
    {
        if (component.Count == 0)
        {
            return false;
        }

        var degrees = new Dictionary<int, int>();
        foreach (var item in component)
        {
            degrees.TryGetValue(item.Edge.StartNodeIndex, out var startDegree);
            degrees[item.Edge.StartNodeIndex] = startDegree + 1;
            degrees.TryGetValue(item.Edge.EndNodeIndex, out var endDegree);
            degrees[item.Edge.EndNodeIndex] = endDegree + 1;
        }

        return degrees.TryGetValue(origin, out var originDegree) &&
               originDegree == 1 &&
               degrees.Values.All(degree => degree <= 2) &&
               degrees.Values.Count(degree => degree == 1) == 2 &&
               component.Count == degrees.Count - 1 &&
               degrees.Keys.All(node => node == origin || node >= boundaryVertexCount);
    }

    private static bool IsCompatibleBoundaryCorner(
        RoofTopology topology,
        int origin,
        RoofStructuralRole role)
    {
        var previous = topology.Nodes[
            (origin + topology.BoundaryVertexCount - 1) % topology.BoundaryVertexCount];
        var current = topology.Nodes[origin];
        var next = topology.Nodes[(origin + 1) % topology.BoundaryVertexCount];
        var turn = (current.X - previous.X) * (next.Y - current.Y) -
                   (current.Y - previous.Y) * (next.X - current.X);
        var isReflex = turn < 0d;
        return (role == RoofStructuralRole.Hip && !isReflex) ||
               (role == RoofStructuralRole.Valley && isReflex);
    }

    private static RoofStructuralEdgeResolutionResult Invalid(
        RoofStructuralEdgeResolutionError error,
        int? topologyEdgeIndex = null) => new(
            false,
            Array.Empty<ResolvedRoofStructuralEdge>(),
            error,
            topologyEdgeIndex,
            null);
}
