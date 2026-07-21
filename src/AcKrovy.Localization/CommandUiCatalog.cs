using System.Globalization;

namespace AcKrovy.Localization;

public static class AcKrovyCommandNames
{
    public const string Help = "AK_HELP";
    public const string Ribbon = "AK_RIBBON";
    public const string Toolbar = "AK_TOOLBAR";
    public const string ToolbarShow = "AK_TOOLBARSHOW";
    public const string ToolbarHide = "AK_TOOLBARHIDE";
    public const string Settings = "AK_SETTINGS";
    public const string ApplyLayers = "AK_APPLYLAYERS";
    public const string Labels = "AK_LABELS";
    public const string LabelSelected = "AK_LABELSELECTED";
    public const string LabelShow = "AK_LABELSHOW";
    public const string LabelHide = "AK_LABELHIDE";
    public const string Assign = "AK_ASSIGN";
    public const string Rafter = "AK_KROKVA";
    public const string WallPlate = "AK_POMURNICA";
    public const string Purlin = "AK_VAZNICA";
    public const string Post = "AK_STLPIK";
    public const string CollarTie = "AK_KLIESTINA";
    public const string Brace = "AK_VZPERA";
    public const string TieBeam = "AK_VAZNYTRAM";
    public const string Edit = "AK_EDIT";
    public const string FlipSlope = "AK_FLIPSLOPE";
    public const string Inspect = "AK_INSPECT";
    public const string Report = "AK_REPORT";
    public const string ReportAll = "AK_REPORTALL";
    public const string Recalc = "AK_RECALC";

    public static IReadOnlyList<string> All { get; } =
    [
        Help, Ribbon, Toolbar, ToolbarShow, ToolbarHide, Settings, ApplyLayers, Labels, LabelSelected,
        LabelShow, LabelHide, Assign, Rafter, WallPlate, Purlin, Post, CollarTie, Brace, TieBeam, Edit,
        FlipSlope, Inspect, Report, ReportAll, Recalc,
    ];
}

public sealed class CommandUiDescriptor
{
    internal CommandUiDescriptor(
        string commandName,
        string ribbonControlId,
        string iconKey,
        string labelResourceKey,
        string toolTipResourceKey)
    {
        CommandName = commandName;
        RibbonControlId = ribbonControlId;
        IconKey = iconKey;
        LabelResourceKey = labelResourceKey;
        ToolTipResourceKey = toolTipResourceKey;
    }

    public string CommandName { get; }
    public string RibbonControlId { get; }
    public string IconKey { get; }
    public string LabelResourceKey { get; }
    public string ToolTipResourceKey { get; }

    public string GetLabel(CultureInfo? culture = null) =>
        UiStrings.GetString(LabelResourceKey, culture);

    public string GetToolTip(CultureInfo? culture = null) =>
        UiStrings.GetString(ToolTipResourceKey, culture);

    public LocalizedCommandUiContent GetLocalizedContent(CultureInfo? culture = null) =>
        new(
            CommandName,
            RibbonControlId,
            IconKey,
            GetLabel(culture),
            GetToolTip(culture));
}

/// <summary>
/// Lokalizovaný prezentačný snapshot príkazu. Technické identity zostávajú
/// prevzaté z <see cref="CommandUiDescriptor"/> a nemenia sa s UI kultúrou.
/// </summary>
public sealed class LocalizedCommandUiContent
{
    internal LocalizedCommandUiContent(
        string commandName,
        string controlId,
        string iconKey,
        string title,
        string description)
    {
        CommandName = commandName;
        ControlId = controlId;
        IconKey = iconKey;
        Title = title;
        Description = description;
    }

    public string CommandName { get; }
    public string ControlId { get; }
    public string IconKey { get; }
    public string Title { get; }
    public string Description { get; }
}

public static class CommandUiCatalog
{
    public const string RibbonTabId = "DECORAIR_ACAD_KROVY_TAB";
    public const string ClassicToolbarPaletteId = "AE3310A6-6077-4FB3-B9BE-D4A1DCC866C4";

