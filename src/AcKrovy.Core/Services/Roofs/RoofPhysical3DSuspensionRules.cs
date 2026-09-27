using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// CAD-neutral policy for live STRETCH / GRIP when a Physical3DEnabled hip footprint
/// leaves rectangular eligibility. Preference stays on so 3D can auto-restore.
/// </summary>
public static class RoofPhysical3DSuspensionRules
{
    /// <summary>
    /// True when owned physical-3D entities must be cleared because the live footprint
    /// is no longer rectangular/symmetric-hip eligible, while the user's
    /// <see cref="RoofAbsoluteElevationState.Physical3DEnabled"/> preference is kept.
    /// </summary>
    public static bool ShouldSuspendMaterialization(
        bool physical3DEnabledPreference,
        bool footprintEligible) =>
        physical3DEnabledPreference && !footprintEligible;

    /// <summary>
    /// Elevation state written while 3D is suspended: never clears Physical3DEnabled.
    /// </summary>
    public static RoofAbsoluteElevationState PreservePreferenceWhileSuspended(
        RoofAbsoluteElevationState current)
    {
        if (current is null)
        {
            throw new ArgumentNullException(nameof(current));
        }

        return current;
    }

    /// <summary>
    /// Flattened 2D plan remains the owned display projection whenever the Physical3D
    /// preference is on, including while materialization is suspended.
    /// </summary>
    public static bool UseFlattenedDrawingPlane(bool physical3DEnabledPreference) =>
        physical3DEnabledPreference;
}
