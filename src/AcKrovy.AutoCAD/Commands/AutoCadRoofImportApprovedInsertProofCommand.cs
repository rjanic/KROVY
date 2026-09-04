#if DEBUG
using System.IO;
using System.Security.Cryptography;
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG-only neutral proof of an approved, synchronous native INSERT invocation.
/// It is not a production import workflow and performs no KROVY mutation.
/// </summary>
public sealed class AutoCadRoofImportApprovedInsertProofCommand
{
    private const string CommandName = "AK_DEBUG_APPROVED_INSERT_PROOF";
    private const string SnapshotFileName = "approved-insert-source.dwg";

    [CommandMethod(CommandName, CommandFlags.Modal)]
    public void Execute()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        var selection = editor.GetFileNameForOpen(
            new PromptOpenFileOptions("\nSelect neutral external DWG for approved INSERT proof:")
            {
                Filter = "Drawing (*.dwg)|*.dwg",
            });
        if (selection.Status != PromptStatus.OK ||
            string.IsNullOrWhiteSpace(selection.StringResult))
        {
            Write(editor, "ROOF_IMPORT_APPROVED_INSERT_PROOF result=cancelled stage=source-selection");
            return;
        }

        Guid? sessionId = null;
        string? snapshotDirectory = null;
        FileStream? snapshotLease = null;
        var approvedInsertCompleted = false;

