using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>CAD-neutral product defaults and generated-key reconciliation.</summary>
public static class RoofAutomaticPurlinMaterializationRules
{
    public static TimberElementData CreateTimberData(
        TimberElementDefaultProfile defaultProfile,
        string elementId)
    {
        if (defaultProfile is null)
        {
            throw new ArgumentNullException(nameof(defaultProfile));
        }

        var defaults = TimberElementDefaults.For(TimberElementType.Purlin, defaultProfile);
        return CreateTimberDataCore(
            defaults,
            TimberElementType.Purlin,
            defaults.WidthMm,
            defaults.HeightMm,
            elementId);
    }

    public static TimberElementData CreateTimberData(
        TimberElementDefaultProfile defaultProfile,
        RoofAutomaticPurlinPlanItem item,
        string elementId)
    {
        if (defaultProfile is null)
        {
            throw new ArgumentNullException(nameof(defaultProfile));
        }

        if (!IsValidDesiredItem(item))
        {
            throw new ArgumentException("A valid automatic-purlin plan item is required.", nameof(item));
        }

        return CreateTimberDataCore(
            TimberElementDefaults.For(item.ElementType, defaultProfile),
            item.ElementType,
            item.WidthMm,
            item.HeightMm,
            elementId);
    }

    private static TimberElementData CreateTimberDataCore(
        TimberElementData defaults,
        TimberElementType elementType,
        double widthMm,
        double heightMm,
        string elementId)
    {
        // PrepareForWrite so desired-state equality matches the persisted readback
        // after the first successful materialization write.
        return TimberElementDataVersioning.PrepareForWrite(
            defaults with
            {
                ElementId = elementId,
                ElementType = elementType,
                WidthMm = widthMm,
                HeightMm = heightMm,
                SlopeDegrees = 0d,
                AnnotationMode = TimberAnnotationMode.NoAnnotations,
                LengthCalculationMode = LengthCalculationMode.PlanLength,
                ManualLengthMm = null,
            });
    }

    public static bool IsValidDesiredItem(RoofAutomaticPurlinPlanItem? item)
    {
        if (item is null || item.GeneratedKey is null ||
            !IsMatchingTimberType(item.GeneratedKey, item.ElementType) ||
            !IsFinite(item.WidthMm) || item.WidthMm <= 0d ||
            !IsFinite(item.HeightMm) || item.HeightMm <= 0d)
        {
            return false;
        }

        var segment = item.Segment3D;
        return IsFinite(segment.Start.X) &&
               IsFinite(segment.Start.Y) &&
               IsFinite(segment.Start.Z) &&
               IsFinite(segment.End.X) &&
               IsFinite(segment.End.Y) &&
               IsFinite(segment.End.Z) &&
               Math.Abs(segment.Start.Z - segment.End.Z) <=
               RoofAutomaticPurlinPlanner.CoordinateToleranceMm &&
               IsFinite(segment.LengthMm) &&
               segment.LengthMm > RoofAutomaticPurlinPlanner.CoordinateToleranceMm;
    }

    public static RoofAutomaticPurlinReconciliationResult Reconcile(
        IReadOnlyList<RoofAutomaticPurlinPlanItem>? desired,
        IReadOnlyList<RoofAutomaticPurlinExistingMember>? existing)
    {
        if (desired is null || existing is null || desired.Any(item => !IsValidDesiredItem(item)))
        {
            return Invalid(RoofAutomaticPurlinReconciliationError.InvalidDesiredItem);
        }

        var desiredByKey = new Dictionary<RoofAutomaticPurlinGeneratedKey, RoofAutomaticPurlinPlanItem>();
        foreach (var item in desired)
        {
            if (desiredByKey.ContainsKey(item.GeneratedKey))
            {
                return Invalid(
                    RoofAutomaticPurlinReconciliationError.DuplicateDesiredKey,
                    item.GeneratedKey);
            }

            desiredByKey.Add(item.GeneratedKey, item);
        }

        var existingByKey = new Dictionary<RoofAutomaticPurlinGeneratedKey, RoofAutomaticPurlinExistingMember>();
        foreach (var member in existing)
        {
            if (member is null ||
                string.IsNullOrWhiteSpace(member.EntityToken) ||
                member.GeneratedKey is null)
            {
                return Invalid(RoofAutomaticPurlinReconciliationError.InvalidExistingMember);
            }

            if (existingByKey.ContainsKey(member.GeneratedKey))
            {
                return Invalid(
                    RoofAutomaticPurlinReconciliationError.DuplicateExistingKey,
                    member.GeneratedKey);
            }


            existingByKey.Add(member.GeneratedKey, member);
        }

        var create = desired
            .Where(item => !existingByKey.ContainsKey(item.GeneratedKey))
            .ToArray();
        var reuse = desired
            .Where(item => existingByKey.ContainsKey(item.GeneratedKey))
            .Select(item => new RoofAutomaticPurlinReuse(item, existingByKey[item.GeneratedKey]))
            .ToArray();
        var stale = existing
            .Where(member => !desiredByKey.ContainsKey(member.GeneratedKey!))
            .ToArray();
        return new RoofAutomaticPurlinReconciliationResult(
            true,
            new RoofAutomaticPurlinReconciliationPlan(
                Array.AsReadOnly(create),
                Array.AsReadOnly(reuse),
                Array.AsReadOnly(stale)),
            RoofAutomaticPurlinReconciliationError.None,
            null);
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    public static bool IsMatchingTimberType(
        RoofAutomaticPurlinGeneratedKey key,
        TimberElementType type) => key switch
        {
            RoofAutomaticPurlinWallPlateKey => type == TimberElementType.WallPlate,
            RoofAutomaticPurlinRidgeKey or RoofAutomaticPurlinIntermediateKey =>
                type == TimberElementType.Purlin,
            _ => false,
        };

    private static RoofAutomaticPurlinReconciliationResult Invalid(
        RoofAutomaticPurlinReconciliationError error,
        RoofAutomaticPurlinGeneratedKey? duplicateGeneratedKey = null) => new(
            false,
            null,
            error,
            duplicateGeneratedKey);
}
