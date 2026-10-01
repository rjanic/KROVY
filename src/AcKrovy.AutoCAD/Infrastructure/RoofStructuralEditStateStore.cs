using System.Text.Json;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Owner-local, transaction/undo governed semantic state. Missing means canonical.</summary>
internal static class RoofStructuralEditStateStore
{
    internal const string RecordName = "AK_ROOF_STRUCTURAL_EDITS";

    public static RoofStructuralEditState Read(Polyline owner, Transaction transaction)
    {
        if (owner.ExtensionDictionary.IsNull) return RoofStructuralEditState.Empty;
        var dictionary = (DBDictionary)transaction.GetObject(owner.ExtensionDictionary, OpenMode.ForRead);
        if (!dictionary.Contains(RecordName)) return RoofStructuralEditState.Empty;
        var record = (Xrecord)transaction.GetObject(dictionary.GetAt(RecordName), OpenMode.ForRead);
        using var buffer = record.Data;
        var values = buffer?.AsArray() ?? throw new InvalidOperationException("Structural edit payload missing.");
        if (values.Any(value => value.TypeCode != (int)DxfCode.Text || value.Value is not string))
            throw new InvalidOperationException("Structural edit payload malformed.");
        var state = JsonSerializer.Deserialize<RoofStructuralEditState>(string.Concat(values.Select(value => (string)value.Value)));
        if (!RoofStructuralEditRules.IsValid(state)) throw new InvalidOperationException("Structural edit state invalid.");
        return state!;
    }

    public static void Write(Polyline owner, Transaction transaction, RoofStructuralEditState state)
    {
        if (!RoofStructuralEditRules.IsValid(state)) throw new ArgumentException("Invalid structural edit state.");
        if (!owner.IsWriteEnabled) owner.UpgradeOpen();
        if (owner.ExtensionDictionary.IsNull) owner.CreateExtensionDictionary();
        var dictionary = (DBDictionary)transaction.GetObject(owner.ExtensionDictionary, OpenMode.ForWrite);
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
        var values = Enumerable.Range(0, (json.Length + 249) / 250)
            .Select(index => new TypedValue((int)DxfCode.Text, json.Substring(index * 250, Math.Min(250, json.Length - index * 250)))).ToArray();
        using var buffer = new ResultBuffer(values);
        record.Data = buffer;
    }
}
