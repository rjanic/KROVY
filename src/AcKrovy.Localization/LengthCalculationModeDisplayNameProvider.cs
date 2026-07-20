using System.Globalization;
using AcKrovy.Core.Models;

namespace AcKrovy.Localization;

public static class LengthCalculationModeDisplayNameProvider
{
    public static string GetDisplayName(LengthCalculationMode mode, CultureInfo? culture = null) =>
        mode switch
        {
            LengthCalculationMode.AutoByElementType => UiStrings.GetString("LengthCalculationMode_AutoByElementType", culture),
            LengthCalculationMode.PlanLength => UiStrings.GetString("LengthCalculationMode_PlanLength", culture),
            LengthCalculationMode.SlopeCorrected => UiStrings.GetString("LengthCalculationMode_SlopeCorrected", culture),
            LengthCalculationMode.ManualLength => UiStrings.GetString("LengthCalculationMode_ManualLength", culture),
            _ => mode.ToString(),
        };
}
