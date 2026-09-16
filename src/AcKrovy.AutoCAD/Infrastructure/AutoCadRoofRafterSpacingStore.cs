using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Stores optional drawing-level automatic-rafter defaults independently from the
/// established annotation-scale payload. Missing data resolves in Core and is not
/// written during a read. SchemaVersion stays 1; payloads with three reals are
/// accepted and resolve MinimumAutomaticLengthMm to the Core default.
/// </summary>
internal sealed class AutoCadRoofRafterSpacingStore
{
    internal const string DrawingSettingsRecordName = "ROOF_RAFTER_SETTINGS";
    private const int DxfInt32Code = (int)DxfCode.Int32;
    private const int DxfRealCode = (int)DxfCode.Real;

    private readonly Database _database;
    private readonly Transaction _transaction;

    public AutoCadRoofRafterSpacingStore(
        Database database,
        Transaction transaction)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    public bool TryRead(out RoofRafterSettings? settings)
    {
        settings = null;
        try
        {
            var root = _transaction.GetObject(
                _database.NamedObjectsDictionaryId,
                OpenMode.ForRead) as DBDictionary;
            if (root is null ||
                !root.Contains(AutoCadDrawingAnnotationScaleStore.ApplicationDictionaryName))
            {
                return false;
            }

            var applicationDictionary = _transaction.GetObject(
                root.GetAt(AutoCadDrawingAnnotationScaleStore.ApplicationDictionaryName),
                OpenMode.ForRead) as DBDictionary;
            if (applicationDictionary is null ||
                !applicationDictionary.Contains(DrawingSettingsRecordName))
            {
                return false;
            }

            var record = _transaction.GetObject(
                applicationDictionary.GetAt(DrawingSettingsRecordName),
                OpenMode.ForRead) as Xrecord;
            var data = record?.Data;
            if (data is null)
            {
                return false;
            }

            using (data)
            {
                return TryParsePayload(data.AsArray(), out settings);
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            settings = null;
            return false;
        }
    }

    public void Write(RoofRafterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!RoofRafterSpacingRules.IsValidSettings(
                settings.DefaultAutomaticSpacingMm,
                settings.MinimumAutomaticSpacingMm,
                settings.MinimumAutomaticLengthMm))
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }
        if (TryRead(out var existing) && existing == settings)
        {
            return;
        }

        var root = (DBDictionary)_transaction.GetObject(
            _database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        var applicationDictionary = GetOrCreateApplicationDictionary(root);
        var record = GetOrCreateRecord(applicationDictionary);
        var reals = RoofRafterSettingsPayload.EncodeReals(settings);
        using var data = new ResultBuffer(
            new TypedValue(DxfInt32Code, RoofRafterSettingsPayload.SchemaVersion),
            new TypedValue(DxfRealCode, reals[0]),
            new TypedValue(DxfRealCode, reals[1]),
            new TypedValue(DxfRealCode, reals[2]));
        record.Data = data;
    }

    public static RoofRafterSettings ReadEffective(
        Database database,
        out bool hasStoredValue)
    {
        ArgumentNullException.ThrowIfNull(database);
        using var transaction = database.TransactionManager.StartOpenCloseTransaction();
        var store = new AutoCadRoofRafterSpacingStore(database, transaction);
        hasStoredValue = store.TryRead(out var stored);
        return RoofRafterSpacingRules.Resolve(hasStoredValue, stored);
    }

    public static RafterLayoutParameters CreateLayoutParameters(
        Database database,
        double maximumSpacingMm,
        double rafterPlanWidthMm)
    {
        var settings = ReadEffective(database, out _);
        return new RafterLayoutParameters(
            MaximumSpacingMm: maximumSpacingMm,
            RafterPlanWidthMm: rafterPlanWidthMm,
            MinimumAutomaticLengthMm: settings.MinimumAutomaticLengthMm);
    }

    private DBDictionary GetOrCreateApplicationDictionary(DBDictionary root)
    {
        var name = AutoCadDrawingAnnotationScaleStore.ApplicationDictionaryName;
        if (root.Contains(name))
        {
            return _transaction.GetObject(
                root.GetAt(name),
                OpenMode.ForRead) as DBDictionary
                ?? throw new InvalidOperationException(
                    $"NOD entry '{name}' is not a dictionary.");
        }

        if (!root.IsWriteEnabled)
        {
            root.UpgradeOpen();
        }
        var dictionary = new DBDictionary();
        root.SetAt(name, dictionary);
        _transaction.AddNewlyCreatedDBObject(dictionary, true);
        return dictionary;
    }

    private Xrecord GetOrCreateRecord(DBDictionary applicationDictionary)
    {
        if (applicationDictionary.Contains(DrawingSettingsRecordName))
        {
            return _transaction.GetObject(
                applicationDictionary.GetAt(DrawingSettingsRecordName),
                OpenMode.ForWrite) as Xrecord
                ?? throw new InvalidOperationException(
                    $"NOD entry '{DrawingSettingsRecordName}' is not an XRecord.");
        }

        if (!applicationDictionary.IsWriteEnabled)
        {
            applicationDictionary.UpgradeOpen();
        }
        var record = new Xrecord();
        applicationDictionary.SetAt(DrawingSettingsRecordName, record);
        _transaction.AddNewlyCreatedDBObject(record, true);
        return record;
    }

    private static bool TryParsePayload(
        IReadOnlyList<TypedValue> values,
        out RoofRafterSettings? settings)
    {
        settings = null;
        // Payload: version + 2 or 3 reals. AutoCAD may surface Int32 DXF values as
        // Int16/Int32; accept both so a successful Write is not lost on read-back.
        if (values.Count is not (3 or 4) ||
            values[0].TypeCode != DxfInt32Code ||
            values[1].TypeCode != DxfRealCode ||
            values[2].TypeCode != DxfRealCode ||
            !TryReadSchemaVersion(values[0].Value, out var version) ||
            values[1].Value is not double defaultSpacing ||
            values[2].Value is not double minimumSpacing)
        {
            return false;
        }

        double[] reals;
        if (values.Count == 3)
        {
            reals = [defaultSpacing, minimumSpacing];
        }
        else
        {
            if (values[3].TypeCode != DxfRealCode ||
                values[3].Value is not double minimumLength)
            {
                return false;
            }

            reals = [defaultSpacing, minimumSpacing, minimumLength];
        }

        return RoofRafterSettingsPayload.TryDecode(version, reals, out settings);
    }

    private static bool TryReadSchemaVersion(object? value, out int version)
    {
        switch (value)
        {
            case int int32:
                version = int32;
                return true;
            case short int16:
                version = int16;
                return true;
            case long int64 when int64 is >= int.MinValue and <= int.MaxValue:
                version = (int)int64;
                return true;
            default:
                version = 0;
                return false;
        }
    }
}
