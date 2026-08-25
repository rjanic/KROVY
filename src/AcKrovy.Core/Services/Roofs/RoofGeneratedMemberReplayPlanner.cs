using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public enum RoofGeneratedMemberReplayDisposition
{
    Canonical = 0,
    GeometryReplayed = 1,
    Suppressed = 2,
    DormantInvalidDomain = 3,
}

public sealed record RoofGeneratedMemberReplayItem(
    RoofRafterGeometry Rafter,
    RoofGeneratedMemberGeometry? Geometry,
    RoofGeneratedMemberOverride? Override,
    RoofGeneratedMemberReplayDisposition Disposition);

public sealed record RoofGeneratedMemberReplayPlan(
    bool IsValid,
    IReadOnlyList<RoofGeneratedMemberReplayItem> Items,
    int StoredOverrideCount,
    int ResolvedOverrideCount,
    int GeometryReplayCount,
    int SuppressedCount,
    int DormantMissingKeyCount,
    int DormantInvalidDomainCount,
    int DuplicateKeyCount,
    string? FailureReason)
{
    public int DormantCount => DormantMissingKeyCount + DormantInvalidDomainCount;

    public int MaterializedCount => Items.Count(item => item.Geometry is not null);
}

/// <summary>
/// Plans one atomic generated-rafter materialization from fresh canonical layout geometry
/// plus persisted exact-key overrides. Domain-invalid overrides remain stored and dormant;
/// their exact canonical member is materialized instead.
/// </summary>
public static class RoofGeneratedMemberReplayPlanner
{
    public static RoofGeneratedMemberReplayPlan Create(
        RoofRafterLayout layout,
        double sourceElevationMm,
        RoofPoint3D planeNormal,
        IReadOnlyList<RoofGeneratedMemberOverride>? overrides)
    {
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        var stored = overrides ?? Array.Empty<RoofGeneratedMemberOverride>();
        var validStored = stored.Where(item => item is not null).ToArray();
        var duplicateKeyCount = validStored.Length - validStored
            .Select(item => item.Key)
            .Distinct()
            .Count();
        var overrideSet = new RoofManualOverrideSet(validStored);
        var layoutKeys = new HashSet<RoofGeneratedMemberKey>(
            layout.Rafters.Select(item => item.LogicalKey));
        var resolvedOverrideCount = overrideSet.Items.Count(item => layoutKeys.Contains(item.Key));
        var dormantMissingKeyCount = overrideSet.Count - resolvedOverrideCount;
        var geometryReplayCount = 0;
        var suppressedCount = 0;
        var dormantInvalidDomainCount = 0;
        var items = new List<RoofGeneratedMemberReplayItem>(layout.Rafters.Count);

        foreach (var rafter in layout.Rafters)
        {
            var canonical = RoofGeneratedMemberOverrideRules.CanonicalGeometry(
                rafter,
                sourceElevationMm);
            if (!overrideSet.TryGet(rafter.LogicalKey, out var overrideData))
            {
                items.Add(new RoofGeneratedMemberReplayItem(
                    rafter,
                    canonical,
                    null,
                    RoofGeneratedMemberReplayDisposition.Canonical));
                continue;
            }

            if (overrideData.Suppressed)
            {
                suppressedCount++;
                items.Add(new RoofGeneratedMemberReplayItem(
                    rafter,
                    null,
                    overrideData,
                    RoofGeneratedMemberReplayDisposition.Suppressed));
                continue;
            }

            if (!RoofGeneratedMemberOverrideMath.TryApply(
                    canonical,
                    planeNormal,
                    overrideData,
                    out var applied))
            {
                return Invalid(
                    stored.Count,
                    resolvedOverrideCount,
                    dormantMissingKeyCount,
                    duplicateKeyCount,
                    "override-apply-failed");
            }

            if (!RoofGeneratedMemberDomainRules.OverlapsBoundedPlane(layout, rafter, applied))
            {
                dormantInvalidDomainCount++;
                items.Add(new RoofGeneratedMemberReplayItem(
                    rafter,
                    canonical,
                    overrideData,
                    RoofGeneratedMemberReplayDisposition.DormantInvalidDomain));
                continue;
            }

            geometryReplayCount++;
            items.Add(new RoofGeneratedMemberReplayItem(
                rafter,
                applied,
                overrideData,
                RoofGeneratedMemberReplayDisposition.GeometryReplayed));
        }

        return new RoofGeneratedMemberReplayPlan(
            true,
            items,
            stored.Count,
            resolvedOverrideCount,
            geometryReplayCount,
            suppressedCount,
            dormantMissingKeyCount,
            dormantInvalidDomainCount,
            duplicateKeyCount,
            null);
    }

