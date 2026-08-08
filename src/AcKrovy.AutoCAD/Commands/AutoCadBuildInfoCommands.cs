using AcKrovy.Infrastructure.Diagnostics;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>Permanent read-only identity for the actually loaded plug-in DLL.</summary>
public static class AutoCadBuildInfoCommands
{
    [CommandMethod("AK_BUILDINFO", CommandFlags.Modal)]
    public static void WriteBuildInfo()
    {
        var editor = AcApplication.DocumentManager.MdiActiveDocument?.Editor;
        if (editor is null)
        {
            return;
        }

        try
        {
            var assembly = typeof(AutoCadBuildInfoCommands).Assembly;
            var output = BuildInfoFormatter.Format(
                BuildInfoCollector.Collect(assembly));
            editor.WriteMessage("\n" + output.Replace("\r\n", "\n"));
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                "\n=== ACAD KROVY BUILD INFO ===" +
                "\nBuildInfo=<unavailable: " +
                exception.GetType().Name +
                ">" +
                "\n=== END BUILD INFO ===");
        }
    }
}
