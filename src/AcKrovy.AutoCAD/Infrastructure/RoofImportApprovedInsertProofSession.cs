#if DEBUG
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG-only authorization and lifecycle correlation for one approved native INSERT.
/// This state never performs a database read or write.
/// </summary>
internal static class RoofImportApprovedInsertProofSession
{
    internal const string ExpectedGlobalCommandName = "-INSERT";

    private static SessionState? _active;

    public static Guid Create(
        Document document,
        Database targetDatabase,
        string sourceIdentity,
        string destinationBlockName)
    {
        ClearActive("superseded");
        var state = new SessionState(
            Guid.NewGuid(),
            document,
            targetDatabase,
            sourceIdentity,
            destinationBlockName);
        _active = state;
        Write(state, "created");
        return state.Id;
    }

    public static void MarkPreflightOk(Guid sessionId, int blockCount, int entityCount)
    {
        if (!TryGet(sessionId, out var state))
        {
            return;
        }

        state.Phase = SessionPhase.PreflightOk;
        state.SourceBlockCount = blockCount;
        state.SourceEntityCount = entityCount;
        Write(
            state,
            "preflight-ok",
            $"sourceBlocks={blockCount} sourceEntities={entityCount}");
    }

    public static void Approve(Guid sessionId)
    {
        if (!TryGet(sessionId, out var state) ||
            state.Phase != SessionPhase.PreflightOk)
        {
            throw new InvalidOperationException(
                "Approved INSERT proof cannot be armed before preflight succeeds.");
        }

        state.Phase = SessionPhase.Approved;
        state.ApprovalAvailable = true;
        Write(state, "approved", "token=available");
    }

    public static bool TryConsume(
        Document document,
        string globalCommandName,
        out Guid sessionId)
    {
        sessionId = Guid.Empty;
        try
        {
            var state = _active;
            if (state is null ||
                !state.ApprovalAvailable ||
                state.TokenConsumed ||
                state.Phase != SessionPhase.Approved ||
                !ReferenceEquals(document, state.Document) ||
                !string.Equals(
                    globalCommandName,
                    ExpectedGlobalCommandName,
                    StringComparison.Ordinal))
            {
                return false;
            }

            state.ApprovalAvailable = false;
            state.TokenConsumed = true;
            state.Phase = SessionPhase.TokenConsumed;
            sessionId = state.Id;
            Write(state, "token-consumed", "token=consumed");
            return true;
        }
        catch
        {
            ClearActive("consume-error");
            return false;
        }
    }

    public static void ObserveCommandWillStart(Document document, string? command)
    {
        var state = _active;
        if (!MatchesConsumedSession(state, document) ||
            !string.Equals(
                command ?? string.Empty,
                ExpectedGlobalCommandName,
                StringComparison.Ordinal))
        {
            return;
        }

        state!.Phase = SessionPhase.InsertStarted;
        Write(state, "insert-started", $"command={ExpectedGlobalCommandName}");
    }

    public static void ObserveLifecycle(Document document, string callback)
    {
        var state = _active;
        if (!MatchesStartedSession(state, document))
        {
            return;
        }

        Write(state!, PhaseToken(callback), $"callback={Token(callback)}");
    }

    public static void ObserveObjectAppended(Document document, ObjectId objectId)
    {
        var state = _active;
        if (!MatchesStartedSession(state, document))
        {
            return;
        }

        state!.ObjectAppendedCount++;
        Write(
            state,
            "object-appended",
            $"count={state.ObjectAppendedCount} object={ObjectIdToken(objectId)}");
    }

    public static void ObserveMapping(
        Document document,
        string callback,
        IdMapping mapping)
    {
        var state = _active;
        if (!MatchesStartedSession(state, document))
        {
            return;
        }

        var pairCount = 0;
        var clonedCount = 0;
        foreach (IdPair pair in mapping)
        {
            pairCount++;
            if (pair.IsCloned)
            {
                clonedCount++;
            }
        }

        state!.MappingSeen = true;
        state.Phase = SessionPhase.MappingSeen;
        Write(
            state,
            "mapping-seen",
            $"callback={Token(callback)} pairs={pairCount} cloned={clonedCount}");
    }

    public static void ObserveCommandTerminal(
        Document document,
        string? command,
        string outcome)
    {
        var state = _active;
        if (!MatchesConsumedSession(state, document) ||
            !string.Equals(
                command ?? string.Empty,
                ExpectedGlobalCommandName,
                StringComparison.Ordinal))
        {
            return;
        }

        state!.ApprovalAvailable = false;
        state.TerminalOutcome = outcome;
        state.Phase = string.Equals(outcome, "completed", StringComparison.Ordinal)
            ? SessionPhase.Completed
            : string.Equals(outcome, "cancelled", StringComparison.Ordinal)
                ? SessionPhase.Cancelled
                : SessionPhase.Failed;
        Write(
            state,
            outcome,
            $"mappingSeen={state.MappingSeen.ToString().ToLowerInvariant()} " +
            $"objectAppendedCount={state.ObjectAppendedCount}");
    }

