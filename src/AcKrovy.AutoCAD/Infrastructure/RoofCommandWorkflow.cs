using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using AcKrovy.AutoCAD.UI;
using AcKrovy.AutoCAD.Settings;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Validated roof definition, transient preview and explicit permanent-display lifecycle.</summary>
internal static class RoofCommandWorkflow
{
    private static readonly RoofNotificationDescriptor InvalidObjectNotification = new(
        "Command_Roof_InvalidObjectNotificationTitle",
        "Command_Roof_InvalidObjectNotificationBody");
    private static readonly RoofNotificationDescriptor OpenLoopNotification = new(
        "Command_Roof_OpenLoopNotificationTitle",
        "Command_Roof_OpenLoopNotificationBody");
    private static readonly RoofNotificationDescriptor InvalidFootprintNotification = new(
        "Command_Roof_InvalidFootprintNotificationTitle",
        "Command_Roof_InvalidFootprintNotificationBody");
    private static readonly RoofNotificationDescriptor UnsupportedFootprintNotification = new(
        "Command_Roof_UnsupportedFootprintNotificationTitle",
        "Command_Roof_UnsupportedFootprintNotificationBody");
    private static readonly RoofNotificationDescriptor InvalidDirectionNotification = new(
        "Command_Roof_InvalidDirectionNotificationTitle",
        "Command_Roof_InvalidDirectionNotificationBody");
    private static readonly RoofNotificationDescriptor InvalidSlopeNotification = new(
        "Command_Roof_InvalidSlopeNotificationTitle",
        "Command_Roof_InvalidSlopeNotificationBody");

    public static void Run(Document document, RoofKind requestedKind = RoofKind.SimpleGable)
    {
        ArgumentNullException.ThrowIfNull(document);
        var editor = document.Editor;

        while (true)
        {
            var prompt = new PromptEntityOptions(
                UiStrings.GetString("Command_Roof_Prompt"));
            var selected = editor.GetEntity(prompt);
            if (selected.Status != PromptStatus.OK)
            {
                return;
            }

            RoofValidationResult validation;
            RoofFootprintInput sourceInput;
            double sourceElevation;
            string sourceReference;
            ObjectId ownerId;
            RoofDefinitionStoreReadResult storedDefinition;
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                var resolution = RoofOwnerSelectionResolver.Resolve(
                    document.Database,
                    transaction,
                    selected.ObjectId);
                if (!resolution.IsResolved)
                {
                    if (TryGetSelectionNotification(resolution.Error, out var notification))
                    {
                        ShowNotification(notification);
                    }
                    else
                    {
                        editor.WriteMessage(GetSelectionMessage(resolution.Error));
                    }
                    continue;
                }
                ownerId = resolution.OwnerId;
                if (transaction.GetObject(ownerId, OpenMode.ForRead) is not Polyline polyline)
                {
                    editor.WriteMessage(UiStrings.GetString("Command_Roof_SelectionOrphan"));
                    continue;
                }

                sourceInput = RoofPolylineExtractor.Extract(polyline);
                validation = RoofFootprintValidator.Validate(sourceInput);
                sourceElevation = RoofPolylineExtractor.GetSourceElevation(polyline);
                sourceReference = polyline.Handle.ToString();
                storedDefinition = RoofDefinitionStore.Read(polyline);
            }

            if (!validation.IsValid || validation.Footprint is null)
            {
                if (TryGetValidationNotification(validation.Error, out var notification))
                {
                    ShowNotification(notification);
                }
                else
                {
                    editor.WriteMessage(GetValidationMessage(validation.Error));
                }

                continue;
            }

            if (storedDefinition.Exists)
            {
                if (storedDefinition.Data is null)
                {
                    editor.WriteMessage(GetStoredDefinitionMessage(storedDefinition.Error));
                    continue;
                }

                var restored = RoofDefinitionPersistence.Restore(
                    sourceInput,
                    validation.Footprint,
                    storedDefinition.Data);
                if (!restored.IsValid || restored.Geometry is null)
                {
                    editor.WriteMessage(UiStrings.GetString(
                        restored.Error == RoofDefinitionRestoreError.StaleFootprint
                            ? "Command_Roof_PersistedStale"
                            : "Command_Roof_PersistedInvalid"));
                    continue;
                }

                editor.SetImpliedSelection([ownerId]);
                editor.WriteMessage(UiStrings.GetString("Command_Roof_PersistedLoaded"));
                ShowPreview(document, restored.Geometry, sourceElevation);

#if DEBUG
                if (restored.Geometry is HipRoofGeometry hipGeometry)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AK_ROOF_HIP] reload success token=Hip " +
                        $"slope={hipGeometry.PrimarySlopeDegrees:R} " +
                        $"vertices={validation.Footprint.Vertices.Count} " +
                        $"hip={hipGeometry.Topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Hip)} " +
                        $"ridge={hipGeometry.Topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Ridge)} " +
                        $"valley={hipGeometry.Topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Valley)}");
                }
