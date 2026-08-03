using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed record AutoCadFullLabelPresentationPreparation(
    ObjectId TextStyleId,
    string ResolvedTextStyleName,
    double ModelHeightMm,
    double LabelAndDimensionPaperHeightMm,
    int AnnotationScaleDenominator,
    AutoCadTextStyleResolutionKind ResolutionKind,
    AutoCadTextStyleRequestStatus RequestStatus,
    bool IsFallback,
    bool HasExplicitTextSettings);

/// <summary>
/// Validates FullLabel MText presentation before any ModelSpace or MText
/// ForWrite mutation. Uses the existing database-bound presentation context.
/// FullLabel keeps its frozen single-MText layout, so one role owns the whole
/// label: the Dimension role, which historically carried the combined
/// label-and-dimension height.
/// </summary>
internal static class AutoCadFullLabelPresentationPolicy
{
    private const TimberAnnotationTextRole Role =
        TimberAnnotationTextRole.Dimension;

    public static bool TryPrepare(
        Database database,
        AutoCadAnnotationPresentationContext? presentationContext,
        out AutoCadFullLabelPresentationPreparation? preparation,
        out string diagnosticReason)
    {
        ArgumentNullException.ThrowIfNull(database);
        preparation = null;

        if (presentationContext is null)
        {
            diagnosticReason =
                "FullLabel requires an annotation presentation context.";
            return false;
        }

        if (!AutoCadDatabaseIdentity.IsSame(
                database,
                presentationContext.Database))
        {
            diagnosticReason =
                "FullLabel presentation context belongs to a different database.";
            return false;
        }

        var roleText = presentationContext.ForRole(Role);
        if (!roleText.HasCompatibleStyle ||
            roleText.ResolvedTextStyleId is not ObjectId textStyleId)
        {
            diagnosticReason =
                "FullLabel has no compatible text style; " +
                $"Kind={roleText.ResolutionKind}; " +
                $"Request={roleText.RequestStatus}; " +
                $"Requested={roleText.RequestedTextStyleName ?? "<none>"}.";
            return false;
        }

        if (textStyleId.IsNull || textStyleId.IsErased)
        {
            diagnosticReason =
                "FullLabel resolved text style ObjectId is null or erased.";
            return false;
        }

        if (!AutoCadDatabaseIdentity.IsSame(database, textStyleId))
        {
            diagnosticReason =
                "FullLabel resolved text style belongs to a different database.";
            return false;
        }

        var modelHeightMm = roleText.ModelHeightMm;
        if (modelHeightMm <= 0d ||
            double.IsNaN(modelHeightMm) ||
            double.IsInfinity(modelHeightMm))
        {
            diagnosticReason =
                "FullLabel model height is not a finite positive value.";
            return false;
        }

        var paperHeightMm = roleText.PaperHeightMm;
        if (!TimberAnnotationTextSettingsRules
                .IsValidDimensionPaperHeightMm(paperHeightMm))
        {
            diagnosticReason =
                "FullLabel paper height is outside the contract range.";
            return false;
        }

        preparation = new AutoCadFullLabelPresentationPreparation(
            textStyleId,
            roleText.ResolvedTextStyleName ??
                TimberAnnotationTextSettingsRules.DefaultTextStyleName,
            modelHeightMm,
            paperHeightMm,
            presentationContext.AnnotationScaleDenominator,
            roleText.ResolutionKind,
            roleText.RequestStatus,
            roleText.IsFallback,
            presentationContext.HasExplicitTextSettings);
        diagnosticReason = string.Empty;
        return true;
    }
}
