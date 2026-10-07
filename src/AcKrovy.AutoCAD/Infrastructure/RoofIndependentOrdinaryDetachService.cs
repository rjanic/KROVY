using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Atomically transfers one Ordinary plan and its current body out of roof ownership.</summary>
internal static class RoofIndependentOrdinaryDetachService
{
    public static bool TryDetach(Document document, Transaction transaction, Polyline owner,
        Line line, bool movePhysicalByPlanDelta, Vector3d planDelta,
        IReadOnlyCollection<ObjectId> nativeModifiedIds, RoofOrdinaryPhysicalBuildState? acceptedBuildState = null)
    {
        var generated = RoofGeneratedTimberStore.Read(line).Data;
        if (generated is not { MemberKind: RoofGeneratedTimberKind.Rafter } ||
            !string.Equals(generated.RoofOwnerReference, owner.Handle.ToString(),
                StringComparison.OrdinalIgnoreCase) ||
            RoofAttachedManualTimberStore.Read(line).Data is not null ||
            RoofIndependentOrdinaryTimberStore.Read(line) is not null ||
            !RoofOrdinaryRafterSolidMaterializationService.TryGetPlanPhysicalIdentity(
                line, owner.Handle.ToString(), out var physicalKey)) return false;

        var physicalMatches = RoofPhysical3DGeneratedStore.FindByOwner(document.Database,
                transaction, owner.Handle.ToString())
            .Where(id => transaction.GetObject(id, OpenMode.ForRead) is Solid3d solid &&
                RoofPhysical3DGeneratedStore.Read(solid).Data is
                    { Role: RoofPhysical3DGeneratedRole.OrdinaryRafterSolid } data &&
                string.Equals(data.StructuralId, physicalKey, StringComparison.Ordinal))
            .ToArray();
        var physicalExpected = RoofPhysicalElevationStore.Read(owner).Data?.Physical3DEnabled == true;
        if (physicalMatches.Length != (physicalExpected ? 1 : 0)) return false;

        var timberStore = new AutoCadTimberElementMetadataStore(transaction);
        if (!timberStore.TryRead(line, out var timber) || timber is null ||
            string.IsNullOrWhiteSpace(timber.ElementId)) return false;
        var elementIdBefore = timber.ElementId;

        // MOVE's native Plan has already moved. Capture the original geometry,
        // then rigidly translate the member-owned context with the accepted MOVE.
        // Other geometry edits supply their fully rebuilt, accepted state.
        var state = acceptedBuildState;
        if (state is null)
        {
            var current = RoofIndependentOrdinaryPhysicalStateService.Axis(line);
            var before = movePhysicalByPlanDelta ? new RoofSegment3D(
                new(current.Start.X - planDelta.X, current.Start.Y - planDelta.Y, 0),
                new(current.End.X - planDelta.X, current.End.Y - planDelta.Y, 0)) : current;
            if (!RoofOrdinaryRafterSolidMaterializationService.TryCaptureOrdinaryBuildState(document.Database,
                    transaction, owner, RoofGeneratedMemberKey.From(generated), before, out state) || state is null ||
                !RoofOrdinaryPhysicalBuildStateRules.TryRebase(state, current, out state) || state is null)
                throw new InvalidOperationException("Independent Ordinary detach physical context unavailable.");
            state = state with { WidthMm = timber.WidthMm, HeightMm = timber.HeightMm };
            if (physicalMatches.Length == 1 && !RoofOrdinaryPhysicalSectionFrameReader.TryCapture(
                    (Solid3d)transaction.GetObject(physicalMatches[0], OpenMode.ForRead), state, out state))
                throw new InvalidOperationException("Independent Ordinary detach physical section unavailable.");
        }
        if (state is null) throw new InvalidOperationException("Independent Ordinary detach complete state unavailable.");

        var memberId = Guid.NewGuid().ToString("N");
        RoofGeneratedRafterSetService.PreserveRecipe(document.Database, transaction, owner.ObjectId);
        var provenance = new RoofIndependentOrdinaryTimberData(
            RoofIndependentOrdinaryTimberDataSchema.CurrentVersion,
            memberId,
            RoofIndependentOrdinaryOriginKind.DetachedFromAuto,
            RoofIndependentOrdinaryEntityRole.PlanLine,
            owner.Handle.ToString(),
            RoofGeneratedMemberKey.From(generated));
        var ownedIds = new[] { line.ObjectId }.Concat(physicalMatches).ToArray();
        var groupRemoved = RoofAssemblyGroupSyncService.DetachMembersBeforeErase(
            document.Database, transaction, owner.ObjectId, ownedIds);

        void Trace(string stage, Entity entity, string writeResult, string readbackResult,
            bool generatedCleared, string? elementIdAfter, string result)
        {
#if DEBUG
            document.Editor.WriteMessage("\nROOF_INDEPENDENT_DETACH_MOVE" +
                $" owner={owner.Handle} sourceHandle={line.Handle}" +
                $" solidHandle={(physicalMatches.Length == 0 ? "-" : physicalMatches[0].Handle.ToString())}" +
                $" independentMemberId={memberId} stage={stage} entity={entity.Handle}" +
                $" writeResult={writeResult} readbackResult={readbackResult}" +
                $" generatedCleared={generatedCleared} elementIdBefore={elementIdBefore}" +
                $" elementIdAfter={elementIdAfter ?? "null"} groupRemoved={groupRemoved > 0}" +
                $" groupRemovedCount={groupRemoved}" +
                $" result={result}");
#endif
        }

        line.UpgradeOpen();
        try
        {
            RoofIndependentOrdinaryTimberStore.TransferFromRoof(line, transaction, provenance,
                RoofGeneratedTimberStore.RegAppName, RoofGeneratedTimberStore.LinkRegAppName);
        }
        catch (Exception ex)
        {
            Trace("line-transfer", line, ex.GetType().Name, ex.Message.Replace(' ', '_'),
                RoofGeneratedTimberStore.Read(line).Data is null,
                timberStore.TryRead(line, out var current) ? current?.ElementId : null, "failure");
            throw;
        }
        if (!timberStore.TryRead(line, out var lineTimber) ||
            lineTimber?.ElementId != elementIdBefore ||
            RoofAttachedManualTimberStore.Read(line).Data is not null)
            throw new InvalidOperationException("Independent Ordinary line ElementId/ownership readback failed.");
        Trace("line-transfer", line, "success", "success", true, lineTimber.ElementId, "pass");
        foreach (var id in physicalMatches)
        {
            var solid = (Solid3d)transaction.GetObject(id, OpenMode.ForWrite);
            if (movePhysicalByPlanDelta && !nativeModifiedIds.Contains(id))
                solid.TransformBy(Matrix3d.Displacement(planDelta));
            try
            {
                RoofIndependentOrdinaryTimberStore.TransferFromRoof(solid, transaction,
                    provenance with { EntityRole = RoofIndependentOrdinaryEntityRole.PhysicalSolid },
                    RoofPhysical3DGeneratedStore.RegAppName);
            }
            catch (Exception ex)
            {
                Trace("solid-transfer", solid, ex.GetType().Name, ex.Message.Replace(' ', '_'),
                    RoofPhysical3DGeneratedStore.Read(solid).Data is null,
                    elementIdBefore, "failure");
                throw;
            }
            Trace("solid-transfer", solid, "success", "success", true, elementIdBefore, "pass");
        }
        RoofIndependentOrdinaryPhysicalStateService.Persist(line, transaction, state);
        IReadOnlyList<ObjectId> annotations;
        try
        {
            var profile = TimberElementDefaultProfileStore.Load();
            TimberAnnotationService.EnsureForElement(document.Database, transaction, line, timber,
                AutoCadAnnotationPresentationBatchContext.Create(document.Database, transaction, profile),
                timber.ElementId, profile.GetCuttingLengthRoundingStepMm());
            annotations = FindAnnotations(document.Database, transaction, line.Handle.ToString());
            foreach (var id in annotations)
            {
                var annotation = (Entity)transaction.GetObject(id, OpenMode.ForWrite);
                RoofIndependentOrdinaryTimberStore.Write(annotation, transaction,
                    provenance with { EntityRole = RoofIndependentOrdinaryEntityRole.Annotation });
                Trace("annotation-transfer", annotation, "success", "success", true,
                    elementIdBefore, "pass");
            }
        }
        catch (Exception ex)
        {
            Trace("annotation-transfer", line, ex.GetType().Name,
                ex.Message.Replace(' ', '_'), true, elementIdBefore, "failure");
            throw;
        }
        var groupSynced = RoofAssemblyGroupSyncService.TrySyncForOwner(document, transaction, owner.ObjectId);
        var groupClear = RoofDisplayGroupService.TryOpenCanonicalGroup(document.Database, transaction,
                owner.ObjectId, OpenMode.ForRead, out var group) && group is not null &&
            !group.GetAllEntityIds().Intersect(ownedIds.Concat(annotations)).Any();
        Trace("group-sync", line, groupSynced ? "success" : "failed",
            groupClear ? "clear" : "member-remains", true, elementIdBefore,
            groupSynced && groupClear ? "pass" : "failure");
        return groupSynced && groupClear &&
            RoofGeneratedTimberStore.Read(line).Data is null &&
            RoofIndependentOrdinaryTimberStore.Read(line) == provenance &&
            annotations.All(id => RoofIndependentOrdinaryTimberStore.Read(
                (Entity)transaction.GetObject(id, OpenMode.ForRead))?.IndependentMemberId == memberId) &&
            physicalMatches.All(id =>
                RoofPhysical3DGeneratedStore.Read((Entity)transaction.GetObject(id, OpenMode.ForRead)).Data is null);
    }

    private static IReadOnlyList<ObjectId> FindAnnotations(Database database, Transaction transaction,
        string sourceHandle)
    {
        var blocks = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)transaction.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        return model.Cast<ObjectId>().Where(id => !id.IsErased &&
            transaction.GetObject(id, OpenMode.ForRead) is Entity entity && !entity.IsErased &&
            RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(entity, out var handle) &&
            string.Equals(handle, sourceHandle, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
