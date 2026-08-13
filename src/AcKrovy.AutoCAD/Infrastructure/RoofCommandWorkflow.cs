using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using AcKrovy.AutoCAD.UI;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Read-only S1 selection plus S2 transient simple-gable preview workflow.</summary>
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
            double sourceElevation;
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                if (transaction.GetObject(selected.ObjectId, OpenMode.ForRead) is not Polyline polyline)
                {
                    editor.WriteMessage(UiStrings.GetString("Command_Roof_PolylineOnly"));
                    continue;
                }

                validation = RoofFootprintValidator.Validate(
                    RoofPolylineExtractor.Extract(polyline));
                sourceElevation = RoofPolylineExtractor.GetSourceElevation(polyline);
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
            using (RoofTransientPreviewSession.Show(
                       document,
                       geometryResult.Geometry,
                       sourceElevation))
            {
                _ = editor.GetString(new PromptStringOptions(
                    UiStrings.GetString("Command_Roof_PreviewClosePrompt"))
                {
                    AllowSpaces = false,
                });
            }

            return;
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
}
