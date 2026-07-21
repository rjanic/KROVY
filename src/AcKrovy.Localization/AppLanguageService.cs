using System.Globalization;

namespace AcKrovy.Localization;

public sealed class SupportedAppLanguage
{
    internal SupportedAppLanguage(string code, string nativeName)
    {
        Code = code;
        NativeName = nativeName;
    }

    public string Code { get; }
    public string NativeName { get; }
}

public sealed class AppLanguageChangedEventArgs : EventArgs
{
    internal AppLanguageChangedEventArgs(string previousLanguageCode, string languageCode)
    {
        PreviousLanguageCode = previousLanguageCode;
        LanguageCode = languageCode;
    }

    public string PreviousLanguageCode { get; }
    public string LanguageCode { get; }
}

/// <summary>
/// Owns the presentation language only. It never changes CurrentCulture, technical
/// serialization, CAD identifiers, or drawing data.
/// </summary>
public static class AppLanguageService
{
    public const string DefaultLanguageCode = "sk";

    private static readonly IReadOnlyList<SupportedAppLanguage> Languages =
    [
        new("sk", "Slovenčina"),
        new("cs", "Čeština"),
        new("en", "English"),
        new("de", "Deutsch"),
        new("pl", "Polski"),
        new("fr", "Français"),
    ];

    private static readonly HashSet<string> SupportedCodes = new(
        Languages.Select(item => item.Code),
        StringComparer.OrdinalIgnoreCase);

    private static string _currentLanguageCode = DefaultLanguageCode;

    public static IReadOnlyList<SupportedAppLanguage> SupportedLanguages => Languages;
    public static string CurrentLanguageCode => _currentLanguageCode;

    public static event EventHandler<AppLanguageChangedEventArgs>? LanguageChanged;

    public static string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return DefaultLanguageCode;
        }

        var normalized = languageCode!.Trim().Replace('_', '-');
        var separatorIndex = normalized.IndexOf('-');
        if (separatorIndex >= 0)
        {
            normalized = normalized.Substring(0, separatorIndex);
        }

        return SupportedCodes.Contains(normalized)
            ? normalized.ToLowerInvariant()
            : DefaultLanguageCode;
    }

    public static CultureInfo GetCultureInfo(string? languageCode) =>
        CultureInfo.GetCultureInfo(NormalizeLanguageCode(languageCode));

    public static string Apply(string? languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        var culture = GetCultureInfo(normalized);
        var previous = _currentLanguageCode;

        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        _currentLanguageCode = normalized;

        if (string.Equals(UiStringBindingSource.Shared.Culture?.Name, culture.Name, StringComparison.OrdinalIgnoreCase))
        {
            UiStringBindingSource.Shared.Refresh();
        }
        else
        {
            UiStringBindingSource.Shared.Culture = culture;
        }

        if (!string.Equals(previous, normalized, StringComparison.Ordinal))
        {
            LanguageChanged?.Invoke(null, new AppLanguageChangedEventArgs(previous, normalized));
        }

        return normalized;
    }
}
