using System.Windows.Input;
using Autodesk.Windows;
using AcKrovy.Localization;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Ribbon;

/// <summary>
/// Prepojí tlačidlo ribbonu s existujúcim príkazom ACAD KROVY.
/// Príkaz sa vloží do príkazového riadka aktívneho výkresu a zachová
/// štandardné správanie AutoCADu vrátane PickFirst výberu.
/// </summary>
internal sealed class RibbonCommandHandler : ICommand
{
    public static RibbonCommandHandler Instance { get; } = new();

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => TryGetCommand(parameter, out _);

    public void Execute(object? parameter)
    {
        if (!TryGetCommand(parameter, out var command))
        {
            return;
        }

        AcKrovyCommandDispatcher.Execute(command);
    }

    private static bool TryGetCommand(object? parameter, out string command)
    {
        command = parameter switch
        {
            RibbonButton { CommandParameter: string value } => value,
            string value => value,
            _ => string.Empty,
        };

        return !string.IsNullOrWhiteSpace(command);
    }
}


/// <summary>
/// Jedno miesto pre bezpečné spúšťanie príkazov z vlastných UI prvkov.
/// Ribbon aj klasický panel používajú presne jedno ukončenie príkazu, aby
/// interaktívny výber v AutoCADe neprebral nadbytočný Enter.
/// </summary>
internal static class AcKrovyCommandDispatcher
{
    public static void Execute(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var document = AcApp.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        document.SendStringToExecute(CommandMacroBuilder.Build(command), true, false, false);
    }
}
