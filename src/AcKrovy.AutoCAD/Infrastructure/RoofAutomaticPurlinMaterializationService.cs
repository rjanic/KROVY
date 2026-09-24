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
/// Explicit desired-state materialization for owner-scoped automatic Purlins.
/// It has no command/event lifecycle registration; callers own the transaction.
/// </summary>
internal static class RoofAutomaticPurlinMaterializationService
{
    private const double GeometryToleranceMm = 1e-7;

    public static RoofAutomaticPurlinMaterializationResult MaterializePersistedLayoutInTransaction(
        Document document,
        Transaction transaction,
        Polyline owner,
        TimberElementDefaultProfile defaultProfile,
        AcKrovy.Cad.Abstractions.Layers.ElementLayerProfile layerProfile)
    {
        var storedLayout = RoofPurlinLayoutStore.Read(owner);
        if (!storedLayout.Exists)
        {
            return RoofAutomaticPurlinMaterializationResult.Failure(
                "NoLayout",
                owner.Handle.ToString());
        }

        if (storedLayout.Data is null)
        {
            return RoofAutomaticPurlinMaterializationResult.Failure(
                "layout-" + storedLayout.Error,
                owner.Handle.ToString());
        }

        var storedDatum = RoofRelativeElevationDatumStore.Read(owner);
        if (!storedDatum.Exists)
        {
            return RoofAutomaticPurlinMaterializationResult.Failure(
                "NoRelativeElevationDatum",
                owner.Handle.ToString());
        }

        if (storedDatum.Data is null)
        {
            return RoofAutomaticPurlinMaterializationResult.Failure(
                "relative-elevation-datum-" + storedDatum.Error,
                owner.Handle.ToString());
        }

        return MaterializeInTransaction(
            document,
            transaction,
            owner,
            storedLayout.Data,
            storedDatum.Data,
            defaultProfile,
            layerProfile,
            includeWallPlates: storedLayout.Data.WallPlateEnabled);
    }

    public static RoofAutomaticPurlinMaterializationResult MaterializeInTransaction(
        Document document,
        Transaction transaction,
        Polyline owner,
        RoofAutomaticPurlinLayout layout,
        RoofRelativeElevationDatum relativeElevationDatum,
        TimberElementDefaultProfile defaultProfile,
        AcKrovy.Cad.Abstractions.Layers.ElementLayerProfile layerProfile,
        bool includeWallPlates = false,
        bool syncAssemblyGroup = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(relativeElevationDatum);
        ArgumentNullException.ThrowIfNull(defaultProfile);
        ArgumentNullException.ThrowIfNull(layerProfile);

        var preparation = PrepareInTransaction(
            document,
            transaction,
            owner,
            out var preparationFailure);
        if (preparation is null)
        {
            return RoofAutomaticPurlinMaterializationResult.Failure(
                preparationFailure,
                owner.Handle.ToString());
        }

        return MaterializePreparedInTransaction(
            document,
            transaction,
            owner,
            preparation,
            layout,
            relativeElevationDatum,
            defaultProfile,
            layerProfile,
            includeWallPlates: includeWallPlates,
            syncAssemblyGroup: syncAssemblyGroup);
    }

    public static RoofAutomaticPurlinMaterializationPreparation? PrepareInTransaction(
        Document document,
        Transaction transaction,
        Polyline owner,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(owner);

        var ownerReference = owner.Handle.ToString();
        var sourceInput = RoofPolylineExtractor.Extract(owner);
        var sourceValidation = RoofFootprintValidator.Validate(sourceInput);
        var storedDefinition = RoofDefinitionStore.Read(owner);
        if (!sourceValidation.IsValid ||
            sourceValidation.Footprint is null ||
            storedDefinition.Data is null)
        {
            failure = "invalid-roof-source";
            return null;
        }

        var restored = RoofDefinitionPersistence.Restore(
            sourceInput,
            sourceValidation.Footprint,
            storedDefinition.Data);
        if (!restored.IsValid || restored.Geometry is not HipRoofGeometry hipGeometry)
        {
            failure = "hip-geometry-required";
            return null;
        }

        var boundaryIdentity = RoofBoundaryIdentityService.EnsureBoundaryIdentity(
            document.Database,
            transaction,
            owner.ObjectId);
        if (boundaryIdentity.Identity is null)
        {
            failure = "boundary-identity-" + boundaryIdentity.Error;
            return null;
        }

        failure = string.Empty;
        return new RoofAutomaticPurlinMaterializationPreparation(
            ownerReference,
            sourceInput,
            hipGeometry,
            boundaryIdentity.Identity,
            RoofPolylineExtractor.GetSourceElevation(owner));
    }

