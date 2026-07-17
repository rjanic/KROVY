using System.Globalization;
using System.Text;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Prenosné XData metadáta automatického textového štítku. Sú uložené priamo
/// na MText objekte, preto ostávajú pri popise aj po COPY a WBLOCK.
/// </summary>
internal sealed record ElementLabelData
{
    public int SchemaVersion { get; init; } = 1;
    public string ElementId { get; init; } = string.Empty;
    public string SourceHandle { get; init; } = string.Empty;
}

internal static class ElementLabelStore
{
    private const string RegAppName = "DECORAIR_ACADKROVY_LABEL";
    private const int DxfRegAppNameCode = 1001;
    private const int DxfAsciiStringCode = 1000;
    private const int MaxTextChunkLength = 240;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public static bool TryRead(Entity entity, out ElementLabelData? data)
    {
        data = null;

        var xdata = entity.XData;
        if (xdata is null)
        {
            return false;
        }

        using (xdata)
        {
            var insideOurSection = false;
            var json = new StringBuilder();

            foreach (var value in xdata.AsArray())
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

            if (json.Length == 0)
            {
                return false;
            }

            try
            {
                data = JsonSerializer.Deserialize<ElementLabelData>(json.ToString(), JsonOptions);
                return data is not null && !string.IsNullOrWhiteSpace(data.ElementId);
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    public static void Write(Entity entity, Transaction transaction, ElementLabelData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);

        if (!entity.IsWriteEnabled)
        {
            entity.UpgradeOpen();
        }

        EnsureRegAppRegistered(entity.Database, transaction);

        var retained = ReadForeignXData(entity);
        retained.Add(new TypedValue(DxfRegAppNameCode, RegAppName));

        var json = JsonSerializer.Serialize(data, JsonOptions);
        foreach (var chunk in SplitIntoChunks(json))
        {
            retained.Add(new TypedValue(DxfAsciiStringCode, chunk));
        }

        using var buffer = new ResultBuffer(retained.ToArray());
        entity.XData = buffer;
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
}
