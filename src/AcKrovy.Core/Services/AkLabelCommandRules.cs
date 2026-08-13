using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Neutral AK_LABEL decision rules. Host executes Ensure/delete; Core never
/// touches CAD entities.
/// </summary>
public static class AkLabelCommandRules
{
    public static bool ExpectsMainAnnotation(TimberAnnotationMode mode) =>
        TimberAnnotationModeRules.Normalize(mode) != TimberAnnotationMode.NoAnnotations;

    public static AkLabelSourceAction Decide(
        AkLabelIntention intention,
        TimberAnnotationMode annotationMode,
        bool hasExistingMainAnnotation)
    {
        var expects = ExpectsMainAnnotation(annotationMode);

        switch (intention)
        {
            case AkLabelIntention.MissingOnly:
                if (expects && hasExistingMainAnnotation)
                {
                    // Existing presentation must survive — including classic
                    // AutoCAD ROTATE / MOVE / annotation grips.
                    return AkLabelSourceAction.NoOp;
                }

                if (!expects && !hasExistingMainAnnotation)
                {
                    return AkLabelSourceAction.NoOp;
                }

                // Missing expected annotation, or NoAnnotations cleanup of leftovers.
                return AkLabelSourceAction.EnsureMissing;

            case AkLabelIntention.ResetSelected:
            case AkLabelIntention.ResetAll:
                if (!expects && !hasExistingMainAnnotation)
                {
                    return AkLabelSourceAction.NoOp;
                }

                return AkLabelSourceAction.ForceCanonicalRecreate;

            default:
                return AkLabelSourceAction.NoOp;
        }
    }

    public static bool HasExistingMainAnnotationForSource(
        string sourceHandle,
        IEnumerable<string> existingMainAnnotationSourceHandles)
    {
        if (string.IsNullOrWhiteSpace(sourceHandle) ||
            existingMainAnnotationSourceHandles is null)
        {
            return false;
        }

        var normalized = sourceHandle.Trim();
        foreach (var candidate in existingMainAnnotationSourceHandles)
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                string.Equals(candidate.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