    /// <summary>
    /// Read-only production preflight for the exact owner-scoped automatic-Purlin set.
    /// It rejects malformed children and duplicate generated keys before a dialog can
    /// offer Apply; reconciliation repeats the same checks inside the write transaction.
    /// </summary>
    public static RoofAutomaticPurlinOwnerStateInspection InspectExistingOwnerStateInTransaction(
        Database database,
        Transaction transaction,
        Polyline owner)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(owner);

        var ownerReference = owner.Handle.ToString();
        var scan = RoofAutomaticPurlinGeneratedStore.ScanForOwner(
            database,
            transaction,
            ownerReference);
        if (scan.MalformedIds.Count > 0)
        {
            return RoofAutomaticPurlinOwnerStateInspection.Failure(
                "malformed-automatic-purlin-generated-metadata");
        }

        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var existing = new List<RoofAutomaticPurlinExistingMember>(scan.MatchingIds.Count);
        foreach (var id in scan.MatchingIds)
        {
            if (!TryReadExisting(
                    database,
                    transaction,
                    metadataStore,
                    id,
                    ownerReference,
                    out var member) ||
                member is null)
            {
                return RoofAutomaticPurlinOwnerStateInspection.Failure(
                    "invalid-automatic-purlin-child");
            }

            existing.Add(new RoofAutomaticPurlinExistingMember(
                member.Id.Handle.ToString(),
                member.GeneratedData.GeneratedKey,
                member.TimberData.ElementId));
        }

