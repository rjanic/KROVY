#if DEBUG
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG-only neutral proof of a direct managed Database.Insert plus BlockReference.
/// It is not a production import workflow and performs no KROVY mutation.
/// </summary>
public sealed class AutoCadRoofImportDirectInsertProofCommand
{
    private const string SnapshotFileName = "direct-insert-source.dwg";

    [CommandMethod(
        RoofImportDirectInsertProofSession.CommandName,
        CommandFlags.Modal)]
    public void Execute()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        var selection = editor.GetFileNameForOpen(
            new PromptOpenFileOptions("\nSelect neutral external DWG for direct INSERT proof:")
            {
                Filter = "Drawing (*.dwg)|*.dwg",
            });
        if (selection.Status != PromptStatus.OK ||
            string.IsNullOrWhiteSpace(selection.StringResult))
        {
            Write(editor, "ROOF_IMPORT_DIRECT_INSERT_PROOF result=cancelled stage=source-selection");
            return;
        }

        Guid? sessionId = null;
        string? snapshotDirectory = null;
        FileStream? snapshotLease = null;
        string? sourceIdentity = null;
        var completed = false;

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
                "DirectInsertProof",
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
            sourceIdentity = ComputeSourceIdentity(snapshotLease);

            using var sourceDatabase = new Database(
                buildDefaultDrawing: false,
                noDocument: true);
            sourceDatabase.ReadDwgFile(
                snapshotPath,
                FileOpenMode.OpenForReadAndAllShare,
                allowCPConversion: true,
                password: string.Empty);
            sourceDatabase.CloseInput(closeFile: true);

            var preflight = PreflightSourceDatabase(sourceDatabase, editor);
            var targetBlockCountBefore = EnsureDestinationDoesNotExist(
                document.Database,
                destinationBlockName);

            sessionId = RoofImportDirectInsertProofSession.Create(
                document,
                document.Database,
                sourceDatabase,
                sourceIdentity,
                destinationBlockName);
            RoofImportDirectInsertProofSession.MarkPreflightOk(
                sessionId.Value,
                preflight.BlockCount,
                preflight.ModelSpaceEntityCount);

            Write(
                editor,
                "ROOF_IMPORT_DIRECT_INSERT_PREFLIGHT " +
                $"session={sessionId.Value:N} result=pass " +
                $"sourceIdentity={sourceIdentity} " +
                $"destination={Token(destinationBlockName)} " +
                $"sourceBlocks={preflight.BlockCount} " +
                $"modelSpaceEntities={preflight.ModelSpaceEntityCount} " +
                $"geometry={preflight.GeometryType} nestedBlocks=false krovyMetadata=false " +
                $"targetBtrsBefore={targetBlockCountBefore}");

            RoofImportDirectInsertProofSession.BeginInsert(sessionId.Value);
            Write(
                editor,
                "ROOF_IMPORT_DIRECT_INSERT_PROOF " +
                $"session={sessionId.Value:N} stage=invoke " +
                "api=Database.Insert preserveSourceDatabase=true " +
                "point=0,0,0 xScale=1 yScale=1 zScale=1 rotation=0");

            ObjectId rootBlockId;
            ObjectId referenceId;
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                rootBlockId = document.Database.Insert(
                    destinationBlockName,
                    sourceDatabase,
                    preserveSourceDatabase: true);

