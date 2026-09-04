using System.IO;
using System.Security.Cryptography;
using AcKrovy.AutoCAD.Infrastructure;
#if DEBUG
using AcKrovy.Core.Services.Roofs;
#endif
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

public sealed class AutoCadRoofExternalImportCommand
{
    [CommandMethod(AcKrovyCommandNames.ImportDwg, CommandFlags.Modal)]
    public void Execute() => RoofExternalImportWorkflow.Execute(RoofExternalImportFaultMode.None);
}

#if DEBUG
public sealed class AutoCadRoofExternalImportAbortProofCommands
{
    internal const string AfterInsertCommand = "AK_DEBUG_C1_ABORT_AFTER_INSERT";
    internal const string AfterSanitationCommand = "AK_DEBUG_C1_ABORT_AFTER_SANITATION";

    [CommandMethod(AfterInsertCommand, CommandFlags.Modal)]
    public void AbortAfterInsert()
    {
        RoofExternalImportC1DbmodTimeline.Arm(
            AcApplication.DocumentManager.MdiActiveDocument,
            RoofExternalImportFaultMode.AfterMappingValidation);
        RoofExternalImportWorkflow.Execute(RoofExternalImportFaultMode.AfterMappingValidation);
    }

    [CommandMethod(AfterSanitationCommand, CommandFlags.Modal)]
    public void AbortAfterSanitation()
    {
        RoofExternalImportC1DbmodTimeline.Arm(
            AcApplication.DocumentManager.MdiActiveDocument,
            RoofExternalImportFaultMode.AfterMappedNewSanitation);
        RoofExternalImportWorkflow.Execute(RoofExternalImportFaultMode.AfterMappedNewSanitation);
    }
}
#endif

internal enum RoofExternalImportFaultMode
{
    None,
#if DEBUG
    AfterMappingValidation,
    AfterMappedNewSanitation,
#endif
}

internal static class RoofExternalImportWorkflow
{
    private const string SnapshotFileName = "source.dwg";

    public static void Execute(RoofExternalImportFaultMode faultMode)
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        var selected = editor.GetFileNameForOpen(new PromptOpenFileOptions(
            UiStrings.GetString("Command_ImportDwg_Prompt"))
        {
            Filter = "Drawing (*.dwg)|*.dwg",
        });
        if (selected.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(selected.StringResult))
        {
            editor.WriteMessage(UiStrings.GetString("Command_ImportDwg_Cancelled"));
            return;
        }

