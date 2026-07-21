using System.Drawing;
using Autodesk.AutoCAD.Windows;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.ClassicToolbar;

/// <summary>
/// Klasický plávajúci/dokovateľný panel ACAD KROVY. Je postavený na PaletteSet,
/// aby fungoval aj keď používateľ zavrie Ribbon príkazom RIBBONCLOSE.
/// </summary>
internal static class ClassicToolbarManager
{
    private static readonly Guid PaletteId = new(CommandUiCatalog.ClassicToolbarPaletteId);
    private static PaletteSet? _palette;
    private static ClassicToolbarControl? _control;

    public static bool IsVisible => _palette?.Visible == true;

    public static void Toggle()
    {
        EnsureCreated();
        _palette!.Visible = !_palette.Visible;
        RefreshLocalizedContent();
    }

    public static void Show()
    {
        EnsureCreated();
        _palette!.Visible = true;
        RefreshLocalizedContent();
    }

    public static void Hide()
    {
        if (_palette is not null)
        {
            _palette.Visible = false;
            RefreshLocalizedContent();
        }
    }

    /// <summary>
    /// Synchronizuje iba zobrazovaný názov existujúcej palety. Nevytvára nový
    /// PaletteSet a nemení GUID, dokovanie, polohu ani workspace stav.
    /// </summary>
    public static bool SynchronizeLocalizedTitle()
    {
        if (_palette is null)
        {
            return false;
        }

        return ClassicToolbarTitleSynchronizer.TrySynchronize(title => _palette.Name = title);
    }

    /// <summary>
    /// Synchronizuje titulok a textové vlastnosti existujúcich tlačidiel bez
    /// vytvorenia nového PaletteSetu alebo toolbar controlu.
    /// </summary>
    public static bool RefreshLocalizedContent()
    {
        if (_palette is null)
        {
            return false;
        }

        SynchronizeLocalizedTitle();
        _control?.RefreshLocalizedContent();
        return true;
    }

    public static void Dispose()
    {
        // PaletteSet vlastní AutoCAD. Pri ukončení iba schováme panel a uvoľníme
        // referenciu, aby nenastal problém, ak už UI AutoCADu zaniká.
        try
        {
            if (_palette is not null)
            {
                _palette.Visible = false;
            }
        }
        catch
        {
            // AutoCAD môže byť pri ukončovaní už v stave, keď PaletteSet nie je dostupný.
        }
        finally
        {
            _control = null;
            _palette = null;
        }
    }

    private static void EnsureCreated()
    {
        if (_palette is not null)
        {
            // Existujúci PaletteSet za behu nereštartujeme ani neduplikujeme: AutoCAD
            // si k stabilnému GUID ukladá dokovanie a polohu. Aktualizujeme iba
            // lokalizované texty existujúceho panelu a jeho tlačidiel.
            RefreshLocalizedContent();
            return;
        }

        var localizedTitle = UiStrings.ToolbarTitle;
        var palette = new PaletteSet(localizedTitle, PaletteId)
        {
            Style = PaletteSetStyles.ShowAutoHideButton
                  | PaletteSetStyles.ShowCloseButton
                  | PaletteSetStyles.ShowPropertiesMenu,
            MinimumSize = new Size(188, 86),
            Size = new Size(270, 118),
        };

        var control = new ClassicToolbarControl();
        palette.Add(UiStrings.ToolbarContentTitle, control);
        // AutoCAD obnovuje workspace stav podľa stabilného GUID a môže pritom vrátiť
        // starý uložený názov. Zobrazovaný názov preto nastavíme ešte raz po vytvorení
        // obsahu; GUID, dokovanie, poloha ani ostatný workspace stav sa nemenia.
        _control = control;
        _palette = palette;
        RefreshLocalizedContent();
    }
}
