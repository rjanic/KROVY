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
/// Immutable semantic identity of one shared framed ITEM_NO block definition.
/// Identity is frozen geometry only: frame kind, size variant, and geometry
/// version. It deliberately excludes TextStyleId, font family, paper height,
/// and per-element annotation denominator.
/// </summary>
internal sealed record AutoCadItemLeaderBlockVariantKey
{
    /// <summary>
    /// Shared-definition geometry contract after the v0.23 framed baseline
    /// restoration hotfix. Earlier G1 names baked style/height into identity.
    /// </summary>
    public const int CurrentGeometryVersion = 2;

    public int GeometryVersion { get; }
    public AutoCadItemLeaderBlockFrameKind FrameKind { get; }
    public TimberItemLeaderBlockSize FrameSize { get; }

    private AutoCadItemLeaderBlockVariantKey(
        int geometryVersion,
        AutoCadItemLeaderBlockFrameKind frameKind,
        TimberItemLeaderBlockSize frameSize)
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

        GeometryVersion = geometryVersion;
        FrameKind = frameKind;
        FrameSize = frameSize;
    }

    public static AutoCadItemLeaderBlockVariantKey Create(
        AutoCadItemLeaderBlockFrameKind frameKind,
        TimberItemLeaderBlockSize frameSize,
        int geometryVersion = CurrentGeometryVersion) =>
        new(geometryVersion, frameKind, frameSize);

    public static AutoCadItemLeaderBlockVariantKey FromDefinition(
        TimberItemLeaderBlockDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Create(
            ToFrameKind(definition.Style),
            definition.Size);
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
        return ValidateGeneratedName(
            $"AK_ITEM_{frame}{size}_G{key.GeometryVersion}");
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
        return string.Concat(
            "schema=2|geometry=",
            key.GeometryVersion.ToString(CultureInfo.InvariantCulture),
            "|frame=",
            FrameToken(key.FrameKind),
            "|size=",
            SizeToken(key.FrameSize));
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

    private static string FrameToken(AutoCadItemLeaderBlockFrameKind frameKind) =>
        frameKind switch
        {
            AutoCadItemLeaderBlockFrameKind.Circle => "CIR",
            AutoCadItemLeaderBlockFrameKind.Slot => "SLOT",
            AutoCadItemLeaderBlockFrameKind.Rectangle => "RECT",
            _ => throw new ArgumentOutOfRangeException(nameof(frameKind)),
        };

    public static string SizeToken(TimberItemLeaderBlockSize frameSize) =>
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
