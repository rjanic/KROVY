using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Unique, ordered set of roof-owned generated-member overrides.
/// Duplicate keys are rejected. Unmapped keys stay dormant rather than being guessed.
/// </summary>
public sealed class RoofManualOverrideSet
{
    private readonly Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberOverride> _byKey;

    public RoofManualOverrideSet(IEnumerable<RoofGeneratedMemberOverride>? overrides = null)
    {
        _byKey = new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberOverride>();
        if (overrides is null)
        {
            return;
        }

        foreach (var item in overrides)
        {
            if (item is null)
            {
                continue;
            }

            _byKey[item.Key] = item;
        }
    }

    public IReadOnlyList<RoofGeneratedMemberOverride> Items =>
        _byKey.Values
            .OrderBy(item => item.Key.MemberKind)
            .ThenBy(item => item.Key.RoofFace)
            .ThenBy(item => item.Key.StationIndex)
            .ToArray();

    public int Count => _byKey.Count;

    public int GeometryOverrideCount =>
        _byKey.Values.Count(item => !item.Suppressed);

    public int SuppressedCount =>
        _byKey.Values.Count(item => item.Suppressed);

    public bool TryGet(RoofGeneratedMemberKey key, out RoofGeneratedMemberOverride overrideData) =>
        _byKey.TryGetValue(key, out overrideData!);

    public RoofManualOverrideSet Upsert(RoofGeneratedMemberOverride? overrideData)
    {
        var next = new RoofManualOverrideSet(Items);
        if (overrideData is null)
        {
            return next;
        }

        var normalized = RoofGeneratedMemberOverrideMath.Normalize(overrideData);
        if (normalized is null)
        {
            next._byKey.Remove(overrideData.Key);
            return next;
        }

        next._byKey[normalized.Key] = normalized;
        return next;
    }

    public RoofManualOverrideSet Remove(RoofGeneratedMemberKey key)
    {
        var next = new RoofManualOverrideSet(Items);
        next._byKey.Remove(key);
        return next;
    }

    public RoofManualOverrideSet Clear() => new();

    public RoofGeneratedMemberOverride? FindMapped(
        RoofGeneratedMemberKey key,
        int stationCount)
    {
        if (!key.MapsToCurrentLayout(stationCount) ||
            !_byKey.TryGetValue(key, out var overrideData))
        {
            return null;
        }

        return overrideData;
    }

    public IReadOnlyList<RoofGeneratedMemberOverride> FindDormant(int stationCount) =>
        Items.Where(item => !item.Key.MapsToCurrentLayout(stationCount)).ToArray();
}
