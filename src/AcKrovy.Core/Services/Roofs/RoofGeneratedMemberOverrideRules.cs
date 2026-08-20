using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Canonical layout + override replay for one generated member.</summary>
public static class RoofGeneratedMemberOverrideRules
{
    public static bool TryApplyToLayout(
        SimpleGableRafter rafter,
        double sourceElevationMm,
        RoofPoint3D planeNormal,
        RoofManualOverrideSet overrides,
        out RoofGeneratedMemberGeometry? geometry,
        out bool suppressed)
    {
        if (rafter is null)
        {
            throw new ArgumentNullException(nameof(rafter));
        }

        if (overrides is null)
        {
            throw new ArgumentNullException(nameof(overrides));
        }
        geometry = null;
        suppressed = false;
        var canonical = CanonicalGeometry(rafter, sourceElevationMm);
        var key = RoofGeneratedMemberKey.From(rafter);
        var mapped = overrides.FindMapped(key, rafter.StationCount);
        if (mapped is null)
        {
            geometry = canonical;
            return true;
        }

        if (mapped.Suppressed)
        {
            suppressed = true;
            return true;
        }

        if (!RoofGeneratedMemberOverrideMath.TryApply(
                canonical,
                planeNormal,
                mapped,
                out var applied))
        {
            return false;
        }

        geometry = applied;
        return true;
    }

    public static RoofGeneratedMemberGeometry CanonicalGeometry(
        SimpleGableRafter rafter,
        double sourceElevationMm)
    {
        if (rafter is null)
        {
            throw new ArgumentNullException(nameof(rafter));
        }
        return new RoofGeneratedMemberGeometry(
            new RoofPoint3D(rafter.PlanStart.X, rafter.PlanStart.Y, sourceElevationMm),
            new RoofPoint3D(rafter.PlanEnd.X, rafter.PlanEnd.Y, sourceElevationMm));
    }

    public static RoofPoint3D SourceWorkingPlaneNormal { get; } = new(0d, 0d, 1d);

    public static RoofDefinitionData WithEditState(
        RoofDefinitionData geometryData,
        RoofEditState editState,
        IReadOnlyList<RoofGeneratedMemberOverride>? overrides)
    {
        if (geometryData is null)
        {
            throw new ArgumentNullException(nameof(geometryData));
        }
        return geometryData with
        {
            SchemaVersion = RoofDefinitionDataSchema.CurrentVersion,
            EditState = editState,
            ManualOverrides = NormalizeOverrides(overrides),
        };
    }

    public static RoofDefinitionData PreserveEditState(
        RoofDefinitionData geometryData,
        RoofDefinitionData? previous)
    {
        if (geometryData is null)
        {
            throw new ArgumentNullException(nameof(geometryData));
        }
        if (previous is null)
        {
            return WithEditState(geometryData, RoofEditState.Locked, null);
        }

        return WithEditState(geometryData, previous.EditState, previous.Overrides);
    }

    public static IReadOnlyList<RoofGeneratedMemberOverride> NormalizeOverrides(
        IReadOnlyList<RoofGeneratedMemberOverride>? overrides) =>
        new RoofManualOverrideSet(overrides).Items;

    public static bool HasDuplicateKeys(IReadOnlyList<RoofGeneratedMemberOverride>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return false;
        }

        var seen = new HashSet<RoofGeneratedMemberKey>();
        foreach (var item in overrides)
        {
            if (item is null || !seen.Add(item.Key))
            {
                return true;
            }
        }

        return false;
    }
}
