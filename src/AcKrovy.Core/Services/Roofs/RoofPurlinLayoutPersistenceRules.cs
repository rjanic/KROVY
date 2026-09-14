using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>CAD-neutral schema-1 validation for owner purlin-layout persistence.</summary>
public static class RoofPurlinLayoutPersistenceRules
{
    public const string BottomEdgeHeightAboveReferenceToken = "BottomEdgeHeightAboveReference";
    public const string PlanDistanceFromEaveToken = "PlanDistanceFromEave";
    public const string PlanDistanceFromRidgeToken = "PlanDistanceFromRidge";
    public const string NoReferenceRidgeToken = "None";
    public const string RidgeReferenceToken = "Ridge";
    public const string NoSeatingDepthToken = "None";
    public const string PercentOfRafterHeightToken = "PercentOfRafterHeight";
    public const string AbsoluteMmToken = "AbsoluteMm";

    public static RoofPurlinLayoutValidationResult ValidateForWrite(RoofAutomaticPurlinLayout? layout)
    {
        if (layout?.IntermediateItems is null)
        {
            return Invalid(RoofPurlinLayoutPersistenceError.IncompletePayload);
        }

        var items = new List<RoofPurlinLayoutStoredItem>(layout.IntermediateItems.Count);
        foreach (var item in layout.IntermediateItems)
        {
            if (item is null || !TryFormatPlacement(item.PlacementMode, out var placementToken))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidPlacementToken);
            }

