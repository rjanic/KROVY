namespace AcKrovy.Core.Services;

/// <summary>
/// CAD-neutral guard for native MLeader <c>SetDogleg</c> inputs.
/// AutoCAD throws <c>eInvalidInput</c> for zero/non-finite directions and for
/// dogleg lengths that are still degenerate relative to model geometry.
/// DimensionsOnly's 250 mm landing reduction often yields a near-flush landing
/// (1e-6 clamp or only a few model-mm) that passes a tiny epsilon but is still
/// rejected by the host API — especially when <c>SetDogleg</c> runs before the
/// MLeader is database-resident.
/// </summary>
public static class TimberNativeMLeaderDoglegInputRules
{
    /// <summary>
    /// Practical minimum DoglegLength for a safe <c>SetDogleg</c> call.
    /// Must stay well above the DimensionsOnly 1e-6 clamp floor and above the
    /// few-model-mm landings produced when the 250 mm reduction nearly cancels
    /// the envelope-based landing (e.g. ~5-character dimension text).
    /// </summary>
    public const double MinimumSetDoglegLengthMm = 10d;

    /// <summary>
    /// Direction magnitude below this is treated as Vector3d.Zero.
    /// </summary>
    public const double DirectionLengthTolerance = 1e-12d;

    /// <summary>
    /// True when <paramref name="dirX"/>/<paramref name="dirY"/> form a finite,
    /// non-zero planar direction. Writes the unit vector.
    /// </summary>
    public static bool TryNormalizeDirection(
        double dirX,
        double dirY,
        out double unitX,
        out double unitY)
    {
        unitX = 0d;
        unitY = 0d;
        if (double.IsNaN(dirX) ||
            double.IsNaN(dirY) ||
            double.IsInfinity(dirX) ||
            double.IsInfinity(dirY))
        {
            return false;
        }

        var length = Math.Sqrt((dirX * dirX) + (dirY * dirY));
        if (length <= DirectionLengthTolerance)
        {
            return false;
        }

        unitX = dirX / length;
        unitY = dirY / length;
        return true;
    }

    /// <summary>
    /// True when both DoglegLength and direction are safe for <c>SetDogleg</c>.
    /// </summary>
    public static bool ShouldCallSetDogleg(
        double doglegLengthMm,
        double dirX,
        double dirY,
        out double unitX,
        out double unitY)
    {
        unitX = 0d;
        unitY = 0d;
        if (double.IsNaN(doglegLengthMm) ||
            double.IsInfinity(doglegLengthMm) ||
            doglegLengthMm < MinimumSetDoglegLengthMm)
        {
            return false;
        }

        return TryNormalizeDirection(dirX, dirY, out unitX, out unitY);
    }

    /// <summary>
    /// Standalone Plain/Dimensions CREATE must not pass dogleg overrides into
    /// <c>ApplyInstanceProperties</c> before <c>AppendEntity</c>. Dogleg is
    /// applied only by post-append landing finalization.
    /// </summary>
    public static bool DeferSetDoglegUntilDatabaseResidentForStandaloneNative =>
        true;
}
