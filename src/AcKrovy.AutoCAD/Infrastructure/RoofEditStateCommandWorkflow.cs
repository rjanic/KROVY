using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class RoofEditStateCommandWorkflow
{
    public static void Unlock(Document document) =>
        SetEditState(document, RoofEditState.Unlocked);

    public static void Lock(Document document) =>
        SetEditState(document, RoofEditState.Locked);

    public static void ResetEdits(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var editor = document.Editor;
        if (!TrySelectOwner(document, out var ownerId, out var generatedKey))
        {
            return;
        }

        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    ownerId,
                    OpenMode.ForWrite,
                    out var owner,
                    document.Database) ||
                owner is null)
            {
                editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_InvalidRoof"));
                return;
            }

            var stored = RoofDefinitionStore.Read(owner);
            if (stored.Data is null)
            {
                editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_InvalidRoof"));
                return;
            }

            var set = new RoofManualOverrideSet(stored.Data.Overrides);
            if (generatedKey is RoofGeneratedMemberKey key)
            {
                if (!set.TryGet(key, out _))
                {
                    editor.WriteMessage(UiStrings.GetString("Command_RoofResetEdits_NoEdits"));
                    return;
                }

                set = set.Remove(key);
            }
            else if (set.Count == 0)
            {
                editor.WriteMessage(UiStrings.GetString("Command_RoofResetEdits_NoEdits"));
                return;
            }
            else
            {
                set = set.Clear();
            }

            var updated = RoofGeneratedMemberOverrideRules.WithEditState(
                stored.Data,
                stored.Data.EditState,
                set.Items);
            RoofDefinitionStore.Write(owner, transaction, updated);
            if (!TryRebuildGeneratedSet(document, transaction, owner, updated))
            {
                editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_GenerationFailed"));
                return;
            }

            RoofUnlockIndicatorService.Sync(document.Database, transaction, owner);
            RoofDisplayGroupSelectabilityService.ApplyForOwner(document.Database, transaction, ownerId);
            transaction.Commit();
        }

        editor.WriteMessage(UiStrings.GetString("Command_RoofResetEdits_Reset"));
    }

    private static void SetEditState(Document document, RoofEditState desired)
    {
        ArgumentNullException.ThrowIfNull(document);
        var editor = document.Editor;
        if (!TrySelectOwner(document, out var ownerId, out _))
        {
            return;
        }

        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    ownerId,
                    OpenMode.ForWrite,
                    out var owner,
                    document.Database) ||
                owner is null)
            {
                editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_InvalidRoof"));
                return;
            }

            var stored = RoofDefinitionStore.Read(owner);
            if (stored.Data is null)
            {
                editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_InvalidRoof"));
                return;
            }

            if (stored.Data.EditState == desired &&
                stored.Data.SchemaVersion == RoofDefinitionDataSchema.CurrentVersion)
            {
                RoofUnlockIndicatorService.Sync(document.Database, transaction, owner);
                transaction.Commit();
                editor.WriteMessage(UiStrings.GetString(
                    desired == RoofEditState.Unlocked
                        ? "Command_RoofUnlock_AlreadyUnlocked"
                        : "Command_RoofLock_AlreadyLocked"));
                return;
            }

            var updated = RoofGeneratedMemberOverrideRules.WithEditState(
                stored.Data,
                desired,
                stored.Data.Overrides);
            RoofDefinitionStore.Write(owner, transaction, updated);
            RoofUnlockIndicatorService.Sync(document.Database, transaction, owner);
            RoofDisplayGroupSelectabilityService.ApplyForOwner(document.Database, transaction, ownerId);
            transaction.Commit();
        }

        editor.WriteMessage(UiStrings.GetString(
            desired == RoofEditState.Unlocked
                ? "Command_RoofUnlock_Unlocked"
                : "Command_RoofLock_Locked"));
        if (desired == RoofEditState.Unlocked)
        {
            TransientNotificationService.Show(
                "Command_RoofUnlock_UnlockedNotificationTitle",
                "Command_RoofUnlock_UnlockedNotificationBody");
        }
    }

    private static bool TrySelectOwner(
        Document document,
        out ObjectId ownerId,
        out RoofGeneratedMemberKey? generatedKey)
    {
        ownerId = ObjectId.Null;
        generatedKey = null;
        var selected = document.Editor.GetEntity(new PromptEntityOptions(
            UiStrings.GetString("Command_RoofUnlock_SelectPrompt")));
        if (selected.Status != PromptStatus.OK)
        {
            return false;
        }

        using var transaction = document.Database.TransactionManager.StartTransaction();
        var resolution = RoofOwnerSelectionResolver.Resolve(
            document.Database,
            transaction,
            selected.ObjectId);
        if (!resolution.IsResolved)
        {
            TransientNotificationService.Show(
                "Command_Roof_InvalidObjectNotificationTitle",
                "Command_Roof_InvalidObjectNotificationBody");
            return false;
        }

        ownerId = resolution.OwnerId;
        if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                selected.ObjectId,
                OpenMode.ForRead,
                out var entity,
                document.Database) &&
            entity is not null)
        {
            var timber = RoofGeneratedTimberStore.Read(entity);
            if (timber.Data is not null)
            {
                generatedKey = RoofGeneratedMemberKey.From(timber.Data);
            }
        }

        return true;
    }

    private static bool TryRebuildGeneratedSet(
        Document document,
        Transaction transaction,
        Polyline owner,
        RoofDefinitionData data)
    {
        var input = RoofPolylineExtractor.Extract(owner);
        var validation = RoofFootprintValidator.Validate(input);
        if (!validation.IsValid || validation.Footprint is null)
        {
            return false;
        }

        var restored = RoofDefinitionPersistence.Restore(input, validation.Footprint, data);
        if (!restored.IsValid || restored.Geometry is null)
        {
            return false;
        }

        var outcome = RoofGeneratedRafterSetService.TryReplaceForSupportedResize(
            document.Database,
            transaction,
            document.Editor,
            owner,
            restored.Geometry,
            TimberElementDefaultProfileStore.Load(),
            ElementLayerProfileStore.Load());
        return outcome is
            RoofGeneratedRafterSetService.ReplacementOutcome.Replaced or
            RoofGeneratedRafterSetService.ReplacementOutcome.NotApplicable;
    }
}
