using System.Text.RegularExpressions;

namespace AcKrovy.Infrastructure.Diagnostics;

public static class DiagnosticSanitizer
{
    private static readonly Regex DrawingPathPattern = new(
        @"(?i)(?:[a-z]:[\\/]|\\\\|//)[^\r\n|]*?\.dwg",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = DrawingPathPattern.Replace(value!, "<drawing>");
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localApplicationData))
        {
            sanitized = ReplacePath(
                sanitized,
                localApplicationData,
                "%LOCALAPPDATA%");
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            sanitized = ReplacePath(
                sanitized,
                userProfile,
                "%USERPROFILE%");
        }

        var userName = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName))
        {
            sanitized = Regex.Replace(
                sanitized,
                Regex.Escape(userName),
                "<user>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return sanitized;
    }

    private static string ReplacePath(string value, string path, string token) =>
        Regex.Replace(
            value,
            Regex.Escape(path),
            token,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
