using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using AcKrovy.AutoCAD.UI;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Read-only S1 host workflow for selecting and validating one footprint.</summary>
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
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                if (transaction.GetObject(selected.ObjectId, OpenMode.ForRead) is not Polyline polyline)
                {
                    editor.WriteMessage(UiStrings.GetString("Command_Roof_PolylineOnly"));
                    continue;
                }

                validation = RoofFootprintValidator.Validate(
                    RoofPolylineExtractor.Extract(polyline));
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

            var definition = new RoofDefinition(validation.Footprint);
            editor.SetImpliedSelection([selected.ObjectId]);
            editor.UpdateScreen();
            editor.WriteMessage(UiStrings.Format(
                UiStrings.GetString("Command_Roof_AcceptedFormat"),
                definition.Footprint.Vertices.Count,
                definition.Footprint.AreaMm2 / 1_000_000d,
                GetOrientationText(validation.SourceOrientation)));
            return;
        }
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
}
