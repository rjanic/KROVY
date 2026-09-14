using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Independent schema-1 owner XData store. Reads are side-effect free and missing
/// metadata remains distinct from invalid metadata.
/// </summary>
internal static class RoofRelativeElevationDatumStore
{
    internal const string RegAppName = "DECORAIR_ACADKROVY_ROOF_RELATIVE_ELEVATION_DATUM";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;
    private const int DxfRealCode = (int)DxfCode.ExtendedDataReal;
    private const int DxfInt16Code = (int)DxfCode.ExtendedDataInteger16;
    private const int ValueCount = 5;

    public static RoofRelativeElevationDatumStoreReadResult Read(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity is not Polyline source || RoofDefinitionStore.Read(source).Data is null)
        {
            return RoofRelativeElevationDatumStoreReadResult.Invalid(
                RoofRelativeElevationDatumError.NotAuthoritativeSource);
        }

        try
        {
            using var xdata = source.GetXDataForApplication(RegAppName);
            return xdata is null
                ? RoofRelativeElevationDatumStoreReadResult.Missing
                : DecodePayload(xdata.AsArray());
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofRelativeElevationDatumStoreReadResult.Invalid(
                RoofRelativeElevationDatumError.MalformedValueType);
        }
    }

    internal static RoofRelativeElevationDatumStoreReadResult DecodePayload(
        IReadOnlyList<TypedValue> values)
    {
        if (values.Count < ValueCount)
        {
            return RoofRelativeElevationDatumStoreReadResult.Invalid(
                RoofRelativeElevationDatumError.IncompletePayload);
        }

        if (values.Count > ValueCount)
        {
            return RoofRelativeElevationDatumStoreReadResult.Invalid(
                RoofRelativeElevationDatumError.UnexpectedTrailingValue);
        }

        if (values[0].TypeCode != DxfRegAppNameCode ||
            values[0].Value is not string applicationName ||
            !string.Equals(applicationName, RegAppName, StringComparison.OrdinalIgnoreCase) ||
            values[1].TypeCode != DxfInt16Code ||
            values[1].Value is not short schemaVersion ||
            values[2].TypeCode != DxfAsciiStringCode ||
            values[2].Value is not string kindToken ||
            values[3].TypeCode != DxfRealCode ||
            values[3].Value is not double referenceRelativeElevationMm ||
            values[4].TypeCode != DxfRealCode ||
            values[4].Value is not double referenceLocalZMm ||
            !Enum.TryParse<RoofRelativeElevationReferenceKind>(kindToken, false, out var kind) ||
            !string.Equals(kind.ToString(), kindToken, StringComparison.Ordinal))
        {
            return RoofRelativeElevationDatumStoreReadResult.Invalid(
                RoofRelativeElevationDatumError.MalformedValueType);
        }

        var validated = RoofRelativeElevationDatumRules.Validate(
            schemaVersion,
            kind,
            referenceRelativeElevationMm,
            referenceLocalZMm);
        return validated.Datum is null
            ? RoofRelativeElevationDatumStoreReadResult.Invalid(validated.Error)
            : RoofRelativeElevationDatumStoreReadResult.Valid(validated.Datum);
    }

    internal static IReadOnlyList<TypedValue> EncodePayload(RoofRelativeElevationDatum datum)
    {
        ArgumentNullException.ThrowIfNull(datum);
        var validated = RoofRelativeElevationDatumRules.Validate(
            RoofRelativeElevationDatumSchema.CurrentVersion,
            datum.ReferenceKind,
            datum.ReferenceRelativeElevationMm,
            datum.ReferenceLocalZMm);
        if (validated.Datum is null)
        {
            throw new ArgumentException("Invalid roof relative-elevation datum: " + validated.Error, nameof(datum));
        }

        return Array.AsReadOnly(new[]
        {
            new TypedValue(DxfRegAppNameCode, RegAppName),
            new TypedValue(DxfInt16Code, checked((short)RoofRelativeElevationDatumSchema.CurrentVersion)),
            new TypedValue(DxfAsciiStringCode, validated.Datum.ReferenceKind.ToString()),
            new TypedValue(DxfRealCode, validated.Datum.ReferenceRelativeElevationMm),
            new TypedValue(DxfRealCode, validated.Datum.ReferenceLocalZMm),
        });
    }

    public static void Write(
        Polyline source,
        Transaction transaction,
        RoofRelativeElevationDatum datum)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(datum);
        if (!source.IsWriteEnabled)
        {
            throw new InvalidOperationException("Relative-elevation datum source must be opened ForWrite.");
        }

        if (RoofDefinitionStore.Read(source).Data is null)
        {
            throw new InvalidOperationException("Relative-elevation datum may be written only to an authoritative roof source.");
        }

        var section = EncodePayload(datum);
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

internal sealed record RoofRelativeElevationDatumStoreReadResult(
    bool Exists,
    RoofRelativeElevationDatum? Data,
    RoofRelativeElevationDatumError Error)
{
    public static RoofRelativeElevationDatumStoreReadResult Missing { get; } =
        new(false, null, RoofRelativeElevationDatumError.Missing);

    public static RoofRelativeElevationDatumStoreReadResult Valid(RoofRelativeElevationDatum data) =>
        new(true, data, RoofRelativeElevationDatumError.None);

    public static RoofRelativeElevationDatumStoreReadResult Invalid(RoofRelativeElevationDatumError error) =>
        new(true, null, error);
}
