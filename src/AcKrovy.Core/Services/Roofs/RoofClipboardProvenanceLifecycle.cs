namespace AcKrovy.Core.Services.Roofs;

public enum RoofClipboardPasteProvenanceKind
{
    Unknown = 0,
    KnownForeignDocument = 1,
    KnownSameDocument = 2,
}

public sealed record RoofClipboardPasteProvenanceDecision(
    bool HasProvenance,
    bool ClipboardRevisionMatches,
    bool SameDocument,
    bool DatabaseReferenceEqual,
    bool SameDrawing,
    bool IsValid,
    string DiagnosticResult,
    RoofClipboardPasteProvenanceKind ProvenanceKind);

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
    private ActivePaste? _activePaste;

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

    private sealed record ActivePaste(
        DurableProvenance? Provenance,
        TDocument TargetDocument,
        TDatabase TargetDatabase,
        uint ClipboardRevision,
        string TargetCommand,
        RoofClipboardPasteProvenanceKind ProvenanceKind,
        bool SameDrawing);

    public TPayload? ActivePayload => _activePaste is { SameDrawing: true, Provenance: { } provenance }
        ? provenance.Payload
        : null;

    public TPayload? DurablePayload => _durableProvenance?.Payload;

    public bool HasDurableProvenance => _durableProvenance is not null;

    public bool HasActivePaste => _activePaste is not null;

    public TDocument? ActiveTargetDocument => _activePaste?.TargetDocument;

    public TDocument? ActiveSourceDocument => _activePaste?.Provenance?.SourceDocument;

    public uint ActiveClipboardRevision => _activePaste?.ClipboardRevision ?? 0;

    public string ActiveCommand => _activePaste?.TargetCommand ?? string.Empty;

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
        uint clipboardRevision,
        string? targetCommand = null)
    {
        if (targetDocument is null)
        {
            throw new ArgumentNullException(nameof(targetDocument));
        }

        if (targetDatabase is null)
        {
            throw new ArgumentNullException(nameof(targetDatabase));
        }

        var provenance = _durableProvenance;
        if (provenance is null)
        {
            _activePaste = Active(
                null,
                targetDocument,
                targetDatabase,
                clipboardRevision,
                targetCommand,
                RoofClipboardPasteProvenanceKind.Unknown,
                false);
            return Decision(
                false,
                false,
                false,
                false,
                false,
                false,
                "missing-provenance",
                RoofClipboardPasteProvenanceKind.Unknown);
        }

        var clipboardRevisionMatches = clipboardRevision != 0 &&
            provenance.ClipboardRevision == clipboardRevision;
        if (!clipboardRevisionMatches)
        {
            _durableProvenance = null;
            _activePaste = Active(
                null,
                targetDocument,
                targetDatabase,
                clipboardRevision,
                targetCommand,
                RoofClipboardPasteProvenanceKind.Unknown,
                false);
            return Decision(
                true,
                false,
                false,
                false,
                false,
                false,
                "clipboard-replaced",
                RoofClipboardPasteProvenanceKind.Unknown);
        }

        var sameDocument = ReferenceEquals(provenance.SourceDocument, targetDocument);
        var databaseReferenceEqual = ReferenceEquals(
            provenance.SourceDatabase,
            targetDatabase);
        var sameDrawing = sameDocument;
        var valid = sameDrawing;
        var provenanceKind = sameDrawing
            ? RoofClipboardPasteProvenanceKind.KnownSameDocument
            : RoofClipboardPasteProvenanceKind.KnownForeignDocument;
        _activePaste = Active(
            provenance,
            targetDocument,
            targetDatabase,
            clipboardRevision,
            targetCommand,
            provenanceKind,
            sameDrawing);

        var result = valid ? "same-dwg" : "different-document";
        return Decision(
            true,
            true,
            sameDocument,
            databaseReferenceEqual,
            sameDrawing,
            valid,
            result,
            provenanceKind);
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
            (ReferenceEquals(active.TargetDocument, document) ||
             ReferenceEquals(active.Provenance?.SourceDocument, document)))
        {
            _activePaste = null;
        }
    }

    private static ActivePaste Active(
        DurableProvenance? provenance,
        TDocument targetDocument,
        TDatabase targetDatabase,
        uint clipboardRevision,
        string? targetCommand,
        RoofClipboardPasteProvenanceKind provenanceKind,
        bool sameDrawing) =>
        new(
            provenance,
            targetDocument,
            targetDatabase,
            clipboardRevision,
            targetCommand ?? string.Empty,
            provenanceKind,
            sameDrawing);

    private static RoofClipboardPasteProvenanceDecision Decision(
        bool hasProvenance,
        bool clipboardRevisionMatches,
        bool sameDocument,
        bool databaseReferenceEqual,
        bool sameDrawing,
        bool isValid,
        string result,
        RoofClipboardPasteProvenanceKind provenanceKind) =>
        new(
            hasProvenance,
            clipboardRevisionMatches,
            sameDocument,
            databaseReferenceEqual,
            sameDrawing,
            isValid,
            result,
            provenanceKind);
}
