#if DEBUG
using System.Globalization;
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG-only visual oracle. Applies one rigid 180° world transform to an
/// entire production R3 Combined MLeader around its live attachment point.
/// </summary>
public sealed class AutoCadRotateAnnotation180Commands
{
    private const double AttachmentTolerance = 1e-6d;

    [CommandMethod("AK_DEV_ROTATE_ANNOTATION_180", CommandFlags.Modal)]
    public void RotateAnnotation180()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        var selection = editor.GetEntity(
            new PromptEntityOptions(
                "\nSelect one production R3 Combined MLeader annotation: "));
        if (selection.Status != PromptStatus.OK)
        {
            editor.WriteMessage(
                "\nAK_DEV_ROTATE_ANNOTATION_180 FAIL: selection cancelled or empty.");
            return;
        }

        try
        {
            using var documentLock = document.LockDocument();
            using var transaction =
                document.Database.TransactionManager.StartTransaction();
            if (transaction.GetObject(
                    selection.ObjectId,
                    OpenMode.ForRead,
                    openErased: false) is not MLeader leader)
            {
                editor.WriteMessage(
                    "\nAK_DEV_ROTATE_ANNOTATION_180 FAIL: selected entity is not an MLeader.");
                return;
            }

            if (!TryValidateProductionR3Combined(
                    transaction,
                    leader,
                    out var metadataBefore,
                    out var validationReason))
            {
                editor.WriteMessage(
                    "\nAK_DEV_ROTATE_ANNOTATION_180 FAIL: " + validationReason);
                return;
            }

            if (!TryCaptureSnapshot(
                    document.Database,
                    transaction,
                    leader,
                    metadataBefore,
                    out var before,
                    out var beforeReason))
            {
                editor.WriteMessage(
                    "\nAK_DEV_ROTATE_ANNOTATION_180 FAIL: unable to measure BEFORE state: " +
                    beforeReason);
                return;
            }

            if (!AutoCadWholeMLeaderHalfTurnService.TryApplyRigidHalfTurn(
                    transaction,
                    leader,
                    out _,
                    out var transformReason))
            {
                editor.WriteMessage(
                    "\nAK_DEV_ROTATE_ANNOTATION_180 FAIL: " + transformReason);
                return;
            }

            if (!ElementLabelStore.TryRead(leader, out var metadataAfter) ||
                metadataAfter is null ||
                metadataAfter != metadataBefore)
            {
                editor.WriteMessage(
                    "\nAK_DEV_ROTATE_ANNOTATION_180 FAIL: production metadata changed; " +
                    "transaction rolled back.");
                return;
            }

            if (!TryCaptureSnapshot(
                    document.Database,
                    transaction,
                    leader,
                    metadataAfter,
                    out var after,
                    out var afterReason))
            {
                editor.WriteMessage(
                    "\nAK_DEV_ROTATE_ANNOTATION_180 FAIL: unable to measure AFTER state: " +
                    afterReason + "; transaction rolled back.");
                return;
            }

            var attachmentDelta = before.Attachment.DistanceTo(after.Attachment);
            if (attachmentDelta > AttachmentTolerance)
            {
                WriteDiagnostics(editor, before, after, attachmentDelta);
                editor.WriteMessage(
                    "\nResult=FAIL" +
                    "\nReason=Attachment moved beyond tolerance; transaction rolled back." +
                    "\n=== END ===");
                return;
            }

            if (before.BlockContentId != after.BlockContentId ||
                !string.Equals(
                    before.ContentVariant,
                    after.ContentVariant,
                    StringComparison.Ordinal))
            {
                editor.WriteMessage(
                    "\nAK_DEV_ROTATE_ANNOTATION_180 FAIL: content BTR/variant changed; " +
                    "transaction rolled back.");
                return;
            }

            transaction.Commit();
            WriteDiagnostics(editor, before, after, attachmentDelta);
            editor.WriteMessage(
                "\nRotationAppliedDeg=180" +
                "\nResult=PASS" +
                "\n=== END ===");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                "\nAK_DEV_ROTATE_ANNOTATION_180 FAIL: " + exception.Message);
        }
    }

    private static bool TryValidateProductionR3Combined(
        Transaction transaction,
        MLeader leader,
        out ElementLabelData metadata,
        out string reason)
    {
        metadata = null!;
        reason = string.Empty;
        if (leader.IsErased)
        {
            reason = "selected MLeader is erased.";
            return false;
        }

        if (leader.ContentType != ContentType.BlockContent ||
            leader.BlockContentId.IsNull)
        {
            reason = "selected MLeader is not native BlockContent.";
            return false;
        }

        if (!ElementLabelStore.TryRead(leader, out var data) || data is null)
        {
            reason = "production annotation metadata are missing or unreadable.";
            return false;
        }

        if (!AutoCadFramedBlockContentProductionPolicy.IsG5CombinedMetadata(data))
        {
            reason = "metadata do not identify a production G5 Combined annotation.";
            return false;
        }

        if (transaction.GetObject(
                leader.BlockContentId,
                OpenMode.ForRead,
                openErased: false) is not BlockTableRecord block ||
            !TimberFramedBlockContentVariantRules
                .IsProductionR3CombinedContentVariant(block.Name))
        {
            reason = "BlockContent is not a production R3 RIGHT/LEFT Combined variant.";
            return false;
        }

        metadata = data;
        return true;
    }

    private static bool TryCaptureSnapshot(
        Database database,
        Transaction transaction,
        MLeader leader,
        ElementLabelData metadata,
        out AnnotationSnapshot snapshot,
        out string reason)
    {
        snapshot = default!;
        reason = string.Empty;
        if (!TryReadLeaderGeometry(
                leader,
                out var attachment,
                out var knee,
                out var blockPosition,
                out reason))
        {
            return false;
        }

        if (!TryResolveSourcePhysicalAxisDeg(
                database,
                transaction,
                metadata.SourceHandle,
                out var sourcePhysicalAxisDeg,
                out reason))
        {
            return false;
        }

        if (!AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveWorldContentXAxis(
                    transaction,
                    leader,
                    out var frameWorldOrientation,
                    out var frameReason))
        {
            reason = "frame world orientation unavailable: " + frameReason;
            return false;
        }

        if (!TryResolveContentVariant(
                transaction,
                leader,
                out var contentVariant,
                out reason))
        {
            return false;
        }

        if (!TryResolveDimensionsTowardKneeDot(
                transaction,
                leader,
                knee,
                blockPosition,
                out var dimensionsTowardKneeDot,
                out reason))
        {
            return false;
        }

        var landing = blockPosition - knee;
        if (landing.Length <=
            TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            reason = "knee-to-BlockPosition landing is degenerate.";
            return false;
        }

        snapshot = new AnnotationSnapshot(
            Handle: leader.Handle.ToString(),
            SourceHandle: metadata.SourceHandle,
            Attachment: attachment,
            Knee: knee,
            BlockPosition: blockPosition,
            SourcePhysicalAxisAngleDeg: sourcePhysicalAxisDeg,
            LandingWorldAngleDeg: Math.Atan2(landing.Y, landing.X) * 180d / Math.PI,
            FrameWorldOrientationDeg: frameWorldOrientation * 180d / Math.PI,
            BlockRotationDeg: leader.BlockRotation * 180d / Math.PI,
            ContentVariant: contentVariant,
            DimensionsTowardKneeDot: dimensionsTowardKneeDot,
            BlockContentId: leader.BlockContentId);
        return true;
    }

    private static bool TryReadLeaderGeometry(
        MLeader leader,
        out Point3d attachment,
        out Point3d knee,
        out Point3d blockPosition,
        out string reason)
    {
        attachment = Point3d.Origin;
        knee = Point3d.Origin;
        blockPosition = leader.BlockPosition;
        reason = string.Empty;
        try
        {
            var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            if (leaderIndexes.Length != 1)
            {
                reason = "production oracle requires exactly one leader.";
                return false;
            }

            var lineIndexes = leader
                .GetLeaderLineIndexes(leaderIndexes[0])
                .Cast<int>()
                .ToArray();
            if (lineIndexes.Length != 1)
            {
                reason = "production oracle requires exactly one leader line.";
                return false;
            }

            attachment = leader.GetFirstVertex(lineIndexes[0]);
            knee = leader.GetLastVertex(lineIndexes[0]);
            return true;
        }
        catch (System.Exception exception)
        {
            reason = "leader vertices unavailable: " + exception.Message;
            return false;
        }
    }

    private static bool TryResolveSourcePhysicalAxisDeg(
        Database database,
        Transaction transaction,
        string sourceHandle,
        out double sourcePhysicalAxisDeg,
        out string reason)
    {
        sourcePhysicalAxisDeg = double.NaN;
        reason = string.Empty;
        if (!TryParseHandleObjectId(database, sourceHandle, out var sourceId) ||
            transaction.GetObject(
                sourceId,
                OpenMode.ForRead,
                openErased: false) is not Entity source)
        {
            reason = "SourceHandle cannot be resolved to a live source entity.";
            return false;
        }

        Point3d start;
        Point3d end;
        switch (source)
        {
            case Line line:
                start = line.StartPoint;
                end = line.EndPoint;
                break;
            case Polyline polyline:
                start = polyline.StartPoint;
                end = polyline.EndPoint;
                break;
            default:
                reason = "source entity is not a supported Line/Polyline.";
                return false;
        }

        var axis = end - start;
        if (axis.Length <=
            TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            reason = "source physical axis is degenerate.";
            return false;
        }

        sourcePhysicalAxisDeg = Math.Atan2(axis.Y, axis.X) * 180d / Math.PI;
        return true;
    }

    private static bool TryParseHandleObjectId(
        Database database,
        string handleText,
        out ObjectId objectId)
    {
        objectId = ObjectId.Null;
        if (string.IsNullOrWhiteSpace(handleText))
        {
            return false;
        }

        try
        {
            var hex = handleText.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hex = hex[2..];
            }

            if (!long.TryParse(
                    hex,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                return false;
            }

            objectId = database.GetObjectId(false, new Handle(value), 0);
            return !objectId.IsNull;
        }
        catch (System.Exception)
        {
            objectId = ObjectId.Null;
            return false;
        }
    }

    private static bool TryResolveContentVariant(
        Transaction transaction,
        MLeader leader,
        out string contentVariant,
        out string reason)
    {
        contentVariant = string.Empty;
        reason = string.Empty;
        if (transaction.GetObject(
                leader.BlockContentId,
                OpenMode.ForRead,
                openErased: false) is not BlockTableRecord block ||
            !TimberFramedBlockContentVariantRules.TryParseR3VariantKey(
                block.Name,
                out var parse) ||
            !parse.IsProductionCombinedTarget ||
            parse.ContentVariantSide is not
                TimberFramedBlockContentDimensionColumnSide side)
        {
            reason = "R3 Combined content variant cannot be resolved.";
            return false;
        }

        contentVariant =
            TimberFramedCombinedG5ContentVariantRules.ToContentVariantToken(side);
        return true;
    }

    private static bool TryResolveDimensionsTowardKneeDot(
        Transaction transaction,
        MLeader leader,
        Point3d knee,
        Point3d blockPosition,
        out double dimensionsTowardKneeDot,
        out string reason)
    {
        dimensionsTowardKneeDot = double.NaN;
        reason = string.Empty;
        if (!AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryReadWorldAttributePoints(
                    transaction,
                    leader,
                    out var points,
                    out var pointsReason))
        {
            reason = "attribute world points unavailable: " + pointsReason;
            return false;
        }

        var dimensions = new TimberPlanarPoint(
            (points.WidthAlignment.X + points.HeightAlignment.X) * 0.5d,
            (points.WidthAlignment.Y + points.HeightAlignment.Y) * 0.5d);
        if (!TimberFramedBlockContentDefinitionRules.TryEvaluateDimensionsTowardKneeDot(
                new TimberPlanarPoint(blockPosition.X, blockPosition.Y),
                new TimberPlanarPoint(knee.X, knee.Y),
                dimensions,
                out dimensionsTowardKneeDot))
        {
            reason = "DimensionsTowardKneeDot cannot be evaluated.";
            return false;
        }

        return true;
    }

    private static void WriteDiagnostics(
        Editor editor,
        AnnotationSnapshot before,
        AnnotationSnapshot after,
        double attachmentDelta)
    {
        editor.WriteMessage(
            "\n=== AK_DEV_ROTATE_ANNOTATION_180 ===" +
            $"\nHandle={before.Handle}" +
            $"\nSourceHandle={before.SourceHandle}" +
            $"\nAttachmentBefore={Fmt(before.Attachment)}" +
            $"\nAttachmentAfter={Fmt(after.Attachment)}" +
            $"\nAttachmentDelta={FmtLength(attachmentDelta)}" +
            $"\nKneeBefore={Fmt(before.Knee)}" +
            $"\nKneeAfter={Fmt(after.Knee)}" +
            $"\nBlockPositionBefore={Fmt(before.BlockPosition)}" +
            $"\nBlockPositionAfter={Fmt(after.BlockPosition)}" +
            $"\nSourcePhysicalAxisAngleDeg={Fmt(before.SourcePhysicalAxisAngleDeg)}" +
            $"\nLandingWorldAngleBeforeDeg={Fmt(before.LandingWorldAngleDeg)}" +
            $"\nLandingWorldAngleAfterDeg={Fmt(after.LandingWorldAngleDeg)}" +
            $"\nFrameWorldOrientationBeforeDeg={Fmt(before.FrameWorldOrientationDeg)}" +
            $"\nFrameWorldOrientationAfterDeg={Fmt(after.FrameWorldOrientationDeg)}" +
            $"\nBlockRotationBeforeDeg={Fmt(before.BlockRotationDeg)}" +
            $"\nBlockRotationAfterDeg={Fmt(after.BlockRotationDeg)}" +
            $"\nContentVariantBefore={before.ContentVariant}" +
            $"\nContentVariantAfter={after.ContentVariant}" +
            $"\nDimensionsTowardKneeDotBefore={Fmt(before.DimensionsTowardKneeDot)}" +
            $"\nDimensionsTowardKneeDotAfter={Fmt(after.DimensionsTowardKneeDot)}");
    }

    private static string Fmt(Point3d point) =>
        $"({FmtLength(point.X)},{FmtLength(point.Y)},{FmtLength(point.Z)})";

    private static string Fmt(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string FmtLength(double value) =>
        value.ToString("0.000000", CultureInfo.InvariantCulture);

    private sealed record AnnotationSnapshot(
        string Handle,
        string SourceHandle,
        Point3d Attachment,
        Point3d Knee,
        Point3d BlockPosition,
        double SourcePhysicalAxisAngleDeg,
        double LandingWorldAngleDeg,
        double FrameWorldOrientationDeg,
        double BlockRotationDeg,
        string ContentVariant,
        double DimensionsTowardKneeDot,
        ObjectId BlockContentId);
}
#endif
