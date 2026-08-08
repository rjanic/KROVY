using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Portable P4B grip-undo proof policy tests. Does NOT claim AutoCAD undo
/// grouping — that is host-only (AK_DEV_FBC_UNDO_PROOF_*).
/// </summary>
public sealed class TimberFramedBlockContentGripUndoProofRulesTests
{
    [Fact]
    public void Applicability_AcceptsR2CombinedWithDimnxOrDimpx()
    {
        var dimnx = CreateCombinedName(
            TimberFramedBlockContentKind.Circle,
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);
        var dimpx = CreateCombinedName(
            TimberFramedBlockContentKind.Slot,
            TimberFramedBlockContentDimensionColumnSide.PositiveLocalX);

        Assert.True(
            TimberFramedBlockContentGripUndoProofRules.IsApplicableBlockContent(
                dimnx,
                hasItemNo: true,
                hasWidth: true,
                hasHeight: true));
        Assert.True(
            TimberFramedBlockContentGripUndoProofRules.IsApplicableBlockContent(
                dimpx,
                hasItemNo: true,
                hasWidth: true,
                hasHeight: true));
    }

    [Fact]
    public void Applicability_ItemOnlyIsNoOp()
    {
        var itemOnly = TimberFramedBlockContentVariantRules.CreateSafeBlockName(
            TimberFramedBlockContentVariantRules.CreateLegacyR2RawKey(
                TimberFramedBlockContentKind.Circle,
                "MEDIUM",
                "Standard",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.ItemOnly));

        Assert.True(
            TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                itemOnly,
                out var parse));
        Assert.True(
            TimberFramedBlockContentGripUndoProofRules.IsItemOnlyNoOp(parse));
        Assert.False(
            TimberFramedBlockContentGripUndoProofRules.IsApplicableBlockContent(
                itemOnly,
                hasItemNo: true,
                hasWidth: false,
                hasHeight: false));
    }

    [Fact]
    public void Applicability_ForeignLegacyG4Rejected()
    {
        Assert.False(
            TimberFramedBlockContentGripUndoProofRules.IsApplicableBlockContent(
                "AK_G4_COMPOSITE_THING",
                hasItemNo: true,
                hasWidth: true,
                hasHeight: true));
        Assert.False(
            TimberFramedBlockContentGripUndoProofRules.IsApplicableBlockContent(
                "SOME_FOREIGN_MLEADER_BLOCK",
                hasItemNo: true,
                hasWidth: true,
                hasHeight: true));
        Assert.False(
            TimberFramedBlockContentGripUndoProofRules.IsApplicableBlockContent(
                CreateCombinedName(
                    TimberFramedBlockContentKind.Circle,
                    TimberFramedBlockContentDimensionColumnSide.NegativeLocalX),
                hasItemNo: true,
                hasWidth: false,
                hasHeight: true));
    }

    [Fact]
    public void GripKind_RigidMoveSkipsNormalize_UnknownRunsIdempotent()
    {
        Assert.False(
            TimberFramedBlockContentGripUndoProofRules.ShouldRunSharedNormalize(
                proofEnabled: true,
                TimberFramedBlockContentGripKind.RigidWholeLeaderMove));
        Assert.True(
            TimberFramedBlockContentGripUndoProofRules.ShouldRunSharedNormalize(
                proofEnabled: true,
                TimberFramedBlockContentGripKind.Unknown));
        Assert.True(
            TimberFramedBlockContentGripUndoProofRules.ShouldRunSharedNormalize(
                proofEnabled: true,
                TimberFramedBlockContentGripKind.GeometryAffecting));
        Assert.False(
            TimberFramedBlockContentGripUndoProofRules.ShouldRunSharedNormalize(
                proofEnabled: false,
                TimberFramedBlockContentGripKind.GeometryAffecting));

        Assert.Equal(
            TimberFramedBlockContentGripKind.RigidWholeLeaderMove,
            TimberFramedBlockContentGripUndoProofRules.ClassifyGripKind(
                attachmentMoved: true,
                kneeMoved: true,
                blockPositionMoved: true,
                attachmentOffsetMatchesKnee: true,
                kneeOffsetMatchesBlockPosition: true));
        Assert.Equal(
            TimberFramedBlockContentGripKind.GeometryAffecting,
            TimberFramedBlockContentGripUndoProofRules.ClassifyGripKind(
                attachmentMoved: false,
                kneeMoved: true,
                blockPositionMoved: true,
                attachmentOffsetMatchesKnee: false,
                kneeOffsetMatchesBlockPosition: false));
        Assert.Equal(
            TimberFramedBlockContentGripKind.Unknown,
            TimberFramedBlockContentGripUndoProofRules.ClassifyGripKind(
                attachmentMoved: false,
                kneeMoved: false,
                blockPositionMoved: false,
                attachmentOffsetMatchesKnee: false,
                kneeOffsetMatchesBlockPosition: false));
    }

    [Fact]
    public void Classifier_PrePostWrongSplitUnknown()
    {
        var pre = Snapshot(
            handle: "A1",
            kneeX: 0,
            kneeY: 0,
            bpX: 100,
            dim: "DIMNX",
            block: "B_DIMNX",
            kdi: true,
            doglegX: 1);
        var post = Snapshot(
            handle: "A1",
            kneeX: 0,
            kneeY: 400,
            bpX: -100,
            dim: "DIMPX",
            block: "B_DIMPX",
            kdi: true,
            doglegX: -1);
        var wrong = Snapshot(
            handle: "A1",
            kneeX: 0,
            kneeY: 400,
            bpX: -100,
            dim: "DIMNX",
            block: "B_DIMNX",
            kdi: false,
            doglegX: 1);
        var split = Snapshot(
            handle: "A1",
            kneeX: 0,
            kneeY: 400,
            bpX: -100,
            dim: "DIMNX",
            block: "B_DIMNX",
            kdi: false,
            doglegX: 1);

        Assert.Equal(
            TimberFramedBlockContentGripUndoProofState.PreGripCorrect,
            TimberFramedBlockContentGripUndoProofRules.ClassifyState(
                pre,
                post,
                pre,
                normalizeChangedContentOrDogleg: true));
        Assert.Equal(
            TimberFramedBlockContentGripUndoProofRules.StatePreGripCorrect,
            TimberFramedBlockContentGripUndoProofRules.FormatState(
                TimberFramedBlockContentGripUndoProofState.PreGripCorrect));

        Assert.Equal(
            TimberFramedBlockContentGripUndoProofState.PostGripCorrect,
            TimberFramedBlockContentGripUndoProofRules.ClassifyState(
                pre,
                post,
                post,
                normalizeChangedContentOrDogleg: true));
        Assert.Equal(
            TimberFramedBlockContentGripUndoProofRules.StatePostGripCorrect,
            TimberFramedBlockContentGripUndoProofRules.FormatState(
                TimberFramedBlockContentGripUndoProofState.PostGripCorrect));

        Assert.Equal(
            TimberFramedBlockContentGripUndoProofState.PostGripWrong,
            TimberFramedBlockContentGripUndoProofRules.ClassifyState(
                pre,
                postGrip: null,
                wrong,
                normalizeChangedContentOrDogleg: false));
        Assert.Equal(
            TimberFramedBlockContentGripUndoProofRules.StatePostGripWrong,
            TimberFramedBlockContentGripUndoProofRules.FormatState(
                TimberFramedBlockContentGripUndoProofState.PostGripWrong));

        Assert.Equal(
            TimberFramedBlockContentGripUndoProofState.SplitUndo,
            TimberFramedBlockContentGripUndoProofRules.ClassifyState(
                pre,
                post,
                split,
                normalizeChangedContentOrDogleg: true));
        Assert.Equal(
            TimberFramedBlockContentGripUndoProofRules.StateSplitUndo,
            TimberFramedBlockContentGripUndoProofRules.FormatState(
                TimberFramedBlockContentGripUndoProofState.SplitUndo));

        Assert.Equal(
            TimberFramedBlockContentGripUndoProofState.Unknown,
            TimberFramedBlockContentGripUndoProofRules.ClassifyState(
                pre,
                post,
                current: null,
                normalizeChangedContentOrDogleg: true));
        Assert.Equal(
            TimberFramedBlockContentGripUndoProofRules.StateUnknown,
            TimberFramedBlockContentGripUndoProofRules.FormatState(
                TimberFramedBlockContentGripUndoProofState.Unknown));
    }

    private static string CreateCombinedName(
        TimberFramedBlockContentKind kind,
        TimberFramedBlockContentDimensionColumnSide side) =>
        TimberFramedBlockContentVariantRules.CreateSafeBlockName(
            TimberFramedBlockContentVariantRules.CreateLegacyR2RawKey(
                kind,
                "MEDIUM",
                "Standard",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.Combined,
                side));

    private static TimberFramedBlockContentGripUndoProofSnapshot Snapshot(
        string handle,
        double kneeX,
        double kneeY,
        double bpX,
        string dim,
        string block,
        bool kdi,
        double doglegX) =>
        new(
            handle,
            AttachmentX: 0,
            AttachmentY: 0,
            KneeX: kneeX,
            KneeY: kneeY,
            BlockPositionX: bpX,
            BlockPositionY: kneeY,
            DoglegDirectionX: doglegX,
            DoglegDirectionY: 0,
            DoglegLength: 50,
            BlockContentName: block,
            DimensionColumnSideToken: dim,
            KdiCorrect: kdi,
            ItemNoText: "12",
            WidthText: "120",
            HeightText: "60",
            ItemNoHeight: 2.7,
            WidthHeight: 2.5,
            HeightHeight: 2.5);
}
