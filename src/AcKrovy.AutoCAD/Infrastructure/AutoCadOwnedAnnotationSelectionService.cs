using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Read-only resolver that maps a user selection to complete owned KROVY
/// annotation logical groups for future Edit kóty transforms.
/// Never opens entities for write and never mutates geometry/metadata.
/// </summary>
internal static class AutoCadOwnedAnnotationSelectionService
{
    public static AutoCadOwnedAnnotationSelectionResult Resolve(
        Database database,
        IReadOnlyList<ObjectId> selectedIds)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(selectedIds);

        using var transaction = database.TransactionManager.StartOpenCloseTransaction();
        var result = Resolve(database, transaction, selectedIds);
        transaction.Commit();
        return result;
    }

    public static AutoCadOwnedAnnotationSelectionResult Resolve(
        Database database,
        Transaction transaction,
        IReadOnlyList<ObjectId> selectedIds)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(selectedIds);

        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var skipped = new List<TimberOwnedAnnotationSkippedItem>();
        var selectedProbes = new List<TimberOwnedAnnotationComponentProbe>();
        var seenSelectionIds = new HashSet<ObjectId>();

        foreach (var id in selectedIds)
        {
            if (id.IsNull)
            {
                skipped.Add(new TimberOwnedAnnotationSkippedItem(
                    null,
                    TimberOwnedAnnotationSkipReason.UnrelatedEntity));
                continue;
            }

            if (!seenSelectionIds.Add(id))
            {
                skipped.Add(new TimberOwnedAnnotationSkippedItem(
                    id.Handle.ToString(),
                    TimberOwnedAnnotationSkipReason.DuplicateSelection));
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
                skipped.Add(new TimberOwnedAnnotationSkippedItem(
                    id.Handle.ToString(),
                    TimberOwnedAnnotationSkipReason.UnrelatedEntity));
                continue;
            }

            if (AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) &&
                metadataStore.TryRead(entity, out var timberData) &&
                timberData is not null)
            {
                skipped.Add(new TimberOwnedAnnotationSkippedItem(
                    entity.Handle.ToString(),
                    TimberOwnedAnnotationSkipReason.TimberSourceEntity));
                continue;
            }

            if (IsAuxiliaryAnnotation(entity))
            {
                skipped.Add(new TimberOwnedAnnotationSkippedItem(
                    entity.Handle.ToString(),
                    TimberOwnedAnnotationSkipReason.AuxiliaryAnnotation));
                continue;
            }

            if (!ElementLabelStore.TryRead(entity, out var labelData) ||
                labelData is null)
            {
                skipped.Add(new TimberOwnedAnnotationSkippedItem(
                    entity.Handle.ToString(),
                    TimberOwnedAnnotationSkipReason.NoLabelMetadata));
                continue;
            }

            selectedProbes.Add(ToProbe(entity, labelData));
        }

        var ownedProbesBySource = ReadOwnedProbesBySourceHandle(database, transaction);
        var involvedHandles = selectedProbes
            .Where(probe => !string.IsNullOrWhiteSpace(probe.SourceHandle))
            .Select(probe => probe.SourceHandle.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var lookup =
            new Dictionary<string, IReadOnlyList<TimberOwnedAnnotationComponentProbe>>(
                StringComparer.OrdinalIgnoreCase);
        foreach (var handle in involvedHandles)
        {
            if (ownedProbesBySource.TryGetValue(handle, out var siblings))
            {
                lookup[handle] = siblings;
            }
        }

        var evaluation = TimberOwnedAnnotationSelectionRules.Evaluate(
            selectedProbes,
            lookup);

        skipped.AddRange(evaluation.Skipped);

        var accepted = new List<AutoCadOwnedAnnotationAcceptedGroup>();
        var rejected = new List<TimberOwnedAnnotationRejectedGroup>(evaluation.Rejected);

        foreach (var group in evaluation.Accepted)
        {
            var enriched = EnrichAcceptedGroup(
                database,
                transaction,
                metadataStore,
                group.Snapshot);
            if (!enriched.Snapshot.SourceResolved)
            {
                rejected.Add(new TimberOwnedAnnotationRejectedGroup(
                    enriched.Snapshot.LogicalGroupKey,
                    enriched.Snapshot.SourceHandle,
                    TimberOwnedAnnotationRejectReason.DeadOrInvalidSource,
                    enriched.Snapshot.Components
                        .Select(component => component.ComponentKey)
                        .ToArray()));
                continue;
            }

            accepted.Add(enriched);
        }

        return new AutoCadOwnedAnnotationSelectionResult(
            accepted,
            skipped,
            rejected);
    }

    private static AutoCadOwnedAnnotationAcceptedGroup EnrichAcceptedGroup(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        TimberOwnedAnnotationGroupSnapshot snapshot)
    {
        var componentIds = new List<ObjectId>();
        var mainLeaderId = ObjectId.Null;
        foreach (var component in snapshot.Components)
        {
            if (!TryParseHandleObjectId(database, component.ComponentKey, out var id) ||
                id.IsNull)
            {
                continue;
            }

            componentIds.Add(id);
            if (component.IsMainLeader)
            {
                mainLeaderId = id;
            }
        }

        double? attachmentX = null;
        double? attachmentY = null;
        double? contentAngle = null;
        var absoluteWorld = snapshot.ContentOrientationIsAbsoluteWorld;
        if (!mainLeaderId.IsNull &&
            AutoCadObjectIdAccess.TryGetObject<MLeader>(
                transaction,
                mainLeaderId,
                OpenMode.ForRead,
                out var leader,
                database) &&
            leader is not null)
        {
            if (TryReadLiveAttachment(leader, out var attachment))
            {
                attachmentX = attachment.X;
                attachmentY = attachment.Y;
            }

            if (TryReadLiveContentOrientation(
                    leader,
                    snapshot.RepresentationKind,
                    out var angle,
                    out absoluteWorld))
            {
                contentAngle = angle;
            }
        }

        var sourceResolved = TryResolveLiveSource(
            database,
            transaction,
            metadataStore,
            snapshot.SourceHandle,
            out var sourceId,
            out var sourceAxis);

        var enrichedSnapshot = snapshot with
        {
            LiveAttachmentX = attachmentX,
            LiveAttachmentY = attachmentY,
            LiveContentWorldAngleRadians = contentAngle,
            ContentOrientationIsAbsoluteWorld = absoluteWorld,
            SourcePhysicalAxisAngleRadians = sourceAxis,
            SourceResolved = sourceResolved,
        };

        return new AutoCadOwnedAnnotationAcceptedGroup(
            enrichedSnapshot,
            sourceId,
            mainLeaderId,
            componentIds);
    }

    private static bool TryResolveLiveSource(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        string sourceHandle,
        out ObjectId sourceId,
        out double? sourceAxisRadians)
    {
        sourceId = ObjectId.Null;
        sourceAxisRadians = null;
        if (!TryParseHandleObjectId(database, sourceHandle, out var id) ||
            id.IsNull ||
            !AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                id,
                OpenMode.ForRead,
                out var entity,
                database) ||
            entity is null ||
            entity.IsErased ||
            !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
            !metadataStore.TryRead(entity, out var data) ||
            data is null)
        {
            return false;
        }

        sourceId = id;
        if (!TryReadSourcePhysicalAxis(entity, out var axis))
        {
            return false;
        }

        sourceAxisRadians = axis;
        return true;
    }

    private static bool TryReadSourcePhysicalAxis(Entity entity, out double axisRadians)
    {
        axisRadians = 0d;
        Point3d start;
        Point3d end;
        switch (entity)
        {
            case Line line:
                start = line.StartPoint;
                end = line.EndPoint;
                break;
            case Polyline polyline when polyline.NumberOfVertices >= 2:
                start = polyline.GetPoint3dAt(0);
                end = polyline.GetPoint3dAt(polyline.NumberOfVertices - 1);
                break;
            default:
                return false;
        }

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (Math.Sqrt((dx * dx) + (dy * dy)) <=
            TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            return false;
        }

        axisRadians = TimberAnnotationTransformRules.NormalizeWorldAngleRadians(
            Math.Atan2(dy, dx));
        return true;
    }

    private static bool TryReadLiveAttachment(MLeader leader, out Point3d attachment)
    {
        attachment = default;
        try
        {
            var lineIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            if (lineIndexes.Length < 1)
            {
                return false;
            }

            var leaderLineIndexes = leader.GetLeaderLineIndexes(lineIndexes[0])
                .Cast<int>()
                .ToArray();
            if (leaderLineIndexes.Length < 1)
            {
                return false;
            }

            attachment = leader.GetFirstVertex(leaderLineIndexes[0]);
            return true;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static bool TryReadLiveContentOrientation(
        MLeader leader,
        TimberOwnedAnnotationRepresentationKind kind,
        out double angleRadians,
        out bool absoluteWorld)
    {
        angleRadians = 0d;
        absoluteWorld = true;
        try
        {
            switch (kind)
            {
                case TimberOwnedAnnotationRepresentationKind.PlainItemOnly:
                case TimberOwnedAnnotationRepresentationKind.DimensionsOnly:
                case TimberOwnedAnnotationRepresentationKind.CombinedPlain:
                    if (leader.ContentType != ContentType.MTextContent ||
                        leader.MText is null)
                    {
                        return false;
                    }

                    angleRadians =
                        TimberAnnotationTransformRules.NormalizeWorldAngleRadians(
                            leader.MText.Rotation);
                    absoluteWorld = true;
                    return true;

                case TimberOwnedAnnotationRepresentationKind.FramedItemOnly:
                    if (leader.ContentType != ContentType.BlockContent)
                    {
                        return false;
                    }

                    angleRadians =
                        TimberAnnotationTransformRules.NormalizeWorldAngleRadians(
                            leader.BlockRotation);
                    absoluteWorld = true;
                    return true;

                case TimberOwnedAnnotationRepresentationKind.R3Combined:
                    if (leader.ContentType != ContentType.BlockContent)
                    {
                        return false;
                    }

                    // Relative BlockRotation only; absolute AttrRef world axis is Stage 3+.
                    angleRadians =
                        TimberAnnotationTransformRules.NormalizeWorldAngleRadians(
                            leader.BlockRotation);
                    absoluteWorld = false;
                    return true;

                default:
                    return false;
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<TimberOwnedAnnotationComponentProbe>>
        ReadOwnedProbesBySourceHandle(
            Database database,
            Transaction transaction)
    {
        var map = new Dictionary<string, List<TimberOwnedAnnotationComponentProbe>>(
            StringComparer.OrdinalIgnoreCase);
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                entity is not (MText or MLeader) ||
                !ElementLabelStore.TryRead(entity, out var data) ||
                data is null ||
                string.IsNullOrWhiteSpace(data.SourceHandle))
            {
                continue;
            }

            var probe = ToProbe(entity, data);
            var handle = probe.SourceHandle.Trim();
            if (!map.TryGetValue(handle, out var list))
            {
                list = new List<TimberOwnedAnnotationComponentProbe>();
                map[handle] = list;
            }

            list.Add(probe);
        }

        return map.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<TimberOwnedAnnotationComponentProbe>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static TimberOwnedAnnotationComponentProbe ToProbe(
        Entity entity,
        ElementLabelData data)
    {
        var entityKind = entity switch
        {
            MLeader => TimberOwnedAnnotationEntityKind.MLeader,
            MText => TimberOwnedAnnotationEntityKind.MText,
            _ => TimberOwnedAnnotationEntityKind.Other,
        };

        var isBlockContent = entity is MLeader mLeaderBlock &&
            mLeaderBlock.ContentType == ContentType.BlockContent;
        var isMTextContent = entity is MLeader mLeaderText &&
            mLeaderText.ContentType == ContentType.MTextContent;

        return new TimberOwnedAnnotationComponentProbe
        {
            ComponentKey = entity.Handle.ToString(),
            SourceHandle = data.SourceHandle?.Trim() ?? string.Empty,
            ElementId = data.ElementId?.Trim() ?? string.Empty,
            AnnotationMode = data.AnnotationMode,
            ItemNumberLeaderStyle = data.ItemNumberLeaderStyle,
            ComponentRole = data.ComponentRole,
            RendererGeneration = data.RendererGeneration,
            EntityKind = entityKind,
            IsBlockContentMLeader = isBlockContent,
            IsMTextContentMLeader = isMTextContent,
        };
    }

    private static bool IsAuxiliaryAnnotation(Entity entity) =>
        SlopeArrowStore.TryRead(entity, out _) ||
        SlopeAngleTextStore.TryRead(entity, out _) ||
        PostFootprintPerpendicularAnnotationStore.TryRead(entity, out _);

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
}

internal sealed record AutoCadOwnedAnnotationAcceptedGroup(
    TimberOwnedAnnotationGroupSnapshot Snapshot,
    ObjectId SourceEntityId,
    ObjectId MainLeaderEntityId,
    IReadOnlyList<ObjectId> ComponentEntityIds);

internal sealed record AutoCadOwnedAnnotationSelectionResult(
    IReadOnlyList<AutoCadOwnedAnnotationAcceptedGroup> Accepted,
    IReadOnlyList<TimberOwnedAnnotationSkippedItem> Skipped,
    IReadOnlyList<TimberOwnedAnnotationRejectedGroup> Rejected);
