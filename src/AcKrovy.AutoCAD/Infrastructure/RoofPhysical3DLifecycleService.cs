using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Shared reconcile entry for CREATE/EDIT and native lifecycle CommandEnded paths.
/// Callers must already be outside UNDO/REDO (guard with IsUndoRedoCommand).
/// </summary>
internal static class RoofPhysical3DLifecycleService
{
    /// <summary>Discard STRETCH tamper using the existing physical generators.
    /// Metadata supplies identity only. Native physical coordinates are never read.</summary>
    public static bool TryRestoreStretchPhysicalInTransaction(
        Document document, Transaction transaction, Polyline owner,
        IReadOnlyCollection<ObjectId> modifiedIds)
    {
        var database = document.Database;
        var ownerReference = owner.Handle.ToString();
        var ordinaryIds = new List<ObjectId>();
        var otherIds = new List<ObjectId>();
        var surfaceChanged = false;
        var structuralChanged = false;
        foreach (var id in modifiedIds)
        {
            // Accepted Plan2D reconciliation may already have replaced the body.
            if (id.IsNull || id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(transaction, id,
                    OpenMode.ForRead, out var entity, database) || entity is null ||
                RoofPhysical3DGeneratedStore.Read(entity).Data is not { } data ||
                !string.Equals(data.RoofOwnerReference, ownerReference,
                    StringComparison.OrdinalIgnoreCase)) continue;
            if (data.Role == RoofPhysical3DGeneratedRole.OrdinaryRafterSolid)
                ordinaryIds.Add(id);
            else
            {
                otherIds.Add(id);
                if (data.Role == RoofPhysical3DGeneratedRole.StructuralRafterSolid)
                    structuralChanged = true;
                else surfaceChanged = true;
            }
        }
        if (ordinaryIds.Count == 0 && otherIds.Count == 0) return true;
        var input = RoofPolylineExtractor.Extract(owner);
        var footprint = RoofFootprintValidator.Validate(input);
        var definition = RoofDefinitionStore.Read(owner).Data;
        if (!footprint.IsValid || footprint.Footprint is null || definition is null ||
            RoofDefinitionPersistence.Restore(input, footprint.Footprint, definition).Geometry
                is not HipRoofGeometry hip) return false;
        if (ordinaryIds.Count > 0 && !RoofOrdinaryRafterSolidMaterializationService
                .TryRestoreMovedPhysicalMembersInTransaction(database, transaction,
                    owner, hip, ordinaryIds, out _)) return false;
        if (otherIds.Count == 0) return true;
        _ = RoofAssemblyGroupSyncService.DetachMembersBeforeErase(
            database, transaction, owner.ObjectId, otherIds);
        if (structuralChanged)
        {
            var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(
                input, RoofBoundaryIdentityStore.Read(owner).Data);
            var resolution = RoofStructuralEdgeIdentityResolver.Resolve(hip, provenance);
            if (!RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction(
                    database, transaction, owner, hip, resolution, document.Editor, out _))
                return false;
        }
        if (surfaceChanged)
        {
            if (!owner.IsWriteEnabled) owner.UpgradeOpen();
            if (!ReconcileOwnerInTransaction(database, transaction, owner.ObjectId, owner,
                    footprint.Footprint, hip,
                    ResolveElevationState(owner, footprint.Footprint, hip)).IsSuccess)
                return false;
        }
        return true;
    }

    /// <summary>Historical ObjectErased handles are not current deletion authority.</summary>
    public static void CleanupStillErasedSourceInTransaction(Database database, Transaction transaction,
        string ownerReference)
    {
        if (!long.TryParse(ownerReference, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var handle)) return;
        ObjectId ownerId;
        try { ownerId = database.GetObjectId(false, new Handle(handle), 0); }
        catch (Autodesk.AutoCAD.Runtime.Exception) { return; } // Unresolved: fail closed.
        var resolved = AutoCadObjectIdAccess.TryGetObjectAllowErased<Polyline>(
            transaction, ownerId, OpenMode.ForRead, out var owner, database) && owner is not null;
        var isRoof = resolved && (RoofDisplayErasePreCommandMapService.TryGetSourceState(ownerId, out var state)
            ? state.DefinitionAvailable : RoofDefinitionStore.Read(owner!).Data is not null);
        var erase = RoofPhysical3DSetRules.ShouldEraseForSourceState(resolved, isRoof, owner?.IsErased == true);
#if DEBUG
        RoofPhysical3DHostDiagnostics.OwnerCounts(database, transaction, ownerReference,
            $"source-cleanup-before:resolved={resolved}:isRoof={isRoof}:sourceErased={owner?.IsErased}:erase={erase}");
#endif
        if (erase) RoofPhysical3DMaterializationService.EraseOwned(database, transaction, ownerReference);
#if DEBUG
        RoofPhysical3DHostDiagnostics.OwnerCounts(database, transaction, ownerReference,
            erase ? "source-cleanup-after:erased" : "source-cleanup-after:preserved");
#endif
    }

