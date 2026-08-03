using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed record AutoCadPlainItemLeaderPresentationPreparation(
    ObjectId TextStyleId,
    string ResolvedTextStyleName,
    double ModelHeightMm,
    double ItemNumberPaperHeightMm,
    int AnnotationScaleDenominator,
    AutoCadTextStyleResolutionKind ResolutionKind,
    AutoCadTextStyleRequestStatus RequestStatus,
    bool IsFallback,
    bool HasExplicitTextSettings);

/// <summary>
/// Validates Plain item native MLeader presentation before any ForWrite
/// mutation. Used by standalone ItemNumberLeader + Plain (Etapa 5B2) and by
/// the Plain item component of DimensionsWithItemNumber (Etapa 5B3). Primary
/// combined dimensions MText remains outside this policy. The renderer draws
/// the item code, so it consumes the ItemCode role only.
/// </summary>
internal static class AutoCadPlainItemLeaderPresentationPolicy
{
    private const TimberAnnotationTextRole Role =
        TimberAnnotationTextRole.ItemCode;

    public static bool TryPrepare(
        Database database,
        AutoCadAnnotationPresentationContext? presentationContext,
        out AutoCadPlainItemLeaderPresentationPreparation? preparation,
        out string diagnosticReason)
    {
        ArgumentNullException.ThrowIfNull(database);
        preparation = null;

        if (presentationContext is null)
        {
            diagnosticReason =
                "Plain ItemNumberLeader requires an annotation presentation context.";
            return false;
        }

        if (!AutoCadDatabaseIdentity.IsSame(
                database,
                presentationContext.Database))
        {
            diagnosticReason =
                "Plain ItemNumberLeader presentation context belongs to a " +
                "different database.";
            return false;
        }

        var roleText = presentationContext.ForRole(Role);
        if (!roleText.HasCompatibleStyle ||
            roleText.ResolvedTextStyleId is not ObjectId textStyleId)
        {
            diagnosticReason =
                "Plain ItemNumberLeader has no compatible text style; " +
                $"Kind={roleText.ResolutionKind}; " +
                $"Request={roleText.RequestStatus}; " +
                $"Requested={roleText.RequestedTextStyleName ?? "<none>"}.";
            return false;
        }

        if (textStyleId.IsNull || textStyleId.IsErased)
        {
            diagnosticReason =
                "Plain ItemNumberLeader resolved text style ObjectId is null " +
                "or erased.";
            return false;
        }

        if (!AutoCadDatabaseIdentity.IsSame(database, textStyleId))
        {
            diagnosticReason =
                "Plain ItemNumberLeader resolved text style belongs to a " +
                "different database.";
            return false;
        }

        var modelHeightMm = roleText.ModelHeightMm;
        if (modelHeightMm <= 0d ||
            double.IsNaN(modelHeightMm) ||
            double.IsInfinity(modelHeightMm))
        {
            diagnosticReason =
                "Plain ItemNumberLeader model height is not a finite positive value.";
            return false;
        }

        var paperHeightMm = roleText.PaperHeightMm;
        if (!TimberAnnotationTextSettingsRules
                .IsValidItemCodePaperHeightMm(paperHeightMm))
        {
            diagnosticReason =
                "Plain ItemNumberLeader paper height is outside the contract range.";
            return false;
        }

        preparation = new AutoCadPlainItemLeaderPresentationPreparation(
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
