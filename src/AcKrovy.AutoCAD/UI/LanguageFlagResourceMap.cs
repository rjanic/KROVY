namespace AcKrovy.AutoCAD.UI;

internal sealed record LanguageFlagResource(
    string LanguageCode,
    string ColorFileName,
    string BlackFileName)
{
    private const string PackBase =
        "pack://application:,,,/AcKrovy.AutoCAD;component/UI/Assets/Flags/";

    public string ColorPackUri => PackBase + ColorFileName;
    public string BlackPackUri => PackBase + BlackFileName;
}

internal static class LanguageFlagResourceMap
{
    private static readonly IReadOnlyDictionary<string, LanguageFlagResource> Resources =
        new Dictionary<string, LanguageFlagResource>(StringComparer.OrdinalIgnoreCase)
        {
            ["sk"] = new("sk", "sk_flag.png", "sk_flag_black.png"),
            ["cs"] = new("cs", "cz_flag.png", "cz_flag_black.png"),
            ["en"] = new("en", "gb_flag.png", "gb_flag_black.png"),
            ["de"] = new("de", "de_flag.png", "de_flag_black.png"),
            ["pl"] = new("pl", "pl_flag.png", "pl_flag_black.png"),
            ["fr"] = new("fr", "fr_flag.png", "fr_flag_black.png"),
        };

    public static LanguageFlagResource Get(string languageCode)
    {
        var normalized = AcKrovy.Localization.AppLanguageService.NormalizeLanguageCode(
            languageCode);
        return Resources.TryGetValue(normalized, out var resource)
            ? resource
            : Resources[AcKrovy.Localization.AppLanguageService.DefaultLanguageCode];
    }

    public static IReadOnlyCollection<LanguageFlagResource> All =>
        Resources.Values.ToArray();
}
