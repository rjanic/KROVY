using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Host bridge for Combined world-space K→D→I column placement.
/// Core owns the evaluator; this maps AttrRef world AlignmentPoints.
/// </summary>
internal static class AutoCadFramedBlockContentDimensionColumnPlacementService
{
#if DEBUG
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        R3RefreshPresentationDecision> RefreshPresentationTrace =
        new(StringComparer.OrdinalIgnoreCase);
#endif

    internal readonly record struct WorldAttributePoints(
        Point3d Knee,
        Point3d ItemAlignment,
        Point3d WidthAlignment,
        Point3d HeightAlignment,
        TimberFramedBlockContentDimensionColumnSide? ParsedColumnSide,
        string BlockName);

    public static bool TryReadWorldAttributePoints(
        Transaction transaction,
        MLeader leader,
        out WorldAttributePoints points,
        out string note)
    {
        points = default;
        note = string.Empty;
        var blockId = leader.BlockContentId;
        if (blockId.IsNull)
        {
            note = "BlockContentId missing.";
            return false;
        }

        if (transaction.GetObject(blockId, OpenMode.ForRead, true) is not
                BlockTableRecord block ||
            block.IsErased)
        {
            note = "BlockTableRecord unavailable.";
            return false;
        }

        AttributeDefinition? itemDef = null;
        AttributeDefinition? widthDef = null;
        AttributeDefinition? heightDef = null;
        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                    AttributeDefinition attribute ||
                attribute.IsErased)
            {
                continue;
            }

