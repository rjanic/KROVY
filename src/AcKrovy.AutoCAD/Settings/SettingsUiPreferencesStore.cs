using System.IO;
using System.Text.Json;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.Infrastructure.Diagnostics;
using AcKrovy.Localization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.AutoCAD.Settings;

internal sealed record SettingsUiPreferences
{
    public SettingsTheme Theme { get; init; } = SettingsTheme.Light;
    public SettingsWindowTabKind SelectedSection { get; init; } = SettingsWindowTabKind.Layers;
    public double Width { get; init; } = SettingsFashionLookRules.DefaultWindowWidth;
    public double Height { get; init; } = SettingsFashionLookRules.DefaultWindowHeight;
    public double? Left { get; init; }
    public double? Top { get; init; }
    public bool IsMaximized { get; init; }
    public RoofRafterPreferences? AutomaticRafterPreferences { get; init; }

    public SettingsUiPreferences Normalize() => this with
    {
        Theme = SettingsFashionLookRules.NormalizeTheme(Theme),
        SelectedSection = SettingsFashionLookRules.NormalizeSection(SelectedSection),
        Width = NormalizeDimension(
            Width,
            SettingsFashionLookRules.MinimumWindowWidth,
            SettingsFashionLookRules.DefaultWindowWidth),
        Height = NormalizeDimension(
            Height,
            SettingsFashionLookRules.MinimumWindowHeight,
            SettingsFashionLookRules.DefaultWindowHeight),
        Left = NormalizeCoordinate(Left),
        Top = NormalizeCoordinate(Top),
        AutomaticRafterPreferences = NormalizeRafterPreferences(AutomaticRafterPreferences),
    };

    private static RoofRafterPreferences? NormalizeRafterPreferences(
        RoofRafterPreferences? preferences) =>
        preferences is not null &&
        double.IsFinite(preferences.WidthMm) && preferences.WidthMm > 0d &&
        double.IsFinite(preferences.HeightMm) && preferences.HeightMm > 0d &&
        double.IsFinite(preferences.MaximumSpacingMm) && preferences.MaximumSpacingMm > 0d &&
        !string.IsNullOrWhiteSpace(preferences.Material)
            ? preferences with { Material = preferences.Material.Trim() }
            : null;

    private static double NormalizeDimension(double value, double minimum, double fallback) =>
        double.IsFinite(value) && value >= minimum ? value : fallback;

    private static double? NormalizeCoordinate(double? value) =>
        value is { } coordinate && double.IsFinite(coordinate) ? coordinate : null;
}

internal static class SettingsUiPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static SettingsUiPreferences Load() =>
        AcKrovyDiagnostics.Settings.Load(
            LocalSettingsPaths.UiPreferences,
            SettingsConfigurationSubject.SettingsUiPreferences,
            json => JsonSerializer.Deserialize<SettingsUiPreferences>(json, JsonOptions)?.Normalize(),
            () => new SettingsUiPreferences()).Value;

    public static void Save(SettingsUiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        AcKrovyDiagnostics.Settings.Save(
            LocalSettingsPaths.UiPreferences,
            SettingsConfigurationSubject.SettingsUiPreferences,
            JsonSerializer.Serialize(preferences.Normalize(), JsonOptions));
    }
}
