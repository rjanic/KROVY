using AcKrovy.AutoCAD.Ribbon;
using AcKrovy.AutoCAD.ClassicToolbar;
using AcKrovy.AutoCAD.Infrastructure;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.Runtime;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD;

/// <summary>Vstupný bod plug-inu načítaného cez NETLOAD alebo .bundle balík.</summary>
public sealed class PluginEntry : IExtensionApplication
{
    public void Initialize()
    {
        // Ribbon môže byť pri NETLOAD ešte vo fáze inicializácie. AcKrovyRibbon
        // ho preto bezpečne vytvorí pri najbližšom idle AutoCADu.
        AcKrovyRibbon.ScheduleCreation();
        LiveGeometrySynchronizationService.Start();

        var document = AcApp.DocumentManager.MdiActiveDocument;
        document?.Editor.WriteMessage(UiStrings.MessagePluginLoaded);
    }

    public void Terminate()
    {
        LiveGeometrySynchronizationService.Stop();
        ClassicToolbarManager.Dispose();
        AcKrovyRibbon.Dispose();
    }
}
