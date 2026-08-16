using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Adopts a supported SimpleGable GROUP GRIP_STRETCH display delta into the same
/// semantic source ObjectId, then reuses the existing SupportedResize lifecycle.
/// Observed geometry comes from the command-scoped native ObjectModified snapshot.
/// </summary>
internal static class RoofGroupGripResizeAdoptionService
{
    public static bool TryAdoptSupportedGroupGripResize(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        out string rejectionReason)
    {
        rejectionReason = "uninitialized";
        if (ownerId.IsNull)
        {
            rejectionReason = "null-owner";
            return false;
        }

        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForWrite,
                out var owner,
                database) ||
            owner is null)
        {
            rejectionReason = "owner-unavailable";
            return false;
        }

        var stored = RoofDefinitionStore.Read(owner);
        if (stored.Data is null)
        {
            rejectionReason = "no-definition";
            return false;
        }

        var input = RoofPolylineExtractor.Extract(owner);
        var validation = RoofFootprintValidator.Validate(input);
        if (!validation.IsValid || validation.Footprint is null || input.Vertices is null)
        {
            rejectionReason = "source-footprint-invalid";
            return false;
        }

        var classification = RoofDefinitionPersistence.Classify(
            input,
            validation.Footprint,
            stored.Data);
        if (classification.Kind != RoofSourceChangeKind.RigidEquivalent ||
            classification.Geometry is null)
        {
            rejectionReason = "source-not-rigid-equivalent";
            return false;
        }

        if (!TryEnsureSingleValidRoofGroup(database, transaction, ownerId))
        {
            rejectionReason = "group-ambiguous-or-missing";
            return false;
        }

        var elevation = RoofPolylineExtractor.GetSourceElevation(owner);
        var expectedEdges = SimpleGableRoofWireframe.Create(classification.Geometry, elevation);
        var expected = expectedEdges.ToDictionary(edge => edge.Role, edge => edge.Segment);

        RoofGroupGripGeometrySnapshotService.FreezeOwner(ownerId);

        _ = TryCollectLiveDbObservedDisplayByRole(
            database,
            transaction,
            ownerId,
            out var liveDbObserved,
            out _);

        if (!RoofDisplayGroupService.TryCollectStrictStructuralDisplayEraseIds(
                database,
                transaction,
                ownerId,
                out var displayIds))
        {
            rejectionReason = "observed-display-incomplete";
            return false;
        }

        if (!RoofGroupGripGeometrySnapshotService.TryGetLatestObservedDisplayByRole(
                database,
                transaction,
                ownerId,
                displayIds,
                out var snapshotObserved,
                out var snapshotReason))
        {
            rejectionReason = "snapshot-" + snapshotReason;
            return false;
        }

        // Timing case C is valid ONLY against a true pre-command baseline.
        // First ObjectModified geometry is already post-mutation and must not be used.
        // Without pre-command baseline we cannot prove transient-only (case C).
        if (RoofGroupGripPreCommandBaselineService.TryGetPreCommandDisplayByRole(
                ownerId,
                out var preCommandDisplay))
        {
            var timingCase = RoofGroupGripGeometrySnapshotService.ClassifyTimingCase(
                preCommandDisplay,
                snapshotObserved,
                liveDbObserved);
            if (timingCase is "C")
            {
                rejectionReason = "timing-case-C-transient-only";
                return false;
            }
        }

        if (!RoofGroupGripNativeObservationRules.HasMeaningfulDeltaFromExpected(
                expected,
                snapshotObserved,
                RoofGroupGripResizeAdoptionRules.GripAdoptionToleranceMm))
        {
            rejectionReason = "snapshot-zero-delta";
            return false;
        }

        var adoption = RoofGroupGripResizeAdoptionRules.TryDeriveSupportedSideResize(
            input.Vertices,
            stored.Data,
            expected,
            snapshotObserved);
        if (!adoption.CanAdopt || adoption.AdoptedVertices is null)
        {
            rejectionReason = adoption.RejectionReason;
            return false;
        }

        WriteSourceVertices(owner, adoption.AdoptedVertices, elevation);
        rejectionReason = string.Empty;
        return true;
    }

    private static bool TryEnsureSingleValidRoofGroup(
        Database database,
        Transaction transaction,
        ObjectId ownerId)
    {
        if (!RoofDisplayGroupService.TryCountRoofGroupsContainingOwner(
                database,
                transaction,
                ownerId,
                out var groupCount,
                out var hasExactEightMemberRoofGroup))
        {
            return false;
        }

        return groupCount == 1 && hasExactEightMemberRoofGroup;
    }

    private static bool TryCollectLiveDbObservedDisplayByRole(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        out Dictionary<RoofDisplayEdgeRole, RoofSegment3D>? observed,
        out string rejectionReason)
    {
        observed = null;
        rejectionReason = string.Empty;
        if (!RoofDisplayGroupService.TryCollectStrictStructuralDisplayEraseIds(
                database,
                transaction,
                ownerId,
                out var displayIds) ||
            displayIds.Count != SimpleGableRoofWireframe.EdgeCount)
        {
            rejectionReason = "observed-display-incomplete";
            return false;
        }

        var map = new Dictionary<RoofDisplayEdgeRole, RoofSegment3D>();
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
                rejectionReason = "observed-line-unavailable";
                return false;
            }

            var stored = RoofDisplayStore.Read(line);
            if (stored.Data is null)
            {
                rejectionReason = "observed-metadata-missing";
                return false;
            }

            if (!map.TryAdd(
                    stored.Data.Role,
                    new RoofSegment3D(
                        new RoofPoint3D(line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z),
                        new RoofPoint3D(line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z))))
            {
                rejectionReason = "duplicate-observed-role";
                return false;
            }
        }

        if (map.Count != SimpleGableRoofWireframe.EdgeCount)
        {
            rejectionReason = "observed-role-count";
            return false;
        }

        observed = map;
        return true;
    }

    private static void WriteSourceVertices(
        Polyline owner,
        IReadOnlyList<RoofPoint2D> vertices,
        double elevation)
    {
        if (vertices.Count != 4)
        {
            throw new ArgumentException("Adopted source requires four vertices.", nameof(vertices));
        }

        var writable = Math.Min(owner.NumberOfVertices, 4);
        for (var i = 0; i < writable; i++)
        {
            owner.SetPointAt(i, new Point2d(vertices[i].X, vertices[i].Y));
        }

        if (owner.NumberOfVertices > 4)
        {
            owner.SetPointAt(
                owner.NumberOfVertices - 1,
                new Point2d(vertices[0].X, vertices[0].Y));
        }

        owner.Closed = true;
        _ = elevation;
    }
}
