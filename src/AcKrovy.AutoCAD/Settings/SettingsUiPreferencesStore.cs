using System.IO;
using System.Text.Json;
using AcKrovy.Localization;

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
    };

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

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ACAD_KROVY");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings-ui.json");

    public static SettingsUiPreferences Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? (JsonSerializer.Deserialize<SettingsUiPreferences>(
                    File.ReadAllText(SettingsPath),
                    JsonOptions) ?? new SettingsUiPreferences()).Normalize()
                : new SettingsUiPreferences();
        }
        catch
        {
            return new SettingsUiPreferences();
        }
    }

    public static void Save(SettingsUiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        Directory.CreateDirectory(SettingsDirectory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(preferences.Normalize(), JsonOptions));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
