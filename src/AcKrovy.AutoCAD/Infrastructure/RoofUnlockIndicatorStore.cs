using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Derived unlock-indicator XData. Not timber, display, or report metadata.</summary>
internal static class RoofUnlockIndicatorStore
{
    internal const string RegAppName = "DECORAIR_ACADKROVY_ROOF_UI";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;

    public static string? TryReadOwnerReference(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        try
        {
            using var xdata = entity.GetXDataForApplication(RegAppName);
            if (xdata is null)
            {
                return null;
            }

            var values = xdata.AsArray();
            if (values.Length < 2 ||
                values[0].TypeCode != DxfRegAppNameCode)
            {
                return null;
            }

            var payload = new StringBuilder();
            for (var index = 1; index < values.Length; index++)
            {
                if (values[index].TypeCode == DxfAsciiStringCode)
                {
                    payload.Append(Convert.ToString(
                        values[index].Value,
                        CultureInfo.InvariantCulture));
                }
            }

            var owner = payload.ToString();
            return string.IsNullOrWhiteSpace(owner) ? null : owner;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return null;
        }
    }

    public static void Write(Entity entity, Transaction transaction, string ownerReference)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!entity.IsWriteEnabled)
        {
            throw new InvalidOperationException("Unlock indicator must be opened ForWrite.");
        }

        EnsureRegAppRegistered(entity.Database, transaction);
        using var buffer = new ResultBuffer(
            new TypedValue(DxfRegAppNameCode, RegAppName),
            new TypedValue(DxfAsciiStringCode, ownerReference));
        entity.XData = buffer;
    }

    public static bool Exists(Entity entity) => TryReadOwnerReference(entity) is not null;

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
