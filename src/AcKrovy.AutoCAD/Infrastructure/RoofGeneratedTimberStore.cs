using System.Globalization;
using System.Text;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Dedicated secondary XData ownership store for roof-generated timber sources.
/// Owner association uses AutoCAD-remappable XData handle (DXF 1005) as the
/// clone-safe authority; the ASCII payload owner remains a diagnostic fallback.
/// </summary>
internal static class RoofGeneratedTimberStore
{
    internal const string RegAppName = "DECORAIR_ACADKROVY_ROOF_TIMBER";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;
    private const int DxfOwnerHandleCode = (int)DxfCode.ExtendedDataHandle;
    private const int MaxTextChunkLength = 240;

    public static RoofGeneratedTimberStoreReadResult Read(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        try
        {
            using var xdata = entity.GetXDataForApplication(RegAppName);
            if (xdata is null)
            {
                return RoofGeneratedTimberStoreReadResult.Missing;
            }

            var values = xdata.AsArray();
            if (values.Length < 2 ||
                values[0].TypeCode != DxfRegAppNameCode ||
                !string.Equals(
                    Convert.ToString(values[0].Value, CultureInfo.InvariantCulture),
                    RegAppName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return RoofGeneratedTimberStoreReadResult.Invalid(
                    RoofGeneratedTimberDataDecodeError.MalformedPayload);
            }

            var payload = new StringBuilder();
            string? cloneSafeOwnerReference = null;
            for (var index = 1; index < values.Length; index++)
            {
                if (values[index].TypeCode == DxfAsciiStringCode)
                {
                    payload.Append(Convert.ToString(
                        values[index].Value,
                        CultureInfo.InvariantCulture));
                    continue;
                }

                if (values[index].TypeCode == DxfOwnerHandleCode)
                {
                    if (cloneSafeOwnerReference is null &&
                        TryNormalizeOwnerReference(
                            Convert.ToString(values[index].Value, CultureInfo.InvariantCulture),
                            out var remappedOwnerReference))
                    {
                        cloneSafeOwnerReference = remappedOwnerReference;
                    }

                    continue;
                }

                return RoofGeneratedTimberStoreReadResult.Invalid(
                    RoofGeneratedTimberDataDecodeError.MalformedPayload);
            }

            var text = payload.ToString();
            if (RoofGeneratedTimberDataCodec.TryDecode(
                    text,
                    out var data,
                    out var error) && data is not null)
            {
                if (cloneSafeOwnerReference is not null)
                {
                    data = data with { RoofOwnerReference = cloneSafeOwnerReference };
                }

                return RoofGeneratedTimberStoreReadResult.Valid(
                    data,
                    ownerReferenceFromCloneHandle: cloneSafeOwnerReference is not null);
            }

            return RoofGeneratedTimberStoreReadResult.Invalid(error);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofGeneratedTimberStoreReadResult.Invalid(
                RoofGeneratedTimberDataDecodeError.MalformedPayload);
        }
    }

    public static void Write(
        Entity entity,
        Transaction transaction,
        RoofGeneratedTimberData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        if (!entity.IsWriteEnabled)
        {
            throw new InvalidOperationException(
                "Roof-generated timber source must be opened ForWrite.");
        }

        if (!TryNormalizeOwnerReference(data.RoofOwnerReference, out var ownerReference))
        {
            throw new ArgumentException(
                "Roof-generated timber owner reference must be a positive hexadecimal handle.",
                nameof(data));
        }

        // Persist the normalized handle in both the remappable soft pointer and the
        // ASCII payload so legacy readers and clone-safe reads stay aligned on write.
        data = data with { RoofOwnerReference = ownerReference };
        var payload = RoofGeneratedTimberDataCodec.Encode(data);
        EnsureRegAppRegistered(entity.Database, transaction);
        var retained = ReadForeignXData(entity);
        retained.Add(new TypedValue(DxfRegAppNameCode, RegAppName));
        retained.Add(new TypedValue(DxfOwnerHandleCode, ownerReference));
        retained.AddRange(SplitIntoChunks(payload)
            .Select(chunk => new TypedValue(DxfAsciiStringCode, chunk)));
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
        if (!TryNormalizeOwnerReference(ownerReference, out var normalizedOwner))
        {
            return [];
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

    private static List<TypedValue> ReadForeignXData(Entity entity)
    {
        var retained = new List<TypedValue>();
        using var xdata = entity.XData;
        if (xdata is null)
        {
            return retained;
        }

        var skipSection = false;
        foreach (var value in xdata.AsArray())
        {
            if (value.TypeCode == DxfRegAppNameCode)
            {
                skipSection = string.Equals(
                    Convert.ToString(value.Value, CultureInfo.InvariantCulture),
                    RegAppName,
                    StringComparison.OrdinalIgnoreCase);
            }
            if (!skipSection)
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

    private static IEnumerable<string> SplitIntoChunks(string value)
    {
        for (var index = 0; index < value.Length; index += MaxTextChunkLength)
        {
            yield return value.Substring(index, Math.Min(MaxTextChunkLength, value.Length - index));
        }
    }

    private static bool TryNormalizeOwnerReference(
        string? value,
        out string normalized)
    {
        normalized = string.Empty;
        if (!long.TryParse(
                value,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var handleValue) || handleValue <= 0)
        {
            return false;
        }

        normalized = handleValue.ToString("X", CultureInfo.InvariantCulture);
        return true;
    }
}

internal sealed record RoofGeneratedTimberStoreReadResult(
    bool Exists,
    RoofGeneratedTimberData? Data,
    RoofGeneratedTimberDataDecodeError Error,
    bool OwnerReferenceFromCloneHandle = false)
{
    public static RoofGeneratedTimberStoreReadResult Missing { get; } =
        new(false, null, RoofGeneratedTimberDataDecodeError.None);

    public static RoofGeneratedTimberStoreReadResult Valid(
        RoofGeneratedTimberData data,
        bool ownerReferenceFromCloneHandle = false) =>
        new(true, data, RoofGeneratedTimberDataDecodeError.None, ownerReferenceFromCloneHandle);

    public static RoofGeneratedTimberStoreReadResult Invalid(
        RoofGeneratedTimberDataDecodeError error) =>
        new(true, null, error);
}
