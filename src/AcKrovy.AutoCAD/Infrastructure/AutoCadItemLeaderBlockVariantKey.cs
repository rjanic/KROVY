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

internal enum AutoCadItemLeaderTextStyleIdentityKind
{
    Classic,
    Architectural,
    User,
}

/// <summary>
/// Stable, host-neutral identity of the text style baked into a G3 definition.
/// It contains no display name, height, element identity, or host object handle.
/// </summary>
internal sealed record AutoCadItemLeaderTextStyleIdentity
{
    public AutoCadItemLeaderTextStyleIdentityKind Kind { get; }
    public string StableId { get; }

    private AutoCadItemLeaderTextStyleIdentity(
        AutoCadItemLeaderTextStyleIdentityKind kind,
        string stableId)
    {
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(stableId))
        {
            throw new ArgumentException("A valid text-style identity is required.");
        }

        Kind = kind;
        StableId = stableId.Trim();
    }

    public static AutoCadItemLeaderTextStyleIdentity Classic { get; } =
        new(AutoCadItemLeaderTextStyleIdentityKind.Classic, "classic");

    public static AutoCadItemLeaderTextStyleIdentity Architectural { get; } =
        new(AutoCadItemLeaderTextStyleIdentityKind.Architectural, "architectural");

    public static AutoCadItemLeaderTextStyleIdentity User(string stableId) =>
        new(AutoCadItemLeaderTextStyleIdentityKind.User, stableId);

    public static AutoCadItemLeaderTextStyleIdentity FromStoredStyleName(
        string? styleName)
    {
        var normalized = styleName?.Trim();
        if (string.Equals(
                normalized,
                TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
                StringComparison.OrdinalIgnoreCase))
        {
            return Architectural;
        }
        if (normalized is not null &&
            normalized.StartsWith(
                TimberAnnotationTextStylePresetRules.UserStyleNamePrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            var stableId = normalized[
                TimberAnnotationTextStylePresetRules.UserStyleNamePrefix.Length..];
            if (!string.IsNullOrWhiteSpace(stableId))
            {
                return User(stableId);
            }
        }

        return Classic;
    }

    public string CreateNameToken()
    {
        if (Kind == AutoCadItemLeaderTextStyleIdentityKind.Classic)
        {
            return "CLASSIC";
        }
        if (Kind == AutoCadItemLeaderTextStyleIdentityKind.Architectural)
        {
            return "ARCH";
        }

        var safeStableId = new string(StableId
            .Select(character => char.IsLetterOrDigit(character)
                ? char.ToUpperInvariant(character)
                : '_')
            .ToArray());
        return $"USER_{safeStableId}";
    }
}

/// <summary>
/// Immutable semantic identity of one shared framed ITEM_NO block definition.
/// Identity is frame kind, size variant, geometry version, and stable text-style
/// identity. It deliberately excludes host objects, font metrics, paper height,
/// element identity, and per-element annotation denominator.
/// </summary>
internal sealed record AutoCadItemLeaderBlockVariantKey
{
    /// <summary>
    /// Shared-definition geometry contract after the v0.23 framed baseline
    /// restoration hotfix. Earlier G1 names baked style/height into identity.
    /// </summary>
    public const int CurrentGeometryVersion = 3;

    public int GeometryVersion { get; }
    public AutoCadItemLeaderBlockFrameKind FrameKind { get; }
    public TimberItemLeaderBlockSize FrameSize { get; }
    public AutoCadItemLeaderTextStyleIdentity TextStyleIdentity { get; }

    private AutoCadItemLeaderBlockVariantKey(
        int geometryVersion,
        AutoCadItemLeaderBlockFrameKind frameKind,
        TimberItemLeaderBlockSize frameSize,
        AutoCadItemLeaderTextStyleIdentity textStyleIdentity)
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
        TextStyleIdentity = textStyleIdentity ??
            throw new ArgumentNullException(nameof(textStyleIdentity));
    }

    public static AutoCadItemLeaderBlockVariantKey Create(
        AutoCadItemLeaderBlockFrameKind frameKind,
        TimberItemLeaderBlockSize frameSize,
        AutoCadItemLeaderTextStyleIdentity? textStyleIdentity = null,
        int geometryVersion = CurrentGeometryVersion) =>
        new(
            geometryVersion,
            frameKind,
            frameSize,
            textStyleIdentity ?? AutoCadItemLeaderTextStyleIdentity.Classic);

    public static AutoCadItemLeaderBlockVariantKey FromDefinition(
        TimberItemLeaderBlockDefinition definition,
        AutoCadItemLeaderTextStyleIdentity? textStyleIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Create(
            ToFrameKind(definition.Style),
            definition.Size,
            textStyleIdentity);
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
        var prefix = $"AK_ITEM_{frame}{size}_G{key.GeometryVersion}_";
        var styleToken = key.TextStyleIdentity.CreateNameToken();
        var candidate = $"{prefix}{styleToken}";
        if (candidate.Length > MaximumSafeSymbolNameLength)
        {
            styleToken = $"USER_{CreateFingerprint(
                key.TextStyleIdentity.StableId)}";
        }
        return ValidateGeneratedName($"{prefix}{styleToken}");
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
        var suffix = CreateFingerprint(payload);
        var candidate = $"{canonicalName}_C{suffix}";
        if (candidate.Length > MaximumSafeSymbolNameLength)
        {
            candidate = string.Concat(
                "AK_ITEM_G",
                key.GeometryVersion.ToString(CultureInfo.InvariantCulture),
                "_",
                FrameToken(key.FrameKind),
                "_",
                SizeToken(key.FrameSize),
                "_S",
                CreateFingerprint(key.TextStyleIdentity.StableId),
                "_C",
                suffix);
        }
        return ValidateGeneratedName(candidate);
    }

    public static string CreateFingerprintPayload(
        AutoCadItemLeaderBlockVariantKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return string.Concat(
            "schema=3|geometry=",
            key.GeometryVersion.ToString(CultureInfo.InvariantCulture),
            "|frame=",
            FrameToken(key.FrameKind),
            "|size=",
            SizeToken(key.FrameSize),
            "|textStyle=",
            key.TextStyleIdentity.Kind.ToString(),
            ":",
            key.TextStyleIdentity.StableId);
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
