using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// CAD-neutral P4B grip-scoped undo proof policy: applicability filter,
/// grip typology, snapshot matching, and STATUS classifier. No CAD host types.
/// Does not claim AutoCAD undo-record merging — that is host-only.
/// </summary>
public static class TimberFramedBlockContentGripUndoProofRules
{
    public const string DebugMarkerToken = "FBC_UNDO_PROOF";
    public const string RepresentativeCaseKey = "P4B-CIRCLE-COMB-R-90-D50";
    public const string StatePreGripCorrect = "STATE_PRE_GRIP_CORRECT";
    public const string StatePostGripCorrect = "STATE_POST_GRIP_CORRECT";
    public const string StatePostGripWrong = "STATE_POST_GRIP_WRONG";
    public const string StateSplitUndo = "STATE_SPLIT_UNDO";
    public const string StateUnknown = "STATE_UNKNOWN";

    /// <summary>Match PRE/POST snapshots (same handle, geometry, content).</summary>
    public const double GeometryMatchToleranceMm = 1e-3d;

    /// <summary>Knee/landing considered moved vs PRE after opposite-side stretch.</summary>
    public const double KneeMovedToleranceMm = 1.0d;

    public static bool HasCombinedAttributeContract(
        bool hasItemNo,
        bool hasWidth,
        bool hasHeight) =>
        TimberFramedBlockContentStretchNormalizeRules.HasCombinedAttributeContract(
            hasItemNo,
            hasWidth,
            hasHeight);

    /// <summary>
    /// P4B IsApplicable filter: host MLeader BlockContent R2 Combined with
    /// ITEM_NO+WIDTH+HEIGHT and DIMNX or DIMPX. ItemOnly / foreign / legacy / G4
    /// → false.
    /// </summary>
    public static bool IsApplicableBlockContent(
        string? blockNameOrRawKey,
        bool hasItemNo,
        bool hasWidth,
        bool hasHeight) =>
        TimberFramedBlockContentStretchNormalizeRules.IsEligibleBlockContent(
            blockNameOrRawKey,
            hasItemNo,
            hasWidth,
            hasHeight);

    public static bool IsApplicableBlockContent(
        TimberFramedBlockContentR2VariantParse parse,
        bool hasItemNo,
        bool hasWidth,
        bool hasHeight) =>
        TimberFramedBlockContentStretchNormalizeRules.IsEligibleBlockContent(
            parse,
            hasItemNo,
            hasWidth,
            hasHeight);

    /// <summary>
    /// ItemOnly never enters P4B normalize (no-op / untouched).
    /// </summary>
    public static bool IsItemOnlyNoOp(TimberFramedBlockContentR2VariantParse parse) =>
        parse.IsItemOnly;

    /// <summary>
    /// Rigid whole-MLeader move keeps K→D→I; skip write. Geometry-affecting and
    /// unknown run shared dogleg → content-side (idempotent when already correct).
    /// </summary>
    public static bool ShouldRunSharedNormalize(
        bool proofEnabled,
        TimberFramedBlockContentGripKind gripKind)
    {
        if (!proofEnabled)
        {
            return false;
        }

        return gripKind != TimberFramedBlockContentGripKind.RigidWholeLeaderMove;
    }

    /// <summary>
    /// Classify grip from uniform offset: attachment+knee+BP all shift by the
    /// same vector → rigid move. Otherwise geometry-affecting. Empty/unknown
    /// indices → Unknown (idempotent eval).
    /// </summary>
    public static TimberFramedBlockContentGripKind ClassifyGripKind(
        bool attachmentMoved,
        bool kneeMoved,
        bool blockPositionMoved,
        bool attachmentOffsetMatchesKnee,
        bool kneeOffsetMatchesBlockPosition)
    {
        if (attachmentMoved &&
            kneeMoved &&
            blockPositionMoved &&
            attachmentOffsetMatchesKnee &&
            kneeOffsetMatchesBlockPosition)
        {
            return TimberFramedBlockContentGripKind.RigidWholeLeaderMove;
        }

        if (kneeMoved || blockPositionMoved || attachmentMoved)
        {
            return TimberFramedBlockContentGripKind.GeometryAffecting;
        }

        return TimberFramedBlockContentGripKind.Unknown;
    }

