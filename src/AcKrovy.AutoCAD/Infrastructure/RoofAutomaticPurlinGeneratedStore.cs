using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Independent schema-1 XData store for one generated automatic Purlin child.</summary>
internal static class RoofAutomaticPurlinGeneratedStore
{
    internal const string RegAppName =
        "DECORAIR_ACADKROVY_ROOF_AUTOMATIC_PURLIN_GENERATED";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;
    private const int DxfInt16Code = (int)DxfCode.ExtendedDataInteger16;
    private const int DxfInt32Code = (int)DxfCode.ExtendedDataInteger32;
    private const int CommonValueCount = 4;
    private const int WallPlateValueCount = 5;
    private const int RidgeValueCount = 6;
    private const int IntermediateValueCount = 8;

    public static RoofAutomaticPurlinGeneratedStoreReadResult Read(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        try
        {
            using var xdata = entity.GetXDataForApplication(RegAppName);
            return xdata is null
                ? RoofAutomaticPurlinGeneratedStoreReadResult.Missing
                : DecodePayload(xdata.AsArray());
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofAutomaticPurlinGeneratedStoreReadResult.Invalid(
                RoofAutomaticPurlinGeneratedDataError.MalformedValueType);
        }
    }

    internal static RoofAutomaticPurlinGeneratedStoreReadResult DecodePayload(
        IReadOnlyList<TypedValue> values)
    {
        if (values.Count < CommonValueCount)
        {
            return RoofAutomaticPurlinGeneratedStoreReadResult.Invalid(
                RoofAutomaticPurlinGeneratedDataError.IncompletePayload);
        }

        if (values[0].TypeCode != DxfRegAppNameCode ||
            values[0].Value is not string applicationName ||
            !string.Equals(applicationName, RegAppName, StringComparison.OrdinalIgnoreCase) ||
            values[1].TypeCode != DxfInt16Code ||
            values[1].Value is not short schemaVersion ||
            values[2].TypeCode != DxfAsciiStringCode ||
            values[2].Value is not string ownerReference ||
            values[3].TypeCode != DxfAsciiStringCode ||
            values[3].Value is not string roleToken)
        {
            return RoofAutomaticPurlinGeneratedStoreReadResult.Invalid(
                RoofAutomaticPurlinGeneratedDataError.MalformedValueType);
        }

        if (string.Equals(
                roleToken,
                RoofAutomaticPurlinGeneratedDataRules.WallPlateToken,
                StringComparison.Ordinal))
        {
            return DecodeWallPlate(values, schemaVersion, ownerReference, roleToken);
        }

        if (string.Equals(
                roleToken,
                RoofAutomaticPurlinGeneratedDataRules.RidgeToken,
                StringComparison.Ordinal))
        {
            return DecodeRidge(values, schemaVersion, ownerReference, roleToken);
        }

        if (string.Equals(
                roleToken,
                RoofAutomaticPurlinGeneratedDataRules.IntermediateToken,
                StringComparison.Ordinal))
        {
            return DecodeIntermediate(values, schemaVersion, ownerReference, roleToken);
        }

        return RoofAutomaticPurlinGeneratedStoreReadResult.Invalid(
            RoofAutomaticPurlinGeneratedDataError.UnsupportedRole);
    }

    internal static IReadOnlyList<TypedValue> EncodePayload(
        RoofAutomaticPurlinGeneratedData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.SchemaVersion != RoofAutomaticPurlinGeneratedDataSchema.CurrentVersion)
        {
            throw new ArgumentException(
                "Invalid automatic-purlin generated metadata: " +
                RoofAutomaticPurlinGeneratedDataError.UnsupportedSchemaVersion,
                nameof(data));
        }

        var validated = RoofAutomaticPurlinGeneratedDataRules.Create(
            data.RoofOwnerReference,
            data.GeneratedKey);
        if (validated.Data is null)
        {
            throw new ArgumentException(
                "Invalid automatic-purlin generated metadata: " + validated.Error,
                nameof(data));
        }

