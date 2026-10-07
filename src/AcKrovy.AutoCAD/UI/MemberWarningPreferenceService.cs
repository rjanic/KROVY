using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.UI;

/// <summary>One preference path for every member command, using the existing UI settings store.</summary>
internal static class MemberWarningPreferenceService
{
    internal static WarningPreferences Load() => SettingsUiPreferencesStore.Load().Warnings;

    internal static void Save(WarningPreferences preferences)
    {
        var settings = SettingsUiPreferencesStore.Load();
        SettingsUiPreferencesStore.Save(settings with { Warnings = preferences });
    }

    internal static MemberWarningDecision ConfirmAutomaticDetach(bool hasAutomaticMember = true)
    {
        var result = WarningPreferenceRules.ConfirmDetach(Load(), hasAutomaticMember, () =>
        {
            var window = new RoofIndependentOrdinaryDetachWindow();
            AssignOwner(window);
            var accepted = AcApp.ShowModalWindow(window) == true;
            return new(accepted, window.DoNotShowAgain);
        });
        result = PersistChange(result);
#if DEBUG
        if (hasAutomaticMember)
            AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                $"\nROOF_MEMBER_CONFIRMATION decision={(result.Accepted ? "YES" : "NO")} " +
                $"confirmationShown={result.ConfirmationShown} automaticConfirm={result.AutomaticConfirm} " +
                $"warningPreferenceChanged={result.WarningPreferenceChanged}");
#endif
        return result;
    }

    internal static void WarnDerived3DEdit()
    {
        var result = WarningPreferenceRules.RejectDerived3DEdit(Load(), () =>
        {
            var window = new RoofPhysical3DWarningWindow();
            AssignOwner(window);
            AcApp.ShowModalWindow(window);
            return window.DoNotShowAgain;
        });
        result = PersistChange(result);
        if (!result.ConfirmationShown)
            AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                "\n" + UiStrings.GetString("Warning_Derived3DEditCancelled"));
    }

    private static MemberWarningDecision PersistChange(MemberWarningDecision result)
    {
        if (!result.WarningPreferenceChanged) return result;
        try { Save(result.Preferences); return result; }
        catch (Exception ex)
        {
            // A preference persistence failure must not alter the lifecycle decision.
            AcKrovy.AutoCAD.Diagnostics.AcKrovyDiagnostics.Error("MemberWarningPreferences", ex.Message, exception: ex);
            AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                "\n" + UiStrings.GetString("Warning_PreferenceSaveFailed"));
            return result with { WarningPreferenceChanged = false };
        }
    }

    private static void AssignOwner(System.Windows.Window window)
    {
        try { SettingsWindowOwner.TryAssign(window, AcApp.MainWindow?.Handle ?? IntPtr.Zero); }
        catch (Exception) { /* Owner assignment is best-effort. */ }
    }
}
