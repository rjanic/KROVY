using AcKrovy.Cad.Abstractions.Drawing;
using AcKrovy.Core.Models;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed class AutoCadDrawingAnnotationScaleStore : IDrawingAnnotationScaleStore
{
    internal const string ApplicationDictionaryName = "ACAD_KROVY";
    internal const string DrawingSettingsRecordName = "DRAWING_SETTINGS";

    private const int DxfInt32Code = (int)DxfCode.Int32;

    private readonly Database _database;
    private readonly Transaction _transaction;

    public AutoCadDrawingAnnotationScaleStore(
        Database database,
        Transaction transaction)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    public bool TryRead(out int denominator)
    {
        denominator = default;

        try
        {
            var root = _transaction.GetObject(
                _database.NamedObjectsDictionaryId,
                OpenMode.ForRead) as DBDictionary;
            if (root is null || !root.Contains(ApplicationDictionaryName))
            {
                return false;
            }

            var applicationDictionary = _transaction.GetObject(
                root.GetAt(ApplicationDictionaryName),
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
                return TryParsePayload(data.AsArray(), out denominator);
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            denominator = default;
            return false;
        }
    }

    public void Write(int denominator)
    {
        var settings = TimberDrawingSettings.Create(denominator);
        if (TryRead(out var existingDenominator) &&
            existingDenominator == settings.AnnotationScaleDenominator)
        {
            return;
        }

        var root = (DBDictionary)_transaction.GetObject(
            _database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        var applicationDictionary = GetOrCreateApplicationDictionary(root);
        var record = GetOrCreateDrawingSettingsRecord(applicationDictionary);

        // Deterministic XRecord payload:
        // 1. Int32 drawing-settings schema version.
        // 2. Int32 normalized annotation-scale denominator.
        using var data = new ResultBuffer(
            new TypedValue(DxfInt32Code, settings.SchemaVersion),
            new TypedValue(
                DxfInt32Code,
                settings.AnnotationScaleDenominator));
        record.Data = data;
    }

    public void Remove()
    {
        var root = _transaction.GetObject(
            _database.NamedObjectsDictionaryId,
            OpenMode.ForRead) as DBDictionary;
        if (root is null || !root.Contains(ApplicationDictionaryName))
        {
            return;
        }

        var applicationDictionary = _transaction.GetObject(
            root.GetAt(ApplicationDictionaryName),
            OpenMode.ForRead) as DBDictionary;
        if (applicationDictionary is null ||
            !applicationDictionary.Contains(DrawingSettingsRecordName))
        {
            return;
        }

        if (!applicationDictionary.IsWriteEnabled)
        {
            applicationDictionary.UpgradeOpen();
        }

        applicationDictionary.Remove(DrawingSettingsRecordName);
    }

    private DBDictionary GetOrCreateApplicationDictionary(DBDictionary root)
    {
        if (root.Contains(ApplicationDictionaryName))
        {
            return _transaction.GetObject(
                root.GetAt(ApplicationDictionaryName),
                OpenMode.ForRead) as DBDictionary
                ?? throw new InvalidOperationException(
                    $"NOD entry '{ApplicationDictionaryName}' is not a dictionary.");
        }

        if (!root.IsWriteEnabled)
        {
            root.UpgradeOpen();
        }

        var dictionary = new DBDictionary();
        root.SetAt(ApplicationDictionaryName, dictionary);
        _transaction.AddNewlyCreatedDBObject(dictionary, true);
        return dictionary;
    }

    private Xrecord GetOrCreateDrawingSettingsRecord(
        DBDictionary applicationDictionary)
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
        out int denominator)
    {
        denominator = default;
        if (values.Count != 2 ||
            values[0].TypeCode != DxfInt32Code ||
            values[1].TypeCode != DxfInt32Code ||
            values[0].Value is not int schemaVersion ||
            values[1].Value is not int storedDenominator ||
            !TimberDrawingSettings.TryFromStoredValues(
                schemaVersion,
                storedDenominator,
                out var settings))
        {
            return false;
        }

        denominator = settings!.AnnotationScaleDenominator;
        return true;
    }
}
