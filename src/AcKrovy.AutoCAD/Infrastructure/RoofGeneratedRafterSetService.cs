using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.AutoCAD.Settings;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Shared Stage 6 automatic-rafter materialization and supported-STRETCH replacement.
/// Generated rafters are regenerable roof-owned intelligent timber; the prior set's
/// unanimous Timber + RoofGeneratedTimber recipe is authority (not global last-used).
/// </summary>
internal static class RoofGeneratedRafterSetService
{
    public enum ReplacementOutcome
    {
        NotApplicable = 0,
        Replaced = 1,
        SkippedAmbiguousRecipe = 2,
        SkippedInvalidLayout = 3,
        Failed = 4,
    }

    /// <summary>Last <see cref="ReplacementOutcome.Failed"/> detail from ReplacePreparedSetWithRecipe.</summary>
    internal static RoofGeneratedPlanRebuildFailureRules.FailureDetail? LastFailureDetail { get; private set; }

#if DEBUG
    /// <summary>
    /// Test seam: throw once immediately after Plan2D create / before ordinary Physical3D
    /// returns, proving exception detail reaches HardFailure diagnostics.
    /// </summary>
    internal static string? InjectPostCreateOrdinaryPhysicalFailureOnce;
#endif

    public static bool TryRecoverRecipe(
        Database database,
        Transaction transaction,
        IReadOnlyList<ObjectId> generatedIds,
        out RoofRafterGenerationRecipe recipe, RoofOrdinaryPhysicalBuildStateTrace? trace = null)
    {
        recipe = default!;
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        if (generatedIds is null || generatedIds.Count == 0)
        {
            trace?.Add("recipeFailure", "TryRecoverRecipe:no_generated_observations");
            return false;
        }

        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var observations = new List<RoofRafterGenerationRecipe>(generatedIds.Count);
        foreach (var id in generatedIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
                trace?.Add("recipeFailure", "AutoCadObjectIdAccess.TryGetObject:generated_entity");
                trace?.Add("recipeFailureHandle", id.Handle);
                return false;
            }

            var generated = RoofGeneratedTimberStore.Read(entity);
            if (generated.Data is null || generated.Data.MemberKind != RoofGeneratedTimberKind.Rafter)
            {
                trace?.Add("recipeFailure", "RoofGeneratedTimberStore.Read:not_rafter");
                trace?.Add("recipeFailureHandle", id.Handle);
                return false;
            }
            if (!metadataStore.TryRead(entity, out var timber) || timber is null || timber.ElementType != TimberElementType.Rafter)
            {
                trace?.Add("recipeFailure", "AutoCadTimberElementMetadataStore.TryRead:not_rafter");
                trace?.Add("recipeFailureHandle", id.Handle);
                return false;
            }

            observations.Add(new RoofRafterGenerationRecipe(
                timber.WidthMm,
                timber.HeightMm,
                generated.Data.RequestedMaximumSpacingMm,
                timber.Material));
        }

