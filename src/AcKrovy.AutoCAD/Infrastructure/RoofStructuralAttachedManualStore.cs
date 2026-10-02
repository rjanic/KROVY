using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Typed XData store for Structural AttachedManual Hip/Valley children.
/// Parallel to ordinary AttachedManual — does not overload Face/Station schema.
/// </summary>
internal static class RoofStructuralAttachedManualStore
{
    internal const string RegAppName = "DECORAIR_ACADKROVY_ROOF_STRUCTURAL_ATTACHED_MANUAL";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;
    private const int DxfInt16Code = (int)DxfCode.ExtendedDataInteger16;
    private const int DxfInt32Code = (int)DxfCode.ExtendedDataInteger32;
    private const int DxfRealCode = (int)DxfCode.ExtendedDataReal;

    public static RoofStructuralAttachedManualStoreReadResult Read(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        try
        {
            // Prefer the resident XData buffer so same-transaction writers
            // (Generated→Manual conversion) are visible to FindByOwner /
            // materialization before Commit flushes GetXDataForApplication.
            if (entity.XData is { } residentXData)
            {
                var resident = ExtractApplicationSection(residentXData, RegAppName);
                return resident is null
                    ? RoofStructuralAttachedManualStoreReadResult.Missing
                    : DecodePayload(resident);
            }

            using var xdata = entity.GetXDataForApplication(RegAppName);
            return xdata is null
                ? RoofStructuralAttachedManualStoreReadResult.Missing
                : DecodePayload(xdata.AsArray());
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofStructuralAttachedManualStoreReadResult.Invalid(
                RoofStructuralAttachedManualDataError.MalformedValueType);
        }
    }