    public static bool WasCompleted(Guid sessionId) =>
        TryGet(sessionId, out var state) &&
        state.Phase == SessionPhase.Completed &&
        state.TokenConsumed;

    public static void MarkInvocationException(Guid sessionId, System.Exception exception)
    {
        if (!TryGet(sessionId, out var state))
        {
            return;
        }

        state.ApprovalAvailable = false;
        state.TerminalOutcome = "invocation-exception";
        state.Phase = SessionPhase.Failed;
        Write(
            state,
            "failed",
            $"reason=invocation-exception error={Token(exception.GetType().Name)}");
    }

    public static void Clear(Guid sessionId, string reason)
    {
        if (!TryGet(sessionId, out var state))
        {
            return;
        }

        state.ApprovalAvailable = false;
        Write(
            state,
            "cleared",
            $"reason={Token(reason)} outcome={Token(state.TerminalOutcome)}");
        _active = null;
    }

    public static void ClearDocument(Document document, string reason)
    {
        if (_active is { } state && ReferenceEquals(document, state.Document))
        {
            Clear(state.Id, reason);
        }
    }

    public static void ClearAll(string reason) => ClearActive(reason);

    private static bool TryGet(Guid sessionId, out SessionState state)
    {
        if (_active is { } candidate && candidate.Id == sessionId)
        {
            state = candidate;
            return true;
        }

        state = null!;
        return false;
    }

    private static bool MatchesConsumedSession(SessionState? state, Document document) =>
        state is not null &&
        state.TokenConsumed &&
        ReferenceEquals(document, state.Document);

    private static bool MatchesStartedSession(SessionState? state, Document document) =>
        MatchesConsumedSession(state, document) &&
        state!.Phase is SessionPhase.InsertStarted or SessionPhase.MappingSeen;

    private static void ClearActive(string reason)
    {
        if (_active is { } state)
        {
            state.ApprovalAvailable = false;
            Write(state, "cleared", $"reason={Token(reason)}");
            _active = null;
        }
    }

    private static void Write(SessionState state, string phase, string? extra = null)
    {
        try
        {
            var targetDatabase = RuntimeHelpers.GetHashCode(state.TargetDatabase)
                .ToString("X", System.Globalization.CultureInfo.InvariantCulture);
            state.Document.Editor.WriteMessage(
                "\nROOF_IMPORT_APPROVED_INSERT_SESSION " +
                $"session={state.Id:N} phase={phase} " +
                $"command={ExpectedGlobalCommandName} " +
                $"targetDb={targetDatabase} " +
                $"sourceIdentity={Token(state.SourceIdentity)} " +
                $"destination={Token(state.DestinationBlockName)}" +
                (string.IsNullOrWhiteSpace(extra) ? string.Empty : $" {extra}"));
        }
        catch
        {
            // A DEBUG diagnostic must never escape an AutoCAD callback.
        }
    }

    private static string PhaseToken(string callback) => callback switch
    {
        "BeginInsert" => "begin-insert",
        "BeginDeepClone" => "begin-deep-clone",
        "BeginDeepCloneTranslation" => "begin-deep-clone-translation",
        "DeepCloneEnded" => "deep-clone-ended",
        "DeepCloneAborted" => "deep-clone-aborted",
        "InsertEnded" => "insert-ended",
        "InsertAborted" => "insert-aborted",
        _ => "lifecycle",
    };

    private static string ObjectIdToken(ObjectId objectId)
    {
        try
        {
            return objectId.IsNull ? "null" : objectId.Handle.ToString();
        }
        catch
        {
            return "unavailable";
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

    private enum SessionPhase
    {
        Created,
        PreflightOk,
        Approved,
        TokenConsumed,
        InsertStarted,
        MappingSeen,
        Completed,
        Cancelled,
        Failed,
    }

    private sealed class SessionState(
        Guid id,
        Document document,
        Database targetDatabase,
        string sourceIdentity,
        string destinationBlockName)
    {
        public Guid Id { get; } = id;
        public Document Document { get; } = document;
        public Database TargetDatabase { get; } = targetDatabase;
        public string SourceIdentity { get; } = sourceIdentity;
        public string DestinationBlockName { get; } = destinationBlockName;
        public SessionPhase Phase { get; set; } = SessionPhase.Created;
        public bool ApprovalAvailable { get; set; }
        public bool TokenConsumed { get; set; }
        public bool MappingSeen { get; set; }
        public int SourceBlockCount { get; set; }
        public int SourceEntityCount { get; set; }
        public int ObjectAppendedCount { get; set; }
        public string TerminalOutcome { get; set; } = "pending";
    }
}
#endif
