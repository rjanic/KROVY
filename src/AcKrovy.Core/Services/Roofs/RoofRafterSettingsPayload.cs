using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// CAD-neutral encode/decode for the drawing-level automatic-rafter settings
/// payload. Keeps SchemaVersion 1 and accepts legacy two-real payloads.
/// Field order is authoritative:
/// default spacing, minimum spacing, optional minimum length.
/// </summary>
public static class RoofRafterSettingsPayload
{
    public const int SchemaVersion = 1;

    public static IReadOnlyList<double> EncodeReals(RoofRafterSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        return
        [
            settings.DefaultAutomaticSpacingMm,
            settings.MinimumAutomaticSpacingMm,
            settings.MinimumAutomaticLengthMm,
        ];
    }

    public static bool TryDecode(
        int schemaVersion,
        IReadOnlyList<double> reals,
        out RoofRafterSettings? settings)
    {
        settings = null;
        if (schemaVersion != SchemaVersion || reals is null)
        {
            return false;
        }

        if (reals.Count is not (2 or 3))
        {
            return false;
        }

        var defaultSpacing = reals[0];
        var minimumSpacing = reals[1];
        var minimumLength = reals.Count == 3
            ? reals[2]
            : RoofRafterLengthRules.DefaultMinimumAutomaticLengthMm;
        if (!RoofRafterSpacingRules.IsValidSettings(
                defaultSpacing,
                minimumSpacing,
                minimumLength))
        {
            return false;
        }

        settings = new RoofRafterSettings(
            defaultSpacing,
            minimumSpacing,
            minimumLength);
        return true;
    }
}
