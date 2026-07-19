namespace AcKrovy.Core.Services;

public static class LiveGeometryCommandRules
{
    public static bool RequiresFullTimberAnnotationRefresh(string? globalCommandName)
    {
        if (string.IsNullOrWhiteSpace(globalCommandName))
        {
            return false;
        }

        var normalized = globalCommandName!.Trim().TrimStart('_', '.');
        return string.Equals(normalized, "ROTATE", StringComparison.OrdinalIgnoreCase);
    }
}
