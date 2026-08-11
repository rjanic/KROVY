using System.Globalization;
using System.Text;
using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Deterministic immutable BTR variant identity for G5 BlockContent (R3).
/// Angle and annotation-scale denominator are intentionally excluded.
/// Combined R3 encodes content-side RIGHT/LEFT (WIDTH/HEIGHT layout vs frame).
/// Legacy R2 Combined DIMNX/DIMPX remains parseable for DEBUG/migration.
/// Legacy side-agnostic R3 Combined (no RIGHT/LEFT) remains parseable but is
/// not a production create target.
/// </summary>
public static class TimberFramedBlockContentVariantRules
{
    public const int MaximumRawKeyLength = 200;
    public const int MaximumSafeBlockNameLength = 64;

    /// <summary>
    /// Immutable family revision. R1 = pre-revision side-agnostic Combined.
    /// R2 = DIMNX/DIMPX Combined fork (legacy / DEBUG stretch-normalize).
    /// R3 = production Combined with RIGHT/LEFT content variants.
    /// </summary>
    public const string FamilyRevisionToken = "R3";

    public const string LegacyR2FamilyRevisionToken = "R2";

    public const string DimensionsNegativeXToken = "DIMNX";
    public const string DimensionsPositiveXToken = "DIMPX";

    public const string ContentVariantRightToken =
        TimberFramedCombinedG5ContentVariantRules.RightToken;

    public const string ContentVariantLeftToken =
        TimberFramedCombinedG5ContentVariantRules.LeftToken;

    public const string CircleKindToken = "CIR";
    public const string RectangleKindToken = "REC";
    public const string SlotKindToken = "SLT";
    public const string PlainKindToken = "PLAIN";

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
        // Key must never encode angle, denominator, screen leader Side enum,
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
        var kindToken = ToContentKindToken(contentKind);

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
            var side = dimensionColumnSide ??
                TimberFramedBlockContentDefinitionRules
                    .DefaultCombinedDimensionColumnSide;
            if (!Enum.IsDefined(
                    typeof(TimberFramedBlockContentDimensionColumnSide),
                    side))
            {
                throw new ArgumentOutOfRangeException(nameof(dimensionColumnSide));
            }

