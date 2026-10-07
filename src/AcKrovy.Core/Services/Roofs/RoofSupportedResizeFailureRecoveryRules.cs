namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// CAD-neutral SupportedResize transaction phases and HardFailure recovery contract.
/// Models the owner aggregate as one atomic unit: source, generated Plan2D, overrides,
/// structural, Physical3D, annotations, GROUP, and runtime lifecycle readiness.
/// </summary>
public static class RoofSupportedResizeFailureRecoveryRules
{
    public const string OrdinaryCutNotOnStructuralSideFailure =
        "structural-physical-OrdinaryCutNotOnStructuralSide";

    public enum Phase
    {
        Idle = 0,
        SourceDetected = 1,
        OrdinaryRebuilt = 2,
        OverrideReplayed = 3,
        GroupTouched = 4,
        StructuralPhysical = 5,
        Commit = 6,
        Failed = 7,
        Recovered = 8,
    }

    public enum LifecycleOperation
    {
        OrdinaryMove = 0,
        OrdinaryGripStretch = 1,
        SourceGripStretch = 2,
    }

    public sealed record AggregateSnapshot(
        string SourceGeometryToken,
        string GeneratedPlanToken,
        string OrdinaryPhysicalToken,
        string StructuralPhysicalToken,
        string OverridesToken,
        string AnnotationsToken,
        bool GroupCanonical,
        double RigidFootprintEdge12Mm = 6000d,
        int GroupMemberCount = 186);

    public sealed record RuntimeLifecycleState(
        bool PendingResize,
        bool HasStaleSnapshot,
        bool SuppressionActive,
        bool ReentrancyActive,
        bool NextCommandReady)
    {
        public static RuntimeLifecycleState DirtyDuringResize() =>
            new(PendingResize: true, HasStaleSnapshot: true, SuppressionActive: true,
                ReentrancyActive: true, NextCommandReady: false);

        public static RuntimeLifecycleState CleanReady() =>
            new(PendingResize: false, HasStaleSnapshot: false, SuppressionActive: false,
                ReentrancyActive: false, NextCommandReady: true);

        public bool IsFullyClean =>
            !PendingResize && !HasStaleSnapshot && !SuppressionActive &&
            !ReentrancyActive && NextCommandReady;
    }

    public sealed class Session
    {
        public Phase CurrentPhase { get; private set; } = Phase.Idle;
        public AggregateSnapshot PreCommand { get; }
        public AggregateSnapshot Live { get; private set; }
        public RuntimeLifecycleState Runtime { get; private set; } = RuntimeLifecycleState.CleanReady();
        public string? FailureReason { get; private set; }
        public string? InjectedFailureAtStructuralPhysical { get; set; }
        public int SuccessfulSourceResizeCount { get; private set; }
        public int SuccessfulOrdinaryMoveCount { get; private set; }
        public int SuccessfulOrdinaryGripStretchCount { get; private set; }
        public int ForcedFailureCount { get; private set; }

        public Session(AggregateSnapshot preCommand)
        {
            PreCommand = preCommand ?? throw new ArgumentNullException(nameof(preCommand));
            Live = preCommand;
        }

        public bool TryAdvance(Phase next, AggregateSnapshot? live = null)
        {
            if (CurrentPhase is Phase.Failed or Phase.Recovered)
                return false;
            if (!IsValidTransition(CurrentPhase, next))
                return false;

            if (next == Phase.StructuralPhysical &&
                !string.IsNullOrWhiteSpace(InjectedFailureAtStructuralPhysical))
            {
                CurrentPhase = Phase.Failed;
                FailureReason = InjectedFailureAtStructuralPhysical;
                Runtime = RuntimeLifecycleState.DirtyDuringResize();
                if (live is not null)
                    Live = live;
                ForcedFailureCount++;
                return false;
            }

            CurrentPhase = next;
            if (live is not null)
                Live = live;
            if (next is Phase.SourceDetected or Phase.OrdinaryRebuilt or Phase.OverrideReplayed
                or Phase.GroupTouched or Phase.StructuralPhysical)
                Runtime = RuntimeLifecycleState.DirtyDuringResize();
            if (next == Phase.Commit)
            {
                Runtime = RuntimeLifecycleState.CleanReady();
                SuccessfulSourceResizeCount++;
            }

            return true;
        }

        public bool TryRecoverFromHardFailure(bool forceGroupCanonicalFalse = false)
        {
            if (CurrentPhase != Phase.Failed)
                return false;

            Live = forceGroupCanonicalFalse
                ? PreCommand with
                {
                    GroupCanonical = false,
                    GroupMemberCount = PreCommand.GroupMemberCount - 4,
                    RigidFootprintEdge12Mm = 7138.038790322185d,
                    StructuralPhysicalToken = PreCommand.StructuralPhysicalToken + "-dropped",
                }
                : PreCommand;
            Runtime = RuntimeLifecycleState.CleanReady();
            CurrentPhase = Phase.Recovered;
            FailureReason = null;
            return MatchesPreCommand() && Runtime.IsFullyClean;
        }

