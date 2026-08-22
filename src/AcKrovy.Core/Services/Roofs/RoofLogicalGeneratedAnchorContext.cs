using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public enum RoofLogicalGeneratedAnchorResolutionKind
{
    LogicalKeyAbsent = 0,
    NotSuppressed = 1,
    VirtualSuppressed = 2,
}

public sealed record RoofLogicalGeneratedAnchorResolution(
    RoofLogicalGeneratedAnchorResolutionKind Kind,
    RoofGeneratedMemberGeometry? Geometry);

public sealed record RoofLogicalGeneratedAnchor(
    RoofGeneratedMemberKey Key,
    RoofGeneratedMemberGeometry CanonicalGeometry);

/// <summary>
/// CAD-neutral, pre-indexed logical Generated-member geometry for one current roof layout.
/// It resolves only intentionally suppressed members; live physical geometry remains the
/// host adapter's authority.
/// </summary>
public sealed class RoofLogicalGeneratedAnchorContext
{
    private readonly IReadOnlyDictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry>
        _canonicalByKey;
    private readonly RoofManualOverrideSet _overrides;

    private RoofLogicalGeneratedAnchorContext(
        IReadOnlyDictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry> canonicalByKey,
        RoofManualOverrideSet overrides)
    {
        _canonicalByKey = canonicalByKey;
        _overrides = overrides;
    }

    public int LogicalMemberCount => _canonicalByKey.Count;

    /// <summary>
    /// Roof-kind-neutral provider boundary. Each generator contributes its current
    /// canonical logical members; resolution semantics stay shared.
    /// </summary>
    public static RoofLogicalGeneratedAnchorContext Create(
        IEnumerable<RoofLogicalGeneratedAnchor> logicalAnchors,
        IEnumerable<RoofGeneratedMemberOverride>? overrides)
    {
        if (logicalAnchors is null)
        {
            throw new ArgumentNullException(nameof(logicalAnchors));
        }

        var canonicalByKey = new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry>();
        foreach (var anchor in logicalAnchors)
        {
            canonicalByKey.Add(anchor.Key, anchor.CanonicalGeometry);
        }

        return new RoofLogicalGeneratedAnchorContext(
            canonicalByKey,
            new RoofManualOverrideSet(overrides));
    }

    public static RoofLogicalGeneratedAnchorContext FromSimpleGableLayout(
        SimpleGableRafterLayout layout,
        double sourceElevationMm,
        IEnumerable<RoofGeneratedMemberOverride>? overrides)
    {
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        return Create(
            layout.Rafters.Select(rafter => new RoofLogicalGeneratedAnchor(
                RoofGeneratedMemberKey.From(rafter),
                RoofGeneratedMemberOverrideRules.CanonicalGeometry(
                    rafter,
                    sourceElevationMm))),
            overrides);
    }

    public RoofLogicalGeneratedAnchorResolution Resolve(RoofGeneratedMemberKey key)
    {
        if (!_canonicalByKey.TryGetValue(key, out var canonical))
        {
            return new RoofLogicalGeneratedAnchorResolution(
                RoofLogicalGeneratedAnchorResolutionKind.LogicalKeyAbsent,
                null);
        }

        if (!_overrides.TryGet(key, out var mapped) || !mapped.Suppressed)
        {
            return new RoofLogicalGeneratedAnchorResolution(
                RoofLogicalGeneratedAnchorResolutionKind.NotSuppressed,
                null);
        }

        // Suppression owns no geometry transformation. ReservedElementId is identity only,
        // so the virtual anchor is exactly the raw canonical segment for K.
        return new RoofLogicalGeneratedAnchorResolution(
            RoofLogicalGeneratedAnchorResolutionKind.VirtualSuppressed,
            canonical);
    }
}
