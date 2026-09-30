using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Atomic, idempotent materialization of native physical-3D hip roof entities.
/// Decision 2A: Face/Line Z comes from the Core model (resolved eave + local Z);
/// polyline.Elevation is never added.
/// </summary>
internal static class RoofPhysical3DMaterializationService
{
    internal const string FaceLayerName = "KROV_STRECHA_3D";
    internal const string EdgeLayerName = "KROV_STRECHA_3D_HRANY";
    internal const int FaceLayerColorIndex = 30;
    internal const int EdgeLayerColorIndex = 1;

    public static RoofPhysical3DMaterializationResult ReconcileInTransaction(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        Polyline owner,
        RoofFootprint footprint,
        HipRoofGeometry geometry,
        RoofAbsoluteElevationState elevation)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(footprint);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(elevation);

        var ownerReference = owner.Handle.ToString();
#if DEBUG
        RoofPhysical3DHostDiagnostics.Reconcile(database, transaction, ownerReference, "before");
#endif
        var eligibility = RectangularSymmetricHipEligibility.Evaluate(footprint, geometry);
        if (!eligibility.IsEligible || !elevation.Physical3DEnabled)
        {
            // Non-rectangular solved roofs have no roof-surface Face/Edge model
            // yet, but may own valid ordinary rafter solids (including Valley).
            // Disable still clears every physical child; mere surface
            // ineligibility must not erase the independent ordinary role.
            if (elevation.Physical3DEnabled)
                EraseRoofSurfaceOwned(database, transaction, ownerReference);
            else
                EraseOwned(database, transaction, ownerReference);
            // Keep Physical3DEnabled when only eligibility failed so a later valid
            // rectangle automatically restores 3D. Clearing the preference here caused
            // GRIP_STRETCH HardFailure: flattened 2D rebuild + disabled preference made
            // TrySyncForOwner expect spatial edges and roll back the whole resize txn.
            var persisted = elevation;
            var suspended = RoofPhysical3DSuspensionRules.ShouldSuspendMaterialization(
                elevation.Physical3DEnabled,
                eligibility.IsEligible);
            if (suspended)
            {
                persisted = RoofPhysical3DSuspensionRules.PreservePreferenceWhileSuspended(
                    elevation);
            }

            RoofPhysicalElevationStore.Write(
                owner,
                transaction,
                RoofPhysicalElevationRules.CreateFromState(persisted));
            ApplyOwnedVisibility(
                database,
                transaction,
                ownerReference,
                persisted.Physical3DEnabled,
                persisted.DisplayVisibility);
            return RoofPhysical3DMaterializationResult.SuccessCleared(
                persisted.Physical3DEnabled,
                suspendedDueToIneligibility: suspended);
        }

        var build = RectangularHipRoofPhysical3DBuilder.TryBuild(
            ownerReference,
            footprint,
            geometry,
            elevation);
        if (!build.IsValid || build.Model is null)
        {
            return RoofPhysical3DMaterializationResult.Failure("Physical3DBuildFailed");
        }

