namespace AcKrovy.AutoCAD.Settings;

using AcKrovy.Localization;

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
            error = UiStrings.ErrorLayerNameEmpty;
            return false;
        }

        if (normalizedLayerName.Length > 255)
        {
            error = UiStrings.ErrorLayerNameTooLong;
            return false;
        }

        if (normalizedLayerName.IndexOfAny(InvalidCharacters) >= 0)
        {
            error = UiStrings.ErrorLayerNameInvalidCharacter;
            return false;
        }

        return true;
    }
}
