namespace AcKrovy.Core.Services;

/// <summary>
/// Unchanged-axis R3 Combined refresh presentation rules. True physical source
/// rotation is routed by
/// <see cref="TimberFramedCombinedG5SourceRotationRebuildRules"/> to a fresh
/// canonical CREATE and never transforms the existing MLeader in place.
/// </summary>
public static class TimberFramedCombinedG5SourceRotationRules
{
    public const double RotationToleranceRadians = 1e-9d;

    /// <summary>
    /// White radial matrix for CREATE → source STRETCH → live-refresh
    /// orientation proofs (degrees).
    /// </summary>
    public static IReadOnlyList<double> StretchOrientationAnglesDegrees { get; } =
    [
        0d, 35d, 90d, 145d, 180d, 215d, 270d, 325d,
    ];

    public static bool RotationChanged(
        double oldRotationRadians,
        double newRotationRadians,
        double toleranceRadians = RotationToleranceRadians)
    {
        if (toleranceRadians < 0d ||
            double.IsNaN(toleranceRadians) ||
            double.IsInfinity(toleranceRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(toleranceRadians));
        }

        // Undirected source axis (mod π): reverse Start/End and length-only
        // edits are identity — including vertical 90°↔270° where readable
        // half-plane keeps both ±90° as distinct directed values.
        var oldAxis = UndirectedAxisRadians(oldRotationRadians);
        var newAxis = UndirectedAxisRadians(newRotationRadians);
        var delta = Math.Abs(oldAxis - newAxis);
        var undirectedDelta = Math.Min(delta, Math.PI - delta);
        return undirectedDelta > toleranceRadians;
    }

    /// <summary>
    /// Fold physical/readable orientation to an undirected axis in [0, π).
    /// </summary>
    public static double UndirectedAxisRadians(double rotationRadians)
    {
        var wrapped =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                rotationRadians);
        var axis = wrapped;
        while (axis < 0d)
        {
            axis += Math.PI;
        }

        while (axis >= Math.PI)
        {
            axis -= Math.PI;
        }

        return axis;
    }

    /// <summary>
    /// Layer C presentation for in-place Combined refresh after source edit.
    /// Length-only / reverse Start→End (readable axis unchanged) keeps the live
    /// CREATE/grip presentation. True readable-axis change adopts
    /// <see cref="TimberFramedBlockContentReadableOrientationRules.Decide"/> for
    /// the new axis — same presentation a fresh CREATE would use. Production
    /// rejects that changed-axis branch before calling this method. Never folds
    /// from knee/landing.
    /// </summary>
    public static double ResolveRefreshPresentationRadians(
        double oldRotationRadians,
        double newRotationRadians,
        double livePresentationRadians,
        double toleranceRadians = RotationToleranceRadians)
    {
        if (double.IsNaN(livePresentationRadians) ||
            double.IsInfinity(livePresentationRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(livePresentationRadians));
        }

        if (!RotationChanged(
                oldRotationRadians,
                newRotationRadians,
                toleranceRadians))
        {
            return TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                livePresentationRadians);
        }

        return TimberFramedBlockContentReadableOrientationRules
            .Decide(newRotationRadians)
            .PresentationAngle;
    }

}
