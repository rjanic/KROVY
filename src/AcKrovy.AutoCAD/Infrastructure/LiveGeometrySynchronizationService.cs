using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class LiveGeometrySynchronizationService
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

        foreach (var tracker in Trackers.Values.ToList())
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
        if (e.Document is not null && Trackers.TryGetValue(e.Document, out var tracker))
        {
            tracker.Dispose();
            Trackers.Remove(e.Document);
        }
    }

    private static void Attach(Document document)
    {
        if (Trackers.ContainsKey(document))
        {
            return;
        }

        Trackers[document] = new DocumentTracker(document);
    }

    private sealed class DocumentTracker : IDisposable
    {
        private readonly Document _document;
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _modifiedIds = new();
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _appendedLabelIds = new();
        private readonly LiveGeometryRefreshCoordinator<string> _erasedSourceHandles = new();
        private bool _ignoreCurrentCommand;
        private bool _isDisposed;

        public DocumentTracker(Document document)
        {
            _document = document;
            _document.Database.ObjectAppended += ObjectAppended;
            _document.Database.ObjectModified += ObjectModified;
            _document.Database.ObjectErased += ObjectErased;
            _document.CommandWillStart += CommandWillStart;
            _document.CommandEnded += CommandEnded;
            _document.CommandCancelled += CommandCancelled;
            _document.CommandFailed += CommandFailed;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _document.Database.ObjectAppended -= ObjectAppended;
            _document.Database.ObjectModified -= ObjectModified;
            _document.Database.ObjectErased -= ObjectErased;
            _document.CommandWillStart -= CommandWillStart;
            _document.CommandEnded -= CommandEnded;
            _document.CommandCancelled -= CommandCancelled;
            _document.CommandFailed -= CommandFailed;
            _modifiedIds.Clear();
            _appendedLabelIds.Clear();
            _erasedSourceHandles.Clear();
        }

        private void ObjectAppended(object? sender, ObjectEventArgs e)
        {
            if (_ignoreCurrentCommand ||
                _modifiedIds.IsSuppressed ||
                e.DBObject is not Entity entity ||
                entity.ObjectId.IsNull ||
                entity.IsErased)
            {
                return;
            }

            if (AutoCadEntityHelpers.IsSupportedTimberGeometry(entity))
            {
                _modifiedIds.TryAdd(entity.ObjectId);
                return;
            }

            if (!_appendedLabelIds.IsSuppressed && entity is MText)
            {
                _appendedLabelIds.TryAdd(entity.ObjectId);
            }
        }

        private void ObjectModified(object? sender, ObjectEventArgs e)
        {
            if (_ignoreCurrentCommand ||
                _modifiedIds.IsSuppressed ||
                e.DBObject is not Entity entity ||
                entity.ObjectId.IsNull ||
                entity.IsErased)
            {
                return;
            }

            _modifiedIds.TryAdd(entity.ObjectId);
        }

        private void ObjectErased(object? sender, ObjectErasedEventArgs e)
        {
            if (_ignoreCurrentCommand ||
                _erasedSourceHandles.IsSuppressed ||
                !e.Erased ||
                e.DBObject is not Entity entity ||
                !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity))
            {
                return;
            }

            _erasedSourceHandles.TryAdd(entity.Handle.ToString());
        }

        private void CommandWillStart(object? sender, CommandEventArgs e)
        {
            _ignoreCurrentCommand = IsAcKrovyCommand(e.GlobalCommandName);
            if (_ignoreCurrentCommand)
            {
                _modifiedIds.Clear();
                _appendedLabelIds.Clear();
                _erasedSourceHandles.Clear();
            }
        }

        private void CommandEnded(object? sender, CommandEventArgs e)
        {
            var shouldIgnore = _ignoreCurrentCommand || IsAcKrovyCommand(e.GlobalCommandName);
            _ignoreCurrentCommand = false;
            if (shouldIgnore)
            {
                _modifiedIds.Clear();
                _appendedLabelIds.Clear();
                _erasedSourceHandles.Clear();
                return;
            }

            RefreshCandidates();
        }

        private void CommandCancelled(object? sender, CommandEventArgs e)
        {
            _ignoreCurrentCommand = false;
            _modifiedIds.Clear();
            _appendedLabelIds.Clear();
            _erasedSourceHandles.Clear();
        }

        private void CommandFailed(object? sender, CommandEventArgs e)
        {
            _ignoreCurrentCommand = false;
            _modifiedIds.Clear();
            _appendedLabelIds.Clear();
            _erasedSourceHandles.Clear();
        }

        private void RefreshCandidates()
        {
            var ids = _modifiedIds.Drain();
            var appendedLabelIds = _appendedLabelIds.Drain();
            var erasedSourceHandles = _erasedSourceHandles.Drain();
            if (ids.Count == 0 && appendedLabelIds.Count == 0 && erasedSourceHandles.Count == 0)
            {
                return;
            }

            using (_modifiedIds.Suppress())
            using (_appendedLabelIds.Suppress())
            using (_erasedSourceHandles.Suppress())
            {
                RefreshTimberElements(_document, ids, appendedLabelIds, erasedSourceHandles);
            }
        }

        private static void RefreshTimberElements(
            Document document,
            IReadOnlyList<ObjectId> ids,
            IReadOnlyCollection<ObjectId> appendedLabelIds,
            IReadOnlyCollection<string> erasedSourceHandles)
        {
            var editor = document.Editor;

            try
            {
                using (document.LockDocument())
                using (var transaction = document.Database.TransactionManager.StartTransaction())
                {
                    var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
                    ElementLabelService.DeleteLabelsForMissingSourceHandles(
                        document.Database,
                        transaction,
                        erasedSourceHandles);

                    var defaultProfile = TimberElementDefaultProfileStore.Load();
                    var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
                    TimberElementCopyInitializationService.InitializeLocalCopies(
                        document.Database,
                        transaction,
                        metadataStore,
                        ids,
                        defaultProfile);

                    var previousElementIdById = ReadElementIds(document.Database, transaction, metadataStore, ids);
                    var timberIds = FilterTimberElementIds(document.Database, transaction, metadataStore, ids);
                    if (timberIds.Count > 0)
                    {
                        var synchronizedDataById = TimberElementItemIdentityService.SynchronizeElementIds(
                            document.Database,
                            transaction,
                            metadataStore,
                            timberIds,
                            roundingStepMm);

                        foreach (var id in timberIds)
                        {
                            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                                    transaction,
                                    id,
                                    OpenMode.ForRead,
                                    out var entity,
                                    document.Database) ||
                                entity is null ||
                                !synchronizedDataById.TryGetValue(id, out var data))
                            {
                                continue;
                            }

                            previousElementIdById.TryGetValue(id, out var previousElementId);
                            ElementLabelService.UpsertForElement(
                                document.Database,
                                transaction,
                                entity,
                                data,
                                previousElementId,
                                roundingStepMm);
                        }
                    }

                    ElementLabelService.DeleteInsertedLabelsWithoutCurrentSourceHandles(
                        document.Database,
                        transaction,
                        appendedLabelIds);
                    ElementLabelService.DeleteDuplicateLabelsForExistingSourceHandles(document.Database, transaction);
                    transaction.Commit();
                }
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nACAD KROVY: automatická aktualizácia prvkov bola preskočená: {ex.Message}");
            }
        }

        private static IReadOnlyDictionary<ObjectId, string> ReadElementIds(
            Database database,
            Transaction transaction,
            AutoCadTimberElementMetadataStore metadataStore,
            IReadOnlyList<ObjectId> ids)
        {
            var result = new Dictionary<ObjectId, string>();

            foreach (var id in ids.Distinct())
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        database) ||
                    entity is null ||
                    !metadataStore.TryRead(entity, out var data) ||
                    data is null)
                {
                    continue;
                }

                result[id] = data.ElementId;
            }

            return result;
        }

        private static IReadOnlyList<ObjectId> FilterTimberElementIds(
            Database database,
            Transaction transaction,
            AutoCadTimberElementMetadataStore metadataStore,
            IReadOnlyList<ObjectId> ids)
        {
            var result = new List<ObjectId>();

            foreach (var id in ids.Distinct())
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        database) ||
                    entity is null ||
                    !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                    !metadataStore.TryRead(entity, out var data) ||
                    data is null)
                {
                    continue;
                }

                result.Add(id);
            }

            return result;
        }

        private static bool IsAcKrovyCommand(string? commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return false;
            }

            return commandName.Trim().StartsWith("AK_", StringComparison.OrdinalIgnoreCase);
        }
    }
}