    public static bool PointsMatch(
        double ax,
        double ay,
        double bx,
        double by,
        double toleranceMm = GeometryMatchToleranceMm)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return Math.Sqrt((dx * dx) + (dy * dy)) <= toleranceMm;
    }

    /// <summary>
    /// Attachment / knee / BlockPosition only — excludes dogleg so SPLIT_UNDO
    /// can detect post grip geometry with pre dogleg/DIM artifacts.
    /// </summary>
    public static bool SnapshotsMatchGripGeometry(
        TimberFramedBlockContentGripUndoProofSnapshot a,
        TimberFramedBlockContentGripUndoProofSnapshot b,
        double toleranceMm = GeometryMatchToleranceMm) =>
        string.Equals(a.Handle, b.Handle, StringComparison.OrdinalIgnoreCase) &&
        PointsMatch(a.AttachmentX, a.AttachmentY, b.AttachmentX, b.AttachmentY, toleranceMm) &&
        PointsMatch(a.KneeX, a.KneeY, b.KneeX, b.KneeY, toleranceMm) &&
        PointsMatch(
            a.BlockPositionX,
            a.BlockPositionY,
            b.BlockPositionX,
            b.BlockPositionY,
            toleranceMm);

    public static bool SnapshotsMatchGeometry(
        TimberFramedBlockContentGripUndoProofSnapshot a,
        TimberFramedBlockContentGripUndoProofSnapshot b,
        double toleranceMm = GeometryMatchToleranceMm) =>
        SnapshotsMatchGripGeometry(a, b, toleranceMm) &&
        PointsMatch(
            a.DoglegDirectionX,
            a.DoglegDirectionY,
            b.DoglegDirectionX,
            b.DoglegDirectionY,
            toleranceMm) &&
        Math.Abs(a.DoglegLength - b.DoglegLength) <= toleranceMm;

    public static bool SnapshotsMatchContent(
        TimberFramedBlockContentGripUndoProofSnapshot a,
        TimberFramedBlockContentGripUndoProofSnapshot b) =>
        string.Equals(
            a.BlockContentName,
            b.BlockContentName,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            a.DimensionColumnSideToken,
            b.DimensionColumnSideToken,
            StringComparison.OrdinalIgnoreCase) &&
        a.KdiCorrect == b.KdiCorrect &&
        string.Equals(a.ItemNoText, b.ItemNoText, StringComparison.Ordinal) &&
        string.Equals(a.WidthText, b.WidthText, StringComparison.Ordinal) &&
        string.Equals(a.HeightText, b.HeightText, StringComparison.Ordinal);

    public static bool SnapshotsMatch(
        TimberFramedBlockContentGripUndoProofSnapshot a,
        TimberFramedBlockContentGripUndoProofSnapshot b,
        double toleranceMm = GeometryMatchToleranceMm) =>
        SnapshotsMatchGeometry(a, b, toleranceMm) &&
        SnapshotsMatchContent(a, b);

    public static bool KneeMovedFromPre(
        TimberFramedBlockContentGripUndoProofSnapshot pre,
        TimberFramedBlockContentGripUndoProofSnapshot current,
        double toleranceMm = KneeMovedToleranceMm) =>
        !PointsMatch(pre.KneeX, pre.KneeY, current.KneeX, current.KneeY, toleranceMm) ||
        !PointsMatch(
            pre.BlockPositionX,
            pre.BlockPositionY,
            current.BlockPositionX,
            current.BlockPositionY,
            toleranceMm);

    public static bool NormalizeArtifactsMatchPre(
        TimberFramedBlockContentGripUndoProofSnapshot pre,
        TimberFramedBlockContentGripUndoProofSnapshot current) =>
        string.Equals(
            pre.DimensionColumnSideToken,
            current.DimensionColumnSideToken,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            pre.BlockContentName,
            current.BlockContentName,
            StringComparison.OrdinalIgnoreCase) &&
        PointsMatch(
            pre.DoglegDirectionX,
            pre.DoglegDirectionY,
            current.DoglegDirectionX,
            current.DoglegDirectionY);

    public static bool NormalizeArtifactsMatchPost(
        TimberFramedBlockContentGripUndoProofSnapshot post,
        TimberFramedBlockContentGripUndoProofSnapshot current) =>
        string.Equals(
            post.DimensionColumnSideToken,
            current.DimensionColumnSideToken,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            post.BlockContentName,
            current.BlockContentName,
            StringComparison.OrdinalIgnoreCase) &&
        PointsMatch(
            post.DoglegDirectionX,
            post.DoglegDirectionY,
            current.DoglegDirectionX,
            current.DoglegDirectionY);

    /// <summary>
    /// STATUS classifier. When <paramref name="postGrip"/> is set (after a
    /// successful in-grip normalize), SPLIT_UNDO is geometry≈post with
    /// DIMNX/DIMPX/dogleg≈pre. Does not prove AutoCAD undo grouping alone.
    /// </summary>
    public static TimberFramedBlockContentGripUndoProofState ClassifyState(
        TimberFramedBlockContentGripUndoProofSnapshot? preGrip,
        TimberFramedBlockContentGripUndoProofSnapshot? postGrip,
        TimberFramedBlockContentGripUndoProofSnapshot? current,
        bool normalizeChangedContentOrDogleg)
    {
        if (preGrip is null || current is null)
        {
            return TimberFramedBlockContentGripUndoProofState.Unknown;
        }

        if (!string.Equals(
                preGrip.Handle,
                current.Handle,
                StringComparison.OrdinalIgnoreCase))
        {
            return TimberFramedBlockContentGripUndoProofState.Unknown;
        }

        if (SnapshotsMatch(preGrip, current))
        {
            return TimberFramedBlockContentGripUndoProofState.PreGripCorrect;
        }

        if (postGrip is not null &&
            string.Equals(
                postGrip.Handle,
                current.Handle,
                StringComparison.OrdinalIgnoreCase) &&
            SnapshotsMatch(postGrip, current))
        {
            return TimberFramedBlockContentGripUndoProofState.PostGripCorrect;
        }

        var kneeMoved = KneeMovedFromPre(preGrip, current);
        if (!kneeMoved)
        {
            return TimberFramedBlockContentGripUndoProofState.Unknown;
        }

        if (postGrip is not null &&
            normalizeChangedContentOrDogleg &&
            SnapshotsMatchGripGeometry(postGrip, current, KneeMovedToleranceMm) &&
            NormalizeArtifactsMatchPre(preGrip, current) &&
            !NormalizeArtifactsMatchPost(postGrip, current))
        {
            return TimberFramedBlockContentGripUndoProofState.SplitUndo;
        }

        if (current.KdiCorrect &&
            !string.IsNullOrWhiteSpace(current.DimensionColumnSideToken))
        {
            return TimberFramedBlockContentGripUndoProofState.PostGripCorrect;
        }

        if (!current.KdiCorrect)
        {
            return TimberFramedBlockContentGripUndoProofState.PostGripWrong;
        }

        return TimberFramedBlockContentGripUndoProofState.Unknown;
    }

    public static string FormatState(TimberFramedBlockContentGripUndoProofState state) =>
        state switch
        {
            TimberFramedBlockContentGripUndoProofState.PreGripCorrect =>
                StatePreGripCorrect,
            TimberFramedBlockContentGripUndoProofState.PostGripCorrect =>
                StatePostGripCorrect,
            TimberFramedBlockContentGripUndoProofState.PostGripWrong =>
                StatePostGripWrong,
            TimberFramedBlockContentGripUndoProofState.SplitUndo =>
                StateSplitUndo,
            _ => StateUnknown,
        };

    public static string FormatDimensionColumnSideToken(
        TimberFramedBlockContentDimensionColumnSide? side) =>
        side switch
        {
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX =>
                TimberFramedBlockContentVariantRules.DimensionsNegativeXToken,
            TimberFramedBlockContentDimensionColumnSide.PositiveLocalX =>
                TimberFramedBlockContentVariantRules.DimensionsPositiveXToken,
            _ => string.Empty,
        };
}
