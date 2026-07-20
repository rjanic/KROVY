using System.Windows.Controls;
using WpfOrientation = System.Windows.Controls.Orientation;
using Autodesk.Windows;
using AcKrovy.Localization;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Ribbon;

/// <summary>
/// Dynamicky pridáva kartu ACAD KROVY do Ribbonu po NETLOAD.
/// Ribbon AutoCADu nemusí byť pripravený počas Initialize(), preto
/// sa prvý pokus vykoná na Application.Idle a dá sa ručne zopakovať cez AK_RIBBON.
/// </summary>
internal static class AcKrovyRibbon
{
    private static bool _idleSubscribed;

    public static void ScheduleCreation()
    {
        if (_idleSubscribed)
        {
            return;
        }

        _idleSubscribed = true;
        AcApp.Idle += OnApplicationIdle;
    }

    public static bool EnsureCreated(bool activateTab)
    {
        var ribbon = ComponentManager.Ribbon;
        if (ribbon is null)
        {
            return false;
        }

        var tab = ribbon.Tabs.FirstOrDefault(item =>
            string.Equals(item.Id, CommandUiCatalog.RibbonTabId, StringComparison.OrdinalIgnoreCase));

        if (tab is null)
        {
            tab = BuildTab();
            ribbon.Tabs.Add(tab);
        }

        if (activateTab)
        {
            tab.IsActive = true;
        }

        return true;
    }

    /// <summary>
    /// Znovu vytvorí kartu s rovnakým technickým ID a aktuálnymi resource textami.
    /// Budúci prepínač jazyka môže túto metódu zavolať po zmene CurrentUICulture.
    /// </summary>
    internal static bool RebuildLocalizedUi(bool activateTab)
    {
        var ribbon = ComponentManager.Ribbon;
        if (ribbon is null)
        {
            return false;
        }

        var existing = ribbon.Tabs.FirstOrDefault(item =>
            string.Equals(item.Id, CommandUiCatalog.RibbonTabId, StringComparison.OrdinalIgnoreCase));
        var shouldActivate = activateTab || existing?.IsActive == true;
        if (existing is not null)
        {
            ribbon.Tabs.Remove(existing);
        }

        var rebuilt = BuildTab();
        ribbon.Tabs.Add(rebuilt);
        rebuilt.IsActive = shouldActivate;
        return true;
    }

    public static void Dispose()
    {
        if (_idleSubscribed)
        {
            AcApp.Idle -= OnApplicationIdle;
            _idleSubscribed = false;
        }

        // Pri klasickom NETLOAD sa Terminate volá až pri ukončení AutoCADu.
        // Odstránenie je však bezpečné, ak bude doplnok neskôr manažovaný inak.
        try
        {
            var ribbon = ComponentManager.Ribbon;
            var tab = ribbon?.Tabs.FirstOrDefault(item =>
                string.Equals(item.Id, CommandUiCatalog.RibbonTabId, StringComparison.OrdinalIgnoreCase));
            if (tab is not null)
            {
                ribbon!.Tabs.Remove(tab);
            }
        }
        catch
        {
            // Pri vypínaní AutoCADu už Ribbon nemusí byť dostupný.
        }
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        try
        {
            if (EnsureCreated(activateTab: false))
            {
                AcApp.Idle -= OnApplicationIdle;
                _idleSubscribed = false;
            }
        }
        catch
        {
            // Neprerušuj AutoCAD pri oneskorenom vytváraní UI. AK_RIBBON umožní
            // vytvorenie skúsiť znovu po úplnom načítaní používateľského rozhrania.
        }
    }

    private static RibbonTab BuildTab()
    {
        var tab = new RibbonTab
        {
            Id = CommandUiCatalog.RibbonTabId,
            Title = UiStrings.RibbonTabTitle,
        };

        tab.Panels.Add(BuildPanel(UiStrings.RibbonPanelElements, new[]
        {
            Button(CommandUiCatalog.Rafter),
            Button(CommandUiCatalog.WallPlate),
            Button(CommandUiCatalog.Purlin),
            Button(CommandUiCatalog.Post),
            Button(CommandUiCatalog.CollarTie),
            Button(CommandUiCatalog.Brace),
            Button(CommandUiCatalog.TieBeam),
        }));

        tab.Panels.Add(BuildPanel(UiStrings.RibbonPanelData, new[]
        {
            Button(CommandUiCatalog.Assign),
            Button(CommandUiCatalog.Edit),
            Button(CommandUiCatalog.Inspect),
            Button(CommandUiCatalog.Recalc),
        }));

        tab.Panels.Add(BuildPanel(UiStrings.RibbonPanelReports, new[]
        {
            Button(CommandUiCatalog.Report),
            Button(CommandUiCatalog.ReportAll),
        }));

        tab.Panels.Add(BuildPanel(UiStrings.RibbonPanelSettings, new[]
        {
            Button(CommandUiCatalog.Settings),
        }));

        tab.Panels.Add(BuildPanel(UiStrings.RibbonPanelLabels, new[]
        {
            Button(CommandUiCatalog.Labels),
        }));

        tab.Panels.Add(BuildPanel(UiStrings.RibbonPanelToolbar, new[]
        {
            Button(CommandUiCatalog.Toolbar),
        }));

        return tab;
    }

    private static RibbonPanel BuildPanel(string title, IEnumerable<RibbonButton> buttons)
    {
        var source = new RibbonPanelSource
        {
            Title = title,
        };

        foreach (var button in buttons)
        {
            source.Items.Add(button);
        }

        return new RibbonPanel
        {
            Source = source,
        };
    }

    private static RibbonButton Button(CommandUiDescriptor descriptor) => new()
    {
        Id = descriptor.RibbonControlId,
        Text = descriptor.GetLabel(),
        Size = RibbonItemSize.Large,
        Orientation = WpfOrientation.Vertical,
        ShowText = true,
        ShowImage = true,
        IsToolTipEnabled = true,
        LargeImage = RibbonIconProvider.Get(descriptor.IconKey, 32),
        Image = RibbonIconProvider.Get(descriptor.IconKey, 16),
        ToolTip = descriptor.GetToolTip(),
        CommandParameter = descriptor.CommandName,
        CommandHandler = RibbonCommandHandler.Instance,
    };
}