        var unified = RoofRafterGenerationRecipeRules.TryUnify(observations, out recipe);
        if (!unified)
        {
            trace?.Add("recipeFailure", "RoofRafterGenerationRecipeRules.TryUnify");
            trace?.Add("recipeDistinctInputs", string.Join(";", observations.Distinct().Select(r =>
                FormattableString.Invariant($"{r.WidthMm:R}x{r.HeightMm:R}@{r.MaximumSpacingMm:R}:{r.Material}"))));
        }
        return unified;
    }

    internal static void PreserveRecipe(Database database, Transaction transaction, ObjectId ownerId,
        IEnumerable<Line>? plans = null)
    {
        if (ownerId.IsNull || ownerId.IsErased) return;
        var owner = (Polyline)transaction.GetObject(ownerId, OpenMode.ForRead);
        var definition = RoofDefinitionStore.Read(owner).Data;
        if (definition is null) throw new InvalidOperationException("Ordinary generator definition is missing.");
        plans ??= RoofGeneratedTimberStore.FindByOwner(database, transaction, owner.Handle.ToString())
            .Where(id => !id.IsErased).Select(id => transaction.GetObject(id, OpenMode.ForRead)).OfType<Line>();
        var observations = new List<RoofRafterGenerationRecipe>();
        foreach (var line in plans)
            if (RoofGeneratedTimberStore.Read(line).Data is { MemberKind: RoofGeneratedTimberKind.Rafter } generated &&
                ElementDataStore.TryRead(line, transaction, out var timber) && timber is not null)
                observations.Add(new(timber.WidthMm, timber.HeightMm, generated.RequestedMaximumSpacingMm, timber.Material));
        if (!RoofRafterGenerationRecipeRules.TryUnify(observations, out var recipe))
        {
            if (observations.Count == 0 && definition.OrdinaryRafterRecipe is not null) return;
            throw new InvalidOperationException("Ordinary generation recipe is unavailable or ambiguous.");
        }
        PersistRecipe(transaction, owner, definition, recipe);
    }

    internal static bool TryResolveGeneratorRecipe(Database database, Transaction transaction, Polyline owner,
        out RoofRafterGenerationRecipe recipe)
    {
        // Stored generator input wins; legacy inventory is used only to recover recipe values,
        // never to decide which stations belong in a preview or a regenerated set.
        if (RoofDefinitionStore.Read(owner).Data?.OrdinaryRafterRecipe is { } stored)
        {
            recipe = stored;
            return RoofRafterGenerationRecipeRules.IsValid(recipe);
        }
        return TryRecoverRecipe(database, transaction,
            RoofGeneratedTimberStore.FindByOwner(database, transaction, owner.Handle.ToString()), out recipe);
    }

    internal static RoofRafterLayoutResult CreateGeneratorLayout(Database database, IRoofGeometry geometry,
        RoofRafterGenerationRecipe recipe) => RoofRafterLayoutSolver.Solve(geometry,
            AutoCadRoofRafterSpacingStore.CreateLayoutParameters(database, recipe.MaximumSpacingMm, recipe.WidthMm));

    private static void PersistRecipe(Transaction transaction, Polyline owner, RoofDefinitionData definition,
        RoofRafterGenerationRecipe recipe)
    {
        if (definition.OrdinaryRafterRecipe == recipe && definition.SchemaVersion == RoofDefinitionDataSchema.CurrentVersion) return;
        var input = RoofPolylineExtractor.Extract(owner);
        var footprint = RoofFootprintValidator.Validate(input).Footprint;
        var geometry = footprint is null ? null : RoofDefinitionPersistence.Restore(input, footprint, definition).Geometry;
        if (geometry is null) throw new InvalidOperationException("Ordinary recipe owner geometry is unavailable.");
        var updated = RoofDefinitionPersistence.UpdateGeometry(definition, input, geometry) with { OrdinaryRafterRecipe = recipe };
        owner.UpgradeOpen();
        RoofDefinitionStore.Write(owner, transaction, updated);
    }

    private static RoofDefinitionData? PrepareGeneratorDefinition(Transaction transaction, Polyline owner)
    {
        var definition = RoofDefinitionStore.Read(owner).Data;
        if (definition is null || !definition.Overrides.Any(item => item.Suppressed &&
                item.Key.MemberKind == RoofGeneratedTimberKind.Rafter)) return definition;
        var prepared = RoofOrdinaryRebuildRules.Prepare(definition);
        owner.UpgradeOpen();
        RoofDefinitionStore.Write(owner, transaction, prepared);
        return prepared;
    }

    public static ReplacementOutcome TryReplaceForSupportedResize(
        Database database,
        Transaction transaction,
        Editor editor,
        Polyline owner,
        IRoofGeometry geometry,
        TimberElementDefaultProfile defaultProfile,
        ElementLayerProfile layerProfile,
        bool forceRegenerateOnSourceResize = false,
        string rebuildReason = "source-change",
        bool syncAssemblyGroup = true)
    {
        return TryReplaceForSupportedResize(
            database,
            transaction,
            editor,
            owner,
            geometry,
            defaultProfile,
            layerProfile,
            out _,
            out _,
            forceRegenerateOnSourceResize,
            rebuildReason,
            syncAssemblyGroup);
    }

    public static ReplacementOutcome TryReplaceForSupportedResize(
        Database database,
        Transaction transaction,
        Editor editor,
        Polyline owner,
        IRoofGeometry geometry,
        TimberElementDefaultProfile defaultProfile,
        ElementLayerProfile layerProfile,
        out RoofGeneratedAnchorResolutionContext? anchorResolutionContext,
        bool forceRegenerateOnSourceResize = false,
        string rebuildReason = "source-change",
        bool syncAssemblyGroup = true)
    {
        return TryReplaceForSupportedResize(
            database,
            transaction,
            editor,
            owner,
            geometry,
            defaultProfile,
            layerProfile,
            out anchorResolutionContext,
            out _,
            forceRegenerateOnSourceResize,
            rebuildReason,
            syncAssemblyGroup);
    }

    public static ReplacementOutcome TryReplaceForSupportedResize(
        Database database,
        Transaction transaction,
        Editor editor,
        Polyline owner,
        IRoofGeometry geometry,
        TimberElementDefaultProfile defaultProfile,
        ElementLayerProfile layerProfile,
        out RoofGeneratedAnchorResolutionContext? anchorResolutionContext,
        out RoofGeneratedMemberReplayPlan? replayPlan,
        bool forceRegenerateOnSourceResize = false,
        string rebuildReason = "source-change",
        bool syncAssemblyGroup = true)
    {
        anchorResolutionContext = null;
        replayPlan = null;
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(defaultProfile);
        ArgumentNullException.ThrowIfNull(layerProfile);

        var ownerReference = owner.Handle.ToString();
#if DEBUG
        RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
            editor,
            $"TryReplace ownerHandle={ownerReference} reason={rebuildReason} geometrySig={geometry.Signature}");
