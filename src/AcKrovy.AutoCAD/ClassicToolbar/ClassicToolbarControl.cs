using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using DrawingImage = System.Drawing.Image;
using AcKrovy.AutoCAD.Ribbon;

namespace AcKrovy.AutoCAD.ClassicToolbar;

/// <summary>
/// Kompaktná sada 16×16 ikon. Panel je úmyselne bez textových tlačidiel,
/// aby sa správal ako tradičný AutoCAD toolbar. Popisy sa zobrazia v tooltipe.
/// </summary>
internal sealed class ClassicToolbarControl : UserControl
{
    private readonly ToolTip _toolTip = new()
    {
        AutoPopDelay = 8000,
        InitialDelay = 450,
        ReshowDelay = 120,
        ShowAlways = true,
    };

    public ClassicToolbarControl()
    {
        BackColor = Color.FromArgb(45, 52, 60);
        ForeColor = Color.White;
        Margin = Padding.Empty;
        Padding = new Padding(4);
        MinimumSize = new Size(188, 86);

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(1),
            Margin = Padding.Empty,
        };

        AddSection(layout, new[]
        {
            Item("rafter", "Krokva", "AK_KROKVA", "Priradí vybraným čiaram typ Krokva."),
            Item("wallplate", "Pomúrnica", "AK_POMURNICA", "Priradí vybraným čiaram typ Pomúrnica."),
            Item("purlin", "Väznica", "AK_VAZNICA", "Priradí vybraným čiaram typ Väznica."),
            Item("post", "Stĺpik", "AK_STLPIK", "Priradí vybraným čiaram typ Stĺpik."),
            Item("collartie", "Klieština", "AK_KLIESTINA", "Priradí vybraným čiaram typ Klieština / hambálok."),
            Item("brace", "Vzpera", "AK_VZPERA", "Priradí vybraným čiaram typ Vzpera."),
            Item("tiebeam", "Väzný trám", "AK_VAZNYTRAM", "Priradí vybraným čiaram typ Väzný trám."),
        });

        AddSeparator(layout);

        AddSection(layout, new[]
        {
            Item("assign", "Priradiť údaje", "AK_ASSIGN", "Priradí údaje vybraným čiaram alebo polyline."),
            Item("edit", "Upraviť", "AK_EDIT", "Hromadne upraví označené údaje prvkov."),
            Item("inspect", "Skontrolovať", "AK_INSPECT", "Zobrazí údaje jedného prvku."),
            Item("recalc", "Prepočítať", "AK_RECALC", "Skontroluje prepočty všetkých prvkov."),
        });

        AddSeparator(layout);

        AddSection(layout, new[]
        {
            Item("report_selection", "Výkaz z výberu", "AK_REPORT", "Vloží výkaz z označených prvkov."),
            Item("report_all", "Výkaz všetkého", "AK_REPORTALL", "Vloží výkaz zo všetkých prvkov."),
            Item("labels", "Obnoviť automatické popisy", "AK_LABELS", "Vytvorí alebo obnoví popisy všetkých prvkov krovu."),
            Item("settings", "Nastavenia prvkov a hladín", "AK_SETTINGS", "Nastaví názvy hladín a farby prvkov."),
        });

        Controls.Add(layout);
    }

    private void AddSection(FlowLayoutPanel layout, IEnumerable<ToolbarItem> items)
    {
        foreach (var item in items)
        {
            var button = new Button
            {
                AccessibleName = item.Title,
                AccessibleDescription = item.ToolTip,
                AutoSize = false,
                BackColor = Color.FromArgb(54, 63, 73),
                FlatAppearance =
                {
                    BorderColor = Color.FromArgb(85, 96, 108),
                    MouseDownBackColor = Color.FromArgb(33, 40, 47),
                    MouseOverBackColor = Color.FromArgb(72, 84, 97),
                },
                FlatStyle = FlatStyle.Flat,
                Image = ClassicToolbarIconProvider.Get(item.IconKey),
                Margin = new Padding(2),
                Size = new Size(28, 28),
                TabStop = false,
                UseVisualStyleBackColor = false,
            };

            button.Click += (_, _) => AcKrovyCommandDispatcher.Execute(item.Command);
            _toolTip.SetToolTip(button, item.Title + Environment.NewLine + item.ToolTip);
            layout.Controls.Add(button);
        }
    }

    private static void AddSeparator(FlowLayoutPanel layout)
    {
        layout.Controls.Add(new Panel
        {
            BackColor = Color.FromArgb(112, 122, 132),
            Height = 24,
            Margin = new Padding(4, 3, 4, 3),
            Width = 1,
        });
    }

    private static ToolbarItem Item(string iconKey, string title, string command, string tooltip) =>
        new(iconKey, title, command, tooltip);

    private sealed record ToolbarItem(string IconKey, string Title, string Command, string ToolTip);
}

/// <summary>Načíta 16×16 PNG bez zamknutia súborov v priečinku s DLL.</summary>
internal static class ClassicToolbarIconProvider
{
    private static readonly Dictionary<string, DrawingImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static DrawingImage? Get(string iconKey)
    {
        if (Cache.TryGetValue(iconKey, out var cached))
        {
            return cached;
        }

        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var imagePath = string.IsNullOrWhiteSpace(assemblyDirectory)
            ? null
            : Path.Combine(assemblyDirectory, "Resources", "Icons", $"ak_{iconKey}_16.png");

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            Cache[iconKey] = null;
            return null;
        }

        using var stream = File.OpenRead(imagePath);
        using var original = DrawingImage.FromStream(stream);
        var image = new Bitmap(original);
        Cache[iconKey] = image;
        return image;
    }
}
