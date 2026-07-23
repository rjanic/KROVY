using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Ukladá malé, prenosné metadáta prvku priamo do XData entity.
/// XData ide spolu s LINE/LWPOLYLINE pri COPY, WBLOCK a vložení do iného DWG.
/// Staré údaje z verzie 0.1.0 sa stále dokážu načítať z Extension Dictionary,
/// aby existujúce výkresy zostali použiteľné. Pri najbližšom AK_EDIT/AK_ASSIGN
/// sa automaticky zapíšu aj v novom prenosnom formáte.
/// </summary>
internal static class ElementDataStore
{
    // Stabilný názov registrovanej aplikácie. Nezačína na "ACAD_", aby sa nemiešal
    // so systémovými názvami AutoCADu.
    private const string RegAppName = "DECORAIR_ACADKROVY";
    private const int DxfRegAppNameCode = 1001;
    private const int DxfAsciiStringCode = 1000;
    private const int MaxXDataTextChunkLength = 240;
    private const int MaxPortableJsonBytes = 15 * 1024;

    // Legacy názvy z ACAD KROVY 0.1.0. Slúžia len na spätné načítanie starých DWG.
    private const string LegacyApplicationDictionaryName = "ACAD_KROVY";
    private const string LegacyElementDataRecordName = "TIMBER_ELEMENT_DATA";
    private const int LegacyDxfTextCode = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static bool TryRead(Entity entity, Transaction transaction, out TimberElementData? data)
    {
        data = null;

        // Nový, prenosný formát má prednosť.
        if (TryReadPortableXData(entity, out data))
        {
            return true;
        }

        // Kompatibilita s výkresmi vytvorenými vo verzii 0.1.0.
        return TryReadLegacyExtensionDictionary(entity, transaction, out data);
    }

    public static void Write(Entity entity, Transaction transaction, TimberElementData data)
    {
        if (!entity.IsWriteEnabled)
        {
            entity.UpgradeOpen();
        }

        EnsureRegAppRegistered(entity.Database, transaction);

        var normalizedData = TimberElementDataVersioning.PrepareForWrite(data);
        var json = JsonSerializer.Serialize(normalizedData, JsonOptions);
        var jsonByteCount = Encoding.UTF8.GetByteCount(json);
        if (jsonByteCount > MaxPortableJsonBytes)
        {
            throw new InvalidOperationException(
                UiStrings.Format(UiStrings.ErrorXDataTooLargeFormat, jsonByteCount));
        }

        // Zachovaj XData ostatných doplnkov a prepíš iba náš vlastný oddiel.
        var values = ReadForeignXData(entity);
        values.Add(new TypedValue(DxfRegAppNameCode, RegAppName));
        values.AddRange(SplitIntoDxfTextChunks(json)
            .Select(chunk => new TypedValue(DxfAsciiStringCode, chunk)));

        using var newXData = new ResultBuffer(values.ToArray());
        entity.XData = newXData;
    }

    private static bool TryReadPortableXData(Entity entity, out TimberElementData? data)
    {
        data = null;

        var xdata = entity.XData;
        if (xdata is null)
        {
            return false;
        }

        using (xdata)
        {
            var values = xdata.AsArray();
            var insideOurSection = false;
            var json = new StringBuilder();

            foreach (var value in values)
            {
                if (value.TypeCode == DxfRegAppNameCode)
                {
                    if (insideOurSection)
                    {
                        break;
                    }

                    insideOurSection = string.Equals(
                        Convert.ToString(value.Value, CultureInfo.InvariantCulture),
                        RegAppName,
                        StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (insideOurSection && value.TypeCode == DxfAsciiStringCode)
                {
                    json.Append(Convert.ToString(value.Value, CultureInfo.InvariantCulture));
                }
            }

            return json.Length > 0 && TryDeserialize(json.ToString(), out data);
        }
    }

    private static List<TypedValue> ReadForeignXData(Entity entity)
    {
        var retained = new List<TypedValue>();
        var xdata = entity.XData;
        if (xdata is null)
        {
            return retained;
        }

        using (xdata)
        {
            var skipCurrentSection = false;

            foreach (var value in xdata.AsArray())
            {
                if (value.TypeCode == DxfRegAppNameCode)
                {
                    skipCurrentSection = string.Equals(
                        Convert.ToString(value.Value, CultureInfo.InvariantCulture),
                        RegAppName,
                        StringComparison.OrdinalIgnoreCase);
                }

                if (!skipCurrentSection)
                {
                    retained.Add(value);
                }
            }
        }

        return retained;
    }

    private static void EnsureRegAppRegistered(Database database, Transaction transaction)
    {
        var regAppTable = (RegAppTable)transaction.GetObject(database.RegAppTableId, OpenMode.ForRead);
        if (regAppTable.Has(RegAppName))
        {
            return;
        }

        regAppTable.UpgradeOpen();
        var record = new RegAppTableRecord { Name = RegAppName };
        regAppTable.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static bool TryReadLegacyExtensionDictionary(Entity entity, Transaction transaction, out TimberElementData? data)
    {
        data = null;

        if (entity.ExtensionDictionary.IsNull)
        {
            return false;
        }

        var root = transaction.GetObject(entity.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
        if (root is null || !root.Contains(LegacyApplicationDictionaryName))
        {
            return false;
        }

        var appDictionary = transaction.GetObject(
            root.GetAt(LegacyApplicationDictionaryName),
            OpenMode.ForRead) as DBDictionary;
        if (appDictionary is null || !appDictionary.Contains(LegacyElementDataRecordName))
        {
            return false;
        }

        var record = transaction.GetObject(
            appDictionary.GetAt(LegacyElementDataRecordName),
            OpenMode.ForRead) as Xrecord;
        if (record?.Data is null)
        {
            return false;
        }

        var json = string.Concat(record.Data
            .AsArray()
            .Where(value => value.TypeCode == LegacyDxfTextCode)
            .Select(value => Convert.ToString(value.Value, CultureInfo.InvariantCulture)));

        return !string.IsNullOrWhiteSpace(json) && TryDeserialize(json, out data);
    }

    private static bool TryDeserialize(string json, out TimberElementData? data)
    {
        data = null;

        try
        {
            data = JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions);
            if (data is null)
            {
                return false;
            }

            data = TimberElementDataVersioning.Normalize(data);
            return true;
        }
        catch (System.Exception ex) when (
            ex is JsonException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static IEnumerable<string> SplitIntoDxfTextChunks(string value)
    {
        for (var index = 0; index < value.Length; index += MaxXDataTextChunkLength)
        {
            yield return value.Substring(index, Math.Min(MaxXDataTextChunkLength, value.Length - index));
        }
    }
}