    /// <summary>Preserves a complete live set; repairs a missing/incomplete set through the shared generator.</summary>
    public static bool EnsureRestoredOwnerInTransaction(Database database, Transaction transaction,
        ObjectId ownerId)
    {
        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(transaction, ownerId, OpenMode.ForRead,
                out var owner, database) || owner is null) return false;
        var definition = RoofDefinitionStore.Read(owner).Data;
        if (definition is null) return false;
        if (definition.Kind != RoofKind.Hip) return true;
        var input = RoofPolylineExtractor.Extract(owner);
        var footprint = RoofFootprintValidator.Validate(input);
        if (!footprint.IsValid || footprint.Footprint is null) return false;
        var restored = RoofDefinitionPersistence.Restore(input, footprint.Footprint, definition);
        if (!restored.IsValid || restored.Geometry is not HipRoofGeometry hip) return false;
        var elevation = ResolveElevationState(owner, footprint.Footprint, hip);
        if (!elevation.Physical3DEnabled ||
            !RectangularSymmetricHipEligibility.Evaluate(footprint.Footprint, hip).IsEligible)
            return ReconcileOwnerInTransaction(database, transaction, ownerId, owner,
                footprint.Footprint, hip, elevation).IsSuccess;
        var build = RectangularHipRoofPhysical3DBuilder.TryBuild(owner.Handle.ToString(),
            footprint.Footprint, hip, elevation);
        if (!build.IsValid || build.Model is null) return false;
#if DEBUG
        RoofPhysical3DHostDiagnostics.OwnerCounts(database, transaction, owner.Handle.ToString(),
            "locked-source-physical-ensure-before");
#endif
        var children = ReadPhysicalChildren(database, transaction, owner.Handle.ToString());
        if (RoofPhysical3DSetRules.IsComplete(build.Model, children))
        {
            // Rebuilt plan children must also inherit this roof's current mode.
            RoofPhysical3DMaterializationService.ApplyOwnedVisibility(database, transaction,
                owner.Handle.ToString(), true, elevation.DisplayVisibility);
#if DEBUG
            RoofPhysical3DHostDiagnostics.OwnerCounts(database, transaction, owner.Handle.ToString(),
                "locked-source-physical-ensure-preserved");
#endif
            return true;
        }
        var result = ReconcileOwnerInTransaction(database, transaction, ownerId, owner,
            footprint.Footprint, hip, elevation);
        var complete = result.IsSuccess && RoofPhysical3DSetRules.IsComplete(build.Model,
            ReadPhysicalChildren(database, transaction, owner.Handle.ToString()));
#if DEBUG
        RoofPhysical3DHostDiagnostics.OwnerCounts(database, transaction, owner.Handle.ToString(),
            complete ? "locked-source-physical-ensure-restored:provisional" : "locked-source-physical-ensure-failed");
#endif
        return complete;
    }

    private static RoofPhysical3DGeneratedData?[] ReadPhysicalChildren(Database database, Transaction transaction,
        string ownerReference) => RoofPhysical3DGeneratedStore.FindByOwner(database, transaction, ownerReference)
        .Select(id =>
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(transaction, id, OpenMode.ForRead,
                    out var child, database) || child is null)
                return (Skip: false, Data: (RoofPhysical3DGeneratedData?)null);
            var data = RoofPhysical3DGeneratedStore.Read(child).Data;
            if (data?.Role is RoofPhysical3DGeneratedRole.OrdinaryRafterSolid or
                RoofPhysical3DGeneratedRole.StructuralRafterSolid)
                return (Skip: true, Data: (RoofPhysical3DGeneratedData?)null);
            return (Skip: false, Data: data is not null &&
                (data.Role == RoofPhysical3DGeneratedRole.Face ? child is Face : child is Line)
                ? data : null);
        }).Where(item => !item.Skip).Select(item => item.Data).ToArray();

    public static RoofPhysical3DMaterializationResult ReconcileOwnerInTransaction(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        Polyline owner,
        RoofFootprint footprint,
        HipRoofGeometry geometry,
        RoofAbsoluteElevationState elevation) =>
        RoofPhysical3DMaterializationService.ReconcileInTransaction(
            database,
            transaction,
            ownerId,
            owner,
            footprint,
            geometry,
            elevation);

    public static RoofAbsoluteElevationState ResolveElevationState(
        Polyline owner,
        RoofFootprint footprint,
        HipRoofGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(footprint);
        ArgumentNullException.ThrowIfNull(geometry);

        var rise = 0d;
        if (RectangularRoofFootprintRules.TryDescribe(footprint, out var rectangle) &&
            rectangle is not null)
        {
            rise = RoofAbsoluteElevationRules.RiseMm(
                rectangle.HalfWidthMm,
                geometry.PrimarySlopeDegrees);
            if (!IsFinite(rise))
            {
                rise = geometry.RiseMm;
            }
        }
        else
        {
            rise = geometry.RiseMm;
        }

        var stored = RoofPhysicalElevationStore.Read(owner);
        if (stored.Data is null)
        {
            return RoofPhysicalElevationRules.MissingStoreDefault(rise);
        }

        return RoofPhysicalElevationRules.ToState(stored.Data, rise);
    }

    public static bool TryReconcileRestoredOwnerInTransaction(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        Polyline owner,
        RoofFootprintInput sourceInput,
        RoofFootprint footprint,
        RoofDefinitionData definitionData)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(sourceInput);
        ArgumentNullException.ThrowIfNull(footprint);
        ArgumentNullException.ThrowIfNull(definitionData);

        if (definitionData.Kind != RoofKind.Hip)
        {
            return true;
        }

        var restored = RoofDefinitionPersistence.Restore(sourceInput, footprint, definitionData);
        if (!restored.IsValid || restored.Geometry is not HipRoofGeometry hip)
        {
            return true;
        }

        var elevation = ResolveElevationState(owner, footprint, hip);
        if (!elevation.Physical3DEnabled &&
            RectangularSymmetricHipEligibility.Evaluate(footprint, hip).IsEligible == false)
        {
            RoofPhysical3DMaterializationService.EraseOwned(
                database,
                transaction,
                owner.Handle.ToString());
            return true;
        }

        var result = ReconcileOwnerInTransaction(
            database,
            transaction,
            ownerId,
            owner,
            footprint,
            hip,
            elevation);
        return result.IsSuccess;
    }

    /// <summary>
    /// Permanent owned display edges for a roof owner. Physical3DEnabled Hip roofs
    /// get a flattened drawing-plane projection; legacy roofs keep spatial local-Z.
    /// </summary>
    public static IReadOnlyList<RoofDisplayEdge> CreateOwnedDisplayEdges(
        Polyline owner,
        IRoofGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(geometry);

        var sourceElevation = RoofPolylineExtractor.GetSourceElevation(owner);
        var physical3DEnabled = false;
        if (geometry is HipRoofGeometry)
        {
            var stored = RoofPhysicalElevationStore.Read(owner);
            physical3DEnabled = stored.Data?.Physical3DEnabled == true;
        }

        return RoofWireframe.CreateOwnedHipOrLegacy(
            geometry,
            sourceElevation,
            physical3DEnabled);
    }

    /// <inheritdoc cref="CreateOwnedDisplayEdges(Polyline, IRoofGeometry)"/>
    public static IReadOnlyList<RoofDisplayEdge> CreateOwnedDisplayEdges(
        Polyline owner,
        RoofFootprint footprint,
        IRoofGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(footprint);
        return CreateOwnedDisplayEdges(owner, geometry);
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