        var canonical = validated.Data;
        var values = new List<TypedValue>
        {
            new(DxfRegAppNameCode, RegAppName),
            new(
                DxfInt16Code,
                checked((short)RoofAutomaticPurlinGeneratedDataSchema.CurrentVersion)),
            new(DxfAsciiStringCode, canonical.RoofOwnerReference),
            new(
                DxfAsciiStringCode,
                RoofAutomaticPurlinGeneratedDataRules.FormatRole(canonical.GeneratorRole)),
        };

        switch (canonical.GeneratedKey)
        {
            case RoofAutomaticPurlinWallPlateKey wallPlate:
                values.Add(new TypedValue(
                    DxfInt32Code,
                    wallPlate.BoundaryEdgeId));
                break;

            case RoofAutomaticPurlinRidgeKey ridge:
                values.Add(new TypedValue(
                    DxfInt32Code,
                    ridge.StructuralKey.BoundaryEdgeIdA));
                values.Add(new TypedValue(
                    DxfInt32Code,
                    ridge.StructuralKey.BoundaryEdgeIdB));
                break;

            case RoofAutomaticPurlinIntermediateKey intermediate:
                RoofAutomaticPurlinBoundaryKeyRules.TryFormat(
                    intermediate.EndpointBoundaryKeyA,
                    out var endpointA,
                    out _);
                RoofAutomaticPurlinBoundaryKeyRules.TryFormat(
                    intermediate.EndpointBoundaryKeyB,
                    out var endpointB,
                    out _);
                values.Add(new TypedValue(DxfAsciiStringCode, intermediate.LayoutItemId));
                values.Add(new TypedValue(
                    DxfInt32Code,
                    intermediate.SourceFaceBoundaryEdgeId));
                values.Add(new TypedValue(DxfAsciiStringCode, endpointA));
                values.Add(new TypedValue(DxfAsciiStringCode, endpointB));
                break;

            default:
                throw new ArgumentException(
                    "Invalid automatic-purlin generated metadata role.",
                    nameof(data));
        }

