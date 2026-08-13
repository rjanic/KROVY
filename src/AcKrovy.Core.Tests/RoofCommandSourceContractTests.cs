using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofCommandSourceContractTests
{
    private static readonly string Repository = RepositoryRoot();
    private static readonly string Workflow = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofCommandWorkflow.cs");
    private static readonly string Extractor = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofPolylineExtractor.cs");
    private static readonly string NotificationService = Read(
        "src", "AcKrovy.AutoCAD", "UI", "TransientNotificationService.cs");
    private static readonly string NotificationWindow = Read(
        "src", "AcKrovy.AutoCAD", "UI", "TransientNotificationWindow.xaml.cs");

    [Fact]
    public void AutoCadPolylineExtraction_MapsOnlyNeutralValuesIntoCoreContract()
    {
        Assert.Contains("Polyline polyline", Extractor);
        Assert.Contains("polyline.Closed", Extractor);
        Assert.Contains("polyline.GetBulgeAt", Extractor);
        Assert.Contains("polyline.Normal", Extractor);
        Assert.Contains("new RoofPoint2D(point.X, point.Y)", Extractor);
        Assert.Contains("new RoofFootprintInput", Extractor);
        Assert.DoesNotContain("polyline.Closed =", Extractor);
        Assert.DoesNotContain("polyline.UpgradeOpen", Extractor);
    }

    [Fact]
    public void RoofWorkflow_IsReadOnlyAndUsesCoreValidator()
    {
        Assert.Contains("RoofFootprintValidator.Validate", Workflow);
        Assert.Contains("OpenMode.ForRead", Workflow);
        Assert.Contains("SetImpliedSelection", Workflow);
        Assert.DoesNotContain("OpenMode.ForWrite", Workflow);
        Assert.DoesNotContain("OpenMode.ForWrite", Extractor);
        Assert.DoesNotContain("transaction.Commit", Workflow);
        Assert.DoesNotContain("XData", Workflow + Extractor);
        Assert.DoesNotContain("TimberElementDataSchema", Workflow + Extractor);
        Assert.DoesNotContain("ElementDataStore", Workflow + Extractor);
        Assert.DoesNotContain("Database", NotificationService + NotificationWindow);
        Assert.DoesNotContain("Transaction", NotificationService + NotificationWindow);
        Assert.DoesNotContain("XData", NotificationService + NotificationWindow);
        Assert.DoesNotContain("Xrecord", NotificationService + NotificationWindow);
    }

    [Fact]
    public void OpenLoop_UsesTransientNotificationThenContinuesSelectionRetry()
    {
        var failurePath = Segment(
            Workflow,
            "if (!validation.IsValid || validation.Footprint is null)",
            "var definition = new RoofDefinition");

        Assert.Contains(
            "validation.Error == RoofValidationError.OpenLoop",
            failurePath);
        Assert.Contains("TransientNotificationService.Show", failurePath);
        Assert.Contains("Command_Roof_OpenLoopNotificationTitle", failurePath);
        Assert.Contains("Command_Roof_OpenLoopNotificationBody", failurePath);
        Assert.Contains("continue;", failurePath);
        Assert.Equal(
            1,
            CountOccurrences(Workflow, "TransientNotificationService.Show"));

        // OpenLoop is window-only; other errors keep WriteMessage.
        var openLoopBranch = Segment(
            failurePath,
            "if (validation.Error == RoofValidationError.OpenLoop)",
            "else");
        Assert.DoesNotContain("WriteMessage", openLoopBranch);
        Assert.Contains("editor.WriteMessage(GetValidationMessage(validation.Error))", failurePath);
    }

    [Fact]
    public void TransientNotification_UsesFashionOwnerModalHostAnd2500Milliseconds()
    {
        Assert.Contains("TimeSpan.FromMilliseconds(2500)", NotificationWindow);
        Assert.Contains("DispatcherTimer", NotificationWindow);
        Assert.Contains("SettingsUiPreferencesStore.Load().Theme", NotificationService);
        Assert.Contains("SettingsWindowOwner.TryAssign", NotificationService);
        Assert.Contains("AcApp.ShowModalWindow(window)", NotificationService);
        Assert.DoesNotContain("MessageBox", NotificationService + NotificationWindow);
        Assert.DoesNotContain("MouseLeftButtonUp", NotificationWindow);
        Assert.DoesNotContain("Window_MouseLeftButtonUp", NotificationWindow);
        Assert.Contains("ArmEscapeDismiss", NotificationWindow);
        Assert.Contains("DispatcherPriority.ApplicationIdle", NotificationWindow);
        Assert.Contains("KeyDown += Window_KeyDown", NotificationWindow);

        var xaml = Read(
            "src", "AcKrovy.AutoCAD", "UI", "TransientNotificationWindow.xaml");
        Assert.DoesNotContain("MouseLeftButtonUp", xaml);
        Assert.DoesNotContain("KeyDown=", xaml);
    }

    [Fact]
    public void AkRoof_IsRegisteredAndHasOneMinimalRibbonEntry()
    {
        var commandNames = Read(
            "src", "AcKrovy.Localization", "CommandUiCatalog.cs");
        var commands = Read(
            "src", "AcKrovy.AutoCAD", "Commands", "AcKrovyCommands.cs");
        var ribbon = Read(
            "src", "AcKrovy.AutoCAD", "Ribbon", "AcKrovyRibbon.cs");

        Assert.Contains("public const string Roof = \"AK_ROOF\";", commandNames);
        Assert.Contains("[CommandMethod(AcKrovyCommandNames.Roof, CommandFlags.Modal | CommandFlags.Redraw)]", commands);
        Assert.Contains("Button(CommandUiCatalog.Roof)", ribbon);
        Assert.Equal(1, CountOccurrences(ribbon, "Button(CommandUiCatalog.Roof)"));
    }

    [Fact]
    public void CoreRoofDomain_HasNoAutodeskDependency()
    {
        var coreRoofFiles = Directory.GetFiles(
            Path.Combine(Repository, "src", "AcKrovy.Core"),
            "*Roof*.cs",
            SearchOption.AllDirectories);

        Assert.NotEmpty(coreRoofFiles);
        foreach (var file in coreRoofFiles)
        {
            Assert.DoesNotContain("Autodesk", File.ReadAllText(file));
        }
    }

    private static int CountOccurrences(string value, string token) =>
        (value.Length - value.Replace(token, string.Empty, StringComparison.Ordinal).Length) /
        token.Length;

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        Assert.True(endIndex > startIndex, $"End marker not found: {end}");
        return source[startIndex..endIndex];
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([Repository, .. path]));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
