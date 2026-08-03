using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed record AutoCadDimensionsLeaderPresentationPreparation(
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
/// Validates standalone DimensionsLeader native MLeader presentation before
/// any ForWrite mutation (Etapa 5C). Uses
/// LabelAndDimensionModelHeight = paper × denominator without a second
/// ScaleFactor multiply. Shared native create/update remain legacy unless the
/// prepared value is threaded in as an explicit opt-in argument.
/// </summary>
internal static class AutoCadDimensionsLeaderPresentationPolicy
{
    public static bool TryPrepare(
        Database database,
        AutoCadAnnotationPresentationContext? presentationContext,
        out AutoCadDimensionsLeaderPresentationPreparation? preparation,
        out string diagnosticReason)
    {
        ArgumentNullException.ThrowIfNull(database);
        preparation = null;

        if (presentationContext is null)
        {
            diagnosticReason =
                "DimensionsLeader requires an annotation presentation context.";
            return false;
        }

        if (!AutoCadDatabaseIdentity.IsSame(
                database,
                presentationContext.Database))
        {
            diagnosticReason =
                "DimensionsLeader presentation context belongs to a " +
                "different database.";
            return false;
        }

        if (!presentationContext.HasCompatibleStyle ||
            presentationContext.ResolvedTextStyleId is not ObjectId textStyleId)
        {
            diagnosticReason =
                "DimensionsLeader has no compatible text style; " +
                $"Kind={presentationContext.TextStyleResolutionKind}; " +
                $"Request={presentationContext.TextStyleRequestStatus}; " +
                $"Requested={presentationContext.RequestedTextStyleName ?? "<none>"}.";
            return false;
        }

        if (textStyleId.IsNull || textStyleId.IsErased)
        {
            diagnosticReason =
                "DimensionsLeader resolved text style ObjectId is null " +
                "or erased.";
            return false;
        }

        if (!AutoCadDatabaseIdentity.IsSame(database, textStyleId))
        {
            diagnosticReason =
                "DimensionsLeader resolved text style belongs to a " +
                "different database.";
            return false;
        }

        var modelHeightMm = presentationContext.LabelAndDimensionModelHeight;
        if (modelHeightMm <= 0d ||
            double.IsNaN(modelHeightMm) ||
            double.IsInfinity(modelHeightMm))
        {
            diagnosticReason =
                "DimensionsLeader model height is not a finite positive value.";
            return false;
        }

        var paperHeightMm = presentationContext.EffectiveTextSettings
            .DimensionPaperHeightMm;
        if (!TimberAnnotationTextSettingsRules
                .IsValidDimensionPaperHeightMm(paperHeightMm))
        {
            diagnosticReason =
                "DimensionsLeader paper height is outside the contract range.";
            return false;
        }

        preparation = new AutoCadDimensionsLeaderPresentationPreparation(
            textStyleId,
            presentationContext.ResolvedTextStyleName ??
                TimberAnnotationTextSettingsRules.DefaultTextStyleName,
            modelHeightMm,
            paperHeightMm,
            presentationContext.AnnotationScaleDenominator,
            presentationContext.TextStyleResolutionKind,
            presentationContext.TextStyleRequestStatus,
            presentationContext.IsFallback,
            presentationContext.HasExplicitTextSettings);
        diagnosticReason = string.Empty;
        return true;
    }
}
