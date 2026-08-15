using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source-contract guards for the AK_LABEL MissingOnly / ResetSelected / ResetAll path.
/// </summary>
public sealed class AkLabelCommandSourceContractTests
{
    [Fact]
    public void Commands_RouteThroughCentralAkLabelService()
    {
        var commands = Normalize(Read(
            "src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs"));
        var service = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AkLabelCommandService.cs"));

        Assert.Contains("AkLabelCommandService.TryPromptIntention(", commands);
        Assert.Contains("AkLabelCommandService.ConfirmResetAll(", commands);
        Assert.Contains("AkLabelCommandService.Run(", commands);
        Assert.Contains("AkLabelIntention.ResetSelected", commands);
        Assert.Contains("AkLabelIntention.ResetAll", commands);

        Assert.Contains("OpenMode.ForRead", service);
        Assert.DoesNotContain("SynchronizeElementIds(", service);
        Assert.DoesNotContain("InitializeLocalCopies(", service);
        Assert.Contains("ForceCanonicalRecreate", service);
        Assert.Contains("DeleteOwnedAnnotationsForSource(", service);
        Assert.Contains("EnsureForElement(", service);
        Assert.Contains("ResolveSelectedTimberSources(", service);
        Assert.DoesNotContain("AutoCadOwnedAnnotationSelectionService.Resolve(", service);
        Assert.Contains("AkLabelIntentionPromptRules.Parse(", service);
        Assert.Contains("Keywords.Default = AkLabelIntentionPromptRules.GlobalMissing", service);
        Assert.Contains("AppendKeywordsToMessage = false", service);
        Assert.Contains("CreateKeywordPromptSetup(", service);
    }

