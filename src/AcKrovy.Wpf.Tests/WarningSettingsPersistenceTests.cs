using System.Text.Json;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class WarningSettingsPersistenceTests
{
    [Fact]
    public void ExistingUiSettingsWithoutWarnings_DefaultOn_AndOptOutRoundTripsWithOtherSettings()
    {
        var legacy = JsonSerializer.Deserialize<SettingsUiPreferences>("{\"Theme\":1,\"Width\":1600}")!.Normalize();
        Assert.True(legacy.Warnings.ConfirmAutomaticMemberDetach);
        Assert.True(legacy.Warnings.WarnDerived3DEdit);
        var disabled = legacy with { Warnings = new WarningPreferences
            { ConfirmAutomaticMemberDetach = false, WarnDerived3DEdit = false } };
        var loaded = JsonSerializer.Deserialize<SettingsUiPreferences>(JsonSerializer.Serialize(disabled))!.Normalize();
        Assert.Equal(disabled, loaded);
        Assert.Equal(SettingsTheme.Dark, loaded.Theme);
        Assert.Equal(1600, loaded.Width);
        Assert.Equal(new WarningPreferences(), (loaded with { Warnings = new() }).Warnings);
        var nullWarnings = JsonSerializer.Deserialize<SettingsUiPreferences>("{\"Warnings\":null}")!.Normalize();
        Assert.Equal(new WarningPreferences(), nullWarnings.Warnings);
    }
}
