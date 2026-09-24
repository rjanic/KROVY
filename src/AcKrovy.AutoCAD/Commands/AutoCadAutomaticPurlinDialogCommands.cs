#if DEBUG
using System.Globalization;
using System.Windows;
using AcKrovy.AutoCAD.Infrastructure;
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
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>DEBUG-only, DB-read-only Automatic Purlin layout and transient preview.</summary>
public sealed class AutoCadAutomaticPurlinDialogCommands
{
    internal const string CommandName = "AK_DEBUG_PURLIN_DIALOG";

    [CommandMethod(CommandName, CommandFlags.Modal | CommandFlags.Redraw | CommandFlags.NoUndoMarker)]
    public void ShowDialog()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

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

        var dbmodBefore = ReadDbmod();
        RoofTransientPreviewSession? previewSession = null;
        AutomaticPurlinDialogWindow? window = null;
        string? lastPreviewDiagnostic = null;
        try
        {
            var snapshot = ReadSnapshot(document, selection.ObjectId);
            if (snapshot is null)
            {
                editor.WriteMessage(
                    "\n" + UiStrings.GetString("AutomaticPurlin_CommandHipRoofRequired", culture));
                return;
            }

            var defaultProfile = TimberElementDefaultProfileStore.Load();
            if (!TryResolveRafterDefaults(
                    document,
                    selection.ObjectId,
                    snapshot.OwnerHandle,
                    defaultProfile,
                    out var rafterDefaults,
                    out var rafterSource))
            {
                editor.WriteMessage(
                    "\n" + UiStrings.GetString(
                        "AutomaticPurlin_RafterRecipeAmbiguous",
                        culture));
#if DEBUG
                System.Diagnostics.Debug.WriteLine(
                    "ROOF_PURLIN_RAFTER_SOURCE" +
                    $" owner={snapshot.OwnerHandle}" +
                    " result=rafter-recipe-ambiguous");
#endif
                return;
            }

            var viewModel = new AutomaticPurlinDialogViewModel(
                snapshot.Geometry,
                snapshot.BoundaryProvenance,
                snapshot.Layout,
                snapshot.LayoutExists,
                snapshot.Datum,
                snapshot.DatumExists,
                TimberElementDefaults.For(TimberElementType.Purlin, defaultProfile),
                TimberElementDefaults.For(TimberElementType.WallPlate, defaultProfile),
                rafterDefaults,
                culture,
                AutomaticPurlinDialogMode.ReadOnlyPreview,
                existingAutomaticPurlinCount: 0,
                snapshot.StoredDatumLoadError,
                rafterSource);
            if (snapshot.StoredDatumLoadError ==
                RoofRelativeElevationDatumError.InconsistentSourceEaveLocalZ)
            {
                editor.WriteMessage(
                    "\nROOF_RELATIVE_DATUM" +
                    $" owner={snapshot.OwnerHandle}" +
                    " result=invalid" +
                    " reason=InconsistentSourceEaveLocalZ");
                editor.WriteMessage(
                    "\n" + UiStrings.GetString(
                        "AutomaticPurlin_DatumInconsistentSourceEave",
                        culture));
            }
            var theme = SettingsUiPreferencesStore.Load().Theme;
            Rect? restoreBounds = null;

            void RefreshPreview(object? sender, EventArgs args)
            {
                previewSession?.Dispose();
                previewSession = null;

                if (!snapshot.BoundaryResolution.IsValid)
                {
                    WritePreviewDiagnostic(
                        $"ROOF_PURLIN_PREVIEW owner={snapshot.OwnerHandle}" +
                        $" result=blocked reason={snapshot.BoundaryResolution.Error}");
                    return;
                }

                if (!viewModel.TryGetPreviewPlan(out var plan) || plan is null)
                {
                    WritePreviewDiagnostic(
                        $"ROOF_PURLIN_PREVIEW owner={snapshot.OwnerHandle}" +
                        $" result=invalid reason={viewModel.PreviewDiagnosticReason}");
                    return;
                }

                var ridgeCount = plan.Items.Count(item =>
                    item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);
                var wallPlateCount = plan.Items.Count(item =>
                    item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
                var intermediateCount = plan.Items.Count(item =>
                    item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate);
                if (plan.Items.Count > 0)
                {
                    try
                    {
                        previewSession = RoofTransientPreviewSession.ShowAutomaticPurlins(
                            document,
                            plan,
                            snapshot.SourceElevation);
                    }
                    catch (System.Exception exception)
                    {
                        WritePreviewDiagnostic(
                            $"ROOF_PURLIN_PREVIEW owner={snapshot.OwnerHandle}" +
                            $" result=blocked reason=Transient{exception.GetType().Name}");
                        throw;
                    }
                }

                WritePreviewDiagnostic(
                    $"ROOF_PURLIN_PREVIEW owner={snapshot.OwnerHandle}" +
                    $" boundaryIdentity={BoundaryIdentityToken(snapshot.BoundaryResolution.Source)}" +
                    $" wallPlate={wallPlateCount.ToString(CultureInfo.InvariantCulture)}" +
                    $" ridge={ridgeCount.ToString(CultureInfo.InvariantCulture)}" +
                    $" intermediate={intermediateCount.ToString(CultureInfo.InvariantCulture)}" +
                    $" total={plan.Items.Count.ToString(CultureInfo.InvariantCulture)} result=ok");
            }

            void WritePreviewDiagnostic(string message)
            {
                if (string.Equals(lastPreviewDiagnostic, message, StringComparison.Ordinal))
                {
                    return;
                }

                lastPreviewDiagnostic = message;
                // Debug only — Editor.WriteMessage tips over the open WPF dialog.
                System.Diagnostics.Debug.WriteLine(message);
            }

            while (true)
            {
                window = new AutomaticPurlinDialogWindow(viewModel, theme);
                if (restoreBounds is { } bounds)
                {
                    window.ApplyRestoreBounds(bounds);
                }

                SettingsWindowOwner.TryAssign(
                    window,
                    TryGetAutoCadMainWindowHandle());
                AutomaticPurlinDialogWindow activeWindow = window;

                void CloseForDocumentDestruction(object sender, DocumentCollectionEventArgs args)
                {
                    if (!ReferenceEquals(args.Document, document) || activeWindow.IsClosed)
                    {
                        return;
                    }

                    _ = activeWindow.Dispatcher.BeginInvoke(new Action(activeWindow.Close));
                }

                activeWindow.PreviewRequested += RefreshPreview;
                AcApplication.DocumentManager.DocumentToBeDestroyed += CloseForDocumentDestruction;
                var suspendedForCadPreview = false;
                try
                {
                    _ = AcApplication.ShowModalWindow(activeWindow);
                    suspendedForCadPreview = activeWindow.IsSuspendedForCadPreview;
                    if (suspendedForCadPreview)
                    {
                        restoreBounds = activeWindow.SavedRestoreBounds;
                        try
                        {
                            RefreshPreview(activeWindow, EventArgs.Empty);
                            _ = editor.GetString(
                                "\n" + UiStrings.GetString(
                                    "AutomaticPurlin_PreviewReturnPrompt",
                                    culture));
                        }
                        catch (System.Exception)
                        {
                            // finally-safe: always return to the same ViewModel dialog.
                        }
                    }
                }
                finally
                {
                    activeWindow.PreviewRequested -= RefreshPreview;
                    AcApplication.DocumentManager.DocumentToBeDestroyed -= CloseForDocumentDestruction;
                }

                if (!suspendedForCadPreview)
                {
                    break;
                }
            }
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                "\n" + string.Format(
                    culture,
                    UiStrings.GetString("AutomaticPurlin_CommandFailedFormat", culture),
                    exception.GetType().Name));
        }
        finally
        {
            previewSession?.Dispose();
            if (window is { IsClosed: false })
            {
                window.Close();
            }

            var dbmodAfter = ReadDbmod();
            editor.WriteMessage(
                "\nROOF_PURLIN_DIALOG_READONLY" +
                $" dbmodBefore={dbmodBefore.ToString(CultureInfo.InvariantCulture)}" +
                $" dbmodAfter={dbmodAfter.ToString(CultureInfo.InvariantCulture)}" +
                $" unchanged={(dbmodBefore == dbmodAfter ? "1" : "0")}");
        }
    }

    private static AutomaticPurlinDialogSnapshot? ReadSnapshot(
        Document document,
        ObjectId ownerId)
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
            return null;
        }

        var storedDefinition = RoofDefinitionStore.Read(owner);
        if (storedDefinition.Data is null)
        {
            return null;
        }

        var sourceInput = RoofPolylineExtractor.Extract(owner);
        var validated = RoofFootprintValidator.Validate(sourceInput);
        if (!validated.IsValid || validated.Footprint is null)
        {
            return null;
        }

        var restored = RoofDefinitionPersistence.Restore(
            sourceInput,
            validated.Footprint,
            storedDefinition.Data);
        if (!restored.IsValid || restored.Geometry is not HipRoofGeometry geometry)
        {
            return null;
        }

        var layoutRead = RoofPurlinLayoutStore.Read(owner);
        if (layoutRead.Exists && layoutRead.Data is null)
        {
            return null;
        }

        var datumRead = RoofRelativeElevationDatumStore.Read(owner);
        RoofRelativeElevationDatumError? storedDatumLoadError = null;
        if (datumRead.Exists && datumRead.Data is null)
        {
            if (datumRead.Error == RoofRelativeElevationDatumError.InconsistentSourceEaveLocalZ)
            {
                storedDatumLoadError = datumRead.Error;
            }
            else
            {
                return null;
            }
        }

        var boundaryRead = RoofBoundaryIdentityStore.Read(owner);
        var boundaryResolution = RoofBoundaryIdentityPreviewResolver.Resolve(
            sourceInput,
            boundaryRead.Data,
            boundaryRead.Error);
        return new AutomaticPurlinDialogSnapshot(
            owner.Handle.ToString(),
            geometry,
            boundaryResolution,
            layoutRead.Data ?? RoofAutomaticPurlinLayout.Empty,
            layoutRead.Exists,
            datumRead.Data,
            datumRead.Exists,
            RoofPolylineExtractor.GetSourceElevation(owner),
            storedDatumLoadError);
    }

    private static bool TryResolveRafterDefaults(
        Document document,
        ObjectId ownerId,
        string ownerHandle,
        TimberElementDefaultProfile defaultProfile,
        out TimberElementData rafterDefaults,
        out AutomaticPurlinRafterDimensionSource rafterSource)
    {
        rafterDefaults = default!;
        rafterSource = AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed;
        using var transaction = document.Database.TransactionManager.StartOpenCloseTransaction();
        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                document.Database) ||
            owner is null)
        {
            return false;
        }

        if (!RoofAutomaticPurlinRafterDimensionsResolver.TryResolve(
                document.Database,
                transaction,
                ownerHandle,
                defaultProfile,
                out var resolution,
                out _,
                document.Editor))
        {
            return false;
        }

        rafterDefaults = resolution.ApplyToProfileDefaults(
            TimberElementDefaults.For(TimberElementType.Rafter, defaultProfile));
        rafterSource = resolution.Source ==
            RoofAutomaticPurlinRafterDimensionsResolver.SourceKind.RecoveredRoofRecipe
                ? AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe
                : AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed;
        return true;
    }

    private static string BoundaryIdentityToken(RoofBoundaryIdentityPreviewSource source) =>
        source switch
        {
            RoofBoundaryIdentityPreviewSource.Persisted => "persisted",
            RoofBoundaryIdentityPreviewSource.Ephemeral => "ephemeral",
            _ => "blocked",
        };

    private static int ReadDbmod()
    {
        try
        {
            return Convert.ToInt32(
                AcApplication.GetSystemVariable("DBMOD"),
                CultureInfo.InvariantCulture);
        }
        catch (System.Exception)
        {
            return -1;
        }
    }

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

    private sealed record AutomaticPurlinDialogSnapshot(
        string OwnerHandle,
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityPreviewResolution BoundaryResolution,
        RoofAutomaticPurlinLayout Layout,
        bool LayoutExists,
        RoofRelativeElevationDatum? Datum,
        bool DatumExists,
        double SourceElevation,
        RoofRelativeElevationDatumError? StoredDatumLoadError = null)
    {
        public RoofBoundaryIdentityProvenanceResult? BoundaryProvenance =>
            BoundaryResolution.Provenance;
    }
}
#endif
