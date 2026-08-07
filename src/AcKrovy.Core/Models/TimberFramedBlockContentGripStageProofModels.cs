namespace AcKrovy.Core.Models;

/// <summary>
/// In-process Stage D readiness for Stage E arming. Fresh NETLOAD is NotRun —
/// never treat NotRun as Failed.
/// </summary>
public enum TimberFramedBlockContentGripStageDReadiness
{
    NotRun = 0,
    Passed = 1,
    Failed = 2,
}

/// <summary>
/// Stage E SETUP arm gate decision from <see cref="TimberFramedBlockContentGripStageDReadiness"/>.
/// </summary>
public enum TimberFramedBlockContentGripStageEArmDecision
{
    Allowed = 0,
    Blocked = 1,
}

/// <summary>
/// Stage E STATUS classifier after in-callback normalize (no UNDO claim).
/// </summary>
public enum TimberFramedBlockContentGripNormalizeProofState
{
    PostGripCorrect = 0,
    PostGripWrong = 1,
    CallbackFailed = 2,
    Unknown = 3,
}

/// <summary>
/// Stage E per-callback normalize outcome. Mid-drag transient states must not
/// increment ExceptionCount.
/// </summary>
public enum TimberFramedBlockContentGripNormalizeOutcome
{
    SuccessChanged = 0,
    SuccessNoOp = 1,
    TransientSkip = 2,
    NotApplicable = 3,
    Failed = 4,
}

/// <summary>
/// Stage D read-only inspection outcome. Expected mid-drag states must not throw.
/// </summary>
public enum TimberFramedBlockContentGripReadOnlyInspectionOutcome
{
    Success = 0,
    NotApplicable = 1,
    TransientGeometryNotReady = 2,
    ObjectUnavailable = 3,
    InvalidContract = 4,
    Failed = 5,
}

/// <summary>
/// CAD-neutral Stage D read-only inspection result after native grip move.
/// Immutable scalar snapshot only — no CAD object references.
/// </summary>
public sealed record TimberFramedBlockContentGripReadOnlyInspection(
    string Handle,
    bool NativeMoveCompleted,
    bool CurrentPlacementCorrect,
    bool WouldNormalizeDogleg,
    bool WouldNormalizeContentSide,
    string BlockContentName,
    string DimensionColumnSideToken,
    double AttachmentX,
    double AttachmentY,
    double KneeX,
    double KneeY,
    double BlockPositionX,
    double BlockPositionY);