    public static CommandUiDescriptor Rafter { get; } = Create("RAFTER", "rafter", AcKrovyCommandNames.Rafter, "Rafter");
    public static CommandUiDescriptor WallPlate { get; } = Create("WALLPLATE", "wallplate", AcKrovyCommandNames.WallPlate, "WallPlate");
    public static CommandUiDescriptor Purlin { get; } = Create("PURLIN", "purlin", AcKrovyCommandNames.Purlin, "Purlin");
    public static CommandUiDescriptor Post { get; } = Create("POST", "post", AcKrovyCommandNames.Post, "Post");
    public static CommandUiDescriptor CollarTie { get; } = Create("COLLARTIE", "collartie", AcKrovyCommandNames.CollarTie, "CollarTie");
    public static CommandUiDescriptor Brace { get; } = Create("BRACE", "brace", AcKrovyCommandNames.Brace, "Brace");
    public static CommandUiDescriptor TieBeam { get; } = Create("TIEBEAM", "tiebeam", AcKrovyCommandNames.TieBeam, "TieBeam");
    public static CommandUiDescriptor Assign { get; } = Create("ASSIGN", "assign", AcKrovyCommandNames.Assign, "Assign");
    public static CommandUiDescriptor Edit { get; } = Create("EDIT", "edit", AcKrovyCommandNames.Edit, "Edit");
    public static CommandUiDescriptor Inspect { get; } = Create("INSPECT", "inspect", AcKrovyCommandNames.Inspect, "Inspect");
    public static CommandUiDescriptor Recalc { get; } = Create("RECALC", "recalc", AcKrovyCommandNames.Recalc, "Recalc");
    public static CommandUiDescriptor Report { get; } = Create("REPORT", "report_selection", AcKrovyCommandNames.Report, "Report");
    public static CommandUiDescriptor ReportAll { get; } = Create("REPORTALL", "report_all", AcKrovyCommandNames.ReportAll, "ReportAll");
    public static CommandUiDescriptor Settings { get; } = Create("SETTINGS", "settings", AcKrovyCommandNames.Settings, "Settings");
    public static CommandUiDescriptor Labels { get; } = Create("LABELS", "labels", AcKrovyCommandNames.Labels, "Labels");
    public static CommandUiDescriptor Toolbar { get; } = Create("TOOLBAR", "toolbar", AcKrovyCommandNames.Toolbar, "Toolbar");

    public static IReadOnlyList<CommandUiDescriptor> RibbonCommands { get; } =
    [
        Rafter, WallPlate, Purlin, Post, CollarTie, Brace, TieBeam, Assign, Edit, Inspect, Recalc,
        Report, ReportAll, Settings, Labels, Toolbar,
    ];

    public static IReadOnlyList<CommandUiDescriptor> ClassicToolbarCommands { get; } =
    [
        Rafter, WallPlate, Purlin, Post, CollarTie, Brace, TieBeam, Assign, Edit, Inspect, Recalc,
        Report, ReportAll, Labels, Settings,
    ];

    public static IReadOnlyList<LocalizedCommandUiContent> GetLocalizedClassicToolbarContent(
        CultureInfo? culture = null) =>
        ClassicToolbarCommands.Select(item => item.GetLocalizedContent(culture)).ToArray();

    private static CommandUiDescriptor Create(
        string controlSuffix,
        string iconKey,
        string commandName,
        string resourceSuffix) =>
        new(
            commandName,
            $"DECORAIR_AK_{controlSuffix}",
            iconKey,
            $"CommandUi_{resourceSuffix}_Label",
            $"CommandUi_{resourceSuffix}_Tooltip");
}

public static class CommandMacroBuilder
{
    public static string Build(string commandName) =>
        string.IsNullOrWhiteSpace(commandName)
            ? string.Empty
            : $"{commandName.Trim()} ";
}
