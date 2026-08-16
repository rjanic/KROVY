using System.Reflection;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Narrow live SimpleGable rectangular resize and display-cache repair on the existing
/// live-geometry path. Does not add a roof reactor, overrule, or deep-clone hook.
/// </summary>
internal static class RoofLiveResizeService
{
    private const BindingFlags ComInvoke =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    /// <summary>
    /// Owners that already received SOURCE SupportedResize / Unsupported in the current
    /// STRETCH / GRIP_STRETCH command scope. Display rebuild side-effects must not be
    /// reinterpreted as independent display-only tamper for these owners.
    /// Cleared on the next CommandWillStart / cancel / fail boundary.
    /// </summary>
    private static readonly HashSet<ObjectId> SourceHandledOwnersThisCommand = new();

    public static void BeginStretchCommandScope() => SourceHandledOwnersThisCommand.Clear();

    public static void EndStretchCommandScope() => SourceHandledOwnersThisCommand.Clear();

    public static IReadOnlyCollection<ObjectId> Process(
        Document document,
        string? globalCommandName,
        IReadOnlyList<ObjectId> modifiedIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName) ||
            modifiedIds.Count == 0)
        {
            return Array.Empty<ObjectId>();
        }

        try
        {
            var plan = Inspect(document.Database, modifiedIds);
            if (plan.RelatedIds.Count == 0)
            {
                return Array.Empty<ObjectId>();
            }

            if (plan.ResizeOwnerIds.Count > 0)
            {
                foreach (var ownerId in plan.ResizeOwnerIds)
                {
                    SourceHandledOwnersThisCommand.Add(ownerId);
                }

                ApplyResizes(document, plan.ResizeOwnerIds);
            }

            if (plan.UnsupportedOwnerIds.Count > 0)
            {
                foreach (var ownerId in plan.UnsupportedOwnerIds)
                {
                    SourceHandledOwnersThisCommand.Add(ownerId);
                }

                // One CLI diagnostic line remains for host-log visibility.
                document.Editor.WriteMessage(
                    UiStrings.GetString("Command_Roof_PersistedStale"));
                // Visible UX for unsupported STRETCH / grip stretch only.
                if (LiveGeometryCommandRules.IsUndoGroupingSourceCommand(globalCommandName))
                {
                    TransientNotificationService.Show(
                        "Command_Roof_UnsupportedStretchNotificationTitle",
                        "Command_Roof_UnsupportedStretchNotificationBody");
                }
            }

            // Display-only STRETCH / GRIP_STRETCH: source path already handled this owner
            // when ResizeOwnerIds / UnsupportedOwnerIds contain it (precedence), including
            // deferred display-rebuild batches of the same command.
            IReadOnlyCollection<ObjectId> displayTamperOwners = plan.DisplayTamperOwnerIds;
            if (displayTamperOwners.Count > 0 &&
                LiveGeometryCommandRules.IsGripStretchCommand(globalCommandName))
            {
                // Coherent rigid GROUP transform (true MOVE-like grip) before side-resize
                // adoption or DisplayTamper repair.
                displayTamperOwners = TryAcceptRigidGroupTransforms(
                    document,
                    displayTamperOwners,
                    modifiedIds,
                    globalCommandName);
            }

            if (displayTamperOwners.Count > 0 &&
                LiveGeometryCommandRules.IsGripStretchCommand(globalCommandName))
            {
                displayTamperOwners = TryAdoptGroupGripResizes(
                    document,
                    displayTamperOwners,
                    globalCommandName);
            }

            if (displayTamperOwners.Count > 0 &&
                LiveGeometryCommandRules.IsUndoGroupingSourceCommand(globalCommandName) &&
                ApplyDisplayTampers(document, displayTamperOwners, modifiedIds))
            {
                TransientNotificationService.Show(
                    "Command_Roof_DisplayTamperNotificationTitle",
                    "Command_Roof_DisplayTamperNotificationBody");
            }
            return plan.RelatedIds;
        }
        catch (System.Exception ex)
        {
            document.Editor.WriteMessage(
                UiStrings.Format(UiStrings.WarningLiveRefreshSkippedFormat, ex.Message));
            return Array.Empty<ObjectId>();
        }
    }

    public static bool TryBeginGroupedUndo(Document document) =>
        TryInvokeUndoMark(document, "StartUndoMark");

    public static void TryEndGroupedUndo(Document document, bool markOpen)
    {
        if (markOpen)
        {
            _ = TryInvokeUndoMark(document, "EndUndoMark");
        }
    }

    private static InspectionPlan Inspect(
        Database database,
        IReadOnlyList<ObjectId> modifiedIds)
    {
        var related = new HashSet<ObjectId>();
        var resizeOwners = new HashSet<ObjectId>();
        var unsupportedOwners = new HashSet<ObjectId>();
        var displayTamperCandidates = new HashSet<ObjectId>();
        using var transaction = database.TransactionManager.StartTransaction();
        foreach (var id in modifiedIds.Distinct())
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

            if (RoofDisplayStore.Read(entity).Exists)
            {
                related.Add(id);
                var resolution = RoofOwnerSelectionResolver.Resolve(database, transaction, id);
                if (resolution.IsResolved)
                {
                    related.Add(resolution.OwnerId);
                    displayTamperCandidates.Add(resolution.OwnerId);
                }
            }

            if (entity is not Polyline polyline ||
                RoofDefinitionStore.Read(polyline).Data is null)
            {
                continue;
            }

            related.Add(id);
            switch (ClassifyOwner(polyline).Kind)
            {
                case RoofSourceChangeKind.SupportedResize:
                    resizeOwners.Add(id);
                    break;
                case RoofSourceChangeKind.Unsupported:
                    unsupportedOwners.Add(id);
                    break;
            }
        }

        var displayTamperOwners = new HashSet<ObjectId>();
        foreach (var ownerId in displayTamperCandidates)
        {
            // Source lifecycle wins: one outcome per owner per command, including
            // deferred display-rebuild batches after SupportedResize/Unsupported.
            if (resizeOwners.Contains(ownerId) ||
                unsupportedOwners.Contains(ownerId) ||
                SourceHandledOwnersThisCommand.Contains(ownerId))
            {
                continue;
            }

            displayTamperOwners.Add(ownerId);
        }

        return new InspectionPlan(
            related,
            resizeOwners,
            unsupportedOwners,
            displayTamperOwners);
    }

    private static void ApplyResizes(Document document, IReadOnlyCollection<ObjectId> ownerIds)
    {
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var wrote = false;
            try
            {
                foreach (var ownerId in ownerIds)
                {
                    var result = TryApplyResize(document, transaction, ownerId);
                    if (result == ResizeApplyResult.HardFailure)
                    {
                        document.Editor.WriteMessage(
                            UiStrings.GetString("Command_RoofRafters_GenerationFailed"));
                        return;
                    }

                    if (result == ResizeApplyResult.Applied)
                    {
                        wrote = true;
                    }
                }

                if (wrote)
                {
                    transaction.Commit();
                }
            }
            catch (System.Exception)
            {
                document.Editor.WriteMessage(
                    UiStrings.GetString("Command_RoofRafters_GenerationFailed"));
            }
        }
    }

    private static ResizeApplyResult TryApplyResize(
        Document document,
        Transaction transaction,
        ObjectId ownerId)
    {
        var database = document.Database;
        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForWrite,
                out var owner,
                database) ||
            owner is null)
        {
            return ResizeApplyResult.Skipped;
        }

        var classification = ClassifyOwner(owner);
        if (classification.Kind != RoofSourceChangeKind.SupportedResize ||
            classification.Geometry is null)
        {
            return ResizeApplyResult.Skipped;
        }

        var input = RoofPolylineExtractor.Extract(owner);
        var validation = RoofFootprintValidator.Validate(input);
        if (!validation.IsValid || validation.Footprint is null)
        {
            return ResizeApplyResult.Skipped;
        }

        var updated = RoofDefinitionPersistence.Create(
            input,
            validation.Footprint,
            classification.Geometry);
        RoofDefinitionStore.Write(owner, transaction, updated);
        var edges = SimpleGableRoofWireframe.Create(
            classification.Geometry,
            RoofPolylineExtractor.GetSourceElevation(owner));
        var signature = SimpleGableRoofWireframe.BuildGenerationSignature(edges);
        if (!RoofDisplayService.Rebuild(
                database,
                transaction,
                owner.ObjectId,
                owner.Handle.ToString(),
                edges,
                signature))
        {
            return ResizeApplyResult.HardFailure;
        }

        var rafterOutcome = RoofGeneratedRafterSetService.TryReplaceForSupportedResize(
            database,
            transaction,
            document.Editor,
            owner,
            classification.Geometry,
            TimberElementDefaultProfileStore.Load(),
            ElementLayerProfileStore.Load());
        if (rafterOutcome == RoofGeneratedRafterSetService.ReplacementOutcome.Failed)
        {
            return ResizeApplyResult.HardFailure;
        }

        if (rafterOutcome == RoofGeneratedRafterSetService.ReplacementOutcome.SkippedAmbiguousRecipe)
        {
            document.Editor.WriteMessage(
                UiStrings.GetString("Command_RoofRafters_RecipeAmbiguous"));
        }
        else if (rafterOutcome == RoofGeneratedRafterSetService.ReplacementOutcome.SkippedInvalidLayout)
        {
            document.Editor.WriteMessage(
                UiStrings.GetString("Command_RoofRafters_InvalidSpacing"));
        }

        return ResizeApplyResult.Applied;
    }

    private static IReadOnlyCollection<ObjectId> TryAcceptRigidGroupTransforms(
        Document document,
        IReadOnlyCollection<ObjectId> displayTamperOwnerIds,
        IReadOnlyList<ObjectId> modifiedIds,
        string? globalCommandName)
    {
        _ = globalCommandName;
        var remaining = new HashSet<ObjectId>(displayTamperOwnerIds);
        var accepted = new List<ObjectId>();
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            // Read-only accept path — no Commit needed; avoid write side-effects.
            foreach (var ownerId in displayTamperOwnerIds)
            {
                if (!RoofGroupGripRigidTransformService.TryAcceptRigidGroupTransform(
                        document.Database,
                        transaction,
                        ownerId,
                        modifiedIds,
                        out var rejectionReason,
                        out var result))
                {
                    _ = rejectionReason;
                    continue;
                }

                remaining.Remove(ownerId);
                accepted.Add(ownerId);
                SourceHandledOwnersThisCommand.Add(ownerId);
                _ = result;
            }
        }

        _ = accepted;
        return remaining;
    }

    private static IReadOnlyCollection<ObjectId> TryAdoptGroupGripResizes(
        Document document,
        IReadOnlyCollection<ObjectId> displayTamperOwnerIds,
        string? globalCommandName)
    {
        _ = globalCommandName;
        var remaining = new HashSet<ObjectId>(displayTamperOwnerIds);
        var adopted = new List<ObjectId>();
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var wrote = false;
            foreach (var ownerId in displayTamperOwnerIds)
            {
                if (!RoofGroupGripResizeAdoptionService.TryAdoptSupportedGroupGripResize(
                        document.Database,
                        transaction,
                        ownerId,
                        out var rejectionReason))
                {
                    _ = rejectionReason;
                    continue;
                }

                var resizeResult = TryApplyResize(document, transaction, ownerId);
                if (resizeResult != ResizeApplyResult.Applied)
                {
                    continue;
                }

                remaining.Remove(ownerId);
                adopted.Add(ownerId);
                SourceHandledOwnersThisCommand.Add(ownerId);
                wrote = true;
            }

            if (wrote)
            {
                transaction.Commit();
            }
        }

        _ = adopted;
        return remaining;
    }

    private static bool ApplyDisplayTampers(
        Document document,
        IReadOnlyCollection<ObjectId> ownerIds,
        IReadOnlyList<ObjectId> modifiedIds)
    {
        _ = modifiedIds;
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var wrote = false;
            foreach (var ownerId in ownerIds)
            {
                if (!TryApplyDisplayTamper(document.Database, transaction, ownerId))
                {
                    continue;
                }

                wrote = true;
            }

            if (wrote)
            {
                transaction.Commit();
            }

            return wrote;
        }
    }

    private static bool TryApplyDisplayTamper(
        Database database,
        Transaction transaction,
        ObjectId ownerId)
    {
        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                database) ||
            owner is null)
        {
            return false;
        }

        // Source unchanged and still restores: rebuild disposable display cache only.
        var classification = ClassifyOwner(owner);
        if (classification.Kind != RoofSourceChangeKind.RigidEquivalent ||
            classification.Geometry is null)
        {
            return false;
        }

        var edges = SimpleGableRoofWireframe.Create(
            classification.Geometry,
            RoofPolylineExtractor.GetSourceElevation(owner));
        var signature = SimpleGableRoofWireframe.BuildGenerationSignature(edges);
        return RoofDisplayService.Rebuild(
            database,
            transaction,
            owner.ObjectId,
            owner.Handle.ToString(),
            edges,
            signature);
    }

    private static RoofSourceChangeClassification ClassifyOwner(Polyline polyline)
    {
        var stored = RoofDefinitionStore.Read(polyline);
        if (stored.Data is null)
        {
            return new RoofSourceChangeClassification(
                RoofSourceChangeKind.None,
                null,
                RoofDefinitionRestoreError.InvalidDefinition);
        }

        var input = RoofPolylineExtractor.Extract(polyline);
        var validation = RoofFootprintValidator.Validate(input);
        if (!validation.IsValid || validation.Footprint is null)
        {
            return new RoofSourceChangeClassification(
                RoofSourceChangeKind.Unsupported,
                null,
                RoofDefinitionRestoreError.StaleFootprint);
        }

        return RoofDefinitionPersistence.Classify(
            input,
            validation.Footprint,
            stored.Data);
    }

    private static bool TryInvokeUndoMark(Document document, string methodName)
    {
        try
        {
            var acadDocument = GetAcadDocument(document);
            if (acadDocument is null)
            {
                return false;
            }

            acadDocument.GetType().InvokeMember(
                methodName,
                BindingFlags.InvokeMethod | ComInvoke,
                binder: null,
                target: acadDocument,
                args: null);
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static object? GetAcadDocument(Document document)
    {
        var getter = document.GetType().GetMethod("GetAcadDocument", Type.EmptyTypes);
        if (getter?.Invoke(document, null) is { } fromDocument)
        {
            return fromDocument;
        }

        var acadApplication = AcApp.AcadApplication;
        return acadApplication?.GetType().InvokeMember(
            "ActiveDocument",
            BindingFlags.GetProperty | ComInvoke,
            binder: null,
            target: acadApplication,
            args: null);
    }

    private sealed record InspectionPlan(
        HashSet<ObjectId> RelatedIds,
        HashSet<ObjectId> ResizeOwnerIds,
        HashSet<ObjectId> UnsupportedOwnerIds,
        HashSet<ObjectId> DisplayTamperOwnerIds);

    private enum ResizeApplyResult
    {
        Skipped = 0,
        Applied = 1,
        HardFailure = 2,
    }
}
