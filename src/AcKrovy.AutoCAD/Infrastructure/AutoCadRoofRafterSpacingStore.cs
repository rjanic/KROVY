using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Stores optional drawing-level automatic-rafter defaults independently from the
/// established annotation-scale payload. Missing data resolves in Core and is not
/// written during a read.
/// </summary>
internal sealed class AutoCadRoofRafterSpacingStore
{
    internal const string DrawingSettingsRecordName = "ROOF_RAFTER_SETTINGS";
    private const int SchemaVersion = 1;
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
                settings.MinimumAutomaticSpacingMm))
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
        using var data = new ResultBuffer(
            new TypedValue(DxfInt32Code, SchemaVersion),
            new TypedValue(DxfRealCode, settings.DefaultAutomaticSpacingMm),
            new TypedValue(DxfRealCode, settings.MinimumAutomaticSpacingMm));
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
        if (values.Count != 3 ||
            values[0].TypeCode != DxfInt32Code ||
            values[1].TypeCode != DxfRealCode ||
            values[2].TypeCode != DxfRealCode ||
            values[0].Value is not int version ||
            version != SchemaVersion ||
            values[1].Value is not double defaultSpacing ||
            values[2].Value is not double minimumSpacing ||
            !RoofRafterSpacingRules.IsValidSettings(defaultSpacing, minimumSpacing))
        {
            return false;
        }

        settings = new RoofRafterSettings(defaultSpacing, minimumSpacing);
        return true;
    }
}
