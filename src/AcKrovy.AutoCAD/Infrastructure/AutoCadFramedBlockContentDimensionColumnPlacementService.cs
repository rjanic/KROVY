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
        if (TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
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

    private static TimberPlanarPoint ToPlanar(Point3d point) =>
        new(point.X, point.Y);

    private static string FormatPoint(Point3d point) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"({point.X:R},{point.Y:R},{point.Z:R})");
}
