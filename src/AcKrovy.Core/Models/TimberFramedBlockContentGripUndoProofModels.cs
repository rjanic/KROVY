namespace AcKrovy.Core.Models;

/// <summary>
/// P4B host STATUS classifier. Does not claim AutoCAD undo grouping by itself;
/// host protocol distinguishes PRE / POST / SPLIT via snapshots.
/// </summary>
public enum TimberFramedBlockContentGripUndoProofState
{
    PreGripCorrect = 0,
    PostGripCorrect = 1,
    PostGripWrong = 2,
    SplitUndo = 3,
    Unknown = 4,
}

/// <summary>
/// Grip typology for whether normalize should run after a native grip offset.
/// Unknown → idempotent shared eval only (must not change a correct state).
/// </summary>
public enum TimberFramedBlockContentGripKind
{
    Unknown = 0,
    RigidWholeLeaderMove = 1,
    GeometryAffecting = 2,
}

/// <summary>
/// CAD-neutral PRE/POST geometry + content-side snapshot for P4B STATUS.
/// </summary>
public sealed record TimberFramedBlockContentGripUndoProofSnapshot(
    string Handle,
    double AttachmentX,
    double AttachmentY,
    double KneeX,
    double KneeY,
    double BlockPositionX,
    double BlockPositionY,
    double DoglegDirectionX,
    double DoglegDirectionY,
    double DoglegLength,
    string BlockContentName,
    string DimensionColumnSideToken,
    bool KdiCorrect,
    string ItemNoText,
    string WidthText,
    string HeightText,
    double ItemNoHeight,
    double WidthHeight,
    double HeightHeight);
