using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Independent typed XData store for generated structural roof members.
/// </summary>
internal static class RoofStructuralGeneratedStore
{
    internal const string RegAppName = "DECORAIR_ACADKROVY_ROOF_STRUCTURAL_GENERATED";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;
    private const int DxfInt16Code = (int)DxfCode.ExtendedDataInteger16;
    private const int DxfInt32Code = (int)DxfCode.ExtendedDataInteger32;

    public static RoofStructuralGeneratedStoreReadResult Read(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        try
        {
            using var xdata = entity.GetXDataForApplication(RegAppName);
            return xdata is null
                ? RoofStructuralGeneratedStoreReadResult.Missing
                : DecodePayload(xdata.AsArray());
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofStructuralGeneratedStoreReadResult.Invalid(
                RoofStructuralGeneratedDataError.MalformedValueType);
        }
    }

    internal static RoofStructuralGeneratedStoreReadResult DecodePayload(
        IReadOnlyList<TypedValue> values)
    {
        if (values.Count < 6)
        {
            return RoofStructuralGeneratedStoreReadResult.Invalid(
                RoofStructuralGeneratedDataError.IncompletePayload);
        }

        if (values.Count > 6)
        {
            return RoofStructuralGeneratedStoreReadResult.Invalid(
                RoofStructuralGeneratedDataError.UnexpectedTrailingValue);
        }

        if (values[0].TypeCode != DxfRegAppNameCode ||
            values[0].Value is not string applicationName ||
            !string.Equals(applicationName, RegAppName, StringComparison.OrdinalIgnoreCase) ||
            values[1].TypeCode != DxfInt16Code ||
            values[1].Value is not short schemaVersion ||
            values[2].TypeCode != DxfAsciiStringCode ||
            values[2].Value is not string ownerReference ||
            values[3].TypeCode != DxfAsciiStringCode ||
            values[3].Value is not string roleToken ||
            values[4].TypeCode != DxfInt32Code ||
            values[4].Value is not int boundaryEdgeIdA ||
            values[5].TypeCode != DxfInt32Code ||
            values[5].Value is not int boundaryEdgeIdB)
        {
            return RoofStructuralGeneratedStoreReadResult.Invalid(
                RoofStructuralGeneratedDataError.MalformedValueType);
        }

        var validated = RoofStructuralGeneratedDataRules.ValidateStored(
            schemaVersion,
            ownerReference,
            roleToken,
            boundaryEdgeIdA,
            boundaryEdgeIdB);
        return validated.Data is not null
            ? RoofStructuralGeneratedStoreReadResult.Valid(validated.Data)
            : RoofStructuralGeneratedStoreReadResult.Invalid(validated.Error);
    }

    public static void Write(
        Entity entity,
        Transaction transaction,
        RoofStructuralGeneratedData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        if (!entity.IsWriteEnabled)
        {
            throw new InvalidOperationException(
                "Structural generated member must be opened ForWrite.");
        }

        var retained = ReadForeignXData(entity);
        retained.AddRange(BuildSection(entity, transaction, data));
        using var buffer = new ResultBuffer(retained.ToArray());
        entity.XData = buffer;
    }

    /// <summary>
    /// Writes generic Timber and StructuralGenerated metadata in the same XData
    /// assignment before a newly appended entity is registered with the transaction.
    /// </summary>
    public static void WriteAtomic(
        Entity entity,
        Transaction transaction,
        AcKrovy.Core.Models.TimberElementData timberData,
        RoofStructuralGeneratedData structuralData)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(timberData);
        ArgumentNullException.ThrowIfNull(structuralData);
        if (!entity.IsWriteEnabled)
        {
            throw new InvalidOperationException(
                "Structural generated member must be opened ForWrite.");
        }

        var values = ReadForeignXData(entity, removeGenericTimberSection: true);
        values.AddRange(ElementDataStore.BuildSection(entity, transaction, timberData));
        values.AddRange(BuildSection(entity, transaction, structuralData));
        using var buffer = new ResultBuffer(values.ToArray());
        entity.XData = buffer;
    }

    public static IReadOnlyList<ObjectId> FindByOwner(
        Database database,
        Transaction transaction,
        string ownerReference)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!RoofStructuralGeneratedDataRules.TryNormalizeOwnerReference(
                ownerReference,
                out var normalizedOwner))
        {
            return Array.Empty<ObjectId>();
        }

        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        var matches = new List<ObjectId>();
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity ||
                entity.IsErased)
            {
                continue;
            }

            var stored = Read(entity);
            if (stored.Data is not null &&
                string.Equals(
                    stored.Data.RoofOwnerReference,
                    normalizedOwner,
                    StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(id);
            }
        }

        return matches;
    }

    public static IReadOnlyList<TypedValue> BuildSection(
        Entity entity,
        Transaction transaction,
        RoofStructuralGeneratedData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        var validated = RoofStructuralGeneratedDataRules.ValidateStored(
            data.SchemaVersion,
            data.RoofOwnerReference,
            RoofStructuralGeneratedDataRules.FormatRole(data.StructuralRole),
            data.BoundaryEdgeIdA,
            data.BoundaryEdgeIdB);
        if (validated.Data is null)
        {
            throw new ArgumentException(
                "Invalid structural generated metadata: " + validated.Error,
                nameof(data));
        }

        EnsureRegAppRegistered(entity.Database, transaction);
        var canonical = validated.Data;
        return Array.AsReadOnly(new[]
        {
            new TypedValue(DxfRegAppNameCode, RegAppName),
            new TypedValue(DxfInt16Code, checked((short)canonical.SchemaVersion)),
            new TypedValue(DxfAsciiStringCode, canonical.RoofOwnerReference),
            new TypedValue(
                DxfAsciiStringCode,
                RoofStructuralGeneratedDataRules.FormatRole(canonical.StructuralRole)),
            new TypedValue(DxfInt32Code, canonical.BoundaryEdgeIdA),
            new TypedValue(DxfInt32Code, canonical.BoundaryEdgeIdB),
        });
    }

    private static List<TypedValue> ReadForeignXData(
        Entity entity,
        bool removeGenericTimberSection = false)
    {
        var retained = new List<TypedValue>();
        using var xdata = entity.XData;
        if (xdata is null)
        {
            return retained;
        }

        var skipStructuralSection = false;
        foreach (var value in xdata.AsArray())
        {
            if (value.TypeCode == DxfRegAppNameCode)
            {
                var applicationName = Convert.ToString(
                    value.Value,
                    CultureInfo.InvariantCulture);
                skipStructuralSection =
                    string.Equals(
                        applicationName,
                        RegAppName,
                        StringComparison.OrdinalIgnoreCase) ||
                    removeGenericTimberSection &&
                    string.Equals(
                        applicationName,
                        ElementDataStore.RegAppName,
                        StringComparison.OrdinalIgnoreCase);
            }

            if (!skipStructuralSection)
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

internal sealed record RoofStructuralGeneratedStoreReadResult(
    bool Exists,
    RoofStructuralGeneratedData? Data,
    RoofStructuralGeneratedDataError Error)
{
    public static RoofStructuralGeneratedStoreReadResult Missing { get; } =
        new(false, null, RoofStructuralGeneratedDataError.Missing);

    public static RoofStructuralGeneratedStoreReadResult Valid(
        RoofStructuralGeneratedData data) =>
        new(true, data, RoofStructuralGeneratedDataError.None);

    public static RoofStructuralGeneratedStoreReadResult Invalid(
        RoofStructuralGeneratedDataError error) => new(true, null, error);
}
