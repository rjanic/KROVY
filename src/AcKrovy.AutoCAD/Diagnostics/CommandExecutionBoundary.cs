using AcKrovy.Localization;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Diagnostics;

internal static class CommandExecutionBoundary
{
    public static void Execute(string commandName, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        AcKrovyDiagnostics.Info("CommandStarted", "Command started.", commandName);
        try
        {
            action();
            AcKrovyDiagnostics.Info("CommandCompleted", "Command completed.", commandName);
        }
        catch (Exception exception)
        {
            AcKrovyDiagnostics.Error(
                "CommandFailed",
                "Unexpected command failure.",
                commandName,
                exception);

            var document = AcApp.DocumentManager.MdiActiveDocument;
            document?.Editor.WriteMessage(UiStrings.GetString("Command_UnexpectedFailure"));
        }
    }
}
