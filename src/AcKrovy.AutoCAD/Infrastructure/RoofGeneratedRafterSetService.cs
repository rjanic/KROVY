using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
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

    public static bool TryRecoverRecipe(
        Database database,
        Transaction transaction,
        IReadOnlyList<ObjectId> generatedIds,
        out RoofRafterGenerationRecipe recipe)
    {
        recipe = default!;
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        if (generatedIds is null || generatedIds.Count == 0)
        {
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
                return false;
            }

            var generated = RoofGeneratedTimberStore.Read(entity);
            if (generated.Data is null ||
                generated.Data.MemberKind != RoofGeneratedTimberKind.Rafter ||
                !metadataStore.TryRead(entity, out var timber) ||
                timber is null ||
                timber.ElementType != TimberElementType.Rafter)
            {
                return false;
            }

            observations.Add(new RoofRafterGenerationRecipe(
                timber.WidthMm,
                timber.HeightMm,
                generated.Data.RequestedMaximumSpacingMm,
                timber.Material));
        }

        return RoofRafterGenerationRecipeRules.TryUnify(observations, out recipe);
    }

    public static ReplacementOutcome TryReplaceForSupportedResize(
        Database database,
        Transaction transaction,
        Editor editor,
        Polyline owner,
        SimpleGableRoofGeometry geometry,
        TimberElementDefaultProfile defaultProfile,
        ElementLayerProfile layerProfile,
        bool forceRegenerateOnSourceResize = false)
    {
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
            $"TryReplace ownerHandle={ownerReference} geometrySig={geometry.Signature}");
#endif
        var existingIds = RoofGeneratedTimberStore.FindByOwner(
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
            return ReplacementOutcome.NotApplicable;
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
                out var members) ||
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
            return ReplacementOutcome.SkippedAmbiguousRecipe;
        }

        if (!forceRegenerateOnSourceResize &&
            !IsGeneratedSetStale(database, transaction, existingIds, geometry.Signature))
        {
#if DEBUG
            RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
                editor,
                "branch=FreshnessCurrent -> NotApplicable");
#endif
            return ReplacementOutcome.NotApplicable;
        }

        if (!TryRecoverRecipe(database, transaction, existingIds, out var recipe))
        {
#if DEBUG
            RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
                editor,
                "branch=RecipeUnifyFailed -> SkippedAmbiguousRecipe");
#endif
            return ReplacementOutcome.SkippedAmbiguousRecipe;
        }

        var layoutResult = SimpleGableRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(recipe.MaximumSpacingMm, recipe.WidthMm));
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
            var reservedElementIds = CollectReservedElementIds(
                database,
                transaction,
                existingIds,
                RoofDefinitionStore.Read(owner).Data);
            EraseGeneratedSet(database, transaction, owner.ObjectId, existingIds);
            Materialize(
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
                reservedElementIds);
#if DEBUG
            RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
                editor,
                $"branch=Replaced newCount={layoutResult.Layout.Rafters.Count} recipeW={recipe.WidthMm} recipeH={recipe.HeightMm} spacing={recipe.MaximumSpacingMm}");
#endif
            return ReplacementOutcome.Replaced;
        }
        catch (System.Exception ex)
        {
#if DEBUG
            RoofGeneratedTimberCopyOwnershipDiagService.WriteReplaceDiag(
                editor,
                $"branch=MaterializeOrEraseFailed -> Failed ex={ex.GetType().Name}:{ex.Message}");
#else
            _ = ex;
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
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(recipe);

        var sourceElevation = RoofPolylineExtractor.GetSourceElevation(owner);
        var overrides = new RoofManualOverrideSet(RoofDefinitionStore.Read(owner).Data?.Overrides);
        var planeNormal = RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal;
        var canonicalRafterData = TimberElementDefaults.For(
            TimberElementType.Rafter,
            defaultProfile) with
        {
            WidthMm = recipe.WidthMm,
            HeightMm = recipe.HeightMm,
            SlopeDegrees = geometry.SlopeDegrees,
            IsSlopeDirectionReversed = true,
            Material = recipe.Material,
        };
        var accepted = new List<(SimpleGableRafter Rafter, Point3d Start, Point3d End, TimberElementData Data)>();
        foreach (var rafter in layout.Rafters)
        {
            if (!RoofGeneratedMemberOverrideRules.TryApplyToLayout(
                    rafter,
                    sourceElevation,
                    planeNormal,
                    overrides,
                    out var appliedGeometry,
                    out var suppressed) ||
                suppressed ||
                appliedGeometry is null)
            {
                continue;
            }

            var key = RoofGeneratedMemberKey.From(rafter);
            var memberData = canonicalRafterData;
            if (overrides.TryGet(key, out var overrideData) &&
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
                new Point3d(appliedGeometry.Value.Start.X, appliedGeometry.Value.Start.Y, appliedGeometry.Value.Start.Z),
                new Point3d(appliedGeometry.Value.End.X, appliedGeometry.Value.End.Y, appliedGeometry.Value.End.Z),
                memberData));
        }

        var requests = accepted
            .Select(item => new TimberSourceLineCreationRequest(
                item.Start,
                item.End,
                item.Data))
            .ToArray();
        var created = TimberSourceLineCreationService.Create(
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
                        layout.Signature));
            });
        TimberCreatedElementAnnotationService.EnsureForCreatedElements(
            database,
            transaction,
            created,
            defaultProfile);
        var document = editor.Document;
        if (document is not null)
        {
            _ = RoofAssemblyGroupSyncService.TrySyncForOwner(document, transaction, owner.ObjectId);
        }

        return created;
    }

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

    private static Dictionary<RoofGeneratedMemberKey, string> CollectReservedElementIds(
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
        string geometrySignature)
    {
        if (generatedIds.Count == 0)
        {
            return false;
        }

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
                !RoofGeneratedTimberFreshness.IsLayoutCurrent(
                    stored.Data.LayoutSignature,
                    geometrySignature))
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
}