    private static RoofGeneratedMemberReplayPlan Invalid(
        int storedOverrideCount,
        int resolvedOverrideCount,
        int dormantMissingKeyCount,
        int duplicateKeyCount,
        string reason) =>
        new(
            false,
            Array.Empty<RoofGeneratedMemberReplayItem>(),
            storedOverrideCount,
            resolvedOverrideCount,
            0,
            0,
            dormantMissingKeyCount,
            0,
            duplicateKeyCount,
            reason);
}

/// <summary>
/// CAD-neutral membership test for a final generated centerline against the rafter's
/// current bounded roof plane. Endpoint containment is intentionally not required:
/// any segment overlap with the plane keeps supported TRIM/EXTEND endpoint semantics.
/// </summary>
public static class RoofGeneratedMemberDomainRules
{
    private const double ToleranceMm = RoofGeneratedMemberOverrideMath.LengthToleranceMm;

    public static bool OverlapsBoundedPlane(
        RoofRafterLayout layout,
        RoofRafterGeometry canonicalRafter,
        RoofGeneratedMemberGeometry finalGeometry)
    {
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }
        if (canonicalRafter is null)
        {
            throw new ArgumentNullException(nameof(canonicalRafter));
        }
        if (!IsFinite(finalGeometry.Start) ||
            !IsFinite(finalGeometry.End) ||
            finalGeometry.LengthMm <= ToleranceMm ||
            layout.StationSpanMm <= ToleranceMm ||
            canonicalRafter.PlanLengthMm <= ToleranceMm)
        {
            return false;
        }

        var origin = canonicalRafter.PlanStart;
        var start = ToLocal(finalGeometry.Start, origin, canonicalRafter.RunDirection, layout.StationDirection);
        var end = ToLocal(finalGeometry.End, origin, canonicalRafter.RunDirection, layout.StationDirection);
        var minimumStation = -canonicalRafter.StationPositionMm;
        var maximumStation = layout.StationSpanMm - canonicalRafter.StationPositionMm;
        return SegmentIntersectsRectangle(
            start.U,
            start.V,
            end.U,
            end.V,
            0d,
            canonicalRafter.PlanLengthMm,
            minimumStation,
            maximumStation);
    }

    private static LocalPoint ToLocal(
        RoofPoint3D point,
        RoofPoint2D origin,
        RoofDirection2D runDirection,
        RoofDirection2D stationDirection)
    {
        var dx = point.X - origin.X;
        var dy = point.Y - origin.Y;
        return new LocalPoint(
            dx * runDirection.X + dy * runDirection.Y,
            dx * stationDirection.X + dy * stationDirection.Y);
    }

    private static bool SegmentIntersectsRectangle(
        double startU,
        double startV,
        double endU,
        double endV,
        double minimumU,
        double maximumU,
        double minimumV,
        double maximumV)
    {
        minimumU -= ToleranceMm;
        maximumU += ToleranceMm;
        minimumV -= ToleranceMm;
        maximumV += ToleranceMm;
        var deltaU = endU - startU;
        var deltaV = endV - startV;
        var enter = 0d;
        var leave = 1d;
        return Clip(-deltaU, startU - minimumU, ref enter, ref leave) &&
               Clip(deltaU, maximumU - startU, ref enter, ref leave) &&
               Clip(-deltaV, startV - minimumV, ref enter, ref leave) &&
               Clip(deltaV, maximumV - startV, ref enter, ref leave) &&
               enter <= leave;
    }

    private static bool Clip(double direction, double distance, ref double enter, ref double leave)
    {
        if (Math.Abs(direction) <= ToleranceMm)
        {
            return distance >= 0d;
        }

        var ratio = distance / direction;
        if (direction < 0d)
        {
            if (ratio > leave)
            {
                return false;
            }
            enter = Math.Max(enter, ratio);
        }
        else
        {
            if (ratio < enter)
            {
                return false;
            }
            leave = Math.Min(leave, ratio);
        }

        return true;
    }

    private static bool IsFinite(RoofPoint3D point) =>
        IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private readonly record struct LocalPoint(double U, double V);
}
