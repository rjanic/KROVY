using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Command-scoped in-memory snapshot of a valid SimpleGable roof assembly for
/// Unsupported STRETCH / GRIP_STRETCH Auto-Recovery: roof source + owned generated
/// timber Lines + annotations bound to those timber SourceHandles.
/// Captures ALL owned generated members (not a predicted STRETCH subset) so any
/// crossing-window victim can be restored identity-preservingly.
/// Not persisted. Cleared on command end/cancel/fail/dispose/next capture.
/// </summary>
internal static class RoofUnsupportedStretchRecoverySnapshotService
{
    private static readonly object Gate = new();
    private static readonly Dictionary<ObjectId, SnapshotEntry> ByOwner = new();
    private static readonly List<string> CaptureSkips = new();
    private static string? _commandName;
    private static string? _lastClearReason;
    private static string? _lastClearCommand;
    private static string? _lastCaptureCommand;

    public static void Clear(string reason) => Clear(reason, commandName: null);

    public static void Clear(string reason, string? commandName)
    {
        _ = reason;
        lock (Gate)
        {
            ByOwner.Clear();
            _commandName = null;
            _lastClearReason = reason;
            if (!string.IsNullOrWhiteSpace(commandName))
            {
                _lastClearCommand = LiveGeometryCommandRules.NormalizeCommandName(commandName);
            }

            if (string.Equals(reason, "capture-start", StringComparison.Ordinal))
            {
                CaptureSkips.Clear();
            }
        }
    }

    public static string? LastClearReason
    {
        get
        {
            lock (Gate)
            {
                return _lastClearReason;
            }
        }
    }

    public static string? LastClearCommand
    {
        get
        {
            lock (Gate)
            {
                return _lastClearCommand;
            }
        }
    }

    public static string? LastCaptureCommand
    {
        get
        {
            lock (Gate)
            {
                return _lastCaptureCommand;
            }
        }
    }

    public static IReadOnlyList<string> GetCaptureSkips()
    {
        lock (Gate)
        {
            return CaptureSkips.ToArray();
        }
    }

    public static void CaptureForCommand(Document document, string? globalCommandName)
    {
        Clear("capture-start", globalCommandName);
        if (!RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand(globalCommandName))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(document);
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        lock (Gate)
        {
            _commandName = normalized;
            _lastCaptureCommand = normalized;
        }

        using var transaction = document.Database.TransactionManager.StartTransaction();
        try
        {
            var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
            var blockTable = (BlockTable)transaction.GetObject(
                document.Database.BlockTableId,
                OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace],
                OpenMode.ForRead);

            var eligibleRoofs = new List<(ObjectId Id, Polyline Polyline, RoofUnsupportedStretchSourceSnapshotData Source)>();
            foreach (ObjectId id in modelSpace)
            {
                if (id.IsErased ||
                    !AutoCadObjectIdAccess.TryGetObject<Polyline>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var polyline,
                        document.Database) ||
                    polyline is null ||
                    polyline.IsErased)
                {
                    continue;
                }

                if (!TryBuildRoofSourceSnapshot(polyline, out var sourceData))
                {
                    continue;
                }

                eligibleRoofs.Add((id, polyline, sourceData));
            }

            foreach (var roof in eligibleRoofs)
            {
                if (!TryBuildAssembly(
                        document.Database,
                        transaction,
                        metadataStore,
                        modelSpace,
                        roof.Polyline,
                        roof.Source,
                        out var assembly,
                        out var skipReason))
                {
                    RecordCaptureSkip(roof.Source.OwnerHandle, skipReason);
                    continue;
                }

                lock (Gate)
                {
                    ByOwner[roof.Id] = new SnapshotEntry(roof.Id, assembly);
                }
            }

