using System.Globalization;
using System.Text;
using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Deterministic immutable BTR variant identity for G5 BlockContent (R2).
/// Angle, annotation-scale denominator, and leader Side are intentionally
/// excluded — Side affects only ModelSpace knee/landing geometry.
/// Combined variants fork by dimension-column local-X side (not screen L/R).
/// ItemOnly omits the column-side token (centered ITEM_NO only).
/// </summary>
public static class TimberFramedBlockContentVariantRules
{
    public const int MaximumRawKeyLength = 200;
    public const int MaximumSafeBlockNameLength = 64;

    /// <summary>
    /// Immutable family revision. Existing R1 (side-agnostic Combined) BTRs
    /// remain untouched; new Ensures create R2 names only.
    /// </summary>
    public const string FamilyRevisionToken = "R2";

    public const string DimensionsNegativeXToken = "DIMNX";
    public const string DimensionsPositiveXToken = "DIMPX";

    public static string CreateRawKey(
        TimberFramedBlockContentKind contentKind,
        string frameSizeToken,
        string itemTextStyleName,
        string dimensionTextStyleName,
        double itemPaperHeightMm,
        double dimensionPaperHeightMm,
        TimberFramedBlockContentPresentation presentation,
        TimberFramedBlockContentDimensionColumnSide? dimensionColumnSide = null)
    {
        // Key must never encode angle, denominator, screen Left/Right,
        // annotation ownership identity, or free-form text content.
        ValidateStyleIdentity(itemTextStyleName, nameof(itemTextStyleName));
        ValidateStyleIdentity(dimensionTextStyleName, nameof(dimensionTextStyleName));
        ValidatePaperHeight(itemPaperHeightMm, TimberAnnotationTextRole.ItemCode);
        ValidatePaperHeight(dimensionPaperHeightMm, TimberAnnotationTextRole.Dimension);

        var size = string.IsNullOrWhiteSpace(frameSizeToken)
            ? "NONE"
            : frameSizeToken.Trim().ToUpperInvariant();
        if (contentKind == TimberFramedBlockContentKind.Plain)
        {
            size = "NONE";
        }

        var presentationToken =
            presentation == TimberFramedBlockContentPresentation.ItemOnly
                ? "ITEM"
                : "COMB";
        var kindToken = contentKind switch
        {
            TimberFramedBlockContentKind.Plain => "PLAIN",
            TimberFramedBlockContentKind.Circle => "CIR",
            TimberFramedBlockContentKind.Rectangle => "REC",
            TimberFramedBlockContentKind.Slot => "SLT",
            _ => throw new ArgumentOutOfRangeException(nameof(contentKind), contentKind, null),
        };

        var parts = new List<string>
        {
            "AK_KROVY_FBC",
            FamilyRevisionToken,
            kindToken,
            size,
            presentationToken,
        };

        if (presentation == TimberFramedBlockContentPresentation.Combined)
        {
            if (dimensionColumnSide is null)
            {
                throw new ArgumentNullException(
                    nameof(dimensionColumnSide),
                    "Combined R2 variants require a dimension column side.");
            }

            if (!Enum.IsDefined(
                    typeof(TimberFramedBlockContentDimensionColumnSide),
                    dimensionColumnSide.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(dimensionColumnSide));
            }

            parts.Add(ToDimensionColumnSideToken(dimensionColumnSide.Value));
        }

        parts.Add("I" + FormatHeight(itemPaperHeightMm));
        parts.Add("D" + FormatHeight(dimensionPaperHeightMm));
        parts.Add("IS" + SanitizeToken(itemTextStyleName));
        parts.Add("DS" + SanitizeToken(dimensionTextStyleName));
        return string.Join("_", parts);
    }

    public static string ToDimensionColumnSideToken(
        TimberFramedBlockContentDimensionColumnSide side) =>
        side switch
        {
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX =>
                DimensionsNegativeXToken,
            TimberFramedBlockContentDimensionColumnSide.PositiveLocalX =>
                DimensionsPositiveXToken,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
        };

    public static TimberFramedBlockContentDimensionColumnSide OppositeDimensionColumnSide(
        TimberFramedBlockContentDimensionColumnSide side) =>
        side switch
        {
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX =>
                TimberFramedBlockContentDimensionColumnSide.PositiveLocalX,
            TimberFramedBlockContentDimensionColumnSide.PositiveLocalX =>
                TimberFramedBlockContentDimensionColumnSide.NegativeLocalX,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
        };

