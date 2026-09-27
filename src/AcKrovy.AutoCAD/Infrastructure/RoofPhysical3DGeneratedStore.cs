using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Independent typed XData store for generated physical-3D roof entities.</summary>
internal static class RoofPhysical3DGeneratedStore
{
    internal const string RegAppName = "DECORAIR_ACADKROVY_ROOF_PHYSICAL_3D";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;
    private const int DxfInt16Code = (int)DxfCode.ExtendedDataInteger16;
    private const int ValueCount = 6;

    public static RoofPhysical3DGeneratedStoreReadResult Read(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        try
        {
            using var xdata = entity.GetXDataForApplication(RegAppName);
            return xdata is null
                ? RoofPhysical3DGeneratedStoreReadResult.Missing
                : DecodePayload(xdata.AsArray());
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofPhysical3DGeneratedStoreReadResult.Invalid(
                RoofPhysical3DGeneratedDataError.MalformedValueType);
        }
    }

    internal static RoofPhysical3DGeneratedStoreReadResult DecodePayload(
        IReadOnlyList<TypedValue> values)
    {
        if (values.Count < ValueCount)
        {
            return RoofPhysical3DGeneratedStoreReadResult.Invalid(
                RoofPhysical3DGeneratedDataError.IncompletePayload);
        }

        if (values.Count > ValueCount)
        {
            return RoofPhysical3DGeneratedStoreReadResult.Invalid(
                RoofPhysical3DGeneratedDataError.UnexpectedTrailingValue);
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
            values[4].TypeCode != DxfAsciiStringCode ||
            values[4].Value is not string structuralId ||
            values[5].TypeCode != DxfAsciiStringCode ||
            values[5].Value is not string generationSignature)
        {
            return RoofPhysical3DGeneratedStoreReadResult.Invalid(
                RoofPhysical3DGeneratedDataError.MalformedValueType);
        }

        var validated = RoofPhysical3DGeneratedDataRules.ValidateStored(
            schemaVersion,
            ownerReference,
            roleToken,
            structuralId,
            generationSignature);
        return validated.Data is null
            ? RoofPhysical3DGeneratedStoreReadResult.Invalid(validated.Error)
            : RoofPhysical3DGeneratedStoreReadResult.Valid(validated.Data);
    }

    public static void Write(
        Entity entity,
        Transaction transaction,
        RoofPhysical3DGeneratedData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        if (!entity.IsWriteEnabled)
        {
            throw new InvalidOperationException(
                "Physical 3D generated entity must be opened ForWrite.");
        }

        var retained = ReadForeignXData(entity);
        retained.AddRange(BuildSection(entity, transaction, data));
        using var buffer = new ResultBuffer(retained.ToArray());
        entity.XData = buffer;
    }

    public static IReadOnlyList<ObjectId> FindByOwner(
        Database database,
        Transaction transaction,
        string ownerReference)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        if (string.IsNullOrWhiteSpace(ownerReference))
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
                    ownerReference,
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
        RoofPhysical3DGeneratedData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        var validated = RoofPhysical3DGeneratedDataRules.ValidateStored(
            data.SchemaVersion,
            data.RoofOwnerReference,
            data.Role.ToString(),
            data.StructuralId,
            data.GenerationSignature);
        if (validated.Data is null)
        {
            throw new ArgumentException(
                "Invalid physical 3D generated metadata: " + validated.Error,
                nameof(data));
        }

        EnsureRegAppRegistered(entity.Database, transaction);
        var canonical = validated.Data;
        return Array.AsReadOnly(new[]
        {
            new TypedValue(DxfRegAppNameCode, RegAppName),
            new TypedValue(DxfInt16Code, checked((short)canonical.SchemaVersion)),
            new TypedValue(DxfAsciiStringCode, canonical.RoofOwnerReference),
            new TypedValue(DxfAsciiStringCode, canonical.Role.ToString()),
            new TypedValue(DxfAsciiStringCode, canonical.StructuralId),
            new TypedValue(DxfAsciiStringCode, canonical.GenerationSignature),
        });
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

    private static void EnsureRegAppRegistered(Database database, Transaction transaction)
    {
        var table = (RegAppTable)transaction.GetObject(database.RegAppTableId, OpenMode.ForRead);
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

internal sealed record RoofPhysical3DGeneratedStoreReadResult(
    bool Exists,
    RoofPhysical3DGeneratedData? Data,
    RoofPhysical3DGeneratedDataError Error)
{
    public static RoofPhysical3DGeneratedStoreReadResult Missing { get; } =
        new(false, null, RoofPhysical3DGeneratedDataError.Missing);

    public static RoofPhysical3DGeneratedStoreReadResult Valid(RoofPhysical3DGeneratedData data) =>
        new(true, data, RoofPhysical3DGeneratedDataError.None);

    public static RoofPhysical3DGeneratedStoreReadResult Invalid(
        RoofPhysical3DGeneratedDataError error) =>
        new(true, null, error);
}
