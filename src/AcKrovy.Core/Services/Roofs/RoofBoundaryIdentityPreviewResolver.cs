using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Resolves boundary provenance for a read-only preview. A missing persisted identity
/// receives the same sequential owner-local identity produced by the explicit Ensure
/// workflow, but the created value is returned only in memory. Existing invalid data
/// always remains a hard failure.
/// </summary>
public static class RoofBoundaryIdentityPreviewResolver
{
    public static RoofBoundaryIdentityPreviewResolution Resolve(
        RoofFootprintInput? input,
        RoofBoundaryIdentity? persistedIdentity,
        RoofBoundaryIdentityError persistedReadError)
    {
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        if (!normalized.Validation.IsValid)
        {
            return Blocked(RoofBoundaryIdentityError.InvalidFootprint);
        }

        if (persistedIdentity is not null)
        {
            if (persistedReadError != RoofBoundaryIdentityError.None)
            {
                return Blocked(persistedReadError);
            }

            return ResolveProvenance(
                input,
                persistedIdentity,
                RoofBoundaryIdentityPreviewSource.Persisted);
        }

        if (persistedReadError != RoofBoundaryIdentityError.Missing)
        {
            return Blocked(
                persistedReadError == RoofBoundaryIdentityError.None
                    ? RoofBoundaryIdentityError.IncompletePayload
                    : persistedReadError);
        }

        var created = RoofBoundaryIdentityRules.CreateSequential(
            normalized.EdgeProvenance.Count,
            normalized.Validation.SourceOrientation);
        if (!created.IsValid || created.Identity is null)
        {
            return Blocked(created.Error);
        }

        return ResolveProvenance(
            input,
            created.Identity,
            RoofBoundaryIdentityPreviewSource.Ephemeral);
    }

    private static RoofBoundaryIdentityPreviewResolution ResolveProvenance(
        RoofFootprintInput? input,
        RoofBoundaryIdentity identity,
        RoofBoundaryIdentityPreviewSource source)
    {
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        return provenance.IsValid
            ? new RoofBoundaryIdentityPreviewResolution(
                true,
                source,
                identity,
                provenance,
                RoofBoundaryIdentityError.None)
            : Blocked(provenance.IdentityError);
    }

    private static RoofBoundaryIdentityPreviewResolution Blocked(
        RoofBoundaryIdentityError error) => new(
            false,
            RoofBoundaryIdentityPreviewSource.Blocked,
            null,
            null,
            error);
}

public enum RoofBoundaryIdentityPreviewSource
{
    Blocked = 0,
    Persisted,
    Ephemeral,
}

public sealed record RoofBoundaryIdentityPreviewResolution(
    bool IsValid,
    RoofBoundaryIdentityPreviewSource Source,
    RoofBoundaryIdentity? Identity,
    RoofBoundaryIdentityProvenanceResult? Provenance,
    RoofBoundaryIdentityError Error);