#endif
        if (!TryPrepareExistingOrdinarySet(
                database,
                transaction,
                editor,
                ownerReference,
                out var existingIds,
                out var members))
        {
            if (existingIds.Count == 0 && RoofDefinitionStore.Read(owner).Data?.OrdinaryRafterRecipe is { } savedRecipe)
                return ReplacePreparedSetWithRecipe(database, transaction, editor, owner, ownerReference, geometry,
                    savedRecipe, existingIds, members, defaultProfile, layerProfile, out anchorResolutionContext,
                    out replayPlan, rebuildReason, syncAssemblyGroup);
            return existingIds.Count == 0 ? ReplacementOutcome.NotApplicable : ReplacementOutcome.SkippedAmbiguousRecipe;
        }

        if (rebuildReason != "roof-edit" && !forceRegenerateOnSourceResize &&
            !IsGeneratedSetStale(database, transaction, existingIds, geometry))
        {
#if DEBUG
            RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
                editor,
                "branch=FreshnessCurrent -> NotApplicable");
#endif
            return ReplacementOutcome.NotApplicable;
        }

        if (!TryResolveGeneratorRecipe(database, transaction, owner, out var recipe))
        {
#if DEBUG
            RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
                editor,
                "branch=RecipeUnifyFailed -> SkippedAmbiguousRecipe");
#endif
            return ReplacementOutcome.SkippedAmbiguousRecipe;
        }

        return ReplacePreparedSetWithRecipe(
            database,
            transaction,
            editor,
            owner,
            ownerReference,
            geometry,
            recipe,
            existingIds,
            members,
            defaultProfile,
            layerProfile,
            out anchorResolutionContext,
            out replayPlan,
            rebuildReason,
            syncAssemblyGroup);
    }

    /// <summary>
    /// Explicit AK_ROOF_RAFTERS EDIT replacement: erase/materialize the ordinary
    /// generated set using the validated edited recipe while preserving reserved
    /// ElementIds and override replay from the current set.
    /// </summary>
    public static ReplacementOutcome TryReplaceWithEditedRecipe(
        Database database,
        Transaction transaction,
        Editor editor,
        Polyline owner,
        IRoofGeometry geometry,
        RoofRafterGenerationRecipe editedRecipe,
        TimberElementDefaultProfile defaultProfile,
        ElementLayerProfile layerProfile,
        out RoofGeneratedAnchorResolutionContext? anchorResolutionContext,
        out RoofGeneratedMemberReplayPlan? replayPlan)
    {
        anchorResolutionContext = null;
        replayPlan = null;
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(editedRecipe);
        ArgumentNullException.ThrowIfNull(defaultProfile);
        ArgumentNullException.ThrowIfNull(layerProfile);

        if (!RoofRafterGenerationRecipeRules.IsValid(editedRecipe))
        {
            return ReplacementOutcome.SkippedInvalidLayout;
        }

        var ownerReference = owner.Handle.ToString();
#if DEBUG
        RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
            editor,
            $"TryReplaceEdited ownerHandle={ownerReference} recipeW={editedRecipe.WidthMm} recipeH={editedRecipe.HeightMm} spacing={editedRecipe.MaximumSpacingMm}");
#endif
        if (!TryPrepareExistingOrdinarySet(
                database,
                transaction,
                editor,
                ownerReference,
                out var existingIds,
                out var members))
        {
            return existingIds.Count == 0
                ? ReplacementOutcome.NotApplicable
                : ReplacementOutcome.SkippedAmbiguousRecipe;
        }

        // Ambiguous live set must never be erased, even when the dialog already
        // recovered a recipe earlier. Re-check before any mutation.
        if (!TryRecoverRecipe(database, transaction, existingIds, out _))
        {
#if DEBUG
            RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
                editor,
                "branch=EditedRecipeUnifyFailed -> SkippedAmbiguousRecipe");
