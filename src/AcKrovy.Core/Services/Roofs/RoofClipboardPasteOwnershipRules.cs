namespace AcKrovy.Core.Services.Roofs;

public enum RoofClipboardPasteOwnershipClassification
{
    NotClipboardPaste = 0,
    ForeignOrUnknownProvenance = 1,
    WholeRoofOutOfScope = 2,
    NonIndividualPayloadOutOfScope = 3,
    SameDwgIndividualTimber = 4,
}

public sealed record RoofClipboardPasteOwnershipDecision(
    RoofClipboardPasteOwnershipClassification Classification,
    bool ShouldProcessIndividualTimber,
    string DiagnosticResult);

/// <summary>
/// CAD-neutral routing policy for Stage 2D4-A. Host code proves exact tracked
/// Document identity plus the current clipboard revision and supplies only the
/// resulting boolean; Database wrappers and owner handles never establish provenance.
/// </summary>
public static class RoofClipboardPasteOwnershipRules
{
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
                "not-clipboard-paste");
        }

        if (!sameDrawingProvenanceProven)
        {
            return Decision(
                RoofClipboardPasteOwnershipClassification.ForeignOrUnknownProvenance,
                false,
                "foreign-or-unknown-provenance");
        }

        if (appendedRoofOwnerDetected)
        {
            return Decision(
                RoofClipboardPasteOwnershipClassification.WholeRoofOutOfScope,
                false,
                "whole-roof-out-of-scope");
        }

        if (intelligentTimberCount != 1)
        {
            return Decision(
                RoofClipboardPasteOwnershipClassification.NonIndividualPayloadOutOfScope,
                false,
                "non-individual-payload-out-of-scope");
        }

        return Decision(
            RoofClipboardPasteOwnershipClassification.SameDwgIndividualTimber,
            true,
            "same-dwg-individual-timber");
    }

    private static RoofClipboardPasteOwnershipDecision Decision(
        RoofClipboardPasteOwnershipClassification classification,
        bool shouldProcess,
        string diagnosticResult) =>
        new(classification, shouldProcess, diagnosticResult);
}
