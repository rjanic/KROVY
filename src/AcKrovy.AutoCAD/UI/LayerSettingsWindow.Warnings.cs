using System.Windows;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.Core.Models;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

public partial class LayerSettingsWindow
{
    private WarningPreferences _warnings = new();

    private void InitializeWarningPreferences() => _warnings = _loadedUiPreferences.Warnings;

    public bool ConfirmAutomaticMemberDetach
    {
        get => _warnings.ConfirmAutomaticMemberDetach;
        set
        {
            if (value == _warnings.ConfirmAutomaticMemberDetach) return;
            SaveWarningPreferences(_warnings with { ConfirmAutomaticMemberDetach = value });
        }
    }

    public bool WarnDerived3DEdit
    {
        get => _warnings.WarnDerived3DEdit;
        set
        {
            if (value == _warnings.WarnDerived3DEdit) return;
            SaveWarningPreferences(_warnings with { WarnDerived3DEdit = value });
        }
    }

    private void RestoreWarnings_Click(object sender, RoutedEventArgs e) =>
        SaveWarningPreferences(AcKrovy.Core.Services.WarningPreferenceRules.RestoreWarnings());

    private void SaveWarningPreferences(WarningPreferences preferences)
    {
        try
        {
            MemberWarningPreferenceService.Save(preferences);
            _warnings = preferences;
        }
        catch (Exception ex)
        {
            AcKrovyDiagnostics.Error("MemberWarningPreferences", ex.Message, exception: ex);
            ShowStatus("Warning_PreferenceSaveFailed", StatusBannerSeverity.Error);
        }
        OnPropertyChanged(nameof(ConfirmAutomaticMemberDetach));
        OnPropertyChanged(nameof(WarnDerived3DEdit));
    }
}
