namespace AcKrovy.Localization;

public sealed class SettingsApplyChangeTracker
{
    private string? _acceptedFingerprint;

    public bool HasProfileChanged(string fingerprint) =>
        !string.Equals(_acceptedFingerprint, fingerprint, StringComparison.Ordinal);

    public void AcceptProfile(string fingerprint) =>
        _acceptedFingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
}

public static class SettingsApplyDispatchRules
{
    public static bool ShouldDispatch(
        SettingsSaveMode saveMode,
        bool profileChanged) =>
        saveMode is SettingsSaveMode.SelectedElements or SettingsSaveMode.AllElements ||
        profileChanged;

    public static bool ShouldPersistProfile(bool profileChanged) => profileChanged;

    public static string GetDrawingResultResourceKey(
        SettingsSaveMode saveMode,
        bool drawingChanged,
        int eligibleElements)
    {
        if (saveMode == SettingsSaveMode.SelectedElements)
        {
            if (eligibleElements <= 0)
            {
                return "SettingsWindow_NoSmartElementsSelected";
            }

            return drawingChanged
                ? "SettingsWindow_SelectedElementsApplied"
                : "SettingsWindow_SelectedElementsAlreadyMatch";
        }

        if (saveMode == SettingsSaveMode.AllElements)
        {
            return drawingChanged
                ? "SettingsWindow_AllElementsApplied"
                : "SettingsWindow_AllElementsAlreadyMatch";
        }

        throw new ArgumentOutOfRangeException(nameof(saveMode));
    }
}
