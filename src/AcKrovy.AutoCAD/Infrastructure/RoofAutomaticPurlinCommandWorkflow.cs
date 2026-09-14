using System.Globalization;
using System.Windows;
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
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Production CLI workflow for one explicit Automatic-Purlin edit/apply session.
/// It registers no roof-change lifecycle and never writes from a preview callback.
/// </summary>
internal static class RoofAutomaticPurlinCommandWorkflow
{
    public static void Run(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var editor = document.Editor;
        var culture = AppLanguageService.CurrentUiCulture;
        var selectionOptions = new PromptEntityOptions(
            "\n" + UiStrings.GetString("AutomaticPurlin_CommandSelectRoof", culture));
        selectionOptions.SetRejectMessage(
            "\n" + UiStrings.GetString("AutomaticPurlin_CommandPolylineRequired", culture));
        selectionOptions.AddAllowedClass(typeof(Polyline), exactMatch: true);
        var selection = editor.GetEntity(selectionOptions);
        if (selection.Status != PromptStatus.OK)
        {
            return;
        }

        RoofTransientPreviewSession? previewSession = null;
        AutomaticPurlinDialogWindow? window = null;
        string? lastPreviewDiagnostic = null;
        try
        {
            var snapshot = ReadSnapshot(document, selection.ObjectId, out var snapshotFailure);
            if (snapshot is null)
            {
                editor.WriteMessage(
                    "\n" + UiStrings.GetString("AutomaticPurlin_CommandHipRoofRequired", culture));
                WriteApplyFailure(editor, "-", snapshotFailure);
                return;
            }

            var defaultProfile = TimberElementDefaultProfileStore.Load();
            var layerProfile = ElementLayerProfileStore.Load();
            var viewModel = new AutomaticPurlinDialogViewModel(
                snapshot.Geometry,
                snapshot.BoundaryResolution.Provenance,
                snapshot.Layout,
                snapshot.LayoutExists,
                snapshot.Datum,
                snapshot.DatumExists,
                TimberElementDefaults.For(TimberElementType.Purlin, defaultProfile),
                TimberElementDefaults.For(TimberElementType.Rafter, defaultProfile),
                culture,
                AutomaticPurlinDialogMode.ProductionEdit,
                snapshot.ExistingAutomaticPurlinCount);
            window = new AutomaticPurlinDialogWindow(
                viewModel,
                SettingsUiPreferencesStore.Load().Theme);
            SettingsWindowOwner.TryAssign(window, TryGetAutoCadMainWindowHandle());

            void RefreshPreview(object? sender, EventArgs args)
            {
                previewSession?.Dispose();
                previewSession = null;
                if (!snapshot.BoundaryResolution.IsValid)
                {
                    WritePreviewDiagnostic(
                        $"ROOF_PURLIN_PREVIEW owner={snapshot.OwnerReference}" +
                        $" result=blocked reason={snapshot.BoundaryResolution.Error}");
                    return;
                }

                if (!viewModel.TryGetPreviewPlan(out var plan) || plan is null)
                {
                    WritePreviewDiagnostic(
                        $"ROOF_PURLIN_PREVIEW owner={snapshot.OwnerReference}" +
                        $" result=invalid reason={viewModel.PreviewDiagnosticReason}");
                    return;
                }

                var ridgeCount = plan.Items.Count(item =>
                    item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);
                var intermediateCount = plan.Items.Count(item =>
                    item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate);
                if (plan.Items.Count > 0)
                {
                    previewSession = RoofTransientPreviewSession.ShowAutomaticPurlins(
                        document,
                        plan,
                        snapshot.SourceElevation);
                }

                WritePreviewDiagnostic(
                    $"ROOF_PURLIN_PREVIEW owner={snapshot.OwnerReference}" +
                    $" boundaryIdentity={BoundaryIdentityToken(snapshot.BoundaryResolution.Source)}" +
                    $" ridge={ridgeCount.ToString(CultureInfo.InvariantCulture)}" +
                    $" intermediate={intermediateCount.ToString(CultureInfo.InvariantCulture)}" +
                    $" total={plan.Items.Count.ToString(CultureInfo.InvariantCulture)} result=ok");
            }

            void ApplyRequested(
                object? sender,
                AutomaticPurlinApplyRequestedEventArgs args)
            {
                previewSession?.Dispose();
                previewSession = null;
                var result = RoofAutomaticPurlinProductionApplyService.Apply(
                    document,
                    snapshot.OwnerId,
                    args.Layout,
                    args.Datum,
                    args.PreviewPlan,
                    defaultProfile,
                    layerProfile);
                if (!result.IsSuccess)
                {
                    WriteApplySummary(editor, result);
                    editor.WriteMessage(
                        "\n" + UiStrings.GetString("AutomaticPurlin_ApplyFailed", culture));
                    window.CompleteFailedApply();
                    return;
                }

                WriteOwnerWriteDiagnostics(editor, result);
                WriteApplySummary(editor, result);
                editor.WriteMessage(
                    "\n" + UiStrings.GetString("AutomaticPurlin_ApplySucceeded", culture));
                window.CompleteSuccessfulApply();
            }

            void WritePreviewDiagnostic(string message)
            {
                if (string.Equals(lastPreviewDiagnostic, message, StringComparison.Ordinal))
                {
                    return;
                }

                lastPreviewDiagnostic = message;
                editor.WriteMessage("\n" + message);
            }

            void CloseForDocumentDestruction(object sender, DocumentCollectionEventArgs args)
            {
                if (!ReferenceEquals(args.Document, document) || window.IsClosed)
                {
                    return;
                }

                _ = window.Dispatcher.BeginInvoke(new Action(window.Close));
            }

            window.PreviewRequested += RefreshPreview;
            window.ApplyRequested += ApplyRequested;
            AcApplication.DocumentManager.DocumentToBeDestroyed += CloseForDocumentDestruction;
            try
            {
                _ = AcApplication.ShowModalWindow(window);
            }
            finally
            {
                window.PreviewRequested -= RefreshPreview;
                window.ApplyRequested -= ApplyRequested;
                AcApplication.DocumentManager.DocumentToBeDestroyed -= CloseForDocumentDestruction;
            }
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                "\n" + string.Format(
                    culture,
                    UiStrings.GetString("AutomaticPurlin_CommandFailedFormat", culture),
                    exception.GetType().Name));
            WriteApplyFailure(editor, "-", exception.GetType().Name);
        }
        finally
        {
            previewSession?.Dispose();
            if (window is { IsClosed: false })
            {
                window.Close();
            }
        }
    }

    private static AutomaticPurlinProductionSnapshot? ReadSnapshot(
        Document document,
        ObjectId ownerId,
        out string failure)
    {
        using var transaction = document.Database.TransactionManager.StartOpenCloseTransaction();
        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                document.Database) ||
            owner is null)
        {
            failure = "authoritative-roof-required";
            return null;
        }

        var storedDefinition = RoofDefinitionStore.Read(owner);
        var sourceInput = RoofPolylineExtractor.Extract(owner);
        var validated = RoofFootprintValidator.Validate(sourceInput);
        if (storedDefinition.Data is null ||
            !validated.IsValid ||
            validated.Footprint is null)
        {
            failure = "invalid-roof-source";
            return null;
        }

        var restored = RoofDefinitionPersistence.Restore(
            sourceInput,
            validated.Footprint,
            storedDefinition.Data);
        if (!restored.IsValid || restored.Geometry is not HipRoofGeometry geometry)
        {
            failure = "hip-geometry-required";
            return null;
        }

        var layoutRead = RoofPurlinLayoutStore.Read(owner);
        if (layoutRead.Exists && layoutRead.Data is null)
        {
            failure = "layout-" + layoutRead.Error;
            return null;
        }

        var datumRead = RoofRelativeElevationDatumStore.Read(owner);
        if (datumRead.Exists && datumRead.Data is null)
        {
            failure = "relative-elevation-datum-" + datumRead.Error;
            return null;
        }

        var boundaryRead = RoofBoundaryIdentityStore.Read(owner);
        var boundaryResolution = RoofBoundaryIdentityPreviewResolver.Resolve(
            sourceInput,
            boundaryRead.Data,
            boundaryRead.Error);
        if (!boundaryResolution.IsValid)
        {
            failure = "boundary-identity-" + boundaryResolution.Error;
            return null;
        }

        var existingState =
            RoofAutomaticPurlinMaterializationService.InspectExistingOwnerStateInTransaction(
                document.Database,
                transaction,
                owner);
        if (!existingState.IsValid)
        {
            failure = existingState.Result;
            return null;
        }

        failure = string.Empty;
        return new AutomaticPurlinProductionSnapshot(
            ownerId,
            owner.Handle.ToString(),
            geometry,
            boundaryResolution,
            layoutRead.Data ?? RoofAutomaticPurlinLayout.Empty,
            layoutRead.Exists,
            datumRead.Data,
            datumRead.Exists,
            RoofPolylineExtractor.GetSourceElevation(owner),
            existingState.ExistingCount);
    }

    private static void WriteOwnerWriteDiagnostics(
        Editor editor,
        RoofAutomaticPurlinApplyResult result)
    {
        editor.WriteMessage(
            "\nROOF_PURLIN_LAYOUT_WRITE" +
            $" owner={result.OwnerReference}" +
            $" result={WriteResultToken(result.LayoutWrite)}");
        editor.WriteMessage(
            "\nROOF_RELATIVE_DATUM_WRITE" +
            $" owner={result.OwnerReference}" +
            $" result={WriteResultToken(result.DatumWrite)}");
    }

    private static void WriteApplySummary(
        Editor editor,
        RoofAutomaticPurlinApplyResult result)
    {
        var materialization = result.Materialization;
        editor.WriteMessage(
            "\nROOF_PURLIN_APPLY" +
            $" owner={result.OwnerReference}" +
            $" desired={materialization.Desired.ToString(CultureInfo.InvariantCulture)}" +
            $" actual={materialization.Actual.ToString(CultureInfo.InvariantCulture)}" +
            $" ridge={materialization.Ridge.ToString(CultureInfo.InvariantCulture)}" +
            $" intermediate={materialization.Intermediate.ToString(CultureInfo.InvariantCulture)}" +
            $" created={materialization.Created.ToString(CultureInfo.InvariantCulture)}" +
            $" existing={materialization.Existing.ToString(CultureInfo.InvariantCulture)}" +
            $" updated={materialization.Updated.ToString(CultureInfo.InvariantCulture)}" +
            $" staleRemoved={materialization.StaleRemoved.ToString(CultureInfo.InvariantCulture)}" +
            $" groupCanonical={(materialization.GroupCanonical ? "1" : "0")}" +
            $" result={result.Result}");
    }

    private static void WriteApplyFailure(Editor editor, string owner, string result) =>
        WriteApplySummary(
            editor,
            RoofAutomaticPurlinApplyResult.Failure(result, owner));

    private static string WriteResultToken(RoofAutomaticPurlinOwnerWriteResult result) =>
        result.ToString().ToLowerInvariant();

    private static string BoundaryIdentityToken(RoofBoundaryIdentityPreviewSource source) =>
        source switch
        {
            RoofBoundaryIdentityPreviewSource.Persisted => "persisted",
            RoofBoundaryIdentityPreviewSource.Ephemeral => "ephemeral",
            _ => "blocked",
        };

    private static IntPtr TryGetAutoCadMainWindowHandle()
    {
        try
        {
            return AcApplication.MainWindow?.Handle ?? IntPtr.Zero;
        }
        catch (System.Exception)
        {
            return IntPtr.Zero;
        }
    }

    private sealed record AutomaticPurlinProductionSnapshot(
        ObjectId OwnerId,
        string OwnerReference,
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityPreviewResolution BoundaryResolution,
        RoofAutomaticPurlinLayout Layout,
        bool LayoutExists,
        RoofRelativeElevationDatum? Datum,
        bool DatumExists,
        double SourceElevation,
        int ExistingAutomaticPurlinCount);
}
