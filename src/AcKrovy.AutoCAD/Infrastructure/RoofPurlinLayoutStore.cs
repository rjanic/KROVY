using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Independent XData store for automatic-purlin layout owned by an
/// authoritative roof source Polyline. Reading never creates a default section.
/// Schema-1 payloads remain readable; writes use schema-3 when rafter profile exists.
/// </summary>
internal static class RoofPurlinLayoutStore
{
    internal const string RegAppName = "DECORAIR_ACADKROVY_ROOF_PURLIN_LAYOUT";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;
    private const int DxfRealCode = (int)DxfCode.ExtendedDataReal;
    private const int DxfInt16Code = (int)DxfCode.ExtendedDataInteger16;
    private const int DxfInt32Code = (int)DxfCode.ExtendedDataInteger32;
    private const int HeaderValueCount = 4;
    private const int ItemValueCount = 9;
    private const int OptionalWallPlateFlagValueCount = 1;
    private const int OptionalWallPlateElevationValueCount = 1;
    private const int OptionalWallPlateLegacyValueCount =
        OptionalWallPlateFlagValueCount + OptionalWallPlateElevationValueCount;
    /// <summary>
    /// Legacy full WallPlate placement trailer: flag + 7 placement fields
    /// (no separate LowerEdge Real; LowerEdge is reconstructed from Place).
    /// </summary>
    private const int OptionalWallPlatePlacementFieldCount = 7;
    private const int OptionalWallPlatePlacementValueCount =
        OptionalWallPlateFlagValueCount + OptionalWallPlatePlacementFieldCount;
    /// <summary>
    /// Extended full WallPlate placement trailer: legacy 7 fields + explicit
    /// WallPlateLowerEdgeHeightMm Real. Required when product Place=0 under
    /// WallPlateBottom stashes the absolute bootstrap bottom separately.
    /// </summary>
    private const int OptionalWallPlatePlacementLowerEdgeFieldCount = 1;
    private const int OptionalWallPlatePlacementWithLowerEdgeFieldCount =
        OptionalWallPlatePlacementFieldCount + OptionalWallPlatePlacementLowerEdgeFieldCount;
    private const int OptionalWallPlatePlacementWithLowerEdgeValueCount =
        OptionalWallPlateFlagValueCount + OptionalWallPlatePlacementWithLowerEdgeFieldCount;
    /// <summary>
    /// Optional section-dimensions trailer: marker + wall-plate WxH + ridge WxH.
    /// Intermediate WxH pairs (2*N) follow when the trailer is present.
    /// </summary>
    private const short SectionDimensionsTrailerMarker = 1;
    private const int OptionalSectionDimensionsFixedValueCount = 5;
    /// <summary>Schema-2/3 optional rafter profile trailer marker.</summary>
    private const short RafterProfileTrailerMarker = 2;
    /// <summary>Schema-2: marker + policy + manual W + H.</summary>
    private const int OptionalRafterProfileSchema2ValueCount = 4;
    /// <summary>Schema-3: schema-2 fields + ack kind + acknowledged actual W + H.</summary>
    private const int OptionalRafterProfileSchema3ValueCount = 7;

    public static RoofPurlinLayoutStoreReadResult Read(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity is not Polyline source || RoofDefinitionStore.Read(source).Data is null)
        {
            return RoofPurlinLayoutStoreReadResult.Invalid(
                RoofPurlinLayoutPersistenceError.NotAuthoritativeSource);
        }

