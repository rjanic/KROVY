namespace AcKrovy.Core.Services;

/// <summary>
/// Persistent lifecycle policy for the host-proven whole-MLeader vertical
/// correction. Revision 3 means that the rigid annotation half-turn is present;
/// revisions 0-2 retain their existing reference-presentation meaning and mean
/// that the whole-annotation half-turn is absent.
/// </summary>
public static class TimberFramedBlockContentWholeAnnotationHalfTurnRules
{
    public const int AppliedStateRevision = 3;

    public static bool RequiresWholeAnnotationHalfTurn(
        double sourcePhysicalAxisAngleRadians)
    {
        var physical = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            sourcePhysicalAxisAngleRadians);
        return Math.Abs(Math.Abs(physical) - (Math.PI / 2d)) <=
            TimberFramedBlockContentReadableOrientationRules.AngleToleranceRadians;
    }

    public static bool IsWholeAnnotationHalfTurnApplied(int currentRevision) =>
        currentRevision == AppliedStateRevision;

    public static WholeAnnotationHalfTurnDecision Decide(
        double sourcePhysicalAxisAngleRadians,
        int currentRevision)
    {
        var required = RequiresWholeAnnotationHalfTurn(
            sourcePhysicalAxisAngleRadians);
        var appliedBefore = IsWholeAnnotationHalfTurnApplied(currentRevision);
        var transformRequired = required != appliedBefore;
        var revisionAfter = required
            ? AppliedStateRevision
            : appliedBefore
                ? TimberFramedBlockContentReadableOrientationRules
                    .ReferencePresentationRevision
                : currentRevision;

        return new WholeAnnotationHalfTurnDecision(
            Required: required,
            AppliedBefore: appliedBefore,
            TransformRequired: transformRequired,
            AppliedAfter: required,
            RevisionBefore: currentRevision,
            RevisionAfter: revisionAfter);
    }
}

public sealed record WholeAnnotationHalfTurnDecision(
    bool Required,
    bool AppliedBefore,
    bool TransformRequired,
    bool AppliedAfter,
    int RevisionBefore,
    int RevisionAfter);
