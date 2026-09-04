using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// One synchronous production InsertCopy operation. Event handlers only copy facts;
/// they never open, classify, mutate, veto, invoke commands, or throw.
/// </summary>
internal sealed class RoofExternalImportSession : IDisposable
{
    private readonly Document _document;
    private readonly Database _targetDatabase;
    private readonly Database _sourceDatabase;
    private readonly RoofExternalImportManifest _manifest;
    private readonly List<RoofExternalImportIdPair> _pairs = [];
    private readonly List<ObjectId> _appendedIds = [];
#if DEBUG
    internal bool CaptureRollbackDiagnostics { get; set; }
    internal Dictionary<ObjectId, RoofExternalImportRollbackObjectDiagnostics.CapturedObject> RollbackDiagnostics { get; } = [];
#endif
    private bool _mappingCaptured;
    private bool _disposed;
    private string? _captureFailure;
    private DeepCloneType? _cloneContext;
    private string _mappingCallback = string.Empty;
    private RoofExternalImportExplicitReference _explicitReference;
    private bool _explicitReferenceRecorded;

    public RoofExternalImportSession(
        Document document,
        Database targetDatabase,
        Database sourceDatabase,
        RoofExternalImportManifest manifest)
    {
        _document = document;
        _targetDatabase = targetDatabase;
        _sourceDatabase = sourceDatabase;
        _manifest = manifest;
        _targetDatabase.InsertMappingAvailable += InsertMappingAvailable;
        _targetDatabase.BeginDeepCloneTranslation += BeginDeepCloneTranslation;
        _targetDatabase.ObjectAppended += ObjectAppended;
    }

    /// <summary>
    /// Records the single top-level reference this workflow constructed after
    /// <c>Database.Insert</c> returned. Creation ownership is a local fact of the
    /// current operation: the instance was constructed here, was not resident before
    /// the append, and is resident afterwards. It is never derived from the native
    /// insert <see cref="RoofExternalImportOperationFacts.AppendedIds"/> stream.
    /// </summary>
    public RoofExternalImportExplicitReference RecordExplicitReference(
        BlockReference reference,
        bool wasUnresidentBeforeAppend)
    {
        if (_explicitReferenceRecorded)
        {
            throw new InvalidOperationException("explicit-top-level-reference-already-recorded");
        }

        _explicitReferenceRecorded = true;
        _explicitReference = new(
            reference.ObjectId,
            reference.Handle,
            reference.OwnerId,
            reference.BlockTableRecord,
            wasUnresidentBeforeAppend && !reference.ObjectId.IsNull);
        return _explicitReference;
    }

    public RoofExternalImportOperationFacts Freeze(ObjectId returnedRootId)
    {
        if (_captureFailure is not null)
        {
            throw new InvalidOperationException("Import mapping capture failed: " + _captureFailure);
        }

        if (!_mappingCaptured || _cloneContext != DeepCloneType.InsertCopy)
        {
            throw new InvalidOperationException("Import did not provide an operation-bound InsertCopy mapping.");
        }

        if (returnedRootId.IsNull || returnedRootId.Database != _targetDatabase)
        {
            throw new InvalidOperationException("Database.Insert returned a root outside the target database.");
        }

        return new(
            _document,
            _targetDatabase,
            _sourceDatabase,
            _manifest,
            _cloneContext.Value,
            _mappingCallback,
            _pairs.ToArray(),
            _appendedIds.ToArray(),
            returnedRootId,
            _explicitReference);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _targetDatabase.InsertMappingAvailable -= InsertMappingAvailable;
        _targetDatabase.BeginDeepCloneTranslation -= BeginDeepCloneTranslation;
        _targetDatabase.ObjectAppended -= ObjectAppended;
        _disposed = true;
    }

    private void InsertMappingAvailable(object? sender, IdMappingEventArgs e) =>
        Capture("InsertMappingAvailable", e.IdMapping);

    private void BeginDeepCloneTranslation(object? sender, IdMappingEventArgs e) =>
        Capture("BeginDeepCloneTranslation", e.IdMapping);

    private void Capture(string callback, IdMapping mapping)
    {
        try
        {
            if (_mappingCaptured || mapping.DeepCloneContext != DeepCloneType.InsertCopy)
            {
                return;
            }

            var captured = new List<RoofExternalImportIdPair>();
            foreach (IdPair pair in mapping)
            {
                captured.Add(new(pair.Key, pair.Value, pair.IsCloned, pair.IsPrimary));
            }

            _pairs.AddRange(captured);
            _cloneContext = mapping.DeepCloneContext;
            _mappingCallback = callback;
            _mappingCaptured = true;
        }
        catch (System.Exception exception)
        {
            _captureFailure = exception.GetType().Name;
        }
    }

    private void ObjectAppended(object? sender, ObjectEventArgs e)
    {
        try
        {
            _appendedIds.Add(e.DBObject.ObjectId);
        }
        catch (System.Exception exception)
        {
            _captureFailure = exception.GetType().Name;
        }
#if DEBUG
        if (CaptureRollbackDiagnostics)
        {
            // Supplemental facts only; a diagnostic failure must not change session validity.
            try
            {
                RollbackDiagnostics[e.DBObject.ObjectId] =
                    RoofExternalImportRollbackObjectDiagnostics.Capture(e.DBObject);
            }
            catch { }
        }
#endif
    }
}

internal readonly record struct RoofExternalImportIdPair(
    ObjectId SourceId,
    ObjectId DestinationId,
    bool IsCloned,
    bool IsPrimary);

/// <summary>
/// Explicit KROVY reference evidence: the exact top-level reference constructed and
/// appended by the current workflow after native <c>Database.Insert</c> finished.
/// This is a separate evidence class from native insert appends.
/// </summary>
internal readonly record struct RoofExternalImportExplicitReference(
    ObjectId Id,
    Handle Handle,
    ObjectId OwnerId,
    ObjectId ReferencedRootId,
    bool ConstructedByCurrentOperation);

/// <summary>
/// <paramref name="AppendedIds"/> is native <c>Database.Insert</c> append evidence only.
/// It proves the returned root came from the current insert and cannot describe objects
/// the workflow appends after that insert returned; those use <paramref name="ExplicitReference"/>.
/// </summary>
internal sealed record RoofExternalImportOperationFacts(
    Document Document,
    Database TargetDatabase,
    Database SourceDatabase,
    RoofExternalImportManifest Manifest,
    DeepCloneType CloneContext,
    string MappingCallback,
    RoofExternalImportIdPair[] Mapping,
    ObjectId[] AppendedIds,
    ObjectId ReturnedRootId,
    RoofExternalImportExplicitReference ExplicitReference);
