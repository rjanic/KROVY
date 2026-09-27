using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Dedicated owner XData store for absolute roof elevation, Physical3DEnabled, and
/// per-roof display visibility. Independent of RoofDefinitionData schema 5.
/// Schema 1: mode/entered/eave/enabled. Schema 2 adds DisplayVisibility.
/// </summary>
internal static class RoofPhysicalElevationStore
{
    internal const string RegAppName = "DECORAIR_ACADKROVY_ROOF_PHYSICAL_ELEVATION";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;
    private const int DxfRealCode = (int)DxfCode.ExtendedDataReal;
    private const int DxfInt16Code = (int)DxfCode.ExtendedDataInteger16;
    private const int Schema1ValueCount = 6;
    private const int Schema2ValueCount = 7;

    public static RoofPhysicalElevationStoreReadResult Read(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity is not Polyline source || RoofDefinitionStore.Read(source).Data is null)
        {
            return RoofPhysicalElevationStoreReadResult.Invalid(
                RoofPhysicalElevationError.NotAuthoritativeSource);
        }

        try
        {
            using var xdata = source.GetXDataForApplication(RegAppName);
            return xdata is null
                ? RoofPhysicalElevationStoreReadResult.Missing
                : DecodePayload(xdata.AsArray());
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofPhysicalElevationStoreReadResult.Invalid(
                RoofPhysicalElevationError.MalformedValueType);
        }
    }

    internal static RoofPhysicalElevationStoreReadResult DecodePayload(
        IReadOnlyList<TypedValue> values)
    {
        if (values.Count < Schema1ValueCount)
        {
            return RoofPhysicalElevationStoreReadResult.Invalid(
                RoofPhysicalElevationError.IncompletePayload);
        }

        if (values[0].TypeCode != DxfRegAppNameCode ||
            values[0].Value is not string applicationName ||
            !string.Equals(applicationName, RegAppName, StringComparison.OrdinalIgnoreCase) ||
            values[1].TypeCode != DxfInt16Code ||
            values[1].Value is not short schemaVersion)
        {
            return RoofPhysicalElevationStoreReadResult.Invalid(
                RoofPhysicalElevationError.MalformedValueType);
        }

        if (schemaVersion == RoofPhysicalElevationSchema.Version1)
        {
            if (values.Count != Schema1ValueCount)
            {
                return RoofPhysicalElevationStoreReadResult.Invalid(
                    values.Count < Schema1ValueCount
                        ? RoofPhysicalElevationError.IncompletePayload
                        : RoofPhysicalElevationError.UnexpectedTrailingValue);
            }

            return DecodeCommon(
                schemaVersion,
                values,
                displayVisibility: RoofPhysicalDisplayVisibility.Both);
        }

        if (schemaVersion == RoofPhysicalElevationSchema.CurrentVersion)
        {
            if (values.Count != Schema2ValueCount)
            {
                return RoofPhysicalElevationStoreReadResult.Invalid(
                    values.Count < Schema2ValueCount
                        ? RoofPhysicalElevationError.IncompletePayload
                        : RoofPhysicalElevationError.UnexpectedTrailingValue);
            }

            if (values[6].TypeCode != DxfAsciiStringCode ||
                values[6].Value is not string visibilityToken ||
                !Enum.TryParse(visibilityToken, false, out RoofPhysicalDisplayVisibility visibility) ||
                !string.Equals(visibility.ToString(), visibilityToken, StringComparison.Ordinal))
            {
                return RoofPhysicalElevationStoreReadResult.Invalid(
                    RoofPhysicalElevationError.MalformedValueType);
            }

            return DecodeCommon(schemaVersion, values, visibility);
        }

        return RoofPhysicalElevationStoreReadResult.Invalid(
            RoofPhysicalElevationError.UnsupportedSchemaVersion);
    }

    private static RoofPhysicalElevationStoreReadResult DecodeCommon(
        int schemaVersion,
        IReadOnlyList<TypedValue> values,
        RoofPhysicalDisplayVisibility displayVisibility)
    {
        if (values[2].TypeCode != DxfAsciiStringCode ||
            values[2].Value is not string modeToken ||
            values[3].TypeCode != DxfRealCode ||
            values[3].Value is not double enteredRelativeElevationMm ||
            values[4].TypeCode != DxfRealCode ||
            values[4].Value is not double resolvedEaveRelativeElevationMm ||
            values[5].TypeCode != DxfInt16Code ||
            values[5].Value is not short enabledToken ||
            !Enum.TryParse<RoofAbsoluteElevationInputMode>(modeToken, false, out var mode) ||
            !string.Equals(mode.ToString(), modeToken, StringComparison.Ordinal) ||
            enabledToken is not (0 or 1))
        {
            return RoofPhysicalElevationStoreReadResult.Invalid(
                RoofPhysicalElevationError.MalformedValueType);
        }

        var validated = RoofPhysicalElevationRules.Validate(
            schemaVersion,
            mode,
            enteredRelativeElevationMm,
            resolvedEaveRelativeElevationMm,
            enabledToken == 1,
            displayVisibility);
        return validated.Data is null
            ? RoofPhysicalElevationStoreReadResult.Invalid(validated.Error)
            : RoofPhysicalElevationStoreReadResult.Valid(validated.Data);
    }

    internal static IReadOnlyList<TypedValue> EncodePayload(RoofPhysicalElevationData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var validated = RoofPhysicalElevationRules.Validate(
            RoofPhysicalElevationSchema.CurrentVersion,
            data.InputMode,
            data.EnteredRelativeElevationMm,
            data.ResolvedEaveRelativeElevationMm,
            data.Physical3DEnabled,
            data.DisplayVisibility);
        if (validated.Data is null)
        {
            throw new ArgumentException(
                "Invalid roof physical elevation data: " + validated.Error,
                nameof(data));
        }

        var canonical = validated.Data;
        return Array.AsReadOnly(new[]
        {
            new TypedValue(DxfRegAppNameCode, RegAppName),
            new TypedValue(DxfInt16Code, checked((short)RoofPhysicalElevationSchema.CurrentVersion)),
            new TypedValue(DxfAsciiStringCode, canonical.InputMode.ToString()),
            new TypedValue(DxfRealCode, canonical.EnteredRelativeElevationMm),
            new TypedValue(DxfRealCode, canonical.ResolvedEaveRelativeElevationMm),
            new TypedValue(DxfInt16Code, (short)(canonical.Physical3DEnabled ? 1 : 0)),
            new TypedValue(DxfAsciiStringCode, canonical.DisplayVisibility.ToString()),
        });
    }

    public static void Write(
        Polyline source,
        Transaction transaction,
        RoofPhysicalElevationData data)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        if (!source.IsWriteEnabled)
        {
            throw new InvalidOperationException(
                "Physical elevation source must be opened ForWrite.");
        }

        if (RoofDefinitionStore.Read(source).Data is null)
        {
            throw new InvalidOperationException(
                "Physical elevation may be written only to an authoritative roof source.");
        }

        var section = EncodePayload(data);
        EnsureRegAppRegistered(source.Database, transaction);
        var retained = ReadForeignXData(source);
        retained.AddRange(section);
        using var buffer = new ResultBuffer(retained.ToArray());
        source.XData = buffer;
    }

    public static void Clear(Polyline source, Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!source.IsWriteEnabled)
        {
            throw new InvalidOperationException(
                "Physical elevation source must be opened ForWrite.");
        }

        EnsureRegAppRegistered(source.Database, transaction);
        var retained = ReadForeignXData(source);
        using var buffer = retained.Count == 0
            ? null
            : new ResultBuffer(retained.ToArray());
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

internal sealed record RoofPhysicalElevationStoreReadResult(
    bool Exists,
    RoofPhysicalElevationData? Data,
    RoofPhysicalElevationError Error)
{
    public static RoofPhysicalElevationStoreReadResult Missing { get; } =
        new(false, null, RoofPhysicalElevationError.Missing);

    public static RoofPhysicalElevationStoreReadResult Valid(RoofPhysicalElevationData data) =>
        new(true, data, RoofPhysicalElevationError.None);

    public static RoofPhysicalElevationStoreReadResult Invalid(RoofPhysicalElevationError error) =>
        new(true, null, error);
}