            parts.Add(
                TimberFramedCombinedG5ContentVariantRules.ToContentVariantToken(side));
        }

        parts.Add("I" + FormatHeight(itemPaperHeightMm));
        parts.Add("D" + FormatHeight(dimensionPaperHeightMm));
        parts.Add("IS" + SanitizeToken(itemTextStyleName));
        parts.Add("DS" + SanitizeToken(dimensionTextStyleName));
        return string.Join("_", parts);
    }

    /// <summary>
    /// Legacy R2 Combined key with DIMNX/DIMPX — DEBUG stretch-normalize /
    /// migration fixtures only. Production Ensures use <see cref="CreateRawKey"/>.
    /// </summary>
    public static string CreateLegacyR2RawKey(
        TimberFramedBlockContentKind contentKind,
        string frameSizeToken,
        string itemTextStyleName,
        string dimensionTextStyleName,
        double itemPaperHeightMm,
        double dimensionPaperHeightMm,
        TimberFramedBlockContentPresentation presentation,
        TimberFramedBlockContentDimensionColumnSide? dimensionColumnSide = null)
    {
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
        var kindToken = ToContentKindToken(contentKind);

        var parts = new List<string>
        {
            "AK_KROVY_FBC",
            LegacyR2FamilyRevisionToken,
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
                    "Legacy R2 Combined variants require a dimension column side.");
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
    /// Fail-closed classifier for legacy P3 R2 BlockContent variant names.
    /// Truncated hash-suffixed names are rejected. Used by DEBUG stretch
    /// normalize — production R3 Combined is never an R2 target.
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

    /// <summary>
    /// Production R3 Combined / ItemOnly classifier. Combined prefers RIGHT/LEFT
    /// content-variant tokens; legacy side-agnostic Combined still parses.
    /// </summary>
    public static bool TryParseR3VariantKey(
        string? blockNameOrRawKey,
        out bool isCombined,
        out bool isItemOnly) =>
        TryParseR3VariantKey(blockNameOrRawKey, out isCombined, out isItemOnly, out _);

    public static bool TryParseR3VariantKey(
        string? blockNameOrRawKey,
        out bool isCombined,
        out bool isItemOnly,
        out TimberFramedBlockContentDimensionColumnSide? contentVariantSide) =>
        TryParseR3VariantKey(
            blockNameOrRawKey,
            out isCombined,
            out isItemOnly,
            out contentVariantSide,
            out _);

    public static bool TryParseR3VariantKey(
        string? blockNameOrRawKey,
        out bool isCombined,
        out bool isItemOnly,
        out TimberFramedBlockContentDimensionColumnSide? contentVariantSide,
        out TimberFramedBlockContentKind? contentKind)
    {
        isCombined = false;
        isItemOnly = false;
        contentVariantSide = null;
        contentKind = null;
        if (string.IsNullOrWhiteSpace(blockNameOrRawKey))
        {
            return false;
        }

        var sanitized = SanitizeToken(blockNameOrRawKey!);
        const string prefix = "AK_KROVY_FBC_R3_";
        if (!sanitized.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        isCombined = ContainsBoundedToken(sanitized, "COMB");
        isItemOnly = ContainsBoundedToken(sanitized, "ITEM");
        if (isCombined == isItemOnly)
        {
            isCombined = false;
            isItemOnly = false;
            return false;
        }

        // R3 Combined must not carry legacy DIMNX/DIMPX tokens.
        if (isCombined &&
            (ContainsBoundedToken(sanitized, DimensionsNegativeXToken) ||
             ContainsBoundedToken(sanitized, DimensionsPositiveXToken)))
        {
            isCombined = false;
            isItemOnly = false;
            return false;
        }

        if (isCombined)
        {
            var hasRight = ContainsBoundedToken(sanitized, ContentVariantRightToken);
            var hasLeft = ContainsBoundedToken(sanitized, ContentVariantLeftToken);
            if (hasRight && hasLeft)
            {
                isCombined = false;
                isItemOnly = false;
                return false;
            }

            if (hasRight)
            {
                contentVariantSide =
                    TimberFramedCombinedG5ContentVariantRules.RightColumnSide;
            }
            else if (hasLeft)
            {
                contentVariantSide =
                    TimberFramedCombinedG5ContentVariantRules.LeftColumnSide;
            }
        }

        contentKind = TryParseContentKindToken(sanitized);
        return true;
    }

    public static bool TryParseR3VariantKey(
        string? blockNameOrRawKey,
        out TimberFramedBlockContentR3VariantParse parse)
    {
        parse = default;
        if (!TryParseR3VariantKey(
                blockNameOrRawKey,
                out var isCombined,
                out var isItemOnly,
                out var side,
                out var contentKind))
        {
            return false;
        }

        parse = new TimberFramedBlockContentR3VariantParse(
            isCombined,
            isItemOnly,
            side,
            contentKind);
        return true;
    }

    public static string ToContentKindToken(TimberFramedBlockContentKind contentKind) =>
        contentKind switch
        {
            TimberFramedBlockContentKind.Plain => PlainKindToken,
            TimberFramedBlockContentKind.Circle => CircleKindToken,
            TimberFramedBlockContentKind.Rectangle => RectangleKindToken,
            TimberFramedBlockContentKind.Slot => SlotKindToken,
            _ => throw new ArgumentOutOfRangeException(nameof(contentKind), contentKind, null),
        };

    /// <summary>
    /// Fail-closed: exactly one CIR/REC/SLT/PLAIN token must be present.
    /// </summary>
    public static TimberFramedBlockContentKind? TryParseContentKindToken(
        string? blockNameOrRawKey)
    {
        if (string.IsNullOrWhiteSpace(blockNameOrRawKey))
        {
            return null;
        }

        var sanitized = SanitizeToken(blockNameOrRawKey!);
        TimberFramedBlockContentKind? kind = null;
        var matches = 0;
        if (ContainsBoundedToken(sanitized, CircleKindToken))
        {
            kind = TimberFramedBlockContentKind.Circle;
            matches++;
        }

        if (ContainsBoundedToken(sanitized, RectangleKindToken))
        {
            kind = TimberFramedBlockContentKind.Rectangle;
            matches++;
        }

        if (ContainsBoundedToken(sanitized, SlotKindToken))
        {
            kind = TimberFramedBlockContentKind.Slot;
            matches++;
        }

        if (ContainsBoundedToken(sanitized, PlainKindToken))
        {
            kind = TimberFramedBlockContentKind.Plain;
            matches++;
        }

        return matches == 1 ? kind : null;
    }

    public static bool IsP3R2CombinedStretchNormalizeTarget(
        string? blockNameOrRawKey) =>
        TryParseR2VariantKey(blockNameOrRawKey, out var parse) &&
        parse.IsP3R2CombinedTarget;

    /// <summary>
    /// Production R3 Combined (RIGHT/LEFT or legacy side-agnostic).
    /// </summary>
    public static bool IsProductionR3Combined(string? blockNameOrRawKey) =>
        TryParseR3VariantKey(blockNameOrRawKey, out var isCombined, out _) &&
        isCombined;

    /// <summary>
    /// Production R3 Combined with an explicit RIGHT/LEFT content variant.
    /// </summary>
    public static bool IsProductionR3CombinedContentVariant(
        string? blockNameOrRawKey) =>
        TryParseR3VariantKey(blockNameOrRawKey, out var parse) &&
        parse.IsProductionCombinedTarget;

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
