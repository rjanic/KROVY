using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Authoritative explicit desired-state materialization for automatic Hip/Valley
/// rafters. Ridge topology is intentionally not materialized by this subsystem.
/// </summary>
internal static class RoofAutomaticStructuralRafterMaterializationService
{
    private const double GeometryToleranceMm = 1e-7;

    public static RoofAutomaticStructuralRafterMaterializationResult Materialize(
        Document document,
        ObjectId ownerId)
    {
        ArgumentNullException.ThrowIfNull(document);
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var layerProfile = ElementLayerProfileStore.Load();
        try
        {
            using var documentLock = document.LockDocument();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    ownerId,
                    OpenMode.ForRead,
                    out var owner,
                    document.Database) ||
                owner is null)
            {
                return RoofAutomaticStructuralRafterMaterializationResult.Failure("owner-not-found");
            }

            var ownerReference = owner.Handle.ToString();
            var sourceInput = RoofPolylineExtractor.Extract(owner);
            var validation = RoofFootprintValidator.Validate(sourceInput);
            var storedDefinition = RoofDefinitionStore.Read(owner);
            if (!validation.IsValid ||
                validation.Footprint is null ||
                storedDefinition.Data is null)
            {
                return RoofAutomaticStructuralRafterMaterializationResult.Failure(
                    "invalid-roof-source",
                    ownerReference);
            }

            var restored = RoofDefinitionPersistence.Restore(
                sourceInput,
                validation.Footprint,
                storedDefinition.Data);
            if (!restored.IsValid || restored.Geometry is not HipRoofGeometry hipGeometry)
            {
                return RoofAutomaticStructuralRafterMaterializationResult.Failure(
                    "hip-geometry-required",
                    ownerReference);
            }

            var result = MaterializeInTransaction(
                document,
                transaction,
                owner,
                ownerReference,
                sourceInput,
                hipGeometry,
                defaultProfile,
                layerProfile);
            if (!result.IsSuccess)
            {
                return result;
            }

