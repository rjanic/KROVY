using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class AutomaticPurlinDialogAutoCadSourceContractTests
{
    private static readonly string Command = Read(
        "Commands",
        "AutoCadAutomaticPurlinDialogCommands.cs");
    private static readonly string Workflow = Read(
        "Infrastructure",
        "RoofAutomaticPurlinCommandWorkflow.cs");
    private static readonly string Window = Read(
        "UI",
        "AutomaticPurlinDialogWindow.xaml");
    private static readonly string WindowCodeBehind = Read(
        "UI",
        "AutomaticPurlinDialogWindow.xaml.cs");
    private static readonly string ViewModel = Read(
        "UI",
        "AutomaticPurlinDialogViewModel.cs");
    private static readonly string SectionPresentation = Read(
        "UI",
        "AutomaticPurlinSectionPresentation.cs");
    private static readonly string SectionView = Read(
        "UI",
        "AutomaticPurlinRoofSectionView.cs");
    private static readonly string SectionSvgTemplate = Read(
        "UI",
        "AutomaticPurlinSectionSvgTemplate.cs");
    private static readonly string Transient = Read(
        "Infrastructure",
        "RoofTransientPreviewSession.cs");

    [Fact]
    public void DebugDialog_IsReleaseIsolatedAndReadsOwnerForReadOnly()
    {
        Assert.StartsWith("#if DEBUG", Command.TrimStart());
        Assert.Contains("AK_DEBUG_PURLIN_DIALOG", Command);
        Assert.Contains("StartOpenCloseTransaction()", Command);
        Assert.Contains("OpenMode.ForRead", Command);
        Assert.DoesNotContain("OpenMode.ForWrite", Command);
        Assert.DoesNotContain("transaction.Commit", Command);
        Assert.DoesNotContain("LockDocument", Command);
    }

    [Fact]
    public void DebugDialog_HasNoPersistenceOrMaterializationCallSite()
    {
        var forbidden = new[]
        {
            "RoofPurlinLayoutStore.Write",
            "RoofRelativeElevationDatumStore.Write",
            "RoofAutomaticPurlinGeneratedStore.Write",
            "RoofBoundaryIdentityStore.Write",
            "EnsureBoundaryIdentity",
            "RoofAutomaticPurlinMaterializationService",
            "RoofAssemblyGroupSyncService",
            "AppendEntity",
            "AddNewlyCreatedDBObject",
        };
        Assert.All(forbidden, token => Assert.DoesNotContain(token, Command));
        Assert.Contains("RoofPurlinLayoutStore.Read", Command);
        Assert.Contains("RoofRelativeElevationDatumStore.Read", Command);
        Assert.Contains("RoofBoundaryIdentityStore.Read", Command);
        Assert.Contains("RoofBoundaryIdentityPreviewResolver.Resolve", Command);
        Assert.Contains("RoofBoundaryIdentityPreviewSource.Ephemeral", Command);
        Assert.Contains("RoofBoundaryIdentityPreviewSource.Persisted", Command);
    }

    [Fact]
    public void PreviewIdentityAndDiagnosticsRemainReadOnlyAndHostVisible()
    {
        Assert.Contains("ROOF_PURLIN_PREVIEW owner=", Command);
        Assert.Contains("boundaryIdentity=", Command);
        Assert.Contains("result=invalid reason=", Command);
        Assert.Contains("result=blocked reason=", Command);
        Assert.Contains("result=ok", Command);
        Assert.Contains("viewModel.PreviewDiagnosticReason", Command);
        Assert.DoesNotContain("RoofBoundaryIdentityStore.Write", Command);
        Assert.DoesNotContain("EnsureBoundaryIdentity", Command);
    }

    [Fact]
    public void ModalDialogWiresLoadedAndDraftChangesToPreviewRefresh()
    {
        Assert.Contains("AcApplication.ShowModalWindow(activeWindow)", Command);
        Assert.Contains("activeWindow.PreviewRequested += RefreshPreview", Command);
        Assert.Contains("activeWindow.PreviewRequested -= RefreshPreview", Command);
        Assert.Contains("Loaded += Window_Loaded", WindowCodeBehind);
        Assert.Contains("ViewModel.PreviewChanged += ViewModel_PreviewChanged", WindowCodeBehind);
        Assert.Contains("PreviewRequested?.Invoke", WindowCodeBehind);
    }

    [Fact]
    public void PreviewUsesTransientLinesAndDeterministicallyErasesAndDisposesThem()
    {
        Assert.Contains("ShowAutomaticPurlins", Transient);
        Assert.Contains("TransientManager.CurrentTransientManager", Transient);
        Assert.Contains("transientManager.AddTransient", Transient);
        Assert.Contains("EraseTransient", Transient);
        Assert.Contains("drawable.Dispose()", Transient);
        Assert.Contains("DocumentToBeDestroyed", Transient);
        Assert.DoesNotContain("AppendEntity", Transient);
    }

    [Fact]
    public void S6aWindowKeepsPreviewAndCancelAndAddsModeGatedApply()
    {
        Assert.Contains("x:Name=\"PreviewButton\"", Window);
        Assert.Contains("x:Name=\"CancelButton\"", Window);
        Assert.Contains("x:Name=\"ApplyButton\"", Window);
        Assert.Contains("Binding CanApply", Window);
        Assert.Contains("Binding IsProductionEdit", Window);
        Assert.Contains("Value=\"Collapsed\"", Window);
        Assert.Contains("AutomaticPurlin_Apply", Window);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", Window);
    }

    [Fact]
    public void P1GWindowUsesDynamicRoofSectionAndEditableDimensions()
    {
        Assert.Contains("AutomaticPurlinRoofSectionView", Window);
        Assert.Contains("Binding SectionPresentation", Window);
        Assert.Contains("AutomaticPurlin_Width", Window);
        Assert.Contains("AutomaticPurlin_Height", Window);
        Assert.Contains("RidgeWidthText", Window);
        Assert.Contains("RidgeHeightText", Window);
        Assert.Contains("WidthText", Window);
        Assert.Contains("HeightText", Window);
        Assert.Contains("x:Name=\"WallPlateEnabledCheckBox\"", Window);
        Assert.Contains("x:Name=\"RidgeEnabledCheckBox\"", Window);
        Assert.DoesNotContain("x:Name=\"SchematicCard\"", Window);
        Assert.DoesNotContain("x:Name=\"SchematicBaseImage\"", Window);
        Assert.DoesNotContain("x:Name=\"SchematicHighlightOverlay\"", Window);
        Assert.Contains("PreferredWidth = 1700", WindowCodeBehind);
        Assert.Contains("PreferredHeight = 820", WindowCodeBehind);
        Assert.Contains("PreferredMinimumWidth = 1400", WindowCodeBehind);
        Assert.Contains("PreferredMinimumHeight = 680", WindowCodeBehind);
        Assert.Contains("ColumnDefinition Width=\"680\" MinWidth=\"640\"", Window);
        Assert.Contains("ColumnDefinition Width=\"*\" MinWidth=\"600\"", Window);
        Assert.Contains("Design/RoofGeometryVisuals.xaml", Window);
        Assert.Contains("x:Name=\"HeaderBand\"", Window);
        Assert.Contains("x:Name=\"FooterBand\"", Window);
        Assert.Contains("WoodOakDarkBrush", Window);
        Assert.Contains("RoofGeometrySchematicBackgroundBrush", Window);
        Assert.Contains("AutomaticPurlinSectionSvgTemplate.CreateMasterScene", SectionView);
        Assert.Contains("CreateCompactRequest", SectionView);
        Assert.Contains("member.TopText", SectionView);
        Assert.Contains("member.BottomText", SectionView);
        var topIndex = SectionView.IndexOf("member.TopText", StringComparison.Ordinal);
        var bottomIndex = SectionView.IndexOf("member.BottomText", StringComparison.Ordinal);
        Assert.True(topIndex >= 0 && bottomIndex > topIndex);
        Assert.DoesNotContain("member.AxisText", SectionView);
        Assert.Contains("AutomaticPurlin_SectionTopAbbrev", SectionPresentation);
        Assert.DoesNotContain("AutomaticPurlin_SectionAxisAbbrev", SectionPresentation);
        Assert.Contains("ApplySchematicSeating", SectionPresentation);
        Assert.Contains("DefaultWallPlateSeatingPercent", SectionPresentation);
        Assert.Contains("RafterWidthMm", ViewModel);
        Assert.Contains("RafterHeightDimensionText", SectionPresentation);
        Assert.Contains("DrawRafterDimension", SectionView);
        Assert.Contains("RafterAnnotationStyleFamily", SectionView);
        Assert.Contains("RafterLabelBelowBoxMultiples = 3d", SectionView);
        Assert.Contains("RafterSectionDimensionText", SectionView);
        Assert.Contains("preferBelow", SectionView);
        Assert.Contains("RoofAutomaticPurlinGeneratorRole.Ridge", SectionView);
        Assert.Contains("LabelOffsetPx = LabelBaseOffsetPx + LabelHeightPx", SectionView);
        Assert.Contains("TryResolveRafterUpperZMm", SectionPresentation);
        Assert.Contains("SideContactCornerOffsetXMm", SectionPresentation);
        Assert.Contains("TryResolveCornerContactTopZMm", SectionPresentation);
        Assert.Contains("seating <= rafterHeightMm", SectionPresentation);
        Assert.Contains("seatingValue > 100d", ViewModel);
        Assert.Contains("CreateNewDraftDefaults", ViewModel);
        Assert.Contains("!layoutExists || layout is null", ViewModel);
        Assert.Contains("SourceEavePlane", ViewModel);
        Assert.Contains("_explicitReferenceLocalZMm", ViewModel);
        Assert.Contains("InconsistentSourceEaveLocalZ", ViewModel);
        Assert.Contains("InconsistentSourceEaveLocalZ", Command);
        Assert.Contains("InconsistentSourceEaveLocalZ", Workflow);
        Assert.DoesNotContain("_loadedReferenceLocalZMm", ViewModel);
        Assert.DoesNotContain("_referenceLocalZEdited", ViewModel);
        Assert.Contains("SelectedRidgeSeatingMode", ViewModel);
        Assert.Contains("PurlinNumericTextBoxStyle", Window);
        Assert.Contains("Property=\"MinWidth\"", Window);
        Assert.Contains("Value=\"88\"", Window);
        Assert.Contains("PurlinValidatedTextBoxStyle", Window);
        Assert.Contains("PurlinValidationBorderBrush", Window);
        Assert.Contains("TextAlignment.Center", SectionView);
    }

    [Fact]
    public void PreviewHideRestorePatternKeepsSameViewModel()
    {
        Assert.Contains("IsSuspendedForCadPreview", WindowCodeBehind);
        Assert.Contains("SavedRestoreBounds", WindowCodeBehind);
        Assert.Contains("ApplyRestoreBounds", WindowCodeBehind);
        Assert.Contains("IsSuspendedForCadPreview", Workflow);
        Assert.Contains("AutomaticPurlin_PreviewReturnPrompt", Workflow);
        Assert.Contains("new AutomaticPurlinDialogWindow(viewModel, theme)", Workflow);
        Assert.Contains("IsSuspendedForCadPreview", Command);
        Assert.Contains("AutomaticPurlin_PreviewReturnPrompt", Command);
        Assert.Contains("new AutomaticPurlinDialogWindow(viewModel, theme)", Command);
    }

    [Fact]
    public void SectionPresentationConsumesPlanWithoutPlacementSolverTypes()
    {
        Assert.Contains("RoofAutomaticPurlinPlan", SectionPresentation);
        Assert.Contains("ElevationProfile", SectionPresentation);
        Assert.DoesNotContain("RoofAutomaticPurlinPlanner.Create", SectionPresentation);
        Assert.DoesNotContain("RoofAutomaticPurlinPlanner.Create", SectionView);
        Assert.DoesNotContain("HipRoofGeometrySolver", SectionPresentation);
        Assert.DoesNotContain("HipRoofGeometrySolver", SectionView);
        Assert.Contains("SectionPresentation", ViewModel);
        Assert.Contains("AutomaticPurlinSectionPresentation.Create", ViewModel);
    }

    [Fact]
    public void SectionPresentationUsesActualSectionIntersectionsRatherThanMemberProjection()
    {
        Assert.Contains("IntersectFace", SectionPresentation);
        Assert.Contains("TryIntersectSegmentWithSectionPlane", SectionPresentation);
        Assert.Contains("AddIntersections", SectionPresentation);
        Assert.Contains("Show one physical body per role, row and side", SectionPresentation);
        Assert.DoesNotContain("Midpoint(item.Segment3D.Start", SectionPresentation);
        Assert.Contains("IReadOnlyList<AutomaticPurlinSectionRafterMm> Rafters", SectionPresentation);
        Assert.Contains("IReadOnlyList<AutomaticPurlinSectionWallMm> Walls", SectionPresentation);
        Assert.Contains("AutomaticPurlinSectionSide", SectionPresentation);
        Assert.Contains("VisibleRafterOverlapZMm", SectionPresentation);
        Assert.Contains("presentation.Rafters", SectionSvgTemplate);
        Assert.Contains("presentation.Walls", SectionSvgTemplate);
    }

    [Fact]
    public void SectionRendererUsesNativeSvgDerivedSemanticTemplates()
    {
        Assert.Contains("rafter-left", SectionSvgTemplate);
        Assert.Contains("rafter-left_x002c_", SectionSvgTemplate);
        Assert.Contains("rafter-right", SectionSvgTemplate);
        Assert.Contains("wall-left", SectionSvgTemplate);
        Assert.Contains("wall-right", SectionSvgTemplate);
        Assert.Contains("wallplate-left", SectionSvgTemplate);
        Assert.Contains("wallplate-right", SectionSvgTemplate);
        Assert.Contains("purlin-left-1", SectionSvgTemplate);
        Assert.Contains("purlin-right-1", SectionSvgTemplate);
        Assert.Contains("ridge-purlin", SectionSvgTemplate);
        Assert.Contains("MasterViewBox", SectionSvgTemplate);
        Assert.Contains("CreateMasterScene", SectionSvgTemplate);
        Assert.Contains("Geometry.Parse", SectionSvgTemplate);
        Assert.Contains("AddPhysicalRafterDrawing", SectionSvgTemplate);
        Assert.Contains("TryGetRenderedRafterViewPolygon", SectionSvgTemplate);
        Assert.DoesNotContain("CreateAffine", SectionSvgTemplate);
        Assert.DoesNotContain("SvgDocument", SectionSvgTemplate);
        Assert.DoesNotContain("BitmapImage", SectionSvgTemplate);
        Assert.DoesNotContain("HipRoofGeometrySolver", SectionSvgTemplate);
        Assert.DoesNotContain("RoofAutomaticPurlinPlanner.Create", SectionSvgTemplate);
    }

    [Fact]
    public void S5bWindowExposesSeatingOnlyForApplicableDraftModes()
    {
        // Intended behavior: seating editors are shown only when IsSeatingApplicable,
        // otherwise the status text is shown. Markers use Tag (not x:Name) so dynamic
        // editor templates stay reusable; WPF tests resolve the same Tags at runtime.
        Assert.Contains("Tag=\"SeatingControlsPanel\"", Window);
        Assert.Contains("Tag=\"HeightModeSeatingStatus\"", Window);
        Assert.Contains(
            "DataTrigger Binding=\"{Binding IsSeatingApplicable}\" Value=\"True\"",
            Window);
        Assert.Contains(
            "DataTrigger Binding=\"{Binding IsSeatingApplicable}\" Value=\"False\"",
            Window);
        Assert.Contains(
            "SelectedValue=\"{Binding SelectedSeatingMode, Mode=TwoWay}\"",
            Window);
        Assert.Contains(
            "Text=\"{Binding SeatingDepthValueText, UpdateSourceTrigger=PropertyChanged}\"",
            Window);
        Assert.Contains("Text=\"{Binding SeatingStatusText, Mode=OneWay}\"", Window);
        Assert.Contains("AutomaticPurlin_RoofPlaneElevation", Window);
        Assert.Contains("AutomaticPurlin_SeatingDepth", Window);
        Assert.DoesNotContain("AutomaticPurlin_SeatingMethod", Window);
        Assert.DoesNotContain("AutomaticPurlin_PlanPosition", Window);
        Assert.DoesNotContain("IsEnabled=\"False\"", Window);
        Assert.DoesNotContain("x:Name=\"SeatingControlsPanel\"", Window);
        Assert.DoesNotContain("x:Name=\"SeatingModeComboBox\"", Window);
        Assert.DoesNotContain("x:Name=\"SeatingValueTextBox\"", Window);
    }

    [Fact]
    public void TechnicalRoofPlane_UsesPhysicalCoreRules_NotSvgAffine()
    {
        Assert.Contains("RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem", ViewModel);
        Assert.Contains("RefreshTechnicalRoofPlaneValues", ViewModel);
        Assert.DoesNotContain(
            "TryResolveVisualRoofPlaneLocalZMm",
            ViewModel);
        Assert.DoesNotContain(
            "TryResolveRoleRoofPlaneRelativeMm",
            ViewModel);
        Assert.Contains(
            "Technical inspector values must use",
            SectionPresentation);
    }

    private static string Read(params string[] relativeSegments)
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            new[] { root, "src", "AcKrovy.AutoCAD" }.Concat(relativeSegments).ToArray());
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "AcKrovy.AutoCAD")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
