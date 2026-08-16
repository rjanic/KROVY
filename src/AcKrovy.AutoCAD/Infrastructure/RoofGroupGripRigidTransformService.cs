using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Accepts a coherent GRIP_STRETCH rigid translation of a complete roof GROUP
/// using the true pre-command baseline. Does not rebuild display or write metadata.
/// </summary>
internal static class RoofGroupGripRigidTransformService
{
    public static bool TryAcceptRigidGroupTransform(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyList<ObjectId> modifiedIds,
        out string rejectionReason,
        out RoofRigidGroupTransformResult? result)
    {
        rejectionReason = "uninitialized";
        result = null;
        if (ownerId.IsNull)
        {
            rejectionReason = "null-owner";
            return false;
        }

        if (!RoofGroupGripPreCommandBaselineService.TryGet(ownerId, out var baseline))
        {
            rejectionReason = "no-pre-command-baseline";
            return false;
        }

        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
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
        if (!validation.IsValid ||
            validation.Footprint is null ||
            input.Vertices is null ||
            input.Vertices.Count != 4)
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

        if (!RoofDisplayGroupService.TryCountRoofGroupsContainingOwner(
                database,
                transaction,
                ownerId,
                out var groupCount,
                out var hasExactEight) ||
            groupCount != 1 ||
            !hasExactEight)
        {
            rejectionReason = "group-ambiguous-or-missing";
            return false;
        }

        if (!RoofDisplayGroupService.TryCollectStrictStructuralDisplayEraseIds(
                database,
                transaction,
                ownerId,
                out var displayIds) ||
            displayIds.Count != SimpleGableRoofWireframe.EdgeCount)
        {
            rejectionReason = "display-incomplete";
            return false;
        }

        // Same ObjectIds as pre-command: one source + seven display, no rebuild churn.
        if (!baseline.MemberIds.Contains(ownerId) ||
            baseline.MemberIds.Count != RoofDisplayGroupService.ExpectedMemberCount)
        {
            rejectionReason = "baseline-member-set-invalid";
            return false;
        }

        foreach (var displayId in displayIds)
        {
            if (!baseline.MemberIds.Contains(displayId))
            {
                rejectionReason = "display-objectid-changed";
                return false;
            }
        }

        foreach (var modifiedId in modifiedIds.Distinct())
        {
            if (!baseline.MemberIds.Contains(modifiedId))
            {
                rejectionReason = "unrelated-entity-in-batch";
                return false;
            }
        }

        var currentDisplay = new Dictionary<RoofDisplayEdgeRole, RoofSegment3D>();
        foreach (var displayId in displayIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    displayId,
                    OpenMode.ForRead,
                    out var line,
                    database) ||
                line is null)
            {
                rejectionReason = "display-line-unavailable";
                return false;
            }

            var roleData = RoofDisplayStore.Read(line);
            if (roleData.Data is null)
            {
                rejectionReason = "display-metadata-missing";
                return false;
            }

            if (!currentDisplay.TryAdd(
                    roleData.Data.Role,
                    new RoofSegment3D(
                        new RoofPoint3D(line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z),
                        new RoofPoint3D(line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z))))
            {
                rejectionReason = "duplicate-display-role";
                return false;
            }
        }

        var preDisplay = baseline.DisplayByRole.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Segment);

        var elevation = RoofPolylineExtractor.GetSourceElevation(owner);
        var canonicalEdges = SimpleGableRoofWireframe.Create(classification.Geometry, elevation);
        var canonical = canonicalEdges.ToDictionary(edge => edge.Role, edge => edge.Segment);

        result = RoofRigidGroupTransformRules.TryClassifyTranslation(
            baseline.SourceVertices,
            input.Vertices,
            preDisplay,
            currentDisplay,
            canonical);
        if (!result.IsAccepted)
        {
            rejectionReason = result.RejectionReason;
            return false;
        }

        rejectionReason = string.Empty;
        return true;
    }
}
