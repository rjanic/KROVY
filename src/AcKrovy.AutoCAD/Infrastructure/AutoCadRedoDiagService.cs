#if DEBUG
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG observe-only REDO loss telemetry. Zero DWG writes: no transactions,
/// LockDocument, ForWrite, XData, or entity mutation. File trace under _scratch only.
/// </summary>
internal static class AutoCadRedoDiagService
{
    private const string CommandBanner = "AK_DEV_REDO_DIAG";

    private static readonly object Gate = new();
    private static bool _enabled;
    private static bool _dllInitObserved;
    private static string? _tracePath;
    private static int _commandWillStartCount;
    private static int _commandEndedCount;
    private static int _undoCommandObserved;
    private static int _redoCommandObserved;
    private static int _writesAfterUndo;
    private static int _mutationsAfterUndo;
    private static int _liveGeometryRefreshExecutedAfterUndo;
    private static int _liveGeometryTxnAfterUndo;
    private static int _liveGeometryRefreshSkippedUndoRedo;
    private static int _exceptionCount;
    private static bool _undoFamilyInFlight;
    private static string _lastCommand = string.Empty;
    private static string _lastMutationOrigin = string.Empty;
    private static string _lastMutationCommand = string.Empty;
    private static string _lastMutationEvent = string.Empty;
    private static int? _dbmodBeforeLiveGeometryRefresh;

    public static bool IsEnabled
    {
        get
        {
            lock (Gate)
            {
                return _enabled;
            }
        }
    }

    public static void NoteDllInit()
    {
        lock (Gate)
        {
            _dllInitObserved = true;
        }

        Trace("DLL_INIT PluginEntry.Initialize (LiveGeometry starts here)");
    }

    public static void Enable()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        var editor = document?.Editor;
        lock (Gate)
        {
            if (_enabled)
            {
                editor?.WriteMessage($"\n{CommandBanner}: already ON");
                editor?.WriteMessage($"\nTraceFile={_tracePath}");
                return;
            }

            _enabled = true;
            ResetCountersUnlocked();
            _tracePath = CreateTraceFilePath();
        }

