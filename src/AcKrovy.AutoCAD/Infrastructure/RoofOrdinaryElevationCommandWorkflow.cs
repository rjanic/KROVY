using System.Globalization;
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

/// <summary>
/// AK_KROKEV_ELEVATION: Výškové osadenie — elevation seating editor for one Ordinary rafter.
///
/// Prompts to select an Ordinary rafter (AUTO or Independent).
/// Opens RoofOrdinaryElevationWindow with the current elevation state.
/// On Apply: delegates to RoofOrdinaryElevationLifecycleService.
/// On Cancel: exact rollback (dialog closes without any transaction).
///
/// Plan2D is never moved. Physical3D is rebuilt only on geometry-affecting Apply.
/// SH/OS/VH reference switch: persisted as display preference, no detach, no geometry move.
///
/// DIAGNOSTICS emitted:
///   ROOF_MEMBER_ELEVATION_STATE (on read)
///   ROOF_ORDINARY_ELEVATION_LIFECYCLE (on apply)
/// </summary>
internal static class RoofOrdinaryElevationCommandWorkflow
{
    private static readonly CultureInfo SlovakCulture = CultureInfo.GetCultureInfo("sk-SK");

    public static void Run(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var editor = document.Editor;

        // Prompt: select one Ordinary rafter line.
        var prompt = new PromptEntityOptions(UiStrings.GetString("RoofOrdinaryElevation_SelectPrompt"));
        prompt.SetRejectMessage(UiStrings.GetString("RoofOrdinaryElevation_SelectReject"));
        prompt.AddAllowedClass(typeof(Line), exactMatch: false);
        var selected = editor.GetEntity(prompt);
        if (selected.Status != PromptStatus.OK) return;

        // Read current state and plan geometry.
        Line? line = null;
        StructuralMemberElevationState? currentState;
        string stateSource;
        double planLengthMm;
        double heightMm;
        bool isAuto;

        using (var readTx = document.Database.TransactionManager.StartTransaction())
        {
            var entity = (Entity)readTx.GetObject(selected.ObjectId, OpenMode.ForRead);
            if (entity is not Line l)
            {
                editor.WriteMessage(UiStrings.GetString("RoofOrdinaryElevation_NotALine"));
                return;
            }
            line = l;

            // Verify it is an Ordinary rafter (AUTO or Independent).
            isAuto = RoofGeneratedTimberStore.Read(line).Data is
                { MemberKind: RoofGeneratedTimberKind.Rafter };
            var isIndep = RoofIndependentOrdinaryTimberStore.Read(line) is
                { EntityRole: RoofIndependentOrdinaryEntityRole.PlanLine };

            if (!isAuto && !isIndep)
            {
                editor.WriteMessage(UiStrings.GetString("RoofOrdinaryElevation_NotOrdinary"));
                return;
            }

            // Plan geometry.
            var plan = new RoofSegment3D(
                new RoofPoint3D(line.StartPoint.X, line.StartPoint.Y, 0),
                new RoofPoint3D(line.EndPoint.X, line.EndPoint.Y, 0));
            var dx = plan.End.X - plan.Start.X;
            var dy = plan.End.Y - plan.Start.Y;
            planLengthMm = Math.Sqrt(dx * dx + dy * dy);

            if (planLengthMm < 1.0) // < 1 mm: degenerate member
            {
                editor.WriteMessage(UiStrings.GetString("RoofOrdinaryElevation_Degenerate"));
                return;
            }

            // Section height.
            if (!new AutoCadTimberElementMetadataStore(readTx).TryRead(line, out var timber) || timber is null)
            {
                editor.WriteMessage(UiStrings.GetString("RoofOrdinaryElevation_NoMetadata"));
                return;
            }
            heightMm = timber.HeightMm;
            if (heightMm <= 0d) heightMm = 160d; // fallback if somehow missing

            // Read current elevation state. Never invent synthetic zero for an existing member.
            if (!RoofOrdinaryElevationLifecycleService.TryReadCurrentState(
                    line, readTx, out currentState, out stateSource) || currentState is null)
            {
                editor.WriteMessage(
                    UiStrings.GetString("RoofOrdinaryElevation_Failed") +
                    $" (resolve:{stateSource})");
                return;
            }
            readTx.Commit();
        }

        // Open dialog (must be called without a live transaction).
        var vm = new RoofOrdinaryElevationViewModel(currentState, planLengthMm, heightMm, SlovakCulture);
        var window = new RoofOrdinaryElevationWindow(vm);
        var helper = new System.Windows.Interop.WindowInteropHelper(window);
        helper.Owner = AcApp.MainWindow.Handle;

        if (window.ShowDialog() != true) return; // user cancelled

        var requested = vm.BuildRequestedState();
        var geometryChanged = vm.GeometryChanged;

        if (!vm.HasChanges)
        {
            editor.WriteMessage(UiStrings.GetString("RoofOrdinaryElevation_NoChange"));
            return;
        }

        // Apply via lifecycle service.
        var prefs = SettingsUiPreferencesStore.Load();
        // Re-open the line for the lifecycle service (it will open its own transaction).
        using var docLock = document.LockDocument();
        using var applyTx = document.Database.TransactionManager.StartTransaction();
        var lineForApply = (Line)applyTx.GetObject(selected.ObjectId, OpenMode.ForRead);
        var result = RoofOrdinaryElevationLifecycleService.TryApply(
            document, lineForApply, requested, currentState, geometryChanged, prefs);

        switch (result)
        {
            case RoofOrdinaryElevationLifecycleService.ApplyResult.DisplayOnlyApplied:
            case RoofOrdinaryElevationLifecycleService.ApplyResult.AutoDetachedAndApplied:
            case RoofOrdinaryElevationLifecycleService.ApplyResult.IndependentApplied:
                applyTx.Commit();
                editor.WriteMessage(UiStrings.GetString("RoofOrdinaryElevation_Applied"));
                break;
            case RoofOrdinaryElevationLifecycleService.ApplyResult.UserCancelledDetach:
                // No commit; transaction discarded.
                break;
            default:
                editor.WriteMessage(
                    UiStrings.GetString("RoofOrdinaryElevation_Failed") + $" ({result})");
                break;
        }
    }
}
