namespace AcKrovy.Core.Services.Roofs;

public enum RoofClipboardPasteOwnershipClassification
{
    NotClipboardPaste = 0,
    ForeignOrUnknownProvenance = 1,
    WholeRoofOutOfScope = 2,
    NonIndividualPayloadOutOfScope = 3,
    SameDwgIndividualTimber = 4,
    KnownForeignIndividual = 5,
    KnownForeignBatch = 6,
    UnknownIntelligentBatch = 7,
    PlainPayload = 8,
}

public enum RoofClipboardPasteOwnershipAction
{
    UseExistingLifecycle = 0,
    UseStage2D4AAdoption = 1,
    DegradeForeignIndividual = 2,
    DegradeForeignBatch = 3,
    DegradeUnknownBatch = 4,
    SkipWholeRoof = 5,
    SkipSameDwgMulti = 6,
    NoOp = 7,
}

public sealed record RoofClipboardPasteOwnershipDecision(
    RoofClipboardPasteOwnershipClassification Classification,
    bool ShouldProcessIndividualTimber,
    string DiagnosticResult,
    RoofClipboardPasteOwnershipAction Action = RoofClipboardPasteOwnershipAction.NoOp);

/// <summary>
/// CAD-neutral routing policy for Stage 2D4-A/B. The provenance-kind overload
/// separates same-document adoption from foreign/unknown degradation; the boolean
/// overload preserves the published Stage 2D4-A contract. Database wrappers and
/// owner handles never establish provenance.
/// </summary>
public static class RoofClipboardPasteOwnershipRules
{
    /// <summary>
    /// Narrows final annotation observations to the exact Entity ids appended by the
    /// current paste command. Ordering and duplicate observations do not affect output.
    /// </summary>
    public static IReadOnlyList<T> SelectCurrentPasteAnnotations<T>(
        IEnumerable<T> appendedEntityIds,
        IEnumerable<T> finalAnnotationIds,
        IEqualityComparer<T>? comparer = null)
    {
        if (appendedEntityIds is null)
        {
            throw new ArgumentNullException(nameof(appendedEntityIds));
        }

        if (finalAnnotationIds is null)
        {
            throw new ArgumentNullException(nameof(finalAnnotationIds));
        }
        comparer ??= EqualityComparer<T>.Default;
        var appended = new HashSet<T>(appendedEntityIds, comparer);
        return finalAnnotationIds
            .Where(appended.Contains)
            .Distinct(comparer)
            .ToArray();
    }

    public static RoofClipboardPasteOwnershipDecision Classify(
        string? globalCommandName,
        RoofClipboardPasteProvenanceKind provenanceKind,
        int intelligentTimberCount,
        bool appendedRoofOwnerDetected,
        bool genericTimberDetected = false)
    {
        if (!LiveGeometryCommandRules.IsClipboardPasteCommand(globalCommandName))
        {
            return Decision(
                RoofClipboardPasteOwnershipClassification.NotClipboardPaste,
                false,
                "not-clipboard-paste",
                RoofClipboardPasteOwnershipAction.UseExistingLifecycle);
        }

        // A pasted intelligent roof assembly is always fenced, independently of
        // provenance, before any individual-timber or generic lifecycle can run.
        if (appendedRoofOwnerDetected)
        {
            return Decision(
                RoofClipboardPasteOwnershipClassification.WholeRoofOutOfScope,
                false,
                "whole-roof-out-of-scope",
                RoofClipboardPasteOwnershipAction.SkipWholeRoof);
        }

        if (intelligentTimberCount <= 0)
        {
            return Decision(
                RoofClipboardPasteOwnershipClassification.PlainPayload,
                false,
                genericTimberDetected ? "generic-timber-existing-lifecycle" : "plain-payload",
                genericTimberDetected
                    ? RoofClipboardPasteOwnershipAction.UseExistingLifecycle
                    : RoofClipboardPasteOwnershipAction.NoOp);
        }

        if (provenanceKind == RoofClipboardPasteProvenanceKind.KnownSameDocument)
        {
            return intelligentTimberCount == 1
                ? Decision(
                    RoofClipboardPasteOwnershipClassification.SameDwgIndividualTimber,
                    true,
                    "same-dwg-individual-timber",
                    RoofClipboardPasteOwnershipAction.UseStage2D4AAdoption)
                : Decision(
                    RoofClipboardPasteOwnershipClassification.NonIndividualPayloadOutOfScope,
                    false,
                    "same-dwg-multi-timber-out-of-scope",
                    RoofClipboardPasteOwnershipAction.SkipSameDwgMulti);
        }

        if (provenanceKind == RoofClipboardPasteProvenanceKind.KnownForeignDocument)
        {
            return intelligentTimberCount == 1
                ? Decision(
                    RoofClipboardPasteOwnershipClassification.KnownForeignIndividual,
                    false,
                    "known-foreign-individual",
                    RoofClipboardPasteOwnershipAction.DegradeForeignIndividual)
                : Decision(
                    RoofClipboardPasteOwnershipClassification.KnownForeignBatch,
                    false,
                    "known-foreign-batch",
                    RoofClipboardPasteOwnershipAction.DegradeForeignBatch);
        }

        return Decision(
            RoofClipboardPasteOwnershipClassification.UnknownIntelligentBatch,
            false,
            "unknown-intelligent-payload",
            RoofClipboardPasteOwnershipAction.DegradeUnknownBatch);
    }

    public static RoofClipboardPasteOwnershipDecision Classify(
        string? globalCommandName,
        bool sameDrawingProvenanceProven,
        int intelligentTimberCount,
        bool appendedRoofOwnerDetected)
    {
        if (!LiveGeometryCommandRules.IsClipboardPasteCommand(globalCommandName))
        {
            return Decision(
                RoofClipboardPasteOwnershipClassification.NotClipboardPaste,
                false,
                "not-clipboard-paste",
                RoofClipboardPasteOwnershipAction.UseExistingLifecycle);
        }

        if (!sameDrawingProvenanceProven)
        {
            return Decision(
                RoofClipboardPasteOwnershipClassification.ForeignOrUnknownProvenance,
                false,
                "foreign-or-unknown-provenance",
                RoofClipboardPasteOwnershipAction.NoOp);
        }

        if (appendedRoofOwnerDetected)
        {
            return Decision(
                RoofClipboardPasteOwnershipClassification.WholeRoofOutOfScope,
                false,
                "whole-roof-out-of-scope",
                RoofClipboardPasteOwnershipAction.SkipWholeRoof);
        }

        if (intelligentTimberCount != 1)
        {
            return Decision(
                RoofClipboardPasteOwnershipClassification.NonIndividualPayloadOutOfScope,
                false,
                "non-individual-payload-out-of-scope",
                RoofClipboardPasteOwnershipAction.SkipSameDwgMulti);
        }

        return Decision(
            RoofClipboardPasteOwnershipClassification.SameDwgIndividualTimber,
            true,
            "same-dwg-individual-timber",
            RoofClipboardPasteOwnershipAction.UseStage2D4AAdoption);
    }

    private static RoofClipboardPasteOwnershipDecision Decision(
        RoofClipboardPasteOwnershipClassification classification,
        bool shouldProcess,
        string diagnosticResult,
        RoofClipboardPasteOwnershipAction action) =>
        new(classification, shouldProcess, diagnosticResult, action);
}
