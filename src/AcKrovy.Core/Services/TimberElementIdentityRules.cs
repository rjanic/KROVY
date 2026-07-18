using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementIdentityRules
{
    public static string CreateElementId(TimberElementType type, int number)
    {
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Číslo prvku musí byť kladné.");
        }

        return $"{TimberElementLabels.Prefix(type)}{number}";
    }
}
