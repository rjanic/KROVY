using System.Xml.Linq;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofLiveResizeSourceContractTests
{
    private static readonly string Repository = RepositoryRoot();
    private static readonly string LiveGeometry = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string ResizeService = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Persistence = Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofDefinitionPersistence.cs");
    private static readonly string DisplayService = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayService.cs");
    private static readonly string RafterWorkflow = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofRafterCommandWorkflow.cs");
    private static readonly string CommandRules = Read(
        "src", "AcKrovy.Core", "Services", "LiveGeometryCommandRules.cs");

    [Fact]
    public void LiveGeometry_ReusesExistingCommandEndedPathForRoofResize()
    {
        Assert.Contains("RoofLiveResizeService.Process(", LiveGeometry);
        Assert.Contains("RoofLiveResizeService.TryBeginGroupedUndo(", LiveGeometry);
        Assert.Contains("RequiresGroupedUndoMark(e.GlobalCommandName)", LiveGeometry);
        Assert.Contains("IsUndoGroupingSourceCommand(", CommandRules);
        Assert.Contains("EndStretchUndoMark(", LiveGeometry);
        Assert.Contains("LiveGeometryCommandRules.IsUndoRedoCommand(", LiveGeometry);
        Assert.DoesNotContain("DatabaseReactor", ResizeService);
        Assert.DoesNotContain("ObjectOverrule", ResizeService);
        Assert.DoesNotContain("BeginDeepClone", ResizeService);
        Assert.DoesNotContain("ObjectModified", ResizeService);
        Assert.DoesNotContain("CommandEnded", ResizeService);
    }

    [Fact]
    public void ResizeService_UsesExistingRestoreCreateDisplayAndGroupServices()
    {
        Assert.Contains("RoofDefinitionPersistence.Classify(", ResizeService);
        Assert.Contains("RoofDefinitionPersistence.Create(", ResizeService);
        Assert.Contains("RoofDefinitionStore.Write(", ResizeService);
        Assert.Contains("RoofDisplayService.Rebuild(", ResizeService);
        Assert.Contains("SimpleGableRoofWireframe.Create(", ResizeService);
        Assert.Contains("RoofSourceChangeKind.SupportedResize", ResizeService);
        Assert.Contains("Command_Roof_PersistedStale", ResizeService);
        Assert.Contains("TransientNotificationService.Show(", ResizeService);
        Assert.Contains("Command_Roof_UnsupportedStretchNotificationTitle", ResizeService);
        Assert.Contains("Command_Roof_UnsupportedStretchNotificationBody", ResizeService);
        Assert.Contains("Command_Roof_DisplayTamperNotificationTitle", ResizeService);
        Assert.Contains("Command_Roof_DisplayTamperNotificationBody", ResizeService);
        Assert.Contains("DisplayTamperOwnerIds", ResizeService);
        Assert.Contains("TryApplyDisplayTamper(", ResizeService);
        Assert.Contains("IsUndoGroupingSourceCommand(globalCommandName)", ResizeService);
        Assert.Contains("EnsureGroup(", DisplayService);
        Assert.DoesNotContain("TimberAnnotationService", ResizeService);
        Assert.DoesNotContain("ElementLabelService", ResizeService);
        Assert.DoesNotContain("TimberElementStore", ResizeService);
        Assert.DoesNotContain("RoofGeneratedTimberStore.Write", ResizeService);
        Assert.DoesNotContain("SimpleGableRafterLayoutSolver", ResizeService);
        Assert.DoesNotContain("EnsureForCreatedElements", ResizeService);
    }

    [Fact]
    public void RestoreV2_PreservesRidgeFamilyAndAcceptsRectangularResize()
    {
        var restoreV2 = Segment(
            Persistence,
            "private static RoofDefinitionRestoreResult RestoreV2",
            "private static RoofDefinitionRestoreResult Solve");
        Assert.Contains("TryReadSourceTopology(source", restoreV2);
        Assert.Contains("data.RidgeEdgeFamily", restoreV2);
        Assert.Contains("RoofDirection2D.TryCreate(edge.X, edge.Y", restoreV2);
        Assert.Contains("MatchesTopology(", restoreV2);
        Assert.Contains("RoofDefinitionRestoreError.StaleFootprint", restoreV2);
        Assert.DoesNotContain("Encode", restoreV2);
        Assert.DoesNotContain("Write", restoreV2);
        Assert.DoesNotContain("XData", restoreV2);
        Assert.Contains("RoofSourceChangeKind.SupportedResize", Persistence);
        Assert.Contains("RoofSourceChangeKind.RigidEquivalent", Persistence);
    }

    [Fact]
    public void UndoRedoGuards_RemainAndStretchIsGroupedSeparatelyFromUndoFamily()
    {
        Assert.Contains("IsUndoRedoCommand(", CommandRules);
        Assert.Contains("IsUndoGroupingSourceCommand(", CommandRules);
        Assert.Contains("\"STRETCH\"", CommandRules);
        Assert.Contains("\"GRIP_STRETCH\"", CommandRules);
        Assert.Contains("StartUndoMark", ResizeService);
        Assert.Contains("EndUndoMark", ResizeService);
        Assert.Contains("OnLiveGeometryRefreshSkippedUndoRedo(", LiveGeometry);
        var ignoreBranch = Segment(
            LiveGeometry,
            "if (shouldIgnore)",
            "_ignoreCurrentCommand = false;");
        Assert.Contains("ClearPendingLiveGeometryState(", ignoreBranch);
        Assert.DoesNotContain("using (document.LockDocument())", ignoreBranch);
        Assert.DoesNotContain("StartTransaction()", ignoreBranch);
    }

    [Fact]
    public void RoofOnlyModifiedIds_AreRemovedBeforeTimberRefresh()
    {
        var refresh = Segment(
            LiveGeometry,
            "private void RefreshCandidates(",
            "private static void RefreshTimberElements(");
        Assert.Contains("roofRelatedIds.Count > 0", refresh);
        Assert.Contains("!roofRelatedIds.Contains(id)", refresh);
        Assert.Contains("RefreshTimberElements(", refresh);
    }

    [Fact]
    public void Stage6ExistingSet_RemainsSafeAndCanReportStaleLayout()
    {
        Assert.Contains("Command_RoofRafters_ExistingFoundFormat", RafterWorkflow);
        Assert.Contains("Command_RoofRafters_ExistingStale", RafterWorkflow);
        Assert.Contains("Command_RoofRafters_ReplacementDeferred", RafterWorkflow);
        Assert.Contains("RoofGeneratedRafterSetService.IsGeneratedSetStale(", RafterWorkflow);
        Assert.Contains("GeneratedSetIsStale", RafterWorkflow);
        Assert.DoesNotContain(".Erase(", RafterWorkflow);
        Assert.DoesNotContain("SimpleGableRafterLayoutSolver.Solve(", Segment(
            RafterWorkflow,
            "if (selectedRoof.ExistingGeneratedRafterCount > 0)",
            "var defaultProfile = TimberElementDefaultProfileStore.Load();"));
    }

    [Fact]
    public void NoSchemaOrVersionChangeWasIntroduced()
    {
        Assert.Equal(2, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
        Assert.Equal(1, TimberDrawingSettings.DrawingSettingsSchemaVersion);
        Assert.DoesNotContain("CurrentVersion = 3", Read(
            "src", "AcKrovy.Core", "Models", "Roofs", "RoofDefinitionDataSchema.cs"));
        Assert.DoesNotContain("CurrentVersion = 8", Read(
            "src", "AcKrovy.Core", "Models", "TimberElementDataSchema.cs"));
    }

    [Fact]
    public void AllSixLanguagePacksContainTheResizeDiagnosticKeys()
    {
        var resources = Path.Combine(Repository, "src", "AcKrovy.Localization", "Resources");
        var files = new[]
        {
            "UiStrings.resx", "UiStrings.cs.resx", "UiStrings.en.resx",
            "UiStrings.de.resx", "UiStrings.pl.resx", "UiStrings.fr.resx",
        };
        var required = new[]
        {
            "Command_Roof_PersistedStale",
            "Command_Roof_DisplayCurrent",
            "Command_Roof_UnsupportedStretchNotificationTitle",
            "Command_Roof_UnsupportedStretchNotificationBody",
            "Command_Roof_DisplayTamperNotificationTitle",
            "Command_Roof_DisplayTamperNotificationBody",
            "Command_RoofRafters_ExistingStale",
            "Command_RoofRafters_RecipeAmbiguous",
            "Command_RoofRafters_ReplacementDeferred",
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
