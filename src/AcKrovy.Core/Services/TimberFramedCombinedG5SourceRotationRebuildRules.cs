namespace AcKrovy.Core.Services;

/// <summary>
/// Decides whether an existing production R3 Combined annotation must be
/// replaced after the physical Start→End source direction changes. Presentation,
/// readable, landing and block angles are never detector inputs. The directed
/// physical difference is compared modulo one full turn so +90° and -90°
/// remain distinct CREATE families.
/// </summary>
public static class TimberFramedCombinedG5SourceRotationRebuildRules
{
    public const string SourceAxisChangedReason = "SourceAxisChanged";
    public const string SourceAxisUnchangedReason = "SourceAxisUnchanged";

    /// <summary>
    /// Resolves the physical previous source direction from existing G5 metadata.
    /// New metadata stores physical Start→End in RotationRadians and readable
    /// placement in PlacementRotationRadians. Older R3 payloads wrote the readable
    /// angle into both fields; for those payloads choose the π-equivalent physical
    /// axis nearest to the current source geometry. Exact vertical boundaries keep
    /// their directed semantics because +90°/-90° select distinct CREATE families.
    /// </summary>
    public static TimberFramedCombinedG5SourceRotationRebuildDecision
        DecideFromPersistedMetadata(
            double? persistedSourcePhysicalAxisRadians,
            double? persistedPlacementReadableAxisRadians,
            double currentSourcePhysicalAxisRadians,
            double toleranceRadians =
                TimberFramedCombinedG5SourceRotationRules.RotationToleranceRadians)
    {
        if (!persistedSourcePhysicalAxisRadians.HasValue)
        {
            return Decide(
                currentSourcePhysicalAxisRadians,
                currentSourcePhysicalAxisRadians,
                toleranceRadians);
        }

        var persisted = persistedSourcePhysicalAxisRadians.Value;
        var previousPhysical = persisted;
        var legacyReadableInBothFields =
            persistedPlacementReadableAxisRadians is double placement &&
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                persisted - placement)) <= toleranceRadians;
        if (legacyReadableInBothFields &&
            !IsExactVertical(persisted, toleranceRadians))
        {
            previousPhysical = NearestLineAxisEquivalent(
                persisted,
                currentSourcePhysicalAxisRadians);
        }

        return Decide(
            previousPhysical,
            currentSourcePhysicalAxisRadians,
            toleranceRadians);
    }

    public static TimberFramedCombinedG5SourceRotationRebuildDecision Decide(
        double sourceAxisBeforeRadians,
        double sourceAxisAfterRadians,
        double toleranceRadians =
            TimberFramedCombinedG5SourceRotationRules.RotationToleranceRadians)
    {
        if (double.IsNaN(sourceAxisBeforeRadians) ||
            double.IsInfinity(sourceAxisBeforeRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceAxisBeforeRadians));
        }

        if (double.IsNaN(sourceAxisAfterRadians) ||
            double.IsInfinity(sourceAxisAfterRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceAxisAfterRadians));
        }

        if (toleranceRadians < 0d ||
            double.IsNaN(toleranceRadians) ||
            double.IsInfinity(toleranceRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(toleranceRadians));
        }

        var before =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                sourceAxisBeforeRadians);
        var after =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                sourceAxisAfterRadians);
        var delta = TimberAnnotationReadabilityRules.NormalizeAngleDelta(
            after - before);
        var changed = Math.Abs(delta) > toleranceRadians;
        return new TimberFramedCombinedG5SourceRotationRebuildDecision(
            before,
            after,
            delta,
            SourceRotationDetected: changed,
            AnnotationRebuildRequired: changed,
            RebuildReason: changed
                ? SourceAxisChangedReason
                : SourceAxisUnchangedReason);
    }

    private static bool IsExactVertical(double radians, double toleranceRadians)
    {
        var wrapped =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(radians);
        return Math.Abs(Math.Abs(wrapped) - (Math.PI / 2d)) <= toleranceRadians;
    }

    private static double NearestLineAxisEquivalent(
        double persistedReadableRadians,
        double currentPhysicalRadians)
    {
        var best = persistedReadableRadians;
        var bestDelta = Math.Abs(
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                currentPhysicalRadians - best));
        foreach (var candidate in new[]
                 {
                     persistedReadableRadians + Math.PI,
                     persistedReadableRadians - Math.PI,
                 })
        {
            var delta = Math.Abs(
                TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                    currentPhysicalRadians - candidate));
            if (delta < bestDelta)
            {
                best = candidate;
                bestDelta = delta;
            }
        }

        return best;
    }
}

public sealed record TimberFramedCombinedG5SourceRotationRebuildDecision(
    double SourceAxisBeforeRadians,
    double SourceAxisAfterRadians,
    double SourceAxisDeltaRadians,
    bool SourceRotationDetected,
    bool AnnotationRebuildRequired,
    string RebuildReason);
