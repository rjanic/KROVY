using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source contracts for flexible AK_ROOF_PURLINS roof selection and interactive
/// rafter-dimension pick without mutating rafter recipes.
/// </summary>
public sealed class RoofAutomaticPurlinFlexibleSelectionSourceContractTests
{
    private static readonly string Workflow = Read(
        "Infrastructure", "RoofAutomaticPurlinCommandWorkflow.cs");
    private static readonly string Resolver = Read(
        "Infrastructure", "RoofOwnerSelectionResolver.cs");
    private static readonly string RafterPick = Read(
        "Infrastructure", "RoofAutomaticPurlinSelectedRafterResolver.cs");
    private static readonly string Window = Read(
        "UI", "AutomaticPurlinDialogWindow.xaml.cs");
    private static readonly string SectionView = Read(
        "UI", "AutomaticPurlinRoofSectionView.cs");
    private static readonly string ViewModel = Read(
        "UI", "AutomaticPurlinDialogViewModel.cs");
    private static readonly string Apply = Read(
        "Infrastructure", "RoofAutomaticPurlinProductionApplyService.cs");
    private static readonly string Materialization = Read(
        "Infrastructure", "RoofAutomaticPurlinMaterializationService.cs");

    [Fact]
    public void RoofSelection_UsesOwnerResolverNotPolylineOnlyFilter()
    {
        Assert.Contains("TrySelectAuthoritativeRoof(", Workflow);
        Assert.Contains("RoofOwnerSelectionResolver.Resolve(", Workflow);
        Assert.DoesNotContain("AddAllowedClass(typeof(Polyline)", Workflow);
        Assert.Contains("AutomaticPurlin_CommandSelectRoof", Workflow);
        Assert.Contains("AutomaticPurlin_CommandInvalidSelection", Workflow);
        Assert.Contains("Command_Roof_InvalidObjectNotificationTitle", Workflow);
    }

    [Fact]
    public void OwnerResolver_AcceptsGeneratedStructuralAndPurlinMembers()
    {
        Assert.Contains("RoofGeneratedTimberStore.Read(selected)", Resolver);
        Assert.Contains("RoofStructuralGeneratedStore.Read(selected)", Resolver);
        Assert.Contains("RoofAutomaticPurlinGeneratedStore.Read(selected)", Resolver);
        Assert.Contains("RoofDisplayStore.Read(selected)", Resolver);
    }

    [Fact]
    public void RafterLabel_IsClickableAndAlwaysOpensManualDialog()
    {
        Assert.Contains("RafterLabelClicked", SectionView);
        Assert.Contains("Cursors.Hand", SectionView);
        Assert.Contains("_rafterLabelHitBounds", SectionView);
        Assert.Contains("_rafterLabelPressed", SectionView);
        Assert.Contains("AutomaticPurlin_SelectRafterClickHint", SectionView);
        Assert.Contains("DrawInteractiveRafterButtonChrome", SectionView);
        Assert.Contains("RoofSectionView_RafterLabelClicked", Window);
        // PreferCad must not bypass into a raw CAD pick that overwrites host actual.
        var labelHandler = Member(
            Window,
            "private void RoofSectionView_RafterLabelClicked(",
            "private void EnterRafterDimensionsButton_Click(");
        Assert.Contains("OpenManualRafterDimensionsDialog();", labelHandler);
        Assert.DoesNotContain("IsSuspendedForRafterPick = true", labelHandler);
        Assert.DoesNotContain("PrefersCadRafterPick", labelHandler);
        Assert.Contains("TryPickRafterDimensions(", Workflow);
        Assert.Contains("IsSuspendedForCadPreview", Workflow);
        Assert.Contains("IsSuspendedForRafterPick", Workflow);
        Assert.Contains("new AutomaticPurlinDialogWindow(viewModel, theme)", Workflow);
        // Defense: legacy schematic pick path classifies ownership before Selected apply.
        var pickMethod = Member(
            Workflow,
            "private static void TryPickRafterDimensions(",
            "private static void TryPickRafterDimensionsForManualDialog(");
        Assert.Contains("IsCurrentRoofGeneratedRafter(", pickMethod);
        Assert.Contains("TryApplySelectedRafterDimensions(", pickMethod);
        Assert.Contains("TryApplyManualRafterDimensions(", pickMethod);
    }

