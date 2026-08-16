using System.Xml.Linq;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofUnsupportedStretchNotificationSourceContractTests
{
    private static readonly string Repository = RepositoryRoot();
    private static readonly string ResizeService = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string LiveGeometry = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string NotificationService = Read(
        "src", "AcKrovy.AutoCAD", "UI", "TransientNotificationService.cs");
    private static readonly string NotificationWindow = Read(
        "src", "AcKrovy.AutoCAD", "UI", "TransientNotificationWindow.xaml.cs");
    private static readonly string NotificationXaml = Read(
        "src", "AcKrovy.AutoCAD", "UI", "TransientNotificationWindow.xaml");
    private static readonly string Persistence = Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofDefinitionPersistence.cs");
    private static readonly string CommandRules = Read(
        "src", "AcKrovy.Core", "Services", "LiveGeometryCommandRules.cs");

    [Fact]
    public void UnsupportedStretch_RoutesToExistingTransientNotification()
    {
        var unsupportedBranch = Segment(
            ResizeService,
            "if (plan.UnsupportedOwnerIds.Count > 0)",
            "IReadOnlyCollection<ObjectId> displayTamperOwners = plan.DisplayTamperOwnerIds");
        Assert.Contains("Command_Roof_PersistedStale", unsupportedBranch);
        Assert.Contains("IsUndoGroupingSourceCommand(globalCommandName)", unsupportedBranch);
        Assert.Contains("TransientNotificationService.Show(", unsupportedBranch);
        Assert.Contains("Command_Roof_UnsupportedStretchNotificationTitle", unsupportedBranch);
        Assert.Contains("Command_Roof_UnsupportedStretchNotificationBody", unsupportedBranch);
        Assert.DoesNotContain("Command_Roof_DisplayTamperNotificationTitle", unsupportedBranch);
        Assert.Equal(1, Count(unsupportedBranch, "TransientNotificationService.Show("));
        Assert.Equal(1, Count(unsupportedBranch, "document.Editor.WriteMessage("));
        Assert.Equal(
            "Tvar strechy už nie je podporovaný.",
            Resource("Command_Roof_UnsupportedStretchNotificationTitle"));
        Assert.Equal(
            "Vráťte poslednú zmenu príkazom Späť (U).",
            Resource("Command_Roof_UnsupportedStretchNotificationBody"));
    }

    [Fact]
    public void SupportedResizeAndRigidPaths_DoNotShowUnsupportedStretchNotification()
    {
        var apply = Segment(
            ResizeService,
            "private static void ApplyResizes",
            "private static ResizeApplyResult TryApplyResize");
        var tryApply = Segment(
            ResizeService,
            "private static ResizeApplyResult TryApplyResize",
            "private static bool ApplyDisplayTampers");
        Assert.Contains("RoofSourceChangeKind.SupportedResize", tryApply);
        Assert.DoesNotContain("TransientNotificationService.Show(", apply + tryApply);
        Assert.DoesNotContain(
            "Command_Roof_UnsupportedStretchNotificationTitle",
            apply + tryApply);
        Assert.Contains("RoofSourceChangeKind.RigidEquivalent", Persistence);
        Assert.Contains("RoofSourceChangeKind.SupportedResize", Persistence);
        Assert.DoesNotContain("TransientNotificationService", Persistence);
    }

    [Fact]
    public void MoveRotateAreNotUndoGroupedStretchCommands()
    {
        Assert.False(AcKrovy.Core.Services.LiveGeometryCommandRules.IsUndoGroupingSourceCommand("MOVE"));
        Assert.False(AcKrovy.Core.Services.LiveGeometryCommandRules.IsUndoGroupingSourceCommand("ROTATE"));
        Assert.True(AcKrovy.Core.Services.LiveGeometryCommandRules.IsUndoGroupingSourceCommand("STRETCH"));
        Assert.True(AcKrovy.Core.Services.LiveGeometryCommandRules.IsUndoGroupingSourceCommand("GRIP_STRETCH"));
        Assert.Contains("IsUndoGroupingSourceCommand(globalCommandName)", ResizeService);
        var stretchGrouping = Segment(
            CommandRules,
            "public static bool IsUndoGroupingSourceCommand",
            "public static bool IsCopySourcePreservingCommand");
        Assert.Contains("\"STRETCH\"", stretchGrouping);
        Assert.Contains("\"GRIP_STRETCH\"", stretchGrouping);
        Assert.DoesNotContain("\"MOVE\"", stretchGrouping);
        Assert.DoesNotContain("\"ROTATE\"", stretchGrouping);
    }

    [Fact]
    public void UndoRedoProtection_RemainsIntactAndSkipsRoofProcessSideEffects()
    {
        Assert.Contains("LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName)", ResizeService);
        var processGuard = Segment(
            ResizeService,
            "public static IReadOnlyCollection<ObjectId> Process(",
            "var plan = Inspect(");
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", processGuard);
        Assert.Contains("return Array.Empty<ObjectId>();", processGuard);
        Assert.DoesNotContain("TransientNotificationService.Show(", processGuard);
        Assert.Contains("OnLiveGeometryRefreshSkippedUndoRedo(", LiveGeometry);
        var ignoreBranch = Segment(
            LiveGeometry,
            "if (shouldIgnore)",
            "_ignoreCurrentCommand = false;");
        Assert.DoesNotContain("using (document.LockDocument())", ignoreBranch);
        Assert.DoesNotContain("TransientNotificationService.Show(", ignoreBranch);
    }

    [Fact]
    public void NoNewModalDialogOrNotificationInfrastructureWasAdded()
    {
        Assert.Contains("AcApp.ShowModalWindow(window)", NotificationService);
        Assert.DoesNotContain("MessageBox.Show", ResizeService + NotificationService + NotificationWindow);
        Assert.DoesNotContain("PromptKeywordOptions", ResizeService);
        Assert.DoesNotContain("new Window(", ResizeService);
        Assert.DoesNotContain("TransientNotificationWindow", ResizeService);
        Assert.Equal(2, Count(ResizeService, "TransientNotificationService.Show("));
        Assert.DoesNotContain("DatabaseReactor", ResizeService);
        Assert.DoesNotContain("ObjectOverrule", ResizeService);
        Assert.DoesNotContain("BeginDeepClone", ResizeService);
    }

    [Fact]
    public void ExistingTransientNotificationInputBleedSafetyRemainsUnchanged()
    {
        Assert.Contains("TimeSpan.FromMilliseconds(2500)", NotificationWindow);
        Assert.Contains("Loaded += TransientNotificationWindow_Loaded", NotificationWindow);
        Assert.Contains("DispatcherPriority.ApplicationIdle", NotificationWindow);
        Assert.Contains("ArmEscapeDismiss", NotificationWindow);
        Assert.DoesNotContain("MouseLeftButtonUp", NotificationWindow + NotificationXaml);
        Assert.DoesNotContain("MouseDown", NotificationWindow + NotificationXaml);
        Assert.Contains("AcApp.ShowModalWindow(window)", NotificationService);
    }

    [Fact]
    public void AllSixLanguagePacksContainUnsupportedStretchNotificationKeys()
    {
        var resources = Path.Combine(Repository, "src", "AcKrovy.Localization", "Resources");
        var files = new[]
        {
            "UiStrings.resx", "UiStrings.cs.resx", "UiStrings.en.resx",
            "UiStrings.de.resx", "UiStrings.pl.resx", "UiStrings.fr.resx",
        };
        var required = new[]
        {
            "Command_Roof_UnsupportedStretchNotificationTitle",
            "Command_Roof_UnsupportedStretchNotificationBody",
            "Command_Roof_OpenLoopNotificationTitle",
            "Command_Roof_OpenLoopNotificationBody",
        };

        foreach (var file in files)
        {
            var keys = XDocument.Load(Path.Combine(resources, file))
                .Root!.Elements("data")
                .Select(element => (string?)element.Attribute("name"))
                .Where(name => name is not null)
                .ToHashSet(StringComparer.Ordinal);
            Assert.All(required, key => Assert.Contains(key, keys));
        }
    }

    private static int Count(string value, string token) =>
        (value.Length - value.Replace(token, string.Empty, StringComparison.Ordinal).Length) /
        token.Length;

    private static string Segment(string source, string start, string end)
    {
        source = Normalize(source);
        start = Normalize(start);
        end = Normalize(end);
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        Assert.True(endIndex > startIndex, $"End marker not found: {end}");
        return source[startIndex..endIndex];
    }

    private static string Resource(string key)
    {
        var path = Path.Combine(Repository, "src", "AcKrovy.Localization", "Resources", "UiStrings.resx");
        return XDocument.Load(path)
            .Root!.Elements("data")
            .Single(element => string.Equals((string?)element.Attribute("name"), key, StringComparison.Ordinal))
            .Element("value")!.Value;
    }

    private static string Read(params string[] path) =>
        Normalize(File.ReadAllText(Path.Combine([Repository, .. path])));

    private static string Normalize(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

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
