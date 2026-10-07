using System.Text.Json;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class WarningPreferenceTests
{
    [Fact]
    public void DefaultsAndOldSettings_AreOn_StoredFalseSurvivesReload()
    {
        var defaults = JsonSerializer.Deserialize<WarningPreferences>("{}")!;
        Assert.True(defaults.ConfirmAutomaticMemberDetach);
        Assert.True(defaults.WarnDerived3DEdit);
        var saved = defaults with { ConfirmAutomaticMemberDetach = false, WarnDerived3DEdit = false };
        Assert.Equal(saved, JsonSerializer.Deserialize<WarningPreferences>(JsonSerializer.Serialize(saved)));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    public void Confirmation_OnlyAcceptedOptOutChangesPreference(bool yes, bool optOut, bool nextEnabled)
    {
        var count = 0;
        var result = WarningPreferenceRules.ConfirmDetach(new(), true, () =>
        {
            count++;
            return new(yes, optOut);
        });
        Assert.Equal(1, count);
        Assert.Equal(yes, result.Accepted);
        Assert.True(result.ConfirmationShown);
        Assert.False(result.AutomaticConfirm);
        Assert.Equal(nextEnabled, result.Preferences.ConfirmAutomaticMemberDetach);
        Assert.Equal(!nextEnabled, result.WarningPreferenceChanged);
    }

    [Fact]
    public void AcceptedOptOut_NextAutomaticEditAutoAccepts_IndependentNeverPrompts()
    {
        var first = WarningPreferenceRules.ConfirmDetach(new(), true, () => new(true, true));
        var next = WarningPreferenceRules.ConfirmDetach(first.Preferences, true, Unexpected);
        Assert.True(next.Accepted);
        Assert.True(next.AutomaticConfirm);
        Assert.False(next.ConfirmationShown);
        var independent = WarningPreferenceRules.ConfirmDetach(new(), false, Unexpected);
        Assert.True(independent.Accepted);
        Assert.False(independent.AutomaticConfirm);
        Assert.False(independent.ConfirmationShown);
    }

    [Fact]
    public void DerivedEdit_RemainsRejectedAfterWarningOptOut_AndResetRestoresBoth()
    {
        var calls = 0;
        var first = WarningPreferenceRules.RejectDerived3DEdit(new(), () => { calls++; return true; });
        Assert.False(first.Accepted);
        Assert.True(first.ConfirmationShown);
        Assert.True(first.WarningPreferenceChanged);
        Assert.False(first.Preferences.WarnDerived3DEdit);
        var next = WarningPreferenceRules.RejectDerived3DEdit(first.Preferences, () => { calls++; return true; });
        Assert.False(next.Accepted);
        Assert.False(next.ConfirmationShown);
        Assert.Equal(1, calls);
        Assert.Equal(new WarningPreferences(), WarningPreferenceRules.RestoreWarnings());
    }

    [Theory]
    [InlineData(false, false, true)] // AUTO + retain source + YES
    [InlineData(false, false, false)] // AUTO + retain source + NO
    [InlineData(true, false, true)] // Independent: no dialog
    [InlineData(false, true, true)] // AUTO + erase source + YES
    [InlineData(false, true, false)] // AUTO + erase source + NO
    public void MirrorDecision_DoesNotDependOnNativeEraseSource(bool independent, bool eraseSource, bool yes)
    {
        var calls = 0;
        var decision = WarningPreferenceRules.ConfirmDetach(new(), !independent, () => { calls++; return new(yes, false); });
        Assert.Equal(independent ? 0 : 1, calls);
        Assert.Equal(independent || yes, decision.Accepted);
        Assert.Equal(!independent, decision.ConfirmationShown);
        // Native erase-source is not KROVY consent, and does not change preferences.
        _ = eraseSource;
        Assert.Equal(new WarningPreferences(), decision.Preferences);
    }

    [Fact]
    public void MultiMemberMirror_OneOperationDecision_RejectsWholeSelection()
    {
        var independentMembers = new[] { false, true, false, false };
        var calls = 0;
        var decision = WarningPreferenceRules.ConfirmDetach(new(), independentMembers.Any(independent => !independent),
            () => { calls++; return new(false, true); });
        Assert.Equal(1, calls);
        Assert.False(decision.Accepted);
        Assert.True(decision.Preferences.ConfirmAutomaticMemberDetach);
    }

    [Fact]
    public void SharedAdapterRoutingAndMirrorRollback_PrecedeIdentityRebuildAndSuppression()
    {
        var engine = Read("Infrastructure/RoofOrdinaryCopyLifecycleService.cs");
        var gate = engine.IndexOf("MemberWarningPreferenceService.ConfirmAutomaticDetach", StringComparison.Ordinal);
        var identity = engine.IndexOf("var newId = Guid.NewGuid()", StringComparison.Ordinal);
        var rollback = engine.IndexOf("RollbackMirror(document, transaction", gate, StringComparison.Ordinal);
        var commit = engine.IndexOf("transaction.Commit()", rollback, StringComparison.Ordinal);
        var terminal = engine.IndexOf("return context.Handled;", commit, StringComparison.Ordinal);
        Assert.True(gate < rollback && rollback < commit && commit < terminal && terminal < identity);
        var rejected = engine[rollback..terminal];
        Assert.DoesNotContain("CreateMember(", rejected);
        Assert.DoesNotContain("RecalculateDesignations(", rejected);
        Assert.DoesNotContain("TryWriteSuppressOverride(", rejected);
        var restore = engine[engine.IndexOf("private static void RollbackMirror", StringComparison.Ordinal)..
            engine.IndexOf("private static string RejectedMirrorMessage", StringComparison.Ordinal)];
        foreach (var call in new[] { "entity.Erase()", "RoofOrdinaryGripLifecycleService.Restore(", "RoofDefinitionStore.Read(saved.Copy)",
                     "RoofOrdinaryGripLifecycleService.RestoreGroup", "RoofOrdinaryGripLifecycleService.Verify", "clones.Any(id => !id.IsErased)" })
            Assert.Contains(call, restore);
        foreach (var path in new[] { "Infrastructure/RoofOrdinaryGripLifecycleService.cs", "Infrastructure/RoofGeneratedMemberManualEditService.cs" })
            Assert.Contains("MemberWarningPreferenceService.ConfirmAutomaticDetach().Accepted", Read(path));
        var service = Read("UI/MemberWarningPreferenceService.cs");
        Assert.Contains("SettingsUiPreferencesStore.Load()", service);
        Assert.Contains("SettingsUiPreferencesStore.Save(settings with { Warnings = preferences })", service);
        Assert.Contains("Warning_Derived3DEditCancelled", service);
        Assert.DoesNotContain("MessageBox", service);
    }

    private static WarningDialogResponse Unexpected() => throw new InvalidOperationException("Dialog must not be shown.");
    private static string Read(string path)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", path));
    }
}
