using AcKrovy.AutoCAD.Ribbon;
using AcKrovy.AutoCAD.ClassicToolbar;
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.AutoCAD.Settings;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.Runtime;
using AcKrovy.Localization;
using AcKrovy.AutoCAD.Diagnostics;

namespace AcKrovy.AutoCAD;

/// <summary>Vstupný bod plug-inu načítaného cez NETLOAD alebo .bundle balík.</summary>
public sealed class PluginEntry : IExtensionApplication
{
    public void Initialize()
    {
        try
        {
            var languageSettings = AppLanguageSettingsStore.Load();
            AppLanguageService.Apply(languageSettings.LanguageCode);
            AcKrovyDiagnostics.InitializeSession(
                "AutoCAD",
                typeof(AcApp).Assembly.GetName().Version?.ToString() ?? "unknown");

            // Ribbon môže byť pri NETLOAD ešte vo fáze inicializácie. AcKrovyRibbon
            // ho preto bezpečne vytvorí pri najbližšom idle AutoCADu.
            AcKrovyRibbon.ScheduleCreation();
            LiveGeometrySynchronizationService.Start();

            var document = AcApp.DocumentManager.MdiActiveDocument;
            document?.Editor.WriteMessage(UiStrings.MessagePluginLoaded);
        }
        catch (System.Exception exception)
        {
            AcKrovyDiagnostics.Error(
                "PluginInitializationFailed",
                "Unexpected plug-in initialization failure.",
                exception: exception);
        }
    }

    public void Terminate()
    {
        LiveGeometrySynchronizationService.Stop();
        ClassicToolbarManager.Dispose();
        AcKrovyRibbon.Dispose();
    }
}
