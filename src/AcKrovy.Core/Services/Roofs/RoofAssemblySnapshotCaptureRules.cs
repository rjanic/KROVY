using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Eligibility for read-only pre-command roof assembly snapshots used by Locked
/// generated-child recovery and unsupported-source recovery.
/// Snapshot capture must not require <see cref="RoofSourceChangeKind.RigidEquivalent"/>:
/// compact Hip descriptors and closed-polyline closing-vertex representations must not
/// block a safety capture of the current canonical assembly.
/// </summary>
public static class RoofAssemblySnapshotCaptureRules
{
    /// <summary>
    /// True when the current source can be snapshotted for later generated-only or
    /// unsupported recovery. <paramref name="classificationKind"/> may be
    /// <see cref="RoofSourceChangeKind.SupportedResize"/> when the compact descriptor
    /// disagrees with the live polygon but the solve still produced geometry.
    /// </summary>
    public static bool CanCaptureSource(
        bool footprintValid,
        RoofSourceChangeKind classificationKind,
        bool classificationHasGeometry) =>
        footprintValid &&
        classificationHasGeometry &&
        classificationKind is
            RoofSourceChangeKind.RigidEquivalent or
            RoofSourceChangeKind.SupportedResize;

    /// <summary>
    /// Shared CAD-neutral evaluation of a live extracted source for pre-command
    /// assembly snapshot capture. Uses the same effective-closed contract as
    /// <see cref="RoofFootprintValidator"/> / topology — never the AutoCAD Closed
    /// flag alone. Returns HOST-facing skip reasons and vertex counts.
    /// </summary>
    public static bool TryEvaluateSourceForCapture(
        RoofFootprintInput? input,
        RoofDefinitionData? stored,
        out int sourceVertexCount,
        out int normalizedVertexCount,
        out RoofSourceChangeKind classifier,
        out string skipReason)
    {
        sourceVertexCount = 0;
        normalizedVertexCount = 0;
        classifier = RoofSourceChangeKind.None;
        skipReason = "unknown";
        if (stored is null)
        {
            skipReason = "no-roof-definition";
            return false;
        }

        sourceVertexCount = input?.Vertices?.Count ?? 0;
        if (input?.Vertices is null ||
            input.Vertices.Count < 3 ||
            !RoofFootprintValidator.IsEffectivelyClosed(input))
        {
            skipReason = "invalid-source-polyline";
            return false;
        }

        var validation = RoofFootprintValidator.Validate(input);
        normalizedVertexCount = validation.Footprint?.Vertices.Count ?? 0;
        if (!validation.IsValid || validation.Footprint is null)
        {
            skipReason = "footprint-invalid:" + validation.Error;
            return false;
        }

        var classification = RoofDefinitionPersistence.Classify(
            input,
            validation.Footprint,
            stored);
        classifier = classification.Kind;
        if (!CanCaptureSource(
                footprintValid: true,
                classification.Kind,
                classificationHasGeometry: classification.Geometry is not null))
        {
            skipReason = "classifier-not-capturable:" + classification.Kind;
            return false;
        }

        skipReason = string.Empty;
        return true;
    }

    /// <summary>
    /// Closing-vertex duplication on an otherwise identical closed polygon must not
    /// change semantic Hip classification.
    /// </summary>
    public static bool ClosingVertexDuplicationIsSemanticallyEquivalent(
        RoofFootprintInput withExplicitClose,
        RoofFootprintInput withoutExplicitClose,
        RoofDefinitionData persisted)
    {
        if (withExplicitClose is null || withoutExplicitClose is null || persisted is null)
        {
            return false;
        }

        var firstValidation = RoofFootprintValidator.Validate(withExplicitClose);
        var secondValidation = RoofFootprintValidator.Validate(withoutExplicitClose);
        if (!firstValidation.IsValid ||
            !secondValidation.IsValid ||
            firstValidation.Footprint is null ||
            secondValidation.Footprint is null)
        {
            return false;
        }

        var first = RoofDefinitionPersistence.Classify(
            withExplicitClose,
            firstValidation.Footprint,
            persisted);
        var second = RoofDefinitionPersistence.Classify(
            withoutExplicitClose,
            secondValidation.Footprint,
            persisted);
        return first.Kind == second.Kind &&
               first.Geometry is not null &&
               second.Geometry is not null &&
               string.Equals(
                   first.Geometry.Signature,
                   second.Geometry.Signature,
                   StringComparison.Ordinal);
    }
}
