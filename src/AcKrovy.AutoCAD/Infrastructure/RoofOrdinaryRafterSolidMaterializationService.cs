using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CadRegion = Autodesk.AutoCAD.DatabaseServices.Region;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Materializes the physical representation of the existing ordinary Generated
/// members. The 2D lines remain the canonical plan representation; neither
/// representation is constructed by transforming the other CAD entity.
/// </summary>
internal static class RoofOrdinaryRafterSolidMaterializationService
{
    internal const string LayerName = "KROV_KROKVY_3D";

    // Read-only Core model access for the dependent structural physical timber.
    // Reuses the exact ordinary builder and replay path; no parallel end-cut formula.
    public static bool TryBuildExistingModelInTransaction(
        Database database,
        Transaction transaction,
        Polyline owner,
        HipRoofGeometry hip,
        out RoofAutomaticRafterPhysicalModel? model)
    {
        model = null;
        var ownerReference = owner.Handle.ToString();
        var existing = RoofGeneratedTimberStore.FindByOwner(
            database, transaction, ownerReference);
        if (existing.Count == 0 ||
            !RoofGeneratedRafterSetService.TryRecoverRecipe(
                database, transaction, existing, out var recipe))
            return false;
        var solved = RoofRafterLayoutSolver.Solve(
            hip, AutoCadRoofRafterSpacingStore.CreateLayoutParameters(
                database, recipe.MaximumSpacingMm, recipe.WidthMm));
        if (!solved.IsValid || solved.Layout is null)
            return false;
        var replay = RoofGeneratedMemberReplayPlanner.Create(
            solved.Layout, 0d,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
            RoofDefinitionStore.Read(owner).Data?.Overrides);
        var faceLayout = RoofFaceRafterLayoutService.Create(
            hip.Topology, recipe.MaximumSpacingMm);
        var elevation = RoofPhysicalElevationStore.Read(owner).Data;
        var sources = ResolveStructuralSources(
            database, transaction, owner, hip, structuralReconcilePending: false);
        return replay.IsValid && faceLayout.IsValid && faceLayout.Layout is not null &&
            elevation?.Physical3DEnabled == true && sources is not null &&
            RoofAutomaticRafterPhysicalBuilder.TryBuild(
                ownerReference, hip.Topology, faceLayout.Layout, solved.Layout,
                elevation.ResolvedEaveRelativeElevationMm,
                recipe.WidthMm, recipe.HeightMm,
                new RoofAutomaticRafterPhysicalSettings(
                    elevation.LowerEndCutMode, elevation.RidgeJoinMode),
                replay, sources, out model, out _) &&
            model is not null &&
            model.Members.Count == replay.MaterializedCount &&
            MatchesGeneratedMemberKeys(database, transaction, ownerReference, model);
    }

    /// <summary>Rebuild only the physical bodies whose authoritative 2D member
    /// changed. The caller's transaction also owns the accepted 2D override.</summary>
    public static bool TryReconcileModifiedMembersInTransaction(
        Database database,
        Transaction transaction,
        Polyline owner,
        IRoofGeometry geometry,
        IReadOnlyCollection<ObjectId> modifiedIds)
    {
        if (geometry is not HipRoofGeometry hip ||
            RoofPhysicalElevationStore.Read(owner).Data is not { Physical3DEnabled: true } elevation)
            return true;

        var ownerReference = owner.Handle.ToString();
        var changedKeys = new HashSet<RoofGeneratedMemberKey>();
        foreach (var id in modifiedIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction, id, OpenMode.ForRead, out var line, database) ||
                line is null || RoofGeneratedTimberStore.Read(line).Data is not { } data ||
                !string.Equals(data.RoofOwnerReference, ownerReference,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            changedKeys.Add(RoofGeneratedMemberKey.From(data));
        }
        if (changedKeys.Count == 0)
            return true;

        if (!TryBuildExistingModelInTransaction(
                database, transaction, owner, hip, out var model) || model is null)
            return false;

        var members = model.Members.ToDictionary(
            member => PhysicalMemberId(member.MemberKey),
            member => member);
        var existing = new Dictionary<string, (ObjectId Id, RoofPhysical3DGeneratedData Data)>(
            StringComparer.Ordinal);
        foreach (var id in RoofPhysical3DGeneratedStore.FindByOwner(
                     database, transaction, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction, id, OpenMode.ForRead, out var entity, database) ||
                entity is null ||
                RoofPhysical3DGeneratedStore.Read(entity).Data is not { } data ||
                data.Role != RoofPhysical3DGeneratedRole.OrdinaryRafterSolid)
                continue;
            if (entity is not Solid3d || !existing.TryAdd(data.StructuralId, (id, data)))
                return false;
        }
        // A damaged/missing physical set must not be silently repaired by a
        // single-member edit; fail and let the caller restore the 2D snapshot.
        if (existing.Count != members.Count || !existing.Keys.ToHashSet().SetEquals(members.Keys))
            return false;

