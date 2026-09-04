using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class RoofExternalImportSanitizationService
{
    public static RoofExternalImportSanitizationResult Apply(
        Transaction transaction,
        RoofExternalImportOperationFacts operation)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.CloneContext != DeepCloneType.InsertCopy)
        {
            throw new InvalidOperationException("Only InsertCopy is supported by C1.");
        }

        var manifestIds = operation.Manifest.SourceObjectIds.ToHashSet();
        var mapping = operation.Mapping
            .GroupBy(pair => pair.SourceId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var sanitized = new List<ObjectId>();
        var erased = new List<ObjectId>();

        foreach (var source in operation.Manifest.IndividualTimbers)
        {
            var pair = RequireSingleMappedNew(source.SourceId, mapping, manifestIds, operation.TargetDatabase);
            if (transaction.GetObject(pair.DestinationId, OpenMode.ForWrite, false) is not Entity entity ||
                entity.IsErased || !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity))
            {
                throw new InvalidOperationException("Mapped timber destination is unavailable or unsupported.");
            }

            if (source.Role == RoofExternalImportObjectRole.Generated &&
                !RoofGeneratedTimberStore.TryClear(entity, transaction, out var generatedFailure))
            {
                throw new InvalidOperationException("Generated sanitation failed: " + generatedFailure);
            }

            if (source.Role == RoofExternalImportObjectRole.AttachedManual &&
                !RoofAttachedManualTimberStore.TryClear(entity, transaction, out var attachedFailure))
            {
                throw new InvalidOperationException("AttachedManual sanitation failed: " + attachedFailure);
            }

            if (!ElementDataStore.TryClear(entity, transaction, out var genericFailure))
            {
                throw new InvalidOperationException("Generic timber sanitation failed: " + genericFailure);
            }

            entity.Visible = true;
            VerifyPlain(entity, transaction);
            sanitized.Add(pair.DestinationId);
        }

        foreach (var source in operation.Manifest.Annotations)
        {
            var pair = RequireSingleMappedNew(source.SourceId, mapping, manifestIds, operation.TargetDatabase);
            if (transaction.GetObject(pair.DestinationId, OpenMode.ForWrite, false) is not Entity annotation ||
                annotation.IsErased)
            {
                throw new InvalidOperationException("Mapped annotation destination is unavailable.");
            }

            annotation.Erase();
            erased.Add(pair.DestinationId);
        }

        return new(sanitized.ToArray(), erased.ToArray());
    }

    public static void Verify(
        Transaction transaction,
        RoofExternalImportSanitizationResult result)
    {
        foreach (var id in result.SanitizedTimberIds)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity || entity.IsErased)
            {
                throw new InvalidOperationException("Sanitized timber is unavailable before commit.");
            }

            VerifyPlain(entity, transaction);
        }

        foreach (var id in result.ErasedAnnotationIds)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not Entity entity || !entity.IsErased)
            {
                throw new InvalidOperationException("Imported annotation was not erased before commit.");
            }
        }
    }

    private static RoofExternalImportIdPair RequireSingleMappedNew(
        ObjectId sourceId,
        IReadOnlyDictionary<ObjectId, RoofExternalImportIdPair[]> mapping,
        HashSet<ObjectId> manifestIds,
        Database targetDatabase)
    {
        if (!mapping.TryGetValue(sourceId, out var pairs) || pairs.Length != 1)
        {
            throw new InvalidOperationException("Safety-critical source object has no unique mapping pair.");
        }

        var pair = pairs[0];
        var authority = new RoofExternalImportMappingFacts(
            manifestIds.Contains(sourceId),
            true,
            pair.IsCloned,
            !pair.DestinationId.IsNull && pair.DestinationId.Database == targetDatabase);
        if (!RoofExternalImportPolicy.MayMutate(authority))
        {
            throw new InvalidOperationException(
                pair.IsCloned
                    ? "Mapped destination is outside the exact target database."
                    : "Intelligent source object was mapped-reused; existing target objects are immutable.");
        }

        return pair;
    }

    private static void VerifyPlain(Entity entity, Transaction transaction)
    {
        var generated = RoofGeneratedTimberStore.Read(entity);
        var attached = RoofAttachedManualTimberStore.Read(entity);
        if (generated.Exists || attached.Exists ||
            ElementDataStore.TryRead(entity, transaction, out _))
        {
            throw new InvalidOperationException("KROVY intelligence survived mapped-new sanitation.");
        }
    }
}

internal sealed record RoofExternalImportSanitizationResult(
    ObjectId[] SanitizedTimberIds,
    ObjectId[] ErasedAnnotationIds);