            if (string.Equals(
                    attribute.Tag,
                    TimberFramedBlockContentDefinitionRules.ItemNoTag,
                    StringComparison.OrdinalIgnoreCase))
            {
                itemDef = attribute;
            }
            else if (string.Equals(
                         attribute.Tag,
                         TimberFramedBlockContentDefinitionRules.WidthTag,
                         StringComparison.OrdinalIgnoreCase))
            {
                widthDef = attribute;
            }
            else if (string.Equals(
                         attribute.Tag,
                         TimberFramedBlockContentDefinitionRules.HeightTag,
                         StringComparison.OrdinalIgnoreCase))
            {
                heightDef = attribute;
            }
        }

        if (itemDef is null || widthDef is null || heightDef is null)
        {
            note = "Combined BTR must expose ITEM_NO/WIDTH/HEIGHT AttrDefs.";
            return false;
        }

        if (!TryReadWorldAlignment(leader, itemDef.ObjectId, out var itemAp) ||
            !TryReadWorldAlignment(leader, widthDef.ObjectId, out var widthAp) ||
            !TryReadWorldAlignment(leader, heightDef.ObjectId, out var heightAp))
        {
            note = "Missing AttrRef AlignmentPoint for ITEM_NO/WIDTH/HEIGHT.";
            return false;
        }

        TimberFramedBlockContentDimensionColumnSide? parsedSide = null;
        if (TimberFramedBlockContentVariantRules.TryParseR3VariantKey(
                block.Name,
                out var r3Parse) &&
            r3Parse.IsCombined)
        {
            parsedSide = r3Parse.ContentVariantSide;
        }
        else if (TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                     block.Name,
                     out var parse) &&
                 parse.DimensionColumnSide is TimberFramedBlockContentDimensionColumnSide side)
        {
            parsedSide = side;
        }

        points = new WorldAttributePoints(
            ReadKnee(leader),
            itemAp,
            widthAp,
            heightAp,
            parsedSide,
            block.Name);
        return true;
    }

    public static bool TryEvaluate(
        Transaction transaction,
        MLeader leader,
        out TimberFramedBlockContentDimensionColumnMirrorEvaluation evaluation,
        out WorldAttributePoints points,
        out string note)
    {
        evaluation = default;
        if (!TryReadWorldAttributePoints(transaction, leader, out points, out note))
        {
            return false;
        }

        evaluation =
            TimberFramedBlockContentDimensionColumnPlacementRules
                .EvaluateMirroredDimensionColumnPlacement(
                    ToPlanar(points.Knee),
                    ToPlanar(points.ItemAlignment),
                    ToPlanar(points.WidthAlignment),
                    ToPlanar(points.HeightAlignment));
        return true;
    }

    /// <summary>
    /// Measure the live world direction of the R3 block-local +X axis from the
    /// AttrRef geometry itself. This includes every transform already applied by
    /// AutoCAD (CREATE TransformBy, native grip recompute and BlockRotation),
    /// unlike BlockRotation alone, which is only relative to that live basis.
    /// </summary>
    public static bool TryResolveWorldContentXAxis(
        Transaction transaction,
        MLeader leader,
        out double worldAngleRadians,
        out string note)
    {
        worldAngleRadians = 0d;
        if (!TryReadWorldAttributePoints(transaction, leader, out var points, out note))
        {
            return false;
        }

        if (points.ParsedColumnSide is not
            TimberFramedBlockContentDimensionColumnSide side)
        {
            note = "R3 content variant side unavailable.";
            return false;
        }

        var dimensionCenter = new Point3d(
            (points.WidthAlignment.X + points.HeightAlignment.X) * 0.5d,
            (points.WidthAlignment.Y + points.HeightAlignment.Y) * 0.5d,
            (points.WidthAlignment.Z + points.HeightAlignment.Z) * 0.5d);
        var frameToDimensions = dimensionCenter - points.ItemAlignment;
        if (frameToDimensions.Length <=
            TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            note = "R3 frame-to-dimensions AttrRef axis is degenerate.";
            return false;
        }

        var localX = side == TimberFramedBlockContentDimensionColumnSide.NegativeLocalX
            ? -frameToDimensions
            : frameToDimensions;
        worldAngleRadians =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                Math.Atan2(localX.Y, localX.X));
        note = "world content +X measured from R3 AttrRef geometry";
        return true;
    }

    /// <summary>
    /// Same-transaction Combined column correction for create / normalize.
    /// Swaps BlockContentId only when mirrored placement is correct; restores
    /// on post-swap verification failure.
    /// </summary>
    public static bool TryCorrectCombinedContentSide(
        Database database,
        Transaction transaction,
        MLeader leader,
        TimberFramedBlockContentKind contentKind,
        string itemTextStyleName,
        string dimensionTextStyleName,
        ObjectId itemTextStyleId,
        ObjectId dimensionTextStyleId,
        double itemPaperHeightMm,
        double dimensionPaperHeightMm,
        string itemTextForFrameSizing,
        IReadOnlyList<(string Tag, string Text, double Height)> attributeValues,
        out bool changed,
        out ObjectId beforeBlockContentId,
        out ObjectId afterBlockContentId,
        out TimberFramedBlockContentDimensionColumnMirrorEvaluation evaluation,
        out string note,
        ObjectId preferredOppositeBlockContentId = default)
    {
        changed = false;
        beforeBlockContentId = leader.BlockContentId;
        afterBlockContentId = beforeBlockContentId;
        evaluation = default;
        note = string.Empty;

        if (beforeBlockContentId.IsNull)
        {
            note = "BlockContentId Null.";
            return false;
        }

        if (!TryEvaluate(transaction, leader, out evaluation, out var points, out note))
        {
            return false;
        }

        if (TimberFramedBlockContentStretchNormalizeRules.IsContentSideNoOp(
                evaluation.Decision))
        {
            note = "already correct K→D→I";
            return true;
        }

        if (!TimberFramedBlockContentStretchNormalizeRules.ShouldSwapContentSide(
                evaluation.Decision))
        {
            note =
                "content-side decision=" +
                TimberFramedBlockContentDimensionColumnPlacementRules.DescribeDecision(
                    evaluation.Decision) +
                ": " + evaluation.Current.Reason;
            return false;
        }

        if (points.ParsedColumnSide is not
            TimberFramedBlockContentDimensionColumnSide currentSide)
        {
            note = "Combined BTR name must parse as R2 DIMNX/DIMPX.";
            return false;
        }

        ObjectId targetBlockId;
        if (!preferredOppositeBlockContentId.IsNull)
        {
            if (preferredOppositeBlockContentId == beforeBlockContentId)
            {
                note = "preferred opposite BlockContentId equals current";
                return false;
            }

            targetBlockId = preferredOppositeBlockContentId;
        }
        else
        {
            if (!TryResolveOppositeVariantId(
                    database,
                    transaction,
                    beforeBlockContentId,
                    contentKind,
                    itemTextStyleName,
                    dimensionTextStyleName,
                    itemTextStyleId,
                    dimensionTextStyleId,
                    itemPaperHeightMm,
                    dimensionPaperHeightMm,
                    itemTextForFrameSizing,
                    out targetBlockId,
                    out note))
            {
                return false;
            }
        }

        var beforeAttachment = ReadAttachment(leader);
        var beforeKnee = ReadKnee(leader);
        var beforeBlockPosition = leader.BlockPosition;
        var beforeDoglegLength = leader.DoglegLength;
        var beforeDogleg = TryReadDogleg(leader);
        var beforeScale = leader.BlockScale;
        var beforeRotation = leader.BlockRotation;
        var beforeItemAttrRotation = TryReadItemAttributeRotation(transaction, leader);

        leader.BlockContentId = targetBlockId;
        ReapplyAttributes(transaction, leader, targetBlockId, attributeValues);
        RestoreGeometry(
            leader,
            beforeAttachment,
            beforeKnee,
            beforeBlockPosition,
            beforeDoglegLength,
            beforeDogleg,
            beforeScale,
            beforeRotation);
        RestoreReadableContentOrientation(
            leader,
            beforeRotation,
            beforeItemAttrRotation);

        if (!TryEvaluate(
                transaction,
                leader,
                out var afterEvaluation,
                out _,
                out var afterNote) ||
            !afterEvaluation.Current.IsCorrect)
        {
            leader.BlockContentId = beforeBlockContentId;
            ReapplyAttributes(
                transaction,
                leader,
                beforeBlockContentId,
                attributeValues);
            RestoreGeometry(
                leader,
                beforeAttachment,
                beforeKnee,
                beforeBlockPosition,
                beforeDoglegLength,
                beforeDogleg,
                beforeScale,
                beforeRotation);
            RestoreReadableContentOrientation(
                leader,
                beforeRotation,
                beforeItemAttrRotation);
            note =
                "post-swap K→D→I failed; restored original BlockContentId. " +
                (afterEvaluation.Current.Reason ?? afterNote);
            evaluation = afterEvaluation;
            afterBlockContentId = beforeBlockContentId;
            return false;
        }

        evaluation = afterEvaluation;
        changed = true;
        afterBlockContentId = targetBlockId;
        var requiredSide =
            TimberFramedBlockContentStretchNormalizeRules.OppositeColumnSide(
                currentSide);
        note =
            "swapped " +
            TimberFramedBlockContentVariantRules.ToDimensionColumnSideToken(currentSide) +
            " -> " +
            TimberFramedBlockContentVariantRules.ToDimensionColumnSideToken(requiredSide);
        return true;
    }

    /// <summary>
    /// Resolve opposite DIMNX/DIMPX BTR ObjectId. Never GetObject(Null). Current
    /// BTR must parse; opposite ObjectId must be non-null after Ensure.
    /// </summary>
    public static bool TryResolveOppositeVariantId(
        Database database,
        Transaction transaction,
        ObjectId currentBlockContentId,
        TimberFramedBlockContentKind contentKind,
        string itemTextStyleName,
        string dimensionTextStyleName,
        ObjectId itemTextStyleId,
        ObjectId dimensionTextStyleId,
        double itemPaperHeightMm,
        double dimensionPaperHeightMm,
        string itemTextForFrameSizing,
        out ObjectId oppositeBlockContentId,
        out string note)
    {
        oppositeBlockContentId = ObjectId.Null;
        note = string.Empty;

        if (currentBlockContentId.IsNull)
        {
            note = "current BTR ObjectId is Null";
            return false;
        }

        if (transaction.GetObject(currentBlockContentId, OpenMode.ForRead, true) is not
                BlockTableRecord currentBlock ||
            currentBlock.IsErased)
        {
            note = "current BTR unavailable";
            return false;
        }

        if (!TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                currentBlock.Name,
                out var parse) ||
            parse.DimensionColumnSide is not
                TimberFramedBlockContentDimensionColumnSide currentSide)
        {
            note = "current BTR name must parse as R2 DIMNX/DIMPX";
            return false;
        }

        var requiredSide =
            TimberFramedBlockContentStretchNormalizeRules.OppositeColumnSide(
                currentSide);
        var ensure = AcKrovyFramedBlockContentDefinitionService.Ensure(
            database,
            transaction,
            new AutoCadFramedBlockContentRequest(
                contentKind,
                TimberFramedBlockContentPresentation.Combined,
                itemTextStyleName,
                dimensionTextStyleName,
                itemPaperHeightMm,
                dimensionPaperHeightMm,
                itemTextStyleId,
                dimensionTextStyleId,
                itemTextForFrameSizing,
                requiredSide));
        if (!ensure.Succeeded ||
            ensure.BlockTableRecordId is not ObjectId targetBlockId ||
            targetBlockId.IsNull)
        {
            note = "Ensure opposite BTR: " + ensure.DiagnosticReason;
            return false;
        }

        if (targetBlockId == currentBlockContentId)
        {
            note = "opposite Ensure returned same BlockContentId";
            return false;
        }

        oppositeBlockContentId = targetBlockId;
        note =
            "opposite " +
            TimberFramedBlockContentVariantRules.ToDimensionColumnSideToken(requiredSide);
        return true;
    }

    /// <summary>
    /// Shared CREATE-final + post-native-grip ensure: resolve required R3 content
    /// variant from FINAL MLeader world geometry (attachment→BlockPosition in
    /// source Start→End T/N) and swap R3_RIGHT↔R3_LEFT only when mismatched.
    /// Same algorithm as <see cref="TrySwapR3ContentVariantIfSideChanged"/> —
    /// do not invent a second side rule.
    /// </summary>
    public static bool EnsureCorrectR3ContentVariantFromFinalGeometry(
        Database database,
        Transaction transaction,
        MLeader leader,
        double sourceStartX,
        double sourceStartY,
        double sourceEndX,
        double sourceEndY,
        TimberFramedBlockContentKind contentKind,
        string itemTextStyleName,
        string dimensionTextStyleName,
        ObjectId itemTextStyleId,
        ObjectId dimensionTextStyleId,
        double itemPaperHeightMm,
        double dimensionPaperHeightMm,
        string itemTextForFrameSizing,
        IReadOnlyList<(string Tag, string Text, double Height)> attributeValues,
        out bool changed,
        out ObjectId beforeBlockContentId,
        out ObjectId afterBlockContentId,
        out string note,
        double? effectiveContentWorldAngleRadians = null) =>
        TrySwapR3ContentVariantIfSideChanged(
            database,
            transaction,
            leader,
            sourceStartX,
            sourceStartY,
            sourceEndX,
            sourceEndY,
            contentKind,
            itemTextStyleName,
            dimensionTextStyleName,
            itemTextStyleId,
            dimensionTextStyleId,
            itemPaperHeightMm,
            dimensionPaperHeightMm,
            itemTextForFrameSizing,
            attributeValues,
            out changed,
            out beforeBlockContentId,
            out afterBlockContentId,
            out note,
            effectiveContentWorldAngleRadians);

    /// <summary>
    /// Production R3: swap R3_RIGHT ↔ R3_LEFT when final world side (source T/N)
    /// no longer matches the live content variant. Preserves attachment, knee,
    /// BlockPosition, dogleg, scale, rotation. Never forces 60° or dogleg rewrite.
    /// Prefer <see cref="EnsureCorrectR3ContentVariantFromFinalGeometry"/> at
    /// CREATE-final and post-grip call sites.
    /// </summary>
    public static bool TrySwapR3ContentVariantIfSideChanged(
        Database database,
        Transaction transaction,
        MLeader leader,
        double sourceStartX,
        double sourceStartY,
        double sourceEndX,
        double sourceEndY,
        TimberFramedBlockContentKind contentKind,
        string itemTextStyleName,
        string dimensionTextStyleName,
        ObjectId itemTextStyleId,
        ObjectId dimensionTextStyleId,
        double itemPaperHeightMm,
        double dimensionPaperHeightMm,
        string itemTextForFrameSizing,
        IReadOnlyList<(string Tag, string Text, double Height)> attributeValues,
        out bool changed,
        out ObjectId beforeBlockContentId,
        out ObjectId afterBlockContentId,
        out string note,
        double? effectiveContentWorldAngleRadians = null)
    {
        changed = false;
        beforeBlockContentId = leader.BlockContentId;
        afterBlockContentId = beforeBlockContentId;
        note = string.Empty;

        if (beforeBlockContentId.IsNull)
        {
            note = "BlockContentId Null.";
            return false;
        }

        if (transaction.GetObject(beforeBlockContentId, OpenMode.ForRead, true) is not
                BlockTableRecord currentBlock ||
            currentBlock.IsErased)
        {
            note = "current BTR unavailable";
            return false;
        }

        if (!TimberFramedBlockContentVariantRules.TryParseR3VariantKey(
                currentBlock.Name,
                out var parse) ||
            !parse.IsCombined)
        {
            note = "current BTR is not production R3 Combined";
            return false;
        }

        var attachment = ReadAttachment(leader);
        var knee = ReadKnee(leader);
        var blockPosition = leader.BlockPosition;
        double axisX;
        double axisY;
        if (effectiveContentWorldAngleRadians is double effectiveAngle)
        {
            if (double.IsNaN(effectiveAngle) || double.IsInfinity(effectiveAngle))
            {
                note = "effective R3 content world angle is not finite";
                return false;
            }

            axisX = Math.Cos(effectiveAngle);
            axisY = Math.Sin(effectiveAngle);
        }
        else if (!TryResolveEffectiveBlockLocalXAxis(
                     transaction,
                     leader,
                     out axisX,
                     out axisY))
        {
            note = "unable to resolve effective block local +X for R3 content variant";
            return false;
        }

        // Authority: knee vs frame in AttrDef space — not world/source L/R.
        if (!TimberFramedCombinedG5ContentVariantRules.TryResolveRequiredContentVariant(
                knee.X,
                knee.Y,
                blockPosition.X,
                blockPosition.Y,
                axisX,
                axisY,
                out var requiredSide,
                out _,
                out _))
        {
            note = "unable to resolve required R3 content variant from knee/frame landing";
            return false;
        }

        // Source endpoints remain on the public signature for call-site stability;
        // layout authority is knee/frame above (not attachment→content world L/R).
        _ = sourceStartX;
        _ = sourceStartY;
        _ = sourceEndX;
        _ = sourceEndY;

        var currentSide = parse.ContentVariantSide;
        if (TimberFramedCombinedG5ContentVariantRules.IsContentVariantMatch(
                currentSide,
                requiredSide))
        {
            note = "R3 content variant already matches knee-side landing";
            return true;
        }

        var ensure = AcKrovyFramedBlockContentDefinitionService.Ensure(
            database,
            transaction,
            new AutoCadFramedBlockContentRequest(
                contentKind,
                TimberFramedBlockContentPresentation.Combined,
                itemTextStyleName,
                dimensionTextStyleName,
                itemPaperHeightMm,
                dimensionPaperHeightMm,
                itemTextStyleId,
                dimensionTextStyleId,
                itemTextForFrameSizing,
                requiredSide));
        if (!ensure.Succeeded ||
            ensure.BlockTableRecordId is not ObjectId targetBlockId ||
            targetBlockId.IsNull)
        {
            note = "Ensure R3 content variant: " + ensure.DiagnosticReason;
            return false;
        }

        if (targetBlockId == beforeBlockContentId)
        {
            note = "required R3 variant Ensure returned same BlockContentId";
            return false;
        }

        var beforeKnee = knee;
        var beforeDoglegLength = leader.DoglegLength;
        var beforeDogleg = TryReadDogleg(leader);
        var beforeScale = leader.BlockScale;
        var beforeRotation = leader.BlockRotation;
        var beforeItemAttrRotation = TryReadItemAttributeRotation(transaction, leader);
        var beforeBlockPosition = blockPosition;

        leader.BlockContentId = targetBlockId;
        ReapplyAttributes(transaction, leader, targetBlockId, attributeValues);
        // R3 swap is content-only. Do not rewrite native grip vertices, dogleg or
        // landing geometry after base.MoveGripPointsAt.
        leader.BlockScale = beforeScale;
        leader.BlockRotation = beforeRotation;
        leader.BlockPosition = beforeBlockPosition;
        RestoreReadableContentOrientation(
            leader,
            beforeRotation,
            beforeItemAttrRotation);

        _ = attachment;
        _ = beforeKnee;
        _ = beforeDoglegLength;
        _ = beforeDogleg;

        changed = true;
        afterBlockContentId = targetBlockId;
        note =
            "swapped R3 " +
            (currentSide is TimberFramedBlockContentDimensionColumnSide live
                ? TimberFramedCombinedG5ContentVariantRules.ToContentVariantToken(live)
                : "LEGACY") +
            " -> " +
            TimberFramedCombinedG5ContentVariantRules.ToContentVariantToken(requiredSide);
        return true;
    }

    public static string FormatEvaluationDiagnostics(
        WorldAttributePoints points,
        TimberFramedBlockContentDimensionColumnMirrorEvaluation evaluation)
    {
        var current = evaluation.Current;
        var mirrored = evaluation.Mirrored;
        var decision =
            TimberFramedBlockContentDimensionColumnPlacementRules.DescribeDecision(
                evaluation.Decision);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"K={FormatPoint(points.Knee)} I={FormatPoint(points.ItemAlignment)} WIDTH={FormatPoint(points.WidthAlignment)} HEIGHT={FormatPoint(points.HeightAlignment)} D=({current.DimensionColumnCenter.X:R},{current.DimensionColumnCenter.Y:R}) t={current.ParameterT:R} perp={current.PerpendicularDistance:R} currentPlacementCorrect={current.IsCorrect} mirroredD=({evaluation.MirroredDimensionColumnCenter.X:R},{evaluation.MirroredDimensionColumnCenter.Y:R}) mirroredT={mirrored.ParameterT:R} mirroredPerp={mirrored.PerpendicularDistance:R} mirroredCorrect={mirrored.IsCorrect} parsedSide={points.ParsedColumnSide?.ToString() ?? "n/a"} block={points.BlockName} decision={decision}");
    }

    private static void RestoreGeometry(
        MLeader leader,
        Point3d attachment,
        Point3d knee,
        Point3d blockPosition,
        double doglegLength,
        Vector3d? dogleg,
        Scale3d scale,
        double rotation)
    {
        leader.BlockScale = scale;
        leader.BlockRotation = rotation;
        leader.DoglegLength = doglegLength;
        if (dogleg is Vector3d doglegVector)
        {
            var indexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            if (indexes.Length > 0)
            {
                leader.SetDogleg(indexes[0], doglegVector);
            }
        }

        leader.BlockPosition = blockPosition;
        var lineIndex = GetPrimaryLeaderLineIndex(leader);
        leader.SetFirstVertex(lineIndex, attachment);
        leader.SetLastVertex(lineIndex, knee);
        leader.BlockPosition = blockPosition;
        if (dogleg is Vector3d doglegAgain)
        {
            var indexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            if (indexes.Length > 0)
            {
                leader.SetDogleg(indexes[0], doglegAgain);
            }
        }
    }

    /// <summary>
    /// Layer C for refresh / source-rotation paths that rebuild world points in
    /// the readable basis without a fresh TransformBy. Sets shared
    /// <see cref="MLeader.BlockRotation"/> from
    /// <see cref="TimberFramedBlockContentReadableOrientationRules.Decide"/>
    /// (AttrDef rotations stay 0; no glyph mirror). Does not move attachment,
    /// knee, or BlockPosition. CREATE after TransformBy must keep
    /// BlockRotation = 0 — do not call this as a post-hoc override there.
    /// Annotation knee grip syncs presentation from final landing via
    /// <see cref="PreserveBlockContentPresentationRotation"/> with
    /// Decide(landing) — not this helper directly (AttrRefs must clear).
    /// Refresh must use
    /// <see cref="TryRestoreBlockContentPresentationAfterRefresh"/> so a world
    /// angle is never assigned as absolute relative BlockRotation on top of a
    /// CREATE TransformBy basis.
    /// </summary>
    public static void ApplyReadableBlockContentOrientation(
        MLeader leader,
        double sourceOrPresentationAngleRadians)
    {
        var decision =
            TimberFramedBlockContentReadableOrientationRules.Decide(
                sourceOrPresentationAngleRadians);
        leader.BlockRotation = decision.PresentationAngle;
    }

    /// <summary>
    /// Capture live world presentation before refresh AttrRef reapply / R3 sync.
    /// CREATE TransformBy leaves BR≈0 with AttrRef carrying the angle; refresh
    /// and post-swap leave BR=presentation with AttrRef≈0.
    /// </summary>
    public static double CaptureBlockContentPresentationRadians(
        Transaction transaction,
        MLeader leader) =>
        TimberFramedBlockContentGripPresentationRules
            .ResolvePreservedPresentationRadians(
                leader.BlockRotation,
                TryReadItemAttributeRotation(transaction, leader));

    /// <summary>
    /// Final refresh step after optional R3 BTR swap and AttrRef recreation.
    /// Measures the resulting world content axis and applies only the relative
    /// BlockRotation delta required by the preserved/source-rotated world
    /// presentation. No leader vertex, dogleg, landing, scale or BlockPosition
    /// is written.
    /// </summary>
    public static bool TryRestoreBlockContentPresentationAfterRefresh(
        Transaction transaction,
        MLeader leader,
        double sourceRotationBeforeRadians,
        double sourceRotationAfterRadians,
        double presentationBeforeRefreshRadians,
        out R3RefreshPresentationDecision decision,
        out string note)
    {
        decision = null!;
        if (!TryResolveWorldContentXAxis(
                transaction,
                leader,
                out var presentationAfterContentUpdate,
                out note))
        {
            note = "refresh world-axis measurement failed: " + note;
            return false;
        }

        decision = TimberFramedCombinedG5RefreshPlacementRules
            .ResolveContentOnlyRefreshPresentation(
                sourceRotationBeforeRadians,
                sourceRotationAfterRadians,
                presentationBeforeRefreshRadians,
                presentationAfterContentUpdate,
                leader.BlockRotation);
        PreserveBlockContentPresentationRotation(
            transaction,
            leader,
            decision.TargetBlockRotation);
        if (TryResolveWorldContentXAxis(
                transaction,
                leader,
                out var measuredAfterRefresh,
                out _))
        {
            decision = decision with
            {
                PresentationAfterRefresh = measuredAfterRefresh,
                PresentationRefreshDelta =
                    TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                        measuredAfterRefresh -
                        decision.PresentationBeforeRefresh),
            };
        }

        note = "refresh presentation restored by measured world delta";
        return true;
    }

