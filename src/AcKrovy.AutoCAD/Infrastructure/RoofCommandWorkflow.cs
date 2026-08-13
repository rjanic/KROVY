using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using AcKrovy.AutoCAD.UI;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>S1 validation, transient preview and lazy persisted-definition lifecycle.</summary>
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
            prompt.SetRejectMessage(UiStrings.GetString("Command_Roof_PolylineOnly"));
            prompt.AddAllowedClass(typeof(Polyline), exactMatch: true);
            var selected = editor.GetEntity(prompt);
            if (selected.Status != PromptStatus.OK)
            {
                return;
            }

            RoofValidationResult validation;
            RoofFootprintInput sourceInput;
            double sourceElevation;
            RoofDefinitionStoreReadResult storedDefinition;
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                if (transaction.GetObject(selected.ObjectId, OpenMode.ForRead) is not Polyline polyline)
                {
                    editor.WriteMessage(UiStrings.GetString("Command_Roof_PolylineOnly"));
                    continue;
                }

                sourceInput = RoofPolylineExtractor.Extract(polyline);
                validation = RoofFootprintValidator.Validate(sourceInput);
                sourceElevation = RoofPolylineExtractor.GetSourceElevation(polyline);
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

                editor.SetImpliedSelection([selected.ObjectId]);
                editor.WriteMessage(UiStrings.GetString("Command_Roof_PersistedLoaded"));
                ShowPreview(document, restored.Geometry, sourceElevation);
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

            editor.SetImpliedSelection([selected.ObjectId]);
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
                    selected.ObjectId,
                    definitionData,
                    out var failureMessageKey))
            {
                editor.WriteMessage(UiStrings.GetString("Command_Roof_PersistedSaved"));
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
    {
        var uiCulture = AppLanguageService.CurrentUiCulture;
        var yes = UiStrings.GetString("Message_Yes", uiCulture);
        var no = UiStrings.GetString("Message_No", uiCulture);
        var options = new PromptKeywordOptions(
            UiStrings.GetString("Command_Roof_PersistConfirmPrompt", uiCulture))
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

            RoofDefinitionStore.Write(owner, transaction, data);
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
