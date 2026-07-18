using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementDefaultApplicator
{
    public static TimberElementData ApplyCuttingAllowance(
        TimberElementData source,
        TimberElementDefaultProfile profile)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        return source with
        {
            CuttingAllowanceMm = profile.GetCuttingAllowanceMm(source.ElementType),
        };
    }
}
