using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Classifies display validation and group presence without mutating either.</summary>
public static class RoofDisplayLifecycleClassifier
{
    public static RoofDisplayLifecycleKind Classify(
        RoofDisplayValidationResult validation,
        bool groupIsCurrent)
    {
        if (validation is null)
        {
            throw new ArgumentNullException(nameof(validation));
        }
        if (validation.Issues.HasFlag(RoofDisplayValidationIssue.UnsupportedFutureSchema))
        {
            return RoofDisplayLifecycleKind.UnsupportedFutureSchema;
        }

        if (validation.IsCurrent)
        {
            return groupIsCurrent
                ? RoofDisplayLifecycleKind.Current
                : RoofDisplayLifecycleKind.GroupMissingRehydratable;
        }

        return validation.State == RoofDisplayState.Missing
            ? RoofDisplayLifecycleKind.MissingDisplay
            : RoofDisplayLifecycleKind.StaleDisplay;
    }
}