        var decision = RoofAutomaticPurlinMaterializationRules.Reconcile(
            Array.Empty<RoofAutomaticPurlinPlanItem>(),
            existing);
        return decision.IsValid
            ? RoofAutomaticPurlinOwnerStateInspection.Success(existing.Count)
            : RoofAutomaticPurlinOwnerStateInspection.Failure(
                "automatic-purlin-reconcile-" + decision.Error,
                decision.Error == RoofAutomaticPurlinReconciliationError.DuplicateExistingKey
                    ? 1
                    : 0);
    }

    public static RoofAutomaticPurlinMaterializationResult MaterializePreparedInTransaction(
        Document document,
        Transaction transaction,
        Polyline owner,
        RoofAutomaticPurlinMaterializationPreparation preparation,
        RoofAutomaticPurlinLayout layout,
        RoofRelativeElevationDatum relativeElevationDatum,
        TimberElementDefaultProfile defaultProfile,
        AcKrovy.Cad.Abstractions.Layers.ElementLayerProfile layerProfile,
        RoofAutomaticPurlinPlan? expectedPreviewPlan = null,
        bool includeWallPlates = false,
        double? authoritativeRafterHeightMm = null,
        bool syncAssemblyGroup = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(relativeElevationDatum);
        ArgumentNullException.ThrowIfNull(defaultProfile);
        ArgumentNullException.ThrowIfNull(layerProfile);
        if (!string.Equals(
                owner.Handle.ToString(),
                preparation.OwnerReference,
                StringComparison.OrdinalIgnoreCase))
        {
            return RoofAutomaticPurlinMaterializationResult.Failure(
                "owner-reference-mismatch",
                preparation.OwnerReference);
        }

        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(
            preparation.SourceInput,
            preparation.BoundaryIdentity);
        var purlinDefaults = TimberElementDefaults.For(
            TimberElementType.Purlin,
            defaultProfile);
        var wallPlateDefaults = TimberElementDefaults.For(
            TimberElementType.WallPlate,
            defaultProfile);
        double rafterHeightMm;
        if (authoritativeRafterHeightMm is { } explicitHeight)
        {
            if (!double.IsFinite(explicitHeight) || explicitHeight <= 0d)
            {
                return RoofAutomaticPurlinMaterializationResult.Failure(
                    "invalid-authoritative-rafter-height",
                    preparation.OwnerReference);
            }

            rafterHeightMm = explicitHeight;
        }
        else if (!RoofAutomaticPurlinPlanningRafterResolver.TryResolve(
                document.Database,
                transaction,
                preparation.OwnerReference,
                layout,
                defaultProfile,
                out var planningRafter,
                out var rafterFailure,
                document.Editor))
        {
            return RoofAutomaticPurlinMaterializationResult.Failure(
                rafterFailure,
                preparation.OwnerReference);
        }
        else
        {
            rafterHeightMm = planningRafter.HeightMm;
        }

        var planned = RoofAutomaticPurlinPlanner.Create(
            preparation.HipGeometry,
            provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(
                relativeElevationDatum,
                purlinDefaults.HeightMm,
                rafterHeightMm)
            {
                PurlinWidthMm = purlinDefaults.WidthMm,
                WallPlatesEnabled = includeWallPlates,
                WallPlateWidthMm = wallPlateDefaults.WidthMm,
                WallPlateHeightMm = wallPlateDefaults.HeightMm,
            });
        if (!planned.IsValid || planned.Plan is null)
        {
            return RoofAutomaticPurlinMaterializationResult.Failure(
                "automatic-purlin-plan-" + planned.Error,
                preparation.OwnerReference);
        }

        if (expectedPreviewPlan is not null &&
            !PlansEquivalent(expectedPreviewPlan, planned.Plan))
        {
            return RoofAutomaticPurlinMaterializationResult.Failure(
                "preview-production-plan-mismatch",
                preparation.OwnerReference);
        }

        return Reconcile(
            document,
            transaction,
            owner,
            preparation.OwnerReference,
            preparation.SourceElevation,
            planned.Plan.Items,
            defaultProfile,
            layerProfile,
            syncAssemblyGroup: syncAssemblyGroup);
    }

    private static RoofAutomaticPurlinMaterializationResult Reconcile(
        Document document,
        Transaction transaction,
        Polyline owner,
        string ownerReference,
        double sourceElevation,
        IReadOnlyList<RoofAutomaticPurlinPlanItem> desired,
        TimberElementDefaultProfile defaultProfile,
        AcKrovy.Cad.Abstractions.Layers.ElementLayerProfile layerProfile,
        bool syncAssemblyGroup = true)
    {
        var database = document.Database;
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var scan = RoofAutomaticPurlinGeneratedStore.ScanForOwner(
            database,
            transaction,
            ownerReference);
        if (scan.MalformedIds.Count > 0)
        {
            return RoofAutomaticPurlinMaterializationResult.Failure(
                "malformed-automatic-purlin-generated-metadata",
                ownerReference);
        }

        var existingMembers = new List<ExistingMember>(scan.MatchingIds.Count);
        foreach (var id in scan.MatchingIds)
        {
            if (!TryReadExisting(
                    database,
                    transaction,
                    metadataStore,
                    id,
                    ownerReference,
                    out var existing) ||
                existing is null)
            {
                return RoofAutomaticPurlinMaterializationResult.Failure(
                    "invalid-automatic-purlin-child",
                    ownerReference);
            }

            existingMembers.Add(existing);
        }

        var decision = RoofAutomaticPurlinMaterializationRules.Reconcile(
            desired,
            existingMembers.Select(member => new RoofAutomaticPurlinExistingMember(
                member.Id.Handle.ToString(),
                member.GeneratedData.GeneratedKey,
                member.TimberData.ElementId)).ToArray());
        if (!decision.IsValid || decision.Plan is null)
        {
            return RoofAutomaticPurlinMaterializationResult.Failure(
                "automatic-purlin-reconcile-" + decision.Error,
                ownerReference,
                duplicates: decision.Error ==
                    RoofAutomaticPurlinReconciliationError.DuplicateExistingKey ? 1 : 0);
        }

        var existingByToken = existingMembers.ToDictionary(
            member => member.Id.Handle.ToString(),
            StringComparer.OrdinalIgnoreCase);
        var existingByKey = existingMembers.ToDictionary(
            member => member.GeneratedData.GeneratedKey);
        var ownersByElementId = ReadElementIdOwners(database, transaction, metadataStore);
        var existingForDesired = new List<ExistingMember?>(desired.Count);
        foreach (var item in desired)
        {
            existingByKey.TryGetValue(item.GeneratedKey, out var existing);
            existingForDesired.Add(existing);
        }

        var assignedElementIds = AssignElementIds(
            desired,
            existingForDesired,
            ownersByElementId);
        var prepared = new List<PreparedMember>(desired.Count);
        for (var index = 0; index < desired.Count; index++)
        {
            var item = desired[index];
            var existing = existingForDesired[index];
            var timberData = RoofAutomaticPurlinMaterializationRules.CreateTimberData(
                defaultProfile,
                item,
                assignedElementIds[index]);
            var generated = RoofAutomaticPurlinGeneratedDataRules.Create(
                ownerReference,
                item.GeneratedKey).Data;
            var start = MapPoint(item.Segment3D.Start, sourceElevation);
            var end = MapPoint(item.Segment3D.End, sourceElevation);
            if (generated is null ||
                !RoofAutomaticPurlinMaterializationRules.IsValidDesiredItem(item) ||
                Math.Abs(start.Z - end.Z) > GeometryToleranceMm ||
                Math.Abs(start.DistanceTo(end) - item.LengthMm) > GeometryToleranceMm)
            {
                return RoofAutomaticPurlinMaterializationResult.Failure(
                    "invalid-planned-member",
                    ownerReference);
            }

            var layerStyle = layerProfile.GetStyle(item.ElementType);

            prepared.Add(new PreparedMember(
                item,
                existing,
                start,
                end,
                timberData,
                generated,
                layerStyle.LayerName,
                layerStyle.LinetypeScale));
        }

        var staleIds = decision.Plan.Stale
            .Select(member => existingByToken[member.EntityToken].Id)
            .ToArray();
        if (staleIds.Length > 0)
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

        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);
        var layerService = new AutoCadTimberLayerService(
            database,
            transaction,
            document.Editor);
        var members = new List<RoofAutomaticPurlinMemberResult>(prepared.Count);
        var createdCount = 0;
        var existingCount = 0;
        var updatedCount = 0;
        foreach (var member in prepared)
        {
            if (member.Existing is not null)
            {
                var geometryChanged =
                    !SamePoint(member.Existing.Line.StartPoint, member.Start) ||
                    !SamePoint(member.Existing.Line.EndPoint, member.End);
                var metadataChanged =
                    member.Existing.TimberData != member.TimberData ||
                    member.Existing.GeneratedData != member.GeneratedData;
                var layerChanged =
                    !string.Equals(
                        member.Existing.Line.Layer,
                        member.ExpectedLayerName,
                        StringComparison.OrdinalIgnoreCase) ||
                    member.Existing.Line.ColorIndex != 256 ||
                    member.Existing.Line.LinetypeId != database.ByLayerLinetype ||
                    Math.Abs(
                        member.Existing.Line.LinetypeScale -
                        member.ExpectedLinetypeScale) > GeometryToleranceMm;
                if (geometryChanged || metadataChanged || layerChanged)
                {
                    if (!member.Existing.Line.IsWriteEnabled)
                    {
                        member.Existing.Line.UpgradeOpen();
                    }

                    if (geometryChanged)
                    {
                        member.Existing.Line.StartPoint = member.Start;
                        member.Existing.Line.EndPoint = member.End;
                    }

                    if (metadataChanged)
                    {
                        RoofAutomaticPurlinGeneratedStore.WriteAtomic(
                            member.Existing.Line,
                            transaction,
                            member.TimberData,
                            member.GeneratedData);
                    }

                    if (layerChanged)
                    {
                        layerService.ApplyLayerForTimberType(
                            member.Existing.Line,
                            member.Item.ElementType,
                            layerProfile,
                            AcKrovy.Cad.Abstractions.Layers.CadLayerUpdateMode.PreserveExisting);
                    }

                    updatedCount++;
                }
                else
                {
                    existingCount++;
                }

                members.Add(CreateMemberResult(
                    member,
                    member.Existing.Line.Handle.ToString(),
                    geometryChanged || metadataChanged || layerChanged
                        ? "updated"
                        : "existing"));
                continue;
            }

            var line = new Line(member.Start, member.End);
            var id = modelSpace.AppendEntity(line);
            RoofAutomaticPurlinGeneratedStore.WriteAtomic(
                line,
                transaction,
                member.TimberData,
                member.GeneratedData);
            transaction.AddNewlyCreatedDBObject(line, true);
            layerService.ApplyLayerForTimberType(
                line,
                member.Item.ElementType,
                layerProfile,
                AcKrovy.Cad.Abstractions.Layers.CadLayerUpdateMode.PreserveExisting);
            createdCount++;
            members.Add(CreateMemberResult(member, id.Handle.ToString(), "created"));
        }

        var groupCanonical = true;
        if (syncAssemblyGroup)
        {
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
                return RoofAutomaticPurlinMaterializationResult.Failure(
                    "group-sync-failed",
                    ownerReference);
            }

            groupCanonical = RoofDisplayGroupService.Inspect(
                database,
                transaction,
                owner.ObjectId,
                displayIds).IsCurrent;
        }

        var postScan = RoofAutomaticPurlinGeneratedStore.ScanForOwner(
            database,
            transaction,
            ownerReference);
        var actualKeys = postScan.MatchingIds
            .Select(id => transaction.GetObject(id, OpenMode.ForRead, false) as Entity)
            .Where(entity => entity is not null)
            .Select(entity => RoofAutomaticPurlinGeneratedStore.Read(entity!).Data?.GeneratedKey)
            .Where(key => key is not null)
            .Cast<RoofAutomaticPurlinGeneratedKey>()
            .ToArray();
        var duplicates = actualKeys.Length - actualKeys.Distinct().Count();
        var actualKeySet = actualKeys.ToHashSet();
        var missing = desired.Count(item => !actualKeySet.Contains(item.GeneratedKey));
        var expectedByKey = prepared.ToDictionary(item => item.Item.GeneratedKey);
        var success =
            postScan.MalformedIds.Count == 0 &&
            postScan.MatchingIds.Count == desired.Count &&
            duplicates == 0 &&
            missing == 0 &&
            groupCanonical &&
            VerifyMembers(
                database,
                transaction,
                metadataStore,
                postScan.MatchingIds,
                expectedByKey) &&
            VerifyNoOwnedAnnotations(
                database,
                transaction,
                postScan.MatchingIds);

        return new RoofAutomaticPurlinMaterializationResult(
            success,
            ownerReference,
            desired.Count,
            postScan.MatchingIds.Count,
            desired.Count(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate),
            desired.Count(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge),
            desired.Count(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate),
            createdCount,
            existingCount,
            updatedCount,
            staleIds.Length,
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
        string ownerReference,
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
            RoofStructuralGeneratedStore.Read(line).Exists ||
            RoofAttachedManualTimberStore.Read(line).Exists ||
            !metadataStore.TryRead(line, out var timberData) ||
            timberData is null ||
            timberData.SchemaVersion != TimberElementDataSchema.CurrentVersion ||
            !Enum.IsDefined(typeof(TimberElementType), timberData.ElementType))
        {
            return false;
        }

        var generated = RoofAutomaticPurlinGeneratedStore.Read(line).Data;
        if (generated is null ||
            !RoofAutomaticPurlinMaterializationRules.IsMatchingTimberType(
                generated.GeneratedKey,
                timberData.ElementType) ||
            !string.Equals(
                generated.RoofOwnerReference,
                ownerReference,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        existing = new ExistingMember(id, line, timberData, generated);
        return true;
    }

    /// <summary>
    /// Database-wide ownership map over every readable intelligent Timber entity in
    /// ModelSpace (all types/owners). Matching automatic members exclude themselves
    /// via ObjectId handle during Core allocation; batch reservations are explicit.
    /// </summary>
    private static IReadOnlyList<string> AssignElementIds(
        IReadOnlyList<RoofAutomaticPurlinPlanItem> desired,
        IReadOnlyList<ExistingMember?> existing,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> ownersByElementId)
    {
        var assigned = new string[desired.Count];
        foreach (var group in Enumerable.Range(0, desired.Count)
                     .GroupBy(index => desired[index].ElementType))
        {
            var indices = group.ToArray();
            var requests = indices.Select(index =>
                new RoofAutomaticPurlinElementIdRequest(
                    existing[index]?.Id.Handle.ToString(),
                    existing[index]?.TimberData.ElementId ?? string.Empty)).ToArray();
            var groupAssignments = RoofAutomaticPurlinElementIdAllocationRules.Assign(
                group.Key,
                requests,
                ownersByElementId);
            for (var index = 0; index < indices.Length; index++)
            {
                assigned[indices[index]] = groupAssignments[index];
            }
        }

        return assigned;
    }

    private static Dictionary<string, IReadOnlyCollection<string>> ReadElementIdOwners(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore)
    {
        var owners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
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
            if (!owners.TryGetValue(key, out var list))
            {
                list = new List<string>();
                owners[key] = list;
            }

            list.Add(id.Handle.ToString());
        }

        return owners.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyCollection<string>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool VerifyMembers(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        IReadOnlyList<ObjectId> ids,
        IReadOnlyDictionary<RoofAutomaticPurlinGeneratedKey, PreparedMember> expectedByKey)
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
                RoofStructuralGeneratedStore.Read(line).Exists ||
                RoofAttachedManualTimberStore.Read(line).Exists ||
                !metadataStore.TryRead(line, out var timber) ||
                timber is null ||
                !elementIds.Add(timber.ElementId))
            {
                return false;
            }

            var generated = RoofAutomaticPurlinGeneratedStore.Read(line).Data;
            if (generated is null ||
                generated.SchemaVersion != RoofAutomaticPurlinGeneratedDataSchema.CurrentVersion ||
                !expectedByKey.TryGetValue(generated.GeneratedKey, out var expected) ||
                timber != expected.TimberData ||
                generated != expected.GeneratedData ||
                !string.Equals(
                    line.Layer,
                    expected.ExpectedLayerName,
                    StringComparison.OrdinalIgnoreCase) ||
                line.ColorIndex != 256 ||
                line.LinetypeId != database.ByLayerLinetype ||
                Math.Abs(line.LinetypeScale - expected.ExpectedLinetypeScale) >
                    GeometryToleranceMm ||
                TimberElementIdentityRules.TryParseElementNumber(
                    timber.ElementId,
                    expected.Item.ElementType) is not > 0 ||
                !SamePoint(line.StartPoint, expected.Start) ||
                !SamePoint(line.EndPoint, expected.End) ||
                Math.Abs(line.Length - expected.Item.LengthMm) > GeometryToleranceMm)
            {
                return false;
            }
        }

        return true;
    }

    private static bool VerifyNoOwnedAnnotations(
        Database database,
        Transaction transaction,
        IReadOnlyList<ObjectId> timberIds)
    {
        var sourceHandles = timberIds
            .Select(id => id.Handle.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (sourceHandles.Count == 0)
        {
            return true;
        }

        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased || timberIds.Contains(id) ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
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

            if (RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(
                    entity,
                    out var sourceHandle) &&
                sourceHandles.Contains(sourceHandle))
            {
                return false;
            }
        }

        return true;
    }

    private static RoofAutomaticPurlinMemberResult CreateMemberResult(
        PreparedMember member,
        string handle,
        string result) => new(
            member.Item.GeneratedKey,
            member.Item.GeneratorRole,
            member.Item.ElementType,
            member.Item.LayoutItemId,
            handle,
            member.TimberData.ElementId,
            member.Item.WidthMm,
            member.Item.HeightMm,
            member.Item.LengthMm,
            member.Start,
            member.End,
            member.Item.ElevationProfile,
            result);

    private static Point3d MapPoint(RoofPoint3D point, double sourceElevation) =>
        new(point.X, point.Y, point.Z + sourceElevation);

    private static bool SamePoint(Point3d first, Point3d second) =>
        first.DistanceTo(second) <= GeometryToleranceMm;

    private static bool PlansEquivalent(
        RoofAutomaticPurlinPlan expected,
        RoofAutomaticPurlinPlan actual)
    {
        if (expected.Items.Count != actual.Items.Count)
        {
            return false;
        }

        for (var index = 0; index < expected.Items.Count; index++)
        {
            var left = expected.Items[index];
            var right = actual.Items[index];
            if (left.GeneratedKey != right.GeneratedKey ||
                left.ElementType != right.ElementType ||
                Math.Abs(left.WidthMm - right.WidthMm) > GeometryToleranceMm ||
                Math.Abs(left.HeightMm - right.HeightMm) > GeometryToleranceMm ||
                !SamePoint(left.Segment3D.Start, right.Segment3D.Start) ||
                !SamePoint(left.Segment3D.End, right.Segment3D.End) ||
                !SameElevationProfile(left.ElevationProfile, right.ElevationProfile) ||
                !SamePhysicalPlacement(left.PhysicalPlacement, right.PhysicalPlacement))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SamePoint(RoofPoint3D first, RoofPoint3D second) =>
        Math.Abs(first.X - second.X) <= GeometryToleranceMm &&
        Math.Abs(first.Y - second.Y) <= GeometryToleranceMm &&
        Math.Abs(first.Z - second.Z) <= GeometryToleranceMm;

    private static bool SameElevationProfile(
        RoofPurlinElevationProfile? first,
        RoofPurlinElevationProfile? second)
    {
        if (first is null || second is null)
        {
            return first is null && second is null;
        }

        return Math.Abs(first.BottomLocalZMm - second.BottomLocalZMm) <= GeometryToleranceMm &&
               Math.Abs(first.CenterLocalZMm - second.CenterLocalZMm) <= GeometryToleranceMm &&
               Math.Abs(first.TopLocalZMm - second.TopLocalZMm) <= GeometryToleranceMm &&
               Math.Abs(first.BottomRelativeElevationMm - second.BottomRelativeElevationMm) <= GeometryToleranceMm &&
               Math.Abs(first.CenterRelativeElevationMm - second.CenterRelativeElevationMm) <= GeometryToleranceMm &&
               Math.Abs(first.TopRelativeElevationMm - second.TopRelativeElevationMm) <= GeometryToleranceMm &&
               SameNullable(first.SeatingDepthMm, second.SeatingDepthMm);
    }

    private static bool SameNullable(double? first, double? second) =>
        first.HasValue == second.HasValue &&
        (!first.HasValue || Math.Abs(first.Value - second!.Value) <= GeometryToleranceMm);

    private static bool SamePhysicalPlacement(
        RoofPurlinPhysicalPlacement? first,
        RoofPurlinPhysicalPlacement? second)
    {
        if (first is null || second is null)
        {
            return first is null && second is null;
        }

        var firstSection = first.RafterSection;
        var secondSection = second.RafterSection;
        return SamePoint(firstSection.CenterlinePoint, secondSection.CenterlinePoint) &&
               SamePoint(firstSection.LowerSurfacePoint, secondSection.LowerSurfacePoint) &&
               SamePoint(firstSection.UpperSurfacePoint, secondSection.UpperSurfacePoint) &&
               Math.Abs(firstSection.FaceNormal.X - secondSection.FaceNormal.X) <= GeometryToleranceMm &&
               Math.Abs(firstSection.FaceNormal.Y - secondSection.FaceNormal.Y) <= GeometryToleranceMm &&
               Math.Abs(firstSection.FaceNormal.Z - secondSection.FaceNormal.Z) <= GeometryToleranceMm &&
               Math.Abs(firstSection.HeightMm - secondSection.HeightMm) <= GeometryToleranceMm &&
               Math.Abs(first.RafterCenterLocalZMm - second.RafterCenterLocalZMm) <= GeometryToleranceMm &&
               Math.Abs(first.RafterLowerSurfaceLocalZMm - second.RafterLowerSurfaceLocalZMm) <= GeometryToleranceMm &&
               Math.Abs(first.RafterUpperSurfaceLocalZMm - second.RafterUpperSurfaceLocalZMm) <= GeometryToleranceMm &&
               Math.Abs(first.SeatingDepthMm - second.SeatingDepthMm) <= GeometryToleranceMm &&
               Math.Abs(first.PurlinBottomLocalZMm - second.PurlinBottomLocalZMm) <= GeometryToleranceMm &&
               Math.Abs(first.PurlinCenterLocalZMm - second.PurlinCenterLocalZMm) <= GeometryToleranceMm &&
               Math.Abs(first.PurlinTopLocalZMm - second.PurlinTopLocalZMm) <= GeometryToleranceMm &&
               Math.Abs(first.RafterCenterRelativeElevationMm - second.RafterCenterRelativeElevationMm) <= GeometryToleranceMm &&
               Math.Abs(first.RafterLowerSurfaceRelativeElevationMm - second.RafterLowerSurfaceRelativeElevationMm) <= GeometryToleranceMm &&
               Math.Abs(first.RafterUpperSurfaceRelativeElevationMm - second.RafterUpperSurfaceRelativeElevationMm) <= GeometryToleranceMm &&
               Math.Abs(first.PurlinBottomRelativeElevationMm - second.PurlinBottomRelativeElevationMm) <= GeometryToleranceMm &&
               Math.Abs(first.PurlinCenterRelativeElevationMm - second.PurlinCenterRelativeElevationMm) <= GeometryToleranceMm &&
               Math.Abs(first.PurlinTopRelativeElevationMm - second.PurlinTopRelativeElevationMm) <= GeometryToleranceMm;
    }

    private sealed record ExistingMember(
        ObjectId Id,
        Line Line,
        TimberElementData TimberData,
        RoofAutomaticPurlinGeneratedData GeneratedData);

    private sealed record PreparedMember(
        RoofAutomaticPurlinPlanItem Item,
        ExistingMember? Existing,
        Point3d Start,
        Point3d End,
        TimberElementData TimberData,
        RoofAutomaticPurlinGeneratedData GeneratedData,
        string ExpectedLayerName,
        double ExpectedLinetypeScale);
}

internal sealed record RoofAutomaticPurlinMaterializationPreparation(
    string OwnerReference,
    RoofFootprintInput SourceInput,
    HipRoofGeometry HipGeometry,
    RoofBoundaryIdentity BoundaryIdentity,
    double SourceElevation);

internal sealed record RoofAutomaticPurlinOwnerStateInspection(
    bool IsValid,
    int ExistingCount,
    int Duplicates,
    string Result)
{
    public static RoofAutomaticPurlinOwnerStateInspection Success(int existingCount) =>
        new(true, existingCount, 0, "ok");

    public static RoofAutomaticPurlinOwnerStateInspection Failure(
        string result,
        int duplicates = 0) =>
        new(false, 0, duplicates, result);
}

internal sealed record RoofAutomaticPurlinMemberResult(
    RoofAutomaticPurlinGeneratedKey GeneratedKey,
    RoofAutomaticPurlinGeneratorRole Role,
    TimberElementType ElementType,
    string? LayoutItemId,
    string Handle,
    string ElementId,
    double WidthMm,
    double HeightMm,
    double PlanLengthMm,
    Point3d Start,
    Point3d End,
    RoofPurlinElevationProfile? ElevationProfile,
    string Result);

internal sealed record RoofAutomaticPurlinMaterializationResult(
    bool IsSuccess,
    string OwnerReference,
    int Desired,
    int Actual,
    int WallPlate,
    int Ridge,
    int Intermediate,
    int Created,
    int Existing,
    int Updated,
    int StaleRemoved,
    int Duplicates,
    int Missing,
    bool GroupCanonical,
    string Result,
    IReadOnlyList<RoofAutomaticPurlinMemberResult> Members)
{
    public static RoofAutomaticPurlinMaterializationResult Failure(
        string result,
        string ownerReference = "-",
        int duplicates = 0) => new(
            false,
            ownerReference,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            duplicates,
            0,
            false,
            result,
            Array.Empty<RoofAutomaticPurlinMemberResult>());
}
