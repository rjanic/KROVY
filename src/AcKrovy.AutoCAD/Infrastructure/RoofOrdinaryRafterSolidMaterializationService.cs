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

    /// <summary>Capture full inputs before detach, without replay overrides or drawing writes.</summary>
    internal static bool TryCaptureOrdinaryBuildState(Database database, Transaction transaction,
        Polyline owner, RoofGeneratedMemberKey key, RoofSegment3D commandStart,
        out RoofOrdinaryPhysicalBuildState? state, bool historicalIndependent = false,
        RoofOrdinaryPhysicalBuildStateTrace? trace = null)
    {
        state = null;
        var input = RoofPolylineExtractor.Extract(owner);
        var footprint = RoofFootprintValidator.Validate(input);
        var definition = RoofDefinitionStore.Read(owner).Data;
        trace?.Add("definitionResolved", definition is not null);
        trace?.Add("footprintResolved", footprint.Footprint is not null);
        var geometry = footprint.Footprint is null || definition is null ? null :
            RoofDefinitionPersistence.Restore(input, footprint.Footprint, definition).Geometry;
        var generated = RoofGeneratedTimberStore.FindByOwner(database, transaction, owner.Handle.ToString());
        trace?.Add("geometryResolved", geometry is not null);
        trace?.Add("generatedSourceCount", generated.Count);
        if (geometry is null)
        { trace?.Fail("RoofDefinitionPersistence.Restore:geometry_unavailable"); return false; }
        if (RoofPhysicalElevationStore.Read(owner).Data is not { } elevation)
        { trace?.Add("elevationResolved", false); trace?.Fail("RoofPhysicalElevationStore.Read"); return false; }
        trace?.Add("elevationResolved", true);
        trace?.Add("resolvedEaveElevationMm", elevation.ResolvedEaveRelativeElevationMm);
        if (!RoofGeneratedRafterSetService.TryRecoverRecipe(database, transaction, generated, out var recipe, trace))
        { trace?.Add("recipeResolved", false); trace?.Fail("RoofGeneratedRafterSetService.TryRecoverRecipe"); return false; }
        trace?.Add("recipeResolved", true);
        trace?.Add("recipeSection", FormattableString.Invariant($"{recipe.WidthMm:R}x{recipe.HeightMm:R}"));
        trace?.Add("recipeMaterial", recipe.Material);
        trace?.Add("recipeMaximumSpacingMm", recipe.MaximumSpacingMm);
        var solved = RoofRafterLayoutSolver.Solve(geometry,
            AutoCadRoofRafterSpacingStore.CreateLayoutParameters(database, recipe.MaximumSpacingMm, recipe.WidthMm));
        trace?.Add("layoutResolved", solved.IsValid && solved.Layout is not null);
        if (!solved.IsValid || solved.Layout is null)
        { trace?.Fail("RoofRafterLayoutSolver.Solve"); return false; }
        if (!TryResolveOrdinaryPhysicalBuildContext(database, transaction, owner, geometry, solved.Layout,
                recipe, false, out var context, trace))
        { trace?.Add("physicalContextResolved", false); return false; }
        trace?.Add("physicalContextResolved", true);
        var index = key.StationIndex;
        var anchor = key.RoofFace == RafterRoofFace.Face0 && context.HipGeometry is not null
            ? context.FaceLayout.Segments.ElementAtOrDefault(index)
            : context.FaceLayout.Segments.FirstOrDefault(s => s.StationIndex == index &&
                s.SourceFaceIndex == (int)key.RoofFace);
        trace?.Add("faceResolved", anchor is not null);
        if (anchor is null)
        { trace?.Fail("TryCaptureOrdinaryBuildState:provenance_anchor_not_in_current_layout"); return false; }
        trace?.Add("anchorFace", anchor.SourceFaceIndex);
        trace?.Add("anchorEaveEdge", anchor.SourceEaveEdgeIndex);
        trace?.Add("anchorStation", anchor.StationIndex);
        var reference = historicalIndependent
            ? new RoofSegment3D(new(anchor.PlanStart.X, anchor.PlanStart.Y, 0), new(anchor.PlanEnd.X, anchor.PlanEnd.Y, 0))
            : commandStart;
        var captured = RoofOrdinaryPhysicalBuildStateRules.Capture(context.Topology, anchor, key,
            elevation.ResolvedEaveRelativeElevationMm, recipe.WidthMm, recipe.HeightMm,
            new(elevation.LowerEndCutMode, elevation.RidgeJoinMode), context.StructuralSources, reference);
        if (historicalIndependent)
        {
            var rebased = RoofOrdinaryPhysicalBuildStateRules.TryRebase(captured, commandStart, out state);
            trace?.Rebase("provenanceRebase", captured, commandStart, rebased);
            return rebased;
        }
        state = captured;
        var valid = RoofOrdinaryPhysicalBuildStateRules.IsValid(state);
        if (!valid) trace?.Fail("RoofOrdinaryPhysicalBuildStateRules.IsValid:captured_provenance");
        return valid;
    }

    internal static Solid3d MaterializeOrdinaryMember(RoofAutomaticRafterPhysicalMember member) => CreateSolid(member);

    // Read-only Core model access for the dependent structural physical timber.
    // Reuses the exact ordinary builder and replay path; no parallel end-cut formula.
    public static bool TryBuildExistingModelInTransaction(
        Database database,
        Transaction transaction,
        Polyline owner,
        HipRoofGeometry hip,
        out RoofAutomaticRafterPhysicalModel? model, bool structuralReconcilePending = false, bool persistAttachedIdentity = false)
    {
        model = null;
        var ownerReference = owner.Handle.ToString();
        var existing = RoofGeneratedTimberStore.FindByOwner(
            database, transaction, ownerReference);
        if (existing.Count == 0)
        {
            var physical = RoofPhysicalElevationStore.Read(owner).Data;
            var structural = ResolveStructuralSources(database, transaction, owner, hip, structuralReconcilePending);
            return physical?.Physical3DEnabled == true && structural is not null &&
                TryAppendAttachedModel(database, transaction, owner, hip,
                    new RoofFaceRafterLayout(0d, Array.Empty<RoofFaceRafterSegment>(), string.Empty),
                    physical, structural, new RoofAutomaticRafterPhysicalModel(ownerReference,
                        Array.Empty<RoofAutomaticRafterPhysicalMember>()), out model, persistAttachedIdentity);
        }
        if (!RoofGeneratedRafterSetService.TryRecoverRecipe(
                database, transaction, existing, out var recipe))
            return false;
        var solved = RoofRafterLayoutSolver.Solve(
            hip, AutoCadRoofRafterSpacingStore.CreateLayoutParameters(
                database, recipe.MaximumSpacingMm, recipe.WidthMm));
        if (!solved.IsValid || solved.Layout is null)
            return false;
        var livePlans = new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry>();
        foreach (var id in existing)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(transaction, id, OpenMode.ForRead,
                    out var planLine, database) || planLine is null ||
                RoofGeneratedTimberStore.Read(planLine).Data is not { } generated ||
                !livePlans.TryAdd(RoofGeneratedMemberKey.From(generated), new(
                    new(planLine.StartPoint.X, planLine.StartPoint.Y, planLine.StartPoint.Z),
                    new(planLine.EndPoint.X, planLine.EndPoint.Y, planLine.EndPoint.Z)))) return false;
        }
        var replay = RoofGeneratedMemberReplayPlanner.CreateForExistingPhysicalMembers(
            solved.Layout, RoofDefinitionStore.Read(owner).Data?.Overrides, livePlans);
        replay = RoofAcceptedOrdinaryOverrideReplayRules.Apply(
            replay, RoofDefinitionStore.Read(owner).Data?.EditState ?? RoofEditState.Locked, livePlans);
        var faceLayout = RoofFaceRafterLayoutService.Create(
            hip.Topology, recipe.MaximumSpacingMm);
        var elevation = RoofPhysicalElevationStore.Read(owner).Data;
        var sources = ResolveStructuralSources(
            database, transaction, owner, hip, structuralReconcilePending);
        if (!(replay.IsValid && faceLayout.IsValid && faceLayout.Layout is not null &&
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
            MatchesGeneratedMemberKeys(database, transaction, ownerReference, model))) return false;
        return TryAppendAttachedModel(database, transaction, owner, hip, faceLayout.Layout,
            elevation!, sources!, model!, out model, persistAttachedIdentity);
    }

    /// <summary>Rebuild only the physical bodies whose authoritative 2D member
    /// changed. The caller's transaction also owns the accepted 2D override.</summary>
    public static bool TryReconcileModifiedMembersInTransaction(
        Database database,
        Transaction transaction,
        Polyline owner,
        IRoofGeometry geometry,
        IReadOnlyCollection<ObjectId> modifiedIds,
        bool restoreStretchCollateral = false,
        IReadOnlyCollection<ObjectId>? acceptedPlanIds = null)
    {
        if (geometry is not HipRoofGeometry hip ||
            RoofPhysicalElevationStore.Read(owner).Data is not { Physical3DEnabled: true } elevation)
            return true;

        var ownerReference = owner.Handle.ToString();
        var changedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in acceptedPlanIds ?? modifiedIds)
            if (AutoCadObjectIdAccess.TryGetObject<Line>(transaction, id, OpenMode.ForRead,
                    out var line, database) && line is not null &&
                TryGetPlanPhysicalIdentity(line, ownerReference, out var identity)) changedKeys.Add(identity);
        if (restoreStretchCollateral)
            foreach (var id in modifiedIds)
                if (AutoCadObjectIdAccess.TryGetObject<Solid3d>(transaction, id, OpenMode.ForRead,
                        out var solid, database) && solid is not null &&
                    RoofPhysical3DGeneratedStore.Read(solid).Data is { Role: RoofPhysical3DGeneratedRole.OrdinaryRafterSolid } data &&
                    string.Equals(data.RoofOwnerReference, ownerReference, StringComparison.OrdinalIgnoreCase) &&
                    !data.StructuralId.StartsWith("AttachedManual:", StringComparison.Ordinal))
                    changedKeys.Add(data.StructuralId);
        if (changedKeys.Count == 0) return true;
        return TryReconcileSemanticMembersInTransaction(database, transaction, owner, hip,
            changedKeys, Array.Empty<ObjectId>(), allowCardinalityChanges: false);
    }

    public static bool TryGetPlanPhysicalIdentity(Line line, string ownerReference, out string identity)
    {
        identity = string.Empty;
        if (RoofGeneratedTimberStore.Read(line).Data is { } generated &&
            string.Equals(generated.RoofOwnerReference, ownerReference, StringComparison.OrdinalIgnoreCase))
            identity = PhysicalMemberId(RoofGeneratedMemberKey.From(generated));
        else if (RoofAttachedManualTimberStore.Read(line).Data is { } attached &&
                 string.Equals(attached.RoofOwnerReference, ownerReference, StringComparison.OrdinalIgnoreCase))
            identity = RoofAttachedManualIdentityRules.PhysicalKey(attached);
        return identity.Length > 0;
    }

    /// <summary>One semantic reconciliation engine for split, clone, replacement and
    /// AttachedManual edits. Never inspects native solid geometry.</summary>
    public static bool TryReconcileSemanticMembersInTransaction(Database database, Transaction transaction,
        Polyline owner, IRoofGeometry geometry, IReadOnlyCollection<string> changedKeys,
        IReadOnlyCollection<ObjectId> collateralIds, bool allowCardinalityChanges = true, bool structuralReconcilePending = false)
    {
        if (geometry is not HipRoofGeometry hip ||
            RoofPhysicalElevationStore.Read(owner).Data is not { Physical3DEnabled: true } elevation) return true;
        if (!TryBuildExistingModelInTransaction(database, transaction, owner, hip, out var model, structuralReconcilePending, persistAttachedIdentity: true) || model is null)
            return false;
        var members = model.Members.ToDictionary(member => member.PhysicalIdentity, StringComparer.Ordinal);
        var existing = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        var bindings = new List<RoofOrdinaryPhysicalBinding>();
        foreach (var id in RoofPhysical3DGeneratedStore.FindByOwner(database, transaction, owner.Handle.ToString()))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(transaction, id, OpenMode.ForRead,
                    out var entity, database) || entity is null ||
                RoofPhysical3DGeneratedStore.Read(entity).Data is not { Role: RoofPhysical3DGeneratedRole.OrdinaryRafterSolid } data)
                continue;
            if (entity is not Solid3d) return false;
            var binding = entity.Handle.ToString();
            existing.Add(binding, id);
            bindings.Add(new RoofOrdinaryPhysicalBinding(binding, data.StructuralId, collateralIds.Contains(id)));
        }
        if (!RoofOrdinaryPhysicalReconciliationRules.TryPlan(members.Keys.ToArray(), changedKeys,
                bindings, allowCardinalityChanges, out var plan) || plan is null) return false;
        if (plan.RebuildKeys.Count == 0 && plan.RemoveBodyIdentities.Count == 0)
            return RoofOrdinaryPhysicalReconciliationRules.IsCanonical(members.Keys.ToArray(), bindings.Select(body => body.SemanticKey).ToArray());
        var removeIds = plan.RemoveBodyIdentities.Select(binding => existing[binding]).ToArray();
        _ = RoofAssemblyGroupSyncService.DetachMembersBeforeErase(database, transaction, owner.ObjectId, removeIds);
        EnsureLayer(database, transaction);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForWrite);
        var signature = RoofGeneratedTimberStore.FindByOwner(database, transaction, owner.Handle.ToString())
            .Select(id => RoofGeneratedTimberStore.Read((Entity)transaction.GetObject(id, OpenMode.ForRead)).Data)
            .FirstOrDefault(data => data is not null)?.LayoutSignature ?? "AttachedManual";
        foreach (var key in plan.RebuildKeys)
        {
            var solid = CreateSolid(members[key]);
            var appended = false;
            try
            {
                solid.SetDatabaseDefaults(database);
                solid.Layer = LayerName;
                solid.Visible = elevation.DisplayVisibility is RoofPhysicalDisplayVisibility.Both or RoofPhysicalDisplayVisibility.Model3D;
                modelSpace.AppendEntity(solid);
                appended = true;
                transaction.AddNewlyCreatedDBObject(solid, true);
                RoofPhysical3DGeneratedStore.Write(solid, transaction, RoofPhysical3DGeneratedDataRules.Create(
                    owner.Handle.ToString(), RoofPhysical3DGeneratedRole.OrdinaryRafterSolid, key, signature));
            }
            finally { if (!appended) solid.Dispose(); }
        }
        foreach (var id in removeIds)
            ((Entity)transaction.GetObject(id, OpenMode.ForWrite)).Erase();
