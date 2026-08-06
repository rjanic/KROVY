namespace AcKrovy.Core.Models;

/// <summary>
/// World-space Combined dimension-column placement relative to knee → ITEM_NO.
/// Visual contract: column center D must lie between knee K and item center I.
/// </summary>
public readonly record struct TimberFramedBlockContentDimensionColumnPlacementEvaluation(
    bool IsCorrect,
    double ParameterT,
    double PerpendicularDistance,
    TimberPlanarPoint KneePoint,
    TimberPlanarPoint ItemCenter,
    TimberPlanarPoint DimensionColumnCenter,
    string Reason);

/// <summary>
/// Decision after evaluating current D and its mirror about ITEM_NO.
/// </summary>
public enum TimberFramedBlockContentDimensionColumnMirrorDecision
{
    NoOp = 0,
    Swap = 1,
    FailAmbiguous = 2,
    FailUnresolved = 3,
    FailDegenerate = 4,
}

/// <summary>
/// Current vs mirrored column placement and the resulting swap/no-op decision.
/// </summary>
public readonly record struct TimberFramedBlockContentDimensionColumnMirrorEvaluation(
    TimberFramedBlockContentDimensionColumnMirrorDecision Decision,
    TimberFramedBlockContentDimensionColumnPlacementEvaluation Current,
    TimberFramedBlockContentDimensionColumnPlacementEvaluation Mirrored,
    TimberPlanarPoint MirroredDimensionColumnCenter);