#endif
            return ReplacementOutcome.SkippedAmbiguousRecipe;
        }

        return ReplacePreparedSetWithRecipe(
            database,
            transaction,
            editor,
            owner,
            ownerReference,
            geometry,
            editedRecipe,
            existingIds,
            members,
            defaultProfile,
            layerProfile,
            out anchorResolutionContext,
            out replayPlan,
            rebuildReason: "rafter-edit");
    }

    private static bool TryPrepareExistingOrdinarySet(
        Database database,
        Transaction transaction,
        Editor editor,
        string ownerReference,
        out IReadOnlyList<ObjectId> existingIds,
        out List<RoofGeneratedTimberData> members)
    {
        members = [];
        existingIds = RoofGeneratedTimberStore.FindByOwner(
            database,
            transaction,
            ownerReference);
        if (existingIds.Count == 0)
        {
#if DEBUG
            RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
                editor,
                "branch=FindByOwnerEmpty -> NotApplicable");
#endif
            return false;
        }

#if DEBUG
        RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
            editor,
            $"FindByOwnerCount={existingIds.Count}");
#endif
        if (!TryCollectGeneratedMembers(
                database,
                transaction,
                existingIds,
                out members) ||
            !RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(members))
        {
            // Same-DWG COPY without remappable owner soft-pointers can leave two
            // physical sets claiming one JSON owner. Never erase in that state.
#if DEBUG
            RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
                editor,
                $"branch=OwnershipAmbiguousOrUnreadable memberCount={members.Count} uniqueStations={RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(members)} -> SkippedAmbiguousRecipe");
            RoofGeneratedCopyLifecycleDiag.WriteResizeTrace(
                editor,
                ownerReference,
                existingIds.Count,
                members.Count,
                RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(members),
                RoofGeneratedCopyLifecycleDiag.DescribeDuplicateStations(members),
                nameof(ReplacementOutcome.SkippedAmbiguousRecipe));
#endif
            return false;
        }

        return true;
    }

    private static ReplacementOutcome ReplacePreparedSetWithRecipe(
        Database database,
        Transaction transaction,
        Editor editor,
        Polyline owner,
        string ownerReference,
        IRoofGeometry geometry,
        RoofRafterGenerationRecipe recipe,
        IReadOnlyList<ObjectId> existingIds,
        List<RoofGeneratedTimberData> members,
        TimberElementDefaultProfile defaultProfile,
        ElementLayerProfile layerProfile,
        out RoofGeneratedAnchorResolutionContext? anchorResolutionContext,
        out RoofGeneratedMemberReplayPlan? replayPlan,
        string rebuildReason,
        bool syncAssemblyGroup = true)
    {
        anchorResolutionContext = null;
        replayPlan = null;
        LastFailureDetail = null;
        var layoutResult = CreateGeneratorLayout(database, geometry, recipe);
        if (!layoutResult.IsValid || layoutResult.Layout is null)
        {
#if DEBUG
            RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
                editor,
                "branch=InvalidLayout -> SkippedInvalidLayout");
#endif
            return ReplacementOutcome.SkippedInvalidLayout;
        }

        try
        {
            var definition = PrepareGeneratorDefinition(transaction, owner);
            var reservedElementIds = CollectReservedElementIds(
                database,
                transaction,
                existingIds,
                definition);
            replayPlan = RoofOrdinaryRebuildRules.CreateReplayPlan(layoutResult.Layout, definition);
            if (!replayPlan.IsValid)
            {
                throw new InvalidOperationException(
                    $"Generated override replay planning failed: {replayPlan.FailureReason ?? "unknown"}.");
            }
            EraseGeneratedSet(database, transaction, owner.ObjectId, existingIds);
            var materialized = MaterializeCore(
                database,
                transaction,
                editor,
                owner,
                ownerReference,
                geometry,
                layoutResult.Layout,
                recipe,
                defaultProfile,
                layerProfile,
                reservedElementIds,
                replayPlan,
                syncAssemblyGroup: syncAssemblyGroup,
                includeAttachedManual: rebuildReason is not ("source-resize" or "roof-edit"));
            var created = materialized.Created;
#if DEBUG
            WriteOverrideDomainDiagnostics(
                editor,
                ownerReference,
                layoutResult.Layout,
                0d,
                replayPlan);
            RoofGeneratedMemberManualEditDiag.WriteReplay(
                editor,
                ownerReference,
                geometry.Kind.ToString(),
                replayPlan.StoredOverrideCount,
                replayPlan.ResolvedOverrideCount,
                replayPlan.GeometryReplayCount,
                replayPlan.SuppressedCount,
                replayPlan.DormantCount,
                replayPlan.DormantMissingKeyCount,
                replayPlan.DormantInvalidDomainCount,
                replayPlan.DuplicateKeyCount,
                existingIds.Count,
                created.Count,
                "materialized");
#endif
            _ = RoofGeneratedAnchorResolutionContext.TryCreate(
                database,
                transaction,
                created.Keys.ToArray(),
                layoutResult.Layout,
                0d,
                definition?.Overrides,
                out anchorResolutionContext);
#if DEBUG
            RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
                editor,
                $"branch=Replaced reason={rebuildReason} oldCount={existingIds.Count} newCount={layoutResult.Layout.Rafters.Count} uniqueKeys={RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(members).ToString().ToLowerInvariant()} recipeW={recipe.WidthMm} recipeH={recipe.HeightMm} spacing={recipe.MaximumSpacingMm} anchorContext={(anchorResolutionContext is null ? "unavailable" : "ready")}");
