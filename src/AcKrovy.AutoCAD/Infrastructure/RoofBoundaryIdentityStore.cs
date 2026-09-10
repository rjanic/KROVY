using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Typed XData adapter for identity owned only by an authoritative roof source Polyline.
/// Reading is strictly side-effect free.
/// </summary>
internal static class RoofBoundaryIdentityStore
{
    internal const string RegAppName = "DECORAIR_ACADKROVY_ROOF_BOUNDARY_IDENTITY";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;
    private const int DxfInt16Code = (int)DxfCode.ExtendedDataInteger16;
    private const int DxfInt32Code = (int)DxfCode.ExtendedDataInteger32;

    public static RoofBoundaryIdentityStoreReadResult Read(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity is not Polyline source || RoofDefinitionStore.Read(source).Data is null)
        {
            return RoofBoundaryIdentityStoreReadResult.Invalid(
                RoofBoundaryIdentityError.NotAuthoritativeSource);
        }

        try
        {
            using var xdata = source.GetXDataForApplication(RegAppName);
            if (xdata is null)
            {
                return RoofBoundaryIdentityStoreReadResult.Missing;
            }

            var decoded = DecodePayload(xdata.AsArray());
            if (decoded.Data is null)
            {
                return decoded;
            }

            var normalized = RoofFootprintValidator.ValidateWithProvenance(
                RoofPolylineExtractor.Extract(source));
            if (!normalized.Validation.IsValid)
            {
                return RoofBoundaryIdentityStoreReadResult.Invalid(
                    RoofBoundaryIdentityError.InvalidFootprint);
            }

            var currentError = RoofBoundaryIdentityRules.ValidateCurrentSource(
                decoded.Data,
                normalized.EdgeProvenance.Count,
                normalized.Validation.SourceOrientation);
            return currentError == RoofBoundaryIdentityError.None
                ? decoded
                : RoofBoundaryIdentityStoreReadResult.Invalid(currentError);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofBoundaryIdentityStoreReadResult.Invalid(
                RoofBoundaryIdentityError.MalformedValueType);
        }
    }

    internal static RoofBoundaryIdentityStoreReadResult DecodePayload(
        IReadOnlyList<TypedValue> values)
    {
        if (values.Count < 5 ||
                values[0].TypeCode != DxfRegAppNameCode ||
                values[0].Value is not string applicationName ||
                !string.Equals(applicationName, RegAppName, StringComparison.OrdinalIgnoreCase))
        {
            return RoofBoundaryIdentityStoreReadResult.Invalid(
                RoofBoundaryIdentityError.IncompletePayload);
        }

        if (values[1].TypeCode != DxfInt16Code ||
            values[1].Value is not short schemaVersion ||
            values[2].TypeCode != DxfInt32Code ||
            values[2].Value is not int physicalSegmentCount ||
            values[3].TypeCode != DxfAsciiStringCode ||
            values[3].Value is not string windingToken)
        {
            return RoofBoundaryIdentityStoreReadResult.Invalid(
                RoofBoundaryIdentityError.MalformedValueType);
        }

        var ids = new int[values.Count - 4];
        for (var index = 4; index < values.Count; index++)
        {
            if (values[index].TypeCode != DxfInt32Code ||
                values[index].Value is not int boundaryEdgeId)
            {
                return RoofBoundaryIdentityStoreReadResult.Invalid(
                    RoofBoundaryIdentityError.MalformedValueType);
            }

            ids[index - 4] = boundaryEdgeId;
        }

        var decoded = RoofBoundaryIdentityRules.Validate(
            schemaVersion,
            physicalSegmentCount,
            windingToken,
            ids);
        if (!decoded.IsValid || decoded.Identity is null)
        {
            return RoofBoundaryIdentityStoreReadResult.Invalid(decoded.Error);
        }

        return RoofBoundaryIdentityStoreReadResult.Valid(decoded.Identity);
    }

    public static void Write(
        Polyline source,
        Transaction transaction,
        RoofBoundaryIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(identity);
        if (!source.IsWriteEnabled)
        {
            throw new InvalidOperationException(
                "Boundary identity source must be opened ForWrite.");
        }

        if (RoofDefinitionStore.Read(source).Data is null)
        {
            throw new InvalidOperationException(
                "Boundary identity may be written only to an authoritative roof source.");
        }

        var normalized = RoofFootprintValidator.ValidateWithProvenance(
            RoofPolylineExtractor.Extract(source));
        if (!normalized.Validation.IsValid)
        {
            throw new InvalidOperationException("Boundary identity source footprint is invalid.");
        }

        var currentError = RoofBoundaryIdentityRules.ValidateCurrentSource(
            identity,
            normalized.EdgeProvenance.Count,
            normalized.Validation.SourceOrientation);
        if (currentError != RoofBoundaryIdentityError.None)
        {
            throw new InvalidOperationException(
                "Boundary identity is incompatible with the current source: " + currentError);
        }

        EnsureRegAppRegistered(source.Database, transaction);
        var retained = ReadForeignXData(source);
        retained.Add(new TypedValue(DxfRegAppNameCode, RegAppName));
        retained.Add(new TypedValue(DxfInt16Code, checked((short)identity.SchemaVersion)));
        retained.Add(new TypedValue(DxfInt32Code, identity.PhysicalSegmentCount));
        retained.Add(new TypedValue(
            DxfAsciiStringCode,
            RoofBoundaryIdentityRules.FormatWinding(identity.RawWinding)));
        retained.AddRange(identity.BoundaryEdgeIds.Select(
            id => new TypedValue(DxfInt32Code, id)));

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

        var skipBoundaryIdentitySection = false;
        foreach (var value in xdata.AsArray())
        {
            if (value.TypeCode == DxfRegAppNameCode)
            {
                skipBoundaryIdentitySection = string.Equals(
                    Convert.ToString(value.Value, CultureInfo.InvariantCulture),
                    RegAppName,
                    StringComparison.OrdinalIgnoreCase);
            }

            if (!skipBoundaryIdentitySection)
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

internal sealed record RoofBoundaryIdentityStoreReadResult(
    bool Exists,
    RoofBoundaryIdentity? Data,
    RoofBoundaryIdentityError Error)
{
    public static RoofBoundaryIdentityStoreReadResult Missing { get; } =
        new(false, null, RoofBoundaryIdentityError.Missing);

    public static RoofBoundaryIdentityStoreReadResult Valid(RoofBoundaryIdentity data) =>
        new(true, data, RoofBoundaryIdentityError.None);

    public static RoofBoundaryIdentityStoreReadResult Invalid(
        RoofBoundaryIdentityError error) => new(true, null, error);
}
