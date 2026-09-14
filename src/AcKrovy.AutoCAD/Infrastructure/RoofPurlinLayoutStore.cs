using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Independent schema-1 XData store for automatic-purlin layout owned by an
/// authoritative roof source Polyline. Reading never creates a default section.
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

        if (values.Count > expectedCount)
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
                seatingDepthValue);
        }

        var validated = RoofPurlinLayoutPersistenceRules.ValidateStored(
            schemaVersion,
            ridgeEnabled,
            items);
        return validated.Layout is not null
            ? RoofPurlinLayoutStoreReadResult.Valid(validated.Layout)
            : RoofPurlinLayoutStoreReadResult.Invalid(validated.Error);
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
        var values = new List<TypedValue>(
            HeaderValueCount + canonical.IntermediateItems.Count * ItemValueCount)
        {
            new(DxfRegAppNameCode, RegAppName),
            new(DxfInt16Code, checked((short)RoofPurlinLayoutSchema.CurrentVersion)),
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
