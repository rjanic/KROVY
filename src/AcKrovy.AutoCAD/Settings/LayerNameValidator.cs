namespace AcKrovy.AutoCAD.Settings;

internal static class LayerNameValidator
{
    // Základná množina znakov, ktoré AutoCAD nepovoľuje v názve symbolu/h hladiny.
    private static readonly char[] InvalidCharacters = ['<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '\''];

    public static bool TryValidate(string? rawLayerName, out string normalizedLayerName, out string error)
    {
        normalizedLayerName = rawLayerName?.Trim() ?? string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedLayerName))
        {
            error = "Názov hladiny nesmie byť prázdny.";
            return false;
        }

        if (normalizedLayerName.Length > 255)
        {
            error = "Názov hladiny je príliš dlhý.";
            return false;
        }

        if (normalizedLayerName.IndexOfAny(InvalidCharacters) >= 0)
        {
            error = "Názov hladiny obsahuje nepovolený znak.";
            return false;
        }

        return true;
    }
}