        var selected = changedKeys.Select(PhysicalMemberId).ToHashSet();
        if (!selected.IsSubsetOf(members.Keys))
            return false;

        EnsureLayer(database, transaction);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForWrite);
        var showModel3D = elevation.DisplayVisibility is
            RoofPhysicalDisplayVisibility.Both or RoofPhysicalDisplayVisibility.Model3D;
        foreach (var memberId in selected)
        {
            var old = existing[memberId];
            // Construct first. A geometric failure leaves the old body untouched;
            // any later database failure aborts the caller-owned transaction.
            var solid = CreateSolid(members[memberId]);
            var appended = false;
            try
            {
                solid.SetDatabaseDefaults(database);
                solid.Layer = LayerName;
                solid.Visible = showModel3D;
                modelSpace.AppendEntity(solid);
                appended = true;
                transaction.AddNewlyCreatedDBObject(solid, true);
                RoofPhysical3DGeneratedStore.Write(solid, transaction, old.Data);
                var oldEntity = (Entity)transaction.GetObject(old.Id, OpenMode.ForWrite);
                oldEntity.Erase();
            }
            finally
            {
                if (!appended)
                    solid.Dispose();
            }
        }
        return true;
    }

    /// <summary>Reject a native edit of derived 3D bodies by rebuilding their
    /// exact keys from the unchanged authoritative 2D members and roof model.
    /// The caller owns group sync and the transaction commit.</summary>
    public static bool TryRestoreMovedPhysicalMembersInTransaction(
        Database database,
        Transaction transaction,
        Polyline owner,
        IRoofGeometry geometry,
        IReadOnlyCollection<ObjectId> movedSolidIds,
        out IReadOnlyCollection<string> restoredMemberIds)
    {
        restoredMemberIds = Array.Empty<string>();
        if (movedSolidIds.Count == 0 || geometry is not HipRoofGeometry ||
            RoofPhysicalElevationStore.Read(owner).Data is not { Physical3DEnabled: true })
            return false;

        var ownerReference = owner.Handle.ToString();
        var movedMemberIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in movedSolidIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Solid3d>(
                    transaction, id, OpenMode.ForRead, out var solid, database) ||
                solid is null || RoofPhysical3DGeneratedStore.Read(solid).Data is not { } physical ||
                physical.Role != RoofPhysical3DGeneratedRole.OrdinaryRafterSolid ||
                !string.Equals(physical.RoofOwnerReference, ownerReference,
                    StringComparison.OrdinalIgnoreCase) ||
                !movedMemberIds.Add(physical.StructuralId))
                return false;
        }

        var authoritativeLines = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        foreach (var id in RoofGeneratedTimberStore.FindByOwner(
                     database, transaction, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction, id, OpenMode.ForRead, out var line, database) ||
                line is null || RoofGeneratedTimberStore.Read(line).Data is not { } generated ||
                !authoritativeLines.TryAdd(
                    PhysicalMemberId(RoofGeneratedMemberKey.From(generated)), id))
                return false;
        }
        if (!movedMemberIds.IsSubsetOf(authoritativeLines.Keys))
            return false;

        // Detach only the moved physical ObjectIds before the existing targeted
        // ordinary builder erases them. No 2D Line is opened for write.
        _ = RoofAssemblyGroupSyncService.DetachMembersBeforeErase(
            database, transaction, owner.ObjectId, movedSolidIds);
        if (!TryReconcileModifiedMembersInTransaction(
                database, transaction, owner, geometry,
                movedMemberIds.Select(id => authoritativeLines[id]).ToArray()))
            return false;
        restoredMemberIds = movedMemberIds;
        return true;
    }

    public static bool TryVerifyRestoredPhysicalMembersInTransaction(
        Database database,
        Transaction transaction,
        Polyline owner,
        HipRoofGeometry geometry,
        IReadOnlyCollection<string> restoredMemberIds,
        out IReadOnlyCollection<ObjectId> restoredSolidIds)
    {
        restoredSolidIds = Array.Empty<ObjectId>();
        if (!TryBuildExistingModelInTransaction(
                database, transaction, owner, geometry, out var model) || model is null)
            return false;

        var expected = model.Members.Select(member => PhysicalMemberId(member.MemberKey))
            .ToHashSet(StringComparer.Ordinal);
        var actual = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        foreach (var id in RoofPhysical3DGeneratedStore.FindByOwner(
                     database, transaction, owner.Handle.ToString()))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction, id, OpenMode.ForRead, out var entity, database) ||
                entity is null || RoofPhysical3DGeneratedStore.Read(entity).Data is not { } data ||
                data.Role != RoofPhysical3DGeneratedRole.OrdinaryRafterSolid)
                continue;
            if (entity is not Solid3d || !actual.TryAdd(data.StructuralId, id))
                return false;
        }
        if (expected.Count != model.Members.Count ||
            actual.Count != expected.Count || !actual.Keys.ToHashSet().SetEquals(expected) ||
            !restoredMemberIds.All(actual.ContainsKey))
            return false;
        restoredSolidIds = restoredMemberIds.Select(id => actual[id]).ToArray();
        return true;
    }

    private static string PhysicalMemberId(RoofGeneratedMemberKey key) => string.Join(":",
        key.MemberKind, key.RoofFace,
        key.StationIndex.ToString(CultureInfo.InvariantCulture));

    /// <summary>Accepted 2D ERASE suppresses the logical member; only its matching
    /// derived body is removed. Other ordinary bodies and roof surfaces stay put.</summary>
    public static bool TryRemoveSuppressedMembersInTransaction(
        Database database,
        Transaction transaction,
        Polyline owner,
        IRoofGeometry geometry,
        IReadOnlyCollection<RoofGeneratedMemberKey> suppressedKeys)
    {
        if (suppressedKeys.Count == 0 || geometry is not HipRoofGeometry ||
            RoofPhysicalElevationStore.Read(owner).Data is not { Physical3DEnabled: true })
            return true;

        var ownerReference = owner.Handle.ToString();
        var suppressedIds = suppressedKeys.Select(PhysicalMemberId).ToHashSet();
        if (suppressedIds.Count != suppressedKeys.Count)
            return false;

        var activeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in RoofGeneratedTimberStore.FindByOwner(
                     database, transaction, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction, id, OpenMode.ForRead, out var line, database) ||
                line is null || RoofGeneratedTimberStore.Read(line).Data is not { } data ||
                !activeIds.Add(PhysicalMemberId(RoofGeneratedMemberKey.From(data))))
                return false;
        }
        if (activeIds.Overlaps(suppressedIds))
            return false;

        var physical = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        foreach (var id in RoofPhysical3DGeneratedStore.FindByOwner(
                     database, transaction, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction, id, OpenMode.ForRead, out var entity, database) ||
                entity is null ||
                RoofPhysical3DGeneratedStore.Read(entity).Data is not { } data ||
                data.Role != RoofPhysical3DGeneratedRole.OrdinaryRafterSolid)
                continue;
            if (entity is not Solid3d || !physical.TryAdd(data.StructuralId, id))
                return false;
        }
        var expectedBefore = activeIds.Concat(suppressedIds).ToHashSet();
        if (physical.Count != expectedBefore.Count ||
            !physical.Keys.ToHashSet().SetEquals(expectedBefore))
            return false;

        // Detach the exact physical ObjectIds while they are still live. The
        // accepted ERASE transaction then removes their bodies; EnsureGroup
        // must never see an erased member waiting for a deferred reattach.
        var suppressedSolidIds = suppressedIds.Select(id => physical[id]).ToArray();
        _ = RoofAssemblyGroupSyncService.DetachMembersBeforeErase(
            database, transaction, owner.ObjectId, suppressedSolidIds);
        foreach (var memberId in suppressedIds)
        {
            var solid = (Solid3d)transaction.GetObject(physical[memberId], OpenMode.ForWrite);
            solid.Erase();
        }
        return true;
    }

    public static bool TryReconcileExistingInTransaction(
        Database database,
        Transaction transaction,
        Polyline owner,
        IRoofGeometry geometry)
    {
        var ownerReference = owner.Handle.ToString();
        var existing = RoofGeneratedTimberStore.FindByOwner(
            database, transaction, ownerReference);
        if (existing.Count == 0)
        {
            RoofPhysical3DMaterializationService.EraseOrdinaryRafterSolids(
                database, transaction, ownerReference);
            return true;
        }
        if (!RoofGeneratedRafterSetService.TryRecoverRecipe(
                database, transaction, existing, out var recipe))
        {
            return false;
        }
        var solved = RoofRafterLayoutSolver.Solve(
            geometry,
            AutoCadRoofRafterSpacingStore.CreateLayoutParameters(
                database, recipe.MaximumSpacingMm, recipe.WidthMm));
        if (!solved.IsValid || solved.Layout is null)
        {
            return false;
        }
        var replay = RoofGeneratedMemberReplayPlanner.Create(
            solved.Layout, 0d,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
            RoofDefinitionStore.Read(owner).Data?.Overrides);
        if (!replay.IsValid)
        {
            return false;
        }
        ReconcileInTransaction(database, transaction, owner, geometry,
            solved.Layout, recipe, replay);
        var elevation = RoofPhysicalElevationStore.Read(owner).Data;
        if (elevation is not null)
        {
            RoofPhysical3DMaterializationService.ApplyOwnedVisibility(
                database, transaction, ownerReference,
                elevation.Physical3DEnabled, elevation.DisplayVisibility);
        }
        return true;
    }

    public static void ReconcileInTransaction(
        Database database,
        Transaction transaction,
        Polyline owner,
        IRoofGeometry geometry,
        RoofRafterLayout layout,
        RoofRafterGenerationRecipe recipe,
        RoofGeneratedMemberReplayPlan replayPlan,
        bool structuralReconcilePending = false)
    {
        var ownerReference = owner.Handle.ToString();
        if (geometry is not HipRoofGeometry hip)
        {
            RoofPhysical3DMaterializationService.EraseOrdinaryRafterSolids(
                database, transaction, ownerReference);
            return;
        }

        var elevation = RoofPhysicalElevationStore.Read(owner).Data;
        var footprint = RoofFootprintValidator.Validate(RoofPolylineExtractor.Extract(owner));
        if (elevation?.Physical3DEnabled != true ||
            !footprint.IsValid || footprint.Footprint is null)
        {
            RoofPhysical3DMaterializationService.EraseOrdinaryRafterSolids(
                database, transaction, ownerReference);
            return;
        }

        var faceLayout = RoofFaceRafterLayoutService.Create(
            hip.Topology, recipe.MaximumSpacingMm);
        var structuralSources = ResolveStructuralSources(
            database, transaction, owner, hip, structuralReconcilePending);
        var failureReason = "InvalidFaceLayoutOrMemberCount";
        if (structuralSources is null ||
            !faceLayout.IsValid || faceLayout.Layout is null ||
            !RoofAutomaticRafterPhysicalBuilder.TryBuild(
                ownerReference, hip.Topology, faceLayout.Layout, layout,
                elevation.ResolvedEaveRelativeElevationMm,
                recipe.WidthMm, recipe.HeightMm,
                new RoofAutomaticRafterPhysicalSettings(
                    elevation.LowerEndCutMode,
                    elevation.RidgeJoinMode), replayPlan, structuralSources,
                out var model, out failureReason) || model is null ||
            model.Members.Count != replayPlan.MaterializedCount)
        {
            throw new InvalidOperationException(
                $"Ordinary physical rafter model is inconsistent: {failureReason}.");
        }
        if (!MatchesGeneratedMemberKeys(database, transaction, ownerReference, model))
            throw new InvalidOperationException(
                "Ordinary physical rafter model is inconsistent: GeneratedMemberKeyMismatch.");

        // The caller owns the transaction. A failed solid or metadata write aborts
        // the same transaction as the 2D Generated replacement.
        RoofPhysical3DMaterializationService.EraseOrdinaryRafterSolids(
            database, transaction, ownerReference);
        EnsureLayer(database, transaction);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForWrite);
        var signature = RoofGeneratedLayoutFingerprint.ToPersistedIdentity(layout.Signature);
        var showModel3D = elevation.DisplayVisibility is
            RoofPhysicalDisplayVisibility.Both or RoofPhysicalDisplayVisibility.Model3D;
        foreach (var member in model.Members)
        {
            var solid = CreateSolid(member);
            solid.SetDatabaseDefaults(database);
            solid.Layer = LayerName;
            solid.Visible = showModel3D;
            modelSpace.AppendEntity(solid);
            transaction.AddNewlyCreatedDBObject(solid, true);
            var memberId = PhysicalMemberId(member.MemberKey);
            RoofPhysical3DGeneratedStore.Write(
                solid, transaction,
                RoofPhysical3DGeneratedDataRules.Create(
                    ownerReference,
                    RoofPhysical3DGeneratedRole.OrdinaryRafterSolid,
                    memberId,
                    signature));
        }
    }

    private static bool MatchesGeneratedMemberKeys(
        Database database,
        Transaction transaction,
        string ownerReference,
        RoofAutomaticRafterPhysicalModel model)
    {
        var expected = model.Members.Select(member => member.MemberKey).ToHashSet();
        if (expected.Count != model.Members.Count)
            return false;
        var actual = new HashSet<RoofGeneratedMemberKey>();
        foreach (var id in RoofGeneratedTimberStore.FindByOwner(
                     database, transaction, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction, id, OpenMode.ForRead, out var line, database) ||
                line is null ||
                RoofGeneratedTimberStore.Read(line).Data is not { } generated ||
                !actual.Add(RoofGeneratedMemberKey.From(generated)))
            {
                return false;
            }
        }
        return actual.SetEquals(expected);
    }

    private static IReadOnlyList<RoofStructuralRafterTrimSource>? ResolveStructuralSources(
        Database database,
        Transaction transaction,
        Polyline owner,
        HipRoofGeometry hip,
        bool structuralReconcilePending)
    {
        var source = RoofPolylineExtractor.Extract(owner);
        // Read the same persisted identity as structural materialization. On
        // first creation and whole-roof MIRROR rehome run after ordinary
        // generation, so derive the same future sequential identity without
        // an early database write. Other malformed identities fail closed.
        var stored = RoofBoundaryIdentityStore.Read(owner);
        if (stored.Exists && stored.Data is null &&
            stored.Error != RoofBoundaryIdentityError.CurrentRawWindingMismatch)
            return null;
        var identity = stored.Data;
        if (identity is null)
        {
            var normalized = RoofFootprintValidator.ValidateWithProvenance(source);
            if (!normalized.Validation.IsValid)
                return null;
            identity = RoofBoundaryIdentityRules.CreateSequential(
                normalized.EdgeProvenance.Count,
                normalized.Validation.SourceOrientation).Identity;
        }
        if (identity is null)
            return null;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(source, identity);
        var resolution = RoofStructuralEdgeIdentityResolver.Resolve(hip, provenance);
        var elevation = RoofPhysicalElevationStore.Read(owner).Data;
        var desired = RoofAutomaticStructuralRafterPlanner.Create(
            resolution, ownerPhysicalState: elevation is null ? null :
                RoofPhysicalElevationRules.ToState(elevation, hip.RiseMm));
        if (!resolution.IsValid || !desired.IsValid)
        {
            return null;
        }
        var existingKeys = new HashSet<RoofStructuralLogicalKey>();
        if (!structuralReconcilePending)
        {
            var metadata = new AutoCadTimberElementMetadataStore(transaction);
            foreach (var id in RoofStructuralGeneratedStore.FindByOwner(
                         database, transaction, owner.Handle.ToString()))
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                        transaction, id, OpenMode.ForRead, out var line, database) ||
                    line is null ||
                    RoofStructuralGeneratedStore.Read(line).Data is not { } structural ||
                    !metadata.TryRead(line, out TimberElementData? timber) ||
                    timber is null ||
                    !existingKeys.Add(structural.LogicalKey))
                {
                    return null;
                }
            }
        }
        var resolvedByKey = resolution.Edges.ToDictionary(edge =>
            edge.StructuralIdentity);
        var sources = new List<RoofStructuralRafterTrimSource>();
        foreach (var item in desired.Items)
        {
            if (!resolvedByKey.TryGetValue(item.LogicalKey, out var edge))
            {
                return null;
            }
            var role = item.LogicalKey.Role switch
            {
                RoofStructuralRole.Hip => RoofRafterBoundaryRole.Hip,
                RoofStructuralRole.Valley => RoofRafterBoundaryRole.Valley,
                _ => throw new InvalidOperationException("Unsupported structural trim role."),
            };
            sources.Add(new RoofStructuralRafterTrimSource(
                edge.TopologyEdgeIndex,
                role,
                item.Segment3D,
                // Owner settings are the shared width SSOT for the physical
                // structural side planes and the ordinary physical cut.
                item.TimberData.WidthMm));
        }
        return sources;
    }

    private static Solid3d CreateSolid(RoofAutomaticRafterPhysicalMember member)
    {
        var horizontalCut = member.HorizontalCut;
        var structuralCut = member.StructuralCut;
        var ridgeOverlapCut = member.RidgeOverlapCut;
        var vertices = horizontalCut?.SourcePrismVertices ??
            structuralCut?.SourcePrismVertices ??
            ridgeOverlapCut?.SourcePrismVertices ?? member.SolidVertices;
        if (vertices.Count != 8)
        {
            throw new InvalidOperationException("Expected eight rafter prism vertices.");
        }

        // Transient unopened curves are required by Region.CreateFromCurves.
        // Core supplies the transient side profile. Horizontal additionally
        // supplies the final clipped-body geometry and WCS eave plane; Slice
        // is only the host materialization of that already-solved half-space.
        using var upper = new Line(Map(vertices[0]), Map(vertices[2]));
        using var inner = new Line(Map(vertices[2]), Map(vertices[6]));
        using var lower = new Line(Map(vertices[6]), Map(vertices[4]));
        using var outer = new Line(Map(vertices[4]), Map(vertices[0]));
        var regions = CadRegion.CreateFromCurves(
            new DBObjectCollection { upper, inner, lower, outer });
        try
        {
            if (regions.Count != 1 || regions[0] is not CadRegion region)
            {
                throw new InvalidOperationException("Rafter side profile did not produce one region.");
            }

            var width = Map(vertices[1]) - Map(vertices[0]);
            var solid = new Solid3d();
            try
            {
                solid.CreateExtrudedSolid(region, width, new SweepOptions());
                if (horizontalCut is not null)
                {
                    using var plane = new Plane(
                        new Point3d(0d, 0d, horizontalCut.EaveElevationMm),
                        Vector3d.ZAxis);
                    // Solid3d.Slice retains the positive-normal half in this
                    // solid. Core verified that the physical upslope body is
                    // on this side; no negative half is created or persisted.
                    solid.Slice(plane, false);
                }
                if (structuralCut is not null)
                {
                    using var plane = new Plane(
                        Map(structuralCut.PlanePoint),
                        new Vector3d(
                            structuralCut.PlaneNormal.X,
                            structuralCut.PlaneNormal.Y,
                            structuralCut.PlaneNormal.Z));
                    solid.Slice(plane, false);
                }
                if (ridgeOverlapCut is not null)
                {
                    using var plane = new Plane(
                        Map(ridgeOverlapCut.PlanePoint),
                        new Vector3d(
                            ridgeOverlapCut.RetainedNormal.X,
                            ridgeOverlapCut.RetainedNormal.Y,
                            ridgeOverlapCut.RetainedNormal.Z));
                    solid.Slice(plane, false);
                }
                return solid;
            }
            catch
            {
                solid.Dispose();
                throw;
            }
        }
        finally
        {
            foreach (DBObject region in regions)
            {
                region.Dispose();
            }
        }
    }

    private static Point3d Map(RoofPoint3D point) => new(point.X, point.Y, point.Z);

    private static void EnsureLayer(Database database, Transaction transaction)
    {
        var table = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        if (table.Has(LayerName))
        {
            return;
        }

        table.UpgradeOpen();
        var layer = new LayerTableRecord { Name = LayerName };
        table.Add(layer);
        transaction.AddNewlyCreatedDBObject(layer, true);
    }
}
