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
/// CAD-neutral membership test for a final generated centerline against the
/// current regenerated roof domain. Prefers the authoritative footprint polygon
/// on <see cref="RoofRafterLayout.DomainPolygon"/>. Endpoint containment is not
/// required: any segment overlap keeps supported TRIM/EXTEND past-eave semantics.
/// Completely disjoint outside geometry must dormant as invalid-domain.
/// </summary>
public static class RoofGeneratedMemberDomainRules
{
    private const double ToleranceMm = RoofGeneratedMemberOverrideMath.LengthToleranceMm;

    public readonly record struct DomainEvaluation(
        bool StartInsideFootprint,
        bool EndInsideFootprint,
        bool SegmentIntersectsDomain,
        bool OverlapsDomain,
        string Reason);

    public static bool OverlapsBoundedPlane(
        RoofRafterLayout layout,
        RoofRafterGeometry canonicalRafter,
        RoofGeneratedMemberGeometry finalGeometry) =>
        Evaluate(layout, canonicalRafter, finalGeometry).OverlapsDomain;

    public static DomainEvaluation Evaluate(
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
            finalGeometry.LengthMm <= ToleranceMm)
        {
            return new DomainEvaluation(false, false, false, false, "invalid-geometry");
        }

        var start2 = new RoofPoint2D(finalGeometry.Start.X, finalGeometry.Start.Y);
        var end2 = new RoofPoint2D(finalGeometry.End.X, finalGeometry.End.Y);
        var domain = layout.DomainPolygon;
        if (domain is not null && domain.Count >= 3)
        {
            var startInside = RoofFootprintContainmentRules.IsPointInsideOrOnBoundary(start2, domain);
            var endInside = RoofFootprintContainmentRules.IsPointInsideOrOnBoundary(end2, domain);
            var intersects = RoofFootprintContainmentRules.SegmentOverlapsPolygon(start2, end2, domain);
            return new DomainEvaluation(
                startInside,
                endInside,
                intersects,
                intersects,
                intersects ? "footprint-overlap" : "outside-footprint");
        }

        // Legacy rectangular UV slab for layouts without a footprint polygon.
        if (layout.StationSpanMm <= ToleranceMm ||
            canonicalRafter.PlanLengthMm <= ToleranceMm)
        {
            return new DomainEvaluation(false, false, false, false, "invalid-plane");
        }

        var origin = canonicalRafter.PlanStart;
        var start = ToLocal(finalGeometry.Start, origin, canonicalRafter.RunDirection, layout.StationDirection);
        var end = ToLocal(finalGeometry.End, origin, canonicalRafter.RunDirection, layout.StationDirection);
        var minimumStation = -canonicalRafter.StationPositionMm;
        var maximumStation = layout.StationSpanMm - canonicalRafter.StationPositionMm;
        var overlaps = SegmentIntersectsRectangle(
            start.U,
            start.V,
            end.U,
            end.V,
            0d,
            canonicalRafter.PlanLengthMm,
            minimumStation,
            maximumStation);
        return new DomainEvaluation(
            false,
            false,
            overlaps,
            overlaps,
            overlaps ? "uv-plane-overlap" : "outside-uv-plane");
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
