using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Deterministic encoding of roof-owned generated-member overrides.</summary>
public static class RoofGeneratedMemberOverrideCodec
{
    private const char ItemSeparator = ';';
    private const char FieldSeparator = ':';
    private const string EmptyElementIdToken = "-";

    public static string Encode(IReadOnlyList<RoofGeneratedMemberOverride> overrides)
    {
        if (overrides is null)
        {
            throw new ArgumentNullException(nameof(overrides));
        }
        var normalized = RoofGeneratedMemberOverrideRules.NormalizeOverrides(overrides);
        if (RoofGeneratedMemberOverrideRules.HasDuplicateKeys(normalized))
        {
            throw new ArgumentException("Duplicate generated-member override keys.", nameof(overrides));
        }

        return string.Join(ItemSeparator.ToString(), normalized.Select(EncodeOne));
    }

    public static bool TryDecode(
        string? payload,
        out IReadOnlyList<RoofGeneratedMemberOverride> overrides,
        out RoofDefinitionDataDecodeError error)
    {
        overrides = Array.Empty<RoofGeneratedMemberOverride>();
        error = RoofDefinitionDataDecodeError.InvalidManualOverride;
        if (payload is null || payload.Length == 0)
        {
            error = RoofDefinitionDataDecodeError.None;
            return true;
        }

        var items = new List<RoofGeneratedMemberOverride>();
        var seen = new HashSet<RoofGeneratedMemberKey>();
        foreach (var token in payload.Split(ItemSeparator))
        {
            if (!TryDecodeOne(token, out var item))
            {
                return false;
            }

            if (item is null)
            {
                continue;
            }

            if (!seen.Add(item.Key))
            {
                return false;
            }

            items.Add(item);
        }

        overrides = RoofGeneratedMemberOverrideRules.NormalizeOverrides(items);
        error = RoofDefinitionDataDecodeError.None;
        return true;
    }

    private static string EncodeOne(RoofGeneratedMemberOverride item)
    {
        var elementId = string.IsNullOrWhiteSpace(item.ReservedElementId)
            ? EmptyElementIdToken
            : item.ReservedElementId;
        return string.Join(
            FieldSeparator.ToString(),
            item.Key.MemberKind.ToString(),
            item.Key.RoofFace.ToString(),
            item.Key.StationIndex.ToString(CultureInfo.InvariantCulture),
            item.Suppressed ? "1" : "0",
            item.AlongMm.ToString("R", CultureInfo.InvariantCulture),
            item.LateralMm.ToString("R", CultureInfo.InvariantCulture),
            item.RotationRadians.ToString("R", CultureInfo.InvariantCulture),
            item.StartOffsetMm.ToString("R", CultureInfo.InvariantCulture),
            item.EndOffsetMm.ToString("R", CultureInfo.InvariantCulture),
            elementId);
    }

    private static bool TryDecodeOne(string token, out RoofGeneratedMemberOverride? item)
    {
        item = null;
        var fields = token.Split(FieldSeparator);
        if (fields.Length != 10)
        {
            return false;
        }

        if (!Enum.TryParse(fields[0], false, out RoofGeneratedTimberKind kind) ||
            !Enum.IsDefined(typeof(RoofGeneratedTimberKind), kind) ||
            !Enum.TryParse(fields[1], false, out RafterRoofFace face) ||
            !Enum.IsDefined(typeof(RafterRoofFace), face) ||
            !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var station) ||
            station < 0 ||
            fields[3] is not ("0" or "1") ||
            !TryParseFinite(fields[4], out var along) ||
            !TryParseFinite(fields[5], out var lateral) ||
            !TryParseFinite(fields[6], out var rotation) ||
            !TryParseFinite(fields[7], out var start) ||
            !TryParseFinite(fields[8], out var end) ||
            string.IsNullOrWhiteSpace(fields[9]) ||
            fields[9].Contains('|') ||
            fields[9].Contains(ItemSeparator) ||
            fields[9].Contains(FieldSeparator))
        {
            return false;
        }

        var reserved = string.Equals(fields[9], EmptyElementIdToken, StringComparison.Ordinal)
            ? null
            : fields[9];
        item = RoofGeneratedMemberOverrideMath.Normalize(
            new RoofGeneratedMemberOverride(
                new RoofGeneratedMemberKey(kind, face, station),
                fields[3] == "1",
                along,
                lateral,
                rotation,
                start,
                end,
                reserved));
        if (item is null && fields[3] == "1")
        {
            item = RoofGeneratedMemberOverride.Suppress(
                new RoofGeneratedMemberKey(kind, face, station),
                reserved);
        }

        return true;
    }

    private static bool TryParseFinite(string value, out double result) =>
        double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result) &&
        !double.IsNaN(result) &&
        !double.IsInfinity(result);
}