        try
        {
            EraseRoofSurfaceOwned(database, transaction, ownerReference);
            CreateEntities(database, transaction, build.Model);
#if DEBUG
            RoofPhysical3DHostDiagnostics.Reconcile(database, transaction, ownerReference, "after-create");
#endif
            RoofPhysicalElevationStore.Write(
                owner,
                transaction,
                RoofPhysicalElevationRules.CreateFromState(elevation));
            ApplyOwnedVisibility(
                database,
                transaction,
                ownerReference,
                physical3DEnabled: true,
                elevation.DisplayVisibility);
            return RoofPhysical3DMaterializationResult.SuccessCreated(build.Model);
        }
        catch (System.Exception)
        {
            return RoofPhysical3DMaterializationResult.Failure("Physical3DMaterializeFailed");
        }
    }

    /// <summary>
    /// Per-roof entity Visibility only — never toggles shared layers.
    /// </summary>
    public static void ApplyOwnedVisibility(
        Database database,
        Transaction transaction,
        string ownerReference,
        bool physical3DEnabled,
        RoofPhysicalDisplayVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        if (string.IsNullOrWhiteSpace(ownerReference))
        {
            return;
        }

        var showPlan2D = !physical3DEnabled ||
            visibility is RoofPhysicalDisplayVisibility.Both or RoofPhysicalDisplayVisibility.Plan2D;
        var showModel3D = physical3DEnabled &&
            visibility is RoofPhysicalDisplayVisibility.Both or RoofPhysicalDisplayVisibility.Model3D;

        foreach (var id in FindOwnedDisplayChildren(database, transaction, ownerReference))
        {
            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var entity,
                    database) &&
                entity is not null &&
                !entity.IsErased)
            {
                entity.Visible = showPlan2D;
            }
        }

        var ordinarySourceHandles = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var id in RoofGeneratedTimberStore.FindByOwner(
                     database, transaction, ownerReference))
        {
            if (AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction, id, OpenMode.ForWrite, out var line, database) &&
                line is not null && !line.IsErased)
            {
                ordinarySourceHandles.Add(line.Handle.ToString());
                line.Visible = showPlan2D;
            }
        }

        if (ordinarySourceHandles.Count > 0)
        {
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);
            foreach (ObjectId id in modelSpace)
            {
                if (id.IsErased ||
                    !AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction, id, OpenMode.ForRead, out var entity, database) ||
                    entity is null || entity.IsErased ||
                    !RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(
                        entity, out var sourceHandle) ||
                    !ordinarySourceHandles.Contains(sourceHandle))
                {
                    continue;
                }
                entity.UpgradeOpen();
                entity.Visible = showPlan2D;
            }
        }

        foreach (var id in RoofPhysical3DGeneratedStore.FindByOwner(
                     database,
                     transaction,
                     ownerReference))
        {
            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var entity,
                    database) &&
                entity is not null &&
                !entity.IsErased)
            {
                entity.Visible = showModel3D;
            }
        }
    }

    private static IReadOnlyList<ObjectId> FindOwnedDisplayChildren(
        Database database,
        Transaction transaction,
        string ownerReference)
    {
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        var matches = new List<ObjectId>();
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity ||
                entity.IsErased)
            {
                continue;
            }

            var display = RoofDisplayStore.Read(entity);
            if (display.Exists &&
                string.Equals(
                    display.OwnerReference,
                    ownerReference,
                    StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(id);
            }
        }

        return matches;
    }

    public static void EraseOwned(
        Database database,
        Transaction transaction,
        string ownerReference)
        => EraseOwnedMatching(database, transaction, ownerReference, ordinaryOnly: false,
            roofSurfaceOnly: false);

    public static void EraseRoofSurfaceOwned(
        Database database,
        Transaction transaction,
        string ownerReference)
        => EraseOwnedMatching(database, transaction, ownerReference, ordinaryOnly: false,
            roofSurfaceOnly: true);

    public static void EraseOrdinaryRafterSolids(
        Database database,
        Transaction transaction,
        string ownerReference)
        => EraseOwnedMatching(database, transaction, ownerReference, ordinaryOnly: true,
            roofSurfaceOnly: false);

    public static void EraseStructuralRafterSolids(
        Database database,
        Transaction transaction,
        string ownerReference)
        => EraseOwnedMatching(database, transaction, ownerReference, ordinaryOnly: false,
            roofSurfaceOnly: false, structuralOnly: true);

    private static void EraseOwnedMatching(
        Database database,
        Transaction transaction,
        string ownerReference,
        bool ordinaryOnly,
        bool roofSurfaceOnly,
        bool structuralOnly = false)
    {
        foreach (var id in RoofPhysical3DGeneratedStore.FindByOwner(
                     database,
                     transaction,
                     ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var entity,
                    database) ||
                entity is null ||
                entity.IsErased)
            {
                continue;
            }

            var role = RoofPhysical3DGeneratedStore.Read(entity).Data?.Role;
            var ordinary = role == RoofPhysical3DGeneratedRole.OrdinaryRafterSolid;
            var structural = role == RoofPhysical3DGeneratedRole.StructuralRafterSolid;
            if ((ordinaryOnly && !ordinary) ||
                (roofSurfaceOnly && (ordinary || structural)) ||
                (structuralOnly && !structural))
            {
                continue;
            }

            entity.Erase();
        }
    }

    private static void CreateEntities(
        Database database,
        Transaction transaction,
        RoofPhysical3DModel model)
    {
        EnsureLayer(database, transaction, FaceLayerName, FaceLayerColorIndex);
        EnsureLayer(database, transaction, EdgeLayerName, EdgeLayerColorIndex);
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);

        foreach (var face in model.Faces)
        {
            var entity = CreateFace(face);
            entity.SetDatabaseDefaults(database);
            entity.Layer = FaceLayerName;
            modelSpace.AppendEntity(entity);
            transaction.AddNewlyCreatedDBObject(entity, true);
            RoofPhysical3DGeneratedStore.Write(
                entity,
                transaction,
                RoofPhysical3DGeneratedDataRules.Create(
                    model.OwnerReference,
                    RoofPhysical3DGeneratedRole.Face,
                    face.PlaneId.Value,
                    model.GenerationSignature));
        }

        // Physical WCS eaves are independent of the sole 2D source perimeter.
        // Face eave edges stay suppressed to avoid drawing the same 3D edge twice.
        foreach (var eave in model.Eaves)
        {
            AppendEdge(
                database,
                transaction,
                modelSpace,
                model,
                eave,
                RoofPhysical3DGeneratedRole.EaveEdge);
        }

        foreach (var ridge in model.Ridges)
        {
            AppendEdge(
                database,
                transaction,
                modelSpace,
                model,
                ridge,
                RoofPhysical3DGeneratedRole.RidgeEdge);
        }

        foreach (var hip in model.Hips)
        {
            AppendEdge(
                database,
                transaction,
                modelSpace,
                model,
                hip,
                RoofPhysical3DGeneratedRole.HipEdge);
        }
    }

    private static void AppendEdge(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        RoofPhysical3DModel model,
        RoofPhysical3DEdge edge,
        RoofPhysical3DGeneratedRole role)
    {
        var line = new Line(MapPoint(edge.Segment.Start), MapPoint(edge.Segment.End));
        line.SetDatabaseDefaults(database);
        line.Layer = EdgeLayerName;
        modelSpace.AppendEntity(line);
        transaction.AddNewlyCreatedDBObject(line, true);
        RoofPhysical3DGeneratedStore.Write(
            line,
            transaction,
            RoofPhysical3DGeneratedDataRules.Create(
                model.OwnerReference,
                role,
                edge.StructuralId,
                model.GenerationSignature));
    }

    private static Face CreateFace(RoofPhysical3DFace face)
    {
        var polygon = face.Polygon;
        var visibility = RoofPhysical3DPlanDisplayRules.FaceEdgeVisibility(
            polygon,
            face.EaveSegment);
        if (polygon.Count == 3)
        {
            var a = MapPoint(polygon[0]);
            var b = MapPoint(polygon[1]);
            var c = MapPoint(polygon[2]);
            return new Face(
                a,
                b,
                c,
                c,
                visibility[0],
                visibility[1],
                visibility[2],
                visibility[3]);
        }

        if (polygon.Count == 4)
        {
            return new Face(
                MapPoint(polygon[0]),
                MapPoint(polygon[1]),
                MapPoint(polygon[2]),
                MapPoint(polygon[3]),
                visibility[0],
                visibility[1],
                visibility[2],
                visibility[3]);
        }

        // Degenerate fallback: triangulate from first vertex (should not occur for eligible hips).
        var first = MapPoint(polygon[0]);
        var second = MapPoint(polygon[1]);
        var third = MapPoint(polygon[2]);
        return new Face(
            first,
            second,
            third,
            third,
            visibility[0],
            visibility[1],
            visibility[2],
            visibility[3]);
    }

    private static Point3d MapPoint(RoofPoint3D point) =>
        new(point.X, point.Y, point.Z);

    private static void EnsureLayer(
        Database database,
        Transaction transaction,
        string layerName,
        int colorIndex)
    {
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        if (layerTable.Has(layerName))
        {
            return;
        }

        layerTable.UpgradeOpen();
        var record = new LayerTableRecord
        {
            Name = layerName,
            Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                ColorMethod.ByAci,
                (short)colorIndex),
        };
        layerTable.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }
}

internal sealed record RoofPhysical3DMaterializationResult(
    bool IsSuccess,
    RoofPhysical3DModel? Model,
    bool Physical3DEnabled,
    string? FailureKey,
    bool SuspendedDueToIneligibility = false)
{
    public static RoofPhysical3DMaterializationResult SuccessCreated(RoofPhysical3DModel model) =>
        new(true, model, true, null);

    public static RoofPhysical3DMaterializationResult SuccessCleared(
        bool enabled,
        bool suspendedDueToIneligibility = false) =>
        new(true, null, enabled, null, suspendedDueToIneligibility);

    public static RoofPhysical3DMaterializationResult Failure(string key) =>
        new(false, null, false, key);
}
