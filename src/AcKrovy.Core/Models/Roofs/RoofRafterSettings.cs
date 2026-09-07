namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Drawing-level defaults and minimum policy for automatic rafter generation.
/// These values are not geometry constants and do not constrain manual timber.
/// </summary>
public sealed record RoofRafterSettings(
    double DefaultAutomaticSpacingMm,
    double MinimumAutomaticSpacingMm)
{
    public static RoofRafterSettings CreateDefault() =>
        new(
            Services.Roofs.RoofRafterSpacingRules.DefaultAutomaticSpacingMm,
            Services.Roofs.RoofRafterSpacingRules.DefaultMinimumAutomaticSpacingMm);
}
