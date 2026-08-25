namespace AcKrovy.Core.Services.Roofs;

public sealed record RoofClipboardPasteProvenanceDecision(
    bool HasProvenance,
    bool ClipboardRevisionMatches,
    bool SameDocument,
    bool DatabaseReferenceEqual,
    bool SameDrawing,
    bool IsValid,
    string DiagnosticResult);

/// <summary>
/// CAD-neutral lifecycle for one current clipboard payload. A source snapshot is
/// pending while COPYCLIP/COPYBASE runs and becomes durable only after successful
/// command completion. Exact tracked host Document reference identity and the
/// operating-system clipboard revision jointly prove a same-open-drawing paste.
/// The durable token remains valid for repeated paste commands while those two
/// authorities remain unchanged.
/// Database wrapper reference equality is retained only as diagnostic evidence;
/// host APIs may return different managed wrappers for the same open Document.
/// </summary>
public sealed class RoofClipboardProvenanceLifecycle<TDocument, TDatabase, TPayload>
    where TDocument : class
    where TDatabase : class
    where TPayload : class
{
    private PendingCopy? _pendingCopy;
    private DurableProvenance? _durableProvenance;
    private DurableProvenance? _activePaste;

    private sealed record PendingCopy(
        TDocument SourceDocument,
        TDatabase SourceDatabase,
        string SourceCommand,
        TPayload Payload);

    private sealed record DurableProvenance(
        TDocument SourceDocument,
        TDatabase SourceDatabase,
        string SourceCommand,
        TPayload Payload,
        uint ClipboardRevision);

    public TPayload? ActivePayload => _activePaste?.Payload;

    public TPayload? DurablePayload => _durableProvenance?.Payload;

    public bool HasDurableProvenance => _durableProvenance is not null;

    public void BeginCopy(
        TDocument sourceDocument,
        TDatabase sourceDatabase,
        string sourceCommand,
        TPayload payload)
    {
        if (sourceDocument is null)
        {
            throw new ArgumentNullException(nameof(sourceDocument));
        }

        if (sourceDatabase is null)
        {
            throw new ArgumentNullException(nameof(sourceDatabase));
        }

        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        _activePaste = null;
        _pendingCopy = new PendingCopy(
            sourceDocument,
            sourceDatabase,
            sourceCommand ?? string.Empty,
            payload);
    }

    public bool CompleteCopy(uint clipboardRevision)
    {
        var pending = _pendingCopy;
        _pendingCopy = null;
        _activePaste = null;

        if (pending is null || clipboardRevision == 0)
        {
            // A successful new clipboard source command with an unreadable clipboard
            // revision cannot safely leave an older payload marked as current.
            if (pending is not null)
            {
                _durableProvenance = null;
            }

            return false;
        }

        _durableProvenance = new DurableProvenance(
            pending.SourceDocument,
            pending.SourceDatabase,
            pending.SourceCommand,
            pending.Payload,
            clipboardRevision);
        return true;
    }

    public void CancelOrFailCopy(TDocument sourceDocument)
    {
        if (_pendingCopy is { } pending &&
            ReferenceEquals(pending.SourceDocument, sourceDocument))
        {
            _pendingCopy = null;
        }

        _activePaste = null;
    }

    public RoofClipboardPasteProvenanceDecision BeginPaste(
        TDocument targetDocument,
        TDatabase targetDatabase,
        uint clipboardRevision)
    {
        var provenance = _durableProvenance;
        if (provenance is null)
        {
            _activePaste = null;
            return Decision(false, false, false, false, false, false, "missing-provenance");
        }

        var clipboardRevisionMatches = clipboardRevision != 0 &&
            provenance.ClipboardRevision == clipboardRevision;
        if (!clipboardRevisionMatches)
        {
            _activePaste = null;
            _durableProvenance = null;
            return Decision(true, false, false, false, false, false, "clipboard-replaced");
        }

        var sameDocument = ReferenceEquals(provenance.SourceDocument, targetDocument);
        var databaseReferenceEqual = ReferenceEquals(
            provenance.SourceDatabase,
            targetDatabase);
        var sameDrawing = sameDocument;
        var valid = sameDrawing;
        _activePaste = valid ? provenance : null;

        var result = valid ? "same-dwg" : "different-document";
        return Decision(
            true,
            true,
            sameDocument,
            databaseReferenceEqual,
            sameDrawing,
            valid,
            result);
    }

    /// <summary>
    /// Completes one paste command without consuming the durable provenance. The
    /// token describes the current clipboard payload and remains reusable until its
    /// clipboard revision changes, a newer copy replaces it, or its source Document
    /// is cleared. Cancelled and failed paste attempts follow the same lifetime.
    /// </summary>
    public void CompletePaste() => _activePaste = null;

    public void CancelOrFailPaste() => _activePaste = null;

    public void ClearActivePaste() => _activePaste = null;

    public void ClearForDocument(TDocument document)
    {
        if (_pendingCopy is { } pending &&
            ReferenceEquals(pending.SourceDocument, document))
        {
            _pendingCopy = null;
        }

        if (_durableProvenance is { } durable &&
            ReferenceEquals(durable.SourceDocument, document))
        {
            _durableProvenance = null;
        }

        if (_activePaste is { } active &&
            ReferenceEquals(active.SourceDocument, document))
        {
            _activePaste = null;
        }
    }

    private static RoofClipboardPasteProvenanceDecision Decision(
        bool hasProvenance,
        bool clipboardRevisionMatches,
        bool sameDocument,
        bool databaseReferenceEqual,
        bool sameDrawing,
        bool isValid,
        string result) =>
        new(
            hasProvenance,
            clipboardRevisionMatches,
            sameDocument,
            databaseReferenceEqual,
            sameDrawing,
            isValid,
            result);
}