                var currentSpace = (BlockTableRecord)transaction.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(document.Database),
                    OpenMode.ForWrite);
                using var reference = new BlockReference(Point3d.Origin, rootBlockId)
                {
                    ScaleFactors = new Scale3d(1.0, 1.0, 1.0),
                    Rotation = 0.0,
                };
                referenceId = currentSpace.AppendEntity(reference);
                transaction.AddNewlyCreatedDBObject(reference, add: true);
                transaction.Commit();
            }

            var session = RoofImportDirectInsertProofSession.GetSnapshot(sessionId.Value);
            var result = InspectResult(
                document.Database,
                destinationBlockName,
                rootBlockId,
                referenceId);

            if (!session.MappingSeen ||
                result.TargetBlockCount - targetBlockCountBefore != 1 ||
                result.TopLevelReferenceCount != 1)
            {
                throw new InvalidOperationException(
                    "Direct INSERT did not produce the required mapping, one root BTR, and one top-level BlockReference.");
            }

            session = RoofImportDirectInsertProofSession.Complete(sessionId.Value);
            completed = true;
            Write(
                editor,
                "ROOF_IMPORT_DIRECT_INSERT_RESULT " +
                $"session={sessionId.Value:N} result=completed " +
                $"sourceIdentity={sourceIdentity} " +
                $"destination={Token(destinationBlockName)} " +
                $"rootBtr={result.RootBtrHandle} " +
                $"rootEntities={result.RootEntityCount} " +
                $"targetBtrsBefore={targetBlockCountBefore} " +
                $"targetBtrsAfter={result.TargetBlockCount} " +
                $"targetBtrDelta={result.TargetBlockCount - targetBlockCountBefore} " +
                $"topLevelReferences={result.TopLevelReferenceCount} " +
                $"referenceHandle={result.ReferenceHandle} " +
                $"mappingSeen={Bool(session.MappingSeen)} " +
                $"mappingCallback={Token(session.MappingCallback)} " +
                $"mappingPairs={session.MappingPairCount} " +
                $"mappingCloned={session.MappingClonedCount} " +
                $"objectAppendedCount={session.ObjectAppendedCount}");

            RoofImportDocumentLockVetoProbe.ArmDashInsert(document);
        }
        catch (System.Exception exception)
        {
            RoofImportDirectInsertProofSession.SessionSnapshot? session = null;
            if (sessionId is { } id)
            {
                try
                {
                    session = RoofImportDirectInsertProofSession.Fail(id, exception);
                }
                catch
                {
                    // Preserve the original proof failure if session cleanup raced shutdown.
                }
            }

            Write(
                editor,
                "ROOF_IMPORT_DIRECT_INSERT_RESULT " +
                $"session={(sessionId.HasValue ? sessionId.Value.ToString("N") : "-")} " +
                $"result=failed mappingSeen={Bool(session?.MappingSeen ?? false)} " +
                $"sourceIdentity={Token(sourceIdentity)} " +
                $"objectAppendedCount={session?.ObjectAppendedCount ?? 0} " +
                $"error={Token(exception.GetType().Name)} " +
                $"message={Token(exception.Message)}");
        }
        finally
        {
            if (sessionId is { } id)
            {
                RoofImportDirectInsertProofSession.Clear(
                    id,
                    completed ? "completed" : "failed-or-cancelled");
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

    private static PreflightResult PreflightSourceDatabase(
        Database sourceDatabase,
        Editor editor)
    {
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
        var modelSpaceEntityCount = 0;
        var geometryType = string.Empty;
        foreach (ObjectId blockId in blockTable)
        {
            blockCount++;
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            if (!block.IsLayout)
            {
                var isConfirmedSystemSupport = WriteNonLayoutBlockTableRecordAudit(
                    editor,
                    transaction,
                    sourceDatabase,
                    block);
                if (!isConfirmedSystemSupport)
                {
                    throw new InvalidOperationException(
                        "The proof source contains a nested or user-defined block.");
                }
            }

            EnsureNoKrovyObjectMetadata(transaction, block, visitedDictionaries);
            foreach (ObjectId entityId in block)
            {
                var entity = (Entity)transaction.GetObject(entityId, OpenMode.ForRead);
                EnsureNoKrovyObjectMetadata(transaction, entity, visitedDictionaries);
                if (blockId != SymbolUtilityServices.GetBlockModelSpaceId(sourceDatabase))
                {
                    continue;
                }

                modelSpaceEntityCount++;
                if (entity is not Line && entity is not Polyline)
                {
                    throw new InvalidOperationException(
                        "The proof source ModelSpace must contain only one LINE or RECTANG polyline.");
                }

                geometryType = entity.GetType().Name;
            }
        }

        if (modelSpaceEntityCount != 1)
        {
            throw new InvalidOperationException(
                "The proof source ModelSpace must contain exactly one LINE or RECTANG polyline.");
        }

        return new PreflightResult(blockCount, modelSpaceEntityCount, geometryType);
    }

    private static bool WriteNonLayoutBlockTableRecordAudit(
        Editor editor,
        Transaction transaction,
        Database sourceDatabase,
        BlockTableRecord block)
    {
        try
        {
            var name = block.Name ?? string.Empty;
            var audit = AuditNonLayoutBlockTableRecord(
                transaction,
                sourceDatabase,
                block);
            Write(
                editor,
                "ROOF_IMPORT_DIRECT_INSERT_PREFLIGHT_BTR " +
                "session=- " +
                $"name={Quoted(name)} " +
                $"handle={HandleToken(block)} " +
                $"objectId={Token(block.ObjectId.ToString())} " +
                $"isLayout={Bool(block.IsLayout)} " +
                $"isAnonymous={Bool(block.IsAnonymous)} " +
                $"isFromExternalReference={Bool(block.IsFromExternalReference)} " +
                $"isFromOverlayReference={Bool(block.IsFromOverlayReference)} " +
                $"isDependent={Bool(block.IsDependent)} " +
                $"origin={OriginToken(block)} " +
                $"entityCount={block.Cast<ObjectId>().Count()} " +
                $"exactSystemName={ExactSystemName(name)} " +
                $"classification={audit.SemanticClassification} " +
                (audit.SupportRelationship.IsConfirmedSystemSupport
                    ? "action=allow-system-support"
                    : "rejectReason=nested-or-user-defined-block"));
            Write(
                editor,
                "ROOF_IMPORT_DIRECT_INSERT_BTR_AUDIT " +
                "session=- " +
                $"name={Quoted(name)} " +
                $"handle={HandleToken(block)} " +
                $"objectId={Token(block.ObjectId.ToString())} " +
                $"entityCount={audit.EntityTypes.Count} " +
                $"entityTypes={ListToken(audit.EntityTypes)} " +
                $"nestedBlockReferenceCount={audit.NestedReferences.Count} " +
                $"nestedReferencedBtrNames={ListToken(audit.NestedReferences.Select(item => item.Name))} " +
                $"nestedReferencedBtrHandles={ListToken(audit.NestedReferences.Select(item => item.Handle))} " +
                $"dimStyleReferenceCount={audit.DimStyleReferences.Count} " +
                $"dimStyleReferences={ListToken(audit.DimStyleReferences.Select(StyleReferenceToken))} " +
                $"currentDimStyleReferences={ListToken(audit.DimStyleReferences.Where(item => item.IsCurrent).Select(StyleReferenceToken))} " +
                $"currentDatabaseArrowReferences={ListToken(audit.CurrentDatabaseArrowReferences)} " +
                $"mLeaderStyleReferenceCount={audit.MLeaderStyleReferences.Count} " +
                $"mLeaderStyleReferences={ListToken(audit.MLeaderStyleReferences.Select(StyleReferenceToken))} " +
                $"currentMLeaderStyleReferences={ListToken(audit.MLeaderStyleReferences.Where(item => item.IsCurrent).Select(StyleReferenceToken))} " +
                $"otherStyleReferences={ListToken(audit.MLeaderStyleReferences.Select(StyleReferenceToken))} " +
                $"directBlockReferenceCount={audit.DirectBlockReferenceHandles.Count} " +
                $"directBlockReferenceHandles={ListToken(audit.DirectBlockReferenceHandles)} " +
                $"hasStyleObjectIdReference={Bool(audit.SupportRelationship.HasStyleObjectIdReference)} " +
                $"referencedFromModelSpace={Bool(audit.SupportRelationship.ReferencedFromModelSpace)} " +
                $"referencedFromPaperSpace={Bool(audit.SupportRelationship.ReferencedFromPaperSpace)} " +
                $"reachableFromSourceGeometry={Bool(audit.SupportRelationship.ReachableFromSourceGeometry)} " +
                $"isConfirmedSystemSupport={Bool(audit.SupportRelationship.IsConfirmedSystemSupport)} " +
                $"semanticClassification={audit.SemanticClassification} " +
                $"reason={audit.SemanticReason}");
            return audit.SupportRelationship.IsConfirmedSystemSupport;
        }
        catch (System.Exception exception)
        {
            Write(
                editor,
                "ROOF_IMPORT_DIRECT_INSERT_PREFLIGHT_BTR " +
                "session=- name=unavailable handle=unavailable objectId=unavailable " +
                "isLayout=false diagnostic=failed " +
                $"error={Token(exception.GetType().Name)} " +
                "rejectReason=nested-or-user-defined-block");
            Write(
                editor,
                "ROOF_IMPORT_DIRECT_INSERT_BTR_AUDIT " +
                "session=- name=unavailable audit=failed " +
                $"error={Token(exception.GetType().Name)} " +
                "semanticClassification=unclassified reason=audit-error");
            return false;
        }
    }

    private static BtrAuditResult AuditNonLayoutBlockTableRecord(
        Transaction transaction,
        Database sourceDatabase,
        BlockTableRecord offendingBlock)
    {
        var entityTypes = new List<string>();
        var nestedReferences = new List<NestedReferenceInfo>();
        foreach (ObjectId entityId in offendingBlock)
        {
            var entity = (Entity)transaction.GetObject(entityId, OpenMode.ForRead);
            entityTypes.Add(entity.GetType().Name);
            if (entity is not BlockReference reference)
            {
                continue;
            }

            var referencedBlock = (BlockTableRecord)transaction.GetObject(
                reference.BlockTableRecord,
                OpenMode.ForRead);
            nestedReferences.Add(new NestedReferenceInfo(
                referencedBlock.Name ?? string.Empty,
                HandleToken(referencedBlock)));
        }

        var dimStyleReferences = FindDimStyleReferences(
            transaction,
            sourceDatabase,
            offendingBlock.ObjectId);
        var currentDatabaseArrowReferences = FindCurrentDatabaseArrowReferences(
            sourceDatabase,
            offendingBlock.ObjectId);
        var mLeaderStyleReferences = FindMLeaderStyleReferences(
            transaction,
            sourceDatabase,
            offendingBlock.ObjectId);
        var directBlockReferenceHandles = FindDirectBlockReferenceHandles(
            transaction,
            offendingBlock);
        var reachability = FindSpaceReachability(
            transaction,
            sourceDatabase,
            offendingBlock.ObjectId);

        var supportRelationship = new SystemSupportRelationshipResult(
            dimStyleReferences.Count > 0 ||
            currentDatabaseArrowReferences.Count > 0 ||
            mLeaderStyleReferences.Count > 0,
            nestedReferences.Count,
            directBlockReferenceHandles.Count,
            reachability.ReferencedFromModelSpace,
            reachability.ReferencedFromPaperSpace,
            reachability.ReferencedFromModelSpace);
        var hasActualBlockReferenceRelationship =
            supportRelationship.ContainedBlockReferenceCount > 0 ||
            supportRelationship.DirectBlockReferenceCount > 0 ||
            supportRelationship.ReferencedFromModelSpace ||
            supportRelationship.ReferencedFromPaperSpace ||
            supportRelationship.ReachableFromSourceGeometry;
        var semanticClassification = supportRelationship.IsConfirmedSystemSupport
            ? "system-support"
            : hasActualBlockReferenceRelationship
            ? "user-nested"
            : "unclassified";
        var semanticReason = supportRelationship.IsConfirmedSystemSupport
            ? "exact-style-objectid-reference-without-block-reference-or-space-reachability"
            : hasActualBlockReferenceRelationship
                ? "actual-block-reference-or-space-reachability"
                : supportRelationship.HasStyleObjectIdReference
                    ? "style-reference-present-but-support-proof-incomplete"
                    : "no-authoritative-style-or-space-relationship";

        return new BtrAuditResult(
            entityTypes,
            nestedReferences,
            dimStyleReferences,
            currentDatabaseArrowReferences,
            mLeaderStyleReferences,
            directBlockReferenceHandles,
            supportRelationship,
            semanticClassification,
            semanticReason);
    }

    private static List<StyleReferenceInfo> FindDimStyleReferences(
        Transaction transaction,
        Database sourceDatabase,
        ObjectId offendingBlockId)
    {
        var references = new List<StyleReferenceInfo>();
        var table = (DimStyleTable)transaction.GetObject(
            sourceDatabase.DimStyleTableId,
            OpenMode.ForRead);
        foreach (ObjectId styleId in table)
        {
            var record = (DimStyleTableRecord)transaction.GetObject(
                styleId,
                OpenMode.ForRead);
            AddStyleReference(
                references,
                record.Name,
                "Dimblk",
                record.Dimblk == offendingBlockId,
                styleId == sourceDatabase.Dimstyle);
            AddStyleReference(
                references,
                record.Name,
                "Dimblk1",
                record.Dimblk1 == offendingBlockId,
                styleId == sourceDatabase.Dimstyle);
            AddStyleReference(
                references,
                record.Name,
                "Dimblk2",
                record.Dimblk2 == offendingBlockId,
                styleId == sourceDatabase.Dimstyle);
            AddStyleReference(
                references,
                record.Name,
                "Dimldrblk",
                record.Dimldrblk == offendingBlockId,
                styleId == sourceDatabase.Dimstyle);
        }

        return references
            .OrderBy(item => item.StyleName, StringComparer.Ordinal)
            .ThenBy(item => item.PropertyName, StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> FindCurrentDatabaseArrowReferences(
        Database sourceDatabase,
        ObjectId offendingBlockId)
    {
        var references = new List<string>();
        AddDatabaseReference(references, "Dimblk", sourceDatabase.Dimblk == offendingBlockId);
        AddDatabaseReference(references, "Dimblk1", sourceDatabase.Dimblk1 == offendingBlockId);
        AddDatabaseReference(references, "Dimblk2", sourceDatabase.Dimblk2 == offendingBlockId);
        AddDatabaseReference(
            references,
            "Dimldrblk",
            sourceDatabase.Dimldrblk == offendingBlockId);
        return references;
    }

    private static List<StyleReferenceInfo> FindMLeaderStyleReferences(
        Transaction transaction,
        Database sourceDatabase,
        ObjectId offendingBlockId)
    {
        var references = new List<StyleReferenceInfo>();
        var dictionary = (DBDictionary)transaction.GetObject(
            sourceDatabase.MLeaderStyleDictionaryId,
            OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (transaction.GetObject(entry.Value, OpenMode.ForRead) is MLeaderStyle style)
            {
                AddStyleReference(
                    references,
                    entry.Key,
                    "ArrowSymbolId",
                    style.ArrowSymbolId == offendingBlockId,
                    entry.Value == sourceDatabase.MLeaderstyle);
            }
        }

        return references
            .OrderBy(item => item.StyleName, StringComparer.Ordinal)
            .ThenBy(item => item.PropertyName, StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> FindDirectBlockReferenceHandles(
        Transaction transaction,
        BlockTableRecord offendingBlock)
    {
        var handles = new List<string>();
        foreach (ObjectId referenceId in offendingBlock.GetBlockReferenceIds(true, false))
        {
            if (transaction.GetObject(referenceId, OpenMode.ForRead) is BlockReference reference)
            {
                handles.Add(reference.Handle.ToString());
            }
        }

        handles.Sort(StringComparer.Ordinal);
        return handles;
    }

    private static SpaceReachability FindSpaceReachability(
        Transaction transaction,
        Database sourceDatabase,
        ObjectId offendingBlockId)
    {
        var modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(sourceDatabase);
        var blockTable = (BlockTable)transaction.GetObject(
            sourceDatabase.BlockTableId,
            OpenMode.ForRead);
        var referencedFromModelSpace = false;
        var referencedFromPaperSpace = false;
        foreach (ObjectId blockId in blockTable)
        {
            var space = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            if (!space.IsLayout)
            {
                continue;
            }

            var reachable = IsBlockReachable(
                transaction,
                space,
                offendingBlockId,
                new HashSet<ObjectId> { space.ObjectId });
            if (blockId == modelSpaceId)
            {
                referencedFromModelSpace |= reachable;
            }
            else
            {
                referencedFromPaperSpace |= reachable;
            }
        }

        return new SpaceReachability(
            referencedFromModelSpace,
            referencedFromPaperSpace);
    }

    private static bool IsBlockReachable(
        Transaction transaction,
        BlockTableRecord container,
        ObjectId targetBlockId,
        HashSet<ObjectId> visitedBlockTableRecords)
    {
        foreach (ObjectId entityId in container)
        {
            if (transaction.GetObject(entityId, OpenMode.ForRead) is not
                BlockReference reference)
            {
                continue;
            }

            var referencedBlockId = reference.BlockTableRecord;
            if (referencedBlockId == targetBlockId)
            {
                return true;
            }

            if (!visitedBlockTableRecords.Add(referencedBlockId))
            {
                continue;
            }

            var referencedBlock = (BlockTableRecord)transaction.GetObject(
                referencedBlockId,
                OpenMode.ForRead);
            if (IsBlockReachable(
                    transaction,
                    referencedBlock,
                    targetBlockId,
                    visitedBlockTableRecords))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddStyleReference(
        List<StyleReferenceInfo> references,
        string styleName,
        string propertyName,
        bool matches,
        bool isCurrent)
    {
        if (matches)
        {
            references.Add(new StyleReferenceInfo(styleName, propertyName, isCurrent));
        }
    }

    private static void AddDatabaseReference(
        List<string> references,
        string propertyName,
        bool matches)
    {
        if (matches)
        {
            references.Add(propertyName);
        }
    }

    private static string StyleReferenceToken(StyleReferenceInfo reference) =>
        $"{reference.StyleName}:{reference.PropertyName}";

    private static string ListToken(IEnumerable<string> values)
    {
        var items = values.ToArray();
        return items.Length == 0
            ? "none"
            : Quoted(string.Join("|", items));
    }

    private static string ExactSystemName(string name)
    {
        if (string.Equals(name, "*Model_Space", StringComparison.Ordinal))
        {
            return "model-space";
        }

        if (string.Equals(name, "*Paper_Space", StringComparison.Ordinal))
        {
            return "paper-space";
        }

        return string.Equals(name, "*Paper_Space0", StringComparison.Ordinal)
            ? "paper-space0"
            : "other";
    }

    private static string HandleToken(BlockTableRecord block)
    {
        try
        {
            return block.Handle.ToString();
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string OriginToken(BlockTableRecord block)
    {
        try
        {
            var origin = block.Origin;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{origin.X:R},{origin.Y:R},{origin.Z:R}");
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string Quoted(string value) =>
        "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    private static void EnsureNoKrovyObjectMetadata(
        Transaction transaction,
        DBObject value,
        HashSet<ObjectId> visitedDictionaries)
    {
        using (var xdata = value.XData)
        {
            if (xdata is not null &&
                xdata.Cast<TypedValue>().Any(item =>
                    item.TypeCode == (int)DxfCode.ExtendedDataRegAppName &&
                    item.Value is string regAppName &&
                    IsKrovyMarker(regAppName)))
            {
                throw new InvalidOperationException("The proof source contains KROVY XData.");
            }
        }

        if (!value.ExtensionDictionary.IsNull)
        {
            var dictionary = (DBDictionary)transaction.GetObject(
                value.ExtensionDictionary,
                OpenMode.ForRead);
            if (ContainsKrovyDictionaryMarker(transaction, dictionary, visitedDictionaries))
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
                    xdata.Cast<TypedValue>().Any(item =>
                        item.TypeCode == (int)DxfCode.ExtendedDataRegAppName &&
                        item.Value is string regAppName &&
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
        string destinationBlockName,
        ObjectId expectedRootId,
        ObjectId expectedReferenceId)
    {
        using var transaction = targetDatabase.TransactionManager.StartOpenCloseTransaction();
        var blockTable = (BlockTable)transaction.GetObject(
            targetDatabase.BlockTableId,
            OpenMode.ForRead);
        if (!blockTable.Has(destinationBlockName) ||
            blockTable[destinationBlockName] != expectedRootId)
        {
            throw new InvalidOperationException(
                "Direct INSERT completed without the expected root block definition.");
        }

        var root = (BlockTableRecord)transaction.GetObject(expectedRootId, OpenMode.ForRead);
        var currentSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(targetDatabase),
            OpenMode.ForRead);
        var referenceCount = 0;
        var referenceHandle = "-";
        foreach (ObjectId objectId in currentSpace)
        {
            if (transaction.GetObject(objectId, OpenMode.ForRead) is BlockReference reference &&
                reference.BlockTableRecord == expectedRootId)
            {
                referenceCount++;
                if (objectId == expectedReferenceId)
                {
                    referenceHandle = reference.Handle.ToString();
                }
            }
        }

        return new InsertResult(
            root.Handle.ToString(),
            root.Cast<ObjectId>().Count(),
            blockTable.Cast<ObjectId>().Count(),
            referenceCount,
            referenceHandle);
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
                "ROOF_IMPORT_DIRECT_INSERT_PROOF " +
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

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Token(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Trim()
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Replace(' ', '_');

    private readonly record struct PreflightResult(
        int BlockCount,
        int ModelSpaceEntityCount,
        string GeometryType);

    private readonly record struct BtrAuditResult(
        IReadOnlyList<string> EntityTypes,
        IReadOnlyList<NestedReferenceInfo> NestedReferences,
        IReadOnlyList<StyleReferenceInfo> DimStyleReferences,
        IReadOnlyList<string> CurrentDatabaseArrowReferences,
        IReadOnlyList<StyleReferenceInfo> MLeaderStyleReferences,
        IReadOnlyList<string> DirectBlockReferenceHandles,
        SystemSupportRelationshipResult SupportRelationship,
        string SemanticClassification,
        string SemanticReason);

    private readonly record struct SystemSupportRelationshipResult(
        bool HasStyleObjectIdReference,
        int ContainedBlockReferenceCount,
        int DirectBlockReferenceCount,
        bool ReferencedFromModelSpace,
        bool ReferencedFromPaperSpace,
        bool ReachableFromSourceGeometry)
    {
        public bool IsConfirmedSystemSupport =>
            HasStyleObjectIdReference &&
            ContainedBlockReferenceCount == 0 &&
            DirectBlockReferenceCount == 0 &&
            !ReferencedFromModelSpace &&
            !ReferencedFromPaperSpace &&
            !ReachableFromSourceGeometry;
    }

    private readonly record struct NestedReferenceInfo(string Name, string Handle);

    private readonly record struct StyleReferenceInfo(
        string StyleName,
        string PropertyName,
        bool IsCurrent);

    private readonly record struct SpaceReachability(
        bool ReferencedFromModelSpace,
        bool ReferencedFromPaperSpace);

    private readonly record struct InsertResult(
        string RootBtrHandle,
        int RootEntityCount,
        int TargetBlockCount,
        int TopLevelReferenceCount,
        string ReferenceHandle);
}
#endif
