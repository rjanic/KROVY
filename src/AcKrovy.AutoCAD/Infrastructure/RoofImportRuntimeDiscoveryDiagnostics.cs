#if DEBUG
using System.Globalization;
using System.Runtime.CompilerServices;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG-only, read-only AutoCAD 2027 event probe for Stage 2D4-C discovery.
/// This service records evidence; it is never consulted by production lifecycle code.
/// </summary>
internal static class RoofImportRuntimeDiscoveryDiagnostics
{
    private static readonly Dictionary<Document, DocumentTracker> Trackers = new();
    private static bool _isStarted;

    public static void Start()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        var documents = AcApp.DocumentManager;
        documents.DocumentCreated += DocumentCreated;
        documents.DocumentToBeDestroyed += DocumentToBeDestroyed;
        foreach (Document document in documents)
        {
            Attach(document);
        }
    }

    public static void Stop()
    {
        if (!_isStarted)
        {
            return;
        }

        _isStarted = false;
        var documents = AcApp.DocumentManager;
        documents.DocumentCreated -= DocumentCreated;
        documents.DocumentToBeDestroyed -= DocumentToBeDestroyed;
        foreach (var tracker in Trackers.Values.ToArray())
        {
            tracker.Dispose();
        }

        Trackers.Clear();
    }

    private static void DocumentCreated(object? sender, DocumentCollectionEventArgs e)
    {
        if (e.Document is not null)
        {
            Attach(e.Document);
        }
    }

    private static void DocumentToBeDestroyed(object? sender, DocumentCollectionEventArgs e)
    {
        if (e.Document is not null && Trackers.Remove(e.Document, out var tracker))
        {
            tracker.Dispose();
        }
    }

    private static void Attach(Document document)
    {
        if (!Trackers.ContainsKey(document))
        {
            Trackers.Add(document, new DocumentTracker(document));
        }
    }

    private sealed class DocumentTracker : IDisposable
    {
        private readonly Document _document;
        private readonly Database _database;
        private readonly HashSet<ObjectId> _appendedIds = new();
        private readonly HashSet<ObjectId> _mappedDestinationIds = new();
        private readonly HashSet<ObjectId> _mappedGroupIds = new();
        private readonly HashSet<ObjectId> _preExistingBtrIds = new();
        private readonly HashSet<ObjectId> _preExistingGroupIds = new();
        private readonly List<MappingSnapshot> _mappingSnapshots = new();
        private readonly HashSet<Database> _transientDatabases = new();
        private long _sequence;
        private long _operation;
        private int _eventCount;
        private int _mapCount;
        private int _objectCount;
        private int _btrCount;
        private int _xdataCount;
        private int _groupCount;
        private int _appendedIdCount;
        private int _mappedDestinationIdCount;
        private int _mappingSnapshotCount;
        private int _transientDatabaseCount;
        private string _rawCommand = string.Empty;
        private string _normalizedCommand = string.Empty;
        private bool _active;
        private bool _disposed;
        private bool _preExistingStateCaptured;
        private bool _databaseConstructedArmed;
        private Database? _sourceDatabase;
        private Database? _destinationDatabase;

        public DocumentTracker(Document document)
        {
            _document = document;
            _database = document.Database;
            Subscribe();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Unsubscribe();
            ClearOperationState();
        }

        private void Subscribe()
        {
            _document.CommandWillStart += CommandWillStart;
            _document.CommandEnded += CommandEnded;
            _document.CommandCancelled += CommandCancelled;
            _document.CommandFailed += CommandFailed;
            _database.ObjectAppended += ObjectAppended;
            _database.BeginInsert += BeginInsert;
            _database.InsertMappingAvailable += InsertMappingAvailable;
            _database.InsertEnded += InsertEnded;
            _database.InsertAborted += InsertAborted;
            _database.BeginDeepClone += BeginDeepClone;
            _database.BeginDeepCloneTranslation += BeginDeepCloneTranslation;
            _database.DeepCloneEnded += DeepCloneEnded;
            _database.DeepCloneAborted += DeepCloneAborted;
            _database.WblockNotice += WblockNotice;
            _database.BeginWblockBlock += BeginWblockBlock;
            _database.BeginWblockEntireDatabase += BeginWblockEntireDatabase;
            _database.BeginWblockObjects += BeginWblockObjects;
            _database.BeginWblockSelectedObjects += BeginWblockSelectedObjects;
            _database.WblockMappingAvailable += WblockMappingAvailable;
            _database.WblockEnded += WblockEnded;
            _database.WblockAborted += WblockAborted;
        }

        private void Unsubscribe()
        {
            _document.CommandWillStart -= CommandWillStart;
            _document.CommandEnded -= CommandEnded;
            _document.CommandCancelled -= CommandCancelled;
            _document.CommandFailed -= CommandFailed;
            _database.ObjectAppended -= ObjectAppended;
            _database.BeginInsert -= BeginInsert;
            _database.InsertMappingAvailable -= InsertMappingAvailable;
            _database.InsertEnded -= InsertEnded;
            _database.InsertAborted -= InsertAborted;
            _database.BeginDeepClone -= BeginDeepClone;
            _database.BeginDeepCloneTranslation -= BeginDeepCloneTranslation;
            _database.DeepCloneEnded -= DeepCloneEnded;
            _database.DeepCloneAborted -= DeepCloneAborted;
            _database.WblockNotice -= WblockNotice;
            _database.BeginWblockBlock -= BeginWblockBlock;
            _database.BeginWblockEntireDatabase -= BeginWblockEntireDatabase;
            _database.BeginWblockObjects -= BeginWblockObjects;
            _database.BeginWblockSelectedObjects -= BeginWblockSelectedObjects;
            _database.WblockMappingAvailable -= WblockMappingAvailable;
            _database.WblockEnded -= WblockEnded;
            _database.WblockAborted -= WblockAborted;
            DisarmDatabaseConstructionDiscovery();
            DetachAllTransientDatabases();
        }

        private void CommandWillStart(object? sender, CommandEventArgs e)
        {
            var raw = e.GlobalCommandName ?? string.Empty;
            RoofImportApprovedInsertProofSession.ObserveCommandWillStart(_document, raw);
            var normalized = LiveGeometryCommandRules.NormalizeCommandName(raw);
            if (!IsDiscoveryCommand(normalized))
            {
                return;
            }

            ClearOperationState();
            _operation++;
            _rawCommand = raw;
            _normalizedCommand = normalized;
            _active = true;
            _sourceDatabase = _database;
            var isWblock = IsWblockCommand(normalized);
            _destinationDatabase = isWblock ? null : _database;
            if (isWblock)
            {
                ArmDatabaseConstructionDiscovery();
            }

            CapturePreExistingState();
            WriteEvent("CommandWillStart", _database, _sourceDatabase, _destinationDatabase);
        }

        private void CommandEnded(object? sender, CommandEventArgs e)
        {
            RoofImportApprovedInsertProofSession.ObserveCommandTerminal(
                _document,
                e.GlobalCommandName,
                "completed");
            if (!IsCurrentDiscoveryCommand(e.GlobalCommandName))
            {
                return;
            }

            WriteEvent("CommandEnded", _database, _sourceDatabase, _destinationDatabase);
            DumpCurrentOperationGraph("CommandEnded", _database);
            DumpRelevantGroups("CommandEnded", _database);
            WriteSummary("CommandEnded", "completed");
            ClearOperationState();
        }

        private void CommandCancelled(object? sender, CommandEventArgs e)
        {
            RoofImportApprovedInsertProofSession.ObserveCommandTerminal(
                _document,
                e.GlobalCommandName,
                "cancelled");
            FinishAbortedCommand("CommandCancelled", e.GlobalCommandName);
        }

        private void CommandFailed(object? sender, CommandEventArgs e)
        {
            RoofImportApprovedInsertProofSession.ObserveCommandTerminal(
                _document,
                e.GlobalCommandName,
                "failed");
            FinishAbortedCommand("CommandFailed", e.GlobalCommandName);
        }

        private void FinishAbortedCommand(string callback, string? command)
        {
            if (!IsCurrentDiscoveryCommand(command))
            {
                return;
            }

            WriteEvent(callback, _database, _sourceDatabase, _destinationDatabase);
            WriteSummary(callback, "aborted");
            ClearOperationState();
        }

        private void ObjectAppended(object? sender, ObjectEventArgs e)
        {
            if (!_active || e.DBObject is null)
            {
                return;
            }

            var objectId = e.DBObject.ObjectId;
            if (!objectId.IsNull && _appendedIds.Add(objectId))
            {
                _appendedIdCount++;
            }

            RoofImportApprovedInsertProofSession.ObserveObjectAppended(
                _document,
                objectId);
            RoofImportDirectInsertProofSession.ObserveObjectAppended(
                _document,
                objectId);

            DumpObject(
                "ObjectAppended",
                e.DBObject,
                transaction: null,
                knownState: "new-appended",
                sourceDatabase: _sourceDatabase,
                destinationDatabase: _destinationDatabase);
        }

        private void BeginInsert(object? sender, BeginInsertEventArgs e)
        {
            EnsureActive("INSERT");
            RoofImportApprovedInsertProofSession.ObserveLifecycle(
                _document,
                "BeginInsert");
            RoofImportDirectInsertProofSession.ObserveLifecycle(
                _document,
                "BeginInsert");
            var eventDatabase = EventDatabase(sender);
            _sourceDatabase = e.From;
            _destinationDatabase = eventDatabase;
            WriteEvent("BeginInsert", eventDatabase, e.From, eventDatabase);
        }

        private void InsertMappingAvailable(object? sender, IdMappingEventArgs e)
        {
            EnsureActive("INSERT");
            RoofImportApprovedInsertProofSession.ObserveMapping(
                _document,
                "InsertMappingAvailable",
                e.IdMapping);
            RoofImportDirectInsertProofSession.ObserveMapping(
                _document,
                "InsertMappingAvailable",
                e.IdMapping);
            var eventDatabase = EventDatabase(sender);
            WriteEvent(
                "InsertMappingAvailable",
                eventDatabase,
                e.IdMapping.OriginalDatabase,
                e.IdMapping.DestinationDatabase,
                e.IdMapping.DeepCloneContext.ToString());
            DumpMapping("InsertMappingAvailable", e.IdMapping);
        }

        private void InsertEnded(object? sender, EventArgs e)
        {
            if (!_active)
            {
                return;
            }

            RoofImportApprovedInsertProofSession.ObserveLifecycle(
                _document,
                "InsertEnded");
            RoofImportDirectInsertProofSession.ObserveLifecycle(
                _document,
                "InsertEnded");

            var eventDatabase = EventDatabase(sender);
            WriteEvent("InsertEnded", eventDatabase, _sourceDatabase, _destinationDatabase);
            DumpCurrentOperationGraph("InsertEnded", _database);
            DumpRelevantGroups("InsertEnded", _database);
            WriteSummary("InsertEnded", "checkpoint");
        }

        private void InsertAborted(object? sender, EventArgs e)
        {
            if (_active)
            {
                RoofImportApprovedInsertProofSession.ObserveLifecycle(
                    _document,
                    "InsertAborted");
                RoofImportDirectInsertProofSession.ObserveLifecycle(
                    _document,
                    "InsertAborted");
                WriteEvent(
                    "InsertAborted",
                    EventDatabase(sender),
                    _sourceDatabase,
                    _destinationDatabase);
                WriteSummary("InsertAborted", "aborted");
            }
        }

        private void BeginDeepClone(object? sender, IdMappingEventArgs e)
        {
            if (!IsDiscoveryCloneContext(e.IdMapping.DeepCloneContext))
            {
                return;
            }

            EnsureActive(e.IdMapping.DeepCloneContext.ToString());
            RoofImportApprovedInsertProofSession.ObserveLifecycle(
                _document,
                "BeginDeepClone");
            RoofImportDirectInsertProofSession.ObserveLifecycle(
                _document,
                "BeginDeepClone");
            var eventDatabase = EventDatabase(sender);
            WriteEvent(
                "BeginDeepClone",
                eventDatabase,
                e.IdMapping.OriginalDatabase,
                e.IdMapping.DestinationDatabase,
                e.IdMapping.DeepCloneContext.ToString());
            DumpMapping("BeginDeepClone", e.IdMapping);
        }

        private void BeginDeepCloneTranslation(object? sender, IdMappingEventArgs e)
        {
            if (!IsDiscoveryCloneContext(e.IdMapping.DeepCloneContext))
            {
                return;
            }

            EnsureActive(e.IdMapping.DeepCloneContext.ToString());
            RoofImportApprovedInsertProofSession.ObserveMapping(
                _document,
                "BeginDeepCloneTranslation",
                e.IdMapping);
            RoofImportDirectInsertProofSession.ObserveMapping(
                _document,
                "BeginDeepCloneTranslation",
                e.IdMapping);
            RoofImportApprovedInsertProofSession.ObserveLifecycle(
                _document,
                "BeginDeepCloneTranslation");
            RoofImportDirectInsertProofSession.ObserveLifecycle(
                _document,
                "BeginDeepCloneTranslation");
            var eventDatabase = EventDatabase(sender);
            WriteEvent(
                "BeginDeepCloneTranslation",
                eventDatabase,
                e.IdMapping.OriginalDatabase,
                e.IdMapping.DestinationDatabase,
                e.IdMapping.DeepCloneContext.ToString());
            DumpMapping("BeginDeepCloneTranslation", e.IdMapping);
        }

        private void DeepCloneEnded(object? sender, EventArgs e)
        {
            if (_active)
            {
                RoofImportApprovedInsertProofSession.ObserveLifecycle(
                    _document,
                    "DeepCloneEnded");
                RoofImportDirectInsertProofSession.ObserveLifecycle(
                    _document,
                    "DeepCloneEnded");
                WriteEvent(
                    "DeepCloneEnded",
                    EventDatabase(sender),
                    _sourceDatabase,
                    _destinationDatabase);
                WriteSummary("DeepCloneEnded", "checkpoint");
            }
        }

        private void DeepCloneAborted(object? sender, EventArgs e)
        {
            if (_active)
            {
                RoofImportApprovedInsertProofSession.ObserveLifecycle(
                    _document,
                    "DeepCloneAborted");
                RoofImportDirectInsertProofSession.ObserveLifecycle(
                    _document,
                    "DeepCloneAborted");
                WriteEvent(
                    "DeepCloneAborted",
                    EventDatabase(sender),
                    _sourceDatabase,
                    _destinationDatabase);
                WriteSummary("DeepCloneAborted", "aborted");
            }
        }

        private void WblockNotice(object? sender, WblockNoticeEventArgs e)
        {
            EnsureActive("WBLOCK");
            ArmDatabaseConstructionDiscovery();
            var eventDatabase = EventDatabase(sender);
            _sourceDatabase = e.From;
            WriteEvent(
                "WblockNotice",
                eventDatabase,
                e.From,
                _destinationDatabase,
                extra: "scope=source-wblock");
        }

        private void BeginWblockBlock(object? sender, BeginWblockBlockEventArgs e)
        {
            EnsureActive("WBLOCK");
            var eventDatabase = ObserveWblockDatabases(sender, e.From);
            _sourceDatabase = e.From;
            WriteEvent("BeginWblockBlock", eventDatabase, e.From, _destinationDatabase);
            WriteBtrById("BeginWblockBlock", e.BlockId, "source-selected-block");
        }

        private void BeginWblockEntireDatabase(
            object? sender,
            BeginWblockEntireDatabaseEventArgs e)
        {
            EnsureActive("WBLOCK");
            var eventDatabase = ObserveWblockDatabases(sender, e.From);
            _sourceDatabase = e.From;
            WriteEvent(
                "BeginWblockEntireDatabase",
                eventDatabase,
                e.From,
                _destinationDatabase);
        }

        private void BeginWblockObjects(object? sender, BeginWblockObjectsEventArgs e)
        {
            EnsureActive("WBLOCK");
            var eventDatabase = ObserveWblockDatabases(sender, e.From);
            _sourceDatabase = e.From;
            WriteEvent(
                "BeginWblockObjects",
                eventDatabase,
                e.From,
                e.IdMapping.DestinationDatabase ?? eventDatabase,
                e.IdMapping.DeepCloneContext.ToString());
            DumpMapping("BeginWblockObjects", e.IdMapping);
        }

        private void BeginWblockSelectedObjects(
            object? sender,
            BeginWblockSelectedObjectsEventArgs e)
        {
            EnsureActive("WBLOCK");
            var eventDatabase = ObserveWblockDatabases(sender, e.From);
            _sourceDatabase = e.From;
            WriteEvent(
                "BeginWblockSelectedObjects",
                eventDatabase,
                e.From,
                _destinationDatabase,
                extra: "insertionPoint=" + FormatPoint(e.InsertionPoint));
        }

        private void WblockMappingAvailable(object? sender, IdMappingEventArgs e)
        {
            EnsureActive("WBLOCK");
            var eventDatabase = EventDatabase(sender);
            WriteEvent(
                "WblockMappingAvailable",
                eventDatabase,
                e.IdMapping.OriginalDatabase,
                e.IdMapping.DestinationDatabase,
                e.IdMapping.DeepCloneContext.ToString());
            DumpMapping("WblockMappingAvailable", e.IdMapping);
            DumpCurrentOperationGraph(
                "WblockMappingAvailable",
                e.IdMapping.DestinationDatabase);
            DumpRelevantGroups(
                "WblockMappingAvailable",
                e.IdMapping.DestinationDatabase);
        }

        private void WblockEnded(object? sender, EventArgs e)
        {
            if (_active)
            {
                var eventDatabase = EventDatabase(sender);
                WriteEvent(
                    "WblockEnded",
                    eventDatabase,
                    _sourceDatabase,
                    _destinationDatabase);
                DumpCurrentOperationGraph("WblockEnded", eventDatabase);
                DumpRelevantGroups("WblockEnded", eventDatabase);
                WriteSummary("WblockEnded", "checkpoint");
                DetachTransientDatabase(eventDatabase);
                DisarmDatabaseConstructionDiscovery();
            }
        }

        private void WblockAborted(object? sender, EventArgs e)
        {
            if (_active)
            {
                var eventDatabase = EventDatabase(sender);
                WriteEvent(
                    "WblockAborted",
                    eventDatabase,
                    _sourceDatabase,
                    _destinationDatabase);
                WriteSummary("WblockAborted", "aborted");
                DetachTransientDatabase(eventDatabase);
                DisarmDatabaseConstructionDiscovery();
            }
        }

        private void ArmDatabaseConstructionDiscovery()
        {
            if (_databaseConstructedArmed)
            {
                return;
            }

            Database.DatabaseConstructed += DatabaseConstructed;
            _databaseConstructedArmed = true;
        }

        private void DisarmDatabaseConstructionDiscovery()
        {
            if (!_databaseConstructedArmed)
            {
                return;
            }

            Database.DatabaseConstructed -= DatabaseConstructed;
            _databaseConstructedArmed = false;
        }

        private void DatabaseConstructed(object? sender, EventArgs e)
        {
            if (!_active ||
                !IsWblockCommand(_normalizedCommand) ||
                !ReferenceEquals(AcApp.DocumentManager.MdiActiveDocument, _document) ||
                sender is not Database candidate ||
                candidate.IsDisposed ||
                ReferenceEquals(candidate, _database) ||
                IsDocumentDatabase(candidate) ||
                !AttachTransientDatabase(candidate))
            {
                return;
            }

            _destinationDatabase = candidate;
            _transientDatabaseCount++;
            WriteEvent(
                "DatabaseConstructed",
                candidate,
                _sourceDatabase,
                candidate,
                extra:
                    "scope=transient-wblock subscription=attached" +
                    $" transientIndex={_transientDatabaseCount.ToString(CultureInfo.InvariantCulture)}");
        }

        private bool AttachTransientDatabase(Database database)
        {
            if (!_transientDatabases.Add(database))
            {
                return false;
            }

            database.ObjectAppended += ObjectAppended;
            database.BeginDeepClone += BeginDeepClone;
            database.BeginDeepCloneTranslation += BeginDeepCloneTranslation;
            database.DeepCloneEnded += DeepCloneEnded;
            database.DeepCloneAborted += DeepCloneAborted;
            database.BeginWblockBlock += BeginWblockBlock;
            database.BeginWblockEntireDatabase += BeginWblockEntireDatabase;
            database.BeginWblockObjects += BeginWblockObjects;
            database.BeginWblockSelectedObjects += BeginWblockSelectedObjects;
            database.WblockMappingAvailable += WblockMappingAvailable;
            database.WblockEnded += WblockEnded;
            database.WblockAborted += WblockAborted;
            database.DatabaseToBeDestroyed += TransientDatabaseToBeDestroyed;
            return true;
        }

        private void DetachTransientDatabase(Database database)
        {
            if (!_transientDatabases.Remove(database))
            {
                return;
            }

            if (!database.IsDisposed)
            {
                database.ObjectAppended -= ObjectAppended;
                database.BeginDeepClone -= BeginDeepClone;
                database.BeginDeepCloneTranslation -= BeginDeepCloneTranslation;
                database.DeepCloneEnded -= DeepCloneEnded;
                database.DeepCloneAborted -= DeepCloneAborted;
                database.BeginWblockBlock -= BeginWblockBlock;
                database.BeginWblockEntireDatabase -= BeginWblockEntireDatabase;
                database.BeginWblockObjects -= BeginWblockObjects;
                database.BeginWblockSelectedObjects -= BeginWblockSelectedObjects;
                database.WblockMappingAvailable -= WblockMappingAvailable;
                database.WblockEnded -= WblockEnded;
                database.WblockAborted -= WblockAborted;
                database.DatabaseToBeDestroyed -= TransientDatabaseToBeDestroyed;
            }

            ReleaseTransientReferences(database);
        }

        private void DetachAllTransientDatabases()
        {
            foreach (var database in _transientDatabases.ToArray())
            {
                DetachTransientDatabase(database);
            }
        }

        private void TransientDatabaseToBeDestroyed(object? sender, EventArgs e)
        {
            if (sender is not Database database)
            {
                return;
            }

            if (_active)
            {
                WriteEvent(
                    "DatabaseToBeDestroyed",
                    database,
                    _sourceDatabase,
                    database,
                    extra: "scope=transient-wblock subscription=detaching");
            }

            DetachTransientDatabase(database);
        }

        private void ReleaseTransientReferences(Database database)
        {
            _appendedIds.RemoveWhere(id => BelongsToDatabase(id, database));
            _mappedDestinationIds.RemoveWhere(id => BelongsToDatabase(id, database));
            _mappedGroupIds.RemoveWhere(id => BelongsToDatabase(id, database));
            _mappingSnapshots.RemoveAll(snapshot =>
                BelongsToDatabase(snapshot.Source, database) ||
                BelongsToDatabase(snapshot.Destination, database));
            if (ReferenceEquals(_destinationDatabase, database))
            {
                _destinationDatabase = null;
            }
        }

        private Database ObserveWblockDatabases(object? sender, Database? sourceDatabase)
        {
            var eventDatabase = EventDatabase(sender);
            _sourceDatabase = sourceDatabase ?? _sourceDatabase;
            if (!ReferenceEquals(eventDatabase, _database))
            {
                _destinationDatabase = eventDatabase;
            }

            return eventDatabase;
        }

        private Database EventDatabase(object? sender) =>
            sender as Database ?? _database;

        private static bool IsDocumentDatabase(Database candidate)
        {
            foreach (Document document in AcApp.DocumentManager)
            {
                if (ReferenceEquals(document.Database, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private void DumpMapping(string callback, IdMapping mapping)
        {
            _sourceDatabase = mapping.OriginalDatabase ?? _sourceDatabase;
            _destinationDatabase = mapping.DestinationDatabase ?? _destinationDatabase;

            Transaction? sourceTransaction = null;
            Transaction? destinationTransaction = null;
            try
            {
                sourceTransaction = TryStartReadTransaction(mapping.OriginalDatabase);
                destinationTransaction = ReferenceEquals(
                        mapping.OriginalDatabase,
                        mapping.DestinationDatabase)
                    ? sourceTransaction
                    : TryStartReadTransaction(mapping.DestinationDatabase);

                foreach (IdPair pair in mapping)
                {
                    var sourceObject = TryOpen(pair.Key, sourceTransaction);
                    var destinationObject = TryOpen(pair.Value, destinationTransaction);
                    var destinationBtr = DescribeOwnerBtr(destinationObject, destinationTransaction);
                    Write(
                        "ROOF_IMPORT_MAP",
                        callback,
                        mapping.DestinationDatabase,
                        mapping.OriginalDatabase,
                        mapping.DestinationDatabase,
                        $"cloneContext={Token(mapping.DeepCloneContext.ToString())}" +
                        $" key={DescribeId(pair.Key)} value={DescribeId(pair.Value)}" +
                        $" isCloned={Bool(pair.IsCloned)} isPrimary={Bool(pair.IsPrimary)}" +
                        $" sourceHandle={DescribeHandle(sourceObject, pair.Key)}" +
                        $" destinationHandle={DescribeHandle(destinationObject, pair.Value)}" +
                        $" sourceType={DescribeType(sourceObject)}" +
                        $" destinationType={DescribeType(destinationObject)}" +
                        $" destinationOwner={DescribeOwner(destinationObject)}" +
                        $" destinationBtrId={DescribeId(destinationBtr.Id)}" +
                        $" destinationBtrName={Token(destinationBtr.Name)}");
                    _mapCount++;

                    _mappingSnapshots.Add(new MappingSnapshot(
                        pair.Key,
                        pair.Value,
                        pair.IsCloned,
                        pair.IsPrimary));
                    _mappingSnapshotCount++;
                    if (!pair.Value.IsNull && _mappedDestinationIds.Add(pair.Value))
                    {
                        _mappedDestinationIdCount++;
                    }

                    if (destinationObject is Group)
                    {
                        _mappedGroupIds.Add(pair.Value);
                    }

                    if (destinationObject is Entity or BlockTableRecord or Group)
                    {
                        DumpObject(
                            callback + ":destination",
                            destinationObject,
                            destinationTransaction,
                            pair.IsCloned ? "mapped-new" : "mapped-reused",
                            mapping.OriginalDatabase,
                            mapping.DestinationDatabase);
                    }
                }
            }
            catch (System.Exception exception)
            {
                WriteError(callback + ":map", mapping.DestinationDatabase, exception);
            }
            finally
            {
                if (!ReferenceEquals(sourceTransaction, destinationTransaction))
                {
                    destinationTransaction?.Dispose();
                }

                sourceTransaction?.Dispose();
            }
        }

        private void DumpCurrentOperationGraph(string callback, Database database)
        {
            using var transaction = TryStartReadTransaction(database);
            if (transaction is null)
            {
                WriteError(callback + ":graph", database, "transaction-unavailable");
                return;
            }

            var candidateIds = _appendedIds
                .Concat(_mappedDestinationIds)
                .Where(id => BelongsToDatabase(id, database))
                .Distinct()
                .ToArray();
            var visitedBtrs = new HashSet<ObjectId>();
            var rootCount = 0;
            foreach (var id in candidateIds)
            {
                var candidate = TryOpen(id, transaction);
                if (candidate is BlockReference blockReference)
                {
                    rootCount++;
                    DumpObject(
                        callback + ":graph-root",
                        blockReference,
                        transaction,
                        ResolveKnownState(id),
                        _sourceDatabase,
                        database);
                    DumpBtrGraph(
                        callback,
                        blockReference.BlockTableRecord,
                        database,
                        transaction,
                        visitedBtrs,
                        depth: 0);
                }
                else if (candidate is Entity entity)
                {
                    rootCount++;
                    DumpObject(
                        callback + ":top-level",
                        entity,
                        transaction,
                        ResolveKnownState(id),
                        _sourceDatabase,
                        database);
                }
                else if (candidate is BlockTableRecord btr)
                {
                    DumpBtrGraph(
                        callback,
                        btr.ObjectId,
                        database,
                        transaction,
                        visitedBtrs,
                        depth: 0);
                }
            }

            if (rootCount == 0)
            {
                Write(
                    "ROOF_IMPORT_OBJECT",
                    callback + ":graph",
                    database,
                    _sourceDatabase,
                    database,
                    "result=none");
                _objectCount++;
            }
        }

        private void DumpBtrGraph(
            string callback,
            ObjectId btrId,
            Database database,
            Transaction transaction,
            HashSet<ObjectId> visitedBtrs,
            int depth)
        {
            if (btrId.IsNull)
            {
                return;
            }

            if (!visitedBtrs.Add(btrId))
            {
                Write(
                    "ROOF_IMPORT_BTR",
                    callback + ":graph-cycle",
                    database,
                    _sourceDatabase,
                    database,
                    $"depth={depth.ToString(CultureInfo.InvariantCulture)}" +
                    $" btrId={DescribeId(btrId)} cycle=true");
                _btrCount++;
                return;
            }

            if (TryOpen(btrId, transaction) is not BlockTableRecord btr)
            {
                WriteError(callback + ":graph-btr", database, "btr-open-failed");
                return;
            }

            WriteBtr(callback + ":graph", btr, depth, ResolveKnownState(btrId));
            foreach (ObjectId memberId in btr)
            {
                var member = TryOpen(memberId, transaction);
                if (member is null)
                {
                    WriteError(callback + ":graph-member", database, "member-open-failed");
                    continue;
                }

                DumpObject(
                    callback + ":graph-member",
                    member,
                    transaction,
                    ResolveKnownState(memberId),
                    _sourceDatabase,
                    database,
                    extra: $"depth={(depth + 1).ToString(CultureInfo.InvariantCulture)}");
                if (member is BlockReference nested)
                {
                    DumpBtrGraph(
                        callback,
                        nested.BlockTableRecord,
                        database,
                        transaction,
                        visitedBtrs,
                        depth + 1);
                }
            }
        }

        private void DumpRelevantGroups(string callback, Database database)
        {
            using var transaction = TryStartReadTransaction(database);
            if (transaction is null)
            {
                WriteError(callback + ":groups", database, "transaction-unavailable");
                return;
            }

            var relevantIds = _appendedIds
                .Concat(_mappedDestinationIds)
                .Where(id => BelongsToDatabase(id, database))
                .ToHashSet();
            var emitted = 0;
            try
            {
                if (transaction.GetObject(database.GroupDictionaryId, OpenMode.ForRead) is not
                    DBDictionary dictionary)
                {
                    return;
                }

                foreach (DBDictionaryEntry entry in dictionary)
                {
                    if (transaction.GetObject(entry.Value, OpenMode.ForRead) is not Group group)
                    {
                        continue;
                    }

                    var members = group.GetAllEntityIds();
                    var relevantMembers = members.Where(relevantIds.Contains).ToArray();
                    var mapped = _mappedGroupIds.Contains(entry.Value);
                    if (!mapped && relevantMembers.Length == 0)
                    {
                        continue;
                    }

                    Write(
                        "ROOF_IMPORT_GROUP",
                        callback,
                        database,
                        _sourceDatabase,
                        database,
                        $"groupId={DescribeId(entry.Value)} groupHandle={DescribeHandle(group, entry.Value)}" +
                        $" groupName={Token(entry.Key)} classification=Group" +
                        $" mapped={Bool(mapped)} preExisting={PreExistingStatus(entry.Value, database)}" +
                        $" memberCount={members.Length.ToString(CultureInfo.InvariantCulture)}" +
                        $" relevantMemberIds={Token(string.Join(",", relevantMembers.Select(DescribeId)))}");
                    _groupCount++;
                    emitted++;
                }
            }
            catch (System.Exception exception)
            {
                WriteError(callback + ":groups", database, exception);
            }

            if (emitted == 0)
            {
                Write(
                    "ROOF_IMPORT_GROUP",
                    callback,
                    database,
                    _sourceDatabase,
                    database,
                    "result=none");
                _groupCount++;
            }
        }

        private void DumpObject(
            string callback,
            DBObject value,
            Transaction? transaction,
            string knownState,
            Database? sourceDatabase,
            Database? destinationDatabase,
            string? extra = null)
        {
            try
            {
                var ownerBtr = DescribeOwnerBtr(value, transaction);
                var classification = value switch
                {
                    Group => "Group",
                    BlockTableRecord => "BlockTableRecord",
                    BlockReference => "BlockReference",
                    Entity => "Entity",
                    _ => "DBObject",
                };
                var referenceBtr = value is BlockReference blockReference
                    ? DescribeBtr(blockReference.BlockTableRecord, transaction)
                    : BtrDescription.Empty;
                Write(
                    "ROOF_IMPORT_OBJECT",
                    callback,
                    value.Database,
                    sourceDatabase,
                    destinationDatabase,
                    $"objectId={DescribeId(value.ObjectId)} handle={DescribeHandle(value, value.ObjectId)}" +
                    $" type={DescribeType(value)} classification={classification}" +
                    $" owner={DescribeOwner(value)} ownerBtrId={DescribeId(ownerBtr.Id)}" +
                    $" ownerBtrName={Token(ownerBtr.Name)}" +
                    $" referencedBtrId={DescribeId(referenceBtr.Id)}" +
                    $" referencedBtrName={Token(referenceBtr.Name)}" +
                    $" state={Token(knownState)}" +
                    (string.IsNullOrWhiteSpace(extra) ? string.Empty : " " + extra));
                _objectCount++;

                if (value is BlockTableRecord btr)
                {
                    WriteBtr(callback, btr, depth: 0, knownState);
                }

                if (value is Entity entity)
                {
                    DumpRawXData(callback, entity, sourceDatabase, destinationDatabase);
                    DumpDecodedMetadata(
                        callback,
                        entity,
                        transaction,
                        sourceDatabase,
                        destinationDatabase);
                }
            }
            catch (System.Exception exception)
            {
                WriteError(callback + ":object", value.Database, exception);
            }
        }

        private void DumpRawXData(
            string callback,
            Entity entity,
            Database? sourceDatabase,
            Database? destinationDatabase)
        {
            try
            {
                using var xdata = entity.XData;
                if (xdata is null)
                {
                    return;
                }

                string? currentRegApp = null;
                var handles1005 = new List<string>();
                void Flush()
                {
                    if (currentRegApp is null || !IsKrovyRegApp(currentRegApp))
                    {
                        handles1005.Clear();
                        return;
                    }

                    Write(
                        "ROOF_IMPORT_XDATA",
                        callback + ":raw",
                        entity.Database,
                        sourceDatabase,
                        destinationDatabase,
                        $"objectId={DescribeId(entity.ObjectId)} handle={DescribeHandle(entity, entity.ObjectId)}" +
                        $" regApp={Token(currentRegApp)} final1005={Token(string.Join(",", handles1005))}");
                    _xdataCount++;
                    handles1005.Clear();
                }

                foreach (var typedValue in xdata.AsArray())
                {
                    if (typedValue.TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                    {
                        Flush();
                        currentRegApp = Convert.ToString(
                            typedValue.Value,
                            CultureInfo.InvariantCulture);
                    }
                    else if (typedValue.TypeCode == (int)DxfCode.ExtendedDataHandle)
                    {
                        handles1005.Add(
                            Convert.ToString(typedValue.Value, CultureInfo.InvariantCulture) ?? "-");
                    }
                }

                Flush();
            }
            catch (System.Exception exception)
            {
                WriteError(callback + ":raw-xdata", entity.Database, exception);
            }
        }

        private void DumpDecodedMetadata(
            string callback,
            Entity entity,
            Transaction? transaction,
            Database? sourceDatabase,
            Database? destinationDatabase)
        {
            try
            {
                var roof = RoofDefinitionStore.Read(entity);
                if (roof.Exists)
                {
                    WriteXData(
                        callback,
                        entity,
                        "RoofDefinition",
                        $"valid={Bool(roof.Data is not null)} schema={Int(roof.Data?.SchemaVersion)}" +
                        $" role=RoofDefinition roofKind={Token(roof.Data?.Kind.ToString())}" +
                        $" owner=- error={Token(roof.Error.ToString())}",
                        sourceDatabase,
                        destinationDatabase);
                }

                var display = RoofDisplayStore.Read(entity);
                if (display.Exists)
                {
                    WriteXData(
                        callback,
                        entity,
                        "RoofDisplay",
                        $"valid={Bool(display.Data is not null)} schema={Int(display.Data?.SchemaVersion)}" +
                        $" owner={Token(display.OwnerReference)} role={Token(display.Data?.Role.ToString())}" +
                        $" ownerFrom1005={Bool(display.OwnerReferenceFromCloneHandle)}" +
                        $" error={Token(display.Error.ToString())}",
                        sourceDatabase,
                        destinationDatabase);
                }

                var generated = RoofGeneratedTimberStore.Read(entity);
                if (generated.Exists)
                {
                    var data = generated.Data;
                    WriteXData(
                        callback,
                        entity,
                        "Generated",
                        $"valid={Bool(data is not null)} schema={Int(data?.SchemaVersion)}" +
                        $" owner={Token(data?.RoofOwnerReference)} role=Generated" +
                        $" face={Token(data?.RoofFace.ToString())} stationIndex={Int(data?.StationIndex)}" +
                        $" memberKind={Token(data?.MemberKind.ToString())}" +
                        $" ownerFrom1005={Bool(generated.OwnerReferenceFromCloneHandle)}" +
                        $" error={Token(generated.Error.ToString())}",
                        sourceDatabase,
                        destinationDatabase);
                }

                var attached = RoofAttachedManualTimberStore.Read(entity);
                if (attached.Exists)
                {
                    var data = attached.Data;
                    WriteXData(
                        callback,
                        entity,
                        "AttachedManual",
                        $"valid={Bool(data is not null)} schema={Int(data?.SchemaVersion)}" +
                        $" owner={Token(data?.RoofOwnerReference)} role={Token(data?.Role.ToString())}" +
                        $" childIdentity={Token(data?.ChildIdentity)} origin={Token(data?.Origin.ToString())}" +
                        $" face={Token(data?.AnchorGeneratedMemberKey?.RoofFace.ToString())}" +
                        $" stationIndex={Int(data?.AnchorGeneratedMemberKey?.StationIndex)}" +
                        $" anchor={FormatAnchor(data?.AnchorGeneratedMemberKey)}" +
                        $" relativeSegment={FormatRelativeSegment(data?.RelativeSegment)}",
                        sourceDatabase,
                        destinationDatabase);
                }

                if (transaction is not null &&
                    ElementDataStore.TryRead(entity, transaction, out var element) &&
                    element is not null)
                {
                    var storage = HasRegApp(entity, "DECORAIR_ACADKROVY")
                        ? "portable"
                        : "legacy";
                    WriteXData(
                        callback,
                        entity,
                        storage == "legacy" ? "LegacyTimber" : "GenericTimber",
                        $"valid=true schema={element.SchemaVersion.ToString(CultureInfo.InvariantCulture)}" +
                        $" elementId={Token(element.ElementId)} role=GenericTimber" +
                        $" storage={storage}",
                        sourceDatabase,
                        destinationDatabase);
                }

                if (ElementLabelStore.TryRead(entity, out var label) && label is not null)
                {
                    WriteXData(
                        callback,
                        entity,
                        "ElementLabel",
                        $"valid=true schema={label.SchemaVersion.ToString(CultureInfo.InvariantCulture)}" +
                        $" elementId={Token(label.ElementId)} sourceHandle={Token(label.SourceHandle)}" +
                        $" role={Token(label.ComponentRole.ToString())}",
                        sourceDatabase,
                        destinationDatabase);
                }

                if (SlopeArrowStore.TryRead(entity, out var slopeArrow) && slopeArrow is not null)
                {
                    WriteXData(
                        callback,
                        entity,
                        "SlopeArrow",
                        $"valid=true schema={slopeArrow.SchemaVersion.ToString(CultureInfo.InvariantCulture)}" +
                        $" sourceHandle={Token(slopeArrow.SourceHandle)} role=SlopeArrow",
                        sourceDatabase,
                        destinationDatabase);
                }

                if (SlopeAngleTextStore.TryRead(entity, out var angle) && angle is not null)
                {
                    WriteXData(
                        callback,
                        entity,
                        "SlopeAngleText",
                        $"valid=true schema={angle.SchemaVersion.ToString(CultureInfo.InvariantCulture)}" +
                        $" sourceHandle={Token(angle.SourceHandle)} role=SlopeAngleText",
                        sourceDatabase,
                        destinationDatabase);
                }

                if (PostFootprintPerpendicularAnnotationStore.TryRead(entity, out var post) &&
                    post is not null)
                {
                    WriteXData(
                        callback,
                        entity,
                        "PostFootprint",
                        $"valid=true schema={post.SchemaVersion.ToString(CultureInfo.InvariantCulture)}" +
                        $" sourceHandle={Token(post.SourceHandle)} role=PostFootprint",
                        sourceDatabase,
                        destinationDatabase);
                }
            }
            catch (System.Exception exception)
            {
                WriteError(callback + ":decoded-xdata", entity.Database, exception);
            }
        }

        private void WriteXData(
            string callback,
            Entity entity,
            string kind,
            string fields,
            Database? sourceDatabase,
            Database? destinationDatabase)
        {
            Write(
                "ROOF_IMPORT_XDATA",
                callback + ":decoded",
                entity.Database,
                sourceDatabase,
                destinationDatabase,
                $"objectId={DescribeId(entity.ObjectId)} handle={DescribeHandle(entity, entity.ObjectId)}" +
                $" kind={kind} {fields}");
            _xdataCount++;
        }

        private void WriteBtrById(string callback, ObjectId id, string knownState)
        {
            var database = TryGetDatabase(id) ?? _database;
            using var transaction = TryStartReadTransaction(database);
            if (transaction is not null && TryOpen(id, transaction) is BlockTableRecord btr)
            {
                WriteBtr(callback, btr, depth: 0, knownState);
            }
        }

        private void WriteBtr(
            string callback,
            BlockTableRecord btr,
            int depth,
            string knownState)
        {
            Write(
                "ROOF_IMPORT_BTR",
                callback,
                btr.Database,
                _sourceDatabase,
                _destinationDatabase,
                $"btrId={DescribeId(btr.ObjectId)} handle={DescribeHandle(btr, btr.ObjectId)}" +
                $" btrName={Token(SafeBtrName(btr))} classification=BlockTableRecord" +
                $" state={Token(knownState)} depth={depth.ToString(CultureInfo.InvariantCulture)}" +
                $" memberCount={CountBtrMembers(btr).ToString(CultureInfo.InvariantCulture)}");
            _btrCount++;
        }

        private void WriteEvent(
            string callback,
            Database? eventDatabase,
            Database? sourceDatabase,
            Database? destinationDatabase,
            string? cloneContext = null,
            string? extra = null)
        {
            Write(
                "ROOF_IMPORT_EVENT",
                callback,
                eventDatabase,
                sourceDatabase,
                destinationDatabase,
                $"cloneContext={Token(cloneContext)} dbmod={ReadDbmod()}" +
                (string.IsNullOrWhiteSpace(extra) ? string.Empty : " " + extra));
            _eventCount++;
        }

        private void WriteSummary(string callback, string result)
        {
            Write(
                "ROOF_IMPORT_SUMMARY",
                callback,
                _database,
                _sourceDatabase,
                _destinationDatabase,
                $"result={result} dbmod={ReadDbmod()}" +
                $" events={_eventCount.ToString(CultureInfo.InvariantCulture)}" +
                $" maps={_mapCount.ToString(CultureInfo.InvariantCulture)}" +
                $" objects={_objectCount.ToString(CultureInfo.InvariantCulture)}" +
                $" btrs={_btrCount.ToString(CultureInfo.InvariantCulture)}" +
                $" xdata={_xdataCount.ToString(CultureInfo.InvariantCulture)}" +
                $" groups={_groupCount.ToString(CultureInfo.InvariantCulture)}" +
                $" appendedIds={_appendedIdCount.ToString(CultureInfo.InvariantCulture)}" +
                $" mappedIds={_mappedDestinationIdCount.ToString(CultureInfo.InvariantCulture)}" +
                $" mappingSnapshots={_mappingSnapshotCount.ToString(CultureInfo.InvariantCulture)}" +
                $" transientDatabases={_transientDatabaseCount.ToString(CultureInfo.InvariantCulture)}");
        }

        private void Write(
            string prefix,
            string callback,
            Database? eventDatabase,
            Database? sourceDatabase,
            Database? destinationDatabase,
            string fields)
        {
            var line =
                $"{prefix}" +
                $" seq={(++_sequence).ToString(CultureInfo.InvariantCulture)}" +
                $" operation={_operation.ToString(CultureInfo.InvariantCulture)}" +
                $" rawCommand={Token(_rawCommand)} normalizedCommand={Token(_normalizedCommand)}" +
                $" document={DocumentIdentity(_document)} documentName={Token(_document.Name)}" +
                $" documentDb={DatabaseIdentity(_database)} eventDb={DatabaseIdentity(eventDatabase)}" +
                $" sourceDb={DatabaseIdentity(sourceDatabase)} destinationDb={DatabaseIdentity(destinationDatabase)}" +
                $" callback={Token(callback)} {fields}";
            try
            {
                _document.Editor?.WriteMessage("\n" + line);
            }
            catch
            {
            }

            AcKrovyDiagnostics.Info(prefix, line, _normalizedCommand);
        }

        private void WriteError(string callback, Database? database, System.Exception exception)
        {
            WriteError(callback, database, exception.GetType().Name + ":" + exception.Message);
        }

        private void WriteError(string callback, Database? database, string error)
        {
            Write(
                "ROOF_IMPORT_EVENT",
                callback,
                database,
                _sourceDatabase,
                _destinationDatabase,
                $"cloneContext=- dbmod={ReadDbmod()} result=error error={Token(error)}");
            _eventCount++;
        }

        private void CapturePreExistingState()
        {
            using var transaction = TryStartReadTransaction(_database);
            if (transaction is null)
            {
                return;
            }

            try
            {
                if (transaction.GetObject(_database.BlockTableId, OpenMode.ForRead) is BlockTable table)
                {
                    foreach (ObjectId id in table)
                    {
                        _preExistingBtrIds.Add(id);
                    }
                }

                if (transaction.GetObject(_database.GroupDictionaryId, OpenMode.ForRead) is
                    DBDictionary groups)
                {
                    foreach (DBDictionaryEntry entry in groups)
                    {
                        _preExistingGroupIds.Add(entry.Value);
                    }
                }

                _preExistingStateCaptured = true;
            }
            catch (System.Exception exception)
            {
                WriteError("CapturePreExistingState", _database, exception);
            }
        }

        private void EnsureActive(string fallbackCommand)
        {
            if (_active)
            {
                return;
            }

            ClearOperationState();
            _operation++;
            _rawCommand = fallbackCommand;
            _normalizedCommand = LiveGeometryCommandRules.NormalizeCommandName(fallbackCommand);
            _active = true;
            _sourceDatabase = _database;
            var isWblock = IsWblockCommand(_normalizedCommand);
            _destinationDatabase = isWblock ? null : _database;
            if (isWblock)
            {
                ArmDatabaseConstructionDiscovery();
            }

            CapturePreExistingState();
        }

        private bool IsCurrentDiscoveryCommand(string? command)
        {
            if (!_active)
            {
                return false;
            }

            var normalized = LiveGeometryCommandRules.NormalizeCommandName(command);
            return IsDiscoveryCommand(normalized) ||
                   normalized.Equals(_normalizedCommand, StringComparison.OrdinalIgnoreCase);
        }

        private void ClearOperationState()
        {
            DisarmDatabaseConstructionDiscovery();
            DetachAllTransientDatabases();
            _active = false;
            _rawCommand = string.Empty;
            _normalizedCommand = string.Empty;
            _sourceDatabase = null;
            _destinationDatabase = null;
            _eventCount = 0;
            _mapCount = 0;
            _objectCount = 0;
            _btrCount = 0;
            _xdataCount = 0;
            _groupCount = 0;
            _appendedIdCount = 0;
            _mappedDestinationIdCount = 0;
            _mappingSnapshotCount = 0;
            _transientDatabaseCount = 0;
            _preExistingStateCaptured = false;
            _appendedIds.Clear();
            _mappedDestinationIds.Clear();
            _mappedGroupIds.Clear();
            _preExistingBtrIds.Clear();
            _preExistingGroupIds.Clear();
            _mappingSnapshots.Clear();
        }

        private string ResolveKnownState(ObjectId id)
        {
            if (_appendedIds.Contains(id))
            {
                return "new-appended";
            }

            var mapping = _mappingSnapshots.LastOrDefault(item => item.Destination == id);
            if (mapping is not null)
            {
                return mapping.IsCloned ? "mapped-new" : "mapped-reused";
            }

            if (_preExistingBtrIds.Contains(id) || _preExistingGroupIds.Contains(id))
            {
                return "pre-existing";
            }

            return "unknown";
        }

        private string PreExistingStatus(ObjectId id, Database database)
        {
            if (!_preExistingStateCaptured || !ReferenceEquals(database, _database))
            {
                return "unknown";
            }

            return Bool(_preExistingBtrIds.Contains(id) || _preExistingGroupIds.Contains(id));
        }

        private string ReadDbmod()
        {
            if (!ReferenceEquals(AcApp.DocumentManager.MdiActiveDocument, _document))
            {
                return "not-active-document";
            }

            try
            {
                return Convert.ToString(
                           AcApp.GetSystemVariable("DBMOD"),
                           CultureInfo.InvariantCulture) ?? "-";
            }
            catch
            {
                return "unavailable";
            }
        }
    }

    private sealed record MappingSnapshot(
        ObjectId Source,
        ObjectId Destination,
        bool IsCloned,
        bool IsPrimary);

    private readonly record struct BtrDescription(ObjectId Id, string? Name)
    {
        public static BtrDescription Empty { get; } = new(ObjectId.Null, null);
    }

    private static bool IsDiscoveryCommand(string normalizedCommand)
    {
        var command = normalizedCommand.TrimStart('-');
        return command.Equals(
                   RoofImportDirectInsertProofSession.CommandName,
                   StringComparison.OrdinalIgnoreCase) ||
               command.Equals("WBLOCK", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("INSERT", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("CLASSICINSERT", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("EXPLODE", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("U", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("UNDO", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("REDO", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("MREDO", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWblockCommand(string normalizedCommand) =>
        normalizedCommand.TrimStart('-')
            .Equals("WBLOCK", StringComparison.OrdinalIgnoreCase);

    private static bool IsDiscoveryCloneContext(DeepCloneType context) =>
        context is DeepCloneType.Wblock or
            DeepCloneType.WblockObjects or
            DeepCloneType.Insert or
            DeepCloneType.InsertCopy or
            DeepCloneType.Explode;

    private static Transaction? TryStartReadTransaction(Database? database)
    {
        if (database is null || database.IsDisposed)
        {
            return null;
        }

        try
        {
            return database.TransactionManager.StartOpenCloseTransaction();
        }
        catch
        {
            return null;
        }
    }

    private static DBObject? TryOpen(ObjectId id, Transaction? transaction)
    {
        if (transaction is null || id.IsNull || !id.IsValid)
        {
            return null;
        }

        try
        {
            return transaction.GetObject(id, OpenMode.ForRead);
        }
        catch
        {
            return null;
        }
    }

    private static BtrDescription DescribeOwnerBtr(DBObject? value, Transaction? transaction)
    {
        if (value is null)
        {
            return BtrDescription.Empty;
        }

        if (value is BlockTableRecord self)
        {
            return new BtrDescription(self.ObjectId, SafeBtrName(self));
        }

        return DescribeBtr(value.OwnerId, transaction);
    }

    private static BtrDescription DescribeBtr(ObjectId id, Transaction? transaction)
    {
        return TryOpen(id, transaction) is BlockTableRecord btr
            ? new BtrDescription(id, SafeBtrName(btr))
            : BtrDescription.Empty;
    }

    private static string SafeBtrName(BlockTableRecord btr)
    {
        try
        {
            return btr.Name;
        }
        catch
        {
            return "-";
        }
    }

    private static int CountBtrMembers(BlockTableRecord btr)
    {
        try
        {
            return btr.Cast<ObjectId>().Count();
        }
        catch
        {
            return -1;
        }
    }

    private static string DescribeOwner(DBObject? value)
    {
        if (value is null)
        {
            return "-";
        }

        try
        {
            return DescribeId(value.OwnerId);
        }
        catch
        {
            return "-";
        }
    }

    private static string DescribeType(DBObject? value) =>
        Token(value?.GetType().FullName);

    private static string DescribeId(ObjectId id)
    {
        if (id.IsNull)
        {
            return "-";
        }

        try
        {
            return Token(id.ToString());
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string DescribeHandle(DBObject? value, ObjectId fallbackId)
    {
        try
        {
            if (value is not null)
            {
                return Token(value.Handle.ToString());
            }

            return fallbackId.IsNull ? "-" : Token(fallbackId.Handle.ToString());
        }
        catch
        {
            return "unavailable";
        }
    }

    private static Database? TryGetDatabase(ObjectId id)
    {
        try
        {
            return id.Database;
        }
        catch
        {
            return null;
        }
    }

    private static bool BelongsToDatabase(ObjectId id, Database database) =>
        ReferenceEquals(TryGetDatabase(id), database);

    private static bool HasRegApp(Entity entity, string regApp)
    {
        try
        {
            using var xdata = entity.GetXDataForApplication(regApp);
            return xdata is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsKrovyRegApp(string value) =>
        value.StartsWith("DECORAIR_ACADKROVY", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("ACAD_KROVY", StringComparison.OrdinalIgnoreCase);

    private static string FormatAnchor(AcKrovy.Core.Models.Roofs.RoofGeneratedMemberKey? key) =>
        key is null
            ? "-"
            : Token(
                $"{key.Value.MemberKind}:{key.Value.RoofFace}:s" +
                key.Value.StationIndex.ToString(CultureInfo.InvariantCulture));

    private static string FormatRelativeSegment(
        AcKrovy.Core.Models.Roofs.RoofAttachedManualRelativeSegment? segment) =>
        segment is null
            ? "-"
            : Token(string.Join(",", new[]
            {
                Number(segment.U0Mm), Number(segment.V0Mm), Number(segment.W0Mm),
                Number(segment.U1Mm), Number(segment.V1Mm), Number(segment.W1Mm),
            }));

    private static string FormatPoint(Autodesk.AutoCAD.Geometry.Point3d point) =>
        Token($"{Number(point.X)},{Number(point.Y)},{Number(point.Z)}");

    private static string Number(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private static string Int(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string Bool(bool value) =>
        value ? "true" : "false";

    private static string DocumentIdentity(Document document) =>
        "D" + RuntimeHelpers.GetHashCode(document).ToString("X8", CultureInfo.InvariantCulture);

    private static string DatabaseIdentity(Database? database) =>
        database is null
            ? "-"
            : "DB" + RuntimeHelpers.GetHashCode(database).ToString("X8", CultureInfo.InvariantCulture);

    private static string Token(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return value.Trim()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Replace(' ', '_');
    }
}
#endif
