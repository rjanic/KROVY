using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// READ-ONLY pre-command GRIP_STRETCH baseline for selected roof GROUP(s).
/// Captured at CommandWillStart before any native ObjectModified mutation.
/// Not persisted. Cleared on command end/cancel/fail.
/// </summary>
internal static class RoofGroupGripPreCommandBaselineService
{
    private static readonly object Gate = new();
    private static readonly Dictionary<ObjectId, RoofGroupPreCommandBaseline> ByOwner = new();

    public static void Clear(string reason)
    {
        _ = reason;
        lock (Gate)
        {
            ByOwner.Clear();
        }
    }

    public static bool TryGet(ObjectId ownerId, out RoofGroupPreCommandBaseline baseline)
    {
        lock (Gate)
        {
            return ByOwner.TryGetValue(ownerId, out baseline!);
        }
    }

    public static bool TryFindOwnerByMemberId(ObjectId memberId, out ObjectId ownerId)
    {
        lock (Gate)
        {
            foreach (var pair in ByOwner)
            {
                if (pair.Value.MemberIds.Contains(memberId))
                {
                    ownerId = pair.Key;
                    return true;
                }
            }
        }

        ownerId = ObjectId.Null;
        return false;
    }

    public static void CaptureFromImpliedSelection(Document document, string? globalCommandName)
    {
        Clear("capture-start");
        if (!LiveGeometryCommandRules.IsGripStretchCommand(globalCommandName))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(document);
        var editor = document.Editor;
        PromptSelectionResult implied;
        try
        {
            implied = editor.SelectImplied();
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return;
        }

        // Do NOT clear implied selection — grip needs it.
        if (implied.Status != PromptStatus.OK ||
            implied.Value is null ||
            implied.Value.Count == 0)
        {
            return;
        }

        var selectedIds = implied.Value.GetObjectIds();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        try
        {
            var ownerIds = ResolveOwnerIds(document.Database, transaction, selectedIds);
            foreach (var ownerId in ownerIds)
            {
                if (!TryBuildBaseline(document.Database, transaction, ownerId, out var baseline))
                {
                    continue;
                }

                lock (Gate)
                {
                    ByOwner[ownerId] = baseline;
                }
            }

            // Read-only capture — do not Commit (avoid DBMOD).
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            Clear("capture-exception");
        }
    }

    public static bool TryGetPreCommandDisplayByRole(
        ObjectId ownerId,
        out IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> display)
    {
        display = new Dictionary<RoofDisplayEdgeRole, RoofSegment3D>();
        if (!TryGet(ownerId, out var baseline))
        {
            return false;
        }

        var map = new Dictionary<RoofDisplayEdgeRole, RoofSegment3D>();
        foreach (var pair in baseline.DisplayByRole)
        {
            map[pair.Key] = pair.Value.Segment;
        }

        display = map;
        return RoofGroupGripNativeObservationRules.IsCompleteSevenRoles(display);
    }

    private static HashSet<ObjectId> ResolveOwnerIds(
        Database database,
        Transaction transaction,
        IReadOnlyList<ObjectId> selectedIds)
    {
        var owners = new HashSet<ObjectId>();
        foreach (var id in selectedIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
                continue;
            }

            if (entity is Polyline polyline &&
                RoofDefinitionStore.Read(polyline).Data is not null)
            {
                owners.Add(id);
                continue;
            }

            if (RoofDisplayStore.Read(entity).Exists)
            {
                var resolution = RoofOwnerSelectionResolver.Resolve(database, transaction, id);
                if (resolution.IsResolved)
                {
                    owners.Add(resolution.OwnerId);
                }
            }
        }

        return owners;
    }

    private static bool TryBuildBaseline(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        out RoofGroupPreCommandBaseline baseline)
    {
        baseline = null!;
        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                database) ||
            owner is null ||
            RoofDefinitionStore.Read(owner).Data is null)
        {
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
            return false;
        }

        if (!RoofDisplayGroupService.TryCollectStrictStructuralDisplayEraseIds(
                database,
                transaction,
                ownerId,
                out var displayIds) ||
            displayIds.Count != SimpleGableRoofWireframe.EdgeCount)
        {
            return false;
        }

        var input = RoofPolylineExtractor.Extract(owner);
        if (input.Vertices is null || input.Vertices.Count != 4)
        {
            return false;
        }

        var displayByRole = new Dictionary<RoofDisplayEdgeRole, DisplayBaselineEntry>();
        var memberIds = new HashSet<ObjectId> { ownerId };
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
                return false;
            }

            var stored = RoofDisplayStore.Read(line);
            if (stored.Data is null)
            {
                return false;
            }

            var segment = new RoofSegment3D(
                new RoofPoint3D(line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z),
                new RoofPoint3D(line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z));
            if (!displayByRole.TryAdd(
                    stored.Data.Role,
                    new DisplayBaselineEntry(displayId, segment)))
            {
                return false;
            }

            memberIds.Add(displayId);
        }

        if (displayByRole.Count != SimpleGableRoofWireframe.EdgeCount ||
            memberIds.Count != RoofDisplayGroupService.ExpectedMemberCount)
        {
            return false;
        }

        baseline = new RoofGroupPreCommandBaseline(
            ownerId,
            input.Vertices.ToArray(),
            RoofPolylineExtractor.GetSourceElevation(owner),
            displayByRole,
            memberIds);
        return true;
    }

    internal sealed class RoofGroupPreCommandBaseline
    {
        public RoofGroupPreCommandBaseline(
            ObjectId ownerId,
            IReadOnlyList<RoofPoint2D> sourceVertices,
            double sourceElevation,
            IReadOnlyDictionary<RoofDisplayEdgeRole, DisplayBaselineEntry> displayByRole,
            IReadOnlySet<ObjectId> memberIds)
        {
            OwnerId = ownerId;
            SourceVertices = sourceVertices;
            SourceElevation = sourceElevation;
            DisplayByRole = displayByRole;
            MemberIds = memberIds;
        }

        public ObjectId OwnerId { get; }
        public IReadOnlyList<RoofPoint2D> SourceVertices { get; }
        public double SourceElevation { get; }
        public IReadOnlyDictionary<RoofDisplayEdgeRole, DisplayBaselineEntry> DisplayByRole { get; }
        public IReadOnlySet<ObjectId> MemberIds { get; }
    }

    internal readonly record struct DisplayBaselineEntry(ObjectId EntityId, RoofSegment3D Segment);
}
