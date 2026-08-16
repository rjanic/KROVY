using System.Xml.Linq;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDisplayTamperStretchSourceContractTests
{
    private static readonly string Repository = RepositoryRoot();
    private static readonly string ResizeService = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string LiveGeometry = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string DisplayService = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayService.cs");
    private static readonly string DisplayGroup = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayGroupService.cs");
    private static readonly string OwnerResolver = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofOwnerSelectionResolver.cs");
    private static readonly string NotificationService = Read(
        "src", "AcKrovy.AutoCAD", "UI", "TransientNotificationService.cs");
    private static readonly string NotificationWindow = Read(
        "src", "AcKrovy.AutoCAD", "UI", "TransientNotificationWindow.xaml.cs");
    private static readonly string NotificationXaml = Read(
        "src", "AcKrovy.AutoCAD", "UI", "TransientNotificationWindow.xaml");
    private static readonly string CommandRules = Read(
        "src", "AcKrovy.Core", "Services", "LiveGeometryCommandRules.cs");

    [Fact]
    public void DisplayChildMutation_WithUnchangedSource_ClassifiesAsDisplayTamper()
    {
        var inspect = Segment(
            ResizeService,
            "private static InspectionPlan Inspect(",
            "private static void ApplyResizes");
        Assert.Contains("RoofDisplayStore.Read(entity).Exists", inspect);
        Assert.Contains("RoofOwnerSelectionResolver.Resolve(", inspect);
        Assert.Contains("displayTamperCandidates.Add(resolution.OwnerId)", inspect);
        Assert.Contains("resizeOwners.Contains(ownerId)", inspect);
        Assert.Contains("unsupportedOwners.Contains(ownerId)", inspect);
        Assert.Contains("displayTamperOwners.Add(ownerId)", inspect);
        Assert.Contains("displayTamperOwners)", ResizeService);
        Assert.Contains("DisplayTamperOwnerIds", ResizeService);
    }

    [Fact]
    public void SeveralDisplayChildren_ResolveToOneOwnerRepairPath()
    {
        Assert.Contains("var displayTamperCandidates = new HashSet<ObjectId>();", ResizeService);
        Assert.Contains("var displayTamperOwners = new HashSet<ObjectId>();", ResizeService);
        Assert.Contains("foreach (var ownerId in displayTamperCandidates)", ResizeService);
        Assert.Contains("ApplyDisplayTampers(document, displayTamperOwners, modifiedIds)", ResizeService);
        Assert.Equal(1, Count(ResizeService, "ApplyDisplayTampers(document,"));
        Assert.Equal(1, Count(ResizeService, "TryApplyDisplayTamper(document.Database,"));
    }

    [Fact]
    public void CanonicalRepair_UsesExistingDisplayServiceRebuildAndGroupEnsure()
    {
        var repair = Segment(
            ResizeService,
            "private static bool TryApplyDisplayTamper",
            "private static RoofSourceChangeClassification ClassifyOwner");
        Assert.Contains("RoofSourceChangeKind.RigidEquivalent", repair);
        Assert.Contains("SimpleGableRoofWireframe.Create(", repair);
        Assert.Contains("RoofDisplayService.Rebuild(", repair);
        Assert.DoesNotContain("RoofDefinitionStore.Write(", repair);
        Assert.DoesNotContain("RoofDefinitionPersistence.Create(", repair);
        Assert.Contains("EnsureGroup(", DisplayService);
        Assert.Contains("ExpectedMemberCount = 8", DisplayGroup);
        Assert.Contains("SimpleGableRoofWireframe.EdgeCount", DisplayService);
    }

    [Fact]
    public void DisplayTamper_DoesNotMutateRaftersOrAnnotations()
    {
        Assert.DoesNotContain("TimberAnnotationService", ResizeService);
        Assert.DoesNotContain("ElementLabelService", ResizeService);
        Assert.DoesNotContain("TimberElementStore", ResizeService);
        Assert.DoesNotContain("RoofGeneratedTimberStore.Write", ResizeService);
        Assert.DoesNotContain("SimpleGableRafterLayoutSolver", ResizeService);
        Assert.DoesNotContain("EnsureForCreatedElements", ResizeService);
        var repair = Segment(
            ResizeService,
            "private static bool TryApplyDisplayTamper",
            "private static RoofSourceChangeClassification ClassifyOwner");
        Assert.DoesNotContain("RoofDefinitionStore.Write(", repair);
        Assert.DoesNotContain("RoofGeneratedTimberStore", repair);
        Assert.DoesNotContain("TimberElementStore", repair);
    }

    [Fact]
    public void DisplayTamperNotification_RoutesExactlyOnceViaExistingTransientService()
    {
        var displayBranch = Segment(
            ResizeService,
            "IReadOnlyCollection<ObjectId> displayTamperOwners = plan.DisplayTamperOwnerIds",
            "return plan.RelatedIds;");
        Assert.Contains("IsUndoGroupingSourceCommand(globalCommandName)", displayBranch);
        Assert.Contains("ApplyDisplayTampers(", displayBranch);
        Assert.Contains("TransientNotificationService.Show(", displayBranch);
        Assert.Contains("Command_Roof_DisplayTamperNotificationTitle", displayBranch);
        Assert.Contains("Command_Roof_DisplayTamperNotificationBody", displayBranch);
        Assert.Equal(1, Count(displayBranch, "TransientNotificationService.Show("));
        Assert.Equal(
            "Zobrazenie strechy nemožno upravovať samostatne.",
            Resource("Command_Roof_DisplayTamperNotificationTitle"));
        Assert.Equal(
            "Upravte základný obrys strechy.",
            Resource("Command_Roof_DisplayTamperNotificationBody"));
        Assert.DoesNotContain(
            "Command_Roof_UnsupportedStretchNotificationTitle",
            displayBranch);
    }

    [Fact]
    public void SourcePrecedence_ExcludesDisplayTamperWhenResizeOrUnsupportedWins()
    {
        var inspect = Segment(
            ResizeService,
            "private static InspectionPlan Inspect(",
            "private static void ApplyResizes");
        Assert.Contains(
            "if (resizeOwners.Contains(ownerId) ||",
            inspect);
        Assert.Contains("unsupportedOwners.Contains(ownerId)", inspect);
        Assert.Contains("SourceHandledOwnersThisCommand.Contains(ownerId)", inspect);
        Assert.Contains("continue;", inspect);
        var process = Segment(
            ResizeService,
            "public static IReadOnlyCollection<ObjectId> Process(",
            "private static InspectionPlan Inspect(");
        Assert.Contains("if (plan.ResizeOwnerIds.Count > 0)", process);
        Assert.Contains("if (plan.UnsupportedOwnerIds.Count > 0)", process);
        Assert.Contains("displayTamperOwners = plan.DisplayTamperOwnerIds", process);
        // Unsupported notification must not include display-tamper keys.
        var unsupportedBranch = Segment(
            process,
            "if (plan.UnsupportedOwnerIds.Count > 0)",
            "IReadOnlyCollection<ObjectId> displayTamperOwners = plan.DisplayTamperOwnerIds");
        Assert.Contains("Command_Roof_UnsupportedStretchNotificationTitle", unsupportedBranch);
        Assert.DoesNotContain("Command_Roof_DisplayTamperNotificationTitle", unsupportedBranch);
        var displayBranch = Segment(
            process,
            "IReadOnlyCollection<ObjectId> displayTamperOwners = plan.DisplayTamperOwnerIds",
            "return plan.RelatedIds;");
        Assert.DoesNotContain("Command_Roof_UnsupportedStretchNotificationTitle", displayBranch);
        // Exactly one Show per outcome branch; two total in Process for distinct keys.
        Assert.Equal(2, Count(process, "TransientNotificationService.Show("));
    }

    [Fact]
    public void UnrelatedRedLine_IsNotAdoptedWithoutDisplayMetadata()
    {
        Assert.Contains("RoofDisplayStore.Read(entity).Exists", ResizeService);
        Assert.Contains("RoofOwnerSelectionResolver.Resolve(", ResizeService);
        Assert.Contains("OwnerReference", OwnerResolver);
        Assert.DoesNotContain("ColorIndex == 1", ResizeService);
        Assert.DoesNotContain("KROV_STRECHA", ResizeService);
        Assert.DoesNotContain("LayerName", ResizeService);
    }

    [Fact]
    public void MoveRotateRemainOutOfDisplayTamperScope()
    {
        Assert.False(AcKrovy.Core.Services.LiveGeometryCommandRules.IsUndoGroupingSourceCommand("MOVE"));
        Assert.False(AcKrovy.Core.Services.LiveGeometryCommandRules.IsUndoGroupingSourceCommand("ROTATE"));
        Assert.True(AcKrovy.Core.Services.LiveGeometryCommandRules.IsUndoGroupingSourceCommand("STRETCH"));
        Assert.True(AcKrovy.Core.Services.LiveGeometryCommandRules.IsUndoGroupingSourceCommand("GRIP_STRETCH"));
        var displayBranch = Segment(
            ResizeService,
            "IReadOnlyCollection<ObjectId> displayTamperOwners = plan.DisplayTamperOwnerIds",
            "return plan.RelatedIds;");
        Assert.Contains("IsUndoGroupingSourceCommand(globalCommandName)", displayBranch);
        var stretchGrouping = Segment(
            CommandRules,
            "public static bool IsUndoGroupingSourceCommand",
            "public static bool IsCopySourcePreservingCommand");
        Assert.DoesNotContain("\"MOVE\"", stretchGrouping);
        Assert.DoesNotContain("\"ROTATE\"", stretchGrouping);
    }

    [Fact]
    public void UndoRedoProtection_StillSkipsProcessAndDisplayTamperSideEffects()
    {
        var processGuard = Segment(
            ResizeService,
            "public static IReadOnlyCollection<ObjectId> Process(",
            "var plan = Inspect(");
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", processGuard);
        Assert.Contains("return Array.Empty<ObjectId>();", processGuard);
        Assert.DoesNotContain("ApplyDisplayTampers(", processGuard);
        Assert.DoesNotContain("TransientNotificationService.Show(", processGuard);
        var ignoreBranch = Segment(
            LiveGeometry,
            "if (shouldIgnore)",
            "_ignoreCurrentCommand = false;");
        Assert.DoesNotContain("using (document.LockDocument())", ignoreBranch);
        Assert.DoesNotContain("ApplyDisplayTampers(", ignoreBranch);
        Assert.DoesNotContain("TransientNotificationService.Show(", ignoreBranch);
    }

    [Fact]
    public void NoNewNotificationInfrastructureOrReactorsWereAdded()
    {
        Assert.Contains("AcApp.ShowModalWindow(window)", NotificationService);
        Assert.DoesNotContain("MessageBox.Show", ResizeService + NotificationService);
        Assert.DoesNotContain("new Window(", ResizeService);
        Assert.DoesNotContain("TransientNotificationWindow", ResizeService);
        Assert.Equal(2, Count(ResizeService, "TransientNotificationService.Show("));
        Assert.DoesNotContain("DatabaseReactor", ResizeService);
        Assert.DoesNotContain("ObjectOverrule", ResizeService);
        Assert.DoesNotContain("BeginDeepClone", ResizeService);
        Assert.DoesNotContain("EndDeepClone", ResizeService);
        Assert.Contains("TimeSpan.FromMilliseconds(2500)", NotificationWindow);
        Assert.Contains("DispatcherPriority.ApplicationIdle", NotificationWindow);
        Assert.DoesNotContain("MouseLeftButtonUp", NotificationWindow + NotificationXaml);
    }

    [Fact]
    public void AllSixLanguagePacksContainDisplayTamperNotificationKeys()
    {
        var resources = Path.Combine(Repository, "src", "AcKrovy.Localization", "Resources");
        var files = new[]
        {
            "UiStrings.resx", "UiStrings.cs.resx", "UiStrings.en.resx",
            "UiStrings.de.resx", "UiStrings.pl.resx", "UiStrings.fr.resx",
        };
        var required = new[]
        {
            "Command_Roof_DisplayTamperNotificationTitle",
            "Command_Roof_DisplayTamperNotificationBody",
            "Command_Roof_UnsupportedStretchNotificationTitle",
            "Command_Roof_UnsupportedStretchNotificationBody",
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
