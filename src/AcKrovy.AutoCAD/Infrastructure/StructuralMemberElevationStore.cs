using System.Text.Json;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Additive XRecord persistence for StructuralMemberElevationState.
/// Uses the Line's ExtensionDictionary (same pattern as RoofOrdinaryPhysicalBuildStateStore).
/// Key: ACAD_KROVY_MEMBER_ELEVATION_V1.
///
/// Schema v2 adds CalculationMode (C). Legacy v1 payloads without C default to LowerUpper.
/// Does NOT persist slope (derived on demand from AxisStart/AxisEnd + plan length).
/// </summary>
internal static class StructuralMemberElevationStore
{
    internal const string RecordName = "ACAD_KROVY_MEMBER_ELEVATION_V1";

    internal static bool HasRecord(Line line, Transaction transaction) =>
        !line.ExtensionDictionary.IsNull &&
        ((DBDictionary)transaction.GetObject(line.ExtensionDictionary, OpenMode.ForRead))
            .Contains(RecordName);

    public static StructuralMemberElevationState? Read(Line line, Transaction transaction)
    {
        if (line.ExtensionDictionary.IsNull) return null;
        var dictionary = (DBDictionary)transaction.GetObject(line.ExtensionDictionary, OpenMode.ForRead);
        if (!dictionary.Contains(RecordName)) return null;
        var record = (Xrecord)transaction.GetObject(dictionary.GetAt(RecordName), OpenMode.ForRead);
        using var data = record.Data;
        if (data is null) return null;
        var values = data.AsArray();
        if (values.Length == 0 || values.Any(v => v.TypeCode != (int)DxfCode.Text)) return null;
        try
        {
            var json = string.Concat(values.Select(v => v.Value as string));
            var state = JsonSerializer.Deserialize<ElevationPayload>(json);
            if (state is null) return null;
            return ToState(state);
        }
        catch (JsonException) { return null; }
    }

    public static void Write(Line line, Transaction transaction, StructuralMemberElevationState state)
    {
        var normalized = NormalizeForWrite(state);
        if (!line.IsWriteEnabled || normalized is null || !StructuralMemberElevationRules.IsValid(normalized))
            throw new InvalidOperationException("StructuralMemberElevationState cannot be written: invalid or read-only.");
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
        var payload = FromState(normalized);
        var json = JsonSerializer.Serialize(payload);
        var chunks = new List<TypedValue>();
        for (var offset = 0; offset < json.Length; offset += 240)
            chunks.Add(new TypedValue((int)DxfCode.Text,
                json.Substring(offset, Math.Min(240, json.Length - offset))));
        using var buffer = new ResultBuffer(chunks.ToArray());
        record.Data = buffer;
        var readback = Read(line, transaction);
        if (!StateEquals(readback, normalized))
            throw new InvalidOperationException("StructuralMemberElevationState readback verification failed.");
    }

    public static void Erase(Line line, Transaction transaction)
    {
        if (line.ExtensionDictionary.IsNull) return;
        var dictionary = (DBDictionary)transaction.GetObject(line.ExtensionDictionary, OpenMode.ForRead);
        if (!dictionary.Contains(RecordName)) return;
        dictionary.UpgradeOpen();
        var id = dictionary.GetAt(RecordName);
        dictionary.Remove(RecordName);
        var record = (Xrecord)transaction.GetObject(id, OpenMode.ForWrite);
        record.Erase();
    }

    private sealed class ElevationPayload
    {
        public int V { get; set; }
        public int B { get; set; }
        public double A0 { get; set; }
        public double A1 { get; set; }
        public int R { get; set; }
        /// <summary>CalculationMode. Absent on legacy v1 → LowerUpper.</summary>
        public int? C { get; set; }
    }

    private static ElevationPayload FromState(StructuralMemberElevationState s) => new()
    {
        V = StructuralMemberElevationStateSchema.CurrentVersion,
        B = (int)s.Behavior,
        A0 = s.AxisStartElevationMm,
        A1 = s.AxisEndElevationMm,
        R = (int)s.DisplayReference,
        C = (int)s.CalculationMode,
    };

    private static StructuralMemberElevationState? ToState(ElevationPayload p)
    {
        if (p.V != StructuralMemberElevationStateSchema.CurrentVersion &&
            p.V != StructuralMemberElevationStateSchema.LegacyVersionWithoutCalculationMode)
            return null;
        if (!Enum.IsDefined(typeof(StructuralMemberElevationBehavior), p.B)) return null;
        if (!Enum.IsDefined(typeof(StructuralMemberElevationReferenceKind), p.R)) return null;
        var mode = StructuralMemberElevationCalculationMode.LowerUpper;
        if (p.C is int storedMode)
        {
            if (!Enum.IsDefined(typeof(StructuralMemberElevationCalculationMode), storedMode))
                return null;
            mode = (StructuralMemberElevationCalculationMode)storedMode;
        }

        var state = new StructuralMemberElevationState(
            StructuralMemberElevationStateSchema.CurrentVersion,
            (StructuralMemberElevationBehavior)p.B,
            p.A0,
            p.A1,
            (StructuralMemberElevationReferenceKind)p.R,
            mode);
        return StructuralMemberElevationRules.IsValid(state) ? state : null;
    }

    private static StructuralMemberElevationState? NormalizeForWrite(StructuralMemberElevationState state) =>
        state with { SchemaVersion = StructuralMemberElevationStateSchema.CurrentVersion };

    private static bool StateEquals(StructuralMemberElevationState? a, StructuralMemberElevationState? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.SchemaVersion == b.SchemaVersion &&
               a.Behavior == b.Behavior &&
               a.DisplayReference == b.DisplayReference &&
               a.CalculationMode == b.CalculationMode &&
               Math.Abs(a.AxisStartElevationMm - b.AxisStartElevationMm) < 1e-9 &&
               Math.Abs(a.AxisEndElevationMm - b.AxisEndElevationMm) < 1e-9;
    }
}
