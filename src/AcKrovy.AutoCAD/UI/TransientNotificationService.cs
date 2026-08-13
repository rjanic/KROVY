using AcKrovy.AutoCAD.Settings;
using AcKrovy.Localization;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.UI;

/// <summary>Minimal host-level entry point for localized, non-destructive notices.</summary>
internal static class TransientNotificationService
{
    public static void Show(string titleResourceKey, string bodyResourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(titleResourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyResourceKey);

        var window = new TransientNotificationWindow(
            UiStrings.GetString(titleResourceKey),
            UiStrings.GetString(bodyResourceKey),
            SettingsUiPreferencesStore.Load().Theme);
        SettingsWindowOwner.TryAssign(window, TryGetAutoCadMainWindowHandle());
        AcApp.ShowModalWindow(window);
    }

    private static IntPtr TryGetAutoCadMainWindowHandle()
    {
        try
        {
            return AcApp.MainWindow?.Handle ?? IntPtr.Zero;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }
}
