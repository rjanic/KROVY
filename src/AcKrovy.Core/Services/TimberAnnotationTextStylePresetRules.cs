using System.Text;
using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberAnnotationTextStylePresetRules
{
    public const string ArchitecturalStyleName = "AK_KROVY_ARCHITECTURAL";
    public const string ClassicStyleName = "AK_KROVY_CLASSIC";
    public const string TechnicalStyleName = "AK_KROVY_TECHNICAL";
    public const string ArialStyleName = "AK_KROVY_ARIAL";
    public const string LegacyArchitecturalStyleName = "AK_KROVY_ARCH";
    public const string ArchitecturalFontFile = "Arial Narrow";
    public const string ClassicFontFile = "romans.shx";
    // Audited against AutoCAD's ISO style: isocp.shx, XScale 1, oblique 0,
    // variable height and no big font.
    public const string TechnicalFontFile = "isocp.shx";
    public const string ArialFontFile = "Arial";
    public const string ArchitecturalStableId = "architectural";
    public const string ClassicStableId = "classic";
    public const string TechnicalStableId = "technical";
    public const string ArialStableId = "arial";
    public const string UserStyleNamePrefix = "AK_KROVY_USER_";
    public const string ArchitecturalLocalizationKey = "SettingsTextStylePreset_Architectural";
    public const string ClassicLocalizationKey = "SettingsTextStylePreset_Classic";
    public const string TechnicalLocalizationKey = "SettingsTextStylePreset_Technical";
    public const string ArialLocalizationKey = "SettingsTextStylePreset_Arial";

    public const double DefaultWidthFactor = 1.0d;
    public const double DefaultObliqueAngleDegrees = 0.0d;
    public const double MinimumWidthFactor = 0.1d;
    public const double MaximumWidthFactor = 10.0d;
    public const double MinimumObliqueAngleDegrees = -85.0d;
    public const double MaximumObliqueAngleDegrees = 85.0d;
    public const int MaximumDisplayNameLength = 64;
    public const int MaximumFontFileLength = 255;
    public const int MaximumStableIdLength = 64;

    private static readonly TimberAnnotationTextStylePresetDefinition ArchitecturalDefinition =
        new(
            ArchitecturalStableId,
            TimberAnnotationTextStylePresetKind.BuiltIn,
            TimberAnnotationBuiltInTextStylePreset.Architectural,
            ArchitecturalLocalizationKey,
            DisplayName: null,
            ArchitecturalStyleName,
            ArchitecturalFontFile,
            DefaultWidthFactor,
            DefaultObliqueAngleDegrees);

    private static readonly TimberAnnotationTextStylePresetDefinition ClassicDefinition =
        new(
            ClassicStableId,
            TimberAnnotationTextStylePresetKind.BuiltIn,
            TimberAnnotationBuiltInTextStylePreset.Classic,
            ClassicLocalizationKey,
            DisplayName: null,
            ClassicStyleName,
            ClassicFontFile,
            DefaultWidthFactor,
            DefaultObliqueAngleDegrees);

    private static readonly TimberAnnotationTextStylePresetDefinition TechnicalDefinition =
        new(
            TechnicalStableId,
            TimberAnnotationTextStylePresetKind.BuiltIn,
            TimberAnnotationBuiltInTextStylePreset.Technical,
            TechnicalLocalizationKey,
            DisplayName: null,
            TechnicalStyleName,
            TechnicalFontFile,
            DefaultWidthFactor,
            DefaultObliqueAngleDegrees);

    private static readonly TimberAnnotationTextStylePresetDefinition ArialDefinition =
        new(
            ArialStableId,
            TimberAnnotationTextStylePresetKind.BuiltIn,
            TimberAnnotationBuiltInTextStylePreset.Arial,
            ArialLocalizationKey,
            DisplayName: null,
            ArialStyleName,
            ArialFontFile,
            DefaultWidthFactor,
            DefaultObliqueAngleDegrees);

    private static readonly IReadOnlyList<TimberAnnotationTextStylePresetDefinition> BuiltInDefinitions =
        new[]
        {
            ArchitecturalDefinition,
            ClassicDefinition,
            TechnicalDefinition,
            ArialDefinition,
        };

    public static IReadOnlyList<TimberAnnotationTextStylePresetDefinition> GetBuiltInDefinitions() =>
        BuiltInDefinitions;

    public static TimberAnnotationTextStylePresetDefinition GetBuiltIn(
        TimberAnnotationBuiltInTextStylePreset preset) =>
        preset switch
        {
            TimberAnnotationBuiltInTextStylePreset.Architectural => ArchitecturalDefinition,
            TimberAnnotationBuiltInTextStylePreset.Classic => ClassicDefinition,
            TimberAnnotationBuiltInTextStylePreset.Technical => TechnicalDefinition,
            TimberAnnotationBuiltInTextStylePreset.Arial => ArialDefinition,
            _ => ArialDefinition,
        };

    public static bool TryResolveBuiltInByStyleName(
        string? styleName,
        out TimberAnnotationTextStylePresetDefinition? definition)
    {
        var normalized = styleName?.Trim();
        definition = BuiltInDefinitions.FirstOrDefault(candidate =>
            string.Equals(
                normalized,
                candidate.AutoCadTextStyleName,
                StringComparison.OrdinalIgnoreCase));
        if (definition is not null)
        {
            return true;
        }

        definition = null;
        return false;
    }

    public static bool TryResolveBuiltInByStableId(
        string? stableId,
        out TimberAnnotationTextStylePresetDefinition? definition)
    {
        var normalized = stableId?.Trim();
        definition = BuiltInDefinitions.FirstOrDefault(candidate =>
            string.Equals(normalized, candidate.StableId, StringComparison.OrdinalIgnoreCase));
        if (definition is not null)
        {
            return true;
        }

        definition = null;
        return false;
    }

    /// <summary>
    /// Fresh-profile factory defaults: Klasický style name with the shared paper
    /// heights. This is also the explicit product fallback for legacy null text
    /// settings; the current DWG text style is never a product default.
    /// </summary>
    public static TimberAnnotationTextSettings CreateFreshProfileTextSettings() =>
        TimberAnnotationTextSettings.Shared(
            ClassicStyleName,
            TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
            TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
            TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm);

    public static bool IsBuiltInStyleName(string? styleName)
    {
        var normalized = styleName?.Trim();
        return BuiltInDefinitions.Any(definition =>
            string.Equals(
                normalized,
                definition.AutoCadTextStyleName,
                StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsAppOwnedStyleName(string? styleName)
    {
        var normalized = styleName?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        return IsBuiltInStyleName(normalized) ||
            normalized!.StartsWith(UserStyleNamePrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidDisplayName(string? displayName)
    {
        var normalized = displayName?.Trim();
        return normalized is not null &&
            normalized.Length > 0 &&
            normalized.Length <= MaximumDisplayNameLength &&
            !normalized.Any(char.IsControl);
    }

    public static bool IsValidStableId(string? stableId)
    {
        var normalized = stableId?.Trim();
        if (normalized is null ||
            normalized.Length == 0 ||
            normalized.Length > MaximumStableIdLength ||
            normalized.Any(char.IsControl))
        {
            return false;
        }

        if (BuiltInDefinitions.Any(definition =>
                string.Equals(
                    normalized,
                    definition.StableId,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    public static bool IsValidFontFile(string? fontFile)
    {
        var normalized = fontFile?.Trim();
        return normalized is not null &&
            normalized.Length > 0 &&
            normalized.Length <= MaximumFontFileLength &&
            !normalized.Any(char.IsControl);
    }

    public static string ValidateAndNormalizeFontFile(string fontFile)
    {
        if (!IsValidFontFile(fontFile))
        {
            throw new ArgumentException(
                $"Font file must contain between 1 and {MaximumFontFileLength} non-control characters.",
                nameof(fontFile));
        }

        return fontFile.Trim();
    }

    public static bool IsValidWidthFactor(double value) =>
        IsWithinInclusiveRange(value, MinimumWidthFactor, MaximumWidthFactor);

    public static bool IsValidObliqueAngle(double value) =>
        IsWithinInclusiveRange(
            value,
            MinimumObliqueAngleDegrees,
            MaximumObliqueAngleDegrees);

    public static string GenerateUserAutoCadTextStyleName(string stableId)
    {
        if (!IsValidStableId(stableId))
        {
            throw new ArgumentException(
                $"Stable id must contain between 1 and {MaximumStableIdLength} non-control characters and must not reuse a built-in id.",
                nameof(stableId));
        }

        return BuildAutoCadTextStyleName(stableId.Trim());
    }

    /// <summary>
    /// Deterministic AutoCAD style name from a stable id. Callers must already
    /// validate the stable id when they need rejection semantics.
    /// </summary>
    public static string BuildAutoCadTextStyleName(string stableId)
    {
        if (stableId is null)
        {
            throw new ArgumentNullException(nameof(stableId));
        }

        var builder = new StringBuilder(UserStyleNamePrefix.Length + stableId.Length);
        builder.Append(UserStyleNamePrefix);
        foreach (var character in stableId)
        {
            builder.Append(
                char.IsLetterOrDigit(character)
                    ? char.ToUpperInvariant(character)
                    : '_');
        }

        return builder.ToString();
    }

    public static string EnsureUniqueDisplayName(
        string displayName,
        IEnumerable<TimberAnnotationUserTextStylePreset>? existingPresets,
        string? excludeStableId = null)
    {
        if (!IsValidDisplayName(displayName))
        {
            throw new ArgumentException(
                $"Display name must contain between 1 and {MaximumDisplayNameLength} non-control characters.",
                nameof(displayName));
        }

        var trimmed = displayName.Trim();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in existingPresets ?? Enumerable.Empty<TimberAnnotationUserTextStylePreset>())
        {
            if (preset is null)
            {
                continue;
            }

            if (excludeStableId is not null &&
                string.Equals(
                    preset.StableId?.Trim(),
                    excludeStableId.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var existingName = preset.DisplayName?.Trim();
            if (!string.IsNullOrEmpty(existingName))
            {
                taken.Add(existingName!);
            }
        }

        if (!taken.Contains(trimmed))
        {
            return trimmed;
        }

        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            var candidate = $"{trimmed} ({suffix})";
            if (candidate.Length > MaximumDisplayNameLength)
            {
                break;
            }

            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new ArgumentException(
            $"Unable to allocate a unique display name for '{trimmed}'.",
            nameof(displayName));
    }

    public static TimberAnnotationUserTextStylePreset ValidateAndNormalizeUserPreset(
        TimberAnnotationUserTextStylePreset preset,
        IEnumerable<TimberAnnotationUserTextStylePreset>? existingPresets = null)
    {
        if (preset is null)
        {
            throw new ArgumentNullException(nameof(preset));
        }

        if (!IsValidStableId(preset.StableId))
        {
            throw new ArgumentException(
                $"Stable id must contain between 1 and {MaximumStableIdLength} non-control characters and must not reuse a built-in id.",
                nameof(preset));
        }

        if (!IsValidFontFile(preset.FontFile))
        {
            throw new ArgumentException(
                $"Font file must contain between 1 and {MaximumFontFileLength} non-control characters.",
                nameof(preset));
        }

        if (!IsValidWidthFactor(preset.WidthFactor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(preset),
                preset.WidthFactor,
                $"Width factor must be between {MinimumWidthFactor} and {MaximumWidthFactor}.");
        }

        if (!IsValidObliqueAngle(preset.ObliqueAngleDegrees))
        {
            throw new ArgumentOutOfRangeException(
                nameof(preset),
                preset.ObliqueAngleDegrees,
                $"Oblique angle must be between {MinimumObliqueAngleDegrees} and {MaximumObliqueAngleDegrees} degrees.");
        }

        var stableId = preset.StableId.Trim();
        var autoCadStyleName = BuildAutoCadTextStyleName(stableId);
        if ((existingPresets ?? Enumerable.Empty<TimberAnnotationUserTextStylePreset>())
            .Any(existing =>
                existing is not null &&
                !string.Equals(
                    existing.StableId?.Trim(),
                    stableId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    BuildAutoCadTextStyleName(existing.StableId?.Trim() ?? string.Empty),
                    autoCadStyleName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Stable id maps to an AutoCAD text-style name already in use.",
                nameof(preset));
        }
        var displayName = EnsureUniqueDisplayName(
            preset.DisplayName,
            existingPresets,
            excludeStableId: stableId);

        return new TimberAnnotationUserTextStylePreset
        {
            StableId = stableId,
            DisplayName = displayName,
            FontFile = preset.FontFile.Trim(),
            AutoCadTextStyleName = autoCadStyleName,
            WidthFactor = preset.WidthFactor,
            ObliqueAngleDegrees = preset.ObliqueAngleDegrees,
        };
    }

    public static TimberAnnotationTextStylePresetDefinition ToDefinition(
        TimberAnnotationUserTextStylePreset preset)
    {
        var normalized = ValidateAndNormalizeUserPreset(preset);
        return new TimberAnnotationTextStylePresetDefinition(
            normalized.StableId,
            TimberAnnotationTextStylePresetKind.User,
            BuiltInPreset: null,
            LocalizationKey: null,
            normalized.DisplayName,
            normalized.AutoCadTextStyleName,
            normalized.FontFile,
            normalized.WidthFactor,
            normalized.ObliqueAngleDegrees);
    }

    public static TimberAnnotationTextStylePresetLibrary NormalizeLibrary(
        TimberAnnotationTextStylePresetLibrary? library)
    {
        if (library is null)
        {
            return TimberAnnotationTextStylePresetLibrary.CreateDefault();
        }

        var normalizedPresets = new List<TimberAnnotationUserTextStylePreset>();
        foreach (var candidate in library.Presets ?? Enumerable.Empty<TimberAnnotationUserTextStylePreset>())
        {
            if (candidate is null || !TryNormalizeStoredUserPreset(candidate, out var normalized))
            {
                continue;
            }

            var duplicateIndex = normalizedPresets.FindIndex(existing =>
                string.Equals(
                    existing.StableId,
                    normalized.StableId,
                    StringComparison.OrdinalIgnoreCase));
            if (duplicateIndex >= 0)
            {
                normalizedPresets[duplicateIndex] = normalized;
            }
            else
            {
                normalizedPresets.Add(normalized);
            }
        }

        var uniqueNames = new List<TimberAnnotationUserTextStylePreset>();
        foreach (var preset in normalizedPresets
            .OrderBy(preset => preset.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(preset => preset.StableId, StringComparer.OrdinalIgnoreCase))
        {
            uniqueNames.Add(new TimberAnnotationUserTextStylePreset
            {
                StableId = preset.StableId,
                DisplayName = EnsureUniqueDisplayName(
                    preset.DisplayName,
                    uniqueNames,
                    excludeStableId: preset.StableId),
                FontFile = preset.FontFile,
                AutoCadTextStyleName = BuildAutoCadTextStyleName(preset.StableId),
                WidthFactor = preset.WidthFactor,
                ObliqueAngleDegrees = preset.ObliqueAngleDegrees,
            });
        }

        return new TimberAnnotationTextStylePresetLibrary
        {
            Version = library.Version <= 0
                ? TimberAnnotationTextStylePresetLibrary.CurrentVersion
                : library.Version,
            Presets = uniqueNames,
        };
    }

    private static bool TryNormalizeStoredUserPreset(
        TimberAnnotationUserTextStylePreset preset,
        out TimberAnnotationUserTextStylePreset normalized)
    {
        normalized = null!;
        if (!IsValidStableId(preset.StableId) ||
            !IsValidDisplayName(preset.DisplayName) ||
            !IsValidFontFile(preset.FontFile) ||
            !IsValidWidthFactor(preset.WidthFactor) ||
            !IsValidObliqueAngle(preset.ObliqueAngleDegrees))
        {
            return false;
        }

        var stableId = preset.StableId.Trim();
        normalized = new TimberAnnotationUserTextStylePreset
        {
            StableId = stableId,
            DisplayName = preset.DisplayName.Trim(),
            FontFile = preset.FontFile.Trim(),
            AutoCadTextStyleName = BuildAutoCadTextStyleName(stableId),
            WidthFactor = preset.WidthFactor,
            ObliqueAngleDegrees = preset.ObliqueAngleDegrees,
        };
        return true;
    }

    private static bool IsWithinInclusiveRange(
        double value,
        double minimum,
        double maximum) =>
        !double.IsNaN(value) &&
        !double.IsInfinity(value) &&
        value >= minimum &&
        value <= maximum;
}
