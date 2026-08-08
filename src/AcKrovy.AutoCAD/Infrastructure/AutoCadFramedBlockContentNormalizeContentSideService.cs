using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Swap Combined BlockContent to the mirrored R2 dimension-column variant when
/// world-space WIDTH/HEIGHT center is not between knee and ITEM_NO (K→D→I).
/// Preserves attachment, knee, BlockPosition, DoglegDirection/Length,
/// BlockScale/Rotation. Does not mutate shared BTRs, rewrite AttrRef
/// Position/AlignmentPoint, or run dogleg normalize.
/// Interactive <see cref="Run"/> remains DEBUG-command owned.
/// </summary>
internal static class AutoCadFramedBlockContentNormalizeContentSideService
{
    private const string CommandBanner = "AK_DEV_FBC_NORMALIZE_CONTENT_SIDE";

    public static void Run()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        editor.WriteMessage($"\n=== {CommandBanner} ===");
        editor.WriteMessage(
            "\nSwap Combined BlockContentId to mirrored R2 column-side BTR when " +
            "world K→D→I is wrong. Attachment/knee/BP preserved.");

        using var documentLock = document.LockDocument();
        var database = document.Database;
        ObjectId leaderId;
        using (var transaction = database.TransactionManager.StartTransaction())
        {
            if (!TryResolveLeader(editor, transaction, out var leader, out var note))
            {
                editor.WriteMessage($"\n{note}");
                transaction.Commit();
                return;
            }

            leaderId = leader.ObjectId;
            leader.UpgradeOpen();
            TryNormalizeContentSide(database, transaction, leader, editor);
            transaction.Commit();
        }

        using (var read = database.TransactionManager.StartTransaction())
        {
            if (read.GetObject(leaderId, OpenMode.ForRead, true) is not MLeader persisted ||
                persisted.IsErased)
            {
                editor.WriteMessage($"\n{CommandBanner}: FAIL reload after commit.");
                read.Commit();
                return;
            }

            PrintPersisted(editor, read, persisted);
            read.Commit();
        }

