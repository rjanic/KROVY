using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Resolves existing Hip topology edges to owner-scoped BoundaryEdgeId identities.
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

        var resolved = new List<ResolvedRoofStructuralEdge>();
        for (var topologyEdgeIndex = 0; topologyEdgeIndex < topology.Edges.Count; topologyEdgeIndex++)
        {
            var edge = topology.Edges[topologyEdgeIndex];
            if (edge.Kind is RoofTopologyEdgeKind.Eave or RoofTopologyEdgeKind.CoplanarSeam)
            {
                continue;
            }

            if (!TryMapRole(edge.Kind, out var role))
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

            resolved.Add(new ResolvedRoofStructuralEdge(
                identity,
                topologyEdgeIndex,
                segment));
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
        RoofTopologyEdgeKind kind,
        out RoofStructuralRole role)
    {
        role = kind switch
        {
            RoofTopologyEdgeKind.Hip => RoofStructuralRole.Hip,
            RoofTopologyEdgeKind.Valley => RoofStructuralRole.Valley,
            RoofTopologyEdgeKind.Ridge => RoofStructuralRole.Ridge,
            _ => RoofStructuralRole.Undefined,
        };
        return role != RoofStructuralRole.Undefined;
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static RoofStructuralEdgeResolutionResult Invalid(
        RoofStructuralEdgeResolutionError error,
        int? topologyEdgeIndex = null) => new(
            false,
            Array.Empty<ResolvedRoofStructuralEdge>(),
            error,
            topologyEdgeIndex,
            null);
}
