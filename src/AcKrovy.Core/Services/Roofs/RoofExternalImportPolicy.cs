namespace AcKrovy.Core.Services.Roofs;

/// <summary>CAD-neutral, fail-closed decisions for the Stage 2D4-C1 import boundary.</summary>
public static class RoofExternalImportPolicy
{
    public static RoofExternalImportDecision Classify(RoofExternalImportFacts facts)
    {
        if (facts is null)
        {
            throw new ArgumentNullException(nameof(facts));
        }

        if (facts.HasMalformedOrUnknownKrovyPayload)
        {
            return RoofExternalImportDecision.Reject("malformed-or-unknown-krovy-payload");
        }

        if (facts.HasRoofDefinition)
        {
            return RoofExternalImportDecision.Reject("whole-roof-out-of-scope");
        }

        if (facts.HasRoofOwnedState)
        {
            return RoofExternalImportDecision.Reject("orphan-roof-owned-state");
        }

        if (facts.HasConflictingIndividualRoles)
        {
            return RoofExternalImportDecision.Reject("conflicting-individual-roles");
        }

        if (facts.HasOrphanAnnotation)
        {
            return RoofExternalImportDecision.Reject("orphan-krovy-annotation");
        }

        return RoofExternalImportDecision.Allow(
            facts.IndividualTimberCount == 0 && facts.AnnotationCount == 0
                ? RoofExternalImportPayloadKind.Neutral
                : RoofExternalImportPayloadKind.Individual);
    }

    public static bool IsMappedNew(RoofExternalImportMappingFacts mapping) =>
        mapping.SourceBelongsToManifest &&
        mapping.HasOperationPair &&
        mapping.IsCloned &&
        mapping.DestinationBelongsToTarget;

    public static bool MayMutate(RoofExternalImportMappingFacts mapping) =>
        IsMappedNew(mapping);
}

public enum RoofExternalImportPayloadKind
{
    Neutral,
    Individual,
    Rejected,
}

public sealed record RoofExternalImportFacts(
    bool HasMalformedOrUnknownKrovyPayload,
    bool HasRoofDefinition,
    bool HasRoofOwnedState,
    bool HasConflictingIndividualRoles,
    bool HasOrphanAnnotation,
    int IndividualTimberCount,
    int AnnotationCount);

public readonly record struct RoofExternalImportMappingFacts(
    bool SourceBelongsToManifest,
    bool HasOperationPair,
    bool IsCloned,
    bool DestinationBelongsToTarget);

public sealed record RoofExternalImportDecision(
    bool IsAllowed,
    RoofExternalImportPayloadKind PayloadKind,
    string Reason)
{
    public static RoofExternalImportDecision Allow(RoofExternalImportPayloadKind kind) =>
        new(true, kind, "allowed");

    public static RoofExternalImportDecision Reject(string reason) =>
        new(false, RoofExternalImportPayloadKind.Rejected, reason);
}