        editor.WriteMessage(
            $"\n{CommandBanner}: re-run on same handle must report changed=False.");
    }

    /// <summary>
    /// Write-open MLeader overload: uses TopTransaction or a short OpenClose
    /// for related BTR/AttrRef reads only. Never reopens the callback MLeader.
    /// Optional pre-resolved opposite BTR ObjectId avoids mid-drag Ensure when
    /// SETUP already captured both DIMNX/DIMPX scalars.
    /// </summary>
    public static AutoCadFramedBlockContentNormalizeResult TryNormalizeContentSide(
        MLeader writeOpenMLeader,
        Database database,
        ObjectId preferredOppositeBlockContentId = default,
        Editor? editor = null)
    {
        ArgumentNullException.ThrowIfNull(writeOpenMLeader);
        ArgumentNullException.ThrowIfNull(database);

        var top = database.TransactionManager.TopTransaction;
        if (top is not null)
        {
            return TryNormalizeContentSide(
                database,
                top,
                writeOpenMLeader,
                editor,
                preferredOppositeBlockContentId);
        }

        using var openClose = database.TransactionManager.StartOpenCloseTransaction();
        var result = TryNormalizeContentSide(
            database,
            openClose,
            writeOpenMLeader,
            editor,
            preferredOppositeBlockContentId);
        openClose.Commit();
        return result;
    }

    /// <summary>
    /// Reusable content-side normalize for an open writable MLeader. No entity prompt.
    /// Evaluates geometry after any prior dogleg normalize in the same transaction.
    /// Visual authority is world K→D→I only (not BlockRotation / local-X sign).
    /// </summary>
    public static AutoCadFramedBlockContentNormalizeResult TryNormalizeContentSide(
        Database database,
        Transaction transaction,
        MLeader leader,
        Editor? editor = null,
        ObjectId preferredOppositeBlockContentId = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(leader);

        var handle = leader.ObjectId.IsNull
            ? "(null-ObjectId)"
            : leader.ObjectId.Handle.ToString();
        var beforeAttachment = ReadAttachment(leader);
        var beforeKnee = ReadKnee(leader);
        var beforeBlockPosition = leader.BlockPosition;
        var beforeDoglegLength = leader.DoglegLength;
        var beforeDogleg = TryReadDogleg(leader);
        var beforeBlockId = leader.BlockContentId;

        if (beforeBlockId.IsNull)
        {
            return AutoCadFramedBlockContentNormalizeResult.Failed(
                "BlockContentId Null (transient?)",
                beforeBlockId);
        }

        if (!TryReadCombinedContext(
                database,
                transaction,
                leader,
                out var context,
                out var contextNote))
        {
            if (editor is not null)
            {
                editor.WriteMessage($"\nhandle={handle}: FAIL {contextNote}");
                WriteSummary(
                    editor,
                    handle,
                    changed: false,
                    beforeBlockId,
                    beforeBlockId,
                    null,
                    null,
                    beforeAttachment,
                    beforeAttachment,
                    beforeKnee,
                    beforeKnee,
                    beforeBlockPosition,
                    beforeBlockPosition,
                    beforeDogleg,
                    beforeDogleg,
                    beforeDoglegLength,
                    beforeDoglegLength);
            }

            return AutoCadFramedBlockContentNormalizeResult.Failed(
                contextNote,
                beforeBlockId);
        }

        editor?.WriteMessage(
            $"\nhandle={handle} " +
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .FormatEvaluationDiagnostics(context.Points, context.Evaluation));

        if (TimberFramedBlockContentStretchNormalizeRules.IsContentSideNoOp(
                context.Evaluation.Decision))
        {
            editor?.WriteMessage($"\n{CommandBanner}: changed=False (already correct).");
            if (editor is not null)
            {
                WriteSummary(
                    editor,
                    handle,
                    changed: false,
                    beforeBlockId,
                    beforeBlockId,
                    context.Points.ParsedColumnSide,
                    context.Points.ParsedColumnSide,
                    beforeAttachment,
                    beforeAttachment,
                    beforeKnee,
                    beforeKnee,
                    beforeBlockPosition,
                    beforeBlockPosition,
                    beforeDogleg,
                    beforeDogleg,
                    beforeDoglegLength,
                    beforeDoglegLength);
            }

            return AutoCadFramedBlockContentNormalizeResult.NoOp(
                "already correct K→D→I",
                beforeBlockId,
                beforeAttachment,
                beforeKnee,
                beforeBlockPosition);
        }

        if (!AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryCorrectCombinedContentSide(
                    database,
                    transaction,
                    leader,
                    context.ContentKind,
                    context.ItemTextStyleName,
                    context.DimensionTextStyleName,
                    context.ItemTextStyleId,
                    context.DimensionTextStyleId,
                    context.ItemPaperHeightMm,
                    context.DimensionPaperHeightMm,
                    context.ItemTextForFrameSizing,
                    context.AttributeValues,
                    out var changed,
                    out _,
                    out var afterBlockId,
                    out var afterEvaluation,
                    out var correctNote,
                    preferredOppositeBlockContentId))
        {
            editor?.WriteMessage($"\nhandle={handle}: FAIL {correctNote}");
            if (editor is not null)
            {
                WriteSummary(
                    editor,
                    handle,
                    changed: false,
                    beforeBlockId,
                    beforeBlockId,
                    context.Points.ParsedColumnSide,
                    context.Points.ParsedColumnSide is TimberFramedBlockContentDimensionColumnSide side
                        ? TimberFramedBlockContentStretchNormalizeRules.OppositeColumnSide(side)
                        : null,
                    beforeAttachment,
                    beforeAttachment,
                    beforeKnee,
                    beforeKnee,
                    beforeBlockPosition,
                    beforeBlockPosition,
                    beforeDogleg,
                    beforeDogleg,
                    beforeDoglegLength,
                    beforeDoglegLength);
            }

            return AutoCadFramedBlockContentNormalizeResult.Failed(
                correctNote,
                beforeBlockId);
        }

        var afterAttachment = ReadAttachment(leader);
        var afterKnee = ReadKnee(leader);
        var afterBlockPosition = leader.BlockPosition;
        var afterDogleg = TryReadDogleg(leader);

        if (!changed)
        {
            editor?.WriteMessage($"\n{CommandBanner}: changed=False ({correctNote}).");
            if (editor is not null)
            {
                WriteSummary(
                    editor,
                    handle,
                    changed: false,
                    beforeBlockId,
                    beforeBlockId,
                    context.Points.ParsedColumnSide,
                    context.Points.ParsedColumnSide,
                    beforeAttachment,
                    afterAttachment,
                    beforeKnee,
                    afterKnee,
                    beforeBlockPosition,
                    afterBlockPosition,
                    beforeDogleg,
                    afterDogleg,
                    beforeDoglegLength,
                    leader.DoglegLength);
            }

            return AutoCadFramedBlockContentNormalizeResult.NoOp(
                correctNote,
                beforeBlockId,
                beforeAttachment,
                beforeKnee,
                beforeBlockPosition);
        }

        editor?.WriteMessage(
            $"\n{CommandBanner}: changed=True {correctNote} " +
            $"BlockContentId {beforeBlockId} -> {afterBlockId}");
        if (editor is not null &&
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryReadWorldAttributePoints(
                    transaction,
                    leader,
                    out var afterPoints,
                    out _))
        {
            editor.WriteMessage(
                $"\npost-swap " +
                AutoCadFramedBlockContentDimensionColumnPlacementService
                    .FormatEvaluationDiagnostics(afterPoints, afterEvaluation));
        }

        if (editor is not null)
        {
            WriteSummary(
                editor,
                handle,
                changed: true,
                beforeBlockId,
                afterBlockId,
                context.Points.ParsedColumnSide,
                context.Points.ParsedColumnSide is TimberFramedBlockContentDimensionColumnSide parsed
                    ? TimberFramedBlockContentStretchNormalizeRules.OppositeColumnSide(parsed)
                    : null,
                beforeAttachment,
                afterAttachment,
                beforeKnee,
                afterKnee,
                beforeBlockPosition,
                afterBlockPosition,
                beforeDogleg,
                afterDogleg,
                beforeDoglegLength,
                leader.DoglegLength);
        }

        return new AutoCadFramedBlockContentNormalizeResult(
            applied: true,
            changed: true,
            correctNote,
            beforeBlockId,
            afterBlockId,
            beforeAttachment.DistanceTo(afterAttachment),
            beforeKnee.DistanceTo(afterKnee),
            beforeBlockPosition.DistanceTo(afterBlockPosition));
    }

    private static void PrintPersisted(
        Editor editor,
        Transaction transaction,
        MLeader leader)
    {
        var handle = leader.ObjectId.Handle.ToString();
        editor.WriteMessage($"\nphase=PERSISTED_AFTER handle={handle}");
        editor.WriteMessage($"\nBlockContentId={leader.BlockContentId}");
        editor.WriteMessage($"\nattachment={FormatPoint(ReadAttachment(leader))}");
        editor.WriteMessage($"\nknee={FormatPoint(ReadKnee(leader))}");
        editor.WriteMessage($"\nBlockPosition={FormatPoint(leader.BlockPosition)}");
        editor.WriteMessage($"\nDoglegDirection={FormatVector(TryReadDogleg(leader))}");
        editor.WriteMessage($"\nDoglegLength={Format(leader.DoglegLength)}");
        editor.WriteMessage($"\nBlockScale={Format(leader.BlockScale.X)}");
        editor.WriteMessage($"\nBlockRotation={Format(leader.BlockRotation)}");

        if (AutoCadFramedBlockContentDimensionColumnPlacementService.TryEvaluate(
                transaction,
                leader,
                out var evaluation,
                out var points,
                out _))
        {
            editor.WriteMessage(
                $"\npersisted " +
                AutoCadFramedBlockContentDimensionColumnPlacementService
                    .FormatEvaluationDiagnostics(points, evaluation));
            foreach (var tag in new[]
                     {
                         TimberFramedBlockContentDefinitionRules.ItemNoTag,
                         TimberFramedBlockContentDefinitionRules.WidthTag,
                         TimberFramedBlockContentDefinitionRules.HeightTag,
                     })
            {
                if (!TryReadAttrTextHeight(transaction, leader, tag, out var text, out var height))
                {
                    continue;
                }

                editor.WriteMessage(
                    $"\nAttrRef {tag} text='{text}' height={Format(height)}");
            }
        }
    }

    private static void WriteSummary(
        Editor editor,
        string handle,
        bool changed,
        ObjectId beforeBlockId,
        ObjectId afterBlockId,
        TimberFramedBlockContentDimensionColumnSide? currentSide,
        TimberFramedBlockContentDimensionColumnSide? requiredSide,
        Point3d beforeAttachment,
        Point3d afterAttachment,
        Point3d beforeKnee,
        Point3d afterKnee,
        Point3d beforeBlockPosition,
        Point3d afterBlockPosition,
        Vector3d? beforeDogleg,
        Vector3d? afterDogleg,
        double beforeDoglegLength,
        double afterDoglegLength)
    {
        editor.WriteMessage(
            $"\nSUMMARY handle={handle} changed={changed} " +
            $"parsedCurrentSide={currentSide?.ToString() ?? "n/a"} " +
            $"parsedTargetSide={requiredSide?.ToString() ?? "n/a"}");
        editor.WriteMessage(
            $"\nBlockContentId before={beforeBlockId} after={afterBlockId}");
        editor.WriteMessage(
            $"\nattachmentDrift={Format(beforeAttachment.DistanceTo(afterAttachment))} " +
            $"kneeDrift={Format(beforeKnee.DistanceTo(afterKnee))} " +
            $"bpDrift={Format(beforeBlockPosition.DistanceTo(afterBlockPosition))}");
        editor.WriteMessage(
            $"\nDoglegDirection before={FormatVector(beforeDogleg)} " +
            $"after={FormatVector(afterDogleg)} " +
            $"DoglegLength before={Format(beforeDoglegLength)} " +
            $"after={Format(afterDoglegLength)}");
    }

    private static bool TryReadCombinedContext(
        Database database,
        Transaction transaction,
        MLeader leader,
        out ContentSideContext context,
        out string note)
    {
        context = default!;
        note = string.Empty;
        _ = database;

        if (!AutoCadFramedBlockContentDimensionColumnPlacementService.TryEvaluate(
                transaction,
                leader,
                out var evaluation,
                out var points,
                out note))
        {
            return false;
        }

        if (points.ParsedColumnSide is null)
        {
            note = "Combined BTR name must parse as R2 DIMNX/DIMPX.";
            return false;
        }

        var blockId = leader.BlockContentId;
        if (blockId.IsNull)
        {
            note = "BlockContentId Null.";
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
        Entity? frame = null;
        foreach (ObjectId id in block)
        {
            if (id.IsNull)
            {
                continue;
            }

            if (transaction.GetObject(id, OpenMode.ForRead, true) is not Entity entity ||
                entity.IsErased)
            {
                continue;
            }

            if (entity is AttributeDefinition attribute)
            {
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
            else
            {
                frame = entity;
            }
        }

        if (itemDef is null || widthDef is null || heightDef is null)
        {
            note = "Combined BTR must expose ITEM_NO/WIDTH/HEIGHT AttrDefs.";
            return false;
        }

        if (!TryResolveContentKind(frame, out var contentKind, out var frameNote))
        {
            note = frameNote;
            return false;
        }

        var itemStyle = (TextStyleTableRecord)transaction.GetObject(
            itemDef.TextStyleId,
            OpenMode.ForRead);
        var dimStyle = (TextStyleTableRecord)transaction.GetObject(
            widthDef.TextStyleId,
            OpenMode.ForRead);
        var itemStyleName = string.IsNullOrWhiteSpace(itemStyle.Name)
            ? "Standard"
            : itemStyle.Name;
        var dimStyleName = string.IsNullOrWhiteSpace(dimStyle.Name)
            ? "Standard"
            : dimStyle.Name;

        var itemPaper =
            itemDef.Height /
            TimberFramedBlockContentDefinitionRules.BaselineDenominator;
        var dimPaper =
            widthDef.Height /
            TimberFramedBlockContentDefinitionRules.BaselineDenominator;

        var values = new List<(string Tag, string Text, double Height)>();
        foreach (var definition in new[] { itemDef, widthDef, heightDef })
        {
            using var attribute = leader.GetBlockAttribute(definition.ObjectId);
            if (attribute is null)
            {
                note = $"Missing AttrRef for {definition.Tag}.";
                return false;
            }

            values.Add((
                definition.Tag.ToUpperInvariant(),
                attribute.TextString ?? string.Empty,
                attribute.Height));
        }

        var itemText = values
            .First(v => v.Tag == TimberFramedBlockContentDefinitionRules.ItemNoTag)
            .Text;

        context = new ContentSideContext(
            contentKind,
            evaluation,
            points,
            itemStyleName,
            dimStyleName,
            itemDef.TextStyleId,
            widthDef.TextStyleId,
            itemPaper,
            dimPaper,
            itemText,
            values);
        return true;
    }

    private static bool TryResolveContentKind(
        Entity? frame,
        out TimberFramedBlockContentKind kind,
        out string note)
    {
        kind = default;
        note = string.Empty;
        if (frame is null)
        {
            note = "Combined BTR missing frame/connection entity.";
            return false;
        }

        if (frame is DBPoint)
        {
            kind = TimberFramedBlockContentKind.Plain;
            return true;
        }

        if (frame is Circle)
        {
            kind = TimberFramedBlockContentKind.Circle;
            return true;
        }

        if (frame is Polyline polyline && polyline.Closed && polyline.NumberOfVertices == 4)
        {
            var hasBulge = Enumerable.Range(0, 4).Any(i =>
                Math.Abs(polyline.GetBulgeAt(i)) >
                TimberFramedBlockContentDefinitionRules.AttributeTolerance);
            kind = hasBulge
                ? TimberFramedBlockContentKind.Slot
                : TimberFramedBlockContentKind.Rectangle;
            return true;
        }

        note = "Unable to classify Combined frame geometry kind.";
        return false;
    }

    private static bool TryReadAttrTextHeight(
        Transaction transaction,
        MLeader leader,
        string tag,
        out string text,
        out double height)
    {
        text = string.Empty;
        height = double.NaN;
        var block = (BlockTableRecord)transaction.GetObject(
            leader.BlockContentId,
            OpenMode.ForRead);
        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                    AttributeDefinition definition ||
                definition.IsErased ||
                !string.Equals(definition.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var attribute = leader.GetBlockAttribute(definition.ObjectId);
            if (attribute is null)
            {
                return false;
            }

            text = attribute.TextString ?? string.Empty;
            height = attribute.Height;
            return true;
        }

        return false;
    }

    private static bool TryResolveLeader(
        Editor editor,
        Transaction transaction,
        out MLeader leader,
        out string note)
    {
        leader = null!;
        note = string.Empty;
        var options = new PromptEntityOptions(
            "\nSelect Combined BlockContent MLeader: ");
        options.SetRejectMessage("\nMust select an MLeader.");
        options.AddAllowedClass(typeof(MLeader), exactMatch: false);
        var result = editor.GetEntity(options);
        if (result.Status != PromptStatus.OK)
        {
            note = "Selection cancelled.";
            return false;
        }

        if (transaction.GetObject(result.ObjectId, OpenMode.ForRead, true) is not
                MLeader selected ||
            selected.IsErased)
        {
            note = "Selected entity is not an available MLeader.";
            return false;
        }

        if (selected.ContentType != ContentType.BlockContent ||
            selected.BlockContentId.IsNull)
        {
            note = "MLeader must be BlockContent with a BlockContentId.";
            return false;
        }

        leader = selected;
        return true;
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

    private static string FormatPoint(Point3d point) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"({point.X:R},{point.Y:R},{point.Z:R})");

    private static string FormatVector(Vector3d? vector) =>
        vector is Vector3d v
            ? string.Create(CultureInfo.InvariantCulture, $"({v.X:R},{v.Y:R},{v.Z:R})")
            : "(unavailable)";

    private static string Format(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private sealed record ContentSideContext(
        TimberFramedBlockContentKind ContentKind,
        TimberFramedBlockContentDimensionColumnMirrorEvaluation Evaluation,
        AutoCadFramedBlockContentDimensionColumnPlacementService.WorldAttributePoints Points,
        string ItemTextStyleName,
        string DimensionTextStyleName,
        ObjectId ItemTextStyleId,
        ObjectId DimensionTextStyleId,
        double ItemPaperHeightMm,
        double DimensionPaperHeightMm,
        string ItemTextForFrameSizing,
        IReadOnlyList<(string Tag, string Text, double Height)> AttributeValues);
}
