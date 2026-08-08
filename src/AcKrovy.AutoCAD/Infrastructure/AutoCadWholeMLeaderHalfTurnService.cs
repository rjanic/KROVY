using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Shared host-proven rigid whole-MLeader half-turn. It never writes metadata,
/// BlockRotation, individual vertices, dogleg, or BlockPosition.
/// </summary>
internal static class AutoCadWholeMLeaderHalfTurnService
{
    internal const double AttachmentTolerance = 1e-6d;
    private const double RigidDistanceTolerance = 1e-6d;

#if DEBUG
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        WholeAnnotationHalfTurnTrace> LatestTraces =
        new(StringComparer.OrdinalIgnoreCase);

    internal static bool TryGetLatestTrace(
        string leaderHandle,
        out WholeAnnotationHalfTurnTrace trace) =>
        LatestTraces.TryGetValue(leaderHandle, out trace!);
#endif

    internal static bool TryApplyRequiredState(
        Transaction transaction,
        MLeader leader,
        double sourcePhysicalAxisAngleRadians,
        int currentRevision,
        string lifecyclePath,
        out WholeAnnotationHalfTurnOperation operation,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(leader);
        var decision =
            TimberFramedBlockContentWholeAnnotationHalfTurnRules.Decide(
                sourcePhysicalAxisAngleRadians,
                currentRevision);
        if (!TryCaptureGeometry(transaction, leader, out var before, out reason))
        {
            operation = default!;
            return false;
        }

        WholeMLeaderRigidHalfTurnResult transform;
        if (decision.TransformRequired)
        {
            if (!TryApplyRigidHalfTurn(
                    transaction,
                    leader,
                    out transform,
                    out reason))
            {
                operation = default!;
                return false;
            }
        }
        else
        {
            transform = new WholeMLeaderRigidHalfTurnResult(
                TransformApplied: false,
                RotationRadians: 0d,
                AttachmentBefore: before.Attachment,
                AttachmentAfter: before.Attachment,
                AttachmentDelta: 0d,
                KneeBefore: before.Knee,
                KneeAfter: before.Knee,
                BlockPositionBefore: before.BlockPosition,
                BlockPositionAfter: before.BlockPosition,
                DimensionsTowardKneeDotBefore: before.DimensionsTowardKneeDot,
                DimensionsTowardKneeDotAfter: before.DimensionsTowardKneeDot);
        }

        operation = new WholeAnnotationHalfTurnOperation(
            Decision: decision,
            Transform: transform,
            LifecyclePath: lifecyclePath);
#if DEBUG
        var handle = leader.ObjectId.IsNull
            ? leader.Handle.ToString()
            : leader.ObjectId.Handle.ToString();
        if (!string.IsNullOrWhiteSpace(handle))
        {
            LatestTraces[handle] = new WholeAnnotationHalfTurnTrace(
                SourcePhysicalAxisAngleRadians: sourcePhysicalAxisAngleRadians,
                Required: decision.Required,
                AppliedBefore: decision.AppliedBefore,
                AppliedAfter: decision.AppliedAfter,
                RevisionBefore: decision.RevisionBefore,
                RevisionAfter: decision.RevisionAfter,
                TransformAppliedThisOperation: transform.TransformApplied,
                RotationRadians: transform.RotationRadians,
                AttachmentBefore: transform.AttachmentBefore,
                AttachmentAfter: transform.AttachmentAfter,
                AttachmentDelta: transform.AttachmentDelta,
                DimensionsTowardKneeDotBefore:
                    transform.DimensionsTowardKneeDotBefore,
                DimensionsTowardKneeDotAfter:
                    transform.DimensionsTowardKneeDotAfter,
                LifecyclePath: lifecyclePath);
        }
#endif
        reason = transform.TransformApplied
            ? "whole MLeader rotated rigidly by 180 degrees around attachment"
            : "whole MLeader state already matches source requirement";
        return true;
    }