        Trace("REDO_DIAG_ON");
        editor?.WriteMessage($"\n=== {CommandBanner}_ON ===");
        editor?.WriteMessage("\nObserve-only. Zero DWG writes from this diag.");
        editor?.WriteMessage($"\nDllInitObserved={_dllInitObserved}");
        editor?.WriteMessage($"\nTraceFile={_tracePath}");
    }

    public static void Disable()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        var editor = document?.Editor;
        lock (Gate)
        {
            if (!_enabled)
            {
                editor?.WriteMessage($"\n{CommandBanner}: already OFF");
                return;
            }

            Trace("REDO_DIAG_OFF");
            _enabled = false;
        }

        editor?.WriteMessage($"\n=== {CommandBanner}_OFF ===");
        editor?.WriteMessage($"\nTraceFile={_tracePath}");
    }

    public static void WriteStatus()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        int commandWillStart;
        int commandEnded;
        int undoObserved;
        int redoObserved;
        int writesAfterUndo;
        int mutationsAfterUndo;
        int liveRefreshExecuted;
        int liveTxn;
        int liveRefreshSkipped;
        int exceptions;
        bool enabled;
        bool dllInit;
        string lastCommand;
        string lastOrigin;
        string lastMutationCommand;
        string lastMutationEvent;
        string? tracePath;
        lock (Gate)
        {
            enabled = _enabled;
            dllInit = _dllInitObserved;
            commandWillStart = _commandWillStartCount;
            commandEnded = _commandEndedCount;
            undoObserved = _undoCommandObserved;
            redoObserved = _redoCommandObserved;
            writesAfterUndo = _writesAfterUndo;
            mutationsAfterUndo = _mutationsAfterUndo;
            liveRefreshExecuted = _liveGeometryRefreshExecutedAfterUndo;
            liveTxn = _liveGeometryTxnAfterUndo;
            liveRefreshSkipped = _liveGeometryRefreshSkippedUndoRedo;
            exceptions = _exceptionCount;
            lastCommand = _lastCommand;
            lastOrigin = _lastMutationOrigin;
            lastMutationCommand = _lastMutationCommand;
            lastMutationEvent = _lastMutationEvent;
            tracePath = _tracePath;
        }

        var registration = AutoCadFramedBlockContentGripRegistrationSnapshot.Capture(document);
        editor.WriteMessage($"\n=== {CommandBanner}_STATUS ===");
        editor.WriteMessage($"\nEnabled={enabled}");
        editor.WriteMessage($"\nDllInitObserved={dllInit}");
        editor.WriteMessage($"\nCommandWillStartCount={commandWillStart}");
        editor.WriteMessage($"\nCommandEndedCount={commandEnded}");
        editor.WriteMessage($"\nUndoCommandObserved={undoObserved}");
        editor.WriteMessage($"\nRedoCommandObserved={redoObserved}");
        editor.WriteMessage($"\nLiveGeometryRefreshExecutedAfterUndo={liveRefreshExecuted}");
        editor.WriteMessage($"\nLiveGeometryTxnAfterUndo={liveTxn}");
        editor.WriteMessage($"\nLiveGeometryRefreshSkippedUndoRedo={liveRefreshSkipped}");
        editor.WriteMessage($"\nWritesAfterUndo={writesAfterUndo}");
        editor.WriteMessage($"\nMutationsAfterUndo={mutationsAfterUndo}");
        editor.WriteMessage($"\nExceptionCount={exceptions}");
        editor.WriteMessage($"\nLastCommand='{lastCommand}'");
        editor.WriteMessage($"\nLastMutationOrigin='{lastOrigin}'");
        editor.WriteMessage($"\nLastMutationCommand='{lastMutationCommand}'");
        editor.WriteMessage($"\nLastMutationEvent='{lastMutationEvent}'");
        editor.WriteMessage($"\nDBMOD={TryReadIntSysVar("DBMOD")}");
        editor.WriteMessage($"\nUNDOCTL={TryReadIntSysVar("UNDOCTL")}");
        editor.WriteMessage($"\nOverrule.Overruling={Overrule.Overruling}");
        editor.WriteMessage($"\nPassThroughRegistered={registration.PassThroughRegistered}");
        editor.WriteMessage($"\nReadOnlyRegistered={registration.ReadOnlyRegistered}");
        editor.WriteMessage($"\nNormalizeRegistered={registration.NormalizeRegistered}");
        editor.WriteMessage($"\nUnsafeUndoProofRegistered={registration.UnsafeUndoProofRegistered}");
        editor.WriteMessage($"\nP4AProofEnabled={registration.P4AProofEnabled}");
        editor.WriteMessage($"\nP4ATraceEnabled={registration.P4ATraceEnabled}");
        editor.WriteMessage($"\nP4AQueuedCount={registration.P4AQueuedCount}");
        editor.WriteMessage($"\nNormalizeProofEnabled={registration.NormalizeProofEnabled}");
        editor.WriteMessage($"\nGuardIsProcessing={registration.GuardIsProcessing}");
        editor.WriteMessage($"\nNormalizeInstance={registration.NormalizeInstanceIdentity}");
        editor.WriteMessage($"\nTraceFile={tracePath ?? "(none)"}");
    }

    public static void OnCommandWillStart(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        lock (Gate)
        {
            if (!_enabled)
            {
                return;
            }

            _commandWillStartCount++;
            _lastCommand = normalized;
            if (IsUndoFamily(normalized))
            {
                _undoCommandObserved++;
                _undoFamilyInFlight = true;
                Trace($"CommandWillStart UNDO_FAMILY name='{normalized}' DBMOD={TryReadIntSysVar("DBMOD")}");
            }
            else if (IsRedoFamily(normalized))
            {
                _redoCommandObserved++;
                Trace($"CommandWillStart REDO_FAMILY name='{normalized}' DBMOD={TryReadIntSysVar("DBMOD")}");
            }
            else
            {
                Trace($"CommandWillStart name='{normalized}'");
            }
        }
    }

    public static void OnCommandEnded(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        lock (Gate)
        {
            if (!_enabled)
            {
                return;
            }

            _commandEndedCount++;
            _lastCommand = normalized;
            Trace(
                $"CommandEnded name='{normalized}' undoInFlight={_undoFamilyInFlight} " +
                $"DBMOD={TryReadIntSysVar("DBMOD")}");
            if (IsUndoFamily(normalized) || IsRedoFamily(normalized))
            {
                // Keep undo-in-flight through LiveGeometry refresh that runs inside
                // the same CommandEnded handler, then clear after refresh hooks.
            }
        }
    }

    public static void OnCommandCancelledOrFailed(string phase, string? globalCommandName)
    {
        lock (Gate)
        {
            if (!_enabled)
            {
                return;
            }

            Trace($"{phase} name='{LiveGeometryCommandRules.NormalizeCommandName(globalCommandName)}'");
            _undoFamilyInFlight = false;
            _dbmodBeforeLiveGeometryRefresh = null;
        }
    }

    public static void OnLiveGeometryRefreshBegin(
        string? globalCommandName,
        int modifiedCount,
        int erasedHandleCount,
        int framedLabelCount)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        lock (Gate)
        {
            if (!_enabled)
            {
                return;
            }

            var afterUndo = _undoFamilyInFlight || IsUndoFamily(normalized);
            if (!afterUndo)
            {
                return;
            }

            _liveGeometryRefreshExecutedAfterUndo++;
            _liveGeometryTxnAfterUndo++;
            _writesAfterUndo++;
            _dbmodBeforeLiveGeometryRefresh = TryReadIntSysVar("DBMOD");
            _lastMutationOrigin = "LiveGeometrySynchronizationService.RefreshTimberElements";
            _lastMutationCommand = normalized;
            _lastMutationEvent = "CommandEnded";
            Trace(
                $"LIVE_GEOMETRY_REFRESH_EXECUTED_AFTER_UNDO cmd='{normalized}' " +
                $"modified={modifiedCount} erasedHandles={erasedHandleCount} " +
                $"framedLabels={framedLabelCount} DBMOD_before={_dbmodBeforeLiveGeometryRefresh}");
        }
    }

    public static void OnLiveGeometryRefreshEnd(string? globalCommandName, bool committed)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        lock (Gate)
        {
            if (!_enabled)
            {
                return;
            }

            var afterUndo = _undoFamilyInFlight || IsUndoFamily(normalized);
            if (!afterUndo)
            {
                return;
            }

            var dbmodAfter = TryReadIntSysVar("DBMOD");
            var before = _dbmodBeforeLiveGeometryRefresh;
            if (before.HasValue && dbmodAfter.HasValue && before.Value != dbmodAfter.Value)
            {
                _mutationsAfterUndo++;
                Trace(
                    $"MUTATION_AFTER_UNDO origin=LiveGeometry cmd='{normalized}' " +
                    $"committed={committed} DBMOD {before.Value}->{dbmodAfter.Value}");
            }
            else
            {
                Trace(
                    $"LIVE_GEOMETRY_REFRESH_END_AFTER_UNDO cmd='{normalized}' " +
                    $"committed={committed} DBMOD_after={dbmodAfter} " +
                    $"(no DBMOD delta observed)");
            }

            _dbmodBeforeLiveGeometryRefresh = null;
            if (IsUndoFamily(normalized) || IsRedoFamily(normalized))
            {
                _undoFamilyInFlight = false;
            }
        }
    }

    public static void OnLiveGeometryRefreshSkippedUndoRedo(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        lock (Gate)
        {
            if (!_enabled)
            {
                return;
            }

            _liveGeometryRefreshSkippedUndoRedo++;
            Trace($"LIVE_GEOMETRY_SKIP_UNDO_REDO cmd='{normalized}' (no LockDocument/txn)");
            if (IsUndoFamily(normalized) || IsRedoFamily(normalized) || _undoFamilyInFlight)
            {
                _undoFamilyInFlight = false;
            }
        }
    }

    public static void OnLiveGeometryRefreshSkippedEmpty(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        lock (Gate)
        {
            if (!_enabled)
            {
                return;
            }

            if (_undoFamilyInFlight || IsUndoFamily(normalized))
            {
                Trace($"LIVE_GEOMETRY_SKIP_EMPTY_AFTER_UNDO cmd='{normalized}'");
                if (IsUndoFamily(normalized) || IsRedoFamily(normalized))
                {
                    _undoFamilyInFlight = false;
                }
            }
        }
    }

    public static void OnOverruleRegister(string owner, string instanceIdentity, bool overrulingBefore)
    {
        Trace(
            $"OVERRULE_REGISTER owner={owner} instance={instanceIdentity} " +
            $"OverrulingBefore={overrulingBefore} OverrulingAfter=True");
    }

    public static void OnOverruleUnregister(
        string owner,
        string instanceIdentity,
        bool removed,
        bool overrulingRestoredTo,
        bool ownedWasEnabled)
    {
        Trace(
            $"OVERRULE_UNREGISTER owner={owner} instance={instanceIdentity} " +
            $"removed={removed} ownedWasEnabled={ownedWasEnabled} " +
            $"OverrulingNow={Overrule.Overruling} restoreTarget={overrulingRestoredTo}");
    }

    public static void OnProofEnableDisable(string owner, string action, bool proofEnabled)
    {
        Trace($"PROOF_{action} owner={owner} ProofEnabled={proofEnabled}");
    }

    public static void OnException(string origin, System.Exception exception)
    {
        lock (Gate)
        {
            _exceptionCount++;
        }

        Trace($"EXCEPTION origin={origin} type={exception.GetType().Name} msg={exception.Message}");
    }

    public static void Trace(string message)
    {
        string? path;
        lock (Gate)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(_tracePath))
            {
                return;
            }

            path = _tracePath;
        }

        try
        {
            var line =
                DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                " " +
                message +
                Environment.NewLine;
            File.AppendAllText(path!, line, Encoding.UTF8);
        }
        catch
        {
            // Never throw into host command path from diag I/O.
        }
    }

    private static void ResetCountersUnlocked()
    {
        _commandWillStartCount = 0;
        _commandEndedCount = 0;
        _undoCommandObserved = 0;
        _redoCommandObserved = 0;
        _writesAfterUndo = 0;
        _mutationsAfterUndo = 0;
        _liveGeometryRefreshExecutedAfterUndo = 0;
        _liveGeometryTxnAfterUndo = 0;
        _liveGeometryRefreshSkippedUndoRedo = 0;
        _exceptionCount = 0;
        _undoFamilyInFlight = false;
        _lastCommand = string.Empty;
        _lastMutationOrigin = string.Empty;
        _lastMutationCommand = string.Empty;
        _lastMutationEvent = string.Empty;
        _dbmodBeforeLiveGeometryRefresh = null;
    }

    private static string CreateTraceFilePath()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"ak_dev_redo_diag_{stamp}.txt";
        var scratch = TryFindScratchDirectory();
        if (!string.IsNullOrWhiteSpace(scratch))
        {
            Directory.CreateDirectory(scratch!);
            return Path.Combine(scratch!, fileName);
        }

        var fallback = Path.Combine(Path.GetTempPath(), "AcKrovy");
        Directory.CreateDirectory(fallback);
        return Path.Combine(fallback, fileName);
    }

    private static string? TryFindScratchDirectory()
    {
        try
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir is not null)
            {
                var scratch = Path.Combine(dir.FullName, "_scratch");
                if (Directory.Exists(Path.Combine(dir.FullName, "src")) ||
                    Directory.Exists(scratch))
                {
                    return scratch;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    internal static bool IsUndoFamily(string normalized) =>
        normalized.Equals("U", StringComparison.OrdinalIgnoreCase) ||
        normalized.Equals("UNDO", StringComparison.OrdinalIgnoreCase);

    internal static bool IsRedoFamily(string normalized) =>
        normalized.Equals("REDO", StringComparison.OrdinalIgnoreCase) ||
        normalized.Equals("MREDO", StringComparison.OrdinalIgnoreCase);

    private static int? TryReadIntSysVar(string name)
    {
        try
        {
            var value = AcApplication.GetSystemVariable(name);
            return value switch
            {
                int i => i,
                short s => s,
                long l => checked((int)l),
                double d => (int)d,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }
}

internal readonly record struct GripRegistrationSnapshot(
    bool ProductionRegistered,
    bool PassThroughRegistered,
    bool ReadOnlyRegistered,
    bool NormalizeRegistered,
    bool UnsafeUndoProofRegistered,
    bool P4AProofEnabled,
    bool P4ATraceEnabled,
    int P4AQueuedCount,
    bool OverruleGlobalState,
    bool NormalizeProofEnabled,
    bool GuardIsProcessing,
    string ProductionInstanceIdentity,
    string PassThroughInstanceIdentity,
    string ReadOnlyInstanceIdentity,
    string NormalizeInstanceIdentity,
    string UnsafeUndoInstanceIdentity);

internal static class AutoCadFramedBlockContentGripRegistrationSnapshot
{
    public static GripRegistrationSnapshot Capture(Document? document)
    {
        TimberFramedBlockContentStretchNormalizeSession? p4a = null;
        TimberFramedBlockContentGripStageProofSession? normalizeSession = null;
        if (document is not null)
        {
            p4a = AutoCadFramedBlockContentStretchNormalizeLifecycleService
                .GetOrCreateSession(document);
            normalizeSession = AutoCadFramedBlockContentGripNormalizeProofService
                .GetOrCreateSession(document);
        }

        return new GripRegistrationSnapshot(
            ProductionRegistered: AutoCadFramedBlockContentProductionGripNormalizeService
                .IsOverruleRegistered,
            PassThroughRegistered: AutoCadFramedBlockContentGripPassthroughProofService
                .IsOverruleRegistered,
            ReadOnlyRegistered: AutoCadFramedBlockContentGripReadonlyProofService
                .IsOverruleRegistered,
            NormalizeRegistered: AutoCadFramedBlockContentGripNormalizeProofService
                .IsOverruleRegistered,
            UnsafeUndoProofRegistered: AutoCadFramedBlockContentGripUndoProofService
                .IsOverruleRegistered,
            P4AProofEnabled: p4a?.ProofEnabled ?? false,
            P4ATraceEnabled: p4a?.TraceEnabled ?? false,
            P4AQueuedCount: p4a?.QueuedCount ?? 0,
            OverruleGlobalState: Overrule.Overruling,
            NormalizeProofEnabled: normalizeSession?.ProofEnabled ?? false,
            GuardIsProcessing: (normalizeSession?.IsProcessing ?? false) ||
                AutoCadFramedBlockContentProductionGripNormalizeService.GuardIsProcessing,
            ProductionInstanceIdentity: AutoCadFramedBlockContentProductionGripNormalizeService
                .OverruleInstanceIdentity,
            PassThroughInstanceIdentity: AutoCadFramedBlockContentGripPassthroughProofService
                .OverruleInstanceIdentity,
            ReadOnlyInstanceIdentity: AutoCadFramedBlockContentGripReadonlyProofService
                .OverruleInstanceIdentity,
            NormalizeInstanceIdentity: AutoCadFramedBlockContentGripNormalizeProofService
                .OverruleInstanceIdentity,
            UnsafeUndoInstanceIdentity: AutoCadFramedBlockContentGripUndoProofService
                .OverruleInstanceIdentity);
    }

    public static void WriteStatus()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        var snap = Capture(document);
        editor.WriteMessage("\n=== AK_DEV_FBC_GRIP_REGISTRATION_STATUS ===");
        editor.WriteMessage($"\nProductionRegistered={snap.ProductionRegistered}");
        editor.WriteMessage($"\nPassThroughRegistered={snap.PassThroughRegistered}");
        editor.WriteMessage($"\nReadOnlyRegistered={snap.ReadOnlyRegistered}");
        editor.WriteMessage($"\nNormalizeRegistered={snap.NormalizeRegistered}");
        editor.WriteMessage($"\nUnsafeUndoProofRegistered={snap.UnsafeUndoProofRegistered}");
        editor.WriteMessage($"\nP4AProofEnabled={snap.P4AProofEnabled}");
        editor.WriteMessage($"\nP4ATraceEnabled={snap.P4ATraceEnabled}");
        editor.WriteMessage($"\nP4AQueuedCount={snap.P4AQueuedCount}");
        editor.WriteMessage($"\nOverruleGlobalState={snap.OverruleGlobalState}");
        editor.WriteMessage($"\nNormalizeProofEnabled={snap.NormalizeProofEnabled}");
        editor.WriteMessage($"\nGuardIsProcessing={snap.GuardIsProcessing}");
        editor.WriteMessage($"\nProductionInstance={snap.ProductionInstanceIdentity}");
        editor.WriteMessage($"\nPassThroughInstance={snap.PassThroughInstanceIdentity}");
        editor.WriteMessage($"\nReadOnlyInstance={snap.ReadOnlyInstanceIdentity}");
        editor.WriteMessage($"\nNormalizeInstance={snap.NormalizeInstanceIdentity}");
        editor.WriteMessage($"\nUnsafeUndoInstance={snap.UnsafeUndoInstanceIdentity}");
        AutoCadRedoDiagService.Trace(
            "REGISTRATION_STATUS " +
            $"PROD={snap.ProductionRegistered} PT={snap.PassThroughRegistered} " +
            $"RO={snap.ReadOnlyRegistered} NORM={snap.NormalizeRegistered} " +
            $"UNDO={snap.UnsafeUndoProofRegistered} " +
            $"P4A={snap.P4AProofEnabled}/{snap.P4AQueuedCount} " +
            $"Overruling={snap.OverruleGlobalState} " +
            $"NormProof={snap.NormalizeProofEnabled} Guard={snap.GuardIsProcessing}");
    }

    internal static string FormatInstanceIdentity(object? instance) =>
        instance is null
            ? "null"
            : $"{instance.GetType().Name}#{RuntimeHelpers.GetHashCode(instance)}";
}
#endif
