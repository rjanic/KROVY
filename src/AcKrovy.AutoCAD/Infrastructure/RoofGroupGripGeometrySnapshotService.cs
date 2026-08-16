using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Command-scoped native GRIP_STRETCH geometry snapshot. READ-ONLY capture from
/// ObjectModified entity instances. Frozen before plugin Rebuild so canonical
/// repair cannot overwrite the user delta.
/// </summary>
internal static class RoofGroupGripGeometrySnapshotService
{
    private static readonly object Gate = new();
    private static string? _commandName;
    private static bool _frozen;
    private static readonly Dictionary<ObjectId, EntityGeometrySample> LatestByEntity = new();
    private static readonly HashSet<ObjectId> FrozenOwners = new();

    public static void BeginCommandScope(string? globalCommandName)
    {
        lock (Gate)
        {
            ClearUnlocked();
            if (LiveGeometryCommandRules.IsGripStretchCommand(globalCommandName) ||
                LiveGeometryCommandRules.IsUndoGroupingSourceCommand(globalCommandName))
            {
                _commandName = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
            }
        }
    }

    public static void EndCommandScope(string reason = "unspecified")
    {
        _ = reason;
        lock (Gate)
        {
            ClearUnlocked();
        }
    }

    public static void FreezeAll()
    {
        lock (Gate)
        {
            _frozen = true;
        }
    }

    public static bool IsScopeActive
    {
        get
        {
            lock (Gate)
            {
                return _commandName is not null;
            }
        }
    }

    public static bool IsFrozen
    {
        get
        {
            lock (Gate)
            {
                return _frozen;
            }
        }
    }

    public static void FreezeOwner(ObjectId ownerId)
    {
        if (ownerId.IsNull)
        {
            return;
        }

        lock (Gate)
        {
            FrozenOwners.Add(ownerId);
        }
    }

    /// <summary>
    /// Capture actual DBObject geometry from a native ObjectModified callback.
    /// Must not open write transactions. Safe to call only when suppress is false.
    /// </summary>
    public static void TryCaptureNativeObjectModified(
        Entity entity,
        string? globalCommandName,
        bool modifiedIdsSuppressed)
    {
        if (entity is null || entity.ObjectId.IsNull || entity.IsErased)
        {
            return;
        }

        if (modifiedIdsSuppressed)
        {
            return;
        }

        if (!LiveGeometryCommandRules.IsGripStretchCommand(globalCommandName) &&
            !LiveGeometryCommandRules.IsUndoGroupingSourceCommand(globalCommandName))
        {
            return;
        }

        EntityGeometrySample sample;
        if (entity is Line line)
        {
            sample = EntityGeometrySample.FromLine(line);
        }
        else if (entity is Polyline polyline)
        {
            sample = EntityGeometrySample.FromPolyline(polyline);
        }
        else
        {
            return;
        }

        lock (Gate)
        {
            if (_frozen)
            {
                return;
            }

            LatestByEntity[entity.ObjectId] = sample;
            _commandName ??= LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        }
    }

    public static bool TryGetLatestObservedDisplayByRole(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyList<ObjectId> displayIds,
        out IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> observed,
        out string rejectionReason)
    {
        observed = new Dictionary<RoofDisplayEdgeRole, RoofSegment3D>();
        rejectionReason = string.Empty;
        var map = new Dictionary<RoofDisplayEdgeRole, RoofSegment3D>();
        Dictionary<ObjectId, EntityGeometrySample> latestCopy;
        lock (Gate)
        {
            latestCopy = new Dictionary<ObjectId, EntityGeometrySample>(LatestByEntity);
        }

        foreach (var id in displayIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var line,
                    database) ||
                line is null)
            {
                rejectionReason = "snapshot-line-unavailable";
                return false;
            }

            var stored = RoofDisplayStore.Read(line);
            if (stored.Data is null)
            {
                rejectionReason = "snapshot-metadata-missing";
                return false;
            }

            if (!latestCopy.TryGetValue(id, out var sample) ||
                sample.Segment is null)
            {
                rejectionReason = "snapshot-missing-entity";
                return false;
            }

            if (!map.TryAdd(stored.Data.Role, sample.Segment))
            {
                rejectionReason = "snapshot-duplicate-role";
                return false;
            }
        }

        if (!RoofGroupGripNativeObservationRules.IsCompleteSevenRoles(map))
        {
            rejectionReason = "snapshot-incomplete-roles";
            return false;
        }

        observed = map;
        return true;
    }

    public static string ClassifyTimingCase(
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> preCommandOrExpected,
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D>? snapshotObserved,
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D>? liveDbObserved)
    {
        // Authority for "did DB geometry change?" is the TRUE pre-command baseline when
        // available. First ObjectModified geometry is already post-mutation and must not
        // be used as the no-delta baseline for timingCase=C.
        var tol = RoofGroupGripResizeAdoptionRules.GripAdoptionToleranceMm;
        var snapDelta = snapshotObserved is not null &&
            RoofGroupGripNativeObservationRules.HasMeaningfulDeltaFromExpected(
                preCommandOrExpected,
                snapshotObserved,
                tol);
        var liveDelta = liveDbObserved is not null &&
            RoofGroupGripNativeObservationRules.HasMeaningfulDeltaFromExpected(
                preCommandOrExpected,
                liveDbObserved,
                tol);

        if (snapDelta && !liveDelta)
        {
            return "A";
        }

        if (snapDelta && liveDelta)
        {
            return "B";
        }

        if (!snapDelta && !liveDelta)
        {
            return "C";
        }

        // Live has delta but snapshot does not — unexpected restore/capture miss.
        return "D";
    }

    private static void ClearUnlocked()
    {
        _commandName = null;
        _frozen = false;
        LatestByEntity.Clear();
        FrozenOwners.Clear();
    }

    private readonly record struct EntityGeometrySample(RoofSegment3D? Segment)
    {
        public static EntityGeometrySample FromLine(Line line)
        {
            var segment = new RoofSegment3D(
                new RoofPoint3D(line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z),
                new RoofPoint3D(line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z));
            return new EntityGeometrySample(segment);
        }

        public static EntityGeometrySample FromPolyline(Polyline polyline)
        {
            _ = polyline;
            return new EntityGeometrySample(null);
        }
    }
}