    /// <summary>
    /// The only geometry actuator used by production and the DEBUG visual
    /// oracle: one Matrix3d.Rotation(Math.PI) applied to the whole MLeader.
    /// </summary>
    internal static bool TryApplyRigidHalfTurn(
        Transaction transaction,
        MLeader leader,
        out WholeMLeaderRigidHalfTurnResult result,
        out string reason)
    {
        result = default!;
        if (!TryCaptureGeometry(transaction, leader, out var before, out reason))
        {
            return false;
        }

        var rotation = Matrix3d.Rotation(
            Math.PI,
            Vector3d.ZAxis,
            before.Attachment);
        if (!leader.IsWriteEnabled)
        {
            leader.UpgradeOpen();
        }

        leader.TransformBy(rotation);
        if (!TryCaptureGeometry(transaction, leader, out var after, out reason))
        {
            RestoreHalfTurnOrThrow(leader, rotation);
            reason = "AFTER geometry unavailable; rigid transform was rolled back. " +
                reason;
            return false;
        }

        var attachmentDelta = before.Attachment.DistanceTo(after.Attachment);
        if (attachmentDelta > AttachmentTolerance ||
            before.BlockContentId != after.BlockContentId ||
            !RigidDistancePreserved(
                before.Attachment.DistanceTo(before.Knee),
                after.Attachment.DistanceTo(after.Knee)) ||
            !RigidDistancePreserved(
                before.Knee.DistanceTo(before.BlockPosition),
                after.Knee.DistanceTo(after.BlockPosition)) ||
            !RigidDistancePreserved(
                before.Attachment.DistanceTo(before.BlockPosition),
                after.Attachment.DistanceTo(after.BlockPosition)) ||
            !TowardKneeDotPreserved(
                before.DimensionsTowardKneeDot,
                after.DimensionsTowardKneeDot))
        {
            RestoreHalfTurnOrThrow(leader, rotation);
            reason =
                $"rigid half-turn verification failed; attachmentDelta={attachmentDelta:R}; " +
                "transform was rolled back.";
            return false;
        }

        result = new WholeMLeaderRigidHalfTurnResult(
            TransformApplied: true,
            RotationRadians: Math.PI,
            AttachmentBefore: before.Attachment,
            AttachmentAfter: after.Attachment,
            AttachmentDelta: attachmentDelta,
            KneeBefore: before.Knee,
            KneeAfter: after.Knee,
            BlockPositionBefore: before.BlockPosition,
            BlockPositionAfter: after.BlockPosition,
            DimensionsTowardKneeDotBefore: before.DimensionsTowardKneeDot,
            DimensionsTowardKneeDotAfter: after.DimensionsTowardKneeDot);
        reason = "rigid whole-MLeader half-turn verified";
        return true;
    }

    private static bool TryCaptureGeometry(
        Transaction transaction,
        MLeader leader,
        out WholeMLeaderGeometrySnapshot snapshot,
        out string reason)
    {
        snapshot = default!;
        reason = string.Empty;
        if (leader.IsErased ||
            leader.ContentType != ContentType.BlockContent ||
            leader.BlockContentId.IsNull)
        {
            reason = "entity is not a live native BlockContent MLeader.";
            return false;
        }

        try
        {
            var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            if (leaderIndexes.Length != 1)
            {
                reason = "whole-MLeader correction requires exactly one leader.";
                return false;
            }

            var lineIndexes = leader
                .GetLeaderLineIndexes(leaderIndexes[0])
                .Cast<int>()
                .ToArray();
            if (lineIndexes.Length != 1)
            {
                reason = "whole-MLeader correction requires exactly one leader line.";
                return false;
            }

            var attachment = leader.GetFirstVertex(lineIndexes[0]);
            var knee = leader.GetLastVertex(lineIndexes[0]);
            var blockPosition = leader.BlockPosition;
            if (!TryResolveDimensionsTowardKneeDot(
                    transaction,
                    leader,
                    knee,
                    blockPosition,
                    out var towardKneeDot,
                    out reason))
            {
                return false;
            }

            snapshot = new WholeMLeaderGeometrySnapshot(
                attachment,
                knee,
                blockPosition,
                towardKneeDot,
                leader.BlockContentId);
            return true;
        }
        catch (System.Exception exception)
        {
            reason = "whole-MLeader geometry unavailable: " + exception.Message;
            return false;
        }
    }

