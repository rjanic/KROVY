#if DEBUG
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG-only correlation state for one direct managed Database.Insert proof.
/// It records callback evidence and never reads or writes the drawing database.
/// </summary>
internal static class RoofImportDirectInsertProofSession
{
    internal const string CommandName = "AK_DEBUG_DIRECT_INSERT_PROOF";

    private static SessionState? _active;

    public static Guid Create(
        Document document,
        Database targetDatabase,
        Database sourceDatabase,
        string sourceIdentity,
        string destinationBlockName)
    {
        ClearAll("superseded");
        var state = new SessionState(
            Guid.NewGuid(),
            document,
            targetDatabase,
            sourceDatabase,
            sourceIdentity,
            destinationBlockName);
        _active = state;
        Write(state, "created");
        return state.Id;
    }

    public static void MarkPreflightOk(Guid sessionId, int blockCount, int entityCount)
    {
        var state = Require(sessionId);
        state.Phase = SessionPhase.PreflightOk;
        state.SourceBlockCount = blockCount;
        state.SourceEntityCount = entityCount;
        Write(
            state,
            "preflight-ok",
            $"sourceBlocks={blockCount} sourceEntities={entityCount}");
    }

    public static void BeginInsert(Guid sessionId)
    {
        var state = Require(sessionId);
        if (state.Phase != SessionPhase.PreflightOk)
        {
            throw new InvalidOperationException(
                "Direct INSERT proof cannot begin before preflight succeeds.");
        }

        state.Phase = SessionPhase.Inserting;
        Write(state, "inserting", "api=Database.Insert preserveSourceDatabase=true");
    }

    public static void ObserveLifecycle(Document document, string callback)
    {
        if (!TryMatch(document, out var state))
        {
            return;
        }

        Write(state, PhaseToken(callback), $"callback={Token(callback)}");
    }

    public static void ObserveObjectAppended(Document document, ObjectId objectId)
    {
        if (!TryMatch(document, out var state))
        {
            return;
        }

        state.ObjectAppendedCount++;
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
        if (!TryMatch(document, out var state) ||
            mapping.DeepCloneContext is not DeepCloneType.Insert and
                not DeepCloneType.InsertCopy)
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

        state.MappingSeen = true;
        state.MappingCallback = callback;
        state.MappingPairCount = pairCount;
        state.MappingClonedCount = clonedCount;
        state.Phase = SessionPhase.MappingSeen;
        Write(
            state,
            "mapping-seen",
            $"callback={Token(callback)} pairs={pairCount} cloned={clonedCount}");
        Write(
            state,
            "mapping-evidence",
            $"marker=ROOF_IMPORT_DIRECT_INSERT_MAPPING callback={Token(callback)} " +
            $"pairs={pairCount} cloned={clonedCount}");
    }

    public static SessionSnapshot Complete(Guid sessionId)
    {
        var state = Require(sessionId);
        state.Phase = SessionPhase.Completed;
        Write(
            state,
            "completed",
            $"mappingSeen={Bool(state.MappingSeen)} " +
            $"objectAppendedCount={state.ObjectAppendedCount}");
        return Snapshot(state);
    }

    public static SessionSnapshot Fail(Guid sessionId, System.Exception exception)
    {
        var state = Require(sessionId);
        state.Phase = SessionPhase.Failed;
        Write(
            state,
            "failed",
            $"mappingSeen={Bool(state.MappingSeen)} " +
            $"objectAppendedCount={state.ObjectAppendedCount} " +
            $"error={Token(exception.GetType().Name)}");
        return Snapshot(state);
    }

    public static SessionSnapshot GetSnapshot(Guid sessionId) => Snapshot(Require(sessionId));

    public static void Clear(Guid sessionId, string reason)
    {
        if (_active is not { } state || state.Id != sessionId)
        {
            return;
        }

        Write(state, "cleared", $"reason={Token(reason)}");
        _active = null;
    }

    public static void ClearDocument(Document document, string reason)
    {
        if (_active is { } state && ReferenceEquals(document, state.Document))
        {
            Clear(state.Id, reason);
        }
    }

    public static void ClearAll(string reason)
    {
        if (_active is { } state)
        {
            Clear(state.Id, reason);
        }
    }

    private static SessionState Require(Guid sessionId)
    {
        if (_active is not { } state || state.Id != sessionId)
        {
            throw new InvalidOperationException("Direct INSERT proof session is not active.");
        }

        return state;
    }

    private static bool TryMatch(Document document, out SessionState state)
    {
        if (_active is { } candidate &&
            ReferenceEquals(document, candidate.Document) &&
            candidate.Phase is SessionPhase.Inserting or SessionPhase.MappingSeen)
        {
            state = candidate;
            return true;
        }

        state = null!;
        return false;
    }

    private static SessionSnapshot Snapshot(SessionState state) => new(
        state.MappingSeen,
        state.MappingCallback,
        state.MappingPairCount,
        state.MappingClonedCount,
        state.ObjectAppendedCount);

    private static void Write(SessionState state, string phase, string? extra = null)
    {
        try
        {
            var targetDatabase = RuntimeHelpers.GetHashCode(state.TargetDatabase)
                .ToString("X", System.Globalization.CultureInfo.InvariantCulture);
            var sourceDatabase = RuntimeHelpers.GetHashCode(state.SourceDatabase)
                .ToString("X", System.Globalization.CultureInfo.InvariantCulture);
            state.Document.Editor.WriteMessage(
                "\nROOF_IMPORT_DIRECT_INSERT_SESSION " +
                $"session={state.Id:N} phase={phase} command={CommandName} " +
                $"sourceDb={sourceDatabase} targetDb={targetDatabase} " +
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

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Token(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Trim()
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Replace(' ', '_');

    internal readonly record struct SessionSnapshot(
        bool MappingSeen,
        string MappingCallback,
        int MappingPairCount,
        int MappingClonedCount,
        int ObjectAppendedCount);

    private enum SessionPhase
    {
        Created,
        PreflightOk,
        Inserting,
        MappingSeen,
        Completed,
        Failed,
    }

    private sealed class SessionState(
        Guid id,
        Document document,
        Database targetDatabase,
        Database sourceDatabase,
        string sourceIdentity,
        string destinationBlockName)
    {
        public Guid Id { get; } = id;
        public Document Document { get; } = document;
        public Database TargetDatabase { get; } = targetDatabase;
        public Database SourceDatabase { get; } = sourceDatabase;
        public string SourceIdentity { get; } = sourceIdentity;
        public string DestinationBlockName { get; } = destinationBlockName;
        public SessionPhase Phase { get; set; } = SessionPhase.Created;
        public int SourceBlockCount { get; set; }
        public int SourceEntityCount { get; set; }
        public bool MappingSeen { get; set; }
        public string MappingCallback { get; set; } = "-";
        public int MappingPairCount { get; set; }
        public int MappingClonedCount { get; set; }
        public int ObjectAppendedCount { get; set; }
    }
}
#endif
