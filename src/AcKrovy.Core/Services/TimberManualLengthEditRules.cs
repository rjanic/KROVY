using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberManualLengthEditRules
{
    public static bool CanEdit(
        IReadOnlyCollection<TimberElementData> elements,
        TimberElementType? elementTypeOverride = null,
        LengthCalculationMode? lengthModeOverride = null)
    {
        if (elements is null)
        {
            throw new ArgumentNullException(nameof(elements));
        }

        return elements.Count > 0 && elements.All(element =>
        {
            var candidate = element with
            {
                ElementType = elementTypeOverride ?? element.ElementType,
                LengthCalculationMode = lengthModeOverride ?? element.LengthCalculationMode,
            };

            return TimberCalculator.ResolveLengthCalculationMode(candidate) == LengthCalculationMode.ManualLength;
        });
    }
}
