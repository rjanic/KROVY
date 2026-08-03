using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed record AutoCadSlopeTextPresentationPreparation(
    ObjectId TextStyleId,
    string ResolvedTextStyleName,
    double ModelHeightMm,
    double SlopePaperHeightMm,
    int AnnotationScaleDenominator,
    AutoCadTextStyleResolutionKind ResolutionKind,
    AutoCadTextStyleRequestStatus RequestStatus,
    bool IsFallback,
    bool HasExplicitTextSettings);

/// <summary>
/// Validates the numeric slope-angle DBText presentation before any ForWrite
/// mutation. Only the numeric angle text consumes the Slope role; the "=" and
/// "⊥" special symbols stay geometry-only blocks and never take a text style or
/// a role height.
/// </summary>
internal static class AutoCadSlopeTextPresentationPolicy
{
    private const TimberAnnotationTextRole Role = TimberAnnotationTextRole.Slope;

    public static bool TryPrepare(
        Database database,
        AutoCadAnnotationPresentationContext? presentationContext,
        out AutoCadSlopeTextPresentationPreparation? preparation,
        out string diagnosticReason)
    {
        ArgumentNullException.ThrowIfNull(database);
        preparation = null;

        if (presentationContext is null)
        {
            diagnosticReason =
                "Slope angle text requires an annotation presentation context.";
            return false;
        }

        if (!AutoCadDatabaseIdentity.IsSame(
                database,
                presentationContext.Database))
        {
            diagnosticReason =
                "Slope angle text presentation context belongs to a " +
                "different database.";
            return false;
        }

        var roleText = presentationContext.ForRole(Role);
        if (!roleText.HasCompatibleStyle ||
            roleText.ResolvedTextStyleId is not ObjectId textStyleId)
        {
            diagnosticReason =
                "Slope angle text has no compatible text style; " +
                $"Kind={roleText.ResolutionKind}; " +
                $"Request={roleText.RequestStatus}; " +
                $"Requested={roleText.RequestedTextStyleName ?? "<none>"}.";
            return false;
        }

        if (textStyleId.IsNull || textStyleId.IsErased)
        {
            diagnosticReason =
                "Slope angle text resolved text style ObjectId is null or erased.";
            return false;
        }

        if (!AutoCadDatabaseIdentity.IsSame(database, textStyleId))
        {
            diagnosticReason =
                "Slope angle text resolved text style belongs to a " +
                "different database.";
            return false;
        }

        var modelHeightMm = roleText.ModelHeightMm;
        if (modelHeightMm <= 0d ||
            double.IsNaN(modelHeightMm) ||
            double.IsInfinity(modelHeightMm))
        {
            diagnosticReason =
                "Slope angle text model height is not a finite positive value.";
            return false;
        }

        var paperHeightMm = roleText.PaperHeightMm;
        if (!TimberAnnotationTextSettingsRules
                .IsValidSlopePaperHeightMm(paperHeightMm))
        {
            diagnosticReason =
                "Slope angle text paper height is outside the contract range.";
            return false;
        }

        preparation = new AutoCadSlopeTextPresentationPreparation(
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