    private static bool TryResolveDimensionsTowardKneeDot(
        Transaction transaction,
        MLeader leader,
        Point3d knee,
        Point3d blockPosition,
        out double towardKneeDot,
        out string reason)
    {
        towardKneeDot = double.NaN;
        if (!AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryReadWorldAttributePoints(
                    transaction,
                    leader,
                    out var points,
                    out reason))
        {
            return false;
        }

        var dimensions = new TimberPlanarPoint(
            (points.WidthAlignment.X + points.HeightAlignment.X) * 0.5d,
            (points.WidthAlignment.Y + points.HeightAlignment.Y) * 0.5d);
        if (!TimberFramedBlockContentDefinitionRules.TryEvaluateDimensionsTowardKneeDot(
                new TimberPlanarPoint(blockPosition.X, blockPosition.Y),
                new TimberPlanarPoint(knee.X, knee.Y),
                dimensions,
                out towardKneeDot))
        {
            reason = "DimensionsTowardKneeDot cannot be evaluated.";
            return false;
        }

        return true;
    }

    private static bool RigidDistancePreserved(double before, double after) =>
        Math.Abs(before - after) <= RigidDistanceTolerance;

    private static bool TowardKneeDotPreserved(double before, double after)
    {
        var scale = Math.Max(1d, Math.Max(Math.Abs(before), Math.Abs(after)));
        return before > 0d && after > 0d &&
            Math.Abs(before - after) <= RigidDistanceTolerance * scale;
    }

    private static void RestoreHalfTurnOrThrow(
        MLeader leader,
        Matrix3d sameHalfTurn)
    {
        try
        {
            leader.TransformBy(sameHalfTurn);
        }
        catch (System.Exception exception)
        {
            throw new InvalidOperationException(
                "Failed to restore MLeader after half-turn verification failure.",
                exception);
        }
    }

    private sealed record WholeMLeaderGeometrySnapshot(
        Point3d Attachment,
        Point3d Knee,
        Point3d BlockPosition,
        double DimensionsTowardKneeDot,
        ObjectId BlockContentId);
}

internal sealed record WholeAnnotationHalfTurnOperation(
    WholeAnnotationHalfTurnDecision Decision,
    WholeMLeaderRigidHalfTurnResult Transform,
    string LifecyclePath);

internal sealed record WholeMLeaderRigidHalfTurnResult(
    bool TransformApplied,
    double RotationRadians,
    Point3d AttachmentBefore,
    Point3d AttachmentAfter,
    double AttachmentDelta,
    Point3d KneeBefore,
    Point3d KneeAfter,
    Point3d BlockPositionBefore,
    Point3d BlockPositionAfter,
    double DimensionsTowardKneeDotBefore,
    double DimensionsTowardKneeDotAfter);

#if DEBUG
internal sealed record WholeAnnotationHalfTurnTrace(
    double SourcePhysicalAxisAngleRadians,
    bool Required,
    bool AppliedBefore,
    bool AppliedAfter,
    int RevisionBefore,
    int RevisionAfter,
    bool TransformAppliedThisOperation,
    double RotationRadians,
    Point3d AttachmentBefore,
    Point3d AttachmentAfter,
    double AttachmentDelta,
    double DimensionsTowardKneeDotBefore,
    double DimensionsTowardKneeDotAfter,
    string LifecyclePath);
#endif