            // Read-only capture — do not Commit (avoid DBMOD).
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            Clear("capture-exception", globalCommandName);
        }
    }

    public static bool TryGet(ObjectId ownerId, out SnapshotEntry entry)
    {
        lock (Gate)
        {
            return ByOwner.TryGetValue(ownerId, out entry!);
        }
    }

    public static bool TryGetByHandle(string ownerHandle, out SnapshotEntry entry)
    {
        entry = null!;
        if (string.IsNullOrWhiteSpace(ownerHandle))
        {
            return false;
        }

        lock (Gate)
        {
            foreach (var pair in ByOwner)
            {
                if (string.Equals(
                        pair.Value.Assembly.RoofSource.OwnerHandle,
                        ownerHandle,
                        StringComparison.OrdinalIgnoreCase))
                {
                    entry = pair.Value;
                    return true;
                }
            }
        }

        return false;
    }

    public static string? CurrentCommandName
    {
        get
        {
            lock (Gate)
            {
                return _commandName;
            }
        }
    }

    public static IReadOnlyList<ObjectId> GetOwnerIds()
    {
        lock (Gate)
        {
            return ByOwner.Keys.ToArray();
        }
    }

    public static int SnapshotCount
    {
        get
        {
            lock (Gate)
            {
                return ByOwner.Count;
            }
        }
    }

    private static bool TryBuildRoofSourceSnapshot(
        Polyline polyline,
        out RoofUnsupportedStretchSourceSnapshotData sourceData)
    {
        sourceData = null!;
        var stored = RoofDefinitionStore.Read(polyline);
        if (stored.Data is null)
        {
            return false;
        }

        var input = RoofPolylineExtractor.Extract(polyline);
        if (input.Vertices is null || input.Vertices.Count != 4 || !input.IsClosed)
        {
            return false;
        }

        var validation = RoofFootprintValidator.Validate(input);
        if (!validation.IsValid || validation.Footprint is null)
        {
            return false;
        }

        var classification = RoofDefinitionPersistence.Classify(
            input,
            validation.Footprint,
            stored.Data);
        if (classification.Kind != RoofSourceChangeKind.RigidEquivalent)
        {
            return false;
        }

        var normal = polyline.Normal;
        sourceData = new RoofUnsupportedStretchSourceSnapshotData(
            polyline.Handle.ToString(),
            input.Vertices.ToArray(),
            polyline.Closed,
            RoofPolylineExtractor.GetSourceElevation(polyline),
            normal.X,
            normal.Y,
            normal.Z);
        return RoofUnsupportedStretchRecoveryRules.IsEligibleSnapshot(sourceData);
    }

    private static bool TryBuildAssembly(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        BlockTableRecord modelSpace,
        Polyline owner,
        RoofUnsupportedStretchSourceSnapshotData sourceData,
        out RoofUnsupportedStretchAssemblySnapshotData assembly,
        out string skipReason)
    {
        assembly = null!;
        skipReason = "assembly-build-failed";
        var timberIds = RoofGeneratedTimberStore.FindByOwner(
            database,
            transaction,
            sourceData.OwnerHandle);
        var timberLines = new List<RoofUnsupportedStretchTimberLineSnapshotData>(timberIds.Count);
        var timberSourceHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var timberIdSet = new HashSet<ObjectId>(timberIds);

        foreach (var timberId in timberIds)
        {
            if (timberId.IsNull || timberId.IsErased)
            {
                skipReason = "generated-timber-erased";
                return false;
            }

            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    timberId,
                    OpenMode.ForRead,
                    out var timberEntity,
                    database) ||
                timberEntity is null)
            {
                skipReason = "generated-timber-missing";
                return false;
            }

            if (timberEntity is not Line line)
            {
                skipReason = $"generated-timber-type-mismatch:{timberEntity.GetType().Name}";
                return false;
            }

            if (!metadataStore.TryRead(line, out var timberData) ||
                timberData is null ||
                string.IsNullOrWhiteSpace(timberData.ElementId))
            {
                // Owned set must be fully readable for deterministic recovery.
                skipReason = "generated-timber-metadata-mismatch";
                return false;
            }

            var sourceHandle = line.Handle.ToString();
            timberSourceHandles.Add(sourceHandle);
            timberLines.Add(new RoofUnsupportedStretchTimberLineSnapshotData(
                sourceHandle,
                timberData.ElementId,
                sourceHandle,
                ToPoint(line.StartPoint),
                ToPoint(line.EndPoint)));
        }

        foreach (var attachedId in RoofAttachedManualTimberStore.FindByOwner(
                     database,
                     transaction,
                     sourceData.OwnerHandle))
        {
            if (attachedId.IsNull || attachedId.IsErased || timberIdSet.Contains(attachedId))
            {
                continue;
            }

            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    attachedId,
                    OpenMode.ForRead,
                    out var attachedEntity,
                    database) ||
                attachedEntity is not Line attachedLine)
            {
                skipReason = "attached-manual-timber-missing";
                return false;
            }

            if (!metadataStore.TryRead(attachedLine, out var attachedTimberData) ||
                attachedTimberData is null ||
                string.IsNullOrWhiteSpace(attachedTimberData.ElementId))
            {
                skipReason = "attached-manual-metadata-mismatch";
                return false;
            }

            var attachedHandle = attachedLine.Handle.ToString();
            timberIdSet.Add(attachedId);
            timberSourceHandles.Add(attachedHandle);
            timberLines.Add(new RoofUnsupportedStretchTimberLineSnapshotData(
                attachedHandle,
                attachedTimberData.ElementId,
                attachedHandle,
                ToPoint(attachedLine.StartPoint),
                ToPoint(attachedLine.EndPoint)));
        }

        var annotations = new List<RoofUnsupportedStretchAnnotationSnapshotData>();
        if (timberSourceHandles.Count > 0)
        {
            foreach (ObjectId id in modelSpace)
            {
                if (id.IsErased || timberIdSet.Contains(id))
                {
                    continue;
                }

                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        database) ||
                    entity is null ||
                    entity.IsErased)
                {
                    continue;
                }

                // Skip roof display Lines — rebuilt after source restore.
                if (RoofDisplayStore.Read(entity).Exists)
                {
                    continue;
                }

                if (!TryResolveAnnotationSourceHandle(entity, out var annotationSourceHandle) ||
                    !timberSourceHandles.Contains(annotationSourceHandle))
                {
                    continue;
                }

                if (!TryCaptureAnnotation(
                        entity,
                        annotationSourceHandle,
                        out var annotation,
                        out var annotationSkip))
                {
                    skipReason = annotationSkip;
                    return false;
                }

                annotations.Add(annotation);
            }
        }

        assembly = new RoofUnsupportedStretchAssemblySnapshotData(
            sourceData,
            timberLines,
            annotations);
        if (!RoofUnsupportedStretchRecoveryRules.IsEligibleAssembly(assembly))
        {
            skipReason = "assembly-eligibility-failed";
            return false;
        }

        skipReason = string.Empty;
        return true;
    }

    private static void RecordCaptureSkip(string ownerHandle, string reason)
    {
        lock (Gate)
        {
            CaptureSkips.Add($"owner={ownerHandle};reason={reason}");
        }
    }

    public static string FormatAnnotationKindCounts(RoofUnsupportedStretchAssemblySnapshotData assembly)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var annotation in assembly.Annotations)
        {
            var key = annotation.Kind.ToString();
            counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
        }

        if (counts.Count == 0)
        {
            return "none";
        }

        return string.Join(",", counts.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value}"));
    }

    private static bool TryResolveAnnotationSourceHandle(Entity entity, out string sourceHandle)
    {
        sourceHandle = string.Empty;
        if (ElementLabelStore.TryRead(entity, out var label) &&
            label is not null &&
            !string.IsNullOrWhiteSpace(label.SourceHandle))
        {
            sourceHandle = label.SourceHandle;
            return true;
        }

        if (SlopeArrowStore.TryRead(entity, out var arrow) &&
            arrow is not null &&
            !string.IsNullOrWhiteSpace(arrow.SourceHandle))
        {
            sourceHandle = arrow.SourceHandle;
            return true;
        }

        if (SlopeAngleTextStore.TryRead(entity, out var angle) &&
            angle is not null &&
            !string.IsNullOrWhiteSpace(angle.SourceHandle))
        {
            sourceHandle = angle.SourceHandle;
            return true;
        }

        if (PostFootprintPerpendicularAnnotationStore.TryRead(entity, out var post) &&
            post is not null &&
            !string.IsNullOrWhiteSpace(post.SourceHandle))
        {
            sourceHandle = post.SourceHandle;
            return true;
        }

        return false;
    }

    private static bool TryCaptureAnnotation(
        Entity entity,
        string sourceHandle,
        out RoofUnsupportedStretchAnnotationSnapshotData annotation,
        out string skipReason)
    {
        annotation = null!;
        skipReason = string.Empty;
        var handle = entity.Handle.ToString();
        switch (entity)
        {
            case Line line:
                annotation = new RoofUnsupportedStretchAnnotationSnapshotData(
                    handle,
                    sourceHandle,
                    RoofUnsupportedStretchAnnotationKind.Line,
                    null,
                    null,
                    ToPoint(line.StartPoint),
                    ToPoint(line.EndPoint),
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
                return true;

            case Polyline polyline:
            {
                var vertices = new List<RoofPoint2D>(polyline.NumberOfVertices);
                var bulges = new List<double>(polyline.NumberOfVertices);
                for (var i = 0; i < polyline.NumberOfVertices; i++)
                {
                    var p = polyline.GetPoint2dAt(i);
                    vertices.Add(new RoofPoint2D(p.X, p.Y));
                    bulges.Add(polyline.GetBulgeAt(i));
                }

                annotation = new RoofUnsupportedStretchAnnotationSnapshotData(
                    handle,
                    sourceHandle,
                    RoofUnsupportedStretchAnnotationKind.Polyline,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    vertices,
                    bulges,
                    polyline.Closed,
                    polyline.Elevation);
                return true;
            }

            case MText mtext:
                annotation = new RoofUnsupportedStretchAnnotationSnapshotData(
                    handle,
                    sourceHandle,
                    RoofUnsupportedStretchAnnotationKind.MText,
                    ToPoint(mtext.Location),
                    mtext.Rotation,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
                return true;

            case DBText dbText:
                annotation = new RoofUnsupportedStretchAnnotationSnapshotData(
                    handle,
                    sourceHandle,
                    RoofUnsupportedStretchAnnotationKind.DBText,
                    ToPoint(dbText.Position),
                    dbText.Rotation,
                    ToPoint(dbText.AlignmentPoint),
                    null,
                    null,
                    dbText.Height,
                    null,
                    null,
                    null,
                    null);
                return true;

            case MLeader leader:
                if (!TryCaptureMLeader(leader, handle, sourceHandle, out annotation))
                {
                    skipReason = $"annotation-capture-failed:MLeader:{handle}";
                    return false;
                }

                return true;

            case BlockReference block:
                annotation = new RoofUnsupportedStretchAnnotationSnapshotData(
                    handle,
                    sourceHandle,
                    RoofUnsupportedStretchAnnotationKind.BlockReference,
                    ToPoint(block.Position),
                    block.Rotation,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
                return true;

            case Circle circle:
                annotation = new RoofUnsupportedStretchAnnotationSnapshotData(
                    handle,
                    sourceHandle,
                    RoofUnsupportedStretchAnnotationKind.Circle,
                    ToPoint(circle.Center),
                    null,
                    null,
                    null,
                    null,
                    circle.Radius,
                    null,
                    null,
                    null,
                    null);
                return true;

            default:
                skipReason = $"unsupported-annotation-entity-type:{entity.GetType().Name}:{handle}";
                return false;
        }
    }

    private static bool TryCaptureMLeader(
        MLeader leader,
        string handle,
        string sourceHandle,
        out RoofUnsupportedStretchAnnotationSnapshotData annotation)
    {
        annotation = null!;
        try
        {
            var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            if (leaderIndexes.Length != 1)
            {
                return false;
            }

            var leaderIndex = leaderIndexes[0];
            var lineIndexes = leader.GetLeaderLineIndexes(leaderIndex).Cast<int>().ToArray();
            if (lineIndexes.Length != 1)
            {
                return false;
            }

            var lineIndex = lineIndexes[0];
            var attachment = leader.GetFirstVertex(lineIndex);
            var knee = leader.GetLastVertex(lineIndex);
            Point3d? landing = null;
            double? blockRotation = null;
            var contentKind = leader.ContentType switch
            {
                ContentType.BlockContent => RoofUnsupportedStretchMLeaderContentKind.BlockContent,
                ContentType.MTextContent => RoofUnsupportedStretchMLeaderContentKind.MTextContent,
                ContentType.NoneContent => RoofUnsupportedStretchMLeaderContentKind.NoneContent,
                _ => RoofUnsupportedStretchMLeaderContentKind.Unknown,
            };
            if (leader.ContentType == ContentType.BlockContent)
            {
                landing = leader.BlockPosition;
                blockRotation = leader.BlockRotation;
            }
            else if (leader.ContentType == ContentType.MTextContent)
            {
                landing = leader.TextLocation;
            }

            RoofPoint3D? doglegPoint = null;
            double? doglegLength = null;
            var enableDogleg = leader.EnableDogleg;
            try
            {
                if (enableDogleg)
                {
                    var dogleg = leader.GetDogleg(leaderIndex);
                    doglegPoint = new RoofPoint3D(dogleg.X, dogleg.Y, dogleg.Z);
                    doglegLength = leader.DoglegLength;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                doglegPoint = null;
                doglegLength = null;
            }

            annotation = new RoofUnsupportedStretchAnnotationSnapshotData(
                handle,
                sourceHandle,
                RoofUnsupportedStretchAnnotationKind.MLeader,
                doglegPoint,
                blockRotation,
                ToPoint(attachment),
                ToPoint(knee),
                landing is { } lp ? ToPoint(lp) : null,
                doglegLength,
                null,
                null,
                null,
                null,
                leaderIndex,
                lineIndex,
                contentKind,
                enableDogleg);
            return true;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static RoofPoint3D ToPoint(Point3d point) => new(point.X, point.Y, point.Z);

    internal sealed class SnapshotEntry
    {
        public SnapshotEntry(ObjectId ownerId, RoofUnsupportedStretchAssemblySnapshotData assembly)
        {
            OwnerId = ownerId;
            Assembly = assembly;
        }

        public ObjectId OwnerId { get; }
        public RoofUnsupportedStretchAssemblySnapshotData Assembly { get; }

        public IReadOnlyDictionary<RoofGeneratedMemberKey, string> PreResizeAnchorHandleByKey { get; private set; } =
            new Dictionary<RoofGeneratedMemberKey, string>();

        public void SetPreResizeAnchorHandleByKey(
            IReadOnlyDictionary<RoofGeneratedMemberKey, string> map) =>
            PreResizeAnchorHandleByKey = map;

        public RoofUnsupportedStretchSourceSnapshotData Data => Assembly.RoofSource;
    }
}
