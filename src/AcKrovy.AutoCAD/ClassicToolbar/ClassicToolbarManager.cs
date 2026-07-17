using System.Drawing;
using Autodesk.AutoCAD.Windows;

namespace AcKrovy.AutoCAD.ClassicToolbar;

/// <summary>
/// Klasický plávajúci/dokovateľný panel ACAD KROVY. Je postavený na PaletteSet,
/// aby fungoval aj keď používateľ zavrie Ribbon príkazom RIBBONCLOSE.
/// </summary>
internal static class ClassicToolbarManager
{
    private static readonly Guid PaletteId = new("AE3310A6-6077-4FB3-B9BE-D4A1DCC866C4");
    private static PaletteSet? _palette;

    public static bool IsVisible => _palette?.Visible == true;

    public static void Toggle()
    {
        EnsureCreated();
        _palette!.Visible = !_palette.Visible;
    }

    public static void Show()
    {
        EnsureCreated();
        _palette!.Visible = true;
    }

    public static void Hide()
    {
        if (_palette is not null)
        {
            _palette.Visible = false;
        }
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
            _palette = null;
        }
    }

    private static void EnsureCreated()
    {
        if (_palette is not null)
        {
            return;
        }

        var palette = new PaletteSet("ACAD KROVY – klasický panel", PaletteId)
        {
            Style = PaletteSetStyles.ShowAutoHideButton
                  | PaletteSetStyles.ShowCloseButton
                  | PaletteSetStyles.ShowPropertiesMenu,
            MinimumSize = new Size(188, 86),
            Size = new Size(270, 118),
        };

        palette.Add("ACAD KROVY", new ClassicToolbarControl());
        _palette = palette;
    }
}
