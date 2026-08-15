using System.Globalization;
using System.Text;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Dedicated portable XData store attached only to generated roof-display children.</summary>
internal static class RoofDisplayStore
{
    internal const string RegAppName = "DECORAIR_ACADKROVY_ROOF_DISPLAY";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;
    private const int DxfOwnerHandleCode = (int)DxfCode.ExtendedDataHandle;
    private const int MaxTextChunkLength = 240;

    public static RoofDisplayStoreReadResult Read(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        try
        {
            using var xdata = entity.GetXDataForApplication(RegAppName);
            if (xdata is null)
            {
                return RoofDisplayStoreReadResult.Missing;
            }

            var values = xdata.AsArray();
            if (values.Length < 2 ||
                values[0].TypeCode != DxfRegAppNameCode ||
                !string.Equals(
                    Convert.ToString(values[0].Value, CultureInfo.InvariantCulture),
                    RegAppName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return RoofDisplayStoreReadResult.Invalid(
                    null,
                    RoofDisplayDataDecodeError.MalformedPayload);
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

                return RoofDisplayStoreReadResult.Invalid(
                    cloneSafeOwnerReference,
                    RoofDisplayDataDecodeError.MalformedPayload);
            }

            var text = payload.ToString();
            _ = RoofDisplayDataCodec.TryReadOwnerReference(
                text,
                out var payloadOwnerReference);
            var effectiveOwnerReference = cloneSafeOwnerReference ?? payloadOwnerReference;
            if (RoofDisplayDataCodec.TryDecode(
                    text,
                    out var data,
                    out var error) && data is not null)
            {
                if (cloneSafeOwnerReference is not null)
                {
                    data = data with { OwnerReference = cloneSafeOwnerReference };
                }

                return RoofDisplayStoreReadResult.Valid(
                    data,
                    ownerReferenceFromCloneHandle: cloneSafeOwnerReference is not null);
            }

            return RoofDisplayStoreReadResult.Invalid(effectiveOwnerReference, error);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofDisplayStoreReadResult.Invalid(
                null,
                RoofDisplayDataDecodeError.MalformedPayload);
        }
    }

    public static void Write(Entity entity, Transaction transaction, RoofDisplayData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        if (!entity.IsWriteEnabled)
        {
            throw new InvalidOperationException("Roof display child must be opened ForWrite.");
        }

        if (!TryNormalizeOwnerReference(data.OwnerReference, out var ownerReference))
        {
            throw new ArgumentException(
                "Roof display owner reference must be a positive hexadecimal handle.",
                nameof(data));
        }

        var payload = RoofDisplayDataCodec.Encode(data);
        EnsureRegAppRegistered(entity.Database, transaction);
        var retained = ReadForeignXData(entity);
        retained.Add(new TypedValue(DxfRegAppNameCode, RegAppName));
        retained.Add(new TypedValue(DxfOwnerHandleCode, ownerReference));
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

        var skipDisplaySection = false;
        foreach (var value in xdata.AsArray())
        {
            if (value.TypeCode == DxfRegAppNameCode)
            {
                skipDisplaySection = string.Equals(
                    Convert.ToString(value.Value, CultureInfo.InvariantCulture),
                    RegAppName,
                    StringComparison.OrdinalIgnoreCase);
            }
            if (!skipDisplaySection)
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

internal sealed record RoofDisplayStoreReadResult(
    bool Exists,
    string? OwnerReference,
    RoofDisplayData? Data,
    RoofDisplayDataDecodeError Error,
    bool OwnerReferenceFromCloneHandle = false)
{
    public static RoofDisplayStoreReadResult Missing { get; } =
        new(false, null, null, RoofDisplayDataDecodeError.None);

    public static RoofDisplayStoreReadResult Valid(
        RoofDisplayData data,
        bool ownerReferenceFromCloneHandle = false) =>
        new(true, data.OwnerReference, data, RoofDisplayDataDecodeError.None, ownerReferenceFromCloneHandle);

    public static RoofDisplayStoreReadResult Invalid(
        string? ownerReference,
        RoofDisplayDataDecodeError error) =>
        new(true, ownerReference, null, error);
}
