using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Persistent member-owned identity, independent of roof reconciliation.</summary>
internal static class RoofIndependentOrdinaryTimberStore
{
    internal const string RegAppName = "DECORAIR_ACADKROVY_INDEPENDENT_ORDINARY";
    private const int AppCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int TextCode = (int)DxfCode.ExtendedDataAsciiString;

    public static RoofIndependentOrdinaryTimberData? Read(Entity entity)
    {
        using var section = entity.GetXDataForApplication(RegAppName);
        if (section is null) return null;
        var values = section.AsArray();
        if (values.Length < 2 || values[0].TypeCode != AppCode ||
            !string.Equals(values[0].Value as string, RegAppName, StringComparison.OrdinalIgnoreCase) ||
            values.Skip(1).Any(value => value.TypeCode != TextCode)) return null;
        var payload = string.Concat(values.Skip(1).Select(value => value.Value as string));
        return RoofIndependentOrdinaryTimberDataCodec.TryDecode(payload, out var data) ? data : null;
    }

    public static void Write(Entity entity, Transaction transaction, RoofIndependentOrdinaryTimberData data)
    {
        if (!entity.IsWriteEnabled || !RoofIndependentOrdinaryTimberDataCodec.IsValid(data))
            throw new InvalidOperationException("Independent Ordinary metadata cannot be written.");
        EnsureRegistered(entity, transaction);
        var values = RetainOtherSections(entity);
        values.AddRange(BuildSection(data));
        using var buffer = new ResultBuffer(values.ToArray());
        entity.XData = buffer;
        if (Read(entity) != data)
            throw new InvalidOperationException("Independent Ordinary metadata readback failed.");
    }

    /// <summary>Clear each roof RegApp with AutoCAD's erase sentinel, then add member ownership.</summary>
    public static void TransferFromRoof(Entity entity, Transaction transaction,
        RoofIndependentOrdinaryTimberData data, params string[] roofRegApps)
    {
        if (!entity.IsWriteEnabled || !RoofIndependentOrdinaryTimberDataCodec.IsValid(data))
            throw new InvalidOperationException("Independent Ordinary transfer requires ForWrite.");
        // A combined buffer of erase sentinels and the new section left a roof
        // RegApp visible in HOST and discarded the generic Timber section.
        // AutoCAD clears one application per assignment. The caller's transaction
        // rolls all of these changes back together if any readback fails.
        foreach (var name in roofRegApps.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!HasSection(entity, name)) continue;
            using var erase = new ResultBuffer(new TypedValue(AppCode, name));
            entity.XData = erase;
            if (HasSection(entity, name))
                throw new InvalidOperationException("Independent Ordinary roof XData clear readback failed: " + name);
        }

        Write(entity, transaction, data);
        if (roofRegApps.Any(name => HasSection(entity, name)))
            throw new InvalidOperationException("Independent Ordinary transfer retained roof XData.");
    }

    private static IEnumerable<TypedValue> BuildSection(RoofIndependentOrdinaryTimberData data)
    {
        yield return new TypedValue(AppCode, RegAppName);
        var payload = RoofIndependentOrdinaryTimberDataCodec.Encode(data);
        for (var offset = 0; offset < payload.Length; offset += 240)
            yield return new TypedValue(TextCode,
                payload.Substring(offset, Math.Min(240, payload.Length - offset)));
    }

    private static void EnsureRegistered(Entity entity, Transaction transaction)
    {
        var table = (RegAppTable)transaction.GetObject(entity.Database.RegAppTableId, OpenMode.ForRead);
        if (table.Has(RegAppName)) return;
        table.UpgradeOpen();
        var registration = new RegAppTableRecord { Name = RegAppName };
        table.Add(registration);
        transaction.AddNewlyCreatedDBObject(registration, true);
    }

    private static bool HasSection(Entity entity, string name)
    {
        using var buffer = entity.XData;
        return buffer is not null && buffer.AsArray().Any(value =>
            value.TypeCode == AppCode &&
            string.Equals(value.Value as string, name, StringComparison.OrdinalIgnoreCase));
    }

    private static List<TypedValue> RetainOtherSections(Entity entity)
    {
        var values = new List<TypedValue>();
        using var buffer = entity.XData;
        if (buffer is null) return values;
        var skip = false;
        foreach (var value in buffer.AsArray())
        {
            if (value.TypeCode == AppCode)
                skip = string.Equals(Convert.ToString(value.Value, CultureInfo.InvariantCulture),
                    RegAppName, StringComparison.OrdinalIgnoreCase);
            if (!skip) values.Add(value);
        }
        return values;
    }
}
