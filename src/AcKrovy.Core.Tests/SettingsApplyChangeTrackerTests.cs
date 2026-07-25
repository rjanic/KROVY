using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class SettingsApplyChangeTrackerTests
{
    [Fact]
    public void FirstApplyIsAllowedAndSuccessfulApplyUpdatesBaseline()
    {
        var tracker = new SettingsApplyChangeTracker();

        Assert.True(tracker.HasProfileChanged("profile-a"));

        tracker.AcceptProfile("profile-a");

        Assert.False(tracker.HasProfileChanged("profile-a"));
    }

    [Fact]
    public void ChangedProfileOrApplyModeCanBeAppliedAgain()
    {
        var tracker = new SettingsApplyChangeTracker();
        tracker.AcceptProfile("profile-a");

        Assert.True(tracker.HasProfileChanged("profile-b"));
        Assert.False(tracker.HasProfileChanged("profile-a"));
    }

    [Fact]
    public void FailedApplyDoesNotChangeBaselineUntilAccepted()
    {
        var tracker = new SettingsApplyChangeTracker();

        Assert.True(tracker.HasProfileChanged("profile-a"));
        Assert.True(tracker.HasProfileChanged("profile-a"));
    }

    [Fact]
    public void SelectionApply_IsDispatchedRepeatedlyAfterProfileBaselineIsAccepted()
    {
        var tracker = new SettingsApplyChangeTracker();
        tracker.AcceptProfile("profile-a");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var profileChanged = tracker.HasProfileChanged("profile-a");

            Assert.False(profileChanged);
            Assert.True(SettingsApplyDispatchRules.ShouldDispatch(
                SettingsSaveMode.SelectedElements,
                profileChanged));
            Assert.False(SettingsApplyDispatchRules.ShouldPersistProfile(profileChanged));
        }
    }

    [Fact]
    public void AllElementsApply_IsDispatchedWithoutProfileChanges()
    {
        Assert.True(SettingsApplyDispatchRules.ShouldDispatch(
            SettingsSaveMode.AllElements,
            profileChanged: false));
    }

    [Fact]
    public void NewElementsOnly_IsNoOpWithoutProfileChanges()
    {
        Assert.False(SettingsApplyDispatchRules.ShouldDispatch(
            SettingsSaveMode.NewElementsOnly,
            profileChanged: false));
    }

    [Fact]
    public void ChangedProfile_IsPersistedOnceButDoesNotDisableLaterSelection()
    {
        var tracker = new SettingsApplyChangeTracker();

        Assert.True(tracker.HasProfileChanged("profile-a"));
        Assert.True(SettingsApplyDispatchRules.ShouldPersistProfile(profileChanged: true));
        tracker.AcceptProfile("profile-a");

        Assert.False(tracker.HasProfileChanged("profile-a"));
        Assert.True(SettingsApplyDispatchRules.ShouldDispatch(
            SettingsSaveMode.SelectedElements,
            profileChanged: false));
    }

    [Theory]
    [InlineData(SettingsSaveMode.SelectedElements, true, 1,
        "SettingsWindow_SelectedElementsApplied")]
    [InlineData(SettingsSaveMode.SelectedElements, false, 1,
        "SettingsWindow_SelectedElementsAlreadyMatch")]
    [InlineData(SettingsSaveMode.SelectedElements, false, 0,
        "SettingsWindow_NoSmartElementsSelected")]
    [InlineData(SettingsSaveMode.AllElements, true, 1,
        "SettingsWindow_AllElementsApplied")]
    [InlineData(SettingsSaveMode.AllElements, false, 1,
        "SettingsWindow_AllElementsAlreadyMatch")]
    public void DrawingApplyResult_UsesOperationSpecificBanner(
        SettingsSaveMode saveMode,
        bool drawingChanged,
        int eligibleElements,
        string expectedResourceKey)
    {
        Assert.Equal(
            expectedResourceKey,
            SettingsApplyDispatchRules.GetDrawingResultResourceKey(
                saveMode,
                drawingChanged,
                eligibleElements));
    }
}
