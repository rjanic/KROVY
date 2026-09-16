namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Drawing-level minimum true length for ordinary automatic rafters.
/// Uses the slope-corrected member length (<see cref="Models.Roofs.RoofRafterGeometry.TrueLengthMm"/>),
/// not plan length and not Hip/Valley PlanLength semantics.
/// </summary>
public static class RoofRafterLengthRules
{
    public const double DefaultMinimumAutomaticLengthMm = 500d;

    public static bool IsValidMinimumLength(double minimumAutomaticLengthMm) =>
        !double.IsNaN(minimumAutomaticLengthMm) &&
        !double.IsInfinity(minimumAutomaticLengthMm) &&
        minimumAutomaticLengthMm > 0d;

    /// <summary>
    /// Equal-to-threshold is allowed. Existing layout coordinate tolerance absorbs
    /// floating-point noise around the boundary without inventing a new tolerance.
    /// </summary>
    public static bool MeetsMinimumTrueLength(
        double trueLengthMm,
        double minimumAutomaticLengthMm) =>
        IsValidMinimumLength(minimumAutomaticLengthMm) &&
        !double.IsNaN(trueLengthMm) &&
        !double.IsInfinity(trueLengthMm) &&
        trueLengthMm + RoofRafterLayoutSolver.CoordinateToleranceMm >=
            minimumAutomaticLengthMm;

    public static IReadOnlyList<Models.Roofs.RoofRafterGeometry> FilterByMinimumTrueLength(
        IReadOnlyList<Models.Roofs.RoofRafterGeometry> rafters,
        double minimumAutomaticLengthMm)
    {
        if (rafters is null)
        {
            throw new ArgumentNullException(nameof(rafters));
        }
        if (!IsValidMinimumLength(minimumAutomaticLengthMm))
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAutomaticLengthMm));
        }

        return rafters
            .Where(rafter => MeetsMinimumTrueLength(rafter.TrueLengthMm, minimumAutomaticLengthMm))
            .ToArray();
    }
}
