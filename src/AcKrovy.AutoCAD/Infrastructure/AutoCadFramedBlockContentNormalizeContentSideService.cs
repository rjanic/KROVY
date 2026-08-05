#if DEBUG
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
/// DEBUG-only: swap Combined BlockContent to the mirrored R2 dimension-column
/// variant when WIDTH/HEIGHT lie on the wrong side of the frame relative to the
/// knee. Preserves the same MLeader handle, attachment, knee, BlockPosition,
/// DoglegDirection/Length, BlockScale/Rotation. Does not mutate shared BTRs,
/// rewrite AttrRef Position/AlignmentPoint, or run dogleg normalize.
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
            "WIDTH/HEIGHT are not toward the knee. Attachment/knee/BP preserved.");

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
            ApplyNormalizeInTransaction(editor, database, transaction, leader);
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

    private static void ApplyNormalizeInTransaction(
        Editor editor,
        Database database,
        Transaction transaction,
        MLeader leader)
    {
        var handle = leader.ObjectId.Handle.ToString();
        var beforeAttachment = ReadAttachment(leader);
        var beforeKnee = ReadKnee(leader);
        var beforeBlockPosition = leader.BlockPosition;
        var beforeDoglegLength = leader.DoglegLength;
        var beforeDogleg = TryReadDogleg(leader);
        var beforeScale = leader.BlockScale;
        var beforeRotation = leader.BlockRotation;
        var beforeBlockId = leader.BlockContentId;

        if (!TryReadCombinedContext(
                database,
                transaction,
                leader,
                out var context,
                out var contextNote))
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
            return;
        }

        var requiredSide = context.RequiredColumnSide;
        var currentSide = context.CurrentColumnSide;
        editor.WriteMessage(
            $"\nhandle={handle} requiredSide={requiredSide} " +
            $"currentSide={currentSide} " +
            $"contentLocalX={Format(context.ContentDirectionLocalX)} " +
            $"widthLocalX={Format(context.WidthLocalX)}");

        if (currentSide == requiredSide)
        {
            editor.WriteMessage($"\n{CommandBanner}: changed=False (already correct).");
            WriteSummary(
                editor,
                handle,
                changed: false,
                beforeBlockId,
                beforeBlockId,
                currentSide,
                requiredSide,
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
            return;
        }

        var ensureRequest = context.BuildEnsureRequest(requiredSide);
        var ensure = AcKrovyFramedBlockContentDefinitionService.Ensure(
            database,
            transaction,
            ensureRequest);
        if (!ensure.Succeeded ||
            ensure.BlockTableRecordId is not ObjectId targetBlockId ||
            targetBlockId.IsNull)
        {
            editor.WriteMessage(
                $"\nhandle={handle}: FAIL Ensure opposite BTR: {ensure.DiagnosticReason}");
            WriteSummary(
                editor,
                handle,
                changed: false,
                beforeBlockId,
                beforeBlockId,
                currentSide,
                requiredSide,
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
            return;
        }

        if (targetBlockId == beforeBlockId)
        {
            editor.WriteMessage(
                $"\nhandle={handle}: FAIL opposite Ensure returned same BlockContentId " +
                $"(name={ensure.ResolvedBlockName}).");
            return;
        }

        leader.BlockContentId = targetBlockId;
        ReapplyAttributes(transaction, leader, targetBlockId, context.AttributeValues);

        // Reaffirm ModelSpace geometry — never invent new landing/dogleg.
        leader.BlockScale = beforeScale;
        leader.BlockRotation = beforeRotation;
        leader.DoglegLength = beforeDoglegLength;
        if (beforeDogleg is Vector3d dogleg)
        {
            var indexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            if (indexes.Length > 0)
            {
                leader.SetDogleg(indexes[0], dogleg);
            }
        }

        leader.BlockPosition = beforeBlockPosition;
        var lineIndex = GetPrimaryLeaderLineIndex(leader);
        leader.SetFirstVertex(lineIndex, beforeAttachment);
        leader.SetLastVertex(lineIndex, beforeKnee);
        leader.BlockPosition = beforeBlockPosition;
        if (beforeDogleg is Vector3d doglegAgain)
        {
            var indexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            if (indexes.Length > 0)
            {
                leader.SetDogleg(indexes[0], doglegAgain);
            }
        }

        var afterAttachment = ReadAttachment(leader);
        var afterKnee = ReadKnee(leader);
        var afterBlockPosition = leader.BlockPosition;
        var afterDogleg = TryReadDogleg(leader);
        editor.WriteMessage(
            $"\n{CommandBanner}: changed=True swapped BlockContentId " +
            $"{beforeBlockId} -> {targetBlockId} " +
            $"name={ensure.ResolvedBlockName}");
        WriteSummary(
            editor,
            handle,
            changed: true,
            beforeBlockId,
            targetBlockId,
            currentSide,
            requiredSide,
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

        if (TryReadCombinedContext(
                leader.Database,
                transaction,
                leader,
                out var context,
                out _))
        {
            editor.WriteMessage(
                $"\npersisted requiredSide={context.RequiredColumnSide} " +
                $"currentSide={context.CurrentColumnSide} " +
                $"match={context.CurrentColumnSide == context.RequiredColumnSide}");
            foreach (var (tag, text, height) in context.AttributeValues)
            {
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
            $"currentSide={currentSide?.ToString() ?? "n/a"} " +
            $"requiredSide={requiredSide?.ToString() ?? "n/a"}");
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
        var blockId = leader.BlockContentId;
        if (blockId.IsNull || !AutoCadDatabaseIdentity.IsSame(database, blockId))
        {
            note = "BlockContentId missing or database mismatch.";
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

        if (!TimberFramedBlockContentDefinitionRules.TryClassifyDimensionColumnSide(
                widthDef.AlignmentPoint.X,
                out var currentSide))
        {
            note = "WIDTH AttrDef AlignmentPoint.X is ambiguous (near zero).";
            return false;
        }

        var kneeLocal = WorldToBlockLocal(
            ReadKnee(leader),
            leader.BlockPosition,
            leader.BlockScale,
            leader.BlockRotation,
            leader.Normal);
        // BlockPosition is block origin; content vector BP−knee is −kneeLocal.
        var contentDirectionLocalX = -kneeLocal.X;
        TimberFramedBlockContentDimensionColumnSide requiredSide;
        try
        {
            requiredSide = TimberFramedBlockContentDefinitionRules
                .ResolveDimensionColumnSideFromContentLocalX(contentDirectionLocalX);
        }
        catch (ArgumentOutOfRangeException)
        {
            note = "Degenerate BlockPosition − knee (content local X ~ 0).";
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
            currentSide,
            requiredSide,
            contentDirectionLocalX,
            widthDef.AlignmentPoint.X,
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

    private static void ReapplyAttributes(
        Transaction transaction,
        MLeader leader,
        ObjectId blockId,
        IReadOnlyList<(string Tag, string Text, double Height)> values)
    {
        var byTag = values.ToDictionary(
            v => v.Tag,
            v => v,
            StringComparer.OrdinalIgnoreCase);
        var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
        foreach (ObjectId id in block)
        {
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

    private static Point3d WorldToBlockLocal(
        Point3d world,
        Point3d blockPosition,
        Scale3d blockScale,
        double blockRotation,
        Vector3d normal)
    {
        var matrix = Matrix3d.Displacement(blockPosition.GetAsVector()) *
            Matrix3d.Rotation(blockRotation, normal, Point3d.Origin) *
            Matrix3d.Scaling(blockScale.X, Point3d.Origin);
        return world.TransformBy(matrix.Inverse());
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
        TimberFramedBlockContentDimensionColumnSide CurrentColumnSide,
        TimberFramedBlockContentDimensionColumnSide RequiredColumnSide,
        double ContentDirectionLocalX,
        double WidthLocalX,
        string ItemTextStyleName,
        string DimensionTextStyleName,
        ObjectId ItemTextStyleId,
        ObjectId DimensionTextStyleId,
        double ItemPaperHeightMm,
        double DimensionPaperHeightMm,
        string ItemTextForFrameSizing,
        IReadOnlyList<(string Tag, string Text, double Height)> AttributeValues)
    {
        public AutoCadFramedBlockContentRequest BuildEnsureRequest(
            TimberFramedBlockContentDimensionColumnSide side) =>
            new(
                ContentKind,
                TimberFramedBlockContentPresentation.Combined,
                ItemTextStyleName,
                DimensionTextStyleName,
                ItemPaperHeightMm,
                DimensionPaperHeightMm,
                ItemTextStyleId,
                DimensionTextStyleId,
                ItemTextForFrameSizing,
                side);
    }
}
#endif