    [Fact]
    public void SelectedRafter_AcceptsAnyValidKrovyRafterAsDimensionSource()
    {
        Assert.Contains("TryResolveRafterDimensions(", RafterPick);
        Assert.Contains("TimberElementType.Rafter", RafterPick);
        Assert.Contains("timber.WidthMm", RafterPick);
        Assert.Contains("timber.HeightMm", RafterPick);
        Assert.Contains("IsCurrentRoofGeneratedRafter(", RafterPick);
        Assert.DoesNotContain("expectedOwnerReference", RafterPick);
        Assert.DoesNotContain("OtherRoofRejected", RafterPick);
        Assert.DoesNotContain("SameRoofOrdinaryRafterRequired", RafterPick);
        Assert.DoesNotContain("StartPoint", RafterPick);
        Assert.DoesNotContain("EndPoint", RafterPick);
        Assert.Contains("NonRafterRejected", RafterPick);
    }

    [Fact]
    public void ManualDialog_SelectFromDrawing_ReusesSuspendRestoreWithoutImmediateDraftWrite()
    {
        Assert.Contains("IsSuspendedForManualDialogRafterPick", Window);
        Assert.Contains("TryPickRafterDimensionsForManualDialog(", Workflow);
        Assert.Contains("BeginManualDialogCadPick(", Window + ViewModel);
        Assert.Contains("CompleteManualDialogCadPick(", Workflow + ViewModel);
        Assert.Contains("ShouldAdoptPickedRafterAsSelected(", Window + ViewModel);
        Assert.Contains("IsCurrentRoofGeneratedRafter(", Workflow);
        Assert.Contains("SelectFromDrawingButton", Read("UI", "AutomaticPurlinManualRafterDialogWindow.xaml"));
        Assert.Contains("IsSuspendedForCadPick", Read("UI", "AutomaticPurlinManualRafterDialogWindow.xaml.cs"));
        var manualPickMethod = Member(
            Workflow,
            "private static void TryPickRafterDimensionsForManualDialog(",
            "private static bool TryPromptRafterDimensions(");
        Assert.Contains("CompleteManualDialogCadPick(", manualPickMethod);
        Assert.DoesNotContain("TryApplySelectedRafterDimensions(", manualPickMethod);
    }

    [Fact]
    public void SelectedRafter_DoesNotChangeRoofOwnerOrMutateTimber()
    {
        Assert.Contains("TryResolveRafterDimensions(", Workflow);
        Assert.Contains("WriteSelectionDiagnostic(", Workflow);
        Assert.DoesNotContain("RoofGeneratedRafterSetService.TryReplace", Workflow + RafterPick);
        Assert.DoesNotContain("RoofGeneratedTimberStore.Write", RafterPick);
        Assert.DoesNotContain("metadataStore.Write", RafterPick);
        Assert.Contains("Dimension source only", RafterPick);
    }

    [Fact]
    public void RafterPickDiagnostic_RoutesToDebugNotEditorWriteMessageOrToolTip()
    {
        Assert.Contains("ROOF_PURLIN_RAFTER_PICK", RafterPick);
        Assert.Contains("Debug.WriteLine(", RafterPick);
        Assert.Contains("WriteSelectionDiagnostic(", RafterPick);
        // Must not WriteMessage the diagnostic — AutoCAD tips it over the reopened WPF dialog.
        var diagnosticMethod = SegmentAfter(RafterPick, "WriteSelectionDiagnostic(");
        Assert.DoesNotContain("WriteMessage(", diagnosticMethod.Split("#else")[0]);
        Assert.DoesNotContain("ROOF_PURLIN_RAFTER_PICK", ViewModel);
        Assert.Contains("ResolveRafterUserToolTip(", SectionView);
        Assert.Contains("AutomaticPurlin_SelectRafterHint", SectionView);
        Assert.Contains("ToolTipService.AddToolTipOpeningHandler(", SectionView);
        // Section view may mention the token only as a defensive ToolTip reject guard.
        Assert.Contains(
            "text.Contains(\"ROOF_PURLIN_RAFTER_PICK\"",
            SectionView);
        Assert.DoesNotContain("ToolTip = \"ROOF_PURLIN_RAFTER_PICK", SectionView);
        Assert.DoesNotContain("ToolTip = Diagnostic", SectionView);
    }

