using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Explicit Stage 6 source-only intelligent rafter generation workflow.</summary>
internal static class RoofRafterCommandWorkflow
{
    public static void Run(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var editor = document.Editor;
        if (!TrySelectCurrentRoof(document, out var selectedRoof))
        {
            return;
        }

        if (selectedRoof.ExistingGeneratedRafterCount > 0)
        {
            editor.WriteMessage(UiStrings.Format(
                UiStrings.GetString("Command_RoofRafters_ExistingFoundFormat"),
                selectedRoof.ExistingGeneratedRafterCount));
            if (selectedRoof.GeneratedSetIsStale)
            {
                editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_ExistingStale"));
            }
            editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_ReplacementDeferred"));
            return;
        }

        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var canonicalRafterDefaults = TimberElementDefaults.For(
            TimberElementType.Rafter,
            defaultProfile);
        var uiPreferences = SettingsUiPreferencesStore.Load();
        var remembered = uiPreferences.AutomaticRafterPreferences ??
            RoofRafterPreferences.CreateFirstUse(canonicalRafterDefaults.Material);
        var dialog = new RoofRafterWindow(
            selectedRoof.Geometry,
            remembered,
            uiPreferences.Theme);
        SettingsWindowOwner.TryAssign(dialog, TryGetAutoCadMainWindowHandle());
        if (AcApp.ShowModalWindow(dialog) != true || dialog.Request is null)
        {
            return;
        }

        var result = TryCreateRafters(
            document,
            selectedRoof.OwnerId,
            selectedRoof.OwnerReference,
            dialog.Request,
            defaultProfile);
        if (result.IsSuccess)
        {
            SettingsUiPreferencesStore.Save(uiPreferences with
            {
                AutomaticRafterPreferences = dialog.Request.ToPreferences(),
            });
        }
        editor.WriteMessage(result.IsSuccess
            ? UiStrings.Format(
                UiStrings.GetString("Command_RoofRafters_CreatedFormat"),
                result.CreatedCount)
            : UiStrings.GetString(result.FailureMessageKey));
    }

