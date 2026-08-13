using System.Globalization;
using System.Text;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Dedicated portable XData store owned by the roof-footprint entity.</summary>
internal static class RoofDefinitionStore
{
    internal const string RegAppName = "DECORAIR_ACADKROVY_ROOF";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;
    private const int MaxTextChunkLength = 240;

    public static RoofDefinitionStoreReadResult Read(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        try
        {
            using var xdata = entity.GetXDataForApplication(RegAppName);
            if (xdata is null)
            {
                return RoofDefinitionStoreReadResult.Missing;
            }

            var values = xdata.AsArray();
            if (values.Length < 2 ||
                values[0].TypeCode != DxfRegAppNameCode ||
                !string.Equals(
                    Convert.ToString(values[0].Value, CultureInfo.InvariantCulture),
                    RegAppName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return RoofDefinitionStoreReadResult.Invalid(
                    RoofDefinitionDataDecodeError.MalformedPayload);
            }

            var payload = new StringBuilder();
            for (var index = 1; index < values.Length; index++)
            {
                if (values[index].TypeCode != DxfAsciiStringCode)
                {
                    return RoofDefinitionStoreReadResult.Invalid(
                        RoofDefinitionDataDecodeError.MalformedPayload);
                }

                payload.Append(Convert.ToString(
                    values[index].Value,
                    CultureInfo.InvariantCulture));
            }

            return RoofDefinitionDataCodec.TryDecode(
                       payload.ToString(),
                       out var data,
                       out var error) && data is not null
                ? RoofDefinitionStoreReadResult.Valid(data)
                : RoofDefinitionStoreReadResult.Invalid(error);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofDefinitionStoreReadResult.Invalid(
                RoofDefinitionDataDecodeError.MalformedPayload);
        }
    }

    public static void Write(
        Entity entity,
        Transaction transaction,
        RoofDefinitionData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        if (!entity.IsWriteEnabled)
        {
            throw new InvalidOperationException("Roof definition owner must be opened ForWrite.");
        }

        var payload = RoofDefinitionDataCodec.Encode(data);
        EnsureRegAppRegistered(entity.Database, transaction);
        var retained = ReadForeignXData(entity);
        retained.Add(new TypedValue(DxfRegAppNameCode, RegAppName));
        retained.AddRange(SplitIntoChunks(payload)
            .Select(chunk => new TypedValue(DxfAsciiStringCode, chunk)));

        using var buffer = new ResultBuffer(retained.ToArray());
        entity.XData = buffer;
    }

    private static List<TypedValue> ReadForeignXData(Entity entity)
    {
        var retained = new List<TypedValue>();
        using var xdata = entity.XData;
        if (xdata is null)
        {
            return retained;
        }

        var skipRoofSection = false;
        foreach (var value in xdata.AsArray())
        {
            if (value.TypeCode == DxfRegAppNameCode)
            {
                skipRoofSection = string.Equals(
                    Convert.ToString(value.Value, CultureInfo.InvariantCulture),
                    RegAppName,
                    StringComparison.OrdinalIgnoreCase);
            }

            if (!skipRoofSection)
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

    private static IEnumerable<string> SplitIntoChunks(string value)
    {
        for (var index = 0; index < value.Length; index += MaxTextChunkLength)
        {
            yield return value.Substring(
                index,
                Math.Min(MaxTextChunkLength, value.Length - index));
        }
    }
}

internal sealed record RoofDefinitionStoreReadResult(
    bool Exists,
    RoofDefinitionData? Data,
    RoofDefinitionDataDecodeError Error)
{
    public static RoofDefinitionStoreReadResult Missing { get; } =
        new(false, null, RoofDefinitionDataDecodeError.None);

    public static RoofDefinitionStoreReadResult Valid(RoofDefinitionData data) =>
        new(true, data, RoofDefinitionDataDecodeError.None);

    public static RoofDefinitionStoreReadResult Invalid(
        RoofDefinitionDataDecodeError error) =>
        new(true, null, error);
}
