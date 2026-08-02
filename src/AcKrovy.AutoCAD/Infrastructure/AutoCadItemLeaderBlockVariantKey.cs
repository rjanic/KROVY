using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum AutoCadItemLeaderBlockFrameKind
{
    Circle,
    Slot,
    Rectangle,
}

/// <summary>
/// Immutable semantic identity of one framed ITEM_NO block definition.
/// It deliberately contains no database objects or per-entity denominator.
/// </summary>
internal sealed record AutoCadItemLeaderBlockVariantKey
{
    public const int CurrentGeometryVersion = 1;

    public int GeometryVersion { get; }
    public AutoCadItemLeaderBlockFrameKind FrameKind { get; }
    public TimberItemLeaderBlockSize FrameSize { get; }
    public string ResolvedCanonicalTextStyleName { get; }
    public double ItemNumberPaperHeightMm { get; }
    public int BaseDenominator { get; }

    public string CanonicalPaperHeight =>
        ItemNumberPaperHeightMm.ToString("R", CultureInfo.InvariantCulture);

    private AutoCadItemLeaderBlockVariantKey(
        int geometryVersion,
        AutoCadItemLeaderBlockFrameKind frameKind,
        TimberItemLeaderBlockSize frameSize,
        string resolvedCanonicalTextStyleName,
        double itemNumberPaperHeightMm,
        int baseDenominator)
    {
        if (geometryVersion != CurrentGeometryVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(geometryVersion));
        }
        if (!Enum.IsDefined(frameKind))
        {
            throw new ArgumentOutOfRangeException(nameof(frameKind));
        }
        if (!Enum.IsDefined(frameSize) ||
            frameKind == AutoCadItemLeaderBlockFrameKind.Circle &&
            frameSize != TimberItemLeaderBlockSize.Small)
        {
            throw new ArgumentOutOfRangeException(nameof(frameSize));
        }
        if (!TimberAnnotationTextSettingsRules.IsValidTextStyleName(
                resolvedCanonicalTextStyleName))
        {
            throw new ArgumentException(
                "A resolved canonical text-style name is required.",
                nameof(resolvedCanonicalTextStyleName));
        }
        if (!TimberAnnotationTextSettingsRules
                .IsValidItemNumberPaperHeightMm(itemNumberPaperHeightMm))
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemNumberPaperHeightMm));
        }
        if (baseDenominator != TimberAnnotationScaleRules.DefaultDenominator)
        {
            throw new ArgumentOutOfRangeException(nameof(baseDenominator));
        }

        GeometryVersion = geometryVersion;
        FrameKind = frameKind;
        FrameSize = frameSize;
        ResolvedCanonicalTextStyleName =
            resolvedCanonicalTextStyleName.Trim();
        ItemNumberPaperHeightMm = itemNumberPaperHeightMm;
        BaseDenominator = baseDenominator;
    }

    public static AutoCadItemLeaderBlockVariantKey Create(
        AutoCadItemLeaderBlockFrameKind frameKind,
        TimberItemLeaderBlockSize frameSize,
        string resolvedCanonicalTextStyleName,
        double itemNumberPaperHeightMm,
        int geometryVersion = CurrentGeometryVersion,
        int baseDenominator = TimberAnnotationScaleRules.DefaultDenominator) =>
        new(
            geometryVersion,
            frameKind,
            frameSize,
            resolvedCanonicalTextStyleName,
            itemNumberPaperHeightMm,
            baseDenominator);

    public static AutoCadItemLeaderBlockVariantKey FromDefinition(
        TimberItemLeaderBlockDefinition definition,
        string resolvedCanonicalTextStyleName,
        double itemNumberPaperHeightMm)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Create(
            ToFrameKind(definition.Style),
            definition.Size,
            resolvedCanonicalTextStyleName,
            itemNumberPaperHeightMm);
    }

    public static AutoCadItemLeaderBlockFrameKind ToFrameKind(
        ItemNumberLeaderStyle style) =>
        ItemNumberLeaderStyleRules.Normalize(style) switch
        {
            ItemNumberLeaderStyle.Circle =>
                AutoCadItemLeaderBlockFrameKind.Circle,
            ItemNumberLeaderStyle.Slot =>
                AutoCadItemLeaderBlockFrameKind.Slot,
            ItemNumberLeaderStyle.Rectangle =>
                AutoCadItemLeaderBlockFrameKind.Rectangle,
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };

    public ItemNumberLeaderStyle ToItemNumberLeaderStyle() => FrameKind switch
    {
        AutoCadItemLeaderBlockFrameKind.Circle => ItemNumberLeaderStyle.Circle,
        AutoCadItemLeaderBlockFrameKind.Slot => ItemNumberLeaderStyle.Slot,
        AutoCadItemLeaderBlockFrameKind.Rectangle =>
            ItemNumberLeaderStyle.Rectangle,
        _ => throw new InvalidOperationException("Unsupported frame kind."),
    };
}

