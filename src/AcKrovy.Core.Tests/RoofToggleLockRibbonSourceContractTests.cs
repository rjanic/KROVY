using AcKrovy.Core.Models.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source-contract coverage for the LOCKED/UNLOCKED roof toggle Ribbon UX:
/// the cyclic AK_ROOF_TOGGLELOCK command, its Ribbon button mapping to the
/// approved icons, the RoofEdit Ribbon button, reuse of the existing
/// Lock/Unlock workflow via a shared persisted-state apply, deterministic
/// EditState-based dispatch (never message parsing), and preservation of the
/// existing AK_ROOF_LOCK / AK_ROOF_UNLOCK commands.
/// </summary>
public sealed class RoofToggleLockRibbonSourceContractTests
{
    private static readonly string Catalog = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Localization", "CommandUiCatalog.cs");
    private static readonly string Commands = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Commands", "AcKrovyCommands.cs");
    private static readonly string Ribbon = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Ribbon", "AcKrovyRibbon.cs");
    private static readonly string Workflow = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofEditStateCommandWorkflow.cs");

    [Fact]
    public void ToggleCommand_IsRegisteredAndRoutedThroughBoundary()
    {
        Assert.Contains("public const string RoofToggleLock = \"AK_ROOF_TOGGLELOCK\"", Catalog);
        Assert.Contains("[CommandMethod(AcKrovyCommandNames.RoofToggleLock, CommandFlags.Modal | CommandFlags.Redraw)]", Commands);
        Assert.Contains("RoofEditStateCommandWorkflow.Toggle(ActiveDocument())", Commands);
        Assert.Contains("CommandExecutionBoundary.Execute", Commands);
    }

    [Fact]
    public void ExistingLockUnlockCommands_RemainRegisteredAndRouted()
    {
        Assert.Contains("[CommandMethod(AcKrovyCommandNames.RoofLock, CommandFlags.Modal | CommandFlags.Redraw)]", Commands);
        Assert.Contains("[CommandMethod(AcKrovyCommandNames.RoofUnlock, CommandFlags.Modal | CommandFlags.Redraw)]", Commands);
        Assert.Contains("RoofEditStateCommandWorkflow.Lock(ActiveDocument())", Commands);
        Assert.Contains("RoofEditStateCommandWorkflow.Unlock(ActiveDocument())", Commands);
        Assert.Contains("public const string RoofLock = \"AK_ROOF_LOCK\"", Catalog);
        Assert.Contains("public const string RoofUnlock = \"AK_ROOF_UNLOCK\"", Catalog);
    }

    [Fact]
    public void RibbonContainsRoofEditAndToggleLockButtons()
    {
        Assert.Contains("Button(CommandUiCatalog.RoofEdit)", Ribbon);
        Assert.Contains("Button(CommandUiCatalog.RoofToggleLock)", Ribbon);
        // Both are placed on the Roofs panel alongside the rafters button.
        Assert.True(
            Ribbon.IndexOf("Button(CommandUiCatalog.RoofRafters)", StringComparison.Ordinal) <
            Ribbon.IndexOf("Button(CommandUiCatalog.RoofEdit)", StringComparison.Ordinal));
        Assert.True(
            Ribbon.IndexOf("Button(CommandUiCatalog.RoofEdit)", StringComparison.Ordinal) <
            Ribbon.IndexOf("Button(CommandUiCatalog.RoofToggleLock)", StringComparison.Ordinal));
    }

    [Fact]
    public void ToggleButtonMapsToToggleCommandAndApprovedIcons()
    {
        Assert.Equal(AcKrovyCommandNames.RoofToggleLock, CommandUiCatalog.RoofToggleLock.CommandName);
        Assert.Equal("DECORAIR_AK_ROOF_TOGGLELOCK", CommandUiCatalog.RoofToggleLock.RibbonControlId);
        Assert.Equal("roof_locktoggle", CommandUiCatalog.RoofToggleLock.IconKey);
        Assert.Equal("CommandUi_RoofToggleLock_Label", CommandUiCatalog.RoofToggleLock.LabelResourceKey);
        Assert.Equal("CommandUi_RoofToggleLock_Tooltip", CommandUiCatalog.RoofToggleLock.ToolTipResourceKey);
    }

    [Fact]
    public void EditButtonMapsToEditCommandAndApprovedIcons()
    {
        Assert.Equal(AcKrovyCommandNames.RoofEdit, CommandUiCatalog.RoofEdit.CommandName);
        Assert.Equal("DECORAIR_AK_ROOF_EDIT", CommandUiCatalog.RoofEdit.RibbonControlId);
        Assert.Equal("roof_edit", CommandUiCatalog.RoofEdit.IconKey);
        Assert.Equal("CommandUi_RoofEdit_Label", CommandUiCatalog.RoofEdit.LabelResourceKey);
        Assert.Equal("CommandUi_RoofEdit_Tooltip", CommandUiCatalog.RoofEdit.ToolTipResourceKey);
    }

    [Fact]
    public void ToggleIsThinStateDispatcherReadingActualEditState()
    {
        var toggle = RoofUxSourceContractText.Member(
            Workflow,
            "public static void Toggle(",
            "public static void ResetEdits(");
        // Reads the persisted EditState, never command-message parsing.
        Assert.Contains("RoofDefinitionStore.Read(owner)", toggle);
        Assert.Contains("stored.Data.EditState", toggle);
        Assert.DoesNotContain("GetString(\"Command_RoofLock", toggle);
        Assert.DoesNotContain("GetString(\"Command_RoofUnlock", toggle);
        Assert.DoesNotContain("AlreadyLocked", toggle);
        Assert.DoesNotContain("AlreadyUnlocked", toggle);
        // Deterministic dispatch: Locked -> Unlock, Unlocked -> Lock.
        Assert.Contains("current == RoofEditState.Locked", toggle);
        Assert.Contains("RoofEditState.Unlocked", toggle);
        Assert.Contains("RoofEditState.Locked", toggle);
        // Reuses the shared apply path (not a duplicate persistence implementation).
        Assert.Contains("ApplyEditState(", toggle);
        Assert.Contains("current == RoofEditState.Locked", toggle);
    }