        return values.AsReadOnly();
    }

    public static void Write(
        Entity entity,
        Transaction transaction,
        RoofAutomaticPurlinGeneratedData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        if (!entity.IsWriteEnabled)
        {
            throw new InvalidOperationException(
                "Automatic-purlin generated child must be opened ForWrite.");
        }

        var retained = ReadForeignXData(entity);
        retained.AddRange(BuildSection(entity, transaction, data));
        using var buffer = new ResultBuffer(retained.ToArray());
        entity.XData = buffer;
    }

    /// <summary>
    /// Writes generic Timber and AutomaticPurlinGenerated sections in one XData
    /// assignment. For newly appended entities this is called before transaction
    /// registration so no partially classified child is observable.
    /// </summary>
    public static void WriteAtomic(
        Entity entity,
        Transaction transaction,
        TimberElementData timberData,
        RoofAutomaticPurlinGeneratedData generatedData)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(timberData);
        ArgumentNullException.ThrowIfNull(generatedData);
        if (!entity.IsWriteEnabled)
        {
            throw new InvalidOperationException(
                "Automatic-purlin generated child must be opened ForWrite.");
        }

        var retained = ReadForeignXData(entity, removeGenericTimberSection: true);
        retained.AddRange(ElementDataStore.BuildSection(entity, transaction, timberData));
        retained.AddRange(BuildSection(entity, transaction, generatedData));
        using var buffer = new ResultBuffer(retained.ToArray());
        entity.XData = buffer;
    }

    public static IReadOnlyList<TypedValue> BuildSection(
        Entity entity,
        Transaction transaction,
        RoofAutomaticPurlinGeneratedData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        var section = EncodePayload(data);
        EnsureRegAppRegistered(entity.Database, transaction);
        return section;
    }

    public static RoofAutomaticPurlinGeneratedOwnerScanResult ScanForOwner(
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
            return new RoofAutomaticPurlinGeneratedOwnerScanResult(
                Array.Empty<ObjectId>(),
                Array.Empty<ObjectId>());
        }

        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        var matches = new List<ObjectId>();
        var malformed = new List<ObjectId>();
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                entity.IsErased)
            {
                continue;
            }

            var stored = Read(entity);
            if (!stored.Exists)
            {
                continue;
            }

            if (stored.Data is null)
            {
                if (HasOwnerReference(entity, normalizedOwner))
                {
                    malformed.Add(id);
                }

                continue;
            }

            if (string.Equals(
                    stored.Data.RoofOwnerReference,
                    normalizedOwner,
                    StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(id);
            }
        }

        return new RoofAutomaticPurlinGeneratedOwnerScanResult(
            matches.AsReadOnly(),
            malformed.AsReadOnly());
    }

    public static IReadOnlyList<ObjectId> FindByOwner(
        Database database,
        Transaction transaction,
        string ownerReference) =>
        ScanForOwner(database, transaction, ownerReference).MatchingIds;

    private static bool HasOwnerReference(Entity entity, string normalizedOwner)
    {
        try
        {
            using var xdata = entity.GetXDataForApplication(RegAppName);
            if (xdata is null)
            {
                return false;
            }

            var values = xdata.AsArray();
            return values.Length > 2 &&
                   values[2].TypeCode == DxfAsciiStringCode &&
                   values[2].Value is string ownerReference &&
                   RoofStructuralGeneratedDataRules.TryNormalizeOwnerReference(
                       ownerReference,
                       out var normalizedStoredOwner) &&
                   string.Equals(
                       normalizedStoredOwner,
                       normalizedOwner,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static RoofAutomaticPurlinGeneratedStoreReadResult DecodeRidge(
        IReadOnlyList<TypedValue> values,
        short schemaVersion,
        string ownerReference,
        string roleToken)
    {
        if (values.Count < RidgeValueCount)
        {
            return RoofAutomaticPurlinGeneratedStoreReadResult.Invalid(
                RoofAutomaticPurlinGeneratedDataError.IncompletePayload);
        }

        if (values.Count > RidgeValueCount)
        {
            return RoofAutomaticPurlinGeneratedStoreReadResult.Invalid(
                RoofAutomaticPurlinGeneratedDataError.UnexpectedTrailingValue);
        }

        if (values[4].TypeCode != DxfInt32Code ||
            values[4].Value is not int boundaryEdgeIdA ||
            values[5].TypeCode != DxfInt32Code ||
            values[5].Value is not int boundaryEdgeIdB)
        {
            return RoofAutomaticPurlinGeneratedStoreReadResult.Invalid(
                RoofAutomaticPurlinGeneratedDataError.MalformedValueType);
        }

        var validated = RoofAutomaticPurlinGeneratedDataRules.ValidateRidgeStored(
            schemaVersion,
            ownerReference,
            roleToken,
            boundaryEdgeIdA,
            boundaryEdgeIdB);
        return FromValidation(validated);
    }

    private static RoofAutomaticPurlinGeneratedStoreReadResult DecodeWallPlate(
        IReadOnlyList<TypedValue> values,
        short schemaVersion,
        string ownerReference,
        string roleToken)
    {
        if (values.Count < WallPlateValueCount)
        {
            return RoofAutomaticPurlinGeneratedStoreReadResult.Invalid(
                RoofAutomaticPurlinGeneratedDataError.IncompletePayload);
        }

        if (values.Count > WallPlateValueCount)
        {
            return RoofAutomaticPurlinGeneratedStoreReadResult.Invalid(
                RoofAutomaticPurlinGeneratedDataError.UnexpectedTrailingValue);
        }

        if (values[4].TypeCode != DxfInt32Code ||
            values[4].Value is not int boundaryEdgeId)
        {
            return RoofAutomaticPurlinGeneratedStoreReadResult.Invalid(
                RoofAutomaticPurlinGeneratedDataError.MalformedValueType);
        }

        var validated = RoofAutomaticPurlinGeneratedDataRules.ValidateWallPlateStored(
            schemaVersion,
            ownerReference,
            roleToken,
            boundaryEdgeId);
        return FromValidation(validated);
    }

    private static RoofAutomaticPurlinGeneratedStoreReadResult DecodeIntermediate(
        IReadOnlyList<TypedValue> values,
        short schemaVersion,
        string ownerReference,
        string roleToken)
    {
        if (values.Count < IntermediateValueCount)
        {
            return RoofAutomaticPurlinGeneratedStoreReadResult.Invalid(
                RoofAutomaticPurlinGeneratedDataError.IncompletePayload);
        }

        if (values.Count > IntermediateValueCount)
        {
            return RoofAutomaticPurlinGeneratedStoreReadResult.Invalid(
                RoofAutomaticPurlinGeneratedDataError.UnexpectedTrailingValue);
        }

        if (values[4].TypeCode != DxfAsciiStringCode ||
            values[4].Value is not string layoutItemId ||
            values[5].TypeCode != DxfInt32Code ||
            values[5].Value is not int sourceFaceBoundaryEdgeId ||
            values[6].TypeCode != DxfAsciiStringCode ||
            values[6].Value is not string endpointBoundaryKeyA ||
            values[7].TypeCode != DxfAsciiStringCode ||
            values[7].Value is not string endpointBoundaryKeyB)
        {
            return RoofAutomaticPurlinGeneratedStoreReadResult.Invalid(
                RoofAutomaticPurlinGeneratedDataError.MalformedValueType);
        }

        var validated = RoofAutomaticPurlinGeneratedDataRules.ValidateIntermediateStored(
            schemaVersion,
            ownerReference,
            roleToken,
            layoutItemId,
            sourceFaceBoundaryEdgeId,
            endpointBoundaryKeyA,
            endpointBoundaryKeyB);
        return FromValidation(validated);
    }

    private static RoofAutomaticPurlinGeneratedStoreReadResult FromValidation(
        RoofAutomaticPurlinGeneratedDataValidationResult validated) =>
        validated.Data is not null
            ? RoofAutomaticPurlinGeneratedStoreReadResult.Valid(validated.Data)
            : RoofAutomaticPurlinGeneratedStoreReadResult.Invalid(validated.Error);

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

        var skipOwnSection = false;
        foreach (var value in xdata.AsArray())
        {
            if (value.TypeCode == DxfRegAppNameCode)
            {
                var applicationName = Convert.ToString(
                    value.Value,
                    CultureInfo.InvariantCulture);
                skipOwnSection =
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

internal sealed record RoofAutomaticPurlinGeneratedOwnerScanResult(
    IReadOnlyList<ObjectId> MatchingIds,
    IReadOnlyList<ObjectId> MalformedIds);

internal sealed record RoofAutomaticPurlinGeneratedStoreReadResult(
    bool Exists,
    RoofAutomaticPurlinGeneratedData? Data,
    RoofAutomaticPurlinGeneratedDataError Error)
{
    public static RoofAutomaticPurlinGeneratedStoreReadResult Missing { get; } =
        new(false, null, RoofAutomaticPurlinGeneratedDataError.Missing);

    public static RoofAutomaticPurlinGeneratedStoreReadResult Valid(
        RoofAutomaticPurlinGeneratedData data) =>
        new(true, data, RoofAutomaticPurlinGeneratedDataError.None);

    public static RoofAutomaticPurlinGeneratedStoreReadResult Invalid(
        RoofAutomaticPurlinGeneratedDataError error) => new(true, null, error);
}