    /// <summary>
    /// Filesystem / AutoCAD symbol-name safe form. Truncates with a stable hash
    /// suffix when the raw key exceeds <paramref name="maximumLength"/>.
    /// </summary>
    public static string CreateSafeBlockName(
        string rawKey,
        int maximumLength = MaximumSafeBlockNameLength)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            throw new ArgumentException("Variant key must be non-empty.", nameof(rawKey));
        }
        if (maximumLength < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        var sanitized = SanitizeToken(rawKey);
        if (sanitized.Length <= maximumLength)
        {
            return sanitized;
        }

        var hash = ComputeStableHash(sanitized);
        var prefixLength = maximumLength - 1 - hash.Length;
        if (prefixLength < 1)
        {
            return hash.Substring(0, maximumLength);
        }

        return sanitized.Substring(0, prefixLength) + "_" + hash;
    }

    /// <summary>
    /// Fail-closed classifier for P3 R2 BlockContent variant names (raw keys or
    /// non-truncated safe names). Truncated hash-suffixed names are rejected.
    /// Does not use CAD host types.
    /// </summary>
    public static bool TryParseR2VariantKey(
        string? blockNameOrRawKey,
        out TimberFramedBlockContentR2VariantParse parse)
    {
        parse = default;
        if (string.IsNullOrWhiteSpace(blockNameOrRawKey))
        {
            return false;
        }

        var sanitized = SanitizeToken(blockNameOrRawKey!);
        const string prefix = "AK_KROVY_FBC_R2_";
        if (!sanitized.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var isCombined = ContainsBoundedToken(sanitized, "COMB");
        var isItemOnly = ContainsBoundedToken(sanitized, "ITEM");
        if (isCombined == isItemOnly)
        {
            // Exactly one presentation token is required.
            return false;
        }

        TimberFramedBlockContentDimensionColumnSide? side = null;
        if (isCombined)
        {
            var hasNegative = ContainsBoundedToken(sanitized, DimensionsNegativeXToken);
            var hasPositive = ContainsBoundedToken(sanitized, DimensionsPositiveXToken);
            if (hasNegative == hasPositive)
            {
                return false;
            }

            side = hasNegative
                ? TimberFramedBlockContentDimensionColumnSide.NegativeLocalX
                : TimberFramedBlockContentDimensionColumnSide.PositiveLocalX;
        }

        parse = new TimberFramedBlockContentR2VariantParse(
            isCombined,
            isItemOnly,
            side);
        return true;
    }

    public static bool IsP3R2CombinedStretchNormalizeTarget(
        string? blockNameOrRawKey) =>
        TryParseR2VariantKey(blockNameOrRawKey, out var parse) &&
        parse.IsP3R2CombinedTarget;

    private static bool ContainsBoundedToken(string sanitized, string token)
    {
        if (string.IsNullOrEmpty(sanitized) || string.IsNullOrEmpty(token))
        {
            return false;
        }

        var needle = "_" + token + "_";
        if (sanitized.IndexOf(needle, StringComparison.Ordinal) >= 0)
        {
            return true;
        }

        return sanitized.EndsWith("_" + token, StringComparison.Ordinal) ||
               sanitized.StartsWith(token + "_", StringComparison.Ordinal) ||
               string.Equals(sanitized, token, StringComparison.Ordinal);
    }

    private static void ValidateStyleIdentity(string value, string parameterName)
    {
        if (!TimberAnnotationTextSettingsRules.IsValidTextStyleName(value))
        {
            throw new ArgumentException(
                "Text style identity must be a non-empty, non-control name.",
                parameterName);
        }
    }

    private static void ValidatePaperHeight(
        double paperHeightMm,
        TimberAnnotationTextRole role)
    {
        if (!TimberAnnotationTextSettingsRules.IsValidPaperHeightMm(role, paperHeightMm))
        {
            throw new ArgumentOutOfRangeException(
                nameof(paperHeightMm),
                paperHeightMm,
                $"Paper height for {role} is outside the supported range.");
        }
    }

    private static string FormatHeight(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string SanitizeToken(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_');
        }

        var text = builder.ToString();
        while (text.IndexOf("__", StringComparison.Ordinal) >= 0)
        {
            text = text.Replace("__", "_");
        }

        return text.Trim('_');
    }

    private static string ComputeStableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in value)
            {
                hash = (hash ^ ch) * 16777619u;
            }

            return hash.ToString("X8", CultureInfo.InvariantCulture);
        }
    }
}