        try
        {
            var selectedSource = Path.GetFullPath(selection.StringResult);
            if (!File.Exists(selectedSource) ||
                !string.Equals(
                    Path.GetExtension(selectedSource),
                    ".dwg",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The selected source is not a readable DWG file.");
            }

            var destinationBlockName =
                SymbolUtilityServices.GetBlockNameFromInsertPathName(selectedSource);
            if (string.IsNullOrWhiteSpace(destinationBlockName) ||
                destinationBlockName.Contains('=', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The intended destination block name is not valid for the proof command.");
            }

            snapshotDirectory = Path.Combine(
                Path.GetTempPath(),
                "AcKrovy",
                "ApprovedInsertProof",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(snapshotDirectory);
            var snapshotPath = Path.Combine(snapshotDirectory, SnapshotFileName);
            File.Copy(selectedSource, snapshotPath, overwrite: false);

            snapshotLease = new FileStream(
                snapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            var sourceIdentity = ComputeSourceIdentity(snapshotLease);

            sessionId = RoofImportApprovedInsertProofSession.Create(
                document,
                document.Database,
                sourceIdentity,
                destinationBlockName);

            var preflight = PreflightSnapshot(snapshotPath);
            var targetBlockCountBefore = EnsureDestinationDoesNotExist(
                document.Database,
                destinationBlockName);
            RoofImportApprovedInsertProofSession.MarkPreflightOk(
                sessionId.Value,
                preflight.BlockCount,
                preflight.EntityCount);

            Write(
                editor,
                "ROOF_IMPORT_APPROVED_INSERT_PROOF " +
                $"session={sessionId.Value:N} stage=preflight-ok " +
                $"sourceIdentity={sourceIdentity} " +
                $"destination={Token(destinationBlockName)} " +
                $"snapshotName={SnapshotFileName} sourceBlocks={preflight.BlockCount} " +
                $"sourceEntities={preflight.EntityCount} krovyMetadata=false");

            RoofImportDocumentLockVetoProbe.ArmDashInsert(document);
            RoofImportApprovedInsertProofSession.Approve(sessionId.Value);

            var blockSpecification = $"{destinationBlockName}={snapshotPath}";
            Write(
                editor,
                "ROOF_IMPORT_APPROVED_INSERT_PROOF " +
                $"session={sessionId.Value:N} stage=invoke " +
                "api=Editor.Command command=._-INSERT point=0,0,0 " +
                "xScale=1 yScale=1 zScale=1 rotation=0");

            editor.Command(
                "._-INSERT",
                blockSpecification,
                Point3d.Origin,
                "_XYZ",
                1.0,
                1.0,
                1.0,
                0.0);

            approvedInsertCompleted =
                RoofImportApprovedInsertProofSession.WasCompleted(sessionId.Value);
            if (!approvedInsertCompleted)
            {
                throw new InvalidOperationException(
                    "The approved native INSERT did not reach its exact CommandEnded boundary.");
            }

            var result = InspectResult(document.Database, destinationBlockName);
            Write(
                editor,
                "ROOF_IMPORT_APPROVED_INSERT_RESULT " +
                $"session={sessionId.Value:N} destination={Token(destinationBlockName)} " +
                $"rootBtr={result.RootBtrHandle} " +
                $"rootEntities={result.RootEntityCount} " +
                $"targetBtrsBefore={targetBlockCountBefore} " +
                $"targetBtrsAfter={result.TargetBlockCount} " +
                $"targetBtrDelta={result.TargetBlockCount - targetBlockCountBefore} " +
                $"topLevelReferences={result.TopLevelReferenceCount} " +
                $"referenceHandles={Token(result.ReferenceHandles)} " +
                $"logicalInsertions={(result.TopLevelReferenceCount == 1 ? 1 : 0)}");

            if (result.TargetBlockCount - targetBlockCountBefore != 1 ||
                result.TopLevelReferenceCount != 1)
            {
                throw new InvalidOperationException(
                    "The native INSERT did not produce exactly one root BTR and one top-level BlockReference.");
            }
        }
        catch (System.Exception exception)
        {
            if (sessionId is { } id)
            {
                RoofImportApprovedInsertProofSession.MarkInvocationException(id, exception);
            }

            RoofImportDocumentLockVetoProbe.Disarm(document);
            Write(
                editor,
                "ROOF_IMPORT_APPROVED_INSERT_PROOF " +
                $"result=failed error={Token(exception.GetType().Name)} " +
                $"message={Token(exception.Message)}");
        }
        finally
        {
            if (sessionId is { } id)
            {
                RoofImportApprovedInsertProofSession.Clear(
                    id,
                    approvedInsertCompleted ? "completed" : "failed-or-cancelled");
            }

            snapshotLease?.Dispose();
            DeleteSnapshotDirectory(snapshotDirectory, editor);
        }
    }

    private static string ComputeSourceIdentity(FileStream snapshotLease)
    {
        snapshotLease.Position = 0;
        var hash = SHA256.HashData(snapshotLease);
        var length = snapshotLease.Length;
        snapshotLease.Position = 0;
        return $"sha256:{Convert.ToHexString(hash)}:length:{length}";
    }

    private static PreflightResult PreflightSnapshot(string snapshotPath)
    {
        using var sourceDatabase = new Database(buildDefaultDrawing: false, noDocument: true);
        sourceDatabase.ReadDwgFile(
            snapshotPath,
            FileOpenMode.OpenForReadAndAllShare,
            allowCPConversion: true,
            password: string.Empty);
        sourceDatabase.CloseInput(closeFile: true);

        using var transaction = sourceDatabase.TransactionManager.StartOpenCloseTransaction();
        var blockTable = (BlockTable)transaction.GetObject(
            sourceDatabase.BlockTableId,
            OpenMode.ForRead);
        var regAppTable = (RegAppTable)transaction.GetObject(
            sourceDatabase.RegAppTableId,
            OpenMode.ForRead);

        foreach (ObjectId regAppId in regAppTable)
        {
            var record = (RegAppTableRecord)transaction.GetObject(regAppId, OpenMode.ForRead);
            if (IsKrovyMarker(record.Name))
            {
                throw new InvalidOperationException(
                    "The proof source contains a KROVY RegApp marker.");
            }
        }

        var visitedDictionaries = new HashSet<ObjectId>();
        var namedObjects = (DBDictionary)transaction.GetObject(
            sourceDatabase.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (ContainsKrovyDictionaryMarker(transaction, namedObjects, visitedDictionaries))
        {
            throw new InvalidOperationException(
                "The proof source contains a KROVY dictionary marker.");
        }

        var blockCount = 0;
        var entityCount = 0;
        foreach (ObjectId blockId in blockTable)
        {
            blockCount++;
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            EnsureNoKrovyObjectMetadata(transaction, block, visitedDictionaries);
            foreach (ObjectId entityId in block)
            {
                entityCount++;
                var entity = (Entity)transaction.GetObject(entityId, OpenMode.ForRead);
                EnsureNoKrovyObjectMetadata(transaction, entity, visitedDictionaries);
            }
        }

        return new PreflightResult(blockCount, entityCount);
    }

    private static void EnsureNoKrovyObjectMetadata(
        Transaction transaction,
        DBObject value,
        HashSet<ObjectId> visitedDictionaries)
    {
        using (var xdata = value.XData)
        {
            if (xdata is not null)
            {
                foreach (var typedValue in xdata)
                {
                    if (typedValue.TypeCode == (int)DxfCode.ExtendedDataRegAppName &&
                        typedValue.Value is string regAppName &&
                        IsKrovyMarker(regAppName))
                    {
                        throw new InvalidOperationException(
                            "The proof source contains KROVY XData.");
                    }
                }
            }
        }

        if (!value.ExtensionDictionary.IsNull)
        {
            var dictionary = (DBDictionary)transaction.GetObject(
                value.ExtensionDictionary,
                OpenMode.ForRead);
            if (ContainsKrovyDictionaryMarker(
                    transaction,
                    dictionary,
                    visitedDictionaries))
            {
                throw new InvalidOperationException(
                    "The proof source contains a KROVY extension-dictionary marker.");
            }
        }
    }

    private static bool ContainsKrovyDictionaryMarker(
        Transaction transaction,
        DBDictionary dictionary,
        HashSet<ObjectId> visited)
    {
        if (!visited.Add(dictionary.ObjectId))
        {
            return false;
        }

        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (IsKrovyMarker(entry.Key))
            {
                return true;
            }

            var child = transaction.GetObject(entry.Value, OpenMode.ForRead);
            using (var xdata = child.XData)
            {
                if (xdata is not null &&
                    xdata.Cast<TypedValue>().Any(value =>
                        value.TypeCode == (int)DxfCode.ExtendedDataRegAppName &&
                        value.Value is string regAppName &&
                        IsKrovyMarker(regAppName)))
                {
                    return true;
                }
            }

            if (child is DBDictionary nested &&
                ContainsKrovyDictionaryMarker(transaction, nested, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKrovyMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.StartsWith("DECORAIR_ACADKROVY", StringComparison.Ordinal) ||
         value.StartsWith("ACAD_KROVY", StringComparison.Ordinal));

    private static int EnsureDestinationDoesNotExist(
        Database targetDatabase,
        string destinationBlockName)
    {
        using var transaction = targetDatabase.TransactionManager.StartOpenCloseTransaction();
        var blockTable = (BlockTable)transaction.GetObject(
            targetDatabase.BlockTableId,
            OpenMode.ForRead);
        if (blockTable.Has(destinationBlockName))
        {
            throw new InvalidOperationException(
                "The proof destination block name already exists in the target drawing.");
        }

        return blockTable.Cast<ObjectId>().Count();
    }

    private static InsertResult InspectResult(
        Database targetDatabase,
        string destinationBlockName)
    {
        using var transaction = targetDatabase.TransactionManager.StartOpenCloseTransaction();
        var blockTable = (BlockTable)transaction.GetObject(
            targetDatabase.BlockTableId,
            OpenMode.ForRead);
        if (!blockTable.Has(destinationBlockName))
        {
            throw new InvalidOperationException(
                "Approved INSERT completed without the expected root block definition.");
        }

        var rootId = blockTable[destinationBlockName];
        var root = (BlockTableRecord)transaction.GetObject(rootId, OpenMode.ForRead);
        var rootEntityCount = root.Cast<ObjectId>().Count();
        var currentSpace = (BlockTableRecord)transaction.GetObject(
            targetDatabase.CurrentSpaceId,
            OpenMode.ForRead);
        var referenceHandles = new List<string>();
        foreach (ObjectId objectId in currentSpace)
        {
            if (transaction.GetObject(objectId, OpenMode.ForRead) is BlockReference reference &&
                reference.BlockTableRecord == rootId)
            {
                referenceHandles.Add(reference.Handle.ToString());
            }
        }

        return new InsertResult(
            root.Handle.ToString(),
            rootEntityCount,
            blockTable.Cast<ObjectId>().Count(),
            referenceHandles.Count,
            string.Join(",", referenceHandles));
    }

    private static void DeleteSnapshotDirectory(string? directory, Editor editor)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            var snapshotPath = Path.Combine(directory, SnapshotFileName);
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: false);
            }
        }
        catch (System.Exception exception)
        {
            Write(
                editor,
                "ROOF_IMPORT_APPROVED_INSERT_PROOF " +
                $"result=cleanup-warning error={Token(exception.GetType().Name)}");
        }
    }

    private static void Write(Editor editor, string message)
    {
        try
        {
            editor.WriteMessage("\n" + message);
        }
        catch
        {
            // A DEBUG diagnostic must never escape the proof command.
        }
    }

    private static string Token(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Trim()
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Replace(' ', '_');

    private readonly record struct PreflightResult(int BlockCount, int EntityCount);

    private readonly record struct InsertResult(
        string RootBtrHandle,
        int RootEntityCount,
        int TargetBlockCount,
        int TopLevelReferenceCount,
        string ReferenceHandles);
}
#endif