        public bool TryExecuteAfterRecovery(LifecycleOperation operation)
        {
            if (CurrentPhase is not (Phase.Recovered or Phase.Commit or Phase.Idle))
                return false;
            if (!Runtime.IsFullyClean)
                return false;
            if (CurrentPhase == Phase.Recovered && !MatchesPreCommand())
                return false;

            // After recovery, live equals pre-command; operations may mutate live again.
            switch (operation)
            {
                case LifecycleOperation.OrdinaryMove:
                    SuccessfulOrdinaryMoveCount++;
                    CurrentPhase = Phase.Idle;
                    return true;
                case LifecycleOperation.OrdinaryGripStretch:
                    SuccessfulOrdinaryGripStretchCount++;
                    CurrentPhase = Phase.Idle;
                    return true;
                case LifecycleOperation.SourceGripStretch:
                    // Valid source resize after recovery uses a fresh transaction.
                    InjectedFailureAtStructuralPhysical = null;
                    CurrentPhase = Phase.Idle;
                    return TryAdvance(Phase.SourceDetected)
                        && TryAdvance(Phase.OrdinaryRebuilt)
                        && TryAdvance(Phase.OverrideReplayed)
                        && TryAdvance(Phase.GroupTouched)
                        && TryAdvance(Phase.StructuralPhysical)
                        && TryAdvance(Phase.Commit);
                default:
                    return false;
            }
        }

        public bool MatchesPreCommand() =>
            string.Equals(Live.SourceGeometryToken, PreCommand.SourceGeometryToken, StringComparison.Ordinal) &&
            string.Equals(Live.GeneratedPlanToken, PreCommand.GeneratedPlanToken, StringComparison.Ordinal) &&
            string.Equals(Live.OrdinaryPhysicalToken, PreCommand.OrdinaryPhysicalToken, StringComparison.Ordinal) &&
            string.Equals(Live.StructuralPhysicalToken, PreCommand.StructuralPhysicalToken, StringComparison.Ordinal) &&
            string.Equals(Live.OverridesToken, PreCommand.OverridesToken, StringComparison.Ordinal) &&
            string.Equals(Live.AnnotationsToken, PreCommand.AnnotationsToken, StringComparison.Ordinal) &&
            Live.GroupCanonical == PreCommand.GroupCanonical &&
            PreCommand.GroupCanonical &&
            Math.Abs(Live.RigidFootprintEdge12Mm - PreCommand.RigidFootprintEdge12Mm) <= 1e-6 &&
            Live.GroupMemberCount == PreCommand.GroupMemberCount;

        public AggregateEquality CompareToPreCommand() =>
            new(
                PreCommand.RigidFootprintEdge12Mm,
                Live.RigidFootprintEdge12Mm,
                PreCommand.GroupMemberCount,
                Live.GroupMemberCount,
                SourceEqual: string.Equals(Live.SourceGeometryToken, PreCommand.SourceGeometryToken, StringComparison.Ordinal),
                PlanEqual: string.Equals(Live.GeneratedPlanToken, PreCommand.GeneratedPlanToken, StringComparison.Ordinal),
                OverridesEqual: string.Equals(Live.OverridesToken, PreCommand.OverridesToken, StringComparison.Ordinal),
                OrdinaryPhysicalEqual: string.Equals(Live.OrdinaryPhysicalToken, PreCommand.OrdinaryPhysicalToken, StringComparison.Ordinal),
                StructuralPhysicalEqual: string.Equals(Live.StructuralPhysicalToken, PreCommand.StructuralPhysicalToken, StringComparison.Ordinal),
                AnnotationsEqual: string.Equals(Live.AnnotationsToken, PreCommand.AnnotationsToken, StringComparison.Ordinal),
                GroupCanonical: Live.GroupCanonical && PreCommand.GroupCanonical);
    }

    public static bool IsValidTransition(Phase current, Phase next) =>
        (current, next) switch
        {
            (Phase.Idle, Phase.SourceDetected) => true,
            (Phase.Recovered, Phase.SourceDetected) => true,
            (Phase.Commit, Phase.SourceDetected) => true,
            (Phase.SourceDetected, Phase.OrdinaryRebuilt) => true,
            (Phase.OrdinaryRebuilt, Phase.OverrideReplayed) => true,
            (Phase.OverrideReplayed, Phase.GroupTouched) => true,
            (Phase.GroupTouched, Phase.StructuralPhysical) => true,
            (Phase.StructuralPhysical, Phase.Commit) => true,
            _ => false,
        };

    /// <summary>
    /// HOST recovery marker may report result=ok only when every persistent and
    /// runtime gate is true. groupCanonical=false is always fail.
    /// </summary>
    public static bool IsRecoveryVerdictOk(
        bool dbRollback,
        bool runtimeReset,
        bool pendingResize,
        int suppressionDepth,
        bool hasActiveOwner,
        bool groupCanonical,
        bool sourceGeometryRestored,
        bool roofDefinitionRestored,
        bool physicalInventoryRestored,
        bool annotationsRestored,
        bool nextCommandReady) =>
        dbRollback &&
        runtimeReset &&
        !pendingResize &&
        suppressionDepth == 0 &&
        !hasActiveOwner &&
        groupCanonical &&
        sourceGeometryRestored &&
        roofDefinitionRestored &&
        physicalInventoryRestored &&
        annotationsRestored &&
        nextCommandReady;

    public sealed record AggregateEquality(
        double PreRigidEdge12Mm,
        double PostRigidEdge12Mm,
        int PreGroupMembers,
        int PostGroupMembers,
        bool SourceEqual,
        bool PlanEqual,
        bool OverridesEqual,
        bool OrdinaryPhysicalEqual,
        bool StructuralPhysicalEqual,
        bool AnnotationsEqual,
        bool GroupCanonical)
    {
        public bool IsExactRestore =>
            SourceEqual &&
            PlanEqual &&
            OverridesEqual &&
            OrdinaryPhysicalEqual &&
            StructuralPhysicalEqual &&
            AnnotationsEqual &&
            GroupCanonical &&
            Math.Abs(PreRigidEdge12Mm - PostRigidEdge12Mm) <= 1e-6 &&
            PreGroupMembers == PostGroupMembers &&
            PreGroupMembers > 0;
    }
}
