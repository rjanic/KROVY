using System.Text.Json;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Full independent builder inputs travel with the Plan entity, without the XData size limit.</summary>
internal static class RoofOrdinaryPhysicalBuildStateStore
{
    private const string RecordName = "ACAD_KROVY_ORDINARY_PHYSICAL_BUILD_V1";

    internal static bool HasRecord(Line line, Transaction transaction) => !line.ExtensionDictionary.IsNull &&
        ((DBDictionary)transaction.GetObject(line.ExtensionDictionary, OpenMode.ForRead)).Contains(RecordName);

    public static RoofOrdinaryPhysicalBuildState? Read(Line line, Transaction transaction,
        RoofOrdinaryPhysicalBuildStateTrace? trace = null)
    {
        if (line.ExtensionDictionary.IsNull)
        { trace?.Add("storedStateResolution", "missing_extension_dictionary"); return null; }
        var dictionary = (DBDictionary)transaction.GetObject(line.ExtensionDictionary, OpenMode.ForRead);
        if (!dictionary.Contains(RecordName))
        { trace?.Add("storedStateResolution", "missing_xrecord"); return null; }
        var record = (Xrecord)transaction.GetObject(dictionary.GetAt(RecordName), OpenMode.ForRead);
        using var data = record.Data;
        if (data is null)
        { trace?.Add("storedStateResolution", "null_xrecord_data"); return null; }
        if (data.AsArray().Any(v => v.TypeCode != (int)DxfCode.Text))
        { trace?.Add("storedStateResolution", "non_text_xrecord_data"); return null; }
        try
        {
            var state = JsonSerializer.Deserialize<RoofOrdinaryPhysicalBuildState>(
                string.Concat(data.AsArray().Select(v => v.Value as string)));
            var valid = RoofOrdinaryPhysicalBuildStateRules.IsValid(state);
            trace?.State(state);
            trace?.Add("storedStateResolution", state is null ? "deserialize_null" : valid ? "valid" : "IsValid_false");
            if (!valid) trace?.InvalidState(state);
            return valid ? state : null;
        }
        catch (JsonException ex)
        {
            trace?.Add("storedStateResolution", "JsonException");
            trace?.Add("jsonPath", ex.Path);
            return null;
        }
    }

    public static void Write(Line line, Transaction transaction, RoofOrdinaryPhysicalBuildState state)
    {
        if (!line.IsWriteEnabled || !RoofOrdinaryPhysicalBuildStateRules.IsValid(state))
            throw new InvalidOperationException("Invalid Ordinary physical build state.");
        if (line.ExtensionDictionary.IsNull) line.CreateExtensionDictionary();
        var dictionary = (DBDictionary)transaction.GetObject(line.ExtensionDictionary, OpenMode.ForWrite);
        Xrecord record;
        if (dictionary.Contains(RecordName))
            record = (Xrecord)transaction.GetObject(dictionary.GetAt(RecordName), OpenMode.ForWrite);
        else
        {
            record = new Xrecord();
            dictionary.SetAt(RecordName, record);
            transaction.AddNewlyCreatedDBObject(record, true);
        }
        var json = JsonSerializer.Serialize(state);
        var chunks = new List<TypedValue>();
        for (var offset = 0; offset < json.Length; offset += 240)
            chunks.Add(new TypedValue((int)DxfCode.Text, json.Substring(offset, Math.Min(240, json.Length - offset))));
        using var buffer = new ResultBuffer(chunks.ToArray());
        record.Data = buffer;
        if (JsonSerializer.Serialize(Read(line, transaction)) != json)
            throw new InvalidOperationException("Ordinary physical build state readback failed.");
    }
}
