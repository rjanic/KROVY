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
using Autodesk.AutoCAD.Geometry;
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
        if (!TrySelectAuthoritativeRoof(document, culture, out var selectedOwnerId))
        {
            return;
        }

        RoofTransientPreviewSession? previewSession = null;
        AutomaticPurlinDialogWindow? window = null;
        string? lastPreviewDiagnostic = null;
        try
        {
            var snapshot = ReadSnapshot(document, selectedOwnerId, out var snapshotFailure);
            if (snapshot is null)
            {
                editor.WriteMessage(
                    "\n" + UiStrings.GetString("AutomaticPurlin_CommandHipRoofRequired", culture));
                WriteApplyFailure(editor, "-", snapshotFailure);
                return;
            }

            var defaultProfile = TimberElementDefaultProfileStore.Load();
            var layerProfile = ElementLayerProfileStore.Load();
            if (!TryResolveRafterDefaults(
                    document,
                    snapshot.OwnerId,
                    snapshot.OwnerReference,
                    defaultProfile,
                    out var rafterDefaults,
                    out var rafterSource,
                    out var rafterFailure))
            {
                editor.WriteMessage(
                    "\n" + UiStrings.GetString(
                        "AutomaticPurlin_RafterRecipeAmbiguous",
                        culture));
                WriteRafterSourceFailure(editor, snapshot.OwnerReference, rafterFailure);
                return;
            }

            var viewModel = new AutomaticPurlinDialogViewModel(
                snapshot.Geometry,
                snapshot.BoundaryResolution.Provenance,
                snapshot.Layout,
                snapshot.LayoutExists,
                snapshot.Datum,
                snapshot.DatumExists,
                TimberElementDefaults.For(TimberElementType.Purlin, defaultProfile),
                TimberElementDefaults.For(TimberElementType.WallPlate, defaultProfile),
                rafterDefaults,
                culture,
                AutomaticPurlinDialogMode.ProductionEdit,
                snapshot.ExistingAutomaticPurlinCount,
                snapshot.StoredDatumLoadError,
                rafterSource);
            if (snapshot.StoredDatumLoadError ==
                RoofRelativeElevationDatumError.InconsistentSourceEaveLocalZ)
            {
                editor.WriteMessage(
                    "\nROOF_RELATIVE_DATUM" +
                    $" owner={snapshot.OwnerReference}" +
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
                var wallPlateCount = plan.Items.Count(item =>
                    item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
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
                    $" wallPlate={wallPlateCount.ToString(CultureInfo.InvariantCulture)}" +
                    $" ridge={ridgeCount.ToString(CultureInfo.InvariantCulture)}" +
                    $" intermediate={intermediateCount.ToString(CultureInfo.InvariantCulture)}" +
                    $" total={plan.Items.Count.ToString(CultureInfo.InvariantCulture)}" +
                    $" rafterW={viewModel.RafterWidthMm.ToString("0.###", CultureInfo.InvariantCulture)}" +
                    $" rafterH={viewModel.RafterHeightMm.ToString("0.###", CultureInfo.InvariantCulture)}" +
                    $" result=ok");
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

                SettingsWindowOwner.TryAssign(window, TryGetAutoCadMainWindowHandle());
                AutomaticPurlinDialogWindow activeWindow = window;

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
                        layerProfile,
                        args.RafterHeightMm);
                    if (!result.IsSuccess)
                    {
                        WriteApplySummary(editor, result);
                        editor.WriteMessage(
                            "\n" + UiStrings.GetString("AutomaticPurlin_ApplyFailed", culture));
                        activeWindow.CompleteFailedApply();
                        return;
                    }

                    WriteOwnerWriteDiagnostics(editor, result);
                    WriteDatumDiagnostics(editor, args.Datum);
                    WriteLayoutStateDiagnostics(editor, args.Layout);
                    WriteApplySummary(editor, result);
                    WriteMemberDiagnostics(editor, args.Layout, result);
                    editor.WriteMessage(
                        "\n" + UiStrings.GetString("AutomaticPurlin_ApplySucceeded", culture));
                    activeWindow.CompleteSuccessfulApply();
                }

                void CloseForDocumentDestruction(object sender, DocumentCollectionEventArgs args)
                {
                    if (!ReferenceEquals(args.Document, document) || activeWindow.IsClosed)
                    {
                        return;
                    }

                    _ = activeWindow.Dispatcher.BeginInvoke(new Action(activeWindow.Close));
                }

                activeWindow.PreviewRequested += RefreshPreview;
                activeWindow.ApplyRequested += ApplyRequested;
                AcApplication.DocumentManager.DocumentToBeDestroyed += CloseForDocumentDestruction;
                var suspendedForCadPreview = false;
                var suspendedForRafterPick = false;
                var suspendedForManualDialogRafterPick = false;
                try
                {
                    _ = AcApplication.ShowModalWindow(activeWindow);
                    suspendedForCadPreview = activeWindow.IsSuspendedForCadPreview;
                    suspendedForRafterPick = activeWindow.IsSuspendedForRafterPick;
                    suspendedForManualDialogRafterPick =
                        activeWindow.IsSuspendedForManualDialogRafterPick;
                    if (suspendedForCadPreview ||
                        suspendedForRafterPick ||
                        suspendedForManualDialogRafterPick)
                    {
                        restoreBounds = activeWindow.SavedRestoreBounds;
                    }

                    if (suspendedForCadPreview)
                    {
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
                    else if (suspendedForManualDialogRafterPick)
                    {
                        TryPickRafterDimensionsForManualDialog(
                            document,
                            culture,
                            snapshot.OwnerReference,
                            viewModel);
                    }
                    else if (suspendedForRafterPick)
                    {
                        TryPickRafterDimensions(
                            document,
                            culture,
                            snapshot.OwnerReference,
                            viewModel);
                    }
                }
                finally
                {
                    activeWindow.PreviewRequested -= RefreshPreview;
                    activeWindow.ApplyRequested -= ApplyRequested;
                    AcApplication.DocumentManager.DocumentToBeDestroyed -= CloseForDocumentDestruction;
                }

                if (!suspendedForCadPreview &&
                    !suspendedForRafterPick &&
                    !suspendedForManualDialogRafterPick)
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

    private static bool TrySelectAuthoritativeRoof(
        Document document,
        CultureInfo culture,
        out ObjectId ownerId)
    {
        ownerId = ObjectId.Null;
        var editor = document.Editor;
        while (true)
        {
            var selectionOptions = new PromptEntityOptions(
                "\n" + UiStrings.GetString("AutomaticPurlin_CommandSelectRoof", culture));
            selectionOptions.SetRejectMessage(
                "\n" + UiStrings.GetString("AutomaticPurlin_CommandInvalidSelection", culture));
            var selection = editor.GetEntity(selectionOptions);
            if (selection.Status != PromptStatus.OK)
            {
                return false;
            }

            using var transaction = document.Database.TransactionManager.StartOpenCloseTransaction();
            var resolution = RoofOwnerSelectionResolver.Resolve(
                document.Database,
                transaction,
                selection.ObjectId);
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
                    editor.WriteMessage(
                        "\n" + UiStrings.GetString("AutomaticPurlin_CommandInvalidSelection", culture));
                }

                continue;
            }

            ownerId = resolution.OwnerId;
            return true;
        }
    }

    private static void TryPickRafterDimensions(
        Document document,
        CultureInfo culture,
        string expectedOwnerReference,
        AutomaticPurlinDialogViewModel viewModel)
    {
        if (!TryPromptRafterDimensions(
                document,
                culture,
                expectedOwnerReference,
                viewModel,
                out var selected))
        {
            return;
        }

        // Other-roof / ownership-unknown picks are manual W×H copies only.
        // Never adopt them as SelectedRafter or overwrite current-roof host actual.
        if (RoofAutomaticPurlinSelectedRafterResolver.IsCurrentRoofGeneratedRafter(
                selected,
                expectedOwnerReference))
        {
            if (!viewModel.TryApplySelectedRafterDimensions(selected.WidthMm, selected.HeightMm))
            {
                document.Editor.WriteMessage(
                    "\n" + UiStrings.GetString("AutomaticPurlin_SelectRafterInvalid", culture));
            }

            return;
        }

        if (!viewModel.TryApplyManualRafterDimensions(selected.WidthMm, selected.HeightMm))
        {
            document.Editor.WriteMessage(
                "\n" + UiStrings.GetString("AutomaticPurlin_SelectRafterInvalid", culture));
        }
    }

    /// <summary>
    /// CAD pick from the nested manual dialog: populate reopen seeds only.
    /// Current-roof generated rafters may adopt SelectedRafter on Confirm;
    /// external / ownership-unknown rafters copy W×H as manual draft only.
    /// </summary>
    private static void TryPickRafterDimensionsForManualDialog(
        Document document,
        CultureInfo culture,
        string expectedOwnerReference,
        AutomaticPurlinDialogViewModel viewModel)
    {
        if (!TryPromptRafterDimensions(
                document,
                culture,
                expectedOwnerReference,
                viewModel,
                out var selected))
        {
            viewModel.CompleteManualDialogCadPick(
                success: false,
                pickedWidthMm: null,
                pickedHeightMm: null,
                isCurrentRoofGeneratedRafter: false);
            return;
        }

        var isCurrentRoof = RoofAutomaticPurlinSelectedRafterResolver.IsCurrentRoofGeneratedRafter(
            selected,
            expectedOwnerReference);
        viewModel.CompleteManualDialogCadPick(
            success: true,
            selected.WidthMm,
            selected.HeightMm,
            isCurrentRoof);
    }

    private static bool TryPromptRafterDimensions(
        Document document,
        CultureInfo culture,
        string expectedOwnerReference,
        AutomaticPurlinDialogViewModel viewModel,
        out RoofAutomaticPurlinSelectedRafterResolver.SelectedRafterDimensions selected)
    {
        selected = default;
        var editor = document.Editor;
        var options = new PromptEntityOptions(
            "\n" + UiStrings.GetString("AutomaticPurlin_SelectRafterPrompt", culture));
        options.SetRejectMessage(
            "\n" + UiStrings.GetString("AutomaticPurlin_SelectRafterInvalid", culture));
        var selection = editor.GetEntity(options);
        if (selection.Status != PromptStatus.OK)
        {
            editor.WriteMessage(
                "\n" + UiStrings.GetString("AutomaticPurlin_SelectRafterCancelled", culture));
            return false;
        }

        using var transaction = document.Database.TransactionManager.StartOpenCloseTransaction();
        if (!RoofAutomaticPurlinSelectedRafterResolver.TryResolveRafterDimensions(
                document.Database,
                transaction,
                selection.ObjectId,
                out selected,
                out _))
        {
            editor.WriteMessage(
                "\n" + UiStrings.GetString("AutomaticPurlin_SelectRafterInvalid", culture));
            return false;
        }

        RoofAutomaticPurlinSelectedRafterResolver.WriteSelectionDiagnostic(
            editor,
            expectedOwnerReference,
            selected,
            viewModel.InitialRafterWidthMm,
            viewModel.InitialRafterHeightMm);
        return true;
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
        RoofRelativeElevationDatumError? storedDatumLoadError = null;
        if (datumRead.Exists && datumRead.Data is null)
        {
            if (datumRead.Error == RoofRelativeElevationDatumError.InconsistentSourceEaveLocalZ)
            {
                // Fail closed for planning/regen, but allow the dialog so the user can
                // explicitly choose a consistent reference and Apply (XData untouched until then).
                storedDatumLoadError = datumRead.Error;
            }
            else
            {
                failure = "relative-elevation-datum-" + datumRead.Error;
                return null;
            }
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
            existingState.ExistingCount,
            storedDatumLoadError);
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

    private static void WriteDatumDiagnostics(
        Editor editor,
        RoofRelativeElevationDatum datum)
    {
#if DEBUG
        editor.WriteMessage(
            "\nROOF_RELATIVE_DATUM" +
            $" referenceType={FormatReferenceType(datum.ReferenceKind)}" +
            $" referenceRelativeMm={datum.ReferenceRelativeElevationMm.ToString("0.###", CultureInfo.InvariantCulture)}" +
            $" referenceLocalZMm={datum.ReferenceLocalZMm.ToString("0.###", CultureInfo.InvariantCulture)}");
#else
        _ = editor;
        _ = datum;
#endif
    }

    private static void WriteLayoutStateDiagnostics(
        Editor editor,
        RoofAutomaticPurlinLayout layout)
    {
#if DEBUG
        editor.WriteMessage(
            "\nROOF_PURLIN_LAYOUT" +
            $" wallPlateEnabled={(layout.WallPlateEnabled ? "1" : "0")}" +
            $" wallPlatePlacementMode={RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(layout).PlacementMode}" +
            $" wallPlatePlacementValueMm={RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(layout).PlacementValueMm.ToString("0.###", CultureInfo.InvariantCulture)}" +
            $" wallPlateLowerEdgeMm={layout.WallPlateLowerEdgeHeightMm.ToString("0.###", CultureInfo.InvariantCulture)}" +
            $" ridgeEnabled={(layout.RidgeEnabled ? "1" : "0")}" +
            $" intermediate={layout.IntermediateItems.Count.ToString(CultureInfo.InvariantCulture)}");
#else
        _ = editor;
        _ = layout;
#endif
    }

    private static void WriteMemberDiagnostics(
        Editor editor,
        RoofAutomaticPurlinLayout layout,
        RoofAutomaticPurlinApplyResult result)
    {
#if DEBUG
        foreach (var member in result.Materialization.Members)
        {
            if (member.Role != RoofAutomaticPurlinGeneratorRole.WallPlate &&
                member.Role != RoofAutomaticPurlinGeneratorRole.Intermediate)
            {
                continue;
            }

            var elevation = member.ElevationProfile;
            if (member.Role == RoofAutomaticPurlinGeneratorRole.WallPlate)
            {
                var wallPlatePlacement =
                    RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(layout);
                editor.WriteMessage(
                    "\nROOF_PURLIN_MEMBER" +
                    $" owner={result.OwnerReference}" +
                    $" role=WallPlate" +
                    $" key={member.GeneratedKey}" +
                    $" handle={member.Handle}" +
                    $" elementId={member.ElementId}" +
                    $" placementMode={wallPlatePlacement.PlacementMode}" +
                    $" placementValueMm={wallPlatePlacement.PlacementValueMm.ToString("0.###", CultureInfo.InvariantCulture)}" +
                    $" configuredLowerEdgeMm={layout.WallPlateLowerEdgeHeightMm.ToString("0.###", CultureInfo.InvariantCulture)}" +
                    $" seatingDepth={FormatNullable(elevation?.SeatingDepthMm)}" +
                    $" width={member.WidthMm.ToString("0.###", CultureInfo.InvariantCulture)}" +
                    $" height={member.HeightMm.ToString("0.###", CultureInfo.InvariantCulture)}" +
                    $" start=({FormatPoint(member.Start)})" +
                    $" end=({FormatPoint(member.End)})" +
                    $" bottomLocalZMm={FormatNullable(elevation?.BottomLocalZMm)}" +
                    $" bottomRelative={FormatRelative(elevation?.BottomRelativeElevationMm)}" +
                    $" centerRelative={FormatRelative(elevation?.CenterRelativeElevationMm)}" +
                    $" topRelative={FormatRelative(elevation?.TopRelativeElevationMm)}" +
                    $" result={member.Result}");
                continue;
            }

            var intermediate = layout.IntermediateItems.FirstOrDefault(item =>
                string.Equals(item.LayoutItemId, member.LayoutItemId, StringComparison.Ordinal));
            editor.WriteMessage(
                "\nROOF_PURLIN_MEMBER" +
                $" owner={result.OwnerReference}" +
                $" role=Intermediate" +
                $" key={member.GeneratedKey}" +
                $" handle={member.Handle}" +
                $" elementId={member.ElementId}" +
                $" layoutItemId={member.LayoutItemId}" +
                $" configuredBottomAboveReferenceMm={(intermediate is null ? "-" : intermediate.PlacementValueMm.ToString("0.###", CultureInfo.InvariantCulture))}" +
                $" actualBottomLocalZMm={FormatNullable(elevation?.BottomLocalZMm)}" +
                $" bottomRelative={FormatRelative(elevation?.BottomRelativeElevationMm)}" +
                $" centerRelative={FormatRelative(elevation?.CenterRelativeElevationMm)}" +
                $" topRelative={FormatRelative(elevation?.TopRelativeElevationMm)}" +
                $" result={member.Result}");
        }
#else
        _ = editor;
        _ = layout;
        _ = result;
#endif
    }

#if DEBUG
    private static string FormatReferenceType(RoofRelativeElevationReferenceKind kind) =>
        kind switch
        {
            RoofRelativeElevationReferenceKind.WallPlateBottom => "WallPlateLowerEdge",
            RoofRelativeElevationReferenceKind.SourceEavePlane => "SourceEavePlane",
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane => "ExplicitLocalPlane",
            _ => kind.ToString(),
        };

    private static string FormatPoint(Point3d point) =>
        string.Join(
            ",",
            point.X.ToString("0.###", CultureInfo.InvariantCulture),
            point.Y.ToString("0.###", CultureInfo.InvariantCulture),
            point.Z.ToString("0.###", CultureInfo.InvariantCulture));

    private static string FormatRelative(double? relativeElevationMm) =>
        relativeElevationMm is null
            ? "-"
            : RoofRelativeElevationDatumRules.FormatMetres(relativeElevationMm.Value);

    private static string FormatNullable(double? value) =>
        value is null
            ? "-"
            : value.Value.ToString("0.###", CultureInfo.InvariantCulture);
#endif

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
            $" wallPlate={materialization.WallPlate.ToString(CultureInfo.InvariantCulture)}" +
            $" ridge={materialization.Ridge.ToString(CultureInfo.InvariantCulture)}" +
            $" intermediate={materialization.Intermediate.ToString(CultureInfo.InvariantCulture)}" +
            $" created={materialization.Created.ToString(CultureInfo.InvariantCulture)}" +
            $" existing={materialization.Existing.ToString(CultureInfo.InvariantCulture)}" +
            $" updated={materialization.Updated.ToString(CultureInfo.InvariantCulture)}" +
            $" staleRemoved={materialization.StaleRemoved.ToString(CultureInfo.InvariantCulture)}" +
            $" groupCanonical={(materialization.GroupCanonical ? "1" : "0")}" +
            $" result={result.Result}");
    }

    private static void WriteRafterSourceFailure(Editor editor, string owner, string result)
    {
        // Do not WriteMessage — AutoCAD tips the last editor line over the WPF dialog.
        _ = editor;
#if DEBUG
        System.Diagnostics.Debug.WriteLine(
            "ROOF_PURLIN_RAFTER_SOURCE" +
            $" owner={owner}" +
            $" result={result}");
#else
        _ = owner;
        _ = result;
#endif
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

    private static bool TryResolveRafterDefaults(
        Document document,
        ObjectId ownerId,
        string ownerReference,
        TimberElementDefaultProfile defaultProfile,
        out TimberElementData rafterDefaults,
        out AutomaticPurlinRafterDimensionSource rafterSource,
        out string failureReason)
    {
        rafterDefaults = default!;
        rafterSource = AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed;
        failureReason = string.Empty;
        using var transaction = document.Database.TransactionManager.StartOpenCloseTransaction();
        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                document.Database) ||
            owner is null)
        {
            failureReason = "authoritative-roof-required";
            return false;
        }

        if (!RoofAutomaticPurlinRafterDimensionsResolver.TryResolveForOwner(
                document.Database,
                transaction,
                owner,
                defaultProfile,
                out var resolution,
                out failureReason,
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
        _ = ownerReference;
        return true;
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
        int ExistingAutomaticPurlinCount,
        RoofRelativeElevationDatumError? StoredDatumLoadError = null);
}