    [Fact]
    public void TemporaryGermanForensicProbe_IsAbsentFromProduction()
    {
        var service = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AkLabelCommandService.cs"));
        var commandsDir = Path.Combine(
            FindRepoRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Commands");

        Assert.False(File.Exists(Path.Combine(commandsDir, "AkLabelKeywordProbeCommands.cs")));
        Assert.DoesNotContain("AK_DEV_LABEL_KEYWORDS", service);
        Assert.DoesNotContain("RunKeywordProbe(", service);
        Assert.DoesNotContain("AK_LABEL_KEYWORD_DEBUG", service);
        Assert.DoesNotContain("AK_LABEL_RESETALL_DEBUG", service);
        Assert.DoesNotContain("CONFIRM_ENTER", service);
        Assert.DoesNotContain("SHOWDIALOG_ENTER", service);
        Assert.DoesNotContain("RUN_RESETALL_ENTER", service);
        Assert.DoesNotContain("TraceKeywordPromptBefore(", service);
        Assert.DoesNotContain("TraceResetAllConfirm(", service);
        Assert.Contains("CreateKeywordPromptSetup(", service);
        Assert.Contains("DrainDispatcherInputQueue(", service);
    }

    [Fact]
    public void MissingOnly_NoOpsExisting_ViaCoreRules()
    {
        var rules = Normalize(Read(
            "src/AcKrovy.Core/Services/AkLabelCommandRules.cs"));
        Assert.Contains("AkLabelIntention.MissingOnly", rules);
        Assert.Contains("AkLabelSourceAction.NoOp", rules);
        Assert.Contains("AkLabelSourceAction.EnsureMissing", rules);
        Assert.Contains("AkLabelSourceAction.ForceCanonicalRecreate", rules);
    }

    [Fact]
    public void LabelAndLabelsCommands_AreRegistered()
    {
        var names = Normalize(Read(
            "src/AcKrovy.Localization/CommandUiCatalog.cs"));
        var commands = Normalize(Read(
            "src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs"));

        Assert.Contains("public const string Label = \"AK_LABEL\";", names);
        Assert.Contains("public const string Labels = \"AK_LABELS\";", names);
        Assert.Contains("public const string LabelMissing = \"AK_LABELMISSING\";", names);
        Assert.Contains("public const string LabelSelected = \"AK_LABELSELECTED\";", names);
        Assert.Contains("public const string LabelAll = \"AK_LABELALL\";", names);
        Assert.Contains("AcKrovyCommandNames.Label", commands);
        Assert.Contains("AcKrovyCommandNames.Labels", commands);
        Assert.Contains("AcKrovyCommandNames.LabelMissing", commands);
        Assert.Contains("AcKrovyCommandNames.LabelSelected", commands);
        Assert.Contains("AcKrovyCommandNames.LabelAll", commands);
    }

    [Fact]
    public void RibbonLabelsSplit_RoutesDirectIntentions_WithoutKeywordSimulation()
    {
        var ribbon = Normalize(Read("src/AcKrovy.AutoCAD/Ribbon/AcKrovyRibbon.cs"));
        var commands = Normalize(Read(
            "src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs"));
        var handler = Normalize(Read(
            "src/AcKrovy.AutoCAD/Ribbon/RibbonCommandHandler.cs"));

        Assert.Contains("RibbonSplitButton", ribbon);
        Assert.Contains("IsSplit = true", ribbon);
        Assert.Contains("IsSynchronizedWithCurrentItem = false", ribbon);
        Assert.Contains("LabelsSplitButton()", ribbon);
        Assert.Contains("CommandParameter = AcKrovyCommandNames.LabelMissing", ribbon);
        Assert.Contains("CommandUiCatalog.LabelsSplitActions", ribbon);

        Assert.Equal(3, CommandUiCatalog.LabelsSplitActions.Count);
        Assert.Equal(AcKrovyCommandNames.LabelMissing, CommandUiCatalog.LabelsSplitActions[0].CommandName);
        Assert.Equal(AcKrovyCommandNames.LabelSelected, CommandUiCatalog.LabelsSplitActions[1].CommandName);
        Assert.Equal(AcKrovyCommandNames.LabelAll, CommandUiCatalog.LabelsSplitActions[2].CommandName);
        Assert.Equal("Command_Labels_KeywordMissing", CommandUiCatalog.LabelMissing.LabelResourceKey);
        Assert.Equal("Command_Labels_KeywordSelect", CommandUiCatalog.LabelSelected.LabelResourceKey);
        Assert.Equal("Command_Labels_KeywordAll", CommandUiCatalog.LabelAll.LabelResourceKey);

        Assert.Contains("ExecuteAkLabelIntention(AkLabelIntention.MissingOnly)", commands);
        Assert.Contains("ExecuteAkLabelIntention(AkLabelIntention.ResetSelected)", commands);
        Assert.Contains("ExecuteAkLabelIntention(AkLabelIntention.ResetAll)", commands);
        Assert.Contains("TryPromptIntention(", commands);

        // Interactive command-line path remains; Ribbon does not type keywords.
        Assert.DoesNotContain("SendStringToExecute(\"AK_LABEL ", ribbon);
        Assert.DoesNotContain("GetKeywords(", ribbon);
        Assert.Contains("CommandMacroBuilder.Build(command)", handler);
        Assert.DoesNotContain("TryPromptIntention(", ribbon);
    }

    [Fact]
    public void ResetAll_RequiresExplicitConfirmation_BeforeAnyWrite()
    {
        var service = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AkLabelCommandService.cs"));
        var commands = Normalize(Read(
            "src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs"));

        Assert.Contains("AkLabelResetAllConfirmWindow", service);
        Assert.Contains("AkLabelResetAllProgressWindow", service);
        Assert.Contains("ShowModalWindow(dialog)", service);
        Assert.Contains("DrainDispatcherInputQueue()", service);
        Assert.Contains("CancelButton.IsDefault = false", Normalize(Read(
            "src/AcKrovy.AutoCAD/UI/AkLabelResetAllConfirmWindow.xaml.cs")));
        Assert.Contains("ApplicationIdle", Normalize(Read(
            "src/AcKrovy.AutoCAD/UI/AkLabelResetAllConfirmWindow.xaml.cs")));
        Assert.Contains("IsDefault=\"True\"", Normalize(Read(
            "src/AcKrovy.AutoCAD/UI/AkLabelResetAllConfirmWindow.xaml")));
        Assert.Contains("CancelButton.Focus()", Normalize(Read(
            "src/AcKrovy.AutoCAD/UI/AkLabelResetAllConfirmWindow.xaml.cs")));
        Assert.DoesNotContain("MessageBoxButton.OKCancel", service);
        Assert.DoesNotContain("WpfMessageBox.Show(", service);
        Assert.Contains("ConfirmResetAll(", commands);
        Assert.Contains("Command_Labels_Cancelled", commands);

        var executeStart = commands.IndexOf(
            "private void ExecuteAkLabelIntention(",
            StringComparison.Ordinal);
        Assert.True(executeStart >= 0, "Shared ExecuteAkLabelIntention missing.");
        var executeBody = commands[executeStart..];
        var confirmIndex = executeBody.IndexOf(
            "intention == AkLabelIntention.ResetAll &&",
            StringComparison.Ordinal);
        var runIndex = executeBody.IndexOf(
            "AkLabelCommandService.Run(",
            StringComparison.Ordinal);
        Assert.True(confirmIndex >= 0, "ResetAll confirmation gate missing.");
        Assert.True(runIndex > confirmIndex, "ConfirmResetAll must run before Run.");
    }

    [Fact]
    public void ResetAll_NeverAcquiresSelection_ResetSelectedDoes()
    {
        var commands = Normalize(Read(
            "src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs"));

        // Interactive AK_LABEL / AK_LABELS still prompts, then shares execution.
        var interactiveStart = commands.IndexOf(
            "public void UpdateAllLabels()",
            StringComparison.Ordinal);
        var missingStart = commands.IndexOf(
            "public void UpdateMissingLabels()",
            StringComparison.Ordinal);
        Assert.True(interactiveStart >= 0 && missingStart > interactiveStart);
        var interactive = commands[interactiveStart..missingStart];
        Assert.Contains("TryPromptIntention(", interactive);
        Assert.Contains("ExecuteAkLabelIntention(intention)", interactive);
        Assert.DoesNotContain("PromptForEntities(", interactive);

        var executeStart = commands.IndexOf(
            "private void ExecuteAkLabelIntention(",
            StringComparison.Ordinal);
        Assert.True(executeStart >= 0);
        var executeBody = commands[executeStart..];

        Assert.Contains("if (intention == AkLabelIntention.ResetSelected)", executeBody);
        Assert.Contains("PromptForEntities(", executeBody);

        var resetSelectedIndex = executeBody.IndexOf(
            "if (intention == AkLabelIntention.ResetSelected)",
            StringComparison.Ordinal);
        var promptIndex = executeBody.IndexOf("PromptForEntities(", StringComparison.Ordinal);
        Assert.True(promptIndex > resetSelectedIndex);

        Assert.Contains("AkLabelIntention.ResetAll", executeBody);
        Assert.Contains("ConfirmResetAll(", executeBody);

        // Direct Ribbon ResetAll must not go through Select.
        Assert.Contains(
            "ExecuteAkLabelIntention(AkLabelIntention.ResetAll)",
            commands);
        Assert.Contains(
            "[CommandMethod(AcKrovyCommandNames.LabelAll, CommandFlags.Modal)]",
            commands);
        Assert.DoesNotContain(
            "ExecuteAkLabelIntention(AkLabelIntention.ResetSelected);\r\n        ExecuteAkLabelIntention(AkLabelIntention.ResetAll)",
            commands);

        Assert.Contains(
            "[CommandMethod(AcKrovyCommandNames.Label, CommandFlags.Modal)]",
            commands);
        Assert.Contains(
            "[CommandMethod(AcKrovyCommandNames.Labels, CommandFlags.Modal)]",
            commands);
        Assert.Contains(
            "[CommandMethod(AcKrovyCommandNames.LabelMissing, CommandFlags.Modal)]",
            commands);
    }

    [Fact]
    public void ResetSelected_UsesSelectedTimberSources_NotOwnedAnnotations()
    {
        var service = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AkLabelCommandService.cs"));

        var resetSelectedStart = service.IndexOf(
            "private static ElementLabelUpdateResult ExecuteResetSelected(",
            StringComparison.Ordinal);
        var executeStart = service.IndexOf(
            "private static ElementLabelUpdateResult Execute(",
            StringComparison.Ordinal);
        Assert.True(resetSelectedStart >= 0 && executeStart > resetSelectedStart);
        var resetSelectedBody = service[resetSelectedStart..executeStart];

        Assert.Contains("ResolveSelectedTimberSources(database, selectedIds)", resetSelectedBody);
        Assert.Contains("resolved.AcceptedIds", resetSelectedBody);
        Assert.DoesNotContain("AutoCadOwnedAnnotationSelectionService.Resolve(", resetSelectedBody);

        var resetAllExecute = service[executeStart..];
        Assert.DoesNotContain(
            "ResolveSelectedTimberSources(",
            resetAllExecute);
        Assert.Contains("FindAllTimberElements(", resetAllExecute);
    }

    [Fact]
    public void ResetSelected_PromptsForLocalizedSourceElements()
    {
        var commands = Normalize(Read(
            "src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs"));
        var sk = Normalize(Read("src/AcKrovy.Localization/Resources/UiStrings.resx"));
        var cs = Normalize(Read("src/AcKrovy.Localization/Resources/UiStrings.cs.resx"));
        var en = Normalize(Read("src/AcKrovy.Localization/Resources/UiStrings.en.resx"));
        var de = Normalize(Read("src/AcKrovy.Localization/Resources/UiStrings.de.resx"));
        var pl = Normalize(Read("src/AcKrovy.Localization/Resources/UiStrings.pl.resx"));
        var fr = Normalize(Read("src/AcKrovy.Localization/Resources/UiStrings.fr.resx"));

        Assert.Contains("\"Command_Labels_PromptSelected\"", commands);
        Assert.DoesNotContain("\"Command_Labels_PromptSelectAnnotations\"", commands);
        foreach (var pack in new[] { sk, cs, en, de, pl, fr })
        {
            Assert.Contains("name=\"Command_Labels_PromptSelected\"", pack);
        }

        Assert.Contains(
            "Označ prvky, ktorým chceš vytvoriť alebo obnoviť popisy:",
            sk);
    }

    [Fact]
    public void SelectedSourceResolution_IsReadOnly_MetadataBased_AndDeduplicated()
    {
        var service = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AkLabelCommandService.cs"));
        var resolverStart = service.IndexOf(
            "private static SelectedTimberSourceResolution ResolveSelectedTimberSources(",
            StringComparison.Ordinal);
        var executeStart = service.IndexOf(
            "private static ElementLabelUpdateResult Execute(",
            resolverStart,
            StringComparison.Ordinal);
        Assert.True(resolverStart >= 0 && executeStart > resolverStart);
        var resolver = service[resolverStart..executeStart];

        Assert.Contains("StartOpenCloseTransaction()", resolver);
        Assert.Contains("OpenMode.ForRead", resolver);
        Assert.Contains("IsSupportedTimberGeometry(entity)", resolver);
        Assert.Contains("metadataStore.TryRead(entity, out var data)", resolver);
        Assert.Contains("var seenIds = new HashSet<ObjectId>()", resolver);
        Assert.Contains("if (!seenIds.Add(id))", resolver);
        Assert.DoesNotContain("OpenMode.ForWrite", resolver);
        Assert.DoesNotContain("UpgradeOpen(", resolver);
        Assert.DoesNotContain("Write(", resolver);
        Assert.DoesNotContain("RoofGeneratedTimberStore", resolver);
        Assert.DoesNotContain("RoofDisplayStore", resolver);
    }

    [Fact]
    public void SelectedSources_ReuseCanonicalCreateAndRebuildPath()
    {
        var service = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AkLabelCommandService.cs"));
        var rules = Normalize(Read(
            "src/AcKrovy.Core/Services/AkLabelCommandRules.cs"));

        Assert.Contains("AkLabelCommandRules.Decide(", service);
        Assert.Contains("DeleteOwnedAnnotationsForSource(", service);
        Assert.Contains("TimberAnnotationService.EnsureForElement(", service);
        Assert.Contains("DeleteDuplicatesForExistingSourceHandles(", service);
        Assert.Contains("case AkLabelIntention.ResetSelected:", rules);
        Assert.Contains("return AkLabelSourceAction.ForceCanonicalRecreate;", rules);
        Assert.Contains("hasExistingMainAnnotation", rules);
    }

    [Fact]
    public void SelectedSources_SkipUnsupportedBeforePresentationWrites()
    {
        var service = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AkLabelCommandService.cs"));
        var resetStart = service.IndexOf(
            "private static ElementLabelUpdateResult ExecuteResetSelected(",
            StringComparison.Ordinal);
        var resolverStart = service.IndexOf(
            "private static SelectedTimberSourceResolution ResolveSelectedTimberSources(",
            StringComparison.Ordinal);
        Assert.True(resetStart >= 0 && resolverStart > resetStart);
        var reset = service[resetStart..resolverStart];

        var emptyGate = reset.IndexOf(
            "if (resolved.AcceptedIds.Count == 0)",
            StringComparison.Ordinal);
        var execute = reset.IndexOf("var result = Execute(", StringComparison.Ordinal);
        Assert.True(emptyGate >= 0 && execute > emptyGate);
        Assert.DoesNotContain("AutoCadAnnotationPresentationBatchContext.Create(", reset[..execute]);
    }

    [Fact]
    public void ResetSelected_CancelOrEmptySelection_ReturnsBeforeServiceRun()
    {
        var commands = Normalize(Read(
            "src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs"));
        var executeStart = commands.IndexOf(
            "private void ExecuteAkLabelIntention(",
            StringComparison.Ordinal);
        var showLabelsStart = commands.IndexOf(
            "public void ShowLabels()",
            executeStart,
            StringComparison.Ordinal);
        Assert.True(executeStart >= 0 && showLabelsStart > executeStart);
        var execute = commands[executeStart..showLabelsStart];

        var emptyGate = execute.IndexOf(
            "if (selectedIds.Count == 0)",
            StringComparison.Ordinal);
        var run = execute.IndexOf(
            "AkLabelCommandService.Run(",
            StringComparison.Ordinal);
        Assert.True(emptyGate >= 0 && run > emptyGate);
        Assert.Contains("return;", execute[emptyGate..run]);
    }

    [Fact]
    public void GeneratedRafter_IsEligibleOnlyThroughNormalTimberContract()
    {
        var service = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AkLabelCommandService.cs"));
        var rafterWorkflow = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/RoofRafterCommandWorkflow.cs"));

        Assert.Contains("TimberSourceLineCreationService.Create(", rafterWorkflow);
        Assert.Contains("AutoCadTimberElementMetadataStore", service);
        Assert.DoesNotContain("RoofGeneratedTimberStore", service);
        Assert.DoesNotContain("RoofGeneratedTimberData", service);
        Assert.DoesNotContain("RoofDisplayStore", service);
    }

    [Fact]
    public void MissingAndResetAllPaths_RemainIndependentOfSelectedResolution()
    {
        var service = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AkLabelCommandService.cs"));
        var runStart = service.IndexOf(
            "public static ElementLabelUpdateResult Run(",
            StringComparison.Ordinal);
        var confirmStart = service.IndexOf(
            "public static bool ConfirmResetAll()",
            StringComparison.Ordinal);
        Assert.True(runStart >= 0 && confirmStart > runStart);
        var run = service[runStart..confirmStart];

        Assert.Contains("AkLabelIntention.MissingOnly => Execute(", run);
        Assert.Contains("AkLabelIntention.ResetAll => Execute(", run);
        Assert.Contains("AkLabelIntention.ResetSelected => ExecuteResetSelected(", run);
    }

    [Fact]
    public void OwnedAnnotationResolver_RemainsAvailableButOutsideAkLabels()
    {
        var service = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AkLabelCommandService.cs"));
        var ownedResolver = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AutoCadOwnedAnnotationSelectionService.cs"));

        Assert.DoesNotContain("AutoCadOwnedAnnotationSelectionService", service);
        Assert.Contains(
            "internal static class AutoCadOwnedAnnotationSelectionService",
            ownedResolver);
        Assert.Contains("TimberSourceEntity", ownedResolver);
        Assert.Contains("StartOpenCloseTransaction()", ownedResolver);
    }

    [Fact]
    public void KeywordRegistration_UsesDistinctGlobalLocalDisplay()
    {
        var service = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AkLabelCommandService.cs"));

        Assert.Contains("Command_Labels_KeywordMissingLocal", service);
        Assert.Contains("Command_Labels_KeywordSelectLocal", service);
        Assert.Contains("Command_Labels_KeywordAllLocal", service);
        Assert.Contains("Command_Labels_KeywordMissing", service);
        Assert.Contains("Command_Labels_KeywordSelect", service);
        Assert.Contains("Command_Labels_KeywordAll", service);

        // Native AutoCAD Add(global, local, display) — locals are not display-only.
        Assert.Contains(
            "options.Keywords.Add(\n            AkLabelIntentionPromptRules.GlobalMissing,\n            missingLocal,\n            missingDisplay)",
            service);
        Assert.Contains(
            "options.Keywords.Add(\n            AkLabelIntentionPromptRules.GlobalSelect,\n            selectLocal,\n            selectDisplay)",
            service);
        Assert.Contains(
            "AkLabelIntentionPromptRules.ResolveRegisteredAllGlobal(selectLocal)",
            service);
        Assert.DoesNotContain(
            "options.Keywords.Add(\n            AkLabelIntentionPromptRules.GlobalAll,\n            allLocal,\n            allDisplay)",
            service);
        Assert.DoesNotContain(
            "AkLabelIntentionPromptRules.GlobalMissing,\n            AkLabelIntentionPromptRules.GlobalMissing,",
            service);
    }

    [Fact]
    public void IntentionPrompt_NamesLocalizedMissingDefault()
    {
        var sk = Normalize(Read("src/AcKrovy.Localization/Resources/UiStrings.resx"));
        var en = Normalize(Read("src/AcKrovy.Localization/Resources/UiStrings.en.resx"));

        Assert.Contains("[Chýbajúce/Vybrať/Obnoviť všetky] &lt;Chýbajúce&gt;:", sk);
        Assert.Contains("Command_Labels_KeywordMissingLocal", sk);
        Assert.Contains("Command_Labels_KeywordSelectLocal", sk);
        Assert.Contains("Command_Labels_KeywordAllLocal", sk);
        Assert.Contains("<value>ObnovitVsetky</value>", sk);
        Assert.Contains("<value>Obnoviť všetky</value>", sk);
        Assert.Contains("[Missing/Select/All] &lt;Missing&gt;:", en);
        Assert.Contains("<value>Missing</value>", en);
    }

    [Fact]
    public void LiveGeometryRotatePaths_RemainIntact()
    {
        var live = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs"));
        Assert.Contains("ShouldPreserveAnnotationPresentationOnly(", live);
        Assert.Contains("SelectSourceRefreshCandidates(", live);
        Assert.Contains("IsUndoRedoCommand(", live);
    }

    private static string Read(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(path);
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AcKrovy.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
