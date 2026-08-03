using System.Drawing.Text;
using System.Collections.Concurrent;
using System.IO;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed record AutoCadDiscoveredFont(
    string DisplayName,
    string FontFile);

/// <summary>
/// Lists TrueType/OpenType fonts already available to Windows/AutoCAD.
/// Does not copy, bundle or redistribute font files.
/// </summary>
internal static class AutoCadFontDiscoveryService
{
    private static readonly ConcurrentDictionary<string, byte> AvailableFonts =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object Sync = new();
    private static IReadOnlyList<AutoCadDiscoveredFont>? _cached;

    public static IReadOnlyList<AutoCadDiscoveredFont> ListAvailableFonts()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        lock (Sync)
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var fonts = new SortedDictionary<string, AutoCadDiscoveredFont>(
                StringComparer.OrdinalIgnoreCase);
            try
            {
                using var collection = new InstalledFontCollection();
                foreach (var family in collection.Families)
                {
                    var name = family.Name?.Trim();
                    if (string.IsNullOrWhiteSpace(name) ||
                        name.Length > 255 ||
                        name.IndexOfAny(['\\', '/', ':']) >= 0)
                    {
                        continue;
                    }

                    // Skip specialty vertical / symbol-only families that are
                    // unsuitable for normal DBText / MText / MLeader content.
                    if (name.StartsWith("@", StringComparison.Ordinal) ||
                        name.Contains("Vertical", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    fonts[name] = new AutoCadDiscoveredFont(name, name);
                    AvailableFonts[name] = 0;
                }
            }
            catch
            {
                // Font enumeration must never crash Settings. Fall back to the
                // two built-in fonts the app owns when Windows enumeration fails.
                fonts["Arial"] = new AutoCadDiscoveredFont("Arial", "Arial");
                fonts["Times New Roman"] = new AutoCadDiscoveredFont(
                    "Times New Roman",
                    "Times New Roman");
                AvailableFonts["Arial"] = 0;
                AvailableFonts["Times New Roman"] = 0;
            }

            EnsureSeedFonts(fonts);
            _cached = fonts.Values.ToArray();
            return _cached;
        }
    }

    public static bool IsFontAvailable(string? fontFile)
    {
        var normalized = fontFile?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        _ = ListAvailableFonts();
        if (AvailableFonts.ContainsKey(normalized))
        {
            return true;
        }

        var withoutExtension = Path.GetFileNameWithoutExtension(normalized);
        return !string.IsNullOrWhiteSpace(withoutExtension) &&
            AvailableFonts.ContainsKey(withoutExtension);
    }

    private static void EnsureSeedFonts(
        IDictionary<string, AutoCadDiscoveredFont> fonts)
    {
        foreach (var seed in new[] { "Arial", "Times New Roman" })
        {
            if (!fonts.ContainsKey(seed))
            {
                fonts[seed] = new AutoCadDiscoveredFont(seed, seed);
            }

            AvailableFonts[seed] = 0;
        }
    }
}
