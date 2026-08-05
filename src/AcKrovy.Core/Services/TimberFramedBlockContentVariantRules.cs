using System.Globalization;
using System.Text;
using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Deterministic immutable BTR variant identity for G5 BlockContent.
/// Angle, annotation-scale denominator, and leader Side are intentionally
/// excluded — Side affects only ModelSpace knee/landing geometry.
/// </summary>
public static class TimberFramedBlockContentVariantRules
{
    public const int MaximumRawKeyLength = 200;
    public const int MaximumSafeBlockNameLength = 64;

    public static string CreateRawKey(
        TimberFramedBlockContentKind contentKind,
        string frameSizeToken,
        string itemTextStyleName,
        string dimensionTextStyleName,
        double itemPaperHeightMm,
        double dimensionPaperHeightMm,
        TimberFramedBlockContentPresentation presentation)
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
        var kindToken = contentKind switch
        {
            TimberFramedBlockContentKind.Plain => "PLAIN",
            TimberFramedBlockContentKind.Circle => "CIR",
            TimberFramedBlockContentKind.Rectangle => "REC",
            TimberFramedBlockContentKind.Slot => "SLT",
            _ => throw new ArgumentOutOfRangeException(nameof(contentKind), contentKind, null),
        };

        return string.Join(
            "_",
            "AK_KROVY_FBC",
            kindToken,
            size,
            presentationToken,
            "I" + FormatHeight(itemPaperHeightMm),
            "D" + FormatHeight(dimensionPaperHeightMm),
            "IS" + SanitizeToken(itemTextStyleName),
            "DS" + SanitizeToken(dimensionTextStyleName));
    }

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
