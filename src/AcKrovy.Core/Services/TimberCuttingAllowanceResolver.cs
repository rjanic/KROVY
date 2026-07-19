using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberCuttingAllowanceResolver
{
    public static double Resolve(
        TimberElementType elementType,
        double? perElementOverrideMm,
        TimberElementDefaultProfile? defaultProfile = null)
    {
        if (perElementOverrideMm.HasValue)
        {
            return Math.Max(0, perElementOverrideMm.Value);
        }

        return (defaultProfile ?? TimberElementDefaultProfile.CreateDefault())
            .GetCuttingAllowanceMm(elementType);
    }
}