    private static TypedValue[]? ExtractApplicationSection(ResultBuffer? xdata, string regAppName)
    {
        if (xdata is null) return null;
        var values = xdata.AsArray();
        var start = -1;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i].TypeCode != DxfRegAppNameCode) continue;
            var name = Convert.ToString(values[i].Value, CultureInfo.InvariantCulture);
            if (start >= 0)
                return values[start..i];
            if (string.Equals(name, regAppName, StringComparison.OrdinalIgnoreCase))
                start = i;
        }
        return start >= 0 ? values[start..] : null;
    }

    internal static RoofStructuralAttachedManualStoreReadResult DecodePayload(
        IReadOnlyList<TypedValue> values)
    {
        // schema, owner, identity, role, a, b, creation, width, heightMode, explicitHeight
        if (values.Count is not (10 or 23))
            return RoofStructuralAttachedManualStoreReadResult.Invalid(
                values.Count < 10
                    ? RoofStructuralAttachedManualDataError.IncompletePayload
                    : RoofStructuralAttachedManualDataError.UnexpectedTrailingValue);
        if (values[0].TypeCode != DxfRegAppNameCode ||
            values[0].Value is not string app ||
            !string.Equals(app, RegAppName, StringComparison.OrdinalIgnoreCase) ||
            values[1].TypeCode != DxfInt16Code || values[1].Value is not short schema ||
            values[2].TypeCode != DxfAsciiStringCode || values[2].Value is not string owner ||
            values[3].TypeCode != DxfAsciiStringCode || values[3].Value is not string identity ||
            values[4].TypeCode != DxfAsciiStringCode || values[4].Value is not string role ||
            values[5].TypeCode != DxfInt32Code || values[5].Value is not int edgeA ||
            values[6].TypeCode != DxfInt32Code || values[6].Value is not int edgeB ||
            values[7].TypeCode != DxfAsciiStringCode || values[7].Value is not string creation ||
            values[8].TypeCode != DxfRealCode || values[8].Value is not double width ||
            values[9].TypeCode != DxfAsciiStringCode || values[9].Value is not string heightMode)
        {
            return RoofStructuralAttachedManualStoreReadResult.Invalid(
                RoofStructuralAttachedManualDataError.MalformedValueType);
        }

        double? explicitHeight = null;
        // Explicit height encoded in heightMode token as "Explicit:123.4" when needed,
        // keeping fixed payload length for schema 1.
        var modeToken = heightMode;
        var colon = heightMode.IndexOf(':');
        if (colon > 0)
        {
            modeToken = heightMode[..colon];
            if (!double.TryParse(heightMode[(colon + 1)..], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var parsed))
                return RoofStructuralAttachedManualStoreReadResult.Invalid(
                    RoofStructuralAttachedManualDataError.MalformedValueType);
            explicitHeight = parsed;
        }

        RoofStructuralManualPlacement? placement = null;
        if (values.Count == 23)
        {
            if (!TryReadPlacement(values, out placement))
                return RoofStructuralAttachedManualStoreReadResult.Invalid(
                    RoofStructuralAttachedManualDataError.MalformedValueType);
        }

        var validated = RoofStructuralAttachedManualDataRules.ValidateStored(
            schema, owner, identity, role, edgeA, edgeB, creation, width, modeToken, explicitHeight, placement);
        return validated.Data is not null
            ? RoofStructuralAttachedManualStoreReadResult.Valid(validated.Data)
            : RoofStructuralAttachedManualStoreReadResult.Invalid(validated.Error);
    }

    public static void Write(Entity entity, Transaction transaction, RoofStructuralAttachedManualData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        if (!entity.IsWriteEnabled)
            throw new InvalidOperationException("Structural AttachedManual must be opened ForWrite.");
        var retained = ReadForeignXData(entity);
        retained.AddRange(BuildSection(entity, transaction, data));
        using var buffer = new ResultBuffer(retained.ToArray());
        entity.XData = buffer;
    }

    public static void Clear(Entity entity, Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!entity.IsWriteEnabled)
            throw new InvalidOperationException("Structural AttachedManual must be opened ForWrite.");
        var retained = ReadForeignXData(entity);
        using var buffer = retained.Count == 0 ? null : new ResultBuffer(retained.ToArray());
        entity.XData = buffer;
    }

    public static void ClearGeneratedIdentity(Entity entity, Transaction transaction)
    {
        // Drop StructuralGenerated section so the clone no longer owns a LogicalKey.
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!entity.IsWriteEnabled)
            throw new InvalidOperationException("Entity must be opened ForWrite.");
        WriteWithoutGenerated(entity, transaction, manualData: null);
    }

    /// <summary>
    /// Atomically drops StructuralGenerated and writes ManualStructural metadata.
    /// AutoCAD XData assignment merges by RegApp: omitted applications are NOT removed,
    /// so Generated must be cleared with a RegApp-only erase sentinel first
    /// (same contract as <see cref="RoofGeneratedTimberStore.TryClear"/>).
    /// </summary>
    public static void WriteReplacingGenerated(
        Entity entity,
        Transaction transaction,
        RoofStructuralAttachedManualData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        if (!entity.IsWriteEnabled)
            throw new InvalidOperationException("Structural AttachedManual must be opened ForWrite.");
        WriteWithoutGenerated(entity, transaction, data);
    }

    private static void WriteWithoutGenerated(
        Entity entity,
        Transaction transaction,
        RoofStructuralAttachedManualData? manualData)
    {
#if DEBUG
        var generatedBefore = DescribeApplicationPresence(
            entity, RoofStructuralGeneratedStore.RegAppName);
        var manualBefore = DescribeApplicationPresence(entity, RegAppName);
#endif
        // Merge-safe erase: RegApp-only ResultBuffer removes that application section.
        // Filtering Generated out of a full rewrite and assigning the remainder does NOT
        // clear Generated — AutoCAD leaves omitted RegApps intact.
        if (HasApplicationSection(entity, RoofStructuralGeneratedStore.RegAppName))
        {
            using var eraseGenerated = new ResultBuffer(
                new TypedValue(DxfRegAppNameCode, RoofStructuralGeneratedStore.RegAppName));
            entity.XData = eraseGenerated;
        }

        if (manualData is not null && HasApplicationSection(entity, RegAppName))
        {
            using var eraseManual = new ResultBuffer(
                new TypedValue(DxfRegAppNameCode, RegAppName));
            entity.XData = eraseManual;
        }
#if DEBUG
        var generatedAfterClear = DescribeApplicationPresence(
            entity, RoofStructuralGeneratedStore.RegAppName);
#endif

        if (manualData is not null)
        {
            // Merge writes only the Manual section; foreign RegApps stay intact.
            var section = BuildSection(entity, transaction, manualData);
            using var buffer = new ResultBuffer(section.ToArray());
            entity.XData = buffer;
        }
#if DEBUG
        var manualAfterWrite = DescribeApplicationPresence(entity, RegAppName);
        var generatedAfterWrite = DescribeApplicationPresence(
            entity, RoofStructuralGeneratedStore.RegAppName);
        System.Diagnostics.Debug.WriteLine(
            "[ROOF_STRUCT_XDATA_REPLACE]" +
            " generatedBefore=" + generatedBefore +
            " manualBefore=" + manualBefore +
            " generatedAfterClear=" + generatedAfterClear +
            " manualAfterWrite=" + manualAfterWrite +
            " generatedAfterWrite=" + generatedAfterWrite +
            " rawRegApps=" + DescribePresentRegApps(entity));
#endif
    }

    private static bool HasApplicationSection(Entity entity, string regAppName)
    {
        using var xdata = entity.XData;
        if (xdata is null) return false;
        foreach (var value in xdata.AsArray())
        {
            if (value.TypeCode == DxfRegAppNameCode &&
                string.Equals(
                    Convert.ToString(value.Value, CultureInfo.InvariantCulture),
                    regAppName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

#if DEBUG
    private static string DescribeApplicationPresence(Entity entity, string regAppName) =>
        HasApplicationSection(entity, regAppName) ? "present" : "absent";

    private static string DescribePresentRegApps(Entity entity)
    {
        using var xdata = entity.XData;
        if (xdata is null) return "(none)";
        var names = new List<string>();
        foreach (var value in xdata.AsArray())
        {
            if (value.TypeCode != DxfRegAppNameCode) continue;
            var name = Convert.ToString(value.Value, CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name!);
        }
        return names.Count == 0 ? "(none)" : string.Join("|", names);
    }
#endif

    public static IReadOnlyList<ObjectId> FindByOwner(
        Database database, Transaction transaction, string ownerReference)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!RoofStructuralGeneratedDataRules.TryNormalizeOwnerReference(
                ownerReference, out var normalizedOwner))
            return Array.Empty<ObjectId>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var matches = new List<ObjectId>();
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity ||
                entity.IsErased)
                continue;
            var stored = Read(entity);
            if (stored.Data is not null &&
                string.Equals(stored.Data.RoofOwnerReference, normalizedOwner,
                    StringComparison.OrdinalIgnoreCase))
                matches.Add(id);
        }
        return matches;
    }

    public static IReadOnlyList<TypedValue> BuildSection(
        Entity entity, Transaction transaction, RoofStructuralAttachedManualData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        var validated = RoofStructuralAttachedManualDataRules.Create(
            data.RoofOwnerReference, data.ManualIdentity, data.SourceLogicalKey,
            data.CreationKind, data.WidthMm, data.HeightMode, data.ExplicitHeightMm,
            data.Placement);
        if (validated.Data is null)
            throw new ArgumentException("Invalid Structural AttachedManual metadata: " + validated.Error);
        EnsureRegAppRegistered(entity.Database, transaction);
        var canonical = validated.Data;
        var heightToken = canonical.HeightMode == RoofStructuralHeightMode.Explicit
            ? "Explicit:" + canonical.ExplicitHeightMm!.Value.ToString("R", CultureInfo.InvariantCulture)
            : canonical.HeightMode.ToString();
        var payload = new List<TypedValue>
        {
            new(DxfRegAppNameCode, RegAppName),
            new(DxfInt16Code, checked((short)canonical.SchemaVersion)),
            new(DxfAsciiStringCode, canonical.RoofOwnerReference),
            new(DxfAsciiStringCode, canonical.ManualIdentity),
            new(DxfAsciiStringCode,
                RoofStructuralGeneratedDataRules.FormatRole(canonical.SourceRole)),
            new(DxfInt32Code, canonical.SourceBoundaryEdgeIdA),
            new(DxfInt32Code, canonical.SourceBoundaryEdgeIdB),
            new(DxfAsciiStringCode, canonical.CreationKind.ToString()),
            new(DxfRealCode, canonical.WidthMm),
            new(DxfAsciiStringCode, heightToken),
        };
        if (canonical.Placement is { } placement)
        {
            payload.Add(new TypedValue(DxfRealCode, placement.AxisStartX));
            payload.Add(new TypedValue(DxfRealCode, placement.AxisStartY));
            payload.Add(new TypedValue(DxfRealCode, placement.AxisStartZ));
            payload.Add(new TypedValue(DxfRealCode, placement.AxisEndX));
            payload.Add(new TypedValue(DxfRealCode, placement.AxisEndY));
            payload.Add(new TypedValue(DxfRealCode, placement.AxisEndZ));
            payload.Add(new TypedValue(DxfRealCode, placement.SideX));
            payload.Add(new TypedValue(DxfRealCode, placement.SideY));
            payload.Add(new TypedValue(DxfRealCode, placement.SideZ));
            payload.Add(new TypedValue(DxfRealCode, placement.UpX));
            payload.Add(new TypedValue(DxfRealCode, placement.UpY));
            payload.Add(new TypedValue(DxfRealCode, placement.UpZ));
            payload.Add(new TypedValue(DxfRealCode, placement.SectionHeightMm));
        }
        return payload;
    }

    private static bool TryReadPlacement(
        IReadOnlyList<TypedValue> values, out RoofStructuralManualPlacement? placement)
    {
        placement = null;
        var numbers = new double[13];
        for (var i = 0; i < numbers.Length; i++)
        {
            if (values[10 + i].TypeCode != DxfRealCode || values[10 + i].Value is not double number)
                return false;
            numbers[i] = number;
        }
        placement = new RoofStructuralManualPlacement(
            numbers[0], numbers[1], numbers[2],
            numbers[3], numbers[4], numbers[5],
            numbers[6], numbers[7], numbers[8],
            numbers[9], numbers[10], numbers[11],
            numbers[12]);
        return true;
    }

    private static List<TypedValue> ReadForeignXData(Entity entity)
    {
        var retained = new List<TypedValue>();
        using var xdata = entity.XData;
        if (xdata is null) return retained;
        var skip = false;
        foreach (var value in xdata.AsArray())
        {
            if (value.TypeCode == DxfRegAppNameCode)
            {
                var name = Convert.ToString(value.Value, CultureInfo.InvariantCulture);
                skip = string.Equals(name, RegAppName, StringComparison.OrdinalIgnoreCase);
            }
            if (!skip) retained.Add(value);
        }
        return retained;
    }

    private static void EnsureRegAppRegistered(Database database, Transaction transaction)
    {
        var table = (RegAppTable)transaction.GetObject(database.RegAppTableId, OpenMode.ForRead);
        if (table.Has(RegAppName)) return;
        table.UpgradeOpen();
        var record = new RegAppTableRecord { Name = RegAppName };
        table.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }
}

internal sealed record RoofStructuralAttachedManualStoreReadResult(
    bool Exists,
    RoofStructuralAttachedManualData? Data,
    RoofStructuralAttachedManualDataError Error)
{
    public static RoofStructuralAttachedManualStoreReadResult Missing { get; } =
        new(false, null, RoofStructuralAttachedManualDataError.Missing);

    public static RoofStructuralAttachedManualStoreReadResult Valid(
        RoofStructuralAttachedManualData data) =>
        new(true, data, RoofStructuralAttachedManualDataError.None);

    public static RoofStructuralAttachedManualStoreReadResult Invalid(
        RoofStructuralAttachedManualDataError error) => new(true, null, error);
}