#endif

                var edges = RoofWireframe.Create(restored.Geometry, sourceElevation);
                var signature = RoofWireframe.BuildGenerationSignature(edges);
                var display = InspectDisplay(
                    document.Database,
                    ownerId,
                    sourceReference,
                    edges,
                    signature);
                var lifecycle = display.Lifecycle;
                if (lifecycle == RoofDisplayLifecycleKind.Current)
                {
                    editor.WriteMessage(UiStrings.GetString("Command_Roof_DisplayCurrent"));
                    ClearCompletedWorkflowSelection(editor);
                    return;
                }

                if (lifecycle == RoofDisplayLifecycleKind.GroupMissingRehydratable)
                {
                    editor.WriteMessage(UiStrings.GetString("Command_Roof_GroupMissing"));
                    if (!ConfirmYesNo(editor, "Command_Roof_GroupRepairPrompt"))
                    {
                        return;
                    }
                    if (TryRehydrateGroup(document, ownerId, out var groupFailureKey))
                    {
                        editor.WriteMessage(UiStrings.GetString("Command_Roof_GroupRepaired"));
                        ClearCompletedWorkflowSelection(editor);
                    }
                    else
                    {
                        editor.WriteMessage(UiStrings.GetString(groupFailureKey));
                    }
                    return;
                }
                if (lifecycle == RoofDisplayLifecycleKind.UnsupportedFutureSchema)
                {
                    editor.WriteMessage(UiStrings.GetString("Command_Roof_DisplayFutureSchema"));
                    return;
                }

                var isMissing = lifecycle == RoofDisplayLifecycleKind.MissingDisplay;
                editor.WriteMessage(UiStrings.GetString(isMissing
                    ? "Command_Roof_DisplayMissing"
                    : "Command_Roof_DisplayStale"));
                if (!ConfirmDisplayPersistence(editor, isMissing))
                {
                    return;
                }

                if (TryRebuildDisplay(document, ownerId, out var displayFailureKey))
                {
                    editor.WriteMessage(UiStrings.GetString(isMissing
                        ? "Command_Roof_DisplayCreated"
                        : "Command_Roof_DisplayUpdated"));
                    ClearCompletedWorkflowSelection(editor);
                }
                else
                {
                    editor.WriteMessage(UiStrings.GetString(displayFailureKey));
                }
                return;
            }

            RunCreationDialog(
                document,
                ownerId,
                sourceInput,
                validation,
                sourceElevation,
                requestedKind);
            return;
        }
    }

    private static void RunCreationDialog(
        Document document,
        ObjectId ownerId,
        RoofFootprintInput sourceInput,
        RoofValidationResult validation,
        double sourceElevation,
        RoofKind initialKind)
    {
        if (initialKind == RoofKind.Hip)
        {
            RunHipPreviewDialog(
                document,
                ownerId,
                sourceInput,
                validation,
                sourceElevation);
            return;
        }

        var footprint = validation.Footprint!;
        var viewModel = new GableRoofGeometryViewModel(footprint, initialKind);
        var dialog = new GableRoofGeometryWindow(
            viewModel,
            SettingsUiPreferencesStore.Load().Theme);
        SettingsWindowOwner.TryAssign(dialog, TryGetAutoCadMainWindowHandle());
        try
        {
            while (!dialog.IsClosed)
            {
                dialog.PrepareForInteraction();
                _ = AcApp.ShowModalWindow(dialog);
                switch (dialog.RequestedAction)
                {
                    case GableRoofGeometryDialogAction.PickRidgeDirection:
                        if (TryPromptOrientationDirection(
                                document.Editor,
                                viewModel.SelectedKind,
                                out var direction))
                        {
                            viewModel.SetRidgeDirection(direction);
                        }
                        continue;

                    case GableRoofGeometryDialogAction.Preview:
                        if (viewModel.TryGetRoofGeometry(out var previewGeometry) &&
                            previewGeometry is not null)
                        {
                            document.Editor.SetImpliedSelection([ownerId]);
                            document.Editor.UpdateScreen();
                            ShowPreview(document, previewGeometry, sourceElevation);
                        }
                        continue;

                    case GableRoofGeometryDialogAction.Apply:
                        if (!viewModel.TryGetRoofGeometry(out var geometry) || geometry is null)
                        {
                            continue;
                        }

                        document.Editor.WriteMessage(UiStrings.Format(
                            UiStrings.GetString("Command_Roof_AcceptedFormat"),
                            footprint.Vertices.Count,
                            footprint.AreaMm2 / 1_000_000d,
                            GetOrientationText(validation.SourceOrientation)));
                        var data = RoofDefinitionPersistence.Create(sourceInput, footprint, geometry);
                        if (TryPersist(document, ownerId, data, out var failureMessageKey))
                        {
                            document.Editor.WriteMessage(UiStrings.GetString(
                                "Command_Roof_PersistedAndDisplaySaved"));
                            ClearCompletedWorkflowSelection(document.Editor);
                        }
                        else
                        {
                            document.Editor.WriteMessage(UiStrings.GetString(failureMessageKey));
                        }
                        return;

                    default:
                        return;
                }
            }
        }
        finally
        {
            if (!dialog.IsClosed)
            {
                dialog.Close();
            }
        }
    }

    /// <summary>
    /// Hip create boundary: preview stays read-only and Apply enters the shared
    /// transactional definition store without creating legacy permanent display.
    /// </summary>
    private static void RunHipPreviewDialog(
        Document document,
        ObjectId ownerId,
        RoofFootprintInput sourceInput,
        RoofValidationResult validation,
        double sourceElevation)
    {
        var footprint = validation.Footprint!;
        var viewModel = new HipRoofPreviewViewModel(footprint);
        var dialog = new HipRoofPreviewWindow(
            viewModel,
            SettingsUiPreferencesStore.Load().Theme);
        SettingsWindowOwner.TryAssign(dialog, TryGetAutoCadMainWindowHandle());
        try
        {
            while (!dialog.IsClosed)
            {
                dialog.PrepareForInteraction();
                _ = AcApp.ShowModalWindow(dialog);
                switch (dialog.RequestedAction)
                {
                    case HipRoofPreviewDialogAction.Preview:
                        if (!viewModel.TryGetRoofGeometry(out var previewGeometry) ||
                            previewGeometry is null)
                        {
                            continue;
                        }

#if DEBUG
                        System.Diagnostics.Debug.WriteLine(
                            $"[AK_ROOF_HIP] kind={RoofKind.Hip} vertices={footprint.Vertices.Count} " +
                            $"nodes={previewGeometry.Topology.Nodes.Count} " +
                            $"hip={previewGeometry.Topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Hip)} " +
                            $"ridge={previewGeometry.Topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Ridge)} " +
                            $"valley={previewGeometry.Topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Valley)} " +
                            $"seam={previewGeometry.Topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.CoplanarSeam)}");
#endif
                        document.Editor.SetImpliedSelection([ownerId]);
                        document.Editor.UpdateScreen();
                        try
                        {
                            ShowPreview(
                                document,
                                previewGeometry,
                                sourceElevation);
                        }
                        finally
                        {
                            ClearCompletedWorkflowSelection(document.Editor);
                        }
                        continue;

                    case HipRoofPreviewDialogAction.Apply:
                        if (!viewModel.TryGetRoofGeometry(out var geometry) || geometry is null)
                        {
                            continue;
                        }

                        var data = RoofDefinitionPersistence.Create(
                            sourceInput,
                            footprint,
                            geometry);
#if DEBUG
                        System.Diagnostics.Debug.WriteLine(
                            $"[AK_ROOF_HIP] persistence begin kind={data.Kind} token=Hip " +
                            $"slope={data.SlopeDegrees:R} vertices={footprint.Vertices.Count}");
#endif
                        if (TryPersist(document, ownerId, data, out var failureMessageKey))
                        {
                            document.Editor.WriteMessage(UiStrings.GetString(
                                "Command_Roof_PersistedAndDisplaySaved"));
                            ClearCompletedWorkflowSelection(document.Editor);
#if DEBUG
                            System.Diagnostics.Debug.WriteLine(
                                "[AK_ROOF_HIP] persistence success token=Hip");
#endif
                        }
                        else
                        {
                            document.Editor.WriteMessage(UiStrings.GetString(failureMessageKey));
#if DEBUG
                            System.Diagnostics.Debug.WriteLine(
                                $"[AK_ROOF_HIP] persistence failure key={failureMessageKey}");
#endif
                        }
                        return;

                    default:
                        return;
                }
            }
        }
        finally
        {
            if (!dialog.IsClosed)
            {
                dialog.Close();
            }
        }
    }

    private static void ClearCompletedWorkflowSelection(Editor editor)
        => editor.SetImpliedSelection(Array.Empty<ObjectId>());

    private static void ShowPreview(
        Document document,
        IRoofGeometry geometry,
        double sourceElevation)
    {
        using (RoofTransientPreviewSession.Show(document, geometry, sourceElevation))
        {
            _ = document.Editor.GetString(new PromptStringOptions(
                UiStrings.GetString("Command_Roof_PreviewClosePrompt"))
            {
                AllowSpaces = false,
            });
        }
    }

    private static bool ConfirmDisplayPersistence(Editor editor, bool isMissing)
        => ConfirmYesNo(
            editor,
            isMissing
                ? "Command_Roof_DisplayCreatePrompt"
                : "Command_Roof_DisplayUpdatePrompt");

    private static bool ConfirmYesNo(Editor editor, string promptKey)
    {
        var uiCulture = AppLanguageService.CurrentUiCulture;
        var yes = UiStrings.GetString("Message_Yes", uiCulture);
        var no = UiStrings.GetString("Message_No", uiCulture);
        var options = new PromptKeywordOptions(
            UiStrings.GetString(promptKey, uiCulture))
        {
            AllowNone = true,
            AppendKeywordsToMessage = false,
        };
        options.Keywords.Add("Yes", yes, yes);
        if (RenumberConfirmationRules.SupportsSlovakAsciiYesAlias(uiCulture))
        {
            options.Keywords.Add(
                RenumberConfirmationRules.SlovakAsciiYesKeyword,
                RenumberConfirmationRules.SlovakAsciiYesKeyword,
                RenumberConfirmationRules.SlovakAsciiYesKeyword,
                false,
                true);
        }

        options.Keywords.Add("No", no, no);
        options.Keywords.Default = "No";
        var response = editor.GetKeywords(options);
        return response.Status == PromptStatus.OK &&
               RenumberConfirmationRules.IsConfirmed(
                   response.StringResult,
                   yes,
                   uiCulture);
    }

    private static bool TryPersist(
        Document document,
        ObjectId ownerId,
        RoofDefinitionData data,
        out string failureMessageKey)
    {
        failureMessageKey = "Command_Roof_PersistFailed";
        try
        {
            using var documentLock = document.LockDocument();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            if (transaction.GetObject(ownerId, OpenMode.ForWrite) is not Polyline owner)
            {
                return false;
            }

            var currentInput = RoofPolylineExtractor.Extract(owner);
            var current = RoofFootprintValidator.Validate(currentInput);
            if (!current.IsValid || current.Footprint is null ||
                !RoofDefinitionPersistence.Restore(
                    currentInput,
                    current.Footprint,
                    data).IsValid)
            {
                failureMessageKey = "Command_Roof_PersistSourceChanged";
                return false;
            }

            if (RoofDefinitionStore.Read(owner).Exists)
            {
                failureMessageKey = "Command_Roof_PersistConflict";
                return false;
            }

            var restored = RoofDefinitionPersistence.Restore(
                currentInput,
                current.Footprint,
                data);
            if (!restored.IsValid || restored.Geometry is null)
            {
                failureMessageKey = "Command_Roof_PersistSourceChanged";
                return false;
            }

            RoofDefinitionStore.Write(owner, transaction, data);
            var sourceElevation = RoofPolylineExtractor.GetSourceElevation(owner);
            var edges = RoofWireframe.Create(restored.Geometry, sourceElevation);
            var signature = RoofWireframe.BuildGenerationSignature(edges);
            if (!RoofDisplayService.Rebuild(
                    document.Database,
                    transaction,
                    owner.ObjectId,
                    owner.Handle.ToString(),
                    edges,
                    signature))
            {
                failureMessageKey = "Command_Roof_DisplayFutureSchema";
                return false;
            }

            transaction.Commit();
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static RoofDisplayInspection InspectDisplay(
        Database database,
        ObjectId ownerId,
        string ownerReference,
        IReadOnlyList<RoofDisplayEdge> edges,
        string signature)
    {
        using var transaction = database.TransactionManager.StartTransaction();
        return RoofDisplayService.Inspect(
            database,
            transaction,
            ownerId,
            ownerReference,
            edges,
            signature);
    }

    private static bool TryRebuildDisplay(
        Document document,
        ObjectId ownerId,
        out string failureMessageKey)
    {
        failureMessageKey = "Command_Roof_DisplayFailed";
        try
        {
            using var documentLock = document.LockDocument();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            if (transaction.GetObject(ownerId, OpenMode.ForRead) is not Polyline owner)
            {
                return false;
            }

            var input = RoofPolylineExtractor.Extract(owner);
            var validation = RoofFootprintValidator.Validate(input);
            var stored = RoofDefinitionStore.Read(owner);
            if (!validation.IsValid || validation.Footprint is null || stored.Data is null)
            {
                failureMessageKey = "Command_Roof_PersistedInvalid";
                return false;
            }

            var restored = RoofDefinitionPersistence.Restore(input, validation.Footprint, stored.Data);
            if (!restored.IsValid || restored.Geometry is null)
            {
                failureMessageKey = restored.Error == RoofDefinitionRestoreError.StaleFootprint
                    ? "Command_Roof_PersistedStale"
                    : "Command_Roof_PersistedInvalid";
                return false;
            }

            var edges = RoofWireframe.Create(
                restored.Geometry,
                RoofPolylineExtractor.GetSourceElevation(owner));
            var signature = RoofWireframe.BuildGenerationSignature(edges);
            if (!RoofDisplayService.Rebuild(
                    document.Database,
                    transaction,
                    owner.ObjectId,
                    owner.Handle.ToString(),
                    edges,
                    signature))
            {
                failureMessageKey = "Command_Roof_DisplayFutureSchema";
                return false;
            }

            transaction.Commit();
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static bool TryRehydrateGroup(
        Document document,
        ObjectId ownerId,
        out string failureMessageKey)
    {
        failureMessageKey = "Command_Roof_DisplayFailed";
        try
        {
            using var documentLock = document.LockDocument();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            if (transaction.GetObject(ownerId, OpenMode.ForRead) is not Polyline owner)
            {
                return false;
            }

            var input = RoofPolylineExtractor.Extract(owner);
            var validation = RoofFootprintValidator.Validate(input);
            var stored = RoofDefinitionStore.Read(owner);
            if (!validation.IsValid || validation.Footprint is null || stored.Data is null)
            {
                failureMessageKey = "Command_Roof_PersistedInvalid";
                return false;
            }

            var restored = RoofDefinitionPersistence.Restore(input, validation.Footprint, stored.Data);
            if (!restored.IsValid || restored.Geometry is null)
            {
                failureMessageKey = restored.Error == RoofDefinitionRestoreError.StaleFootprint
                    ? "Command_Roof_PersistedStale"
                    : "Command_Roof_PersistedInvalid";
                return false;
            }

            var edges = RoofWireframe.Create(
                restored.Geometry,
                RoofPolylineExtractor.GetSourceElevation(owner));
            var signature = RoofWireframe.BuildGenerationSignature(edges);
            var inspection = RoofDisplayService.Inspect(
                document.Database,
                transaction,
                owner.ObjectId,
                owner.Handle.ToString(),
                edges,
                signature);
            if (!inspection.Validation.IsCurrent)
            {
                failureMessageKey = inspection.Validation.Issues.HasFlag(
                    RoofDisplayValidationIssue.UnsupportedFutureSchema)
                    ? "Command_Roof_DisplayFutureSchema"
                    : "Command_Roof_DisplayFailed";
                return false;
            }

            RoofDisplayGroupService.CreateGroupFromExistingValidatedDisplay(
                document.Database,
                transaction,
                owner.ObjectId,
                inspection.ChildIds);
            transaction.Commit();
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static bool TryPromptOrientationDirection(
        Editor editor,
        RoofKind kind,
        out RoofDirection2D direction)
    {
        direction = default;

        var isMonopitch = kind == RoofKind.Monopitch;
        var directionStartPrompt = UiStrings.GetString(isMonopitch
            ? "Command_Roof_HighSidePointPrompt"
            : "Command_Roof_RidgeDirectionStartPrompt");
        if (isMonopitch)
        {
            directionStartPrompt = "\n" + directionStartPrompt;
        }
        var directionStartResult = editor.GetPoint(new PromptPointOptions(
            directionStartPrompt));
        if (directionStartResult.Status != PromptStatus.OK)
        {
            return false;
        }

        var directionEndPrompt = UiStrings.GetString(isMonopitch
            ? "Command_Roof_LowSidePointPrompt"
            : "Command_Roof_RidgeDirectionEndPrompt");
        if (isMonopitch)
        {
            directionEndPrompt = "\n" + directionEndPrompt;
        }
        var directionEndOptions = new PromptPointOptions(
            directionEndPrompt)
        {
            BasePoint = directionStartResult.Value,
            UseBasePoint = true,
            UseDashedLine = true,
        };
        var directionEndResult = editor.GetPoint(directionEndOptions);
        if (directionEndResult.Status != PromptStatus.OK)
        {
            return false;
        }

        // Managed Editor point results are already expressed in WCS. The same
        // WCS contract is used by Polyline.GetPoint3dAt in the S1 extractor.
        var start = directionStartResult.Value;
        var end = directionEndResult.Value;
        if (!RoofDirection2D.TryCreate(end.X - start.X, end.Y - start.Y, out direction))
        {
            editor.WriteMessage(UiStrings.GetString("Command_Roof_GeometryErrorDirection"));
            return false;
        }
        if (isMonopitch)
        {
            direction = MonopitchRoofDirectionPresentationRules.ToCanonicalLowToHigh(
                direction);
        }

        return true;
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

    private static void ShowNotification(RoofNotificationDescriptor notification) =>
        TransientNotificationService.Show(
            notification.TitleResourceKey,
            notification.BodyResourceKey);

    private static bool TryGetSelectionNotification(
        RoofOwnerSelectionError error,
        out RoofNotificationDescriptor notification)
    {
        if (error == RoofOwnerSelectionError.UnrelatedObject)
        {
            notification = InvalidObjectNotification;
            return true;
        }

        notification = default;
        return false;
    }

    private static bool TryGetValidationNotification(
        RoofValidationError error,
        out RoofNotificationDescriptor notification)
    {
        if (error == RoofValidationError.OpenLoop)
        {
            notification = OpenLoopNotification;
            return true;
        }

        if (error is RoofValidationError.UnsupportedCurvedSegment or
            RoofValidationError.NonPlanar or
            RoofValidationError.FewerThanThreeUniqueVertices or
            RoofValidationError.NonFiniteCoordinate or
            RoofValidationError.DuplicateConsecutiveVertex or
            RoofValidationError.ZeroLengthEdge or
            RoofValidationError.SelfIntersection or
            RoofValidationError.DegenerateArea or
            RoofValidationError.RedundantCollinearVertex)
        {
            notification = InvalidFootprintNotification;
            return true;
        }

        notification = default;
        return false;
    }

    private static bool TryGetGeometryNotification(
        SimpleGableRoofGeometryError error,
        out RoofNotificationDescriptor notification)
    {
        if (error is SimpleGableRoofGeometryError.FootprintIsNotFourSided or
            SimpleGableRoofGeometryError.FootprintIsNotRectangular)
        {
            notification = UnsupportedFootprintNotification;
            return true;
        }

        if (error == SimpleGableRoofGeometryError.DegenerateDimensions)
        {
            notification = InvalidFootprintNotification;
            return true;
        }

        if (error == SimpleGableRoofGeometryError.RidgeDirectionCannotBeResolved)
        {
            notification = InvalidDirectionNotification;
            return true;
        }

        if (error == SimpleGableRoofGeometryError.InvalidSlope)
        {
            notification = InvalidSlopeNotification;
            return true;
        }

        notification = default;
        return false;
    }

    private static string GetValidationMessage(RoofValidationError error) =>
        UiStrings.GetString(error switch
        {
            RoofValidationError.OpenLoop => "Command_Roof_ErrorOpen",
            RoofValidationError.UnsupportedCurvedSegment => "Command_Roof_ErrorCurved",
            RoofValidationError.NonPlanar => "Command_Roof_ErrorNonPlanar",
            RoofValidationError.FewerThanThreeUniqueVertices => "Command_Roof_ErrorFewVertices",
            RoofValidationError.NonFiniteCoordinate => "Command_Roof_ErrorNonFinite",
            RoofValidationError.DuplicateConsecutiveVertex => "Command_Roof_ErrorDuplicateVertex",
            RoofValidationError.ZeroLengthEdge => "Command_Roof_ErrorZeroLengthEdge",
            RoofValidationError.SelfIntersection => "Command_Roof_ErrorSelfIntersection",
            RoofValidationError.DegenerateArea => "Command_Roof_ErrorDegenerate",
            RoofValidationError.RedundantCollinearVertex => "Command_Roof_ErrorCollinearVertex",
            _ => "Command_Roof_ErrorUnsupported",
        });

    private static string GetSelectionMessage(RoofOwnerSelectionError error) =>
        UiStrings.GetString(error switch
        {
            RoofOwnerSelectionError.MalformedDisplayMetadata =>
                "Command_Roof_SelectionInvalidDisplay",
            RoofOwnerSelectionError.UnsupportedFutureDisplaySchema =>
                "Command_Roof_SelectionFutureDisplay",
            RoofOwnerSelectionError.InvalidOwnerReference or
            RoofOwnerSelectionError.MissingOwner or
            RoofOwnerSelectionError.OwnerIsNotPolyline =>
                "Command_Roof_SelectionOrphan",
            _ => "Command_Roof_SelectionInvalid",
        });

    private static string GetOrientationText(RoofPolygonOrientation orientation) =>
        UiStrings.GetString(orientation == RoofPolygonOrientation.Clockwise
            ? "Command_Roof_OrientationClockwise"
            : "Command_Roof_OrientationCounterClockwise");

    private static string GetGeometryMessage(SimpleGableRoofGeometryError error) =>
        UiStrings.GetString(error switch
        {
            SimpleGableRoofGeometryError.FootprintIsNotFourSided =>
                "Command_Roof_GeometryErrorFourSided",
            SimpleGableRoofGeometryError.FootprintIsNotRectangular =>
                "Command_Roof_GeometryErrorRectangular",
            SimpleGableRoofGeometryError.RidgeDirectionCannotBeResolved =>
                "Command_Roof_GeometryErrorDirection",
            SimpleGableRoofGeometryError.InvalidSlope =>
                "Command_Roof_GeometryErrorSlope",
            SimpleGableRoofGeometryError.DegenerateDimensions =>
                "Command_Roof_GeometryErrorDimensions",
            _ => "Command_Roof_GeometryErrorNonFinite",
        });

    private static string GetStoredDefinitionMessage(
        RoofDefinitionDataDecodeError error) =>
        UiStrings.GetString(error switch
        {
            RoofDefinitionDataDecodeError.UnsupportedFutureSchema =>
                "Command_Roof_PersistedFutureSchema",
            RoofDefinitionDataDecodeError.UnsupportedRoofKind =>
                "Command_Roof_PersistedUnsupportedKind",
            _ => "Command_Roof_PersistedInvalid",
        });

    private readonly record struct RoofNotificationDescriptor(
        string TitleResourceKey,
        string BodyResourceKey);
}
