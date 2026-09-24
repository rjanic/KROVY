using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Resolves planning rafter W×H from durable layout policy + recovered roof recipe.
/// Profile defaults alone are never treated as an actual roof rafter source.
/// Fail-closed on conflict, ambiguous recipe, invalid manual, or unresolved profile.
/// </summary>
internal static class RoofAutomaticPurlinPlanningRafterResolver
{
    internal const string ConflictFailure = "rafter-source-conflict";
    internal const string UnresolvedFailure = "rafter-profile-unresolved";
    internal const string InvalidManualFailure = "invalid-manual-rafter-profile";
    internal const string MissingActualFailure = "rafter-source-missing-actual";

    public readonly record struct Resolution(
        double WidthMm,
        double HeightMm,
        RoofAutomaticPurlinRafterSourceRules.Outcome Outcome,
        RoofAutomaticPurlinRafterSourcePolicy EffectivePolicy);

    public static bool TryResolve(
        Database database,
        Transaction transaction,
        string ownerReference,
        RoofAutomaticPurlinLayout layout,
        TimberElementDefaultProfile defaultProfile,
        out Resolution resolution,
        out string failureReason,
        Editor? editor = null)
    {
        resolution = default;
        failureReason = string.Empty;
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(defaultProfile);

        var actualAvailable = false;
        var actualWidthMm = 0d;
        var actualHeightMm = 0d;
        var actualAmbiguous = false;

        if (!RoofAutomaticPurlinRafterDimensionsResolver.TryResolve(
                database,
                transaction,
                ownerReference,
                defaultProfile,
                out var recipeResolution,
                out var recipeFailure,
                editor))
        {
            if (string.Equals(
                    recipeFailure,
                    RoofAutomaticPurlinRafterDimensionsResolver.AmbiguousFailure,
                    StringComparison.Ordinal))
            {
                actualAmbiguous = true;
            }
            else
            {
                failureReason = recipeFailure;
                return false;
            }
        }
        else if (recipeResolution.Source ==
                 RoofAutomaticPurlinRafterDimensionsResolver.SourceKind.RecoveredRoofRecipe)
        {
            actualAvailable = true;
            actualWidthMm = recipeResolution.WidthMm;
            actualHeightMm = recipeResolution.HeightMm;
        }

        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            actualAvailable,
            actualWidthMm,
            actualHeightMm,
            actualAmbiguous);

        switch (resolved.Outcome)
        {
            case RoofAutomaticPurlinRafterSourceRules.Outcome.UseManual:
            case RoofAutomaticPurlinRafterSourceRules.Outcome.UseActual:
                if (resolved.WidthMm is not { } widthMm ||
                    resolved.HeightMm is not { } heightMm)
                {
                    failureReason = UnresolvedFailure;
                    return false;
                }

                resolution = new Resolution(
                    widthMm,
                    heightMm,
                    resolved.Outcome,
                    resolved.EffectivePolicy);
                return true;

            case RoofAutomaticPurlinRafterSourceRules.Outcome.Conflict:
                failureReason = ConflictFailure;
                return false;

            case RoofAutomaticPurlinRafterSourceRules.Outcome.MissingActualWithRecoverableManual:
                failureReason = MissingActualFailure;
                return false;

            case RoofAutomaticPurlinRafterSourceRules.Outcome.AmbiguousActual:
                failureReason = RoofAutomaticPurlinRafterDimensionsResolver.AmbiguousFailure;
                return false;

            case RoofAutomaticPurlinRafterSourceRules.Outcome.InvalidManual:
                failureReason = InvalidManualFailure;
                return false;

            default:
                failureReason = UnresolvedFailure;
                return false;
        }
    }
}