#if DEBUG
        RoofPhysical3DHostDiagnostics.PhysicalReconcile(database, owner.Handle.ToString(),
            plan.RebuildKeys.Count, removeIds.Length, plan.RebuildKeys);
#endif
        var actual = RoofPhysical3DGeneratedStore.FindByOwner(database, transaction, owner.Handle.ToString())
            .Select(id => RoofPhysical3DGeneratedStore.Read((Entity)transaction.GetObject(id, OpenMode.ForRead)).Data)
            .Where(data => data?.Role == RoofPhysical3DGeneratedRole.OrdinaryRafterSolid)
            .Select(data => data!.StructuralId).ToArray();
#if DEBUG
        foreach (var changedKey in changedKeys)
            if (members.TryGetValue(changedKey, out var gripMember))
                RoofGeneratedMemberManualEditDiag.CompleteOrdinaryGripPhysicalFrame(owner.Handle.ToString(),
                    hip.Topology, gripMember, elevation.ResolvedEaveRelativeElevationMm);
#endif
        return RoofOrdinaryPhysicalReconciliationRules.IsCanonical(members.Keys.ToArray(), actual);
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
        foreach (var id in RoofAttachedManualTimberStore.FindByOwner(database, transaction, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(transaction, id, OpenMode.ForRead,
                    out var line, database) || line is null || !line.Visible) continue;
            if (RoofAttachedManualTimberStore.Read(line).Data is { } data &&
                data.AnchorGeneratedMemberKey?.MemberKind == RoofGeneratedTimberKind.Rafter &&
                !authoritativeLines.TryAdd(RoofAttachedManualIdentityRules.PhysicalKey(data), id)) return false;
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

        var expected = model.Members.Select(member => member.PhysicalIdentity)
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
        IReadOnlyCollection<RoofGeneratedMemberKey> suppressedKeys, IReadOnlyCollection<string>? erasedAttachedKeys = null)
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
        foreach (var id in RoofAttachedManualTimberStore.FindByOwner(database, transaction, ownerReference))
            if (AutoCadObjectIdAccess.TryGetObject<Line>(transaction, id, OpenMode.ForRead, out var line, database) &&
                line is { Visible: true } && RoofAttachedManualTimberStore.Read(line).Data is { } attached &&
                attached.AnchorGeneratedMemberKey?.MemberKind == RoofGeneratedTimberKind.Rafter &&
                !activeIds.Add(RoofAttachedManualIdentityRules.PhysicalKey(attached))) return false;

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
        var expectedBefore = activeIds.Concat(suppressedIds).Concat(erasedAttachedKeys ?? Array.Empty<string>()).ToHashSet();
        if (physical.Count != expectedBefore.Count ||
            !physical.Keys.ToHashSet().SetEquals(expectedBefore))
            return false;

        // Detach the exact physical ObjectIds while they are still live. The
        // accepted ERASE transaction then removes their bodies; EnsureGroup
        // must never see an erased member waiting for a deferred reattach.
        var suppressedSolidIds = suppressedIds.Concat(erasedAttachedKeys ?? Array.Empty<string>()).Distinct()
            .Select(id => physical[id]).ToArray();
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
            if (geometry is HipRoofGeometry && RoofPhysicalElevationStore.Read(owner).Data?.Physical3DEnabled == true)
                return TryReconcileSemanticMembersInTransaction(database, transaction, owner, geometry,
                    Array.Empty<string>(), Array.Empty<ObjectId>());
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
        replay = RoofAcceptedOrdinaryOverrideReplayRules.Apply(
            replay, RoofDefinitionStore.Read(owner).Data?.EditState ?? RoofEditState.Locked);
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
        bool structuralReconcilePending = false, bool includeAttachedManual = true)
    {
        var ownerReference = owner.Handle.ToString();
        var elevation = RoofPhysicalElevationStore.Read(owner).Data;
        var footprint = RoofFootprintValidator.Validate(RoofPolylineExtractor.Extract(owner));
        if (elevation?.Physical3DEnabled != true ||
            !footprint.IsValid || footprint.Footprint is null ||
            !TryResolveOrdinaryPhysicalBuildContext(
                database, transaction, owner, geometry, layout, recipe,
                structuralReconcilePending, out var context))
        {
            RoofPhysical3DMaterializationService.EraseOrdinaryRafterSolids(
                database, transaction, ownerReference);
            return;
        }

        var failureReason = "InvalidFaceLayoutOrMemberCount";
        if (!RoofAutomaticRafterPhysicalBuilder.TryBuild(
                ownerReference, context.Topology, context.FaceLayout, layout,
                elevation.ResolvedEaveRelativeElevationMm,
                recipe.WidthMm, recipe.HeightMm,
                new RoofAutomaticRafterPhysicalSettings(
                    elevation.LowerEndCutMode,
                    elevation.RidgeJoinMode), replayPlan, context.StructuralSources,
                out var model, out failureReason) || model is null ||
            model.Members.Count != replayPlan.MaterializedCount)
        {
            throw new InvalidOperationException(
                $"Ordinary physical rafter model is inconsistent: {failureReason} " +
                $"member={RoofGeneratedPlanRebuildFailureRules.ExtractMember(failureReason) ?? "-"}.");
        }
        if (!MatchesGeneratedMemberKeys(database, transaction, ownerReference, model))
            throw new InvalidOperationException(
                "Ordinary physical rafter model is inconsistent: GeneratedMemberKeyMismatch " +
                $"expected={model.Members.Count} planKeys={RoofGeneratedTimberStore.FindByOwner(database, transaction, ownerReference).Count}.");

        if (includeAttachedManual &&
            context.HipGeometry is { } attachedHip &&
            (!TryAppendAttachedModel(database, transaction, owner, attachedHip,
                context.FaceLayout, elevation, context.StructuralSources, model,
                out model, persistAttachedIdentity: true) || model is null))
            throw new InvalidOperationException("AttachedManual physical model is inconsistent.");

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
            Solid3d solid;
            try
            {
                solid = CreateSolid(member);
            }
            catch (System.Exception ex)
            {
                throw new InvalidOperationException(
                    $"CreateSolid failed member={member.MemberKey} " +
                    $"plan=({member.PlanAxis.Start.X},{member.PlanAxis.Start.Y})->" +
                    $"({member.PlanAxis.End.X},{member.PlanAxis.End.Y}) " +
                    $"overrideCarriesTranslation={member.PlanAxis.Start.DistanceTo(member.PlanAxis.End) > 0} " +
                    $"failureReason={ex.Message}",
                    ex);
            }

            solid.SetDatabaseDefaults(database);
            solid.Layer = LayerName;
            solid.Visible = showModel3D;
            modelSpace.AppendEntity(solid);
            transaction.AddNewlyCreatedDBObject(solid, true);
            var memberId = member.PhysicalIdentity;
            RoofPhysical3DGeneratedStore.Write(
                solid, transaction,
                RoofPhysical3DGeneratedDataRules.Create(
                    ownerReference,
                    RoofPhysical3DGeneratedRole.OrdinaryRafterSolid,
                    memberId,
                    signature));
        }
    }

    /// <summary>
    /// Explicit ordinary Physical3D support: Hip (existing Face0-flattened) and
    /// SimpleGable (Face0+Face1 ordinary only). Asymmetric/Monopitch stay closed.
    /// </summary>
    private static bool TryResolveOrdinaryPhysicalBuildContext(
        Database database,
        Transaction transaction,
        Polyline owner,
        IRoofGeometry geometry,
        RoofRafterLayout layout,
        RoofRafterGenerationRecipe recipe,
        bool structuralReconcilePending,
        out OrdinaryPhysicalBuildContext context, RoofOrdinaryPhysicalBuildStateTrace? trace = null)
    {
        context = null!;
        if (geometry is HipRoofGeometry hip)
        {
            var faceLayout = RoofFaceRafterLayoutService.Create(
                hip.Topology, recipe.MaximumSpacingMm);
            var structuralSources = ResolveStructuralSources(
                database, transaction, owner, hip, structuralReconcilePending, trace);
            if (structuralSources is null ||
                !faceLayout.IsValid || faceLayout.Layout is null)
            {
                if (structuralSources is not null) trace?.Fail("RoofFaceRafterLayoutService.Create:" + faceLayout.Error);
                return false;
            }

            context = new OrdinaryPhysicalBuildContext(
                hip.Topology, faceLayout.Layout, structuralSources, hip);
            return true;
        }

        if (geometry is SimpleGableRoofGeometry
            {
                Kind: RoofKind.SimpleGable
            } gable)
        {
            if (!SimpleGableRoofTopologyAdapter.TryCreate(
                    gable, out var topology, out var topologyReason))
            {
                trace?.Fail("SimpleGableRoofTopologyAdapter.TryCreate:" + topologyReason);
                return false;
            }
            if (!SimpleGableOrdinaryRafterPhysicalAdapter.TryCreateFaceLayout(
                    gable, topology, layout, out var faceLayout, out var layoutReason))
            {
                trace?.Fail("SimpleGableOrdinaryRafterPhysicalAdapter.TryCreateFaceLayout:" + layoutReason);
                return false;
            }

            // Ordinary SimpleGable Physical3D never consumes Structural Hip/Valley.
            context = new OrdinaryPhysicalBuildContext(
                topology, faceLayout,
                Array.Empty<RoofStructuralRafterTrimSource>(),
                HipGeometry: null);
            return true;
        }

        trace?.Fail("TryResolveOrdinaryPhysicalBuildContext:unsupported_geometry:" + geometry.GetType().Name);
        return false;
    }

    private sealed record OrdinaryPhysicalBuildContext(
        RoofTopology Topology,
        RoofFaceRafterLayout FaceLayout,
        IReadOnlyList<RoofStructuralRafterTrimSource> StructuralSources,
        HipRoofGeometry? HipGeometry);

    public static bool TryReconcileAttachedAfterReplay(Database database, Transaction transaction,
        Polyline owner, IRoofGeometry geometry)
    {
        var keys = RoofAttachedManualTimberStore.FindByOwner(database, transaction, owner.Handle.ToString())
            .Select(id => RoofAttachedManualTimberStore.Read((Entity)transaction.GetObject(id, OpenMode.ForRead)).Data)
            .Where(data => data?.AnchorGeneratedMemberKey?.MemberKind == RoofGeneratedTimberKind.Rafter)
            .Select(data => RoofAttachedManualIdentityRules.PhysicalKey(data!)).ToArray();
        return TryReconcileSemanticMembersInTransaction(database, transaction, owner, geometry,
            keys, Array.Empty<ObjectId>(), structuralReconcilePending: true);
    }

    private static bool TryAppendAttachedModel(Database database, Transaction transaction,
        Polyline owner, HipRoofGeometry hip, RoofFaceRafterLayout faceLayout,
        RoofPhysicalElevationData elevation, IReadOnlyList<RoofStructuralRafterTrimSource> sources,
        RoofAutomaticRafterPhysicalModel generated, out RoofAutomaticRafterPhysicalModel? model, bool persistAttachedIdentity = false)
    {
        var inputs = new List<RoofAttachedManualPhysicalInput>();
        var metadata = new AutoCadTimberElementMetadataStore(transaction);
        foreach (var id in RoofAttachedManualTimberStore.FindByOwner(database, transaction, owner.Handle.ToString()))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(transaction, id, OpenMode.ForRead,
                    out var line, database) || line is null ||
                RoofAttachedManualTimberStore.Read(line).Data is not { } data) return Fail(out model);
            if (data.AnchorGeneratedMemberKey?.MemberKind != RoofGeneratedTimberKind.Rafter) continue;
            if (!metadata.TryRead(line, out var timber) || timber is null) return Fail(out model);
            if (persistAttachedIdentity && RoofAttachedManualIdentityRules.Upgrade(data) is { } upgraded && upgraded != data)
            {
                line.UpgradeOpen();
                RoofAttachedManualTimberStore.Write(line, transaction, upgraded);
                data = upgraded;
            }
            inputs.Add(new RoofAttachedManualPhysicalInput(data, new RoofSegment3D(
                new RoofPoint3D(line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z),
                new RoofPoint3D(line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z)),
                timber.WidthMm, timber.HeightMm, line.Visible));
        }
        return RoofAttachedManualPhysicalBuilder.TryAppend(hip.Topology, faceLayout, generated, inputs,
            elevation.ResolvedEaveRelativeElevationMm, new RoofAutomaticRafterPhysicalSettings(
                elevation.LowerEndCutMode, elevation.RidgeJoinMode), sources, out model, out _);
    }

    private static bool Fail(out RoofAutomaticRafterPhysicalModel? model) { model = null; return false; }

    private static bool MatchesGeneratedMemberKeys(
        Database database,
        Transaction transaction,
        string ownerReference,
        RoofAutomaticRafterPhysicalModel model)
    {
        var generatedMembers = model.Members.Where(member => member.AttachedManualIdentity is null).ToArray();
        var expected = generatedMembers.Select(member => member.MemberKey).ToHashSet();
        if (expected.Count != generatedMembers.Length)
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

    internal static IReadOnlyList<RoofStructuralRafterTrimSource>? ResolveStructuralSources(
        Database database,
        Transaction transaction,
        Polyline owner,
        HipRoofGeometry hip,
        bool structuralReconcilePending, RoofOrdinaryPhysicalBuildStateTrace? trace = null)
    {
        var source = RoofPolylineExtractor.Extract(owner);
        // Read the same persisted identity as structural materialization. On
        // first creation and whole-roof MIRROR rehome run after ordinary
        // generation, so derive the same future sequential identity without
        // an early database write. Other malformed identities fail closed.
        var stored = RoofBoundaryIdentityStore.Read(owner);
        if (stored.Exists && stored.Data is null &&
            stored.Error != RoofBoundaryIdentityError.CurrentRawWindingMismatch)
        {
            trace?.Fail("RoofBoundaryIdentityStore.Read:" + stored.Error);
            return null;
        }
        var identity = stored.Data;
        if (identity is null)
        {
            var normalized = RoofFootprintValidator.ValidateWithProvenance(source);
            if (!normalized.Validation.IsValid)
            {
                trace?.Fail("RoofFootprintValidator.ValidateWithProvenance");
                return null;
            }
            identity = RoofBoundaryIdentityRules.CreateSequential(
                normalized.EdgeProvenance.Count,
                normalized.Validation.SourceOrientation).Identity;
        }
        if (identity is null)
        {
            trace?.Fail("RoofBoundaryIdentityRules.CreateSequential");
            return null;
        }
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(source, identity);
        var resolution = RoofStructuralEdgeIdentityResolver.Resolve(hip, provenance);
        var elevation = RoofPhysicalElevationStore.Read(owner).Data;
        var desired = RoofAutomaticStructuralRafterPlanner.Create(
            resolution, ownerPhysicalState: elevation is null ? null :
                RoofPhysicalElevationRules.ToState(elevation, hip.RiseMm));
        if (!resolution.IsValid || !desired.IsValid)
        {
            trace?.Fail(!resolution.IsValid ? "RoofStructuralEdgeIdentityResolver.Resolve" : "RoofAutomaticStructuralRafterPlanner.Create");
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
                    line is null)
                {
                    trace?.Fail("AutoCadObjectIdAccess.TryGetObject:structural_line");
                    trace?.Add("structuralFailureHandle", id.Handle);
                    return null;
                }
                if (RoofStructuralGeneratedStore.Read(line).Data is not { } structural)
                {
                    trace?.Fail("RoofStructuralGeneratedStore.Read");
                    trace?.Add("structuralFailureHandle", id.Handle);
                    return null;
                }
                if (!metadata.TryRead(line, out TimberElementData? timber) || timber is null)
                {
                    trace?.Fail("AutoCadTimberElementMetadataStore.TryRead:structural_line");
                    trace?.Add("structuralFailureHandle", id.Handle);
                    return null;
                }
                if (!existingKeys.Add(structural.LogicalKey))
                {
                    trace?.Fail("ResolveStructuralSources:duplicate_structural_logical_key");
                    trace?.Add("structuralFailureHandle", id.Handle);
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
                trace?.Fail("ResolveStructuralSources:desired_structural_key_not_resolved");
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
        var ridgeMeetCut = member.RidgeMeetCut;
        var sourcePrism = horizontalCut?.SourcePrismVertices ??
            structuralCut?.SourcePrismVertices ??
            ridgeOverlapCut?.SourcePrismVertices ??
            ridgeMeetCut?.SourcePrismVertices;
        // Prefer source-prism + Slice (host materialization of Core half-spaces).
        // When AutoCAD Region rejects that profile after Plan2D already succeeded,
        // fall back to Core's already-clipped SolidVertices without re-slicing.
        if (sourcePrism is { Count: 8 })
        {
            try
            {
                return CreateExtrudedSolidFromPrism(
                    sourcePrism,
                    applyHorizontal: horizontalCut is not null,
                    horizontalCut,
                    structuralCut,
                    ridgeOverlapCut,
                    ridgeMeetCut);
            }
            catch (InvalidOperationException) when (member.SolidVertices.Count == 8 && ridgeMeetCut is null)
            {
                return CreateExtrudedSolidFromPrism(
                    member.SolidVertices,
                    applyHorizontal: false,
                    horizontalCut: null,
                    structuralCut: null,
                    ridgeOverlapCut: null,
                    ridgeMeetCut: null);
            }
        }

        if (member.SolidVertices.Count != 8)
            throw new InvalidOperationException(
                $"Expected eight rafter prism vertices. member={member.MemberKey}");
        return CreateExtrudedSolidFromPrism(
            member.SolidVertices,
            applyHorizontal: false,
            horizontalCut: null,
            structuralCut: null,
            ridgeOverlapCut: null,
            ridgeMeetCut: null);
    }

    private static Solid3d CreateExtrudedSolidFromPrism(
        IReadOnlyList<RoofPoint3D> vertices,
        bool applyHorizontal,
        RoofHorizontalRafterCut? horizontalCut,
        RoofStructuralRafterSideCut? structuralCut,
        RoofRidgeOverlapCut? ridgeOverlapCut,
        RoofRidgeMeetCut? ridgeMeetCut)
    {
        // Transient unopened curves are required by Region.CreateFromCurves.
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
                if (applyHorizontal && horizontalCut is not null)
                {
                    using var plane = new Plane(
                        new Point3d(0d, 0d, horizontalCut.EaveElevationMm),
                        Vector3d.ZAxis);
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
                if (ridgeMeetCut is not null)
                {
                    using var plane = new Plane(
                        Map(ridgeMeetCut.PlanePoint),
                        new Vector3d(ridgeMeetCut.RetainedNormal.X,
                            ridgeMeetCut.RetainedNormal.Y,
                            ridgeMeetCut.RetainedNormal.Z));
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
