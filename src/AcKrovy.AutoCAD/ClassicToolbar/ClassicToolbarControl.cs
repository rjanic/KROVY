using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using DrawingImage = System.Drawing.Image;
using AcKrovy.AutoCAD.Ribbon;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.ClassicToolbar;

/// <summary>
/// Kompaktná sada 16×16 ikon. Panel je úmyselne bez textových tlačidiel,
/// aby sa správal ako tradičný AutoCAD toolbar. Popisy sa zobrazia v tooltipe.
/// </summary>
internal sealed class ClassicToolbarControl : UserControl
{
    private readonly Dictionary<string, Button> _buttonsByControlId =
        new(StringComparer.OrdinalIgnoreCase);
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
            CommandUiCatalog.Rafter,
            CommandUiCatalog.WallPlate,
            CommandUiCatalog.Purlin,
            CommandUiCatalog.Post,
            CommandUiCatalog.CollarTie,
            CommandUiCatalog.Brace,
            CommandUiCatalog.TieBeam,
            CommandUiCatalog.Custom,
        });

        AddSeparator(layout);

        AddSection(layout, new[]
        {
            CommandUiCatalog.Assign,
            CommandUiCatalog.Edit,
            CommandUiCatalog.Inspect,
            CommandUiCatalog.Recalc,
            CommandUiCatalog.Renumber,
        });

        AddSeparator(layout);

        AddSection(layout, new[]
        {
            CommandUiCatalog.Report,
            CommandUiCatalog.ReportAll,
            CommandUiCatalog.Labels,
            CommandUiCatalog.Settings,
        });

        Controls.Add(layout);
    }

    /// <summary>
    /// Obnoví iba lokalizované texty existujúcich tlačidiel. Inštancie controlov,
    /// click handlery, command routing, ikony a technické ID zostávajú nezmenené.
    /// </summary>
    public void RefreshLocalizedContent()
    {
        foreach (var content in CommandUiCatalog.GetLocalizedClassicToolbarContent())
        {
            if (!_buttonsByControlId.TryGetValue(content.ControlId, out var button))
            {
                continue;
            }

            ApplyLocalizedContent(button, content);
        }
    }

    private void AddSection(FlowLayoutPanel layout, IEnumerable<CommandUiDescriptor> items)
    {
        foreach (var item in items)
        {
            var content = item.GetLocalizedContent();
            var button = new Button
            {
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

            button.Click += (_, _) => AcKrovyCommandDispatcher.Execute(item.CommandName);
            ApplyLocalizedContent(button, content);
            _buttonsByControlId.Add(content.ControlId, button);
            layout.Controls.Add(button);
        }
    }

    private void ApplyLocalizedContent(Button button, LocalizedCommandUiContent content)
    {
        button.AccessibleName = content.Title;
        button.AccessibleDescription = content.Description;
        _toolTip.SetToolTip(
            button,
            content.Title + Environment.NewLine + content.Description);
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