            transaction.Commit();
            return result;
        }
        catch (Exception ex)
        {
            return RoofAutomaticStructuralRafterMaterializationResult.Failure(
                ex.GetType().Name + ":" + ex.Message);
        }
    }

    public static RoofAutomaticStructuralRafterMaterializationResult MaterializeInTransaction(
        Document document,
        Transaction transaction,
        Polyline owner,
        string ownerReference,
        RoofFootprintInput sourceInput,
        HipRoofGeometry hipGeometry,
        TimberElementDefaultProfile defaultProfile,
        AcKrovy.Cad.Abstractions.Layers.ElementLayerProfile layerProfile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(sourceInput);
        ArgumentNullException.ThrowIfNull(hipGeometry);
        ArgumentNullException.ThrowIfNull(defaultProfile);
        ArgumentNullException.ThrowIfNull(layerProfile);
        if (!string.Equals(
                owner.Handle.ToString(),
                ownerReference,
                StringComparison.OrdinalIgnoreCase))
        {
            return RoofAutomaticStructuralRafterMaterializationResult.Failure(
                "owner-reference-mismatch",
                ownerReference);
        }

        var boundaryIdentityResult = RoofBoundaryIdentityService.EnsureBoundaryIdentity(
            document.Database,
            transaction,
            owner.ObjectId);
        if (boundaryIdentityResult.Identity is null)
        {
            return RoofAutomaticStructuralRafterMaterializationResult.Failure(
                "boundary-identity-" + boundaryIdentityResult.Error,
                ownerReference);
        }

        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(
            sourceInput,
            boundaryIdentityResult.Identity);
        var structuralResolution = RoofStructuralEdgeIdentityResolver.Resolve(
            hipGeometry,
            provenance);
        var plan = RoofAutomaticStructuralRafterPlanner.Create(
            structuralResolution,
            defaultProfile);
        if (!plan.IsValid)
        {
            return RoofAutomaticStructuralRafterMaterializationResult.Failure(
                "automatic-structural-rafter-plan-" + plan.Error,
                ownerReference);
        }

        return Reconcile(
            document,
            transaction,
            owner,
            ownerReference,
            RoofPolylineExtractor.GetSourceElevation(owner),
            plan.Items,
            defaultProfile,
            layerProfile);
    }

    private static RoofAutomaticStructuralRafterMaterializationResult Reconcile(
        Document document,
        Transaction transaction,
        Polyline owner,
        string ownerReference,
        double sourceElevation,
        IReadOnlyList<RoofAutomaticStructuralRafterPlanItem> desired,
        TimberElementDefaultProfile defaultProfile,
        AcKrovy.Cad.Abstractions.Layers.ElementLayerProfile layerProfile)
    {
        var database = document.Database;
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var existingIds = RoofStructuralGeneratedStore.FindByOwner(
            database,
            transaction,
            ownerReference);
        var desiredByKey = desired.ToDictionary(item => item.LogicalKey);
        var candidatesByKey = new Dictionary<RoofStructuralLogicalKey, List<ExistingMember>>();
        var staleIds = new HashSet<ObjectId>();
        foreach (var id in existingIds)
        {
            if (!TryReadExisting(
                    database,
                    transaction,
                    metadataStore,
                    id,
                    out var existing) ||
                existing is null ||
                !desiredByKey.ContainsKey(existing.StructuralData.LogicalKey))
            {
                staleIds.Add(id);
                continue;
            }

            if (!candidatesByKey.TryGetValue(existing.StructuralData.LogicalKey, out var bucket))
            {
                bucket = [];
                candidatesByKey.Add(existing.StructuralData.LogicalKey, bucket);
            }

            bucket.Add(existing);
        }

        var survivorByKey = new Dictionary<RoofStructuralLogicalKey, ExistingMember>();
        foreach (var (key, bucket) in candidatesByKey)
        {
            var survivor = bucket.OrderBy(item => item.Id.Handle.Value).First();
            survivorByKey.Add(key, survivor);
            foreach (var duplicate in bucket.Where(item => item.Id != survivor.Id))
            {
                staleIds.Add(duplicate.Id);
            }
        }

        if (staleIds.Count > 0)
        {
            _ = RoofAssemblyGroupSyncService.DetachMembersBeforeErase(
                database,
                transaction,
                owner.ObjectId,
                staleIds);
            foreach (var staleId in staleIds)
            {
                if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        staleId,
                        OpenMode.ForWrite,
                        out var stale,
                        database) &&
                    stale is not null &&
                    !stale.IsErased)
                {
                    stale.Erase();
                }
            }
        }

        var elementIdCounts = ReadElementIdCounts(database, transaction, metadataStore);
        var nextNumberByType = new Dictionary<TimberElementType, int>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);
        var layerService = new AutoCadTimberLayerService(
            database,
            transaction,
            document.Editor);
        var members = new List<RoofAutomaticStructuralRafterMemberResult>(desired.Count);
        var createdCount = 0;
        var existingCount = 0;
        var updatedCount = 0;

        foreach (var item in desired)
        {
            var structuralData = RoofStructuralGeneratedDataRules.Create(
                ownerReference,
                item.LogicalKey.Role,
                item.LogicalKey.BoundaryEdgeIdA,
                item.LogicalKey.BoundaryEdgeIdB).Data
                ?? throw new InvalidOperationException("Structural metadata could not be created.");
            var start = MapPoint(item.Segment3D.Start, sourceElevation);
            var end = MapPoint(item.Segment3D.End, sourceElevation);
            if (survivorByKey.TryGetValue(item.LogicalKey, out var existing))
            {
                existingCount++;
                var elementId = ResolveElementId(
                    database,
                    transaction,
                    item.ElementType,
                    existing.TimberData.ElementId,
                    elementIdCounts,
                    nextNumberByType);
                var timberData = item.TimberData with { ElementId = elementId };
                var geometryChanged = !SamePoint(existing.Line.StartPoint, start) ||
                    !SamePoint(existing.Line.EndPoint, end);
                var metadataChanged = existing.TimberData != timberData ||
                    existing.StructuralData != structuralData;
                if (geometryChanged || metadataChanged)
                {
                    if (!existing.Line.IsWriteEnabled)
                    {
                        existing.Line.UpgradeOpen();
                    }

                    existing.Line.StartPoint = start;
                    existing.Line.EndPoint = end;
                    metadataStore.Write(existing.Line, timberData);
                    RoofStructuralGeneratedStore.Write(
                        existing.Line,
                        transaction,
                        structuralData);
                    layerService.ApplyLayerForTimberType(
                        existing.Line,
                        item.ElementType,
                        layerProfile,
                        AcKrovy.Cad.Abstractions.Layers.CadLayerUpdateMode.PreserveExisting);
                    updatedCount++;
                }

                members.Add(new RoofAutomaticStructuralRafterMemberResult(
                    item.LogicalKey,
                    existing.Line.Handle.ToString(),
                    elementId,
                    item.ElementType,
                    item.True3DLengthMm,
                    geometryChanged || metadataChanged ? "updated" : "existing"));
                continue;
            }

            var newElementId = ResolveElementId(
                database,
                transaction,
                item.ElementType,
                string.Empty,
                elementIdCounts,
                nextNumberByType);
            var newTimberData = item.TimberData with { ElementId = newElementId };
            var line = new Line(start, end);
            var id = modelSpace.AppendEntity(line);
            RoofStructuralGeneratedStore.WriteAtomic(
                line,
                transaction,
                newTimberData,
                structuralData);
            transaction.AddNewlyCreatedDBObject(line, true);
            layerService.ApplyLayerForTimberType(
                line,
                item.ElementType,
                layerProfile,
                AcKrovy.Cad.Abstractions.Layers.CadLayerUpdateMode.PreserveExisting);
            createdCount++;
            members.Add(new RoofAutomaticStructuralRafterMemberResult(
                item.LogicalKey,
                id.Handle.ToString(),
                newElementId,
                item.ElementType,
                item.True3DLengthMm,
                "created"));
        }

        if (!RoofAssemblyGroupSyncService.TrySyncForOwner(
                document,
                transaction,
                owner.ObjectId) ||
            !RoofDisplayService.TryCollectCurrentStructuralDisplayChildIds(
                database,
                transaction,
                owner,
                out var displayIds))
        {
            return RoofAutomaticStructuralRafterMaterializationResult.Failure(
                "group-sync-failed",
                ownerReference);
        }

        var actualIds = RoofStructuralGeneratedStore.FindByOwner(
            database,
            transaction,
            ownerReference);
        var actualKeys = actualIds
            .Select(id => transaction.GetObject(id, OpenMode.ForRead, false) as Entity)
            .Where(entity => entity is not null)
            .Select(entity => RoofStructuralGeneratedStore.Read(entity!).Data?.LogicalKey)
            .Where(key => key is not null)
            .Cast<RoofStructuralLogicalKey>()
            .ToArray();
        var duplicates = actualKeys.Length - actualKeys.Distinct().Count();
        var actualKeySet = actualKeys.ToHashSet();
        var missing = desired.Count(item => !actualKeySet.Contains(item.LogicalKey));
        var groupCanonical = RoofDisplayGroupService.Inspect(
            database,
            transaction,
            owner.ObjectId,
            displayIds).IsCurrent;
        var success = actualIds.Count == desired.Count &&
            duplicates == 0 &&
            missing == 0 &&
            groupCanonical &&
            VerifyMembers(database, transaction, metadataStore, actualIds, desiredByKey);

        return new RoofAutomaticStructuralRafterMaterializationResult(
            success,
            ownerReference,
            desired.Count,
            actualIds.Count,
            createdCount,
            existingCount,
            updatedCount,
            duplicates,
            missing,
            groupCanonical,
            success ? "ok" : "postcondition-failed",
            members);
    }

    private static bool TryReadExisting(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        ObjectId id,
        out ExistingMember? existing)
    {
        existing = null;
        if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                transaction,
                id,
                OpenMode.ForRead,
                out var line,
                database) ||
            line is null ||
            RoofGeneratedTimberStore.Read(line).Exists ||
            RoofAttachedManualTimberStore.Read(line).Exists ||
            !metadataStore.TryRead(line, out var timberData) ||
            timberData is null)
        {
            return false;
        }

        var structural = RoofStructuralGeneratedStore.Read(line).Data;
        if (structural is null ||
            !RoofAutomaticStructuralRafterPlanner.TryMapType(
                structural.StructuralRole,
                out var expectedType) ||
            timberData.ElementType != expectedType)
        {
            return false;
        }

        existing = new ExistingMember(id, line, timberData, structural);
        return true;
    }

    private static Dictionary<string, int> ReadElementIdCounts(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in DrawingScanner.FindAllTimberElements(database, transaction, metadataStore))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                !metadataStore.TryRead(entity, out var data) ||
                data is null ||
                string.IsNullOrWhiteSpace(data.ElementId))
            {
                continue;
            }

            var key = data.ElementId.Trim();
            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private static string ResolveElementId(
        Database database,
        Transaction transaction,
        TimberElementType type,
        string currentElementId,
        IDictionary<string, int> elementIdCounts,
        IDictionary<TimberElementType, int> nextNumberByType)
    {
        var parsed = TimberElementIdentityRules.TryParseElementNumber(currentElementId, type);
        if (parsed is > 0 &&
            elementIdCounts.TryGetValue(currentElementId.Trim(), out var count) &&
            count == 1)
        {
            return TimberElementIdentityRules.CreateElementId(type, parsed.Value);
        }

        if (!nextNumberByType.TryGetValue(type, out var nextNumber))
        {
            nextNumber = ElementNumberingService.GetNextNumber(database, transaction, type);
        }

        string candidate;
        do
        {
            candidate = TimberElementIdentityRules.CreateElementId(type, nextNumber++);
        }
        while (elementIdCounts.ContainsKey(candidate));

        nextNumberByType[type] = nextNumber;
        elementIdCounts[candidate] = 1;
        return candidate;
    }

    private static bool VerifyMembers(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        IReadOnlyList<ObjectId> ids,
        IReadOnlyDictionary<RoofStructuralLogicalKey, RoofAutomaticStructuralRafterPlanItem> desired)
    {
        var elementIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var line,
                    database) ||
                line is null ||
                RoofGeneratedTimberStore.Read(line).Exists ||
                RoofAttachedManualTimberStore.Read(line).Exists ||
                !metadataStore.TryRead(line, out var timber) ||
                timber is null ||
                timber.SchemaVersion != TimberElementDataSchema.CurrentVersion ||
                TimberAnnotationModeRules.Normalize(timber.AnnotationMode) !=
                    TimberAnnotationMode.NoAnnotations ||
                !elementIds.Add(timber.ElementId))
            {
                return false;
            }

            var structural = RoofStructuralGeneratedStore.Read(line).Data;
            if (structural is null ||
                structural.SchemaVersion != RoofStructuralGeneratedDataSchema.CurrentVersion ||
                !desired.TryGetValue(structural.LogicalKey, out var item) ||
                item.ElementType != timber.ElementType ||
                TimberElementIdentityRules.TryParseElementNumber(
                    timber.ElementId,
                    timber.ElementType) is not > 0 ||
                Math.Abs(line.Length - item.True3DLengthMm) > GeometryToleranceMm)
            {
                return false;
            }
        }

        return true;
    }

    private static Point3d MapPoint(RoofPoint3D point, double sourceElevation) =>
        new(point.X, point.Y, point.Z + sourceElevation);

    private static bool SamePoint(Point3d first, Point3d second) =>
        first.DistanceTo(second) <= GeometryToleranceMm;

    private sealed record ExistingMember(
        ObjectId Id,
        Line Line,
        TimberElementData TimberData,
        RoofStructuralGeneratedData StructuralData);
}

internal sealed record RoofAutomaticStructuralRafterMemberResult(
    RoofStructuralLogicalKey LogicalKey,
    string Handle,
    string ElementId,
    TimberElementType ElementType,
    double True3DLengthMm,
    string Result);

internal sealed record RoofAutomaticStructuralRafterMaterializationResult(
    bool IsSuccess,
    string OwnerReference,
    int Expected,
    int Actual,
    int Created,
    int Existing,
    int Updated,
    int Duplicates,
    int Missing,
    bool GroupCanonical,
    string Result,
    IReadOnlyList<RoofAutomaticStructuralRafterMemberResult> Members)
{
    public static RoofAutomaticStructuralRafterMaterializationResult Failure(
        string result,
        string ownerReference = "-") => new(
            false,
            ownerReference,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            result,
            Array.Empty<RoofAutomaticStructuralRafterMemberResult>());
}
