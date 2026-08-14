using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using AcKrovy.AutoCAD.UI;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Validated roof definition, transient preview and explicit permanent-display lifecycle.</summary>
internal static class RoofCommandWorkflow
{
    public static void Run(Document document)
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
                    editor.WriteMessage(GetSelectionMessage(resolution.Error));
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
                if (validation.Error == RoofValidationError.OpenLoop)
                {
                    // Custom transient window is the primary OpenLoop UX; avoid
                    // duplicating the same error on the command line.
                    TransientNotificationService.Show(
                        "Command_Roof_OpenLoopNotificationTitle",
                        "Command_Roof_OpenLoopNotificationBody");
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

                var edges = SimpleGableRoofWireframe.Create(restored.Geometry, sourceElevation);
                var signature = SimpleGableRoofWireframe.BuildGenerationSignature(edges);
                var display = InspectDisplay(
                    document.Database,
                    ownerId,
                    sourceReference,
                    edges,
                    signature);
                if (display.Validation.IsCurrent)
                {
                    if (display.Group.IsCurrent)
                    {
                        editor.WriteMessage(UiStrings.GetString("Command_Roof_DisplayCurrent"));
                        return;
                    }

                    editor.WriteMessage(UiStrings.GetString("Command_Roof_GroupMissing"));
                    if (!ConfirmYesNo(editor, "Command_Roof_GroupRepairPrompt"))
                    {
                        return;
                    }
                    if (TryRebuildDisplay(document, ownerId, out var groupFailureKey))
                    {
                        editor.WriteMessage(UiStrings.GetString("Command_Roof_GroupRepaired"));
                    }
                    else
                    {
                        editor.WriteMessage(UiStrings.GetString(groupFailureKey));
                    }
                    return;
                }
                if (display.Validation.Issues.HasFlag(
                        RoofDisplayValidationIssue.UnsupportedFutureSchema))
                {
                    editor.WriteMessage(UiStrings.GetString("Command_Roof_DisplayFutureSchema"));
                    return;
                }

                var isMissing = display.Validation.State == RoofDisplayState.Missing;
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
                }
                else
                {
                    editor.WriteMessage(UiStrings.GetString(displayFailureKey));
                }
                return;
            }

            if (!TryPromptParameters(editor, out var parameters))
            {
                return;
            }

            var definition = new RoofDefinition(validation.Footprint, parameters);
            var geometryResult = SimpleGableRoofGeometrySolver.Solve(definition);
            if (!geometryResult.IsValid || geometryResult.Geometry is null)
            {
                editor.WriteMessage(GetGeometryMessage(geometryResult.Error));
                continue;
            }

            editor.SetImpliedSelection([ownerId]);
            editor.UpdateScreen();
            editor.WriteMessage(UiStrings.Format(
                UiStrings.GetString("Command_Roof_AcceptedFormat"),
                definition.Footprint.Vertices.Count,
                definition.Footprint.AreaMm2 / 1_000_000d,
                GetOrientationText(validation.SourceOrientation)));
            ShowPreview(document, geometryResult.Geometry, sourceElevation);

            if (!ConfirmPersistence(editor))
            {
                return;
            }

            var definitionData = RoofDefinitionPersistence.Create(
                sourceInput,
                validation.Footprint,
                geometryResult.Geometry);
            if (TryPersist(
                    document,
                    ownerId,
                    definitionData,
                    out var failureMessageKey))
            {
                editor.WriteMessage(UiStrings.GetString("Command_Roof_PersistedAndDisplaySaved"));
            }
            else
            {
                editor.WriteMessage(UiStrings.GetString(failureMessageKey));
            }

            return;
        }
    }

    private static void ShowPreview(
        Document document,
        SimpleGableRoofGeometry geometry,
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

    private static bool ConfirmPersistence(Editor editor)
        => ConfirmYesNo(editor, "Command_Roof_PersistConfirmPrompt");

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

            var sourceElevation = RoofPolylineExtractor.GetSourceElevation(owner);
            var edges = SimpleGableRoofWireframe.Create(restored.Geometry, sourceElevation);
            var signature = SimpleGableRoofWireframe.BuildGenerationSignature(edges);
            RoofDefinitionStore.Write(owner, transaction, data);
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

            var edges = SimpleGableRoofWireframe.Create(
                restored.Geometry,
                RoofPolylineExtractor.GetSourceElevation(owner));
            var signature = SimpleGableRoofWireframe.BuildGenerationSignature(edges);
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

    private static bool TryPromptParameters(Editor editor, out RoofParameters parameters)
    {
        parameters = RoofParameters.Unspecified;
        var slopeResult = editor.GetDouble(new PromptDoubleOptions(
            UiStrings.GetString("Command_Roof_SlopePrompt"))
        {
            AllowNegative = false,
            AllowZero = false,
            AllowNone = false,
        });
        if (slopeResult.Status != PromptStatus.OK)
        {
            return false;
        }

        var directionStartResult = editor.GetPoint(new PromptPointOptions(
            UiStrings.GetString("Command_Roof_RidgeDirectionStartPrompt")));
        if (directionStartResult.Status != PromptStatus.OK)
        {
            return false;
        }

        var directionEndOptions = new PromptPointOptions(
            UiStrings.GetString("Command_Roof_RidgeDirectionEndPrompt"))
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
        if (!RoofDirection2D.TryCreate(end.X - start.X, end.Y - start.Y, out var direction))
        {
            editor.WriteMessage(UiStrings.GetString("Command_Roof_GeometryErrorDirection"));
            return false;
        }

        parameters = new RoofParameters(slopeResult.Value, direction);
        return true;
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
}
