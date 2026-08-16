using System.Reflection;
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
                ApplyResizes(document, plan.ResizeOwnerIds);
            }

            if (plan.UnsupportedOwnerIds.Count > 0)
            {
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
            // when ResizeOwnerIds / UnsupportedOwnerIds contain it (precedence).
            if (plan.DisplayTamperOwnerIds.Count > 0 &&
                LiveGeometryCommandRules.IsUndoGroupingSourceCommand(globalCommandName) &&
                ApplyDisplayTampers(document, plan.DisplayTamperOwnerIds))
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
            // Source lifecycle wins: one outcome per owner per command.
            if (resizeOwners.Contains(ownerId) || unsupportedOwners.Contains(ownerId))
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
            foreach (var ownerId in ownerIds)
            {
                if (!TryApplyResize(document.Database, transaction, ownerId))
                {
                    continue;
                }

                wrote = true;
            }

            if (wrote)
            {
                transaction.Commit();
            }
        }
    }

    private static bool ApplyDisplayTampers(
        Document document,
        IReadOnlyCollection<ObjectId> ownerIds)
    {
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

    private static bool TryApplyResize(
        Database database,
        Transaction transaction,
        ObjectId ownerId)
    {
        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForWrite,
                out var owner,
                database) ||
            owner is null)
        {
            return false;
        }

        var classification = ClassifyOwner(owner);
        if (classification.Kind != RoofSourceChangeKind.SupportedResize ||
            classification.Geometry is null)
        {
            return false;
        }

        var input = RoofPolylineExtractor.Extract(owner);
        var validation = RoofFootprintValidator.Validate(input);
        if (!validation.IsValid || validation.Footprint is null)
        {
            return false;
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
        return RoofDisplayService.Rebuild(
            database,
            transaction,
            owner.ObjectId,
            owner.Handle.ToString(),
            edges,
            signature);
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

        return RoofDefinitionPersistence.Classify(input, validation.Footprint, stored.Data);
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
}
