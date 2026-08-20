using System.Globalization;
using System.Text;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// XData for Timber lines manually attached to a roof (COPY/split children).
/// Separate from generated-timber recipe ownership.
/// </summary>
internal static class RoofAttachedManualTimberStore
{
    internal const string RegAppName = "DECORAIR_ACADKROVY_ROOF_ATTACHED_MANUAL";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;
    private const int DxfOwnerHandleCode = (int)DxfCode.ExtendedDataHandle;
    private const int MaxTextChunkLength = 240;

    public static RoofAttachedManualTimberReadResult Read(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        try
        {
            using var xdata = entity.GetXDataForApplication(RegAppName);
            if (xdata is null)
            {
                return RoofAttachedManualTimberReadResult.Missing;
            }

            var values = xdata.AsArray();
            if (values.Length < 2 ||
                values[0].TypeCode != DxfRegAppNameCode ||
                !string.Equals(
                    Convert.ToString(values[0].Value, CultureInfo.InvariantCulture),
                    RegAppName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return RoofAttachedManualTimberReadResult.Invalid;
            }

            var payload = new StringBuilder();
            string? cloneSafeOwnerReference = null;
            for (var index = 1; index < values.Length; index++)
            {
                if (values[index].TypeCode == DxfAsciiStringCode)
                {
                    payload.Append(Convert.ToString(values[index].Value, CultureInfo.InvariantCulture));
                    continue;
                }

                if (values[index].TypeCode == DxfOwnerHandleCode &&
                    cloneSafeOwnerReference is null &&
                    TryNormalizeOwnerReference(
                        Convert.ToString(values[index].Value, CultureInfo.InvariantCulture),
                        out var remapped))
                {
                    cloneSafeOwnerReference = remapped;
                }
            }

            if (!RoofAttachedManualTimberDataCodec.TryDecode(payload.ToString(), out var data) ||
                data is null)
            {
                return RoofAttachedManualTimberReadResult.Invalid;
            }

            if (cloneSafeOwnerReference is not null)
            {
                data = data with { RoofOwnerReference = cloneSafeOwnerReference };
            }

            return RoofAttachedManualTimberReadResult.Valid(data);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofAttachedManualTimberReadResult.Invalid;
        }
    }

    public static void Write(
        Entity entity,
        Transaction transaction,
        RoofAttachedManualTimberData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        if (!entity.IsWriteEnabled)
        {
            throw new InvalidOperationException("Attached-manual timber must be opened ForWrite.");
        }

        if (!TryNormalizeOwnerReference(data.RoofOwnerReference, out var ownerReference))
        {
            throw new ArgumentException("Attached-manual owner must be a positive hex handle.", nameof(data));
        }

        data = data with
        {
            RoofOwnerReference = ownerReference,
            Role = RoofTimberChildRole.AttachedManual,
        };
        EnsureRegAppRegistered(entity.Database, transaction);
        var payload = RoofAttachedManualTimberDataCodec.Encode(data);
        var retained = ReadForeignXData(entity);
        retained.Add(new TypedValue(DxfRegAppNameCode, RegAppName));
        retained.Add(new TypedValue(DxfOwnerHandleCode, ownerReference));
        retained.AddRange(SplitIntoChunks(payload)
            .Select(chunk => new TypedValue(DxfAsciiStringCode, chunk)));
        using var buffer = new ResultBuffer(retained.ToArray());
        entity.XData = buffer;
    }

    public static bool TryClear(Entity entity, Transaction transaction, out string failureReason) =>
        TryClearAttachedOnly(entity, transaction, out failureReason);

    private static bool TryClearAttachedOnly(Entity entity, Transaction transaction, out string failureReason)
    {
        failureReason = string.Empty;
        if (!entity.IsWriteEnabled)
        {
            failureReason = "entity-not-write-enabled";
            return false;
        }

        try
        {
            using var erase = new ResultBuffer(new TypedValue(DxfRegAppNameCode, RegAppName));
            entity.XData = erase;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            failureReason = "attached-xdata-assign-failed:" + ex.ErrorStatus;
            return false;
        }

        using (var residual = entity.GetXDataForApplication(RegAppName))
        {
            if (residual is not null)
            {
                failureReason = "attached-xdata-remains";
                return false;
            }
        }

        return true;
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

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
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

    private static bool TryNormalizeOwnerReference(string? value, out string normalized)
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

internal sealed record RoofAttachedManualTimberReadResult(
    bool Exists,
    RoofAttachedManualTimberData? Data)
{
    public static RoofAttachedManualTimberReadResult Missing { get; } = new(false, null);

    public static RoofAttachedManualTimberReadResult Invalid { get; } = new(true, null);

    public static RoofAttachedManualTimberReadResult Valid(RoofAttachedManualTimberData data) =>
        new(true, data);
}