internal static partial class AutoCadItemLeaderBlockVariantNamePolicy
{
    public const int FingerprintHexLength = 12;
    public const int MaximumSafeSymbolNameLength = 64;
    private const int MaximumCollisionAttempts = 64;

    public static string CreateCanonicalName(
        AutoCadItemLeaderBlockVariantKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var frame = FrameToken(key.FrameKind);
        var size = key.FrameKind == AutoCadItemLeaderBlockFrameKind.Circle
            ? string.Empty
            : $"_{SizeToken(key.FrameSize)}";
        var height = TryCreateReadableHeight(key.ItemNumberPaperHeightMm);
        var fingerprint = CreateFingerprint(CreateFingerprintPayload(key));
        return ValidateGeneratedName(
            $"AK_ITEM_{frame}{size}_G{key.GeometryVersion}_{height}_S{fingerprint}");
    }

    public static string CreateCollisionName(
        AutoCadItemLeaderBlockVariantKey key,
        int collisionAttempt)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (collisionAttempt < 1 || collisionAttempt > MaximumCollisionAttempts)
        {
            throw new ArgumentOutOfRangeException(nameof(collisionAttempt));
        }

        var canonicalName = CreateCanonicalName(key);
        var payload = string.Concat(
            "collision|",
            CreateFingerprintPayload(key),
            "|attempt=",
            collisionAttempt.ToString(CultureInfo.InvariantCulture));
        return ValidateGeneratedName(
            $"{canonicalName}_C{CreateFingerprint(payload)}");
    }

    public static string CreateFingerprintPayload(
        AutoCadItemLeaderBlockVariantKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var style = key.ResolvedCanonicalTextStyleName;
        return string.Concat(
            "schema=1|geometry=",
            key.GeometryVersion.ToString(CultureInfo.InvariantCulture),
            "|frame=",
            FrameToken(key.FrameKind),
            "|size=",
            SizeToken(key.FrameSize),
            "|styleLength=",
            style.Length.ToString(CultureInfo.InvariantCulture),
            "|style=",
            style,
            "|paperHeightMm=",
            key.CanonicalPaperHeight,
            "|baseDenominator=",
            key.BaseDenominator.ToString(CultureInfo.InvariantCulture));
    }

    public static bool IsSafeSymbolName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= MaximumSafeSymbolNameLength &&
        SafeSymbolNameRegex().IsMatch(name);

    private static string CreateFingerprint(string payload)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(digest)[..FingerprintHexLength];
    }

    private static string TryCreateReadableHeight(double paperHeightMm)
    {
        var micrometres = paperHeightMm * 1000d;
        return double.IsFinite(micrometres) &&
            micrometres == Math.Truncate(micrometres) &&
            micrometres >= 0d &&
            micrometres <= long.MaxValue
                ? $"H{((long)micrometres).ToString(CultureInfo.InvariantCulture)}"
                : "HX";
    }

    private static string FrameToken(AutoCadItemLeaderBlockFrameKind frameKind) =>
        frameKind switch
        {
            AutoCadItemLeaderBlockFrameKind.Circle => "CIR",
            AutoCadItemLeaderBlockFrameKind.Slot => "SLOT",
            AutoCadItemLeaderBlockFrameKind.Rectangle => "RECT",
            _ => throw new ArgumentOutOfRangeException(nameof(frameKind)),
        };

    private static string SizeToken(TimberItemLeaderBlockSize frameSize) =>
        frameSize switch
        {
            TimberItemLeaderBlockSize.Small => "S",
            TimberItemLeaderBlockSize.Medium => "M",
            TimberItemLeaderBlockSize.Large => "L",
            _ => throw new ArgumentOutOfRangeException(nameof(frameSize)),
        };

    private static string ValidateGeneratedName(string name) =>
        IsSafeSymbolName(name)
            ? name
            : throw new InvalidOperationException(
                "Generated block name is not a safe AutoCAD symbol name.");

    [GeneratedRegex("^[A-Z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeSymbolNameRegex();
}
