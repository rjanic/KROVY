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
/// DEBUG-only: normalize Combined MLeader dogleg when landing points toward
/// the attachment. DoglegDirection comes from BlockPosition − knee
/// (ConnectBase), never LeaderKneeSide PositiveT/NegativeT → DoglegDirection.
/// Does not swap BlockContentId, mutate BTR/AttrDefs, or rewrite AttrRef
/// Position/AlignmentPoint.
/// </summary>
internal static class AutoCadFramedBlockContentNormalizeDoglegService
{
    private const string CommandBanner = "AK_DEV_FBC_NORMALIZE_DOGLEG";

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
            "\nDoglegDirection from BlockPosition−knee / ConnectBase. " +
            "LeaderKneeSide PositiveT/NegativeT → DoglegDirection cancelled. " +
            "Attachment/knee preserved.");

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
            var applied = ApplyNormalizeInTransaction(
                editor,
                transaction,
                leader,
                writePhase: "WRITE");
            transaction.Commit();
            editor.WriteMessage(
                applied
                    ? $"\n{CommandBanner}: committed normalize."
                    : $"\n{CommandBanner}: committed no-op (already consistent).");
        }

        // Reload in a fresh read transaction — persisted AFTER, not stale write snapshot.
        using (var read = database.TransactionManager.StartTransaction())
        {
            if (read.GetObject(leaderId, OpenMode.ForRead, true) is not MLeader persisted ||
                persisted.IsErased)
            {
                editor.WriteMessage($"\n{CommandBanner}: FAIL reload after commit.");
                read.Commit();
                return;
            }

            PrintNativeAnalysis(editor, read, persisted, phase: "PERSISTED_AFTER");
            read.Commit();
        }

        editor.WriteMessage(
            $"\n{CommandBanner}: re-run on same handle must report changed=False.");
    }

    private static bool ApplyNormalizeInTransaction(
        Editor editor,
        Transaction transaction,
        MLeader leader,
        string writePhase)
    {
        var handle = leader.ObjectId.Handle.ToString();
        var blockContentId = leader.BlockContentId;
        var blockScale = leader.BlockScale;
        var blockRotation = leader.BlockRotation;

        var beforeAttachment = ReadAttachment(leader);
        var beforeKnee = ReadKnee(leader);
        var beforeBlockPosition = leader.BlockPosition;
        var beforeDoglegLength = leader.DoglegLength;
        var beforeDogleg = TryReadDogleg(leader);
        var beforeAttrLocals = CaptureAttrRefLocals(transaction, leader);

        PrintNativeAnalysis(editor, transaction, leader, phase: "BEFORE");

        if (!TimberFramedBlockContentDoglegRules.TryNormalizeDoglegGeometry(
                new TimberPlanarPoint(beforeAttachment.X, beforeAttachment.Y),
                new TimberPlanarPoint(beforeKnee.X, beforeKnee.Y),
                new TimberPlanarPoint(beforeBlockPosition.X, beforeBlockPosition.Y),
                out var doglegDirection,
                out var normalizedBlockPosition,
                out var mirrored))
        {
            editor.WriteMessage(
                $"\nhandle={handle}: FAIL degenerate BlockPosition−knee.");
            PrintSummary(
                editor,
                handle,
                leaderKneeSide: "n/a",
                contentDoglegSide: "n/a",
                beforeDogleg,
                beforeDogleg,
                beforeDoglegLength,
                beforeDoglegLength,
                beforeBlockPosition,
                beforeBlockPosition,
                beforeAttachment,
                beforeAttachment,
                beforeKnee,
                beforeKnee,
                beforeAttrLocals,
                beforeAttrLocals,
                changed: false);
            return false;
        }

        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (leaderIndexes.Length == 0)
        {
            editor.WriteMessage($"\nhandle={handle}: FAIL no leader indexes.");
            return false;
        }

        var lineIndex = GetPrimaryLeaderLineIndex(leader);
        var direction = new Vector3d(doglegDirection.X, doglegDirection.Y, 0d);
        var length = beforeDoglegLength > 1e-9d
            ? beforeDoglegLength
            : beforeKnee.DistanceTo(beforeBlockPosition);

        var directionAlreadyMatches = DoglegEquals(beforeDogleg, direction);
        if (!mirrored && directionAlreadyMatches)
        {
            // Pure no-op: do not touch BlockPosition (ConnectBase offset must stay).
            var tangentNoOp = ResolveStableTangent(
                beforeDogleg,
                beforeBlockPosition,
                beforeKnee);
            TryFormatSide(
                TimberFramedBlockContentDoglegRules.TryResolveLeaderKneeSide(
                    new TimberPlanarPoint(beforeAttachment.X, beforeAttachment.Y),
                    new TimberPlanarPoint(beforeKnee.X, beforeKnee.Y),
                    tangentNoOp,
                    out var kneeSideNoOp),
                kneeSideNoOp,
                out var kneeSideTextNoOp);
            TryFormatSide(
                TimberFramedBlockContentDoglegRules.TryResolveContentDoglegSide(
                    new TimberPlanarPoint(beforeKnee.X, beforeKnee.Y),
                    new TimberPlanarPoint(beforeBlockPosition.X, beforeBlockPosition.Y),
                    tangentNoOp,
                    out var contentSideNoOp),
                contentSideNoOp,
                out var contentSideTextNoOp);

            editor.WriteMessage($"\nphase={writePhase}");
            PrintSummary(
                editor,
                handle,
                kneeSideTextNoOp,
                contentSideTextNoOp,
                beforeDogleg,
                beforeDogleg,
                beforeDoglegLength,
                beforeDoglegLength,
                beforeBlockPosition,
                beforeBlockPosition,
                beforeAttachment,
                beforeAttachment,
                beforeKnee,
                beforeKnee,
                beforeAttrLocals,
                beforeAttrLocals,
                changed: false);
            return false;
        }

        // API order (variant B):
        // 1) SetDogleg / DoglegLength without inventing BlockPosition from length
        // 2) If mirror required, set BlockPosition then reassert knee/attachment
        leader.DoglegLength = length;
        leader.SetDogleg(leaderIndexes[0], direction);

        if (mirrored)
        {
            leader.BlockPosition = new Point3d(
                normalizedBlockPosition.X,
                normalizedBlockPosition.Y,
                beforeBlockPosition.Z);
            leader.SetLastVertex(lineIndex, beforeKnee);
            leader.SetFirstVertex(lineIndex, beforeAttachment);
            leader.SetDogleg(leaderIndexes[0], direction);
            leader.DoglegLength = length;
        }

        leader.BlockConnectionType = BlockConnectionType.ConnectBase;

        if (leader.BlockContentId != blockContentId)
        {
            leader.BlockContentId = blockContentId;
        }

        leader.BlockScale = blockScale;
        leader.BlockRotation = blockRotation;

        var afterAttachment = ReadAttachment(leader);
        var afterKnee = ReadKnee(leader);
        var afterBlockPosition = leader.BlockPosition;
        var afterDoglegLength = leader.DoglegLength;
        var afterDogleg = TryReadDogleg(leader);
        var afterAttrLocals = CaptureAttrRefLocals(transaction, leader);

        var tangent = ResolveStableTangent(beforeDogleg, beforeBlockPosition, beforeKnee);
        TryFormatSide(
            TimberFramedBlockContentDoglegRules.TryResolveLeaderKneeSide(
                new TimberPlanarPoint(beforeAttachment.X, beforeAttachment.Y),
                new TimberPlanarPoint(beforeKnee.X, beforeKnee.Y),
                tangent,
                out var kneeSide),
            kneeSide,
            out var kneeSideText);
        TryFormatSide(
            TimberFramedBlockContentDoglegRules.TryResolveContentDoglegSide(
                new TimberPlanarPoint(beforeKnee.X, beforeKnee.Y),
                new TimberPlanarPoint(beforeBlockPosition.X, beforeBlockPosition.Y),
                tangent,
                out var contentSide),
            contentSide,
            out var contentSideText);

        var changed =
            mirrored ||
            beforeBlockPosition.DistanceTo(afterBlockPosition) > 1e-6d ||
            Math.Abs(beforeDoglegLength - afterDoglegLength) > 1e-6d ||
            !DoglegEquals(beforeDogleg, afterDogleg);

        editor.WriteMessage($"\nphase={writePhase}");
        PrintSummary(
            editor,
            handle,
            kneeSideText,
            contentSideText,
            beforeDogleg,
            afterDogleg,
            beforeDoglegLength,
            afterDoglegLength,
            beforeBlockPosition,
            afterBlockPosition,
            beforeAttachment,
            afterAttachment,
            beforeKnee,
            afterKnee,
            beforeAttrLocals,
            afterAttrLocals,
            changed);

        return changed;
    }

    private static void PrintNativeAnalysis(
        Editor editor,
        Transaction transaction,
        MLeader leader,
        string phase)
    {
        var attachment = ReadAttachment(leader);
        var knee = ReadKnee(leader);
        var blockPosition = leader.BlockPosition;
        var dogleg = TryReadDogleg(leader);
        var length = leader.DoglegLength;
        var landing = blockPosition - knee;
        var tangent = ResolveStableTangent(dogleg, blockPosition, knee);

        TryFormatSide(
            TimberFramedBlockContentDoglegRules.TryResolveLeaderKneeSide(
                new TimberPlanarPoint(attachment.X, attachment.Y),
                new TimberPlanarPoint(knee.X, knee.Y),
                tangent,
                out var kneeSide),
            kneeSide,
            out var kneeSideText);
        TryFormatSide(
            TimberFramedBlockContentDoglegRules.TryResolveContentDoglegSide(
                new TimberPlanarPoint(knee.X, knee.Y),
                new TimberPlanarPoint(blockPosition.X, blockPosition.Y),
                tangent,
                out var contentSide),
            contentSide,
            out var contentSideText);

        var toward = TimberFramedBlockContentDoglegRules.LandingPointsTowardAttachment(
            new TimberPlanarPoint(attachment.X, attachment.Y),
            new TimberPlanarPoint(knee.X, knee.Y),
            new TimberPlanarPoint(blockPosition.X, blockPosition.Y));
        var connectOffset =
            TimberFramedBlockContentDoglegRules.MeasureConnectBaseContentOffsetMm(
                new TimberPlanarPoint(knee.X, knee.Y),
                new TimberPlanarPoint(blockPosition.X, blockPosition.Y),
                length);
        var kneeAttDotT = Dot(
            knee - attachment,
            new Vector3d(tangent.X, tangent.Y, 0d));
        var bpKneeDotT = Dot(
            blockPosition - knee,
            new Vector3d(tangent.X, tangent.Y, 0d));

        editor.WriteMessage($"\n--- native analysis ({phase}) ---");
        editor.WriteMessage($"\nhandle={leader.ObjectId.Handle}");
        editor.WriteMessage($"\nattachment={FormatPoint(attachment)}");
        editor.WriteMessage($"\nknee={FormatPoint(knee)}");
        editor.WriteMessage($"\nBlockPosition={FormatPoint(blockPosition)}");
        editor.WriteMessage($"\nlocalTangent T={FormatVector(new Vector3d(tangent.X, tangent.Y, 0d))}");
        editor.WriteMessage($"\ndot(knee−attachment, T)={Format(kneeAttDotT)}");
        editor.WriteMessage($"\ndot(BlockPosition−knee, T)={Format(bpKneeDotT)}");
        editor.WriteMessage(
            $"\nnormalized(BlockPosition−knee)={FormatVector(landing.Length > 1e-9d ? landing.GetNormal() : null)}");
        editor.WriteMessage($"\nDoglegDirection={FormatVector(dogleg)}");
        editor.WriteMessage($"\nDoglegLength={Format(length)}");
        editor.WriteMessage($"\nConnectBase={leader.BlockConnectionType}");
        editor.WriteMessage(
            $"\ndistance knee→BlockPosition={Format(knee.DistanceTo(blockPosition))}");
        editor.WriteMessage(
            $"\nConnectBase content offset (|BP−knee|−DoglegLength)={Format(connectOffset)}");
        editor.WriteMessage($"\nLeaderKneeSide={kneeSideText}");
        editor.WriteMessage($"\nContentDoglegSide={contentSideText}");
        editor.WriteMessage(
            $"\nLeaderKneeWorldX={FormatWorldXSide(knee.X - attachment.X)} " +
            $"(diag only; not used for normalize)");
        editor.WriteMessage(
            $"\nContentDoglegWorldX={FormatWorldXSide(blockPosition.X - knee.X)} " +
            $"(diag only; not used for normalize)");
        editor.WriteMessage($"\nlandingPointsTowardAttachment={toward}");
        editor.WriteMessage($"\nBlockScale={leader.BlockScale}");
        _ = transaction;
    }

    private static void PrintSummary(
        Editor editor,
        string handle,
        string leaderKneeSide,
        string contentDoglegSide,
        Vector3d? oldDogleg,
        Vector3d? newDogleg,
        double oldLength,
        double newLength,
        Point3d oldBlockPosition,
        Point3d newBlockPosition,
        Point3d oldAttachment,
        Point3d newAttachment,
        Point3d oldKnee,
        Point3d newKnee,
        IReadOnlyList<(string Tag, Point3d LocalPos, Point3d LocalAlign)> beforeAttrs,
        IReadOnlyList<(string Tag, Point3d LocalPos, Point3d LocalAlign)> afterAttrs,
        bool changed)
    {
        editor.WriteMessage($"\nhandle={handle}");
        editor.WriteMessage($"\nLeaderKneeSide={leaderKneeSide}");
        editor.WriteMessage($"\nContentDoglegSide={contentDoglegSide}");
        editor.WriteMessage(
            $"\nold DoglegDirection={FormatVector(oldDogleg)} " +
            $"new={FormatVector(newDogleg)}");
        editor.WriteMessage(
            $"\nold DoglegLength={Format(oldLength)} new={Format(newLength)}");
        editor.WriteMessage(
            $"\nold BlockPosition={FormatPoint(oldBlockPosition)} " +
            $"new={FormatPoint(newBlockPosition)}");
        editor.WriteMessage(
            $"\nattachment drift={Format(oldAttachment.DistanceTo(newAttachment))} " +
            $"knee drift={Format(oldKnee.DistanceTo(newKnee))}");
        editor.WriteMessage(
            $"\nBlockPosition drift={Format(oldBlockPosition.DistanceTo(newBlockPosition))}");
        editor.WriteMessage($"\nchanged={changed}");

        var afterByTag = afterAttrs.ToDictionary(
            x => x.Tag,
            StringComparer.OrdinalIgnoreCase);
        foreach (var before in beforeAttrs)
        {
            if (!afterByTag.TryGetValue(before.Tag, out var after))
            {
                editor.WriteMessage($"\nAttrRef {before.Tag}: missing after normalize");
                continue;
            }

            var posDrift = before.LocalPos.DistanceTo(after.LocalPos);
            var alignDrift = before.LocalAlign.DistanceTo(after.LocalAlign);
            editor.WriteMessage(
                $"\nAttrRef {before.Tag} localPosDrift={Format(posDrift)} " +
                $"localAlignDrift={Format(alignDrift)}");
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

    /// <summary>
    /// Stable annotation T for side labels: prefer DoglegDirection axis, else
    /// BlockPosition − knee, else +X. Not used to invent DoglegDirection.
    /// </summary>
    private static TimberPlanarVector ResolveStableTangent(
        Vector3d? dogleg,
        Point3d blockPosition,
        Point3d knee)
    {
        if (dogleg is Vector3d d && d.Length > 1e-9d)
        {
            var n = d.GetNormal();
            return new TimberPlanarVector(n.X, n.Y);
        }

        var landing = blockPosition - knee;
        if (landing.Length > 1e-9d)
        {
            var n = landing.GetNormal();
            return new TimberPlanarVector(n.X, n.Y);
        }

        return new TimberPlanarVector(1d, 0d);
    }

    private static void TryFormatSide(
        bool resolved,
        TimberLeaderTangentSign side,
        out string text) =>
        text = resolved ? side.ToString() : "Ambiguous";

    /// <summary>
    /// Diag-only world-X label. Never drives normalize / DoglegDirection.
    /// </summary>
    private static string FormatWorldXSide(double deltaX) =>
        Math.Abs(deltaX) <= 1e-9d
            ? "Ambiguous"
            : deltaX < 0d
                ? "WorldLeft"
                : "WorldRight";

    private static double Dot(Vector3d a, Vector3d b) =>
        (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static IReadOnlyList<(string Tag, Point3d LocalPos, Point3d LocalAlign)>
        CaptureAttrRefLocals(Transaction transaction, MLeader leader)
    {
        var list = new List<(string, Point3d, Point3d)>();
        var blockId = leader.BlockContentId;
        if (blockId.IsNull)
        {
            return list;
        }

        var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                    AttributeDefinition definition ||
                definition.IsErased)
            {
                continue;
            }

            var tag = definition.Tag.ToUpperInvariant();
            if (tag is not (
                TimberFramedBlockContentDefinitionRules.ItemNoTag or
                TimberFramedBlockContentDefinitionRules.WidthTag or
                TimberFramedBlockContentDefinitionRules.HeightTag))
            {
                continue;
            }

            using var attribute = leader.GetBlockAttribute(definition.ObjectId);
            if (attribute is null)
            {
                continue;
            }

            var localPos = WorldToBlockLocal(
                attribute.Position,
                leader.BlockPosition,
                leader.BlockScale,
                leader.BlockRotation,
                leader.Normal);
            Point3d worldAlign;
            try
            {
                worldAlign = attribute.AlignmentPoint;
            }
            catch (AcadException)
            {
                worldAlign = attribute.Position;
            }

            var localAlign = WorldToBlockLocal(
                worldAlign,
                leader.BlockPosition,
                leader.BlockScale,
                leader.BlockRotation,
                leader.Normal);
            list.Add((tag, localPos, localAlign));
        }

        return list;
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

    private static bool DoglegEquals(Vector3d? left, Vector3d? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Value.IsEqualTo(right.Value, new Tolerance(1e-6d, 1e-6d));
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
}
#endif
