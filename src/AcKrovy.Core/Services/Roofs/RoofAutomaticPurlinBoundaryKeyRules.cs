using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public enum RoofAutomaticPurlinBoundaryKeyError
{
    None = 0,
    MalformedToken,
    UnsupportedKind,
    NonPositiveBoundaryEdgeId,
    SameBoundaryEdgeId,
    NonCanonicalBoundaryPair,
}

/// <summary>Strict parser and canonical ordering for S1 endpoint-boundary identity.</summary>
public static class RoofAutomaticPurlinBoundaryKeyRules
{
    public static bool TryParse(
        string? value,
        out RoofAutomaticPurlinBoundaryKey? key,
        out RoofAutomaticPurlinBoundaryKeyError error)
    {
        key = null;
        error = RoofAutomaticPurlinBoundaryKeyError.MalformedToken;
        if (value is null)
        {
            return false;
        }

        var parts = value.Split('|');
        if (string.Equals(
                parts[0],
                nameof(RoofTopologyEdgeKind.Eave),
                StringComparison.Ordinal))
        {
            if (parts.Length != 2)
            {
                return false;
            }

            if (!TryParseId(parts[1], out var eaveId, out error)) return false;

            key = new RoofAutomaticPurlinBoundaryKey(
                RoofTopologyEdgeKind.Eave,
                eaveId,
                0);
            error = RoofAutomaticPurlinBoundaryKeyError.None;
            return true;
        }

        if (!TryParseInternalKind(parts[0], out var kind))
        {
            error = RoofAutomaticPurlinBoundaryKeyError.UnsupportedKind;
            return false;
        }

        if (parts.Length != 3)
        {
            return false;
        }

        if (!TryParseId(parts[1], out var first, out error) ||
            !TryParseId(parts[2], out var second, out error)) return false;

        if (first == second)
        {
            error = RoofAutomaticPurlinBoundaryKeyError.SameBoundaryEdgeId;
            return false;
        }

        if (first > second)
        {
            error = RoofAutomaticPurlinBoundaryKeyError.NonCanonicalBoundaryPair;
            return false;
        }

        key = new RoofAutomaticPurlinBoundaryKey(kind, first, second);
        error = RoofAutomaticPurlinBoundaryKeyError.None;
        return true;
    }

    public static bool TryFormat(
        RoofAutomaticPurlinBoundaryKey? key,
        out string value,
        out RoofAutomaticPurlinBoundaryKeyError error)
    {
        value = string.Empty;
        if (!TryValidateCanonical(key, out error) || key is null)
        {
            return false;
        }

        value = key.ToString();
        return true;
    }

    public static bool TryValidateCanonical(
        RoofAutomaticPurlinBoundaryKey? key,
        out RoofAutomaticPurlinBoundaryKeyError error)
    {
        error = RoofAutomaticPurlinBoundaryKeyError.MalformedToken;
        if (key is null)
        {
            return false;
        }

        if (key.Kind == RoofTopologyEdgeKind.Eave)
        {
            if (key.BoundaryEdgeIdA <= 0 || key.BoundaryEdgeIdB != 0)
            {
                error = RoofAutomaticPurlinBoundaryKeyError.NonPositiveBoundaryEdgeId;
                return false;
            }

            error = RoofAutomaticPurlinBoundaryKeyError.None;
            return true;
        }

        if (!IsInternalKind(key.Kind))
        {
            error = RoofAutomaticPurlinBoundaryKeyError.UnsupportedKind;
            return false;
        }

        if (key.BoundaryEdgeIdA <= 0 || key.BoundaryEdgeIdB <= 0)
        {
            error = RoofAutomaticPurlinBoundaryKeyError.NonPositiveBoundaryEdgeId;
            return false;
        }

        if (key.BoundaryEdgeIdA == key.BoundaryEdgeIdB)
        {
            error = RoofAutomaticPurlinBoundaryKeyError.SameBoundaryEdgeId;
            return false;
        }

        if (key.BoundaryEdgeIdA > key.BoundaryEdgeIdB)
        {
            error = RoofAutomaticPurlinBoundaryKeyError.NonCanonicalBoundaryPair;
            return false;
        }

        error = RoofAutomaticPurlinBoundaryKeyError.None;
        return true;
    }

    public static int Compare(
        RoofAutomaticPurlinBoundaryKey first,
        RoofAutomaticPurlinBoundaryKey second)
    {
        var kind = first.Kind.CompareTo(second.Kind);
        if (kind != 0) return kind;
        var firstId = first.BoundaryEdgeIdA.CompareTo(second.BoundaryEdgeIdA);
        return firstId != 0
            ? firstId
            : first.BoundaryEdgeIdB.CompareTo(second.BoundaryEdgeIdB);
    }

    private static bool TryParseId(
        string value,
        out int id,
        out RoofAutomaticPurlinBoundaryKeyError error)
    {
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out id))
        {
            error = RoofAutomaticPurlinBoundaryKeyError.MalformedToken;
            return false;
        }

        if (id <= 0)
        {
            error = RoofAutomaticPurlinBoundaryKeyError.NonPositiveBoundaryEdgeId;
            return false;
        }

        error = RoofAutomaticPurlinBoundaryKeyError.None;
        return true;
    }

    private static bool TryParseInternalKind(
        string token,
        out RoofTopologyEdgeKind kind)
    {
        kind = token switch
        {
            nameof(RoofTopologyEdgeKind.Hip) => RoofTopologyEdgeKind.Hip,
            nameof(RoofTopologyEdgeKind.Valley) => RoofTopologyEdgeKind.Valley,
            nameof(RoofTopologyEdgeKind.Ridge) => RoofTopologyEdgeKind.Ridge,
            nameof(RoofTopologyEdgeKind.CoplanarSeam) => RoofTopologyEdgeKind.CoplanarSeam,
            _ => default,
        };
        return IsInternalKind(kind);
    }

    private static bool IsInternalKind(RoofTopologyEdgeKind kind) => kind is
        RoofTopologyEdgeKind.Hip or
        RoofTopologyEdgeKind.Valley or
        RoofTopologyEdgeKind.Ridge or
        RoofTopologyEdgeKind.CoplanarSeam;
}