        string? snapshotDirectory = null;
        FileStream? snapshotLease = null;
        string? destinationName = null;
#if DEBUG
        ImportTargetSnapshot? targetBefore = null;
        IReadOnlyList<ObjectId> appendedDuringOperation = [];
        IReadOnlyDictionary<ObjectId, RoofExternalImportRollbackObjectDiagnostics.CapturedObject> rollbackDiagnostics =
            new Dictionary<ObjectId, RoofExternalImportRollbackObjectDiagnostics.CapturedObject>();
        var transactionCommitted = false;
#endif
        try
        {
            var sourcePath = Path.GetFullPath(selected.StringResult);
            if (!File.Exists(sourcePath) ||
                !string.Equals(Path.GetExtension(sourcePath), ".dwg", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("source-not-readable-dwg");
            }

            destinationName = SymbolUtilityServices.GetBlockNameFromInsertPathName(sourcePath);
            if (string.IsNullOrWhiteSpace(destinationName) || destinationName.Contains('=', StringComparison.Ordinal))
            {
                throw new InvalidOperationException("invalid-destination-block-name");
            }

            snapshotDirectory = Path.Combine(Path.GetTempPath(), "AcKrovy", "ExternalImport", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(snapshotDirectory);
            var snapshotPath = Path.Combine(snapshotDirectory, SnapshotFileName);
            File.Copy(sourcePath, snapshotPath, overwrite: false);
            snapshotLease = new FileStream(
                snapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
            var sourceIdentity = ComputeIdentity(snapshotLease);

            using var sourceDatabase = new Database(false, true);
            sourceDatabase.ReadDwgFile(
                snapshotPath,
                FileOpenMode.OpenForReadAndAllShare,
                allowCPConversion: true,
                password: string.Empty);
            sourceDatabase.CloseInput(true);

            var manifest = RoofExternalImportPreflightService.Create(
                sourceDatabase,
                sourceIdentity,
                destinationName);
            if (!manifest.Decision.IsAllowed)
            {
                throw new InvalidOperationException(manifest.Decision.Reason);
            }

            EnsureNoCollision(document.Database, manifest);
#if DEBUG
            targetBefore = ImportTargetSnapshot.Capture(document.Database, destinationName);
            RoofExternalImportC1DbmodTimeline.Write(
                document,
                faultMode,
                "before-target-transaction");
#endif

            ObjectId rootId;
            ObjectId referenceId;
#if DEBUG
            try
            {
#endif
                using (var transaction = document.Database.TransactionManager.StartTransaction())
                using (var session = new RoofExternalImportSession(
                           document, document.Database, sourceDatabase, manifest))
                {
#if DEBUG
                    session.CaptureRollbackDiagnostics = faultMode != RoofExternalImportFaultMode.None;
                    rollbackDiagnostics = session.RollbackDiagnostics;
#endif
                    EnsureNoCollision(transaction, document.Database, manifest);
                    rootId = document.Database.Insert(destinationName, sourceDatabase, true);
#if DEBUG
                    RoofExternalImportC1DbmodTimeline.Write(
                        document,
                        faultMode,
                        "after-database-insert-return");
#endif
                    var operation = session.Freeze(rootId);
#if DEBUG
                    appendedDuringOperation = operation.AppendedIds;
#endif
                    ValidateMapping(operation);
                    ValidateReturnedRoot(transaction, operation);
#if DEBUG
                    WriteMappingValidationSucceeded(editor, faultMode, operation);
                    InjectFault(editor, faultMode, RoofExternalImportFaultMode.AfterMappingValidation);
#endif

                    var sanitation = RoofExternalImportSanitizationService.Apply(transaction, operation);
                    RoofExternalImportSanitizationService.Verify(transaction, sanitation);
#if DEBUG
                    InjectFault(editor, faultMode, RoofExternalImportFaultMode.AfterMappedNewSanitation);
#endif

                    var currentSpace = (BlockTableRecord)transaction.GetObject(
                        document.Database.CurrentSpaceId,
                        OpenMode.ForWrite);
                    using var reference = new BlockReference(Point3d.Origin, rootId)
                    {
                        ScaleFactors = new Scale3d(1d, 1d, 1d),
                        Rotation = 0d,
                    };
                    referenceId = currentSpace.AppendEntity(reference);
                    transaction.AddNewlyCreatedDBObject(reference, true);
                    operation = session.Freeze(rootId) with { ExplicitReferenceId = referenceId };
                    ValidateFinalState(transaction, operation);
                    transaction.Commit();
#if DEBUG
                    transactionCommitted = true;
#endif
                }
#if DEBUG
            }
            finally
            {
                RoofExternalImportC1DbmodTimeline.Write(
                    document,
                    faultMode,
                    "after-target-transaction-dispose");
            }
#endif

            editor.WriteMessage(UiStrings.Format(
                UiStrings.GetString("Command_ImportDwg_CompletedFormat"),
                destinationName));
        }
        catch (System.Exception exception)
        {
#if DEBUG
            if (faultMode != RoofExternalImportFaultMode.None && targetBefore is not null)
            {
                var audit = targetBefore.VerifyRollback(
                    document.Database, destinationName ?? string.Empty,
                    editor, appendedDuringOperation, rollbackDiagnostics);
                RoofExternalImportC1DbmodTimeline.Write(
                    document,
                    faultMode,
                    "after-rollback-audit-dispose");
                RoofExternalImportC1DbmodTimeline.RecordRollbackProof(
                    document,
                    faultMode,
                    transactionCommitted,
                    audit.RootAbsent,
                    audit.ImportedObjectsAbsent,
                    audit.TopLevelReferenceAbsent,
                    audit.ObjectsUnchanged,
                    targetBefore.DbmodBefore);
            }
#endif
            editor.WriteMessage(UiStrings.Format(
                UiStrings.GetString("Command_ImportDwg_FailedFormat"),
                exception.Message));
        }
        finally
        {
            snapshotLease?.Dispose();
            DeleteSnapshot(snapshotDirectory);
        }
    }

    private static void ValidateMapping(RoofExternalImportOperationFacts operation)
    {
        var support = operation.Manifest.SupportBlockTableRecords.ToHashSet();
        var pairs = operation.Mapping.GroupBy(pair => pair.SourceId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var sourceId in operation.Manifest.ExpectedCloneObjectIds)
        {
            if (!pairs.TryGetValue(sourceId, out var sourcePairs) || sourcePairs.Length != 1)
            {
                throw new InvalidOperationException("incomplete-operation-mapping");
            }

            var pair = sourcePairs[0];
            if (pair.DestinationId.IsNull || pair.DestinationId.Database != operation.TargetDatabase)
            {
                throw new InvalidOperationException("mapping-destination-outside-target");
            }

            if (!support.Contains(sourceId) && !pair.IsCloned)
            {
                throw new InvalidOperationException("unexpected-mapped-reused-object");
            }
        }

    }

    private static void ValidateReturnedRoot(
        Transaction transaction,
        RoofExternalImportOperationFacts operation)
    {
        if (operation.CloneContext != DeepCloneType.InsertCopy ||
            string.IsNullOrWhiteSpace(operation.MappingCallback) ||
            operation.Document.Database != operation.TargetDatabase ||
            operation.Manifest.SourceRootBlockId.Database != operation.SourceDatabase ||
            operation.ReturnedRootId.IsNull ||
            operation.ReturnedRootId.Database != operation.TargetDatabase)
        {
            throw new InvalidOperationException("returned-root-operation-binding-failed");
        }

        var table = (BlockTable)transaction.GetObject(
            operation.TargetDatabase.BlockTableId,
            OpenMode.ForRead);
        if (!table.Has(operation.Manifest.DestinationBlockName) ||
            table[operation.Manifest.DestinationBlockName] != operation.ReturnedRootId ||
            transaction.GetObject(operation.ReturnedRootId, OpenMode.ForRead, false) is not BlockTableRecord root ||
            root.IsErased || root.IsLayout || root.IsAnonymous || root.IsDependent ||
            root.IsFromExternalReference || root.IsFromOverlayReference ||
            !string.Equals(root.Name, operation.Manifest.DestinationBlockName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("returned-root-invariant-failed");
        }

        if (!operation.AppendedIds.Contains(operation.ReturnedRootId))
        {
            throw new InvalidOperationException("returned-root-append-evidence-missing");
        }
    }

    private static void ValidateFinalState(
        Transaction transaction,
        RoofExternalImportOperationFacts operation)
    {
        if (operation.ExplicitReferenceId.IsNull ||
            transaction.GetObject(operation.ExplicitReferenceId, OpenMode.ForRead) is not BlockReference reference ||
            reference.BlockTableRecord != operation.ReturnedRootId || reference.Position != Point3d.Origin ||
            reference.Rotation != 0d || reference.ScaleFactors != new Scale3d(1d, 1d, 1d))
        {
            throw new InvalidOperationException("top-level-reference-invariant-failed");
        }

        var appended = operation.AppendedIds.ToHashSet();
        if (!appended.Contains(operation.ExplicitReferenceId))
        {
            throw new InvalidOperationException("object-appended-cross-check-failed");
        }
    }

    private static void EnsureNoCollision(Database database, RoofExternalImportManifest manifest)
    {
        using var transaction = database.TransactionManager.StartOpenCloseTransaction();
        EnsureNoCollision(transaction, database, manifest);
    }

    private static void EnsureNoCollision(
        Transaction transaction,
        Database database,
        RoofExternalImportManifest manifest)
    {
        var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        if (table.Has(manifest.DestinationBlockName))
        {
            throw new InvalidOperationException("root-btr-collision");
        }

        if (manifest.ReachableUserBlockNames.Any(table.Has))
        {
            throw new InvalidOperationException("nested-user-btr-collision");
        }
    }

    private static string ComputeIdentity(FileStream lease)
    {
        lease.Position = 0;
        var hash = SHA256.HashData(lease);
        var result = $"sha256:{Convert.ToHexString(hash)}:length:{lease.Length}";
        lease.Position = 0;
        return result;
    }

#if DEBUG
    private static void WriteMappingValidationSucceeded(
        Editor editor,
        RoofExternalImportFaultMode faultMode,
        RoofExternalImportOperationFacts operation)
    {
        if (faultMode == RoofExternalImportFaultMode.AfterMappingValidation)
        {
            WriteDiagnostic(
                editor,
                "ROOF_IMPORT_C1_MAPPING_VALIDATION " +
                $"result=pass callback={operation.MappingCallback} " +
                $"expectedCloneCount={operation.Manifest.ExpectedCloneObjectIds.Count}");
        }
    }

    private static void InjectFault(
        Editor editor,
        RoofExternalImportFaultMode actual,
        RoofExternalImportFaultMode checkpoint)
    {
        if (actual == checkpoint)
        {
            WriteDiagnostic(
                editor,
                $"ROOF_IMPORT_C1_ABORT_FAILPOINT mode={checkpoint} reached=true");
            throw new RoofExternalImportInjectedAbortException(checkpoint.ToString());
        }
    }
#endif

    private static void DeleteSnapshot(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            var file = Path.Combine(directory, SnapshotFileName);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
            Directory.Delete(directory, false);
        }
        catch
        {
            // The private read-only source lease is already closed; cleanup is best-effort.
        }
    }

#if DEBUG
    private static void WriteDiagnostic(Editor editor, string message)
    {
        try { editor.WriteMessage("\n" + message); } catch { }
    }

    private static bool IsRolledBackObject(ObjectId id, Transaction transaction)
    {
        try
        {
            var objectIsValid = id.IsValid;
            var objectIsErased = id.IsErased;
            if (!objectIsValid || objectIsErased)
            {
                return RoofExternalImportRollbackRules.IsAppendedObjectAbsent(
                    objectIsValid,
                    objectIsErased,
                    isStructuralBlockTableRecordSentinel: false,
                    ownerIsValid: true,
                    ownerIsErased: false);
            }

            // Surviving ObjectIds require classification. Only BlockBegin/BlockEnd may
            // pass via erased/invalid owner; ordinary entities fail closed here.
            if (!TryClassifyStructuralBlockSentinel(
                    transaction,
                    id,
                    out var isSentinel,
                    out var ownerIsValid,
                    out var ownerIsErased))
            {
                return false;
            }

            return RoofExternalImportRollbackRules.IsAppendedObjectAbsent(
                objectIsValid: true,
                objectIsErased: false,
                isStructuralBlockTableRecordSentinel: isSentinel,
                ownerIsValid: ownerIsValid,
                ownerIsErased: ownerIsErased);
        }
        catch
        {
            return true;
        }
    }

    private static bool TryClassifyStructuralBlockSentinel(
        Transaction transaction,
        ObjectId id,
        out bool isStructuralBlockTableRecordSentinel,
        out bool ownerIsValid,
        out bool ownerIsErased)
    {
        isStructuralBlockTableRecordSentinel = false;
        ownerIsValid = true;
        ownerIsErased = false;
        try
        {
            var value = transaction.GetObject(id, OpenMode.ForRead, openErased: true);
            if (value is not (BlockBegin or BlockEnd))
            {
                return true;
            }

            isStructuralBlockTableRecordSentinel = true;
            var owner = value.OwnerId;
            ownerIsValid = owner.IsValid;
            ownerIsErased = owner.IsErased;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class RoofExternalImportInjectedAbortException(string checkpoint)
        : InvalidOperationException("debug-c1-abort:" + checkpoint);

    private sealed record ImportTargetSnapshot(
        ObjectId[] BlockIds,
        ObjectId[] EntityIds,
        IReadOnlyDictionary<ObjectId, BlockFingerprint> BlockFingerprints,
        int DbmodBefore,
        string DestinationName)
    {
        public static ImportTargetSnapshot Capture(Database database, string destinationName)
        {
            using var transaction = database.TransactionManager.StartOpenCloseTransaction();
            var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var blocks = table.Cast<ObjectId>().OrderBy(id => id.Handle.Value).ToArray();
            var entities = blocks.SelectMany(id =>
                    ((BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>())
                .OrderBy(id => id.Handle.Value)
                .ToArray();
            var fingerprints = blocks.ToDictionary(
                id => id,
                id => BlockFingerprint.Capture(
                    (BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead)));
            return new(
                blocks,
                entities,
                fingerprints,
                Convert.ToInt32(
                    AcApplication.GetSystemVariable("DBMOD"),
                    System.Globalization.CultureInfo.InvariantCulture),
                destinationName);
        }

        public RollbackAudit VerifyRollback(
            Database database,
            string destinationName,
            Editor editor,
            IReadOnlyList<ObjectId> appendedIds,
            IReadOnlyDictionary<ObjectId, RoofExternalImportRollbackObjectDiagnostics.CapturedObject> capturedObjects)
        {
            using var transaction = database.TransactionManager.StartOpenCloseTransaction();
            var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var blocks = table.Cast<ObjectId>().OrderBy(id => id.Handle.Value).ToArray();
            var entities = blocks.SelectMany(id =>
                    ((BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>())
                .OrderBy(id => id.Handle.Value)
                .ToArray();
            var rootAbsent = !table.Has(destinationName);
            var contentsUnchanged = BlockFingerprints.All(item =>
                blocks.Contains(item.Key) &&
                item.Value == BlockFingerprint.Capture(
                    (BlockTableRecord)transaction.GetObject(item.Key, OpenMode.ForRead)));
            var unchanged = BlockIds.SequenceEqual(blocks) &&
                EntityIds.SequenceEqual(entities) && contentsUnchanged;
            // Full ObjectAppended set; no short-circuit. Verdict may open surviving IDs only
            // to classify BlockBegin/BlockEnd under the structural-owner Core rule. Diagnostic
            // opens remain supplemental and run after every Absent flag is materialized.
            var rollbackStates = appendedIds
                .Select(id => (Id: id, Absent: IsRolledBackObject(id, transaction))).ToArray();
            foreach (var state in rollbackStates)
            {
                capturedObjects.TryGetValue(state.Id, out var captured);
                RoofExternalImportRollbackObjectDiagnostics.Write(
                    editor, transaction, state.Id, captured, state.Absent);
            }
            var survivingCount = rollbackStates.Count(state => !state.Absent);
            WriteDiagnostic(editor, "ROOF_IMPORT_C1_ROLLBACK_OBJECT_SUMMARY " +
                $"checked={rollbackStates.Length} absentPass={rollbackStates.Length - survivingCount} " +
                $"survivingFail={survivingCount}");
            return new(rootAbsent, unchanged, unchanged, rollbackStates.All(state => state.Absent));
        }
    }

    private sealed record BlockFingerprint(
        string Name,
        Point3d Origin,
        bool IsAnonymous,
        bool IsDependent,
        bool IsFromExternalReference,
        bool IsFromOverlayReference,
        string EntityIds)
    {
        public static BlockFingerprint Capture(BlockTableRecord block) => new(
            block.Name,
            block.Origin,
            block.IsAnonymous,
            block.IsDependent,
            block.IsFromExternalReference,
            block.IsFromOverlayReference,
            string.Join("|", block.Cast<ObjectId>().Select(id => id.Handle.ToString())));
    }

    private readonly record struct RollbackAudit(
        bool RootAbsent,
        bool ObjectsUnchanged,
        bool TopLevelReferenceAbsent,
        bool ImportedObjectsAbsent)
    {
        public bool IsPass => RootAbsent && ObjectsUnchanged && TopLevelReferenceAbsent;
    }
#endif
}
