using System.Windows.Controls;
using WpfOrientation = System.Windows.Controls.Orientation;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Ribbon;

/// <summary>
/// Dynamicky pridáva kartu ACAD KROVY do Ribbonu po NETLOAD.
/// Ribbon AutoCADu nemusí byť pripravený počas Initialize(), preto
/// sa prvý pokus vykoná na Application.Idle a dá sa ručne zopakovať cez AK_RIBBON.
/// </summary>
internal static class AcKrovyRibbon
{
    private const string TabId = "DECORAIR_ACAD_KROVY_TAB";
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
            string.Equals(item.Id, TabId, StringComparison.OrdinalIgnoreCase));

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
                string.Equals(item.Id, TabId, StringComparison.OrdinalIgnoreCase));
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
            Id = TabId,
            Title = "ACAD KROVY",
        };

        tab.Panels.Add(BuildPanel("Prvky", new[]
        {
            Button("DECORAIR_AK_RAFTER", "Krokva", "rafter", "AK_KROKVA", "Priradí vybraným čiaram typ Krokva."),
            Button("DECORAIR_AK_WALLPLATE", "Pomúrnica", "wallplate", "AK_POMURNICA", "Priradí vybraným čiaram typ Pomúrnica."),
            Button("DECORAIR_AK_PURLIN", "Väznica", "purlin", "AK_VAZNICA", "Priradí vybraným čiaram typ Väznica."),
            Button("DECORAIR_AK_POST", "Stĺpik", "post", "AK_STLPIK", "Priradí vybraným čiaram typ Stĺpik."),
            Button("DECORAIR_AK_COLLARTIE", "Klieština", "collartie", "AK_KLIESTINA", "Priradí vybraným čiaram typ Klieština / hambálok."),
            Button("DECORAIR_AK_BRACE", "Vzpera", "brace", "AK_VZPERA", "Priradí vybraným čiaram typ Vzpera."),
            Button("DECORAIR_AK_TIEBEAM", "Väzný trám", "tiebeam", "AK_VAZNYTRAM", "Priradí vybraným čiaram typ Väzný trám."),
        }));

        tab.Panels.Add(BuildPanel("Údaje", new[]
        {
            Button("DECORAIR_AK_ASSIGN", "Priradiť údaje", "assign", "AK_ASSIGN", "Priradí údaje vybraným čiaram alebo polyline."),
            Button("DECORAIR_AK_EDIT", "Upraviť", "edit", "AK_EDIT", "Hromadne upraví zaškrtnuté hodnoty vybraných prvkov."),
            Button("DECORAIR_AK_INSPECT", "Skontrolovať", "inspect", "AK_INSPECT", "Zobrazí údaje jedného prvku ACAD KROVY."),
            Button("DECORAIR_AK_RECALC", "Prepočítať", "recalc", "AK_RECALC", "Skontroluje prepočty všetkých prvkov vo výkrese."),
        }));

        tab.Panels.Add(BuildPanel("Výkaz", new[]
        {
            Button("DECORAIR_AK_REPORT", "Výkaz z výberu", "report_selection", "AK_REPORT", "Vloží výkaz reziva z aktuálne vybraných prvkov."),
            Button("DECORAIR_AK_REPORTALL", "Výkaz všetkého", "report_all", "AK_REPORTALL", "Vloží výkaz reziva zo všetkých prvkov ACAD KROVY vo výkrese."),
        }));

        tab.Panels.Add(BuildPanel("Nastavenia", new[]
        {
            Button("DECORAIR_AK_SETTINGS", "Nastavenia", "settings", "AK_SETTINGS", "Nastaví názvy hladín a farby jednotlivých typov krovu."),
        }));

        tab.Panels.Add(BuildPanel("Popisy", new[]
        {
            Button("DECORAIR_AK_LABELS", "Obnoviť popisy", "labels", "AK_LABELS", "Vytvorí alebo obnoví automatické popisy všetkých prvkov krovu."),
        }));

        tab.Panels.Add(BuildPanel("Panel", new[]
        {
            Button("DECORAIR_AK_TOOLBAR", "Klasický panel", "toolbar", "AK_TOOLBAR", "Zobrazí alebo skryje klasický plávajúci panel malých ikon."),
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

    private static RibbonButton Button(
        string id,
        string text,
        string iconKey,
        string command,
        string toolTip) => new()
    {
        Id = id,
        Text = text,
        Size = RibbonItemSize.Large,
        Orientation = WpfOrientation.Vertical,
        ShowText = true,
        ShowImage = true,
        IsToolTipEnabled = true,
        LargeImage = RibbonIconProvider.Get(iconKey, 32),
        Image = RibbonIconProvider.Get(iconKey, 16),
        ToolTip = toolTip,
        CommandParameter = command,
        CommandHandler = RibbonCommandHandler.Instance,
    };
}
