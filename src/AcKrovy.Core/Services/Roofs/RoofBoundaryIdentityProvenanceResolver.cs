using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Joins persisted raw-segment IDs to canonical boundary edges and topology faces.
/// No coordinate-based identity reconciliation is performed.
/// </summary>
public static class RoofBoundaryIdentityProvenanceResolver
{
    public static RoofBoundaryIdentityProvenanceResult Resolve(
        RoofFootprintInput? input,
        RoofBoundaryIdentity? identity)
    {
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        if (!normalized.Validation.IsValid || normalized.Validation.Footprint is null)
        {
            return Invalid(
                normalized.Validation.Error,
                RoofBoundaryIdentityError.InvalidFootprint);
        }

        var validation = normalized.Validation;
        var currentError = RoofBoundaryIdentityRules.ValidateCurrentSource(
            identity,
            normalized.EdgeProvenance.Count,
            validation.SourceOrientation);
        if (currentError != RoofBoundaryIdentityError.None || identity is null)
        {
            return Invalid(validation.Error, currentError);
        }

        var provenance = normalized.EdgeProvenance
            .Select(edge => new RoofBoundaryEdgeProvenance(
                edge.NormalizedBoundaryEdgeIndex,
                edge.RawPhysicalSegmentIndex,
                identity.BoundaryEdgeIds[edge.RawPhysicalSegmentIndex]))
            .ToArray();
        if (provenance.Length != identity.PhysicalSegmentCount ||
            provenance.Select(edge => edge.RawPhysicalSegmentIndex).Distinct().Count() != provenance.Length ||
            provenance.Select(edge => edge.BoundaryEdgeId).Distinct().Count() != provenance.Length)
        {
            return Invalid(
                validation.Error,
                RoofBoundaryIdentityError.BoundaryEdgeIdCountMismatch);
        }

        return new RoofBoundaryIdentityProvenanceResult(
            true,
            validation.Footprint,
            Array.AsReadOnly(provenance),
            RoofValidationError.None,
            RoofBoundaryIdentityError.None);
    }

    /// <summary>
    /// Resolves the persisted source identity for a topology face's SourceEdgeIndex.
    /// </summary>
    public static bool TryResolveNormalizedBoundaryEdge(
        RoofBoundaryIdentityProvenanceResult result,
        int normalizedBoundaryEdgeIndex,
        out RoofBoundaryEdgeProvenance provenance)
    {
        provenance = null!;
        if (!result.IsValid)
        {
            return false;
        }

        provenance = result.EdgeProvenance.FirstOrDefault(edge =>
            edge.NormalizedBoundaryEdgeIndex == normalizedBoundaryEdgeIndex)!;
        return provenance is not null;
    }

    /// <summary>
    /// Produces the stable order-independent pair needed by a future Hip/Valley/Ridge
    /// consumer. It does not create or persist a generated-member key.
    /// </summary>
    public static bool TryResolveBoundaryPair(
        RoofBoundaryIdentityProvenanceResult result,
        int firstNormalizedBoundaryEdgeIndex,
        int secondNormalizedBoundaryEdgeIndex,
        out RoofBoundaryEdgeIdPair pair)
    {
        pair = null!;
        if (!TryResolveNormalizedBoundaryEdge(
                result,
                firstNormalizedBoundaryEdgeIndex,
                out var first) ||
            !TryResolveNormalizedBoundaryEdge(
                result,
                secondNormalizedBoundaryEdgeIndex,
                out var second) ||
            first.BoundaryEdgeId == second.BoundaryEdgeId)
        {
            return false;
        }

        pair = new RoofBoundaryEdgeIdPair(
            Math.Min(first.BoundaryEdgeId, second.BoundaryEdgeId),
            Math.Max(first.BoundaryEdgeId, second.BoundaryEdgeId));
        return true;
    }

    private static RoofBoundaryIdentityProvenanceResult Invalid(
        RoofValidationError footprintError,
        RoofBoundaryIdentityError identityError) => new(
            false,
            null,
            Array.Empty<RoofBoundaryEdgeProvenance>(),
            footprintError,
            identityError);
}
