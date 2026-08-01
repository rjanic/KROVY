using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class SettingsTargetedFeatureTests
{
    [Fact]
    [Trait("Feature", "AciPicker")]
    public void AciCommit_TransfersPendingToSelected()
    {
        var state = new AciColorPickerState();
        state.Open(1);
        Assert.True(state.TrySetPending(142));
        Assert.True(state.Commit());
        Assert.Equal(142, state.SelectedAciIndex);
        Assert.Equal(142, state.OriginalAciIndex);
    }

    [Fact]
    [Trait("Feature", "AciPicker")]
    public void AciCancel_RestoresOriginalWithoutCommitting()
    {
        var state = new AciColorPickerState();
        state.Open(1);
        Assert.True(state.TrySetPending(142));
        state.Cancel();
        Assert.Equal((1, 1, 1), (
            state.OriginalAciIndex,
            state.PendingAciIndex,
            state.SelectedAciIndex));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    [Trait("Feature", "AciPicker")]
    public void AciPending_RejectsValuesOutsideClassicPalette(int index)
    {
        var state = new AciColorPickerState();
        state.Open(1);
        Assert.False(state.TrySetPending(index));
        Assert.Equal(1, state.PendingAciIndex);
    }

    [Fact]
    [Trait("Feature", "AciPicker")]
    public void AciPalette_BasicBandContainsExactlyOneThroughNine()
    {
        Assert.Equal(Enumerable.Range(1, 9), AciColorPickerRules.BasicIndices);
    }

    [Fact]
    [Trait("Feature", "AciPicker")]
    public void AciPalette_MainBandContainsExactlyTenThroughTwoHundredFortyNine()
    {
        Assert.Equal(Enumerable.Range(10, 240), AciColorPickerRules.MainPaletteIndices);
        Assert.Equal(240, AciColorPickerRules.MainPaletteIndices.Count);
    }

    [Fact]
    [Trait("Feature", "AciPicker")]
    public void AciPalette_MainBandIsTwentyFourByTen()
    {
        Assert.Equal(24, AciColorPickerRules.MainPaletteColumns);
        Assert.Equal(10, AciColorPickerRules.MainPaletteRows);
        Assert.Equal(
            AciColorPickerRules.MainPaletteColumns * AciColorPickerRules.MainPaletteRows,
            AciColorPickerRules.MainPaletteIndices.Count);
    }

    [Fact]
    [Trait("Feature", "AciPicker")]
    public void AciPalette_GrayscaleBandContainsExactlyTwoHundredFiftyThroughTwoHundredFiftyFive()
    {
        Assert.Equal(Enumerable.Range(250, 6), AciColorPickerRules.GrayscaleIndices);
    }

    [Fact]
    [Trait("Feature", "AciPicker")]
    public void AciPalette_ExcludesByBlockAndByLayer()
    {
        Assert.DoesNotContain(0, AllPickerIndices());
        Assert.DoesNotContain(256, AllPickerIndices());
    }

    [Fact]
    [Trait("Feature", "AciPicker")]
    public void AciModal_WiresOwnedShowDialogConfirmCancelEnterAndEscape()
    {
        var xaml = File.ReadAllText(Path.Combine(UiDirectory(), "AciColorPicker.xaml"));
        var code = File.ReadAllText(Path.Combine(UiDirectory(), "AciColorPicker.xaml.cs"));
        var windowXaml = File.ReadAllText(Path.Combine(
            UiDirectory(),
            "AciColorPickerWindow.xaml"));
        var windowCode = File.ReadAllText(Path.Combine(
            UiDirectory(),
            "AciColorPickerWindow.xaml.cs"));

        Assert.Contains("new AciColorPickerWindow", code);
        Assert.Contains("Owner = owner", code);
        Assert.Contains("dialog.ShowDialog() == true", code);
        Assert.Contains("SelectedAciIndexProperty", code);
        Assert.Contains("BindingOperations.GetBindingExpression", code);
        Assert.Contains("x:Name=\"ConfirmButton\"", windowXaml);
        Assert.Contains("Click=\"Confirm_Click\"", windowXaml);
        Assert.Contains("Click=\"Cancel_Click\"", windowXaml);
        Assert.Contains("WindowStartupLocation=\"CenterOwner\"", windowXaml);
        Assert.Contains("public int OriginalAciIndex", windowCode);
        Assert.Contains("public int PendingAciIndex", windowCode);
        Assert.Contains("DialogResult = true", windowCode);
        Assert.Contains("DialogResult = false", windowCode);
        Assert.Contains("Key.Enter", windowCode);
        Assert.Contains("Key.Escape", windowCode);
        Assert.DoesNotContain("<Popup", xaml + windowXaml);
        Assert.DoesNotContain("PreviewMouseDown +=", code + windowCode);
    }

    [Fact]
    [Trait("Feature", "ExistingLayerHydration")]
    public void ExistingLayerScale_UniformSmartEntitiesLoadTheirScale()
    {
        var result = CadLayerScaleHydrationRules.Resolve(0.5, [0.75, 0.75]);

        Assert.Equal(0.75, result.Value);
        Assert.True(result.LoadedFromEntities);
        Assert.False(result.HasMixedValues);
    }

    [Fact]
    [Trait("Feature", "ExistingLayerHydration")]
    public void ExistingLayerScale_NoSmartEntitiesPreservesProfileScale()
    {
        var result = CadLayerScaleHydrationRules.Resolve(0.5, []);

        Assert.Equal(0.5, result.Value);
        Assert.False(result.LoadedFromEntities);
        Assert.False(result.HasMixedValues);
    }

    [Fact]
    [Trait("Feature", "ExistingLayerHydration")]
    public void ExistingLayerScale_MixedSmartEntitiesPreserveProfileScale()
    {
        var result = CadLayerScaleHydrationRules.Resolve(0.5, [0.5, 0.75]);

        Assert.Equal(0.5, result.Value);
        Assert.False(result.LoadedFromEntities);
        Assert.True(result.HasMixedValues);
    }

    [Fact]
    [Trait("Feature", "ExistingLayerHydration")]
    public void MixedScaleInformation_IsLocalizedInEverySupportedLanguage()
    {
        foreach (var language in new[] { "sk", "cs", "en", "de", "pl", "fr" })
        {
            Assert.False(string.IsNullOrWhiteSpace(UiStrings.GetString(
                "SettingsWindow_Layers_MixedLinetypeScale",
                CultureInfo.GetCultureInfo(language))));
        }
    }

    [Fact]
    [Trait("Feature", "SuffixApply")]
    public void SelectedExistingLayer_WithoutOverridesDoesNotRequireSuffix()
    {
        var intent = new CadLayerOverrideIntent(
            TimberElementType.Rafter,
            "KROKVA",
            hasPropertyOverrides: false);

        Assert.False(CadLayerOverrideRules.RequiresSuffix(
            "krokva",
            physicalLayerMatchesRequestedAppearance: false,
            intent));
    }

    [Theory]
    [InlineData(TimberElementType.Rafter)]
    [Trait("Feature", "SuffixApply")]
    public void SelectedExistingLayer_WithPropertyOverrideRequiresSuffix(
        TimberElementType elementType)
    {
        var intent = new CadLayerOverrideIntent(
            elementType,
            "KROKVA",
            hasPropertyOverrides: true);

        Assert.True(CadLayerOverrideRules.RequiresSuffix(
            "KROKVA",
            physicalLayerMatchesRequestedAppearance: true,
            intent));
    }

    [Fact]
    [Trait("Feature", "SuffixApply")]
    public void ExistingUntrackedCollision_UsesNormalizedPhysicalAppearance()
    {
        Assert.False(CadLayerOverrideRules.RequiresSuffix(
            "KROKVA",
            physicalLayerMatchesRequestedAppearance: true,
            intent: null));
        Assert.True(CadLayerOverrideRules.RequiresSuffix(
            "KROKVA",
            physicalLayerMatchesRequestedAppearance: false,
            intent: null));
    }

    [Fact]
    [Trait("Feature", "LayerNames")]
    public void LayerCatalog_ContainsRequiredLocalNamesAndFiltersUnsafeRecords()
    {
        var result = CadLayerNameRules.SelectUsableLocalNames(
        [
            new("USER_B"),
            new("xref|KROKVA", isXrefDependent: true),
            new("ERASED", isErased: true),
            new("user_a"),
        ]);

        Assert.Equal(["0", "Defpoints", "user_a", "USER_B"], result);
        Assert.DoesNotContain("xref|KROKVA", result);
        Assert.DoesNotContain("ERASED", result);
    }

    [Fact]
    [Trait("Feature", "LayerNames")]
    [Trait("Feature", "SuffixApply")]
    public void LayerSuffix_FirstConflictUsesTwoDigitOne()
    {
        Assert.Equal(
            "KROKVA_01",
            CadLayerNameRules.NextConflictFreeName("KROKVA", ["KROKVA"]));
    }

    [Fact]
    [Trait("Feature", "LayerNames")]
    [Trait("Feature", "SuffixApply")]
    public void LayerSuffix_OccupiedOneUsesTwo()
    {
        Assert.Equal(
            "KROKVA_02",
            CadLayerNameRules.NextConflictFreeName(
                "KROKVA",
                ["KROKVA", "krokva_01"]));
    }

    [Fact]
    [Trait("Feature", "LayerNames")]
    [Trait("Feature", "SuffixApply")]
    public void LayerSuffix_DoesNotChainRecognizedGeneratedSuffix()
    {
        Assert.Equal(
            "KROKVA_02",
            CadLayerNameRules.NextConflictFreeName(
                "KROKVA_01",
                ["KROKVA", "KROKVA_01"]));
    }

    [Theory]
    [InlineData("KROV_CUSTOM_14", "KROV_CUSTOM")]
    [InlineData("KROV_CUSTOM_14_01", "KROV_CUSTOM")]
    [InlineData("KROV_CUSTOM", "KROV_CUSTOM")]
    [Trait("Feature", "LayerNames")]
    [Trait("Feature", "SuffixApply")]
    public void LayerSuffix_UsesStableCanonicalBase(
        string layerName,
        string expectedBaseName)
    {
        Assert.Equal(
            expectedBaseName,
            CadLayerNameRules.GetCanonicalBaseName(layerName));
    }

    [Fact]
    [Trait("Feature", "LayerNames")]
    [Trait("Feature", "SuffixApply")]
    public void LayerSuffix_FamilyIncludesExistingGeneratedVariants()
    {
        Assert.True(CadLayerNameRules.IsCanonicalOrGeneratedVariant(
            "KROV_CUSTOM_01",
            "KROV_CUSTOM"));
        Assert.True(CadLayerNameRules.IsCanonicalOrGeneratedVariant(
            "KROV_CUSTOM_14",
            "KROV_CUSTOM_01"));
        Assert.False(CadLayerNameRules.IsCanonicalOrGeneratedVariant(
            "KROKVA_01",
            "KROV_CUSTOM"));
    }

    [Fact]
    [Trait("Feature", "LayerNames")]
    [Trait("Feature", "SuffixApply")]
    public void LayerSuffix_ContinuesBeyondNinetyNine()
    {
        var occupied = new[] { "KROKVA" }
            .Concat(Enumerable.Range(1, 99).Select(index => $"KROKVA_{index:D2}"));
        Assert.Equal(
            "KROKVA_100",
            CadLayerNameRules.NextConflictFreeName("KROKVA", occupied));
    }

    [Fact]
    [Trait("Feature", "LayerNames")]
    public void LayerNameCell_IsEditableComboBoxBoundOnlyToName()
    {
        var document = XDocument.Load(Path.Combine(UiDirectory(), "LayerSettingsWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var combo = document.Descendants(presentation + "ComboBox")
            .Single(element =>
                ((string?)element.Attribute("Text"))?.Contains("LayerName") == true);

        Assert.Equal("True", (string?)combo.Attribute("IsEditable"));
        Assert.Contains("LayerNameOptions", (string?)combo.Attribute("ItemsSource"));
        Assert.Contains("Mode=TwoWay", (string?)combo.Attribute("Text"));
        Assert.Contains("UpdateSourceTrigger=LostFocus", (string?)combo.Attribute("Text"));
        Assert.Contains("LayerName", (string?)combo.Attribute("SelectedItem"));
        Assert.Contains("Mode=OneWay", (string?)combo.Attribute("SelectedItem"));
        Assert.Equal(
            "False",
            (string?)combo.Attribute("IsSynchronizedWithCurrentItem"));
        Assert.Equal(
            "LayerNameComboBox_LostKeyboardFocus",
            (string?)combo.Attribute("LostKeyboardFocus"));
        Assert.DoesNotContain("ObjectId", combo.ToString());
    }

    [Fact]
    [Trait("Feature", "ExistingLayerHydration")]
    public void ExistingLayerSelection_IsHydrationOnlyWithoutCadWrite()
    {
        var source = File.ReadAllText(Path.Combine(
            UiDirectory(),
            "LayerSettingsWindow.xaml.cs"));
        var start = source.IndexOf(
            "private void LayerNameComboBox_SelectionChanged",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private void LayerSettingsWindow_Loaded",
            start,
            StringComparison.Ordinal);
        var handler = source.Substring(start, end - start);

        Assert.Contains("HydrateFromExistingLayer", handler);
        Assert.Contains("HydrateExistingLayer(row, selectedName)", handler);
        Assert.DoesNotContain("EnsureLayer", handler);
        Assert.DoesNotContain("Transaction", handler);
        Assert.DoesNotContain("ApplySettings", handler);
    }

    [Fact]
    [Trait("Feature", "LayerNames")]
    [Trait("Feature", "SuffixApply")]
    public void NewOnlyConflictResolution_UsesOneTransactionWritePath()
    {
        var command = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AcKrovyCommands.cs"));
        var service = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "TimberLayerService.cs"));

        Assert.Contains("ResolveNewElementsOnlyProfile", command);
        Assert.Contains("using (document.LockDocument())", command);
        Assert.Contains("transaction.Commit();", command);
        Assert.Contains("CadLayerUpdateMode.UpdateExisting", service);
        Assert.Contains("createdLayerNames.Count == 0", command);
    }

    [Fact]
    [Trait("Feature", "SuffixApply")]
    public void LayerResolution_UsesExactManualNameAndRequestedAppearance()
    {
        var service = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "TimberLayerService.cs"));
        var branch = service.Substring(
            service.IndexOf("if (!table.Has(style.LayerName))", StringComparison.Ordinal),
            service.IndexOf(
                "var existing = (LayerTableRecord)",
                StringComparison.Ordinal) -
            service.IndexOf("if (!table.Has(style.LayerName))", StringComparison.Ordinal));

        Assert.Contains("style.LayerName", branch);
        Assert.Contains("style.ColorIndex", branch);
        Assert.Contains("style.LinetypeName", branch);
        Assert.Contains("CloneStyle(style, style.LayerName)", branch);
        Assert.DoesNotContain("NextConflictFreeName", branch);
    }

    [Fact]
    [Trait("Feature", "SuffixApply")]
    public void LayerResolution_ReusesMatchingCanonicalFamilyBeforeCreatingSuffix()
    {
        var service = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "TimberLayerService.cs"));

        Assert.Contains("IsCanonicalOrGeneratedVariant", service);
        Assert.Contains("LayerPresetMatches", service);
        Assert.Contains("persistedScaleByLayer", service);
        Assert.Contains("CloneStyle(style, matchingPreset.Name)", service);
        Assert.Contains(
            "CadLayerNameRules.GetCanonicalBaseName",
            service);
    }

    [Fact]
    [Trait("Feature", "SuffixApply")]
    public void ResetDefaults_UsesCanonicalCustomLayerAndRebuildsBaselines()
    {
        Assert.Equal(
            "KROV_CUSTOM",
            ElementLayerProfile.CreateDefault()
                .GetStyle(TimberElementType.Custom)
                .LayerName);

        var source = File.ReadAllText(Path.Combine(
            UiDirectory(),
            "LayerSettingsWindow.xaml.cs"));
        var start = source.IndexOf(
            "private void RestoreDefaults_Click",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private void SaveNewElements_Click",
            start,
            StringComparison.Ordinal);
        var handler = source.Substring(start, end - start);

        Assert.Contains("ElementLayerProfile.CreateDefault()", handler);
        Assert.Contains("InitializeExistingLayerBaselines()", handler);
    }

    [Fact]
    [Trait("Feature", "SuffixApply")]
    public void SelectionAndAllApplyOnlyTraverseSmartTimberEntities()
    {
        var command = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AcKrovyCommands.cs"));
        var applyBranch = command.Substring(
            command.IndexOf(
                "private static SettingsDrawingApplyResult ApplySettingsToExistingElements",
                StringComparison.Ordinal));

        Assert.Contains("targetIds is null", applyBranch);
        Assert.Contains("DrawingScanner.FindAllTimberElements", applyBranch);
        Assert.Contains("targetIds.Distinct()", applyBranch);
        Assert.Contains("AutoCadEntityHelpers.IsSupportedTimberGeometry", applyBranch);
        Assert.Contains("metadataStore.TryRead", applyBranch);
        Assert.Contains("TimberAnnotationSettingsApplicator.Apply", applyBranch);
        Assert.DoesNotContain("ApplyLayerForTimberType", applyBranch);
    }

    [Fact]
    [Trait("Feature", "Selection")]
    public void AnnotationFooterScopesAreIsolatedAndApplyAllUsesOneRefreshBatch()
    {
        var window = File.ReadAllText(Path.Combine(
            UiDirectory(),
            "LayerSettingsWindow.xaml.cs"));
        var command = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AcKrovyCommands.cs"));
        var applyStart = command.IndexOf(
            "private static SettingsApplyResponse ApplySettingsFromWindow",
            StringComparison.Ordinal);
        var applyEnd = command.IndexOf(
            "private static SettingsApplyResponse SettingsResponse",
            applyStart,
            StringComparison.Ordinal);
        var applyWorkflow = command.Substring(applyStart, applyEnd - applyStart);

        var selectionStart = window.IndexOf(
            "private void SaveApplySelection_Click",
            StringComparison.Ordinal);
        var selectionEnd = window.IndexOf(
            "private void SaveApplyAll_Click",
            selectionStart,
            StringComparison.Ordinal);
        var selectionHandler = window.Substring(
            selectionStart,
            selectionEnd - selectionStart);
        Assert.Contains("TimberAnnotationSettingsApplyScope.SelectedElements", selectionHandler);
        Assert.DoesNotContain("SettingsSectionScope.Layers", selectionHandler);
        Assert.Contains("TimberAnnotationSettingsApplyScope.AllElements", window);
        Assert.Contains("scope.HasFlag(SettingsSectionScope.Layers)", window);
        Assert.DoesNotContain("ApplyDrawingScale", applyWorkflow);
        Assert.DoesNotContain("preserveInheritedDrawingScale", applyWorkflow);
        Assert.Contains("annotationSettings", applyWorkflow);
        Assert.Contains("request.LayerProfileChanged", applyWorkflow);
        Assert.Contains(
            "SettingsSaveMode.LanguageOnly or SettingsSaveMode.SelectedElements",
            applyWorkflow);
        Assert.Contains("if (applyLayerProfileChange)", applyWorkflow);
        Assert.Contains("if (request.DefaultProfileChanged", applyWorkflow);
        Assert.DoesNotContain("RefreshAllAnnotationsAfterScaleChange", command);
        var existingApply = command.Substring(command.IndexOf(
            "private static SettingsDrawingApplyResult ApplySettingsToExistingElements",
            StringComparison.Ordinal));
        Assert.Equal(1, existingApply.Split(
            "UpdateLabelsForChangedEntities(",
            StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("ElementLabelService.UpdateAll", existingApply);
        Assert.True(
            applyWorkflow.IndexOf("selection.Status != SettingsSelectionStatus.Selected", StringComparison.Ordinal) <
            applyWorkflow.IndexOf("TimberElementDefaultProfileStore.Save", StringComparison.Ordinal));
        Assert.Contains("profileAccepted: true", applyWorkflow);
    }

    [Fact]
    [Trait("Feature", "Settings")]
    public void SettingsWindow_OwnsAutoCadMainWindowWithCenterScreenFallback()
    {
        var command = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AcKrovyCommands.cs"));
        var owner = File.ReadAllText(Path.Combine(
            UiDirectory(),
            "SettingsWindowOwner.cs"));
        var settingsWindow = File.ReadAllText(Path.Combine(
            UiDirectory(),
            "LayerSettingsWindow.xaml.cs"));

        Assert.Contains("AcApp.MainWindow?.Handle", command);
        Assert.Contains("SettingsWindowOwner.TryAssign", command);
        Assert.Contains("WindowStartupLocation.CenterOwner", owner);
        Assert.Contains("WindowStartupLocation.CenterScreen", owner);
        Assert.Contains("GetWindowRect", owner);
        Assert.Contains("MonitorFromWindow", owner);
        Assert.Contains("SetWindowPos", owner);
        Assert.Contains("EnsureHandle", owner);
        Assert.Contains("WindowPositionChanging", owner);
        Assert.Contains("source.AddHook", owner);
        Assert.Contains("PreserveCurrentPosition", owner);
        Assert.Contains(
            "SettingsWindowOwner.RunWithPreservedPlacement",
            settingsWindow);
        Assert.Contains("CapturePlacement(window)", owner);
        Assert.Contains("ScheduleDeferredRestore(window, snapshot, placementGuard)", owner);
        Assert.Contains("RestoreShowAndActivate(window, snapshot)", owner);
        Assert.Contains("finally", owner);
        Assert.Contains("MonitorFromPoint", owner);
        Assert.DoesNotContain("PrepareInitialPlacement(window", settingsWindow);
        Assert.Contains("window.ContentRendered", owner);
        Assert.Contains("DispatcherPriority.ApplicationIdle", owner);
        Assert.Contains("DispatcherPriority.ContextIdle", owner);
        Assert.Contains("placementGuard.Dispose()", owner);
        Assert.DoesNotContain("DispatcherTimer", owner);
        Assert.DoesNotContain("window.Opacity = 0d", owner);
        Assert.DoesNotMatch(
            @"window\.(Left|Top)\s*=\s*-?\d",
            owner);
    }

    [Fact]
    [Trait("Feature", "LayerNames")]
    [Trait("Feature", "SuffixApply")]
    public void NewLayerBanners_AreLocalizedWithMatchingPlaceholders()
    {
        foreach (var language in new[] { "sk", "cs", "en", "de", "pl", "fr" })
        {
            var culture = CultureInfo.GetCultureInfo(language);
            Assert.Contains(
                "{0}",
                UiStrings.GetString("SettingsWindow_NewLayerCreatedFormat", culture));
            Assert.Contains(
                "{0}",
                UiStrings.GetString("SettingsWindow_NewLayersCreatedFormat", culture));
            Assert.False(string.IsNullOrWhiteSpace(
                UiStrings.GetString("SettingsWindow_Layers_LayerNamePrompt", culture)));
        }
    }

    [Fact]
    [Trait("Feature", "NoAnnotations")]
    public void NoAnnotations_IsStableAndUsesNoMainRepresentation()
    {
        Assert.Equal(
            TimberAnnotationMode.NoAnnotations,
            TimberAnnotationModeRules.Normalize(TimberAnnotationMode.NoAnnotations));
        Assert.Equal(
            TimberMainAnnotationRepresentation.None,
            TimberAnnotationModeRules.GetRepresentation(
                TimberAnnotationMode.NoAnnotations,
                ItemNumberLeaderStyle.Circle));
    }

    [Theory]
    [InlineData(TimberElementType.Rafter, 35d)]
    [InlineData(TimberElementType.Post, 0d)]
    [Trait("Feature", "NoAnnotations")]
    public void NoAnnotations_SuppressesMainSlopeAndPostPlans(
        TimberElementType type,
        double slope)
    {
        var data = TimberElementDefaults.For(type) with
        {
            AnnotationMode = TimberAnnotationMode.NoAnnotations,
            SlopeDegrees = slope,
        };
        var plan = TimberAnnotationRefreshPlanner.Create(data);

        Assert.False(plan.EnsureLabel);
        Assert.False(plan.ReconcileSlopeArrow);
        Assert.False(plan.ReconcileSlopeAngleText);
        Assert.False(plan.ShouldSlopeArrowExist);
        Assert.False(plan.ShouldHorizontalSlopeMarkerExist);
        Assert.False(plan.ShouldPostPerpendicularMarkerExist);
        Assert.False(plan.ShouldSlopeAngleTextExist);
    }

    [Fact]
    [Trait("Feature", "NoAnnotations")]
    public void NoAnnotations_PreservesIdentityNumberingAndManufacturingData()
    {
        var original = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            SchemaVersion = 4,
            ElementId = "K17",
            CuttingAllowanceMm = 125,
            ManualLengthMm = 4321,
            ItemNumberLeaderStyle = ItemNumberLeaderStyle.Slot,
        };
        var profile = new TimberElementDefaultProfile
        {
            DefaultAnnotationMode = TimberAnnotationMode.NoAnnotations,
            DefaultItemNumberLeaderStyle = ItemNumberLeaderStyle.Slot,
        };

        var updated = TimberElementDefaultApplicator.ApplyAnnotationMode(original, profile);

        Assert.Equal(4, updated.SchemaVersion);
        Assert.Equal("K17", updated.ElementId);
        Assert.Equal(125, updated.CuttingAllowanceMm);
        Assert.Equal(4321, updated.ManualLengthMm);
        Assert.Equal(ItemNumberLeaderStyle.Slot, updated.ItemNumberLeaderStyle);
    }

    [Fact]
    [Trait("Feature", "NoAnnotations")]
    public void NoAnnotations_RoundTripsWithoutSchemaBump()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
        };
        var source = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            SchemaVersion = 4,
            AnnotationMode = TimberAnnotationMode.NoAnnotations,
        };

        var json = JsonSerializer.Serialize(source, options);
        var loaded = JsonSerializer.Deserialize<TimberElementData>(json, options);

        Assert.Contains("\"SchemaVersion\":4", json);
        Assert.Contains("\"AnnotationMode\":\"NoAnnotations\"", json);
        Assert.Equal(TimberAnnotationMode.NoAnnotations, loaded!.AnnotationMode);
    }

    [Fact]
    [Trait("Feature", "NoAnnotations")]
    public void NoAnnotations_ServiceDeletesEverySourceBoundAnnotationFamily()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "TimberAnnotationService.cs"));
        var branch = source.Substring(
            source.IndexOf("TimberAnnotationMode.NoAnnotations", StringComparison.Ordinal),
            source.IndexOf("var isRectangularFootprintPost", StringComparison.Ordinal) -
            source.IndexOf("TimberAnnotationMode.NoAnnotations", StringComparison.Ordinal));

        Assert.Contains("ElementLabelService.DeleteForSourceHandle", branch);
        Assert.Contains("SlopeAnnotationService.DeleteForSourceHandle", branch);
        Assert.Contains("PostFootprintPerpendicularAnnotationService.DeleteForSourceHandle", branch);
        Assert.Contains("return false", branch);
    }

    [Fact]
    [Trait("Feature", "NoAnnotations")]
    public void ReturningFromNoAnnotationsRecreatesOneRequestedPlan()
    {
        var none = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            AnnotationMode = TimberAnnotationMode.NoAnnotations,
            SlopeDegrees = 35,
        };
        var full = none with { AnnotationMode = TimberAnnotationMode.FullLabel };

        Assert.False(TimberAnnotationRefreshPlanner.Create(none).EnsureLabel);
        Assert.True(TimberAnnotationRefreshPlanner.Create(full).EnsureLabel);
        Assert.Equal(
            TimberMainAnnotationRepresentation.FullLabel,
            TimberAnnotationModeRules.GetRepresentation(full.AnnotationMode));
    }

    [Fact]
    [Trait("Feature", "NoAnnotations")]
    public void NoAnnotations_HasLocalizedNameInEverySupportedLanguage()
    {
        var expected = new Dictionary<string, string>
        {
            ["sk"] = "Bez popisov",
            ["cs"] = "Bez popisů",
            ["en"] = "No annotations",
            ["de"] = "Ohne Beschriftungen",
            ["pl"] = "Bez opisów",
            ["fr"] = "Sans annotations",
        };

        foreach (var pair in expected)
        {
            Assert.Equal(
                pair.Value,
                TimberAnnotationModeDisplayNameProvider.GetDisplayName(
                    TimberAnnotationMode.NoAnnotations,
                    CultureInfo.GetCultureInfo(pair.Key)));
        }
    }

    [Fact]
    [Trait("Feature", "NoAnnotations")]
    public void NoAnnotations_PreservesLastStableFrameStyle()
    {
        var selection = SettingsLocalizedSelectionSet.Create(
            CultureInfo.GetCultureInfo("sk"),
            TimberAnnotationMode.NoAnnotations,
            ItemNumberLeaderStyle.Slot,
            SettingsSaveMode.NewElementsOnly);

        Assert.Equal(TimberAnnotationMode.NoAnnotations, selection.SelectedAnnotationMode);
        Assert.Equal(ItemNumberLeaderStyle.Slot, selection.SelectedItemNumberLeaderStyle);
    }

    [Fact]
    [Trait("Feature", "LanguageCards")]
    public void LanguageCards_AreSixStableCodesInTwoByThreeGridWithLocalFlags()
    {
        Assert.Equal(
            ["sk", "cs", "en", "de", "pl", "fr"],
            AppLanguageService.SupportedLanguages.Select(language => language.Code));

        var xaml = File.ReadAllText(Path.Combine(UiDirectory(), "LayerSettingsWindow.xaml"));
        var design = File.ReadAllText(Path.Combine(
            UiDirectory(),
            "Design",
            "SettingsDesignSystem.xaml"));
        var controls = File.ReadAllText(Path.Combine(
            UiDirectory(),
            "Design",
            "SettingsControls.xaml"));
        var mapping = File.ReadAllText(Path.Combine(
            UiDirectory(),
            "LanguageFlagResourceMap.cs"));
        var project = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "AcKrovy.AutoCAD.csproj"));
        var flagsDirectory = Path.Combine(UiDirectory(), "Assets", "Flags");

        Assert.Contains("<UniformGrid Rows=\"2\" Columns=\"3\" />", xaml);
        Assert.Contains("Text=\"{Binding UpperCode}\"", xaml);
        Assert.DoesNotContain("Text=\"{Binding NativeName}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{Binding AccessibilityName}\"", xaml);
        Assert.Contains("x:Name=\"LanguageSelector\"", xaml);
        Assert.Contains(
            "<Setter Property=\"Source\" Value=\"{Binding BlackFlagUri}\" />",
            xaml);
        Assert.Contains(
            "<Setter Property=\"Source\" Value=\"{Binding ColorFlagUri}\" />",
            xaml);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", xaml);
        Assert.Contains("VerticalAlignment=\"Stretch\"", xaml);
        Assert.Contains("Stretch=\"Fill\"", xaml);
        Assert.Contains("MinHeight=\"220\"", xaml);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", xaml);
        Assert.DoesNotContain("<Grid Width=\"150\"", xaml);
        Assert.DoesNotContain("Height=\"92\"", xaml);
        Assert.Contains("SettingsBadgeBackgroundBrush", xaml);
        Assert.Contains("SettingsBadgeTextBrush", xaml);
        Assert.Contains(
            "ItemContainerStyle=\"{StaticResource SettingsLanguageCardItemStyle}\"",
            xaml);
        Assert.Contains(
            "<Style x:Key=\"SettingsLanguageCardItemStyle\"",
            controls);
        Assert.Contains("<Setter Property=\"Padding\" Value=\"0\" />", controls);
        Assert.Contains("<Trigger Property=\"IsSelected\" Value=\"True\">", controls);
        Assert.Contains(
            "Property=\"BorderBrush\" Value=\"{DynamicResource SettingsAccentBrush}\"",
            controls);
        Assert.Contains(
            "Property=\"Background\" Value=\"{DynamicResource SettingsSelectedBrush}\"",
            controls);
        Assert.Contains("SettingsIconCheck", xaml);
        Assert.Contains("<Resource Include=\"UI\\Assets\\Flags\\*.png\" />", project);
        Assert.Contains(
            "pack://application:,,,/AcKrovy.AutoCAD;component/UI/Assets/Flags/",
            mapping);

        var expectedMappings = new Dictionary<string, (string Color, string Black)>
        {
            ["sk"] = ("sk_flag.png", "sk_flag_black.png"),
            ["cs"] = ("cz_flag.png", "cz_flag_black.png"),
            ["en"] = ("gb_flag.png", "gb_flag_black.png"),
            ["de"] = ("de_flag.png", "de_flag_black.png"),
            ["pl"] = ("pl_flag.png", "pl_flag_black.png"),
            ["fr"] = ("fr_flag.png", "fr_flag_black.png"),
        };
        foreach (var (code, files) in expectedMappings)
        {
            Assert.Contains(
                $"[\"{code}\"] = new(\"{code}\", \"{files.Color}\", \"{files.Black}\")",
                mapping);
            Assert.True(new FileInfo(Path.Combine(flagsDirectory, files.Color)).Length > 0);
            Assert.True(new FileInfo(Path.Combine(flagsDirectory, files.Black)).Length > 0);
        }

        Assert.DoesNotContain("SettingsFlagSkBrush", xaml);
        Assert.DoesNotContain("SettingsFlagSkBrush", design);
        Assert.DoesNotContain("SettingsFlagFrBrush", xaml);
        Assert.DoesNotContain("SettingsFlagFrBrush", design);
    }

    [Theory]
    [InlineData("sk-SK")]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("pl-PL")]
    [InlineData("fr-FR")]
    [Trait("Feature", "LanguageCards")]
    public void LanguageCardAccessibility_IsLocalized(string cultureName)
    {
        var value = UiStrings.GetString(
            "SettingsWindow_Language_SelectFormat",
            CultureInfo.GetCultureInfo(cultureName));

        Assert.Contains("{0}", value);
        Assert.Contains(
            "English",
            string.Format(CultureInfo.GetCultureInfo(cultureName), value, "English"));
    }

    [Fact]
    [Trait("Feature", "AnnotationPreview")]
    public void AnnotationScalePreview_UsesOneDwgLikeSceneAndAllExistingAssets()
    {
        var xaml = File.ReadAllText(Path.Combine(UiDirectory(), "LayerSettingsWindow.xaml"));
        var document = XDocument.Parse(xaml);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var project = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "AcKrovy.AutoCAD.csproj"));
        var resourceMap = File.ReadAllText(Path.Combine(
            UiDirectory(),
            "AnnotationPreviewResourceMap.cs"));
        var light = File.ReadAllText(Path.Combine(
            UiDirectory(),
            "Design",
            "SettingsColors.Light.xaml"));
        var dark = File.ReadAllText(Path.Combine(
            UiDirectory(),
            "Design",
            "SettingsColors.Dark.xaml"));
        var assets = Path.Combine(UiDirectory(), "Assets", "AnnotationPreviews");

        Assert.DoesNotContain("Text=\"35°\"", xaml);
        Assert.DoesNotContain("M0,6 L10,0 7,5 11,8 Z", xaml);
        var previewImages = document.Descendants(presentation + "Image")
            .Where(image => (string?)image.Attribute(x + "Name") ==
                "AnnotationScalePreviewImage")
            .ToArray();
        Assert.Single(previewImages);
        Assert.Equal(
            "{Binding SelectedAnnotationPreviewResourceUri}",
            (string?)previewImages[0].Attribute("Source"));
        Assert.Equal("Uniform", (string?)previewImages[0].Attribute("Stretch"));
        Assert.Empty(previewImages[0].Descendants(presentation + "ScaleTransform"));
        var categoryTabControls = document.Descendants(presentation + "TabControl")
            .Where(control => (string?)control.Attribute(x + "Name") == "AnnotationCategoryTabs")
            .ToArray();
        Assert.Single(categoryTabControls);
        var categoryTabStyle = document.Descendants(presentation + "Style")
            .Single(style => (string?)style.Attribute(x + "Key") ==
                "AnnotationCategoryTabStyle");
        var headerPresenters = categoryTabStyle
            .Descendants(presentation + "ContentPresenter")
            .Where(presenter => (string?)presenter.Attribute("ContentSource") == "Header")
            .ToArray();
        Assert.Single(headerPresenters);
        Assert.Equal(
            "True",
            (string?)headerPresenters[0].Attribute("RecognizesAccessKey"));
        var categoryTabControlStyle = document.Descendants(presentation + "Style")
            .Single(style => (string?)style.Attribute(x + "Key") ==
                "AnnotationCategoryTabControlStyle");
        var tabPanels = categoryTabControlStyle
            .Descendants(presentation + "TabPanel")
            .Where(panel => (string?)panel.Attribute("IsItemsHost") == "True")
            .ToArray();
        Assert.Single(tabPanels);
        var selectedContentPresenters = categoryTabControlStyle
            .Descendants(presentation + "ContentPresenter")
            .Where(presenter => (string?)presenter.Attribute("ContentSource") ==
                "SelectedContent")
            .ToArray();
        Assert.Single(selectedContentPresenters);
        var categoryTabs = categoryTabControls[0]
            .Elements(presentation + "TabItem")
            .ToArray();
        Assert.Equal(6, categoryTabs.Length);
        Assert.Equal("1", (string?)categoryTabControls[0].Attribute("SelectedIndex"));
        var generalTab = categoryTabs[0];
        var scaleTab = categoryTabs[1];
        var picker = generalTab.Descendants(presentation + "ListBox")
            .Single(list => (string?)list.Attribute(x + "Name") == "AnnotationPresetSelector");
        Assert.Equal("Preset", (string?)picker.Attribute("SelectedValuePath"));
        Assert.Contains(
            "SelectedAnnotationPreset",
            (string?)picker.Attribute("SelectedValue") ?? string.Empty);
        Assert.DoesNotContain(
            scaleTab.Descendants(presentation + "ListBox"),
            list => (string?)list.Attribute(x + "Name") == "AnnotationPresetSelector");
        Assert.DoesNotContain(
            generalTab.Descendants(presentation + "ListBox"),
            list => ((string?)list.Attribute(x + "Name")) is
                "DrawingAnnotationScaleSelector" or "UserDefaultAnnotationScaleSelector");
        var scaleSelectors = scaleTab.Descendants(presentation + "ListBox")
            .Where(list => ((string?)list.Attribute(x + "Name")) is
                "DrawingAnnotationScaleSelector" or "UserDefaultAnnotationScaleSelector")
            .ToArray();
        Assert.Single(scaleSelectors);
        Assert.Equal(
            "DrawingAnnotationScaleSelector",
            (string?)scaleSelectors[0].Attribute(x + "Name"));
        Assert.Single(scaleSelectors[0].Descendants(presentation + "StackPanel"));
        Assert.DoesNotContain("UserDefaultAnnotationScaleSelector", xaml);
        Assert.Contains("PreviewDimensionModelText", xaml);
        Assert.Contains("PreviewDimensionPaperText", xaml);
        Assert.Contains("PreviewBlockPaperText", xaml);
        Assert.Single(generalTab.Elements(presentation + "ScrollViewer"));
        Assert.Empty(scaleTab.Elements(presentation + "ScrollViewer"));
        Assert.Contains(
            scaleTab.Descendants(presentation + "RowDefinition"),
            row => (string?)row.Attribute("Height") == "182");
        Assert.All(
            categoryTabs.Skip(2),
            tab => Assert.Empty(tab.Descendants(presentation + "Button")));
        var annotationHost = document.Descendants(presentation + "Border")
            .Single(border => (string?)border.Attribute("Style") ==
                "{StaticResource AnnotationSectionHostStyle}");
        Assert.Single(annotationHost.Elements(presentation + "Grid"));
        Assert.Empty(annotationHost.Elements(presentation + "ScrollViewer"));
        Assert.Contains("Width=\"1500\"", xaml);
        Assert.Contains("Height=\"900\"", xaml);
        Assert.Contains("MinWidth=\"1250\"", xaml);
        Assert.Contains("MinHeight=\"720\"", xaml);
        Assert.Contains("WindowStartupLocation=\"CenterScreen\"", xaml);
        Assert.Contains("SettingsAnnotationPreviewBackgroundBrush", xaml);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", xaml);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Disabled\"", xaml);
        Assert.Contains(
            "<Resource Include=\"UI\\Assets\\AnnotationPreviews\\*.png\" />",
            project);
        Assert.Contains(
            "pack://application:,,,/AcKrovy.AutoCAD;component/UI/Assets/AnnotationPreviews/",
            resourceMap);
        Assert.Contains(
            "x:Key=\"SettingsAnnotationPreviewBackgroundBrush\" Color=\"#FFFFFF\"",
            light);
        Assert.Contains(
            "x:Key=\"SettingsAnnotationPreviewBackgroundBrush\" Color=\"#000000\"",
            dark);
        Assert.All(
            SettingsAnnotationPresetRules.All,
            definition => Assert.True(
                new FileInfo(Path.Combine(assets, definition.ReferenceFileName)).Length > 0));
        Assert.False(File.Exists(Path.Combine(UiDirectory(), "AnnotationPresetPreview.xaml")));
        Assert.False(File.Exists(Path.Combine(
            UiDirectory(),
            "AnnotationPresetTemplateSelector.cs")));
    }

    [Fact]
    [Trait("Feature", "CombinedAnnotation")]
    public void CombinedAnnotation_UsesManagedPrimaryAndFramedItemComponents()
    {
        var service = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs"));
        var store = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelStore.cs"));
        var leaderStyles = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AcKrovyMLeaderStyleService.cs"));
        var blockMethodStart = service.IndexOf(
            "private static MLeader CreateBlockMLeader(",
            StringComparison.Ordinal);
        var blockMethodEnd = service.IndexOf(
            "private static MText CreateLeaderMText(",
            blockMethodStart,
            StringComparison.Ordinal);
        var blockMethod = service[blockMethodStart..blockMethodEnd];

        Assert.Contains("UpsertCombinedLeader(", service);
        Assert.Contains("TimberMainAnnotationComponentRole.Primary", service);
        Assert.Contains("TimberMainAnnotationComponentRole.FramedItem", service);
        Assert.DoesNotContain("CreateBlockOnlyMLeader(", service);
        Assert.Contains("CreateBlockMLeader(", service);
        Assert.Contains("BlockConnectionType.ConnectExtents", service);
        Assert.Contains("AttachmentPoint.MiddleCenter", service);
        Assert.Contains("CalculateCombinedDimensionsTextPlacement(", service);
        Assert.Contains("FormatStackedDimensions(data)", service);
        Assert.Contains("ContentType.BlockContent", service);
        Assert.Contains("new MText()", service);
        Assert.Equal(1, CountOccurrences(blockMethod, "leader.AddLeader();"));
        Assert.Equal(1, CountOccurrences(blockMethod, "leader.AddLeaderLine(leaderIndex);"));
        Assert.Equal(1, CountOccurrences(blockMethod, "leader.AddFirstVertex("));
        Assert.Equal(1, CountOccurrences(blockMethod, "leader.AddLastVertex("));
        Assert.DoesNotContain("placement.LandingEnd", blockMethod);
        Assert.DoesNotContain("new Line(", blockMethod);
        Assert.DoesNotContain("new Polyline(", blockMethod);
        Assert.Contains(
            "ApplyCombinedBlockInstanceProperties(",
            leaderStyles);
        Assert.Contains("LeaderType.StraightLeader", leaderStyles);
        Assert.Contains("LeaderType.SplineLeader", leaderStyles);
        Assert.Contains("BlockConnectionType.ConnectExtents", leaderStyles);
        Assert.Contains("BlockConnectionType.ConnectBase", leaderStyles);
        Assert.Contains("leader.DoglegLength = settings.LandingDistance", leaderStyles);
        Assert.Contains("leader.EnableLanding = settings.HasHorizontalLanding", leaderStyles);
        Assert.DoesNotContain("useCurrentAnnotationScale", service);
        Assert.DoesNotContain("leader.EnableAnnotationScale = true", service);
        Assert.DoesNotContain("text.Annotative = AnnotativeStates.True", service);
        Assert.DoesNotContain("annotation.AddContext(", service);
        Assert.Contains("RecenterCombinedDimensionsText(", service);
        Assert.Contains("TryGetLandingSegment(", service);
        Assert.Contains("leader.GetLastVertex(leaderLineIndexes[0])", service);
        Assert.Contains("leader.GetDogleg(leaderIndexes[0])", service);
        Assert.Contains("doglegDirection.GetNormal() * leader.DoglegLength", service);
        Assert.Contains(
            "TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm",
            service);
        Assert.DoesNotContain("landingDistanceOverride", service);
        Assert.Contains(
            "CalculateTextCenterOffsetFromLandingStartMm(",
            service);
        Assert.Contains("data.ElementId", service);
        Assert.Contains("SourceHandle = sourceHandle", service);
        Assert.Contains("ComponentRole = componentRole", service);
        Assert.Contains("DeleteUnexpectedCompositeComponents(", service);
        Assert.Contains("public int SchemaVersion { get; init; } = 3;", store);
        Assert.Equal(5, TimberElementDataSchema.CurrentVersion);
    }

    [Fact]
    [Trait("Feature", "CopySourcePreservation")]
    public void CopyLifecycle_DropsClonedAnnotationsAndNeverReconcilesSourceIds()
    {
        var service = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "LiveGeometrySynchronizationService.cs"));
        var labels = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs"));
        var styles = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AcKrovyMLeaderStyleService.cs"));
        var blocks = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AcKrovyItemLeaderBlockService.cs"));

        Assert.Contains("_appendedTimberIds.TryAdd(entity.ObjectId)", service);
        Assert.Contains("EraseAppendedAnnotationCopies(", service);
        Assert.Contains("SelectIncrementalCandidates(", service);
        Assert.Contains("preserveCopySources", service);
        Assert.Contains("copySourcePreservation: preserveCopySources", service);
        Assert.Contains("allowElementIdFallback: !copySourcePreservation", labels);
        Assert.Contains("updateExistingDefinitions: !copySourcePreservation", labels);
        Assert.Contains("if (!updateExisting)", styles);
        Assert.Contains("return existingId;", styles);
        Assert.Contains("preserveExistingDefinition ? OpenMode.ForRead : OpenMode.ForWrite", blocks);
        Assert.Contains("AcKrovyMLeaderStyleService.Ensure", labels);
        Assert.Contains("text.SetDatabaseDefaults(database)", labels);
        Assert.DoesNotContain("ISO-Annotative", labels);
    }

    private static IReadOnlyList<int> AllPickerIndices() =>
        AciColorPickerRules.BasicIndices
            .Concat(AciColorPickerRules.MainPaletteIndices)
            .Concat(AciColorPickerRules.GrayscaleIndices)
            .ToArray();

    private static string UiDirectory() => Path.Combine(
        RepositoryRoot(),
        "src",
        "AcKrovy.AutoCAD",
        "UI");

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate AcKrovy.sln.");
    }
}