        try
        {
            using var xdata = source.GetXDataForApplication(RegAppName);
            return xdata is null
                ? RoofPurlinLayoutStoreReadResult.Missing
                : DecodePayload(xdata.AsArray());
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofPurlinLayoutStoreReadResult.Invalid(
                RoofPurlinLayoutPersistenceError.MalformedValueType);
        }
    }

    internal static RoofPurlinLayoutStoreReadResult DecodePayload(
        IReadOnlyList<TypedValue> values)
    {
        if (values.Count < HeaderValueCount)
        {
            return RoofPurlinLayoutStoreReadResult.Invalid(
                RoofPurlinLayoutPersistenceError.IncompletePayload);
        }

        if (values[0].TypeCode != DxfRegAppNameCode ||
            values[0].Value is not string applicationName ||
            !string.Equals(applicationName, RegAppName, StringComparison.OrdinalIgnoreCase) ||
            values[1].TypeCode != DxfInt16Code ||
            values[1].Value is not short schemaVersion ||
            values[2].TypeCode != DxfInt16Code ||
            values[2].Value is not short ridgeEnabled ||
            values[3].TypeCode != DxfInt32Code ||
            values[3].Value is not int itemCount)
        {
            return RoofPurlinLayoutStoreReadResult.Invalid(
                RoofPurlinLayoutPersistenceError.MalformedValueType);
        }

        if (itemCount < 0)
        {
            return RoofPurlinLayoutStoreReadResult.Invalid(
                RoofPurlinLayoutPersistenceError.InvalidItemCount);
        }

        var expectedCount = (long)HeaderValueCount + (long)itemCount * ItemValueCount;
        if (values.Count < expectedCount)
        {
            return RoofPurlinLayoutStoreReadResult.Invalid(
                RoofPurlinLayoutPersistenceError.IncompletePayload);
        }

        var sectionDimensionsTrailerCount =
            OptionalSectionDimensionsFixedValueCount + 2L * itemCount;
        var maxTrailingCount =
            OptionalWallPlatePlacementWithLowerEdgeValueCount +
            sectionDimensionsTrailerCount +
            OptionalRafterProfileSchema3ValueCount;
        if (values.Count > expectedCount + maxTrailingCount)
        {
            return RoofPurlinLayoutStoreReadResult.Invalid(
                RoofPurlinLayoutPersistenceError.UnexpectedTrailingValue);
        }

        short wallPlateEnabled = 0;
        var wallPlateLowerEdgeHeightMm = 0d;
        var wallPlateLowerEdgeHeightExplicit = false;
        RoofPurlinLayoutStoredItem? wallPlatePlacementItem = null;
        RoofPurlinLayoutStoredSectionDimensions? sectionDimensions = null;
        RoofPurlinLayoutStoredRafterProfile? rafterProfile = null;
        var intermediateWidths = new double[itemCount];
        var intermediateHeights = new double[itemCount];
        var trailingCount = values.Count - (int)expectedCount;
        var remainingTrailing = trailingCount;

        if (remainingTrailing >= OptionalRafterProfileSchema3ValueCount &&
            TryDecodeRafterProfileTrailerSchema3(
                values,
                (int)expectedCount + remainingTrailing - OptionalRafterProfileSchema3ValueCount,
                out rafterProfile))
        {
            remainingTrailing -= OptionalRafterProfileSchema3ValueCount;
        }
        else if (remainingTrailing >= OptionalRafterProfileSchema2ValueCount &&
                 TryDecodeRafterProfileTrailerSchema2(
                     values,
                     (int)expectedCount + remainingTrailing - OptionalRafterProfileSchema2ValueCount,
                     out rafterProfile))
        {
            remainingTrailing -= OptionalRafterProfileSchema2ValueCount;
        }

        if (remainingTrailing >= sectionDimensionsTrailerCount &&
            TryDecodeSectionDimensionsTrailer(
                values,
                (int)expectedCount + remainingTrailing - (int)sectionDimensionsTrailerCount,
                itemCount,
                out sectionDimensions,
                intermediateWidths,
                intermediateHeights))
        {
            remainingTrailing -= (int)sectionDimensionsTrailerCount;
        }

        var wallPlateTrailingCount = remainingTrailing;
        if (wallPlateTrailingCount >= OptionalWallPlateFlagValueCount)
        {
            if (values[(int)expectedCount].TypeCode != DxfInt16Code ||
                values[(int)expectedCount].Value is not short storedWallPlateEnabled)
            {
                return RoofPurlinLayoutStoreReadResult.Invalid(
                    RoofPurlinLayoutPersistenceError.MalformedValueType);
            }

            wallPlateEnabled = storedWallPlateEnabled;
        }

        if (wallPlateTrailingCount == OptionalWallPlateLegacyValueCount)
        {
            if (values[(int)expectedCount + OptionalWallPlateFlagValueCount].TypeCode != DxfRealCode ||
                values[(int)expectedCount + OptionalWallPlateFlagValueCount].Value is not double
                    storedWallPlateLowerEdgeHeightMm)
            {
                return RoofPurlinLayoutStoreReadResult.Invalid(
                    RoofPurlinLayoutPersistenceError.MalformedValueType);
            }

            wallPlateLowerEdgeHeightMm = storedWallPlateLowerEdgeHeightMm;
            wallPlateLowerEdgeHeightExplicit = true;
        }
        else if (wallPlateTrailingCount == OptionalWallPlatePlacementWithLowerEdgeValueCount ||
                 wallPlateTrailingCount == OptionalWallPlatePlacementValueCount)
        {
            var offset = (int)expectedCount + OptionalWallPlateFlagValueCount;
            if (values[offset].TypeCode != DxfAsciiStringCode ||
                values[offset].Value is not string placementToken ||
                values[offset + 1].TypeCode != DxfRealCode ||
                values[offset + 1].Value is not double placementValueMm ||
                values[offset + 2].TypeCode != DxfAsciiStringCode ||
                values[offset + 2].Value is not string referenceRidgeRoleToken ||
                values[offset + 3].TypeCode != DxfInt32Code ||
                values[offset + 3].Value is not int referenceRidgeBoundaryEdgeIdA ||
                values[offset + 4].TypeCode != DxfInt32Code ||
                values[offset + 4].Value is not int referenceRidgeBoundaryEdgeIdB ||
                values[offset + 5].TypeCode != DxfAsciiStringCode ||
                values[offset + 5].Value is not string seatingDepthToken ||
                values[offset + 6].TypeCode != DxfRealCode ||
                values[offset + 6].Value is not double seatingDepthValue)
            {
                return RoofPurlinLayoutStoreReadResult.Invalid(
                    RoofPurlinLayoutPersistenceError.MalformedValueType);
            }

            wallPlatePlacementItem = new RoofPurlinLayoutStoredItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                1,
                placementToken,
                placementValueMm,
                referenceRidgeRoleToken,
                referenceRidgeBoundaryEdgeIdA,
                referenceRidgeBoundaryEdgeIdB,
                seatingDepthToken,
                seatingDepthValue);

            if (wallPlateTrailingCount == OptionalWallPlatePlacementWithLowerEdgeValueCount)
            {
                if (values[offset + 7].TypeCode != DxfRealCode ||
                    values[offset + 7].Value is not double storedLowerEdgeHeightMm)
                {
                    return RoofPurlinLayoutStoreReadResult.Invalid(
                        RoofPurlinLayoutPersistenceError.MalformedValueType);
                }

                wallPlateLowerEdgeHeightMm = storedLowerEdgeHeightMm;
                wallPlateLowerEdgeHeightExplicit = true;
            }
        }
        else if (wallPlateTrailingCount != 0 &&
                 wallPlateTrailingCount != OptionalWallPlateFlagValueCount)
        {
            return RoofPurlinLayoutStoreReadResult.Invalid(
                RoofPurlinLayoutPersistenceError.UnexpectedTrailingValue);
        }

        var items = new RoofPurlinLayoutStoredItem[itemCount];
        for (var itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            var offset = HeaderValueCount + itemIndex * ItemValueCount;
            if (values[offset].TypeCode != DxfAsciiStringCode ||
                values[offset].Value is not string layoutItemId ||
                values[offset + 1].TypeCode != DxfInt16Code ||
                values[offset + 1].Value is not short enabled ||
                values[offset + 2].TypeCode != DxfAsciiStringCode ||
                values[offset + 2].Value is not string placementToken ||
                values[offset + 3].TypeCode != DxfRealCode ||
                values[offset + 3].Value is not double placementValueMm ||
                values[offset + 4].TypeCode != DxfAsciiStringCode ||
                values[offset + 4].Value is not string referenceRidgeRoleToken ||
                values[offset + 5].TypeCode != DxfInt32Code ||
                values[offset + 5].Value is not int referenceRidgeBoundaryEdgeIdA ||
                values[offset + 6].TypeCode != DxfInt32Code ||
                values[offset + 6].Value is not int referenceRidgeBoundaryEdgeIdB ||
                values[offset + 7].TypeCode != DxfAsciiStringCode ||
                values[offset + 7].Value is not string seatingDepthToken ||
                values[offset + 8].TypeCode != DxfRealCode ||
                values[offset + 8].Value is not double seatingDepthValue)
            {
                return RoofPurlinLayoutStoreReadResult.Invalid(
                    RoofPurlinLayoutPersistenceError.MalformedValueType);
            }

            items[itemIndex] = new RoofPurlinLayoutStoredItem(
                layoutItemId,
                enabled,
                placementToken,
                placementValueMm,
                referenceRidgeRoleToken,
                referenceRidgeBoundaryEdgeIdA,
                referenceRidgeBoundaryEdgeIdB,
                seatingDepthToken,
                seatingDepthValue,
                sectionDimensions is null ? 0d : intermediateWidths[itemIndex],
                sectionDimensions is null ? 0d : intermediateHeights[itemIndex]);
        }

        var validated = RoofPurlinLayoutPersistenceRules.ValidateStored(
            schemaVersion,
            ridgeEnabled,
            items,
            wallPlateEnabled,
            wallPlateLowerEdgeHeightMm,
            wallPlatePlacementItem,
            sectionDimensions,
            rafterProfile,
            wallPlateLowerEdgeHeightExplicit);
        return validated.Layout is not null
            ? RoofPurlinLayoutStoreReadResult.Valid(validated.Layout)
            : RoofPurlinLayoutStoreReadResult.Invalid(validated.Error);
    }

    private static bool TryDecodeSectionDimensionsTrailer(
        IReadOnlyList<TypedValue> values,
        int startIndex,
        int itemCount,
        out RoofPurlinLayoutStoredSectionDimensions sectionDimensions,
        double[] intermediateWidths,
        double[] intermediateHeights)
    {
        sectionDimensions = null!;
        if (values[startIndex].TypeCode != DxfInt16Code ||
            values[startIndex].Value is not short marker ||
            marker != SectionDimensionsTrailerMarker ||
            values[startIndex + 1].TypeCode != DxfRealCode ||
            values[startIndex + 1].Value is not double wallPlateWidthMm ||
            values[startIndex + 2].TypeCode != DxfRealCode ||
            values[startIndex + 2].Value is not double wallPlateHeightMm ||
            values[startIndex + 3].TypeCode != DxfRealCode ||
            values[startIndex + 3].Value is not double ridgeWidthMm ||
            values[startIndex + 4].TypeCode != DxfRealCode ||
            values[startIndex + 4].Value is not double ridgeHeightMm)
        {
            return false;
        }

        var pairOffset = startIndex + OptionalSectionDimensionsFixedValueCount;
        for (var itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            var offset = pairOffset + itemIndex * 2;
            if (values[offset].TypeCode != DxfRealCode ||
                values[offset].Value is not double widthMm ||
                values[offset + 1].TypeCode != DxfRealCode ||
                values[offset + 1].Value is not double heightMm)
            {
                return false;
            }

            intermediateWidths[itemIndex] = widthMm;
            intermediateHeights[itemIndex] = heightMm;
        }

        sectionDimensions = new RoofPurlinLayoutStoredSectionDimensions(
            wallPlateWidthMm,
            wallPlateHeightMm,
            ridgeWidthMm,
            ridgeHeightMm);
        return true;
    }

    private static bool TryDecodeRafterProfileTrailerSchema2(
        IReadOnlyList<TypedValue> values,
        int startIndex,
        out RoofPurlinLayoutStoredRafterProfile? rafterProfile)
    {
        rafterProfile = null;
        if (startIndex < 0 ||
            startIndex + OptionalRafterProfileSchema2ValueCount > values.Count)
        {
            return false;
        }

        if (values[startIndex].TypeCode != DxfInt16Code ||
            values[startIndex].Value is not short marker ||
            marker != RafterProfileTrailerMarker ||
            values[startIndex + 1].TypeCode != DxfInt16Code ||
            values[startIndex + 1].Value is not short policyValue ||
            values[startIndex + 2].TypeCode != DxfRealCode ||
            values[startIndex + 2].Value is not double widthMm ||
            values[startIndex + 3].TypeCode != DxfRealCode ||
            values[startIndex + 3].Value is not double heightMm)
        {
            return false;
        }

        rafterProfile = new RoofPurlinLayoutStoredRafterProfile(
            policyValue,
            widthMm,
            heightMm);
        return true;
    }

    private static bool TryDecodeRafterProfileTrailerSchema3(
        IReadOnlyList<TypedValue> values,
        int startIndex,
        out RoofPurlinLayoutStoredRafterProfile? rafterProfile)
    {
        rafterProfile = null;
        if (startIndex < 0 ||
            startIndex + OptionalRafterProfileSchema3ValueCount > values.Count)
        {
            return false;
        }

        if (values[startIndex].TypeCode != DxfInt16Code ||
            values[startIndex].Value is not short marker ||
            marker != RafterProfileTrailerMarker ||
            values[startIndex + 1].TypeCode != DxfInt16Code ||
            values[startIndex + 1].Value is not short policyValue ||
            values[startIndex + 2].TypeCode != DxfRealCode ||
            values[startIndex + 2].Value is not double widthMm ||
            values[startIndex + 3].TypeCode != DxfRealCode ||
            values[startIndex + 3].Value is not double heightMm ||
            values[startIndex + 4].TypeCode != DxfInt16Code ||
            values[startIndex + 4].Value is not short acknowledgedKind ||
            values[startIndex + 5].TypeCode != DxfRealCode ||
            values[startIndex + 5].Value is not double acknowledgedWidthMm ||
            values[startIndex + 6].TypeCode != DxfRealCode ||
            values[startIndex + 6].Value is not double acknowledgedHeightMm)
        {
            return false;
        }

        rafterProfile = new RoofPurlinLayoutStoredRafterProfile(
            policyValue,
            widthMm,
            heightMm,
            acknowledgedKind,
            acknowledgedWidthMm,
            acknowledgedHeightMm);
        return true;
    }

    internal static IReadOnlyList<TypedValue> EncodePayload(
        RoofAutomaticPurlinLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var validated = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);
        if (validated.Layout is null)
        {
            throw new ArgumentException(
                "Invalid automatic-purlin layout metadata: " + validated.Error,
                nameof(layout));
        }

        var canonical = validated.Layout;
        var writeRafterProfile =
            RoofPurlinLayoutPersistenceRules.HasPersistedRafterProfile(canonical);
        var values = new List<TypedValue>(
            HeaderValueCount + canonical.IntermediateItems.Count * ItemValueCount +
            OptionalWallPlatePlacementValueCount +
            OptionalSectionDimensionsFixedValueCount +
            2 * canonical.IntermediateItems.Count +
            (writeRafterProfile ? OptionalRafterProfileSchema3ValueCount : 0))
        {
            new(DxfRegAppNameCode, RegAppName),
            new(
                DxfInt16Code,
                checked((short)(writeRafterProfile
                    ? RoofPurlinLayoutSchema.CurrentVersion
                    : RoofPurlinLayoutSchema.Version1))),
            new(DxfInt16Code, checked((short)(canonical.RidgeEnabled ? 1 : 0))),
            new(DxfInt32Code, canonical.IntermediateItems.Count),
        };
        foreach (var item in canonical.IntermediateItems)
        {
            values.Add(new TypedValue(DxfAsciiStringCode, item.LayoutItemId));
            values.Add(new TypedValue(
                DxfInt16Code,
                checked((short)(item.Enabled ? 1 : 0))));
            values.Add(new TypedValue(
                DxfAsciiStringCode,
                item.PlacementMode switch
                {
                    RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference =>
                        RoofPurlinLayoutPersistenceRules.BottomEdgeHeightAboveReferenceToken,
                    RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave =>
                        RoofPurlinLayoutPersistenceRules.PlanDistanceFromEaveToken,
                    RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge =>
                        RoofPurlinLayoutPersistenceRules.PlanDistanceFromRidgeToken,
                    _ => throw new InvalidOperationException("Unsupported purlin placement mode."),
                }));
            values.Add(new TypedValue(DxfRealCode, item.PlacementValueMm));
            values.Add(new TypedValue(
                DxfAsciiStringCode,
                item.ReferenceRidgeKey is null
                    ? RoofPurlinLayoutPersistenceRules.NoReferenceRidgeToken
                    : RoofPurlinLayoutPersistenceRules.RidgeReferenceToken));
            values.Add(new TypedValue(DxfInt32Code, item.ReferenceRidgeKey?.BoundaryEdgeIdA ?? 0));
            values.Add(new TypedValue(DxfInt32Code, item.ReferenceRidgeKey?.BoundaryEdgeIdB ?? 0));
            values.Add(new TypedValue(
                DxfAsciiStringCode,
                item.SeatingDepth?.Mode switch
                {
                    null => RoofPurlinLayoutPersistenceRules.NoSeatingDepthToken,
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight =>
                        RoofPurlinLayoutPersistenceRules.PercentOfRafterHeightToken,
                    RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm =>
                        RoofPurlinLayoutPersistenceRules.AbsoluteMmToken,
                    _ => throw new InvalidOperationException("Unsupported purlin seating-depth mode."),
                }));
            values.Add(new TypedValue(DxfRealCode, item.SeatingDepth?.Value ?? 0d));
        }

        var wallPlatePlacement = RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(canonical);
        var writeWallPlate =
            canonical.WallPlateEnabled ||
            Math.Abs(canonical.WallPlateLowerEdgeHeightMm) > 1e-9 ||
            wallPlatePlacement.PlacementMode !=
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference ||
            wallPlatePlacement.SeatingDepth is not null ||
            Math.Abs(wallPlatePlacement.PlacementValueMm) > 1e-9;
        if (writeWallPlate)
        {
            values.Add(new TypedValue(
                DxfInt16Code,
                checked((short)(canonical.WallPlateEnabled ? 1 : 0))));
            // Legacy single-Real trailer only when Place and LowerEdge are the same
            // BottomEdge value. Divergent Place=0 + absolute LowerEdge stash must use
            // the extended full-placement trailer so both values survive readback.
            var canUseLegacySingleReal =
                wallPlatePlacement.PlacementMode ==
                    RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference &&
                wallPlatePlacement.SeatingDepth is null &&
                wallPlatePlacement.ReferenceRidgeKey is null &&
                wallPlatePlacement.PlacementValueMm >= 0d &&
                Math.Abs(
                    canonical.WallPlateLowerEdgeHeightMm -
                    wallPlatePlacement.PlacementValueMm) <= 1e-9;
            if (canUseLegacySingleReal)
            {
                values.Add(new TypedValue(DxfRealCode, wallPlatePlacement.PlacementValueMm));
            }
            else
            {
                var stored = RoofPurlinLayoutPersistenceRules.ToStoredWallPlatePlacement(wallPlatePlacement);
                values.Add(new TypedValue(DxfAsciiStringCode, stored.PlacementToken));
                values.Add(new TypedValue(DxfRealCode, stored.PlacementValueMm));
                values.Add(new TypedValue(DxfAsciiStringCode, stored.ReferenceRidgeRoleToken));
                values.Add(new TypedValue(DxfInt32Code, stored.ReferenceRidgeBoundaryEdgeIdA));
                values.Add(new TypedValue(DxfInt32Code, stored.ReferenceRidgeBoundaryEdgeIdB));
                values.Add(new TypedValue(DxfAsciiStringCode, stored.SeatingDepthToken));
                values.Add(new TypedValue(DxfRealCode, stored.SeatingDepthValue));
                values.Add(new TypedValue(DxfRealCode, canonical.WallPlateLowerEdgeHeightMm));
            }
        }

        if (RoofPurlinLayoutPersistenceRules.HasPersistedSectionDimensions(canonical))
        {
            values.Add(new TypedValue(DxfInt16Code, SectionDimensionsTrailerMarker));
            values.Add(new TypedValue(
                DxfRealCode,
                RoofPurlinLayoutPersistenceRules.ToStoredSectionDimension(wallPlatePlacement.WidthMm)));
            values.Add(new TypedValue(
                DxfRealCode,
                RoofPurlinLayoutPersistenceRules.ToStoredSectionDimension(wallPlatePlacement.HeightMm)));
            values.Add(new TypedValue(
                DxfRealCode,
                RoofPurlinLayoutPersistenceRules.ToStoredSectionDimension(canonical.RidgeWidthMm)));
            values.Add(new TypedValue(
                DxfRealCode,
                RoofPurlinLayoutPersistenceRules.ToStoredSectionDimension(canonical.RidgeHeightMm)));
            foreach (var item in canonical.IntermediateItems)
            {
                values.Add(new TypedValue(
                    DxfRealCode,
                    RoofPurlinLayoutPersistenceRules.ToStoredSectionDimension(item.WidthMm)));
                values.Add(new TypedValue(
                    DxfRealCode,
                    RoofPurlinLayoutPersistenceRules.ToStoredSectionDimension(item.HeightMm)));
            }
        }

        if (writeRafterProfile)
        {
            values.Add(new TypedValue(DxfInt16Code, RafterProfileTrailerMarker));
            values.Add(new TypedValue(
                DxfInt16Code,
                checked((short)canonical.RafterSourcePolicy)));
            values.Add(new TypedValue(
                DxfRealCode,
                RoofPurlinLayoutPersistenceRules.ToStoredSectionDimension(canonical.ManualRafterWidthMm)));
            values.Add(new TypedValue(
                DxfRealCode,
                RoofPurlinLayoutPersistenceRules.ToStoredSectionDimension(canonical.ManualRafterHeightMm)));
            values.Add(new TypedValue(
                DxfInt16Code,
                checked((short)canonical.AcknowledgedActualKind)));
            values.Add(new TypedValue(
                DxfRealCode,
                RoofPurlinLayoutPersistenceRules.ToStoredSectionDimension(
                    canonical.AcknowledgedActualWidthMm)));
            values.Add(new TypedValue(
                DxfRealCode,
                RoofPurlinLayoutPersistenceRules.ToStoredSectionDimension(
                    canonical.AcknowledgedActualHeightMm)));
        }

        return values.AsReadOnly();
    }

    public static void Write(
        Polyline source,
        Transaction transaction,
        RoofAutomaticPurlinLayout layout)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(layout);
        if (!source.IsWriteEnabled)
        {
            throw new InvalidOperationException(
                "Automatic-purlin layout source must be opened ForWrite.");
        }

        if (RoofDefinitionStore.Read(source).Data is null)
        {
            throw new InvalidOperationException(
                "Automatic-purlin layout may be written only to an authoritative roof source.");
        }

        var section = EncodePayload(layout);
        EnsureRegAppRegistered(source.Database, transaction);
        var retained = ReadForeignXData(source);
        retained.AddRange(section);
        using var buffer = new ResultBuffer(retained.ToArray());
        source.XData = buffer;
    }

    private static List<TypedValue> ReadForeignXData(Entity entity)
    {
        var retained = new List<TypedValue>();
        using var xdata = entity.XData;
        if (xdata is null)
        {
            return retained;
        }

        var skipOwnSection = false;
        foreach (var value in xdata.AsArray())
        {
            if (value.TypeCode == DxfRegAppNameCode)
            {
                skipOwnSection = string.Equals(
                    Convert.ToString(value.Value, CultureInfo.InvariantCulture),
                    RegAppName,
                    StringComparison.OrdinalIgnoreCase);
            }

            if (!skipOwnSection)
            {
                retained.Add(value);
            }
        }

        return retained;
    }

    private static void EnsureRegAppRegistered(
        Database database,
        Transaction transaction)
    {
        var table = (RegAppTable)transaction.GetObject(
            database.RegAppTableId,
            OpenMode.ForRead);
        if (table.Has(RegAppName))
        {
            return;
        }

        table.UpgradeOpen();
        var record = new RegAppTableRecord { Name = RegAppName };
        table.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }
}

internal sealed record RoofPurlinLayoutStoreReadResult(
    bool Exists,
    RoofAutomaticPurlinLayout? Data,
    RoofPurlinLayoutPersistenceError Error)
{
    public static RoofPurlinLayoutStoreReadResult Missing { get; } =
        new(false, RoofAutomaticPurlinLayout.Empty, RoofPurlinLayoutPersistenceError.Missing);

    public static RoofPurlinLayoutStoreReadResult Valid(RoofAutomaticPurlinLayout data) =>
        new(true, data, RoofPurlinLayoutPersistenceError.None);

    public static RoofPurlinLayoutStoreReadResult Invalid(
        RoofPurlinLayoutPersistenceError error) => new(true, null, error);
}
