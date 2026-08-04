using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Immutable identity of a G4 frame-only shared block definition.
/// Identity is frame kind, size variant and geometry version only — no text
/// style, paper height, element identity or annotation denominator.
/// </summary>
internal sealed record AutoCadItemLeaderFrameOnlyBlockKey
{
    public const int GeometryVersion = 4;

    public int Version { get; }
    public AutoCadItemLeaderBlockFrameKind FrameKind { get; }
    public TimberItemLeaderBlockSize FrameSize { get; }

    private AutoCadItemLeaderFrameOnlyBlockKey(
        int version,
        AutoCadItemLeaderBlockFrameKind frameKind,
        TimberItemLeaderBlockSize frameSize)
    {
        if (version != GeometryVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
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

        Version = version;
        FrameKind = frameKind;
        FrameSize = frameSize;
    }

    public static AutoCadItemLeaderFrameOnlyBlockKey Create(
        AutoCadItemLeaderBlockFrameKind frameKind,
        TimberItemLeaderBlockSize frameSize,
        int version = GeometryVersion) =>
        new(version, frameKind, frameSize);

    public static AutoCadItemLeaderFrameOnlyBlockKey FromDefinition(
        TimberItemLeaderBlockDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Create(
            AutoCadItemLeaderBlockVariantKey.ToFrameKind(definition.Style),
            definition.Size);
    }

    public ItemNumberLeaderStyle ToItemNumberLeaderStyle() => FrameKind switch
    {
        AutoCadItemLeaderBlockFrameKind.Circle => ItemNumberLeaderStyle.Circle,
        AutoCadItemLeaderBlockFrameKind.Slot => ItemNumberLeaderStyle.Slot,
        AutoCadItemLeaderBlockFrameKind.Rectangle =>
            ItemNumberLeaderStyle.Rectangle,
        _ => throw new InvalidOperationException("Unsupported frame kind."),
    };
}

internal static partial class AutoCadItemLeaderFrameOnlyBlockNamePolicy
{
    public const int FingerprintHexLength = 12;
    public const int MaximumSafeSymbolNameLength = 64;
    private const int MaximumCollisionAttempts = 64;

    public static string CreateCanonicalName(
        AutoCadItemLeaderFrameOnlyBlockKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var name = string.Concat(
            "AK_ITEM_FRAME_",
            FrameToken(key.FrameKind),
            "_",
            SizeToken(key.FrameSize),
            "_G",
            key.Version.ToString(CultureInfo.InvariantCulture));
        return ValidateGeneratedName(name);
    }

    public static string CreateCollisionName(
        AutoCadItemLeaderFrameOnlyBlockKey key,
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
        var suffix = CreateFingerprint(payload);
        return ValidateGeneratedName($"{canonicalName}_C{suffix}");
    }

    public static string CreateFingerprintPayload(
        AutoCadItemLeaderFrameOnlyBlockKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return string.Concat(
            "schema=4|geometry=",
            key.Version.ToString(CultureInfo.InvariantCulture),
            "|frame=",
            FrameToken(key.FrameKind),
            "|size=",
            SizeToken(key.FrameSize));
    }

    public static bool IsSafeSymbolName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= MaximumSafeSymbolNameLength &&
        SafeSymbolNameRegex().IsMatch(name);

    public static bool IsG4FrameOnlyName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.StartsWith("AK_ITEM_FRAME_", StringComparison.Ordinal) &&
            name.Contains("_G4", StringComparison.Ordinal);
    }

    private static string CreateFingerprint(string payload)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(digest)[..FingerprintHexLength];
    }

    private static string FrameToken(AutoCadItemLeaderBlockFrameKind frameKind) =>
        frameKind switch
        {
            AutoCadItemLeaderBlockFrameKind.Circle => "CIRCLE",
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
                "Generated G4 frame block name is not a safe AutoCAD symbol name.");

    [GeneratedRegex("^[A-Z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeSymbolNameRegex();
}