    [Fact]
    public void ToggleUsesItsOwnNeutralSelectionPrompt_NotUnlockPrompt()
    {
        // Toggle must pass its dedicated neutral prompt key; it must NOT reuse the
        // AK_ROOF_UNLOCK prompt.
        Assert.Contains(
            "Command_RoofToggleLock_SelectPrompt",
            Workflow);
        Assert.Contains(
            "Command_RoofUnlock_SelectPrompt",
            Workflow);
        var toggle = RoofUxSourceContractText.Member(
            Workflow,
            "public static void Toggle(",
            "public static void ResetEdits(");
        Assert.Contains("\"Command_RoofToggleLock_SelectPrompt\"", toggle);
        Assert.DoesNotContain("\"Command_RoofUnlock_SelectPrompt\"", toggle);
        // Lock / Unlock keep the existing prompt through the shared selector default.
        var selector = RoofUxSourceContractText.Member(
            Workflow,
            "private static bool TrySelectOwner(",
            "private static bool TryRebuildGeneratedSet(");
        Assert.Contains("\"Command_RoofUnlock_SelectPrompt\"", selector);
        Assert.DoesNotContain("\"Command_RoofToggleLock_SelectPrompt\"", selector);
    }

    [Fact]
    public void TogglePromptKey_ExistsInAllSixLanguagePacks()
    {
        var languages = new[] { "cs", "de", "en", "fr", "pl" };
        Assert.True(ResourceHasKey("UiStrings.resx", "Command_RoofToggleLock_SelectPrompt"));
        foreach (var lang in languages)
        {
            Assert.True(
                ResourceHasKey($"UiStrings.{lang}.resx", "Command_RoofToggleLock_SelectPrompt"),
                $"Missing Command_RoofToggleLock_SelectPrompt in {lang}");
        }
    }

    [Fact]
    public void ToggleStateDispatcherUnchanged()
    {
        var toggle = RoofUxSourceContractText.Member(
            Workflow,
            "public static void Toggle(",
            "public static void ResetEdits(");
        // Read actual EditState -> Locked => Unlock path, Unlocked => Lock path.
        Assert.Contains("stored.Data.EditState", toggle);
        Assert.Contains("current == RoofEditState.Locked", toggle);
        Assert.Contains("RoofEditState.Unlocked", toggle);
        Assert.Contains("RoofEditState.Locked", toggle);
        Assert.Contains("ApplyEditState(", toggle);
    }

    [Fact]
    public void ToggleDoesNotRegenerateOrRewriteMetadata()
    {
        var toggle = RoofUxSourceContractText.Member(
            Workflow,
            "public static void Toggle(",
            "public static void ResetEdits(");
        Assert.DoesNotContain("TryReplaceForSupportedResize", toggle);
        Assert.DoesNotContain("Materialize(", toggle);
        Assert.DoesNotContain("RoofGeneratedTimberStore", toggle);
        Assert.DoesNotContain("RoofDefinitionStore.Write", toggle);
        Assert.DoesNotContain("CreateAnchoredData", toggle);
    }

    [Fact]
    public void SharedApplyReusesExistingLockUnlockPersistence()
    {
        var apply = RoofUxSourceContractText.Member(
            Workflow,
            "private static void ApplyEditState(",
            "private static bool TrySelectOwner");
        // The shared apply is the existing Lock/Unlock persistence + sync + selectability.
        Assert.Contains("RoofGeneratedMemberOverrideRules.WithEditState", apply);
        Assert.Contains("RoofDefinitionStore.Write(owner, transaction, updated)", apply);
        Assert.Contains("RoofUnlockIndicatorService.Sync", apply);
        Assert.Contains("RoofDisplayGroupSelectabilityService.ApplyForOwner", apply);
        Assert.DoesNotContain("TryReplaceForSupportedResize", apply);
        Assert.DoesNotContain("Materialize(", apply);
        Assert.DoesNotContain("RoofGeneratedTimberStore", apply);
    }

    [Fact]
    public void TogglePreservesGroupSelectabilityViaExistingService()
    {
        // Selectability follows EditState through the existing service, no second
        // mechanism.
        Assert.Contains("RoofDisplayGroupSelectabilityService.ApplyForOwner", Workflow);
        Assert.DoesNotContain("RoofDisplayGroupSelectabilityService", RoofUxSourceContractText.Member(
            Workflow,
            "public static void Toggle(",
            "public static void ResetEdits("));
    }

    [Fact]
    public void ToggleDoesNotIntroduceDeferredOrTimerWork()
    {
        Assert.DoesNotContain("SendStringToExecute", Workflow);
        Assert.DoesNotContain("new Timer", Workflow);
        Assert.DoesNotContain("ObjectOverrule", Workflow);
        Assert.DoesNotContain("DatabaseReactor", Workflow);
    }

    [Fact]
    public void LockedDefaultRemainsLocked()
    {
        // RoofEditState default must remain Locked (unchanged lifecycle invariant).
        Assert.Equal(RoofEditState.Locked, default(RoofEditState));
    }

    private static bool ResourceHasKey(string fileName, string key)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException();
        }

        var path = Path.Combine(
            directory.FullName,
            "src", "AcKrovy.Localization", "Resources", fileName);
        var text = File.ReadAllText(path);
        return text.Contains($"<data name=\"{key}\"", StringComparison.Ordinal);
    }
}