#if DEBUG
    public static void RecordRefreshPresentationTrace(
        string sourceHandle,
        R3RefreshPresentationDecision decision)
    {
        if (string.IsNullOrWhiteSpace(sourceHandle) || decision is null)
        {
            return;
        }

        RefreshPresentationTrace[sourceHandle.Trim()] = decision;
    }

    public static bool TryGetRefreshPresentationTrace(
        string sourceHandle,
        out R3RefreshPresentationDecision decision)
    {
        decision = null!;
        return !string.IsNullOrWhiteSpace(sourceHandle) &&
               RefreshPresentationTrace.TryGetValue(
                   sourceHandle.Trim(),
                   out decision!);
    }
#endif

    /// <summary>
    /// Grip / BTR-swap / refresh install of layer C: clear AttrRef rotations then
    /// assign the exact value required in <see cref="MLeader.BlockRotation"/>
    /// space.
    /// Knee grip must first resolve a relative target with
    /// ResolveFinalContentPresentation because CREATE TransformBy may already
    /// own a non-zero world basis while BR is zero. Source-refresh paths whose
    /// content basis is world-zero may still pass their absolute presentation.
    /// </summary>
    public static void PreserveBlockContentPresentationRotation(
        Transaction transaction,
        MLeader leader,
        double preservedPresentationRadians)
    {
        if (double.IsNaN(preservedPresentationRadians) ||
            double.IsInfinity(preservedPresentationRadians))
        {
            return;
        }

        ClearCombinedAttributeRotations(transaction, leader);
        leader.BlockRotation = preservedPresentationRadians;
    }

    /// <summary>
    /// After BTR swap: restore the world presentation captured before the swap.
    /// On annotation knee grip that capture is already Decide(final landing);
    /// source-stretch / in-place refresh may capture live BR/AttrRef. AttrRefs
    /// were rebuilt from AttrDefs (rotation 0), so only BlockRotation is assigned.
    /// </summary>
    private static void RestoreReadableContentOrientation(
        MLeader leader,
        double beforeBlockRotation,
        double? beforeItemAttrRotation)
    {
        var preserved =
            TimberFramedBlockContentGripPresentationRules
                .ResolvePreservedPresentationRadians(
                    beforeBlockRotation,
                    beforeItemAttrRotation);
        leader.BlockRotation = preserved;
    }

    private static void ClearCombinedAttributeRotations(
        Transaction transaction,
        MLeader leader)
    {
        var blockId = leader.BlockContentId;
        if (blockId.IsNull ||
            transaction.GetObject(blockId, OpenMode.ForRead, true) is not
                BlockTableRecord block ||
            block.IsErased)
        {
            return;
        }

        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                    AttributeDefinition definition ||
                definition.IsErased)
            {
                continue;
            }

            var tag = definition.Tag ?? string.Empty;
            if (!string.Equals(
                    tag,
                    TimberFramedBlockContentDefinitionRules.ItemNoTag,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    tag,
                    TimberFramedBlockContentDefinitionRules.WidthTag,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    tag,
                    TimberFramedBlockContentDefinitionRules.HeightTag,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var attribute = leader.GetBlockAttribute(definition.ObjectId);
            if (attribute is null || Math.Abs(attribute.Rotation) <= 1e-12d)
            {
                continue;
            }

            attribute.Rotation = 0d;
            leader.SetBlockAttribute(definition.ObjectId, attribute);
        }
    }

    private static double? TryReadItemAttributeRotation(
        Transaction transaction,
        MLeader leader)
    {
        var blockId = leader.BlockContentId;
        if (blockId.IsNull ||
            transaction.GetObject(blockId, OpenMode.ForRead, true) is not
                BlockTableRecord block ||
            block.IsErased)
        {
            return null;
        }

        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                    AttributeDefinition definition ||
                definition.IsErased)
            {
                continue;
            }

            if (!string.Equals(
                    definition.Tag,
                    TimberFramedBlockContentDefinitionRules.ItemNoTag,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var itemAttr = leader.GetBlockAttribute(definition.ObjectId);
            return itemAttr?.Rotation;
        }

        return null;
    }

    private static void ReapplyAttributes(
        Transaction transaction,
        MLeader leader,
        ObjectId blockId,
        IReadOnlyList<(string Tag, string Text, double Height)> values)
    {
        if (blockId.IsNull)
        {
            return;
        }

        var byTag = values.ToDictionary(
            v => v.Tag,
            v => v,
            StringComparer.OrdinalIgnoreCase);
        if (transaction.GetObject(blockId, OpenMode.ForRead, true) is not
                BlockTableRecord block ||
            block.IsErased)
        {
            return;
        }

        foreach (ObjectId id in block)
        {
            if (id.IsNull)
            {
                continue;
            }

            if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                    AttributeDefinition definition ||
                definition.IsErased)
            {
                continue;
            }

            if (!byTag.TryGetValue(definition.Tag, out var value))
            {
                continue;
            }

            using var attribute = new AttributeReference();
            attribute.SetAttributeFromBlock(definition, Matrix3d.Identity);
            attribute.TextString = value.Text;
            attribute.Height = value.Height;
            leader.SetBlockAttribute(definition.ObjectId, attribute);
        }
    }

    private static bool TryReadWorldAlignment(
        MLeader leader,
        ObjectId definitionId,
        out Point3d alignment)
    {
        alignment = default;
        using var attribute = leader.GetBlockAttribute(definitionId);
        if (attribute is null)
        {
            return false;
        }

        try
        {
            alignment = attribute.AlignmentPoint;
            return true;
        }
        catch (AcadException)
        {
            alignment = attribute.Position;
            return true;
        }
    }

    private static Point3d ReadAttachment(MLeader leader) =>
        leader.GetFirstVertex(GetPrimaryLeaderLineIndex(leader));

    private static Point3d ReadKnee(MLeader leader) =>
        leader.GetLastVertex(GetPrimaryLeaderLineIndex(leader));

    private static int GetPrimaryLeaderLineIndex(MLeader leader)
    {
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (leaderIndexes.Length == 0)
        {
            throw new InvalidOperationException("MLeader has no leaders.");
        }

        var lineIndexes = leader.GetLeaderLineIndexes(leaderIndexes[0]).Cast<int>().ToArray();
        if (lineIndexes.Length == 0)
        {
            throw new InvalidOperationException("MLeader has no leader lines.");
        }

        return lineIndexes[0];
    }

    private static Vector3d? TryReadDogleg(MLeader leader)
    {
        try
        {
            var indexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            return indexes.Length == 0 ? null : leader.GetDogleg(indexes[0]);
        }
        catch (AcadException)
        {
            return null;
        }
    }

    /// <summary>
    /// Effective block-local +X for AttrDef space. CREATE keeps
    /// <c>BlockRotation = 0</c> after TransformBy; AttrDef rotations stay 0.
    /// Classifiers still accept AttrRef.Rotation when BlockRotation is stale 0.
    /// </summary>
    private static bool TryResolveEffectiveBlockLocalXAxis(
        Transaction transaction,
        MLeader leader,
        out double axisX,
        out double axisY)
    {
        axisX = 0d;
        axisY = 0d;
        double? itemAttrRotation = null;
        var blockId = leader.BlockContentId;
        if (!blockId.IsNull &&
            transaction.GetObject(blockId, OpenMode.ForRead, true) is
                BlockTableRecord block &&
            !block.IsErased)
        {
            foreach (ObjectId id in block)
            {
                if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                        AttributeDefinition definition ||
                    definition.IsErased)
                {
                    continue;
                }

                if (!string.Equals(
                        definition.Tag,
                        TimberFramedBlockContentDefinitionRules.ItemNoTag,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var itemAttr = leader.GetBlockAttribute(definition.ObjectId);
                if (itemAttr is not null)
                {
                    itemAttrRotation = itemAttr.Rotation;
                }

                break;
            }
        }

        var axis = TimberFramedBlockContentOrientationRules
            .ResolveEffectiveBlockLocalXAxis(
                leader.BlockRotation,
                itemAttrRotation);
        axisX = axis.X;
        axisY = axis.Y;
        return !(double.IsNaN(axisX) ||
                 double.IsInfinity(axisX) ||
                 double.IsNaN(axisY) ||
                 double.IsInfinity(axisY));
    }

    private static TimberPlanarPoint ToPlanar(Point3d point) =>
        new(point.X, point.Y);

    private static string FormatPoint(Point3d point) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"({point.X:R},{point.Y:R},{point.Z:R})");
}