    private static bool TrySelectCurrentRoof(
        Document document,
        out SelectedRoof selectedRoof)
    {
        selectedRoof = default!;
        var editor = document.Editor;
        while (true)
        {
            var selected = editor.GetEntity(new PromptEntityOptions(
                UiStrings.GetString("Command_RoofRafters_SelectPrompt")));
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
                if (resolution.Error == RoofOwnerSelectionError.UnrelatedObject)
                {
                    TransientNotificationService.Show(
                        "Command_Roof_InvalidObjectNotificationTitle",
                        "Command_Roof_InvalidObjectNotificationBody");
                }
                else
                {
                    editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_InvalidRoof"));
                }
                continue;
            }

            if (transaction.GetObject(resolution.OwnerId, OpenMode.ForRead) is not Polyline owner)
            {
                editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_InvalidRoof"));
                continue;
            }

            var sourceInput = RoofPolylineExtractor.Extract(owner);
            var validation = RoofFootprintValidator.Validate(sourceInput);
            var stored = RoofDefinitionStore.Read(owner);
            if (!validation.IsValid || validation.Footprint is null || stored.Data is null)
            {
                editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_InvalidRoof"));
                continue;
            }

            var restored = RoofDefinitionPersistence.Restore(
                sourceInput,
                validation.Footprint,
                stored.Data);
            if (!restored.IsValid || restored.Geometry is null)
            {
                editor.WriteMessage(UiStrings.GetString(
                    restored.Error == RoofDefinitionRestoreError.StaleFootprint
                        ? "Command_Roof_PersistedStale"
                        : "Command_RoofRafters_InvalidRoof"));
                return false;
            }

            var ownerReference = owner.Handle.ToString();
            var generatedIds = RoofGeneratedTimberStore.FindByOwner(
                document.Database,
                transaction,
                ownerReference);
            selectedRoof = new SelectedRoof(
                resolution.OwnerId,
                ownerReference,
                RoofPolylineExtractor.GetSourceElevation(owner),
                restored.Geometry,
                generatedIds.Count,
                IsGeneratedSetStale(
                    document.Database,
                    transaction,
                    generatedIds,
                    restored.Geometry.Signature));
            return true;
        }
    }

    private static RoofRafterCreationResult TryCreateRafters(
        Document document,
        ObjectId ownerId,
        string expectedOwnerReference,
        RoofRafterCreationRequest request,
        TimberElementDefaultProfile defaultProfile)
    {
        try
        {
            var layerProfile = ElementLayerProfileStore.Load();
            using var documentLock = document.LockDocument();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            if (transaction.GetObject(ownerId, OpenMode.ForRead) is not Polyline owner ||
                !string.Equals(
                    owner.Handle.ToString(),
                    expectedOwnerReference,
                    StringComparison.OrdinalIgnoreCase))
            {
                return RoofRafterCreationResult.Failure("Command_RoofRafters_SourceChanged");
            }

            var sourceInput = RoofPolylineExtractor.Extract(owner);
            var validation = RoofFootprintValidator.Validate(sourceInput);
            var stored = RoofDefinitionStore.Read(owner);
            if (!validation.IsValid || validation.Footprint is null || stored.Data is null)
            {
                return RoofRafterCreationResult.Failure("Command_RoofRafters_SourceChanged");
            }

            var restored = RoofDefinitionPersistence.Restore(
                sourceInput,
                validation.Footprint,
                stored.Data);
            if (!restored.IsValid || restored.Geometry is null)
            {
                return RoofRafterCreationResult.Failure(
                    restored.Error == RoofDefinitionRestoreError.StaleFootprint
                        ? "Command_Roof_PersistedStale"
                        : "Command_RoofRafters_SourceChanged");
            }
            if (RoofGeneratedTimberStore.FindByOwner(
                    document.Database,
                    transaction,
                    expectedOwnerReference).Count > 0)
            {
                return RoofRafterCreationResult.Failure(
                    "Command_RoofRafters_ReplacementDeferred");
            }

            var layoutResult = SimpleGableRafterLayoutSolver.Solve(
                restored.Geometry,
                new RafterLayoutParameters(
                    request.MaximumSpacingMm,
                    request.WidthMm));
            if (!layoutResult.IsValid || layoutResult.Layout is null)
            {
                return RoofRafterCreationResult.Failure("Command_RoofRafters_InvalidSpacing");
            }

            var layout = layoutResult.Layout;
            var sourceElevation = RoofPolylineExtractor.GetSourceElevation(owner);
            var canonicalRafterData = TimberElementDefaults.For(
                TimberElementType.Rafter,
                defaultProfile) with
            {
                WidthMm = request.WidthMm,
                HeightMm = request.HeightMm,
                SlopeDegrees = restored.Geometry.SlopeDegrees,
                // Neutral layout stays eave -> ridge. Canonical KROVY arrows use
                // Start -> End unless reversed, so true gives ridge -> eave.
                IsSlopeDirectionReversed = true,
                Material = request.Material,
            };
            var requests = layout.Rafters
                .Select(rafter => new TimberSourceLineCreationRequest(
                    new Point3d(rafter.PlanStart.X, rafter.PlanStart.Y, sourceElevation),
                    new Point3d(rafter.PlanEnd.X, rafter.PlanEnd.Y, sourceElevation),
                    canonicalRafterData))
                .ToArray();
            var createdRafters = TimberSourceLineCreationService.Create(
                document.Database,
                transaction,
                document.Editor,
                requests,
                defaultProfile,
                layerProfile,
                (line, currentTransaction, index) =>
                {
                    var rafter = layout.Rafters[index];
                    RoofGeneratedTimberStore.Write(
                        line,
                        currentTransaction,
                        new RoofGeneratedTimberData(
                            RoofGeneratedTimberDataSchema.CurrentVersion,
                            expectedOwnerReference,
                            RoofGeneratedTimberKind.Rafter,
                            rafter.Face,
                            rafter.StationIndex,
                            rafter.StationCount,
                            layout.RequestedMaximumSpacingMm,
                            layout.Signature));
                });
            TimberCreatedElementAnnotationService.EnsureForCreatedElements(
                document.Database,
                transaction,
                createdRafters,
                defaultProfile);
            transaction.Commit();
            return RoofRafterCreationResult.Success(layout.Rafters.Count);
        }
        catch (System.Exception)
        {
            return RoofRafterCreationResult.Failure("Command_RoofRafters_GenerationFailed");
        }
    }

    private static bool IsGeneratedSetStale(
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

    private static IntPtr TryGetAutoCadMainWindowHandle()
    {
        try
        {
            return AcApp.MainWindow?.Handle ?? IntPtr.Zero;
        }
        catch (System.Exception)
        {
            return IntPtr.Zero;
        }
    }

    private sealed record SelectedRoof(
        ObjectId OwnerId,
        string OwnerReference,
        double SourceElevation,
        SimpleGableRoofGeometry Geometry,
        int ExistingGeneratedRafterCount,
        bool GeneratedSetIsStale);

    private sealed record RoofRafterCreationResult(
        bool IsSuccess,
        int CreatedCount,
        string FailureMessageKey)
    {
        public static RoofRafterCreationResult Success(int createdCount) =>
            new(true, createdCount, string.Empty);

        public static RoofRafterCreationResult Failure(string messageKey) =>
            new(false, 0, messageKey);
    }
}
