using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// CommandEnded-only inspection of the exact Entity ObjectIds appended by one
/// clipboard paste. ObjectAppended-time XData is observational only; this final
/// read transaction is the authoritative payload boundary.
/// </summary>
internal static class RoofClipboardPastePayloadInspectionService
{
    public static RoofClipboardPastePayloadSnapshot Inspect(
        Document document,
        IReadOnlyCollection<ObjectId> appendedEntityIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(appendedEntityIds);

        var exactIds = appendedEntityIds
            .Where(id => !id.IsNull && id.Database == document.Database)
            .Distinct()
            .ToArray();
        var roofOwnerIds = new List<ObjectId>();
        var intelligentTimberIds = new List<ObjectId>();
        var genericTimberIds = new List<ObjectId>();
        var annotationIds = new List<ObjectId>();

        using var transaction = document.Database.TransactionManager.StartTransaction();
        foreach (var id in exactIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    document.Database) ||
                entity is null ||
                entity.IsErased)
            {
                continue;
            }

            if (entity is Polyline && RoofDefinitionStore.Read(entity).Exists)
            {
                roofOwnerIds.Add(id);
            }

            if (entity is Line &&
                (RoofGeneratedTimberStore.Read(entity).Exists ||
                 RoofAttachedManualTimberStore.Read(entity).Exists))
            {
                intelligentTimberIds.Add(id);
            }

            if (ElementDataStore.TryRead(entity, transaction, out _))
            {
                genericTimberIds.Add(id);
            }

            if (ElementLabelStore.TryRead(entity, out _) ||
                SlopeArrowStore.TryRead(entity, out _) ||
                SlopeAngleTextStore.TryRead(entity, out _) ||
                PostFootprintPerpendicularAnnotationStore.TryRead(entity, out _))
            {
                annotationIds.Add(id);
            }
        }

        return new RoofClipboardPastePayloadSnapshot(
            exactIds,
            roofOwnerIds,
            intelligentTimberIds,
            genericTimberIds,
            annotationIds);
    }
}

internal sealed record RoofClipboardPastePayloadSnapshot(
    IReadOnlyList<ObjectId> AppendedEntityIds,
    IReadOnlyList<ObjectId> RoofOwnerIds,
    IReadOnlyList<ObjectId> IntelligentTimberIds,
    IReadOnlyList<ObjectId> GenericTimberIds,
    IReadOnlyList<ObjectId> AnnotationIds);
