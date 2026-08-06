namespace AcKrovy.Core.Models;

/// <summary>CAD-neutral DEBUG autotest case definition (no host CAD types).</summary>
public sealed record TimberFramedBlockContentAutotestCase(
    string Key,
    TimberFramedBlockContentKind Kind,
    TimberFramedBlockContentPresentation Presentation,
    TimberLeaderHorizontalSide Side,
    double ElementAxisDegrees,
    int ScaleDenominator,
    bool RequirePersistence,
    bool PreferLifecycleProcessor,
    TimberFramedBlockContentAutotestAngleBand AngleBand,
    TimberFramedBlockContentAutotestCoverageTags Coverage);

[Flags]
public enum TimberFramedBlockContentAutotestCoverageTags
{
    None = 0,
    FramePlain = 1 << 0,
    FrameCircle = 1 << 1,
    FrameRectangle = 1 << 2,
    FrameSlot = 1 << 3,
    AngleCardinal = 1 << 4,
    AngleNearCardinal = 1 << 5,
    AngleOblique = 1 << 6,
    Scale25 = 1 << 7,
    Scale50 = 1 << 8,
    Scale100 = 1 << 9,
    SideLeft = 1 << 10,
    SideRight = 1 << 11,
    PresentationCombined = 1 << 12,
    PresentationItemOnly = 1 << 13,
    Persistence = 1 << 14,
    Lifecycle = 1 << 15,
}

public enum TimberFramedBlockContentAutotestAngleBand
{
    Cardinal,
    NearCardinal,
    Oblique,
}

public enum TimberFramedBlockContentAutotestPhase
{
    Create,
    CreateContentSideNoOp,
    ContentSideWrongBtr,
    RightToLeftCrossing,
    LeftToRightCrossing,
    SyntheticCrossingSetup,
    NormalizeAfterCrossing,
    SecondNormalizeNoOp,
    PersistenceReopen,
    LifecycleProcessor,
    ExternalInventory,
}

public enum TimberFramedBlockContentAutotestCategory
{
    CreatePlacement,
    ContentSideService,
    RightToLeft,
    LeftToRight,
    SyntheticCrossingSetup,
    CardinalAngles,
    NearCardinalAngles,
    Scales,
    ItemOnly,
    Persistence,
    LifecycleProcessor,
    SameHandle,
    ForbiddenDrift,
    ExternalEntities,
    RunnerIsolation,
    DoglegGeometry,
    ContentSideForbiddenDrift,
}

public enum TimberFramedBlockContentAutotestFailureKind
{
    Product = 0,
    Fixture = 1,
}

public enum TimberFramedBlockContentAutotestCategoryStatus
{
    Untested = 0,
    Pass = 1,
    Fail = 2,
    BlockedByFixture = 3,
}

/// <summary>One assertion failure for console (max 10) and detail log.</summary>
public sealed record TimberFramedBlockContentAutotestFailure(
    string CaseKey,
    string Phase,
    string Expected,
    string Actual,
    TimberFramedBlockContentAutotestFailureKind Kind);

/// <summary>Cleanup XData marker payload (RegApp owned by host DEBUG harness).</summary>
public sealed record TimberFramedBlockContentAutotestMarker(
    string Token,
    string RunId,
    string CaseKey)
{
    public string ToPayload() => $"{Token}|{RunId}|{CaseKey}";

    public static bool TryParse(
        string? payload,
        string expectedToken,
        out TimberFramedBlockContentAutotestMarker marker)
    {
        marker = new TimberFramedBlockContentAutotestMarker(
            string.Empty,
            string.Empty,
            string.Empty);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var text = payload!;
        var parts = text.Split('|');
        if (parts.Length < 3)
        {
            return false;
        }

        var token = parts[0] ?? string.Empty;
        var runId = parts[1] ?? string.Empty;
        var caseKey = parts[2] ?? string.Empty;
        if (!string.Equals(token, expectedToken, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(caseKey))
        {
            return false;
        }

        marker = new TimberFramedBlockContentAutotestMarker(token, runId, caseKey);
        return true;
    }

    public bool MatchesRun(string runId) =>
        string.Equals(RunId, runId, StringComparison.Ordinal);
}