    [Fact]
    public void ViewModel_AppliesSelectedDimensionsToSectionAndSeating()
    {
        Assert.Contains("TryApplySelectedRafterDimensions(", ViewModel);
        Assert.Contains("SetRafterHeightMm(", ViewModel);
        Assert.Contains("_rafterWidthMm = widthMm", ViewModel);
        Assert.Contains("_rafterHeightMm = heightMm", ViewModel);
        Assert.Contains("Recalculate()", ViewModel);
    }

    [Fact]
    public void Apply_UsesDialogRafterHeightNotIndependentRecipeOnly()
    {
        Assert.Contains("args.RafterHeightMm", Workflow);
        Assert.Contains("double rafterHeightMm", Apply);
        Assert.Contains("authoritativeRafterHeightMm: rafterHeightMm", Apply);
        Assert.Contains("authoritativeRafterHeightMm = null", Materialization);
        Assert.Contains("explicitHeight", Materialization);
    }

    [Fact]
    public void AmbiguousRecipe_UsesPurlinSpecificMessageWithoutEmptyApply()
    {
        Assert.Contains("AutomaticPurlin_RafterRecipeAmbiguous", Workflow);
        Assert.DoesNotContain(
            "Command_RoofRafters_RecipeAmbiguous",
            Workflow);
        Assert.Contains("WriteRafterSourceFailure(", Workflow);
        Assert.Contains("ROOF_PURLIN_RAFTER_SOURCE", Workflow);
        Assert.Contains("Debug.WriteLine(", Workflow);
        // Failure token must not Editor.WriteMessage — same tip-leak class as RAFTER_PICK.
        var failureMethod = SegmentAfter(Workflow, "private static void WriteRafterSourceFailure(");
        Assert.DoesNotContain("WriteMessage(", failureMethod.Split("private static void WriteApplyFailure(")[0]);
        var ambiguousBranch = SegmentAfter(
            Workflow,
            "if (!TryResolveRafterDefaults(");
        Assert.DoesNotContain("WriteApplyFailure(", ambiguousBranch.Split("var viewModel")[0]);
        Assert.DoesNotContain("RoofAutomaticPurlinProductionApplyService.Apply", ambiguousBranch.Split("var viewModel")[0]);
    }

    [Fact]
    public void RafterSourceDiagnostic_RoutesToDebugNotEditorWriteMessage()
    {
        var rafterDims = Read(
            "Infrastructure", "RoofAutomaticPurlinRafterDimensionsResolver.cs");
        Assert.Contains("ROOF_PURLIN_RAFTER_SOURCE", rafterDims);
        Assert.Contains("Debug.WriteLine(", rafterDims);
        var diagnosticMethod = SegmentAfter(rafterDims, "private static void WriteDiagnostic(");
        Assert.DoesNotContain("WriteMessage(", diagnosticMethod.Split("#else")[0]);
    }

    [Fact]
    public void CanonicalEightyByOneSixty_StillFallbackBaseline()
    {
        var profile = TimberElementDefaults.For(TimberElementType.Rafter);
        Assert.Equal(80d, profile.WidthMm);
        Assert.Equal(160d, profile.HeightMm);
    }

    private static string SegmentAfter(string source, string marker)
    {
        var index = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, marker);
        return source[index..];
    }

    private static string Member(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);

    private static string Read(string folder, string fileName) =>
        RoofUxSourceContractText.Read("src", "AcKrovy.AutoCAD", folder, fileName);
}