#else
            _ = members;
            _ = rebuildReason;
#endif
            return ReplacementOutcome.Replaced;
        }
        catch (System.Exception ex)
        {
            var phase = ex is RoofRafterMaterializationPhaseException materialization
                ? materialization.ServicePhase
                : null;
            LastFailureDetail = RoofGeneratedPlanRebuildFailureRules.FromException(
                ownerReference,
                ex,
                phase);
#if DEBUG
            RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
                editor,
                $"branch=MaterializeOrEraseFailed -> Failed ex={ex.GetType().Name}:{ex.Message}");
            AcKrovyDiagnostics.Info(
                "ROOF_GENERATED_PLAN_REBUILD_FAILURE",
                LastFailureDetail.ToMarkerLine());
            editor.WriteMessage(
                $"\nROOF_GENERATED_PLAN_REBUILD_FAILURE {LastFailureDetail.ToMarkerLine()}\n");
#endif
            return ReplacementOutcome.Failed;
        }
    }

    public static IReadOnlyDictionary<ObjectId, TimberElementData> Materialize(
        Database database,
        Transaction transaction,
        Editor editor,
        Polyline owner,
        string ownerReference,
        SimpleGableRoofGeometry geometry,
        SimpleGableRafterLayout layout,
        RoofRafterGenerationRecipe recipe,
        TimberElementDefaultProfile defaultProfile,
        ElementLayerProfile layerProfile,
        IReadOnlyDictionary<RoofGeneratedMemberKey, string>? reservedElementIds = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(recipe);
        var sharedLayout = RoofRafterLayoutSolver.Solve(
            geometry,
            AutoCadRoofRafterSpacingStore.CreateLayoutParameters(
                database,
                layout.RequestedMaximumSpacingMm,
                layout.RafterPlanWidthMm));
        if (!sharedLayout.IsValid || sharedLayout.Layout is null)
        {
            throw new InvalidOperationException(
                "The legacy Gable layout cannot be adapted for neutral materialization.");
        }

        return MaterializeCore(
            database,
            transaction,
            editor,
            owner,
            ownerReference,
            geometry,
            sharedLayout.Layout,
            recipe,
            defaultProfile,
            layerProfile,
            reservedElementIds).Created;
    }

    public static IReadOnlyDictionary<ObjectId, TimberElementData> Materialize(
        Database database,
        Transaction transaction,
        Editor editor,
        Polyline owner,
        string ownerReference,
        IRoofGeometry geometry,
        RoofRafterLayout layout,
        RoofRafterGenerationRecipe recipe,
        TimberElementDefaultProfile defaultProfile,
        ElementLayerProfile layerProfile,
        IReadOnlyDictionary<RoofGeneratedMemberKey, string>? reservedElementIds = null,
        bool syncAssemblyGroup = true)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(recipe);
        if (!RoofRafterMaterializationRules.IsConsistent(geometry, layout))
        {
            throw new InvalidOperationException(
                "The neutral rafter layout is not internally consistent for materialization.");
        }

        return MaterializeCore(
            database,
            transaction,
            editor,
            owner,
            ownerReference,
            geometry,
            layout,
            recipe,
            defaultProfile,
            layerProfile,
            reservedElementIds,
            syncAssemblyGroup: syncAssemblyGroup).Created;
    }

    private static MaterializationResult MaterializeCore(
        Database database,
        Transaction transaction,
        Editor editor,
        Polyline owner,
        string ownerReference,
        IRoofGeometry geometry,
        RoofRafterLayout layout,
        RoofRafterGenerationRecipe recipe,
        TimberElementDefaultProfile defaultProfile,
        ElementLayerProfile layerProfile,
        IReadOnlyDictionary<RoofGeneratedMemberKey, string>? reservedElementIds,
        RoofGeneratedMemberReplayPlan? preparedReplayPlan = null,
        bool syncAssemblyGroup = true, bool includeAttachedManual = true)
    {

        // Ordinary plan axes are independent of the roof's physical elevation.
        if (PrepareGeneratorDefinition(transaction, owner) is { } recipeDefinition)
            PersistRecipe(transaction, owner, recipeDefinition, recipe);
        var sourceElevation = 0d;
        var storedOverrides = RoofDefinitionStore.Read(owner).Data?.Overrides;
        var planeNormal = RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal;
        RoofGeneratedMemberReplayPlan replayPlan;
        try
        {
            replayPlan = preparedReplayPlan ?? RoofGeneratedMemberReplayPlanner.Create(
                layout,
                sourceElevation,
                planeNormal,
                storedOverrides);
            replayPlan = RoofAcceptedOrdinaryOverrideReplayRules.Apply(
                replayPlan, RoofDefinitionStore.Read(owner).Data?.EditState ?? RoofEditState.Locked);
        }
        catch (Exception ex)
        {
#if DEBUG
            RoofRafterPermanentCreateDiag.WriteMaterializeFailure(
                editor,
                geometry,
                layout,
                recipe,
                -1,
                "RoofGeneratedMemberReplayPlanner.Create",
                ex);
#endif
            throw new RoofRafterMaterializationPhaseException(
                "RoofGeneratedMemberReplayPlanner.Create",
                -1,
                ex);
        }
        if (!replayPlan.IsValid)
        {
            var exception = new InvalidOperationException(
                $"Generated override replay planning failed: {replayPlan.FailureReason ?? "unknown"}.");
#if DEBUG
            RoofRafterPermanentCreateDiag.WriteMaterializeFailure(
                editor,
                geometry,
                layout,
                recipe,
                -1,
                "RoofGeneratedMemberReplayPlanner.Validate",
                exception);
#endif
            throw new RoofRafterMaterializationPhaseException(
                "RoofGeneratedMemberReplayPlanner.Validate",
                -1,
                exception);
        }
        var canonicalRafterData = TimberElementDefaults.For(
            TimberElementType.Rafter,
            defaultProfile) with
        {
            WidthMm = recipe.WidthMm,
            HeightMm = recipe.HeightMm,
            IsSlopeDirectionReversed = true,
            Material = recipe.Material,
        };
        var accepted = new List<(RoofRafterGeometry Rafter, Point3d Start, Point3d End, TimberElementData Data)>();
        foreach (var replayItem in replayPlan.Items)
        {
            if (replayItem.Geometry is not { } appliedGeometry)
            {
                continue;
            }

            var rafter = replayItem.Rafter;
            var key = rafter.LogicalKey;
            var memberData = canonicalRafterData with { SlopeDegrees = rafter.SlopeDegrees };
            if (replayItem.Override is { } overrideData &&
                !string.IsNullOrWhiteSpace(overrideData.ReservedElementId))
            {
                memberData = memberData with { ElementId = overrideData.ReservedElementId };
            }
            else if (reservedElementIds is not null &&
                     reservedElementIds.TryGetValue(key, out var reservedId) &&
                     !string.IsNullOrWhiteSpace(reservedId))
            {
                memberData = memberData with { ElementId = reservedId };
            }

            accepted.Add((
                rafter,
                new Point3d(appliedGeometry.Start.X, appliedGeometry.Start.Y, appliedGeometry.Start.Z),
                new Point3d(appliedGeometry.End.X, appliedGeometry.End.Y, appliedGeometry.End.Z),
                memberData));
        }

        var requests = accepted
            .Select(item => new TimberSourceLineCreationRequest(
                item.Start,
                item.End,
                item.Data))
            .ToArray();
        IReadOnlyDictionary<ObjectId, TimberElementData> created;
        try
        {
            created = TimberSourceLineCreationService.Create(
                database,
                transaction,
                editor,
                requests,
                defaultProfile,
                layerProfile,
                (line, currentTransaction, index) =>
                {
                    var rafter = accepted[index].Rafter;
                    return RoofGeneratedTimberStore.BuildSection(
                        line,
                        currentTransaction,
                        new RoofGeneratedTimberData(
                            RoofGeneratedTimberDataSchema.CurrentVersion,
                            ownerReference,
                            RoofGeneratedTimberKind.Rafter,
                            rafter.Face,
                            rafter.StationIndex,
                            rafter.StationCount,
                            layout.RequestedMaximumSpacingMm,
                            RoofGeneratedLayoutFingerprint.ToPersistedIdentity(layout.Signature)));
                });
        }
        catch (TimberSourceLineCreationPhaseException ex)
        {
#if DEBUG
            RoofRafterPermanentCreateDiag.WriteMaterializeFailure(
                editor,
                geometry,
                layout,
                recipe,
                ex.CandidateOrdinal,
                ex.ServicePhase,
                ex.InnerException ?? ex);
#endif
            throw new RoofRafterMaterializationPhaseException(
                ex.ServicePhase,
                ex.CandidateOrdinal,
                ex.InnerException ?? ex);
        }

        try
        {
            TimberCreatedElementAnnotationService.EnsureForCreatedElements(
                database,
                transaction,
                created,
                defaultProfile,
                copySourcePreservation: !syncAssemblyGroup);
        }
        catch (TimberCreatedElementAnnotationPhaseException ex)
        {
            const string phase = "TimberAnnotationService.EnsureForElement";
#if DEBUG
            RoofRafterPermanentCreateDiag.WriteMaterializeFailure(
                editor,
                geometry,
                layout,
                recipe,
                ex.CandidateOrdinal,
                phase,
                ex.InnerException ?? ex);
#endif
            throw new RoofRafterMaterializationPhaseException(
                phase,
                ex.CandidateOrdinal,
                ex.InnerException ?? ex);
        }
        catch (Exception ex)
        {
            const string phase = "TimberCreatedElementAnnotationService.EnsureForCreatedElements";
#if DEBUG
            RoofRafterPermanentCreateDiag.WriteMaterializeFailure(
                editor,
                geometry,
                layout,
                recipe,
                -1,
                phase,
                ex);
#endif
            throw new RoofRafterMaterializationPhaseException(phase, -1, ex);
        }
        var document = editor.Document;
        try
        {
#if DEBUG
            if (!string.IsNullOrWhiteSpace(InjectPostCreateOrdinaryPhysicalFailureOnce))
            {
                var injected = InjectPostCreateOrdinaryPhysicalFailureOnce;
                InjectPostCreateOrdinaryPhysicalFailureOnce = null;
                throw new InvalidOperationException(injected);
            }
#endif
            RoofOrdinaryRafterSolidMaterializationService.ReconcileInTransaction(
                database, transaction, owner, geometry, layout, recipe, replayPlan,
                structuralReconcilePending: true, includeAttachedManual: includeAttachedManual);
        }
        catch (Exception ex)
        {
            throw new RoofRafterMaterializationPhaseException(
                RoofGeneratedPlanRebuildFailureRules.SubstageOrdinaryPhysicalReconcile,
                -1,
                ex);
        }

        try
        {
            var physicalState = RoofPhysicalElevationStore.Read(owner).Data;
            if (physicalState is not null)
            {
                RoofPhysical3DMaterializationService.ApplyOwnedVisibility(
                    database, transaction, ownerReference,
                    physicalState.Physical3DEnabled,
                    physicalState.DisplayVisibility);
            }
        }
        catch (Exception ex)
        {
            throw new RoofRafterMaterializationPhaseException(
                RoofGeneratedPlanRebuildFailureRules.SubstageVisibility,
                -1,
                ex);
        }
        if (!syncAssemblyGroup)
        {
            return new MaterializationResult(created, replayPlan);
        }

#if DEBUG
        RoofAssemblyGroupDiag.WriteMembershipSnapshot(
            editor,
            database,
            transaction,
            owner.ObjectId,
            "materialize-before-full-sync");
#endif
        const string groupSyncPhase = "RoofAssemblyGroupSyncService.TrySyncForOwner";
        try
        {
            if (document is null ||
                !RoofAssemblyGroupSyncService.TrySyncForOwner(document, transaction, owner.ObjectId))
            {
                throw new InvalidOperationException(
                    "The canonical roof assembly group could not be synchronized.");
            }
        }
        catch (Exception ex)
        {
#if DEBUG
            RoofRafterPermanentCreateDiag.WriteMaterializeFailure(
                editor,
                geometry,
                layout,
                recipe,
                -1,
                groupSyncPhase,
                ex);
#endif
            throw new RoofRafterMaterializationPhaseException(groupSyncPhase, -1, ex);
        }
#if DEBUG
        RoofAssemblyGroupDiag.WriteMembershipSnapshot(
            editor,
            database,
            transaction,
            owner.ObjectId,
            "materialize-after-full-sync");
#endif

        return new MaterializationResult(created, replayPlan);
    }

    private sealed record MaterializationResult(
        IReadOnlyDictionary<ObjectId, TimberElementData> Created,
        RoofGeneratedMemberReplayPlan ReplayPlan);

    private static bool TryCollectGeneratedMembers(
        Database database,
        Transaction transaction,
        IReadOnlyList<ObjectId> generatedIds,
        out List<RoofGeneratedTimberData> members)
    {
        members = new List<RoofGeneratedTimberData>(generatedIds.Count);
        foreach (var id in generatedIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
                members.Clear();
                return false;
            }

            var stored = RoofGeneratedTimberStore.Read(entity);
            if (stored.Data is null ||
                stored.Data.MemberKind != RoofGeneratedTimberKind.Rafter)
            {
                members.Clear();
                return false;
            }

            members.Add(stored.Data);
        }

        return members.Count > 0;
    }

    internal static Dictionary<RoofGeneratedMemberKey, string> CollectReservedElementIds(
        Database database,
        Transaction transaction,
        IReadOnlyList<ObjectId> generatedIds,
        RoofDefinitionData? definition)
    {
        var reserved = new Dictionary<RoofGeneratedMemberKey, string>();
        if (definition is not null)
        {
            foreach (var item in definition.Overrides)
            {
                if (!string.IsNullOrWhiteSpace(item.ReservedElementId))
                {
                    reserved[item.Key] = item.ReservedElementId;
                }
            }
        }

        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        foreach (var id in generatedIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
                continue;
            }

            var generated = RoofGeneratedTimberStore.Read(entity);
            if (generated.Data is null ||
                !metadataStore.TryRead(entity, out var timber) ||
                timber is null ||
                string.IsNullOrWhiteSpace(timber.ElementId))
            {
                continue;
            }

            reserved[RoofGeneratedMemberKey.From(generated.Data)] = timber.ElementId;
        }

        return reserved;
    }

    public static bool IsGeneratedSetStale(
        Database database,
        Transaction transaction,
        IReadOnlyList<ObjectId> generatedIds,
        IRoofGeometry geometry)
    {
        if (generatedIds.Count == 0)
        {
            return false;
        }

        if (!TryRecoverRecipe(database, transaction, generatedIds, out var recipe))
        {
            return true;
        }

        var layoutResult = RoofRafterLayoutSolver.Solve(
            geometry,
            AutoCadRoofRafterSpacingStore.CreateLayoutParameters(
                database,
                recipe.MaximumSpacingMm,
                recipe.WidthMm));
        if (!layoutResult.IsValid || layoutResult.Layout is null)
        {
            return true;
        }

        var currentFullSignature = layoutResult.Layout.Signature;
        foreach (var id in generatedIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
                return true;
            }

            var stored = RoofGeneratedTimberStore.Read(entity);
            if (stored.Data is null ||
                !RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(
                    stored.Data.LayoutSignature,
                    currentFullSignature))
            {
                return true;
            }
        }

        return false;
    }

    private static void EraseGeneratedSet(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyList<ObjectId> generatedIds)
    {
        // Detach the generated members + their annotation family from the GROUP before
        // erasing so native U can reverse the erase without re-adding an erased ObjectId
        // to the group (eInvalidInput).
        _ = RoofAssemblyGroupSyncService.DetachMembersBeforeErase(
            database,
            transaction,
            ownerId,
            generatedIds);
        foreach (var id in generatedIds)
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

            var sourceHandle = entity.Handle.ToString();
            ElementLabelService.DeleteForSourceHandle(database, transaction, sourceHandle);
            SlopeAnnotationService.DeleteForSourceHandle(database, transaction, sourceHandle);
            PostFootprintPerpendicularAnnotationService.DeleteForSourceHandle(
                database,
                transaction,
                sourceHandle);
            entity.Erase();
        }
    }

