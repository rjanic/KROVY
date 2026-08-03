#if DEBUG
using System.Globalization;
using System.Text;
using System.Text.Json;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AutoCadFullLabelProofService
{
    private const int XDataRegAppCode = 1001;
    private const int XDataStringCode = 1000;
    private const int XRecordStringCode = 1;
    private const int XRecordChunkLength = 240;
    private const double Tolerance = 0.001d;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public static void Create()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var database = document.Database;
        var editor = document.Editor;
        try
        {
            ObjectId refreshedLabelId;
            using (document.LockDocument())
            {
                using var transaction =
                    database.TransactionManager.StartTransaction();
                var modelSpace = OpenModelSpace(
                    database,
                    transaction,
                    OpenMode.ForRead);
                var existing = modelSpace
                    .Cast<ObjectId>()
                    .Select(id => transaction.GetObject(
                        id,
                        OpenMode.ForRead,
                        false))
                    .OfType<Entity>()
                    .Where(entity =>
                        !entity.IsErased &&
                        entity.OwnerId == modelSpace.ObjectId)
                    .ToArray();
                if (existing.Length != 0)
                {
                    editor.WriteMessage(
                        $"\nAK_DEV_FULLLABEL_TEXT_CREATE: FAIL - exact " +
                        $"ModelSpace contains {existing.Length} real entities. " +
                        "Layouts, paper-space objects, AEC dictionaries and " +
                        "nested block definitions were not counted.");
                    return;
                }

                var defaultProfile = TimberElementDefaultProfile.CreateDefault();
                var batch = AutoCadAnnotationPresentationBatchContext.Create(
                    database,
                    transaction,
                    defaultProfile);
                if (batch.TextStyleCatalog.CompatibleStyles.Count == 0)
                {
                    editor.WriteMessage(
                        "\nAK_DEV_FULLLABEL_TEXT_CREATE: NOT TESTED - " +
                        "Architecture DWG has no compatible variable-height " +
                        "nonannotative text style. No proof entity was created.");
                    return;
                }

                var styles = batch.TextStyleCatalog.CompatibleStyles
                    .Take(2)
                    .ToArray();
                EnsureRegApp(database, transaction);
                modelSpace.UpgradeOpen();

                var expected = new List<AutoCadFullLabelProofExpectedCase>();
                ObjectId? caseALabelId = null;
                for (var index = 0;
                     index < AutoCadFullLabelProofPolicy.Cases.Count;
                     index++)
                {
                    var proofCase = AutoCadFullLabelProofPolicy.Cases[index];
                    var styleName = ResolveStyleName(proofCase, styles);
                    var source = CreateSource(
                        database,
                        transaction,
                        modelSpace,
                        proofCase,
                        index);
                    WriteMarker(source, proofCase.Token);

                    var data = CreateData(proofCase, styleName, defaultProfile);
                    var presentation = batch.ResolveForElement(data);
                    if (!presentation.HasCompatibleStyle)
                    {
                        editor.WriteMessage(
                            $"\nAK_DEV_FULLLABEL_TEXT_CREATE: NOT TESTED - " +
                            $"case {proofCase.Token} has NoCompatibleStyle.");
                        return;
                    }

                    TimberAnnotationService.EnsureForElement(
                        database,
                        transaction,
                        source,
                        data,
                        batch);

                    var label = FindFullLabel(
                        modelSpace,
                        transaction,
                        source.Handle.ToString());
                    if (label is null)
                    {
                        throw new InvalidOperationException(
                            $"FullLabel MText was not created for {proofCase.Token}.");
                    }

                    if (proofCase.Token == "A")
                    {
                        caseALabelId = label.ObjectId;
                    }

                    expected.Add(AutoCadFullLabelProofPolicy.ToExpected(
                        proofCase,
                        presentation.ResolvedTextStyleName ?? styleName,
                        presentation.EffectiveTextSettings
                            .DimensionPaperHeightMm,
                        presentation.AnnotationScaleDenominator,
                        presentation.TextStyleResolutionKind.ToString(),
                        presentation.IsFallback));

                    editor.WriteMessage(
                        $"\n  {proofCase.Token}: style=" +
                        $"{presentation.ResolvedTextStyleName}; " +
                        $"TextStyleId={presentation.ResolvedTextStyleId}; " +
                        $"paper={presentation.EffectiveTextSettings.DimensionPaperHeightMm:R}; " +
                        $"denominator={presentation.AnnotationScaleDenominator}; " +
                        $"modelHeight={presentation.LabelAndDimensionModelHeight:R}; " +
                        $"kind={presentation.TextStyleResolutionKind}");
                }

                if (caseALabelId is null)
                {
                    throw new InvalidOperationException(
                        "Refresh case A label ObjectId was not captured.");
                }

                var refreshSource = FindMarkedSource(
                    modelSpace,
                    transaction,
                    "A") ??
                    throw new InvalidOperationException(
                        "Refresh source A was not found.");
                var refreshData = CreateData(
                    AutoCadFullLabelProofPolicy.Cases[0],
                    ResolveStyleName(
                        AutoCadFullLabelProofPolicy.Cases[0],
                        styles),
                    defaultProfile);
                TimberAnnotationService.EnsureForElement(
                    database,
                    transaction,
                    refreshSource,
                    refreshData,
                    batch);
                var refreshed = FindFullLabel(
                    modelSpace,
                    transaction,
                    refreshSource.Handle.ToString());
                if (refreshed is null || refreshed.ObjectId != caseALabelId)
                {
                    throw new InvalidOperationException(
                        "FullLabel refresh did not preserve the same MText ObjectId.");
                }

                refreshedLabelId = caseALabelId.Value;
                var manifest = new AutoCadFullLabelProofManifest(
                    AutoCadFullLabelProofPolicy.SchemaVersion,
                    AutoCadFullLabelProofPolicy.SuiteIdentifier,
                    expected);
                WriteManifest(database, transaction, manifest);
                transaction.Commit();
            }

            using var readTransaction =
                database.TransactionManager.StartOpenCloseTransaction();
            var passed = VerifyCore(
                database,
                readTransaction,
                editor,
                requireRefreshObjectId: refreshedLabelId);
            editor.WriteMessage(
                passed
                    ? "\nAK_DEV_FULLLABEL_TEXT_CREATE: PASS - production " +
                      "FullLabel presentation verified after commit."
                    : "\nAK_DEV_FULLLABEL_TEXT_CREATE: FAIL - post-commit " +
                      "readback did not match the expected presentation.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_FULLLABEL_TEXT_CREATE: FAIL - " +
                $"{exception.GetType().Name}: {exception.Message}. " +
                "Partial entities were not committed; the single CREATE " +
                "transaction was rolled back. No completed proof manifest " +
                "was written.");
        }
    }

    public static void Verify()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var database = document.Database;
        var editor = document.Editor;
        try
        {
            var dbmodBefore = Convert.ToInt32(
                AcApplication.GetSystemVariable("DBMOD"));
            using var transaction =
                database.TransactionManager.StartOpenCloseTransaction();
            var passed = VerifyCore(
                database,
                transaction,
                editor,
                requireRefreshObjectId: null);
            var dbmodAfter = Convert.ToInt32(
                AcApplication.GetSystemVariable("DBMOD"));
            editor.WriteMessage(
                passed
                    ? $"\nAK_DEV_FULLLABEL_TEXT_VERIFY: PASS - read-only; " +
                      $"DBMOD before={dbmodBefore}, after={dbmodAfter}."
                    : $"\nAK_DEV_FULLLABEL_TEXT_VERIFY: FAIL - " +
                      $"DBMOD before={dbmodBefore}, after={dbmodAfter}.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_FULLLABEL_TEXT_VERIFY: FAIL - " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    public static void Clean()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var database = document.Database;
        var editor = document.Editor;
        try
        {
            using (document.LockDocument())
            {
                using var transaction =
                    database.TransactionManager.StartTransaction();
                var modelSpace = OpenModelSpace(
                    database,
                    transaction,
                    OpenMode.ForWrite);
                var removed = 0;
                foreach (ObjectId id in modelSpace)
                {
                    if (transaction.GetObject(id, OpenMode.ForRead, false)
                            is not Entity entity ||
                        entity.IsErased ||
                        entity.OwnerId != modelSpace.ObjectId ||
                        !HasMarker(entity))
                    {
                        continue;
                    }

                    entity.UpgradeOpen();
                    entity.Erase();
                    removed++;
                }

                foreach (ObjectId id in modelSpace)
                {
                    if (transaction.GetObject(id, OpenMode.ForRead, false)
                            is not Entity entity ||
                        entity.IsErased ||
                        entity.OwnerId != modelSpace.ObjectId)
                    {
                        continue;
                    }

                    if (!ElementLabelStore.TryRead(entity, out var data) ||
                        data is null)
                    {
                        continue;
                    }

                    entity.UpgradeOpen();
                    entity.Erase();
                    removed++;
                }

                DeleteManifest(database, transaction);
                transaction.Commit();
                editor.WriteMessage(
                    $"\nAK_DEV_FULLLABEL_TEXT_CLEAN: PASS - removed " +
                    $"{removed} proof-related ModelSpace entities.");
            }
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_FULLLABEL_TEXT_CLEAN: FAIL - " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool VerifyCore(
        Database database,
        Transaction transaction,
        Editor editor,
        ObjectId? requireRefreshObjectId)
    {
        var manifest = ReadManifest(database, transaction);
        if (manifest is null)
        {
            editor.WriteMessage(
                "\n  missing FullLabel proof manifest");
            return false;
        }

        if (manifest.SchemaVersion !=
                AutoCadFullLabelProofPolicy.SchemaVersion ||
            !string.Equals(
                manifest.SuiteIdentifier,
                AutoCadFullLabelProofPolicy.SuiteIdentifier,
                StringComparison.Ordinal))
        {
            editor.WriteMessage("\n  unexpected FullLabel proof manifest");
            return false;
        }

        var modelSpace = OpenModelSpace(
            database,
            transaction,
            OpenMode.ForRead);
        var passed = true;
        foreach (var expected in manifest.Cases)
        {
            var source = FindMarkedSource(
                modelSpace,
                transaction,
                expected.Token);
            if (source is null)
            {
                editor.WriteMessage($"\n  {expected.Token}: FAIL - source missing");
                passed = false;
                continue;
            }

            var label = FindFullLabel(
                modelSpace,
                transaction,
                source.Handle.ToString());
            if (label is null)
            {
                editor.WriteMessage($"\n  {expected.Token}: FAIL - MText missing");
                passed = false;
                continue;
            }

            var styleName = ResolveStyleName(database, transaction, label.TextStyleId);
            var styleMatches = string.Equals(
                styleName,
                expected.StyleName,
                StringComparison.OrdinalIgnoreCase);
            var heightMatches = AreClose(label.TextHeight, expected.ModelHeightMm);
            var casePassed = styleMatches && heightMatches;
            if (expected.Token == "A" &&
                requireRefreshObjectId is ObjectId refreshId)
            {
                casePassed = casePassed && label.ObjectId == refreshId;
            }

            editor.WriteMessage(
                $"\n  {expected.Token}: style={styleName}; " +
                $"TextStyleId={label.TextStyleId.Handle}; " +
                $"paper={expected.PaperHeightMm:R}; " +
                $"denominator={expected.Denominator}; " +
                $"modelHeight={label.TextHeight:R}; " +
                $"expectedHeight={expected.ModelHeightMm:R}; " +
                $"kind={expected.ResolutionKind}; " +
                (casePassed ? "PASS" : "FAIL"));
            passed &= casePassed;
        }

        return passed;
    }

    private static Entity CreateSource(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        AutoCadFullLabelProofCase proofCase,
        int index)
    {
        Entity source;
        if (proofCase.IsPostFootprint)
        {
            var size = AutoCadFullLabelProofPolicy.PostFootprintSizeMm;
            var polyline = new Polyline();
            polyline.AddVertexAt(0, new Point2d(index * 2500d, 2000d), 0d, 0d, 0d);
            polyline.AddVertexAt(1, new Point2d(index * 2500d + size, 2000d), 0d, 0d, 0d);
            polyline.AddVertexAt(2, new Point2d(index * 2500d + size, 2000d + size), 0d, 0d, 0d);
            polyline.AddVertexAt(3, new Point2d(index * 2500d, 2000d + size), 0d, 0d, 0d);
            polyline.Closed = true;
            source = polyline;
        }
        else
        {
            source = new Line(
                new Point3d(index * 2500d, 0d, 0d),
                new Point3d(index * 2500d + 1200d, 0d, 0d));
        }

        source.SetDatabaseDefaults(database);
        modelSpace.AppendEntity(source);
        transaction.AddNewlyCreatedDBObject(source, true);
        return source;
    }

    private static TimberElementData CreateData(
        AutoCadFullLabelProofCase proofCase,
        string styleName,
        TimberElementDefaultProfile defaultProfile)
    {
        TimberAnnotationTextSettings? settings = proofCase.TextSettings;
        if (settings is not null)
        {
            settings = settings with { TextStyleName = styleName };
        }

        if (proofCase.IsPostFootprint)
        {
            return AutoCadFullLabelProofPolicy.CreatePostFootprintElementData(
                $"FL-{proofCase.Token}",
                settings,
                proofCase.DenominatorOverride,
                defaultProfile);
        }

        return new TimberElementData
        {
            SchemaVersion = TimberElementDataSchema.CurrentVersion,
            ElementId = $"FL-{proofCase.Token}",
            ElementType = TimberElementType.Rafter,
            WidthMm = 80d,
            HeightMm = 160d,
            AnnotationMode = TimberAnnotationMode.FullLabel,
            ItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain,
            AnnotationTextSettings = settings,
            AnnotationScaleDenominatorOverride =
                proofCase.DenominatorOverride,
            SlopeDegrees = 35d,
            RoofPlaneId = "AK_DEV",
        };
    }

    private static string ResolveStyleName(
        AutoCadFullLabelProofCase proofCase,
        IReadOnlyList<AutoCadTextStyleCatalogEntry> styles)
    {
        if (proofCase.StyleSlot < 0)
        {
            return styles[0].CanonicalName;
        }

        var slot = Math.Min(proofCase.StyleSlot, styles.Count - 1);
        return styles[slot].CanonicalName;
    }

    private static MText? FindFullLabel(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string sourceHandle)
    {
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is not MText label ||
                label.IsErased ||
                label.OwnerId != modelSpace.ObjectId)
            {
                continue;
            }

            if (!ElementLabelStore.TryRead(label, out var data) ||
                data is null ||
                !string.Equals(
                    data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TimberAnnotationModeRules.GetRepresentation(
                    data.AnnotationMode,
                    data.ItemNumberLeaderStyle) !=
                TimberMainAnnotationRepresentation.FullLabel)
            {
                continue;
            }

            return label;
        }

        return null;
    }

    private static Entity? FindMarkedSource(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string token)
    {
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is not Entity entity ||
                entity.IsErased ||
                entity.OwnerId != modelSpace.ObjectId ||
                !HasMarker(entity, token))
            {
                continue;
            }

            return entity;
        }

        return null;
    }

    private static void WriteMarker(Entity entity, string token)
    {
        entity.XData = new ResultBuffer(
            new TypedValue(
                XDataRegAppCode,
                AutoCadFullLabelProofPolicy.RegAppName),
            new TypedValue(XDataStringCode, token));
    }

    private static bool HasMarker(Entity entity, string? token = null)
    {
        var buffer = entity.XData;
        if (buffer is null)
        {
            return false;
        }

        string? currentApp = null;
        foreach (TypedValue value in buffer)
        {
            if (value.TypeCode == XDataRegAppCode)
            {
                currentApp = Convert.ToString(value.Value, CultureInfo.InvariantCulture);
                continue;
            }

            if (!string.Equals(
                    currentApp,
                    AutoCadFullLabelProofPolicy.RegAppName,
                    StringComparison.OrdinalIgnoreCase) ||
                value.TypeCode != XDataStringCode)
            {
                continue;
            }

            var marker = Convert.ToString(value.Value, CultureInfo.InvariantCulture);
            return token is null ||
                string.Equals(marker, token, StringComparison.Ordinal);
        }

        return false;
    }

    private static void EnsureRegApp(Database database, Transaction transaction)
    {
        var regAppTable = (RegAppTable)transaction.GetObject(
            database.RegAppTableId,
            OpenMode.ForRead);
        if (regAppTable.Has(AutoCadFullLabelProofPolicy.RegAppName))
        {
            return;
        }

        regAppTable.UpgradeOpen();
        var record = new RegAppTableRecord
        {
            Name = AutoCadFullLabelProofPolicy.RegAppName,
        };
        regAppTable.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static BlockTableRecord OpenModelSpace(
        Database database,
        Transaction transaction,
        OpenMode mode)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        return (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            mode);
    }

    private static string ResolveStyleName(
        Database database,
        Transaction transaction,
        ObjectId textStyleId)
    {
        if (textStyleId.IsNull ||
            !AutoCadDatabaseIdentity.IsSame(database, textStyleId))
        {
            return "<invalid>";
        }

        return transaction.GetObject(textStyleId, OpenMode.ForRead, false)
            is TextStyleTableRecord style
            ? style.Name
            : "<missing>";
    }

    private static void WriteManifest(
        Database database,
        Transaction transaction,
        AutoCadFullLabelProofManifest manifest)
    {
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForWrite);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        var record = new Xrecord();
        var values = new ResultBuffer();
        for (var offset = 0; offset < json.Length; offset += XRecordChunkLength)
        {
            var length = Math.Min(XRecordChunkLength, json.Length - offset);
            values.Add(new TypedValue(
                XRecordStringCode,
                json.Substring(offset, length)));
        }

        record.Data = values;
        if (namedObjects.Contains(
                AutoCadFullLabelProofPolicy.ManifestDictionaryKey))
        {
            var existingId = namedObjects.GetAt(
                AutoCadFullLabelProofPolicy.ManifestDictionaryKey);
            var existing = (DBObject)transaction.GetObject(
                existingId,
                OpenMode.ForWrite);
            existing.Erase();
        }

        namedObjects.SetAt(
            AutoCadFullLabelProofPolicy.ManifestDictionaryKey,
            record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static AutoCadFullLabelProofManifest? ReadManifest(
        Database database,
        Transaction transaction)
    {
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!namedObjects.Contains(
                AutoCadFullLabelProofPolicy.ManifestDictionaryKey))
        {
            return null;
        }

        var record = (Xrecord)transaction.GetObject(
            namedObjects.GetAt(
                AutoCadFullLabelProofPolicy.ManifestDictionaryKey),
            OpenMode.ForRead);
        if (record.Data is null)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (TypedValue value in record.Data)
        {
            if (value.TypeCode == XRecordStringCode)
            {
                builder.Append(Convert.ToString(
                    value.Value,
                    CultureInfo.InvariantCulture));
            }
        }

        return JsonSerializer.Deserialize<AutoCadFullLabelProofManifest>(
            builder.ToString(),
            JsonOptions);
    }

    private static void DeleteManifest(
        Database database,
        Transaction transaction)
    {
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForWrite);
        if (!namedObjects.Contains(
                AutoCadFullLabelProofPolicy.ManifestDictionaryKey))
        {
            return;
        }

        var existing = (DBObject)transaction.GetObject(
            namedObjects.GetAt(
                AutoCadFullLabelProofPolicy.ManifestDictionaryKey),
            OpenMode.ForWrite);
        existing.Erase();
    }

    private static bool AreClose(double left, double right) =>
        Math.Abs(left - right) <= Tolerance;
}
#endif