            var ridge = item.ReferenceRidgeKey;
            if (ridge is not null &&
                (ridge.Role != RoofStructuralRole.Ridge ||
                 ridge.BoundaryEdgeIdA <= 0 ||
                 ridge.BoundaryEdgeIdA >= ridge.BoundaryEdgeIdB))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidReferenceRidge);
            }

            var ridgeToken = ridge is null ? NoReferenceRidgeToken : RidgeReferenceToken;
            var seatingToken = NoSeatingDepthToken;
            var seatingValue = 0d;
            if (item.SeatingDepth is not null)
            {
                if (!TryFormatSeating(item.SeatingDepth.Mode, out seatingToken))
                {
                    return Invalid(RoofPurlinLayoutPersistenceError.InvalidSeatingDepthToken);
                }

                seatingValue = item.SeatingDepth.Value;
            }

            items.Add(new RoofPurlinLayoutStoredItem(
                item.LayoutItemId,
                item.Enabled ? 1 : 0,
                placementToken,
                item.PlacementValueMm,
                ridgeToken,
                ridge?.BoundaryEdgeIdA ?? 0,
                ridge?.BoundaryEdgeIdB ?? 0,
                seatingToken,
                seatingValue));
        }

        return ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            layout.RidgeEnabled ? 1 : 0,
            items);
    }

    public static RoofPurlinLayoutValidationResult ValidateStored(
        int schemaVersion,
        int ridgeEnabledValue,
        IReadOnlyList<RoofPurlinLayoutStoredItem>? items)
    {
        if (schemaVersion != RoofPurlinLayoutSchema.CurrentVersion)
        {
            return Invalid(RoofPurlinLayoutPersistenceError.UnsupportedSchemaVersion);
        }

        if (!TryParseBoolean(ridgeEnabledValue, out var ridgeEnabled))
        {
            return Invalid(RoofPurlinLayoutPersistenceError.InvalidRidgeEnabled);
        }

        if (items is null)
        {
            return Invalid(RoofPurlinLayoutPersistenceError.IncompletePayload);
        }

        var layoutItems = new List<RoofAutomaticPurlinLayoutItem>(items.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null)
            {
                return Invalid(RoofPurlinLayoutPersistenceError.IncompletePayload);
            }

            if (string.IsNullOrWhiteSpace(item.LayoutItemId))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.EmptyLayoutItemId);
            }

            if (!RoofAutomaticPurlinLayoutItemIdentity.TryNormalize(item.LayoutItemId, out var normalizedId))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.MalformedLayoutItemId);
            }

            if (!string.Equals(item.LayoutItemId, normalizedId, StringComparison.Ordinal))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.NonCanonicalLayoutItemId);
            }

            if (!ids.Add(normalizedId))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.DuplicateLayoutItemId);
            }

            if (!TryParseBoolean(item.EnabledValue, out var enabled))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidEnabled);
            }

            if (!TryParsePlacement(item.PlacementToken, out var placementMode))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidPlacementToken);
            }

            if (!IsFinite(item.PlacementValueMm) || item.PlacementValueMm <= 0d)
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidPlacementValue);
            }

            if (!TryParseRidgeReference(item, placementMode, out var referenceRidge))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidReferenceRidge);
            }

            if (!TryParseSeating(item, placementMode, out var seating, out var seatingError))
            {
                return Invalid(seatingError);
            }

            layoutItems.Add(new RoofAutomaticPurlinLayoutItem(
                normalizedId,
                enabled,
                placementMode,
                item.PlacementValueMm,
                referenceRidge,
                seating));
        }

        return new RoofPurlinLayoutValidationResult(
            true,
            new RoofAutomaticPurlinLayout(ridgeEnabled, Array.AsReadOnly(layoutItems.ToArray())),
            RoofPurlinLayoutPersistenceError.None);
    }

    private static bool TryParseRidgeReference(
        RoofPurlinLayoutStoredItem item,
        RoofAutomaticPurlinPlacementMode placementMode,
        out RoofStructuralLogicalKey? reference)
    {
        reference = null;
        if (string.Equals(item.ReferenceRidgeRoleToken, NoReferenceRidgeToken, StringComparison.Ordinal))
        {
            return item.ReferenceRidgeBoundaryEdgeIdA == 0 && item.ReferenceRidgeBoundaryEdgeIdB == 0;
        }

        if (placementMode != RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge ||
            !string.Equals(item.ReferenceRidgeRoleToken, RidgeReferenceToken, StringComparison.Ordinal) ||
            item.ReferenceRidgeBoundaryEdgeIdA <= 0 ||
            item.ReferenceRidgeBoundaryEdgeIdA >= item.ReferenceRidgeBoundaryEdgeIdB)
        {
            return false;
        }

        reference = new RoofStructuralLogicalKey(
            RoofStructuralRole.Ridge,
            item.ReferenceRidgeBoundaryEdgeIdA,
            item.ReferenceRidgeBoundaryEdgeIdB);
        return true;
    }

    private static bool TryParseSeating(
        RoofPurlinLayoutStoredItem item,
        RoofAutomaticPurlinPlacementMode placementMode,
        out RoofAutomaticPurlinSeatingDepth? seating,
        out RoofPurlinLayoutPersistenceError error)
    {
        seating = null;
        error = RoofPurlinLayoutPersistenceError.None;
        if (placementMode == RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference)
        {
            if (string.Equals(item.SeatingDepthToken, NoSeatingDepthToken, StringComparison.Ordinal) &&
                item.SeatingDepthValue == 0d)
            {
                return true;
            }

            error = RoofPurlinLayoutPersistenceError.InvalidSeatingDepth;
            return false;
        }

        if (!TryParseSeatingToken(item.SeatingDepthToken, out var mode))
        {
            error = RoofPurlinLayoutPersistenceError.InvalidSeatingDepthToken;
            return false;
        }

        if (!IsFinite(item.SeatingDepthValue) || item.SeatingDepthValue <= 0d ||
            (mode == RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight &&
             item.SeatingDepthValue >= 100d))
        {
            error = RoofPurlinLayoutPersistenceError.InvalidSeatingDepth;
            return false;
        }

        seating = new RoofAutomaticPurlinSeatingDepth(mode, item.SeatingDepthValue);
        return true;
    }

    private static bool TryParsePlacement(string token, out RoofAutomaticPurlinPlacementMode mode)
    {
        mode = token switch
        {
            BottomEdgeHeightAboveReferenceToken => RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            PlanDistanceFromEaveToken => RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            PlanDistanceFromRidgeToken => RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge,
            _ => 0,
        };
        return mode != 0;
    }

    private static bool TryFormatPlacement(RoofAutomaticPurlinPlacementMode mode, out string token)
    {
        token = mode switch
        {
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference => BottomEdgeHeightAboveReferenceToken,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave => PlanDistanceFromEaveToken,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge => PlanDistanceFromRidgeToken,
            _ => string.Empty,
        };
        return token.Length > 0;
    }

    private static bool TryParseSeatingToken(string token, out RoofAutomaticPurlinSeatingDepthMode mode)
    {
        mode = token switch
        {
            PercentOfRafterHeightToken => RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            AbsoluteMmToken => RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm,
            _ => 0,
        };
        return mode != 0;
    }

    private static bool TryFormatSeating(RoofAutomaticPurlinSeatingDepthMode mode, out string token)
    {
        token = mode switch
        {
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight => PercentOfRafterHeightToken,
            RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm => AbsoluteMmToken,
            _ => string.Empty,
        };
        return token.Length > 0;
    }

    private static bool TryParseBoolean(int value, out bool parsed)
    {
        parsed = value == 1;
        return value is 0 or 1;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static RoofPurlinLayoutValidationResult Invalid(RoofPurlinLayoutPersistenceError error) =>
        new(false, null, error);
}