#if DEBUG
    private static void WriteOverrideDomainDiagnostics(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string ownerReference,
        RoofRafterLayout layout,
        double sourceElevationMm,
        RoofGeneratedMemberReplayPlan replayPlan)
    {
        foreach (var item in replayPlan.Items)
        {
            if (item.Override is null || !item.Override.HasGeometryOverride)
            {
                continue;
            }

            var canonical = RoofGeneratedMemberOverrideRules.CanonicalGeometry(
                item.Rafter,
                sourceElevationMm);
            if (!RoofGeneratedMemberOverrideMath.TryApply(
                    canonical,
                    RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
                    item.Override,
                    out var applied))
            {
                continue;
            }

            var evaluation = RoofGeneratedMemberDomainRules.Evaluate(
                layout,
                item.Rafter,
                applied);
            var face = item.Rafter.Face.ToString();
            RoofGeneratedMemberManualEditDiag.WriteOverrideDomain(
                editor,
                ownerReference,
                $"{item.Rafter.LogicalKey.MemberKind}:{face}:{item.Rafter.StationIndex}",
                oldFace: face,
                newFace: face,
                storedStart: FormatPoint(applied.Start),
                storedEnd: FormatPoint(applied.End),
                canonicalStart: FormatPoint(canonical.Start),
                canonicalEnd: FormatPoint(canonical.End),
                startInsideFootprint: evaluation.StartInsideFootprint,
                endInsideFootprint: evaluation.EndInsideFootprint,
                startInsideFace: evaluation.StartInsideFootprint,
                endInsideFace: evaluation.EndInsideFootprint,
                segmentIntersectsFace: evaluation.SegmentIntersectsDomain,
                domainResult: item.Disposition.ToString(),
                reason: evaluation.Reason);
        }
    }

    private static string FormatPoint(RoofPoint3D point)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return point.X.ToString("0.###", culture) + "," +
               point.Y.ToString("0.###", culture) + "," +
               point.Z.ToString("0.###", culture);
    }
#endif
}
