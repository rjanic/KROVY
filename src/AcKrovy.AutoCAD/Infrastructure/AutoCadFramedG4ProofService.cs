#if DEBUG
using System.Globalization;
using System.Text.Json;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AutoCadFramedG4ProofService
{
    public static void Clean()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        using var documentLock = document.LockDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var erased = EraseMarkedEntities(document.Database, transaction);
        ClearManifest(document.Database, transaction);
        transaction.Commit();
        editor.WriteMessage($"\nAK_DEV_FRAMED_G4_CLEAN: erased={erased}");
    }

    public static void Create()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        var database = document.Database;
        try
        {
            using var documentLock = document.LockDocument();
            using var transaction = database.TransactionManager.StartTransaction();
            EraseMarkedEntities(database, transaction);

            var profile = TimberElementDefaultProfileStore.Load().Normalize();
            var batch = AutoCadAnnotationPresentationBatchContext.Create(
                database,
                transaction,
                profile);
            var origin = new Point3d(20000d, 9000d, 0d);
            var caseIndex = 0;
            foreach (var proofCase in AutoCadFramedG4ProofPolicy.Cases)
            {
                CreateCase(
                    editor,
                    database,
                    transaction,
                    batch,
                    proofCase,
                    origin + new Vector3d(caseIndex * 2500d, 0d, 0d));
                caseIndex++;
            }

            WriteManifest(database, transaction, AutoCadFramedG4ProofPolicy.CreateManifest());
            transaction.Commit();
            editor.WriteMessage(
                $"\nAK_DEV_FRAMED_G4_CREATE: PASS cases={AutoCadFramedG4ProofPolicy.Cases.Count}");
        }
        catch (Exception exception)
        {
            // Diagnostics for the failing case are already printed before this
            // catch (and before the ambient transaction rolls back on dispose).
            editor.WriteMessage(
                $"\nAK_DEV_FRAMED_G4_CREATE: FAIL - {exception.GetType().Name}: {exception.Message}");
            if (exception is Autodesk.AutoCAD.Runtime.Exception acadException)
            {
                editor.WriteMessage($"\n  ErrorStatus={acadException.ErrorStatus}");
            }

            editor.WriteMessage($"\n  stack={exception.StackTrace}");
            editor.WriteMessage(
                "\nAK_DEV_FRAMED_G4_CREATE: manifest NOT written (all cases must succeed first).");
        }
    }

    public static void Verify()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        var database = document.Database;
        var dbmodBefore = Convert.ToInt32(AcApplication.GetSystemVariable("DBMOD"));
        try
        {
            using var transaction =
                database.TransactionManager.StartOpenCloseTransaction();
            if (!TryReadManifest(database, transaction, out var manifest) ||
                manifest is null)
            {
                editor.WriteMessage(
                    "\nAK_DEV_FRAMED_G4_VERIFY: NOT RUN - Missing G4 proof manifest " +
                    "(expected after failed CREATE; not a separate product bug).");
                return;
            }

            var sharedCircleFrameName = string.Empty;
            foreach (var proofCase in manifest.Cases)
            {
                VerifyCase(editor, database, transaction, proofCase, ref sharedCircleFrameName);
            }

            var dbmodAfter = Convert.ToInt32(AcApplication.GetSystemVariable("DBMOD"));
            editor.WriteMessage(
                $"\nAK_DEV_FRAMED_G4_VERIFY: PASS dbmodBefore={dbmodBefore} dbmodAfter={dbmodAfter}");
        }
        catch (Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_FRAMED_G4_VERIFY: FAIL - {exception.GetType().Name}: {exception.Message}");
        }
    }

    public static void MigrateCreate()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        editor.WriteMessage(
            "\nAK_DEV_FRAMED_G4_MIGRATE_CREATE: create legacy G2/G3 via production " +
            "refresh after temporarily loading framed elements, then run " +
            "AK_DEV_FRAMED_G4_MIGRATE_VERIFY. Host should create G3 leaders first " +
            "with an older build if available; current build migrates on refresh.");
        Create();
        editor.WriteMessage(
            "\nAK_DEV_FRAMED_G4_MIGRATE_CREATE: seeded current G4 baseline for " +
            "idempotent second-refresh checks.");
    }

    public static void MigrateVerify()
    {
        Verify();
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        document?.Editor.WriteMessage(
            "\nAK_DEV_FRAMED_G4_MIGRATE_VERIFY: re-run production AK_LABELS then " +
            "VERIFY again; expect no duplicate CircleText entities and unchanged " +
            "G3 block definition names if present.");
    }

    private static void CreateCase(
        Editor editor,
        Database database,
        Transaction transaction,
        AutoCadAnnotationPresentationBatchContext batch,
        AutoCadFramedG4ProofCase proofCase,
        Point3d origin)
    {
        var attachDiagnostics = string.Equals(
            proofCase.Token,
            "F",
            StringComparison.Ordinal);
        using var diagnosticsScope = attachDiagnostics
            ? AutoCadFramedG4HostDiagnostics.Attach(message =>
                editor.WriteMessage($"\n{message}"))
            : null;

        var frameSize = TimberItemLeaderBlockDefinitionRules
            .Resolve(proofCase.FrameKind, proofCase.ItemText)
            .Size;
        var annotationMode = proofCase.Combined
            ? TimberAnnotationMode.DimensionsWithItemNumber
            : TimberAnnotationMode.ItemNumberLeader;
        if (attachDiagnostics)
        {
            editor.WriteMessage(
                $"\nAK_DEV_FRAMED_G4_CREATE Case F identity: " +
                $"scenario=combined-framed-G4 FrameKind={proofCase.FrameKind} " +
                $"FrameSize={frameSize} AnnotationMode={annotationMode} " +
                $"ItemCodeStyle={proofCase.StyleName} " +
                $"paperHeightMm={proofCase.PaperHeightMm.ToString(CultureInfo.InvariantCulture)} " +
                $"denominator={proofCase.Denominator} " +
                $"ItemText={proofCase.ItemText} sourceEntity=Line");
        }

        var line = new Line(origin, origin + new Vector3d(1200d, 0d, 0d));
        var modelSpace = OpenModelSpace(database, transaction, OpenMode.ForWrite);
        modelSpace.AppendEntity(line);
        transaction.AddNewlyCreatedDBObject(line, true);

        var data = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = proofCase.ItemText,
            AnnotationMode = annotationMode,
            ItemNumberLeaderStyle = proofCase.FrameKind,
            AnnotationScaleDenominatorOverride = proofCase.Denominator,
            AnnotationTextSettings = TimberAnnotationTextSettings.Shared(
                proofCase.StyleName,
                proofCase.PaperHeightMm,
                dimensionPaperHeightMm: 2.5d,
                slopePaperHeightMm: 1.6d),
        };

        ElementDataStore.Write(line, transaction, data);
        MarkProofEntity(line, transaction, proofCase.Token);

        bool ensured;
        try
        {
            ensured = TimberAnnotationService.EnsureForElement(
                database,
                transaction,
                line,
                data,
                batch);
        }
        catch (Exception exception)
        {
            if (attachDiagnostics)
            {
                AutoCadFramedG4HostDiagnostics.Fail(
                    "F.09",
                    "EnsureForElement threw",
                    exception,
                    sourceId: line.ObjectId,
                    sourceHandle: line.Handle.ToString(),
                    expectedHeight: AutoCadFramedG4ProofPolicy.ExpectedModelHeightMm(proofCase));
                AutoCadFramedG4HostDiagnostics.Outcome("FAILED", exception.Message);
            }

            throw;
        }

        if (!ensured)
        {
            if (attachDiagnostics)
            {
                AutoCadFramedG4HostDiagnostics.Outcome(
                    "FAILED",
                    "EnsureForElement returned false (see STEP diagnostics above)");
                PrintCaseFReadback(editor, database, transaction, line, proofCase);
            }

            throw new InvalidOperationException(
                $"Case {proofCase.Token}: EnsureForElement failed.");
        }

        if (attachDiagnostics)
        {
            AutoCadFramedG4HostDiagnostics.Step(
                "F.09",
                "commit/readback after EnsureForElement success");
            PrintCaseFReadback(editor, database, transaction, line, proofCase);
        }

        MarkCreatedAnnotations(database, transaction, line.Handle.ToString(), proofCase.Token);
    }

    private static void PrintCaseFReadback(
        Editor editor,
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        AutoCadFramedG4ProofCase proofCase)
    {
        var sourceHandle = sourceEntity.Handle.ToString();
        ObjectId leaderId = ObjectId.Null;
        ObjectId frameId = ObjectId.Null;
        ObjectId itemId = ObjectId.Null;
        string? frameBlockName = null;
        ObjectId frameBlockId = ObjectId.Null;
        ObjectId textStyleId = ObjectId.Null;
        double? actualHeight = null;
        string? groupId = null;

        foreach (ObjectId id in OpenModelSpace(database, transaction, OpenMode.ForRead))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                !ElementLabelStore.TryRead(entity, out var labelData) ||
                labelData is null ||
                !string.Equals(
                    labelData.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            groupId ??= labelData.AnnotationGroupId;
            if (labelData.ComponentRole == AutoCadFramedG4CompositePolicy.LeaderRole)
            {
                leaderId = id;
            }
            else if (labelData.ComponentRole == AutoCadFramedG4CompositePolicy.FrameRole &&
                     entity is BlockReference frameReference)
            {
                frameId = id;
                frameBlockId = frameReference.BlockTableRecord;
                if (transaction.GetObject(frameBlockId, OpenMode.ForRead) is
                    BlockTableRecord record)
                {
                    frameBlockName = record.Name;
                }
            }
            else if (labelData.ComponentRole == AutoCadFramedG4CompositePolicy.ItemCodeRole &&
                     entity is DBText dbText)
            {
                itemId = id;
                textStyleId = dbText.TextStyleId;
                actualHeight = dbText.Height;
            }
        }

        var expectedHeight = AutoCadFramedG4ProofPolicy.ExpectedModelHeightMm(proofCase);
        editor.WriteMessage(
            $"\n  Case F readback source={sourceEntity.ObjectId} handle={sourceHandle} " +
            $"leader={leaderId} frame={frameId} item={itemId} " +
            $"frameBlock={frameBlockName ?? "<none>"} frameBlockId={frameBlockId} " +
            $"TextStyleId={textStyleId} height expected={expectedHeight.ToString(CultureInfo.InvariantCulture)} " +
            $"actual={(actualHeight?.ToString(CultureInfo.InvariantCulture) ?? "<n/a>")} " +
            $"group={groupId ?? "<n/a>"}");
    }

    private static void VerifyCase(
        Editor editor,
        Database database,
        Transaction transaction,
        AutoCadFramedG4ProofCase proofCase,
        ref string sharedCircleFrameName)
    {
        var annotations = ReadMarkedAnnotations(database, transaction, proofCase.Token)
            .ToArray();
        var itemCode = annotations.FirstOrDefault(entry =>
            entry.Data.ComponentRole == AutoCadFramedG4CompositePolicy.ItemCodeRole);
        var frame = annotations.FirstOrDefault(entry =>
            entry.Data.ComponentRole == AutoCadFramedG4CompositePolicy.FrameRole);
        var leader = annotations.FirstOrDefault(entry =>
            entry.Data.ComponentRole == AutoCadFramedG4CompositePolicy.LeaderRole);

        if (itemCode.Entity is null || frame.Entity is null || leader.Entity is null)
        {
            throw new InvalidOperationException(
                $"Case {proofCase.Token}: missing G4 composite parts.");
        }

        if (itemCode.Data.AnnotationGroupId != frame.Data.AnnotationGroupId ||
            itemCode.Data.AnnotationGroupId != leader.Data.AnnotationGroupId)
        {
            throw new InvalidOperationException(
                $"Case {proofCase.Token}: AnnotationGroupId mismatch.");
        }

        if (itemCode.Entity is not DBText dbText)
        {
            throw new InvalidOperationException(
                $"Case {proofCase.Token}: ItemCode must be DBText.");
        }

        var expectedHeight = AutoCadFramedG4ProofPolicy.ExpectedModelHeightMm(proofCase);
        if (Math.Abs(dbText.Height - expectedHeight) > 0.001d)
        {
            throw new InvalidOperationException(
                $"Case {proofCase.Token}: height expected {expectedHeight}, actual {dbText.Height}.");
        }

        if (frame.Entity is not BlockReference blockReference)
        {
            throw new InvalidOperationException(
                $"Case {proofCase.Token}: Frame must be BlockReference.");
        }

        var frameBlock = (BlockTableRecord)transaction.GetObject(
            blockReference.BlockTableRecord,
            OpenMode.ForRead);
        if (!AutoCadItemLeaderFrameOnlyBlockNamePolicy.IsG4FrameOnlyName(frameBlock.Name))
        {
            throw new InvalidOperationException(
                $"Case {proofCase.Token}: frame block '{frameBlock.Name}' is not G4.");
        }

        if (proofCase.FrameKind == ItemNumberLeaderStyle.Circle)
        {
            if (string.IsNullOrEmpty(sharedCircleFrameName))
            {
                sharedCircleFrameName = frameBlock.Name;
            }
            else if (!string.Equals(
                         sharedCircleFrameName,
                         frameBlock.Name,
                         StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Case {proofCase.Token}: Circle A/B must share one frame definition.");
            }
        }

        if (leader.Entity is not MLeader mLeader ||
            mLeader.ContentType != ContentType.NoneContent)
        {
            throw new InvalidOperationException(
                $"Case {proofCase.Token}: Leader must be MLeader NoneContent.");
        }

        editor.WriteMessage(
            $"\n  [{proofCase.Token}] group={itemCode.Data.AnnotationGroupId} " +
            $"frame={frameBlock.Name} height={dbText.Height.ToString(CultureInfo.InvariantCulture)} " +
            $"style={ResolveTextStyleName(transaction, dbText.TextStyleId)} token={dbText.TextString}");
    }

    private static string ResolveTextStyleName(Transaction transaction, ObjectId textStyleId)
    {
        if (textStyleId.IsNull ||
            transaction.GetObject(textStyleId, OpenMode.ForRead, false) is not
                TextStyleTableRecord style)
        {
            return "<null>";
        }

        return style.Name;
    }

    private static IEnumerable<(Entity Entity, ElementLabelData Data)> ReadMarkedAnnotations(
        Database database,
        Transaction transaction,
        string token)
    {
        foreach (ObjectId id in OpenModelSpace(database, transaction, OpenMode.ForRead))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                !HasProofMarker(entity, token) ||
                !ElementLabelStore.TryRead(entity, out var data) ||
                data is null)
            {
                continue;
            }

            yield return (entity, data);
        }
    }

    private static void MarkCreatedAnnotations(
        Database database,
        Transaction transaction,
        string sourceHandle,
        string token)
    {
        foreach (ObjectId id in OpenModelSpace(database, transaction, OpenMode.ForRead))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var entity,
                    database) ||
                entity is null ||
                !ElementLabelStore.TryRead(entity, out var data) ||
                data is null ||
                !string.Equals(
                    data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MarkProofEntity(entity, transaction, token);
        }
    }

    private static int EraseMarkedEntities(Database database, Transaction transaction)
    {
        var erased = 0;
        foreach (ObjectId id in OpenModelSpace(database, transaction, OpenMode.ForRead)
                     .Cast<ObjectId>()
                     .ToArray())
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var entity,
                    database) ||
                entity is null ||
                !HasAnyProofMarker(entity))
            {
                continue;
            }

            entity.Erase();
            erased++;
        }

        return erased;
    }

    private static void MarkProofEntity(
        Entity entity,
        Transaction transaction,
        string token)
    {
        EnsureRegApp(entity.Database, transaction);
        var retained = ReadForeignXData(entity);
        retained.Add(new TypedValue(1001, AutoCadFramedG4ProofPolicy.RegAppName));
        retained.Add(new TypedValue(1000, token));
        using var buffer = new ResultBuffer(retained.ToArray());
        if (!entity.IsWriteEnabled)
        {
            entity.UpgradeOpen();
        }

        entity.XData = buffer;
    }

    private static bool HasProofMarker(Entity entity, string token)
    {
        var xdata = entity.XData;
        if (xdata is null)
        {
            return false;
        }

        using (xdata)
        {
            var inside = false;
            foreach (var value in xdata.AsArray())
            {
                if (value.TypeCode == 1001)
                {
                    inside = string.Equals(
                        Convert.ToString(value.Value),
                        AutoCadFramedG4ProofPolicy.RegAppName,
                        StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inside &&
                    value.TypeCode == 1000 &&
                    string.Equals(
                        Convert.ToString(value.Value),
                        token,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasAnyProofMarker(Entity entity)
    {
        var xdata = entity.XData;
        if (xdata is null)
        {
            return false;
        }

        using (xdata)
        {
            return xdata.AsArray().Any(value =>
                value.TypeCode == 1001 &&
                string.Equals(
                    Convert.ToString(value.Value),
                    AutoCadFramedG4ProofPolicy.RegAppName,
                    StringComparison.OrdinalIgnoreCase));
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
            var skip = false;
            foreach (var value in xdata.AsArray())
            {
                if (value.TypeCode == 1001)
                {
                    skip = string.Equals(
                        Convert.ToString(value.Value),
                        AutoCadFramedG4ProofPolicy.RegAppName,
                        StringComparison.OrdinalIgnoreCase);
                }

                if (!skip)
                {
                    retained.Add(value);
                }
            }
        }

        return retained;
    }

    private static void EnsureRegApp(Database database, Transaction transaction)
    {
        var table = (RegAppTable)transaction.GetObject(
            database.RegAppTableId,
            OpenMode.ForRead);
        if (table.Has(AutoCadFramedG4ProofPolicy.RegAppName))
        {
            return;
        }

        table.UpgradeOpen();
        var record = new RegAppTableRecord
        {
            Name = AutoCadFramedG4ProofPolicy.RegAppName,
        };
        table.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static void WriteManifest(
        Database database,
        Transaction transaction,
        AutoCadFramedG4ProofManifest manifest)
    {
        var nod = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForWrite);
        var json = JsonSerializer.Serialize(manifest);
        Xrecord record;
        if (nod.Contains(AutoCadFramedG4ProofPolicy.ManifestDictionaryKey))
        {
            record = (Xrecord)transaction.GetObject(
                nod.GetAt(AutoCadFramedG4ProofPolicy.ManifestDictionaryKey),
                OpenMode.ForWrite);
        }
        else
        {
            record = new Xrecord();
            nod.SetAt(AutoCadFramedG4ProofPolicy.ManifestDictionaryKey, record);
            transaction.AddNewlyCreatedDBObject(record, true);
        }

        record.Data = new ResultBuffer(new TypedValue(1000, json));
    }

    private static bool TryReadManifest(
        Database database,
        Transaction transaction,
        out AutoCadFramedG4ProofManifest? manifest)
    {
        manifest = null;
        var nod = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!nod.Contains(AutoCadFramedG4ProofPolicy.ManifestDictionaryKey))
        {
            return false;
        }

        var record = (Xrecord)transaction.GetObject(
            nod.GetAt(AutoCadFramedG4ProofPolicy.ManifestDictionaryKey),
            OpenMode.ForRead);
        var json = record.Data?.AsArray()
            .Where(value => value.TypeCode == 1000)
            .Select(value => Convert.ToString(value.Value))
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        manifest = JsonSerializer.Deserialize<AutoCadFramedG4ProofManifest>(json);
        return manifest is not null;
    }

    private static void ClearManifest(Database database, Transaction transaction)
    {
        var nod = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForWrite);
        if (!nod.Contains(AutoCadFramedG4ProofPolicy.ManifestDictionaryKey))
        {
            return;
        }

        var id = nod.GetAt(AutoCadFramedG4ProofPolicy.ManifestDictionaryKey);
        nod.Remove(AutoCadFramedG4ProofPolicy.ManifestDictionaryKey);
        if (transaction.GetObject(id, OpenMode.ForWrite, true) is DBObject record &&
            !record.IsErased)
        {
            record.Erase();
        }
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
}
#endif
