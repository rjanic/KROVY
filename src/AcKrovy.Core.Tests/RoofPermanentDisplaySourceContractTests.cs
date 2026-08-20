using System.Xml.Linq;
using AcKrovy.Core.Models.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofPermanentDisplaySourceContractTests
{
    private static readonly string Repository = RepositoryRoot();
    private static readonly string Workflow = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofCommandWorkflow.cs");
    private static readonly string Service = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayService.cs");
    private static readonly string Store = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayStore.cs");
    private static readonly string Wireframe = Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "SimpleGableRoofWireframe.cs");

    [Fact]
    public void PermanentDisplay_UsesDedicatedLayerRegAppAndExactlySevenNativeLines()
    {
        Assert.Contains("KROV_STRECHA", Service);
        Assert.Contains("DECORAIR_ACADKROVY_ROOF_DISPLAY", Store);
        Assert.Contains("public const int EdgeCount = 7", Wireframe);
        Assert.Contains("var line = new Line(", Service);
        Assert.Contains("foreach (var edge in expectedEdges.OrderBy(edge => edge.Role))", Service);
        Assert.Contains("modelSpace.AppendEntity(line)", Service);
        Assert.Contains("transaction.AddNewlyCreatedDBObject(line, true)", Service);
        Assert.DoesNotContain("3DFace", Service);
        Assert.DoesNotContain("Polyline3d", Service);
        Assert.DoesNotContain("Solid3d", Service);
        Assert.DoesNotContain("BlockReference", Service);
    }

    [Fact]
    public void EveryLine_IsByLayerAndCarriesStableOwnerRoleAndSignatureMetadata()
    {
        Assert.Contains("ApplyToAnnotationEntity", Service);
        Assert.Contains("line.LinetypeId = database.ByLayerLinetype", Service);
        Assert.Contains("line.LineWeight = LineWeight.ByLayer", Service);
        Assert.Contains("RoofDisplayDataSchema.CurrentVersion", Service);
        Assert.Contains("ownerReference", Service);
        Assert.Contains("edge.Role", Service);
        Assert.Contains("generationSignature", Service);
        Assert.Contains("ReadForeignXData", Store);
        Assert.Contains("EnsureRegAppRegistered", Store);
    }

    [Fact]
    public void Discovery_IsReadOnlyAndScansOnlyModelSpace()
    {
        var inspect = Segment(Service, "public static RoofDisplayInspection Inspect", "public static bool Rebuild");
        Assert.Contains("ScanModelSpaceDisplayChildren", inspect);
        Assert.Contains("BlockTableRecord.ModelSpace", inspect);
        Assert.Contains("OpenMode.ForRead", inspect);
        Assert.DoesNotContain("OpenMode.ForWrite", inspect);
        Assert.DoesNotContain("UpgradeOpen", inspect);
        Assert.DoesNotContain("AppendEntity", inspect);
        Assert.DoesNotContain("Erase", inspect);
        Assert.DoesNotContain("Commit", inspect);
    }

    [Fact]
    public void NewRoof_SaveIsOneAtomicDefinitionAndSevenLineTransaction()
    {
        var confirm = Workflow.IndexOf("ConfirmPersistence(editor)", StringComparison.Ordinal);
        var persist = Segment(
            Workflow,
            "private static bool TryPersist",
            "private static RoofDisplayInspection InspectDisplay");
        Assert.True(confirm >= 0);
        Assert.Contains("RoofDefinitionStore.Write(owner, transaction, data)", persist);
        Assert.Contains("RoofDisplayService.Rebuild(", persist);
        Assert.Equal(1, CountOccurrences(persist, "transaction.Commit();"));
        Assert.True(Workflow.IndexOf("TryPersist(", confirm, StringComparison.Ordinal) > confirm);
    }

    [Fact]
    public void ExistingRoof_RebuildKeepsSemanticOwnerForReadAndCommitsOnlyAfterYes()
    {
        var rebuild = Segment(Workflow, "private static bool TryRebuildDisplay", "private static bool TryRehydrateGroup");
        Assert.Contains("transaction.GetObject(ownerId, OpenMode.ForRead) is not Polyline owner", rebuild);
        Assert.DoesNotContain("OpenMode.ForWrite", rebuild);
        Assert.DoesNotContain("RoofDefinitionStore.Write", rebuild);
        Assert.Equal(1, CountOccurrences(rebuild, "transaction.Commit();"));
        Assert.Contains("if (!ConfirmDisplayPersistence(editor, isMissing))", Workflow);
        Assert.Contains("return;", Segment(
            Workflow,
            "if (!ConfirmDisplayPersistence(editor, isMissing))",
            "if (TryRebuildDisplay"));
    }

    [Fact]
    public void CurrentDisplay_IsReadOnlyAndDoesNotDuplicateOrRewrite()
    {
        var current = Segment(
            Workflow,
            "RoofDisplayLifecycleKind.Current",
            "RoofDisplayLifecycleKind.GroupMissingRehydratable");
        Assert.Contains("Command_Roof_DisplayCurrent", current);
        Assert.Contains("return;", current);
        Assert.DoesNotContain("Rebuild", current);
        Assert.DoesNotContain("Commit", current);
    }

    [Fact]
    public void RebuildErasesInspectedAndOwnerMatchedChildrenAndProtectsFutureSchema()
    {
        var rebuild = Segment(Service, "public static bool Rebuild", "private static RoofPoint3D MapPoint");
        Assert.Contains("UnsupportedFutureSchema", rebuild);
        Assert.Contains("return false;", rebuild);
        Assert.Contains("CollectDisplayIdsToErase", rebuild);
        Assert.Contains("RoofDisplayRebuildEraseRules.ShouldEraseInspectedDisplayChild", rebuild);
        Assert.Contains("RoofDisplayRebuildEraseRules.ShouldEraseOwnerMatchedSweepChild", rebuild);
        Assert.Contains("TryCollectStrictStructuralDisplayEraseIds", rebuild);
        Assert.Contains("ownerReference", rebuild);
        Assert.Contains("child.Erase();", rebuild);
        Assert.DoesNotContain("Erase(true)", rebuild);
    }

    [Fact]
    public void RigidTransformAndCopyAreResolvedFromCurrentOwnerGeometryAndHandle()
    {
        var rebuild = Segment(Workflow, "private static bool TryRebuildDisplay", "private static bool TryRehydrateGroup");
        Assert.Contains("RoofPolylineExtractor.Extract(owner)", rebuild);
        Assert.Contains("RoofDefinitionPersistence.Restore", rebuild);
        Assert.Contains("RoofPolylineExtractor.GetSourceElevation(owner)", rebuild);
        Assert.Contains("owner.Handle.ToString()", rebuild);
        Assert.DoesNotContain("TransformBy", Service + Store + Workflow);
        Assert.DoesNotContain("Closed = true", Service + Store + Workflow);
    }

    [Fact]
    public void StageFiveAddsNoReactorsDeferredWritesOrTimberGeneration()
    {
        var source = Service + Store + Workflow;
        Assert.DoesNotContain("DatabaseReactor", source);
        Assert.DoesNotContain("ObjectModified", source);
        Assert.DoesNotContain("CommandEnded", source);
        Assert.DoesNotContain("WriteQueue", source);
        Assert.DoesNotContain("TimberElementDataSchema", source);
        Assert.DoesNotContain("TimberElementStore", source);
        Assert.DoesNotContain("GenerateTimber", source);
    }

    [Fact]
    public void GeneratedChildrenAreNeverUsedAsSemanticRoofInput()
    {
        Assert.DoesNotContain("SimpleGableRoofGeometrySolver", Service + Store);
        Assert.DoesNotContain("RoofDefinitionPersistence.Create", Service + Store);
        Assert.DoesNotContain("RoofDefinitionStore.Write", Service + Store);
        Assert.Contains("RoofDefinitionPersistence.Restore", Workflow);
        Assert.True(Workflow.IndexOf("RoofDefinitionPersistence.Restore", StringComparison.Ordinal) <
                    Workflow.IndexOf("RoofDisplayService.Rebuild", StringComparison.Ordinal));
    }

    [Fact]
    public void DisplaySchemaIsOneWhileEstablishedSchemasRemainUnchanged()
    {
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Contains("public const int CurrentVersion = 3", Read(
            "src", "AcKrovy.Core", "Models", "Roofs", "RoofDefinitionDataSchema.cs"));
        Assert.Contains("public const int CurrentVersion = 7", Read(
            "src", "AcKrovy.Core", "Models", "TimberElementDataSchema.cs"));
        Assert.Contains("public const int DrawingSettingsSchemaVersion = 1", Read(
            "src", "AcKrovy.Core", "Models", "TimberDrawingSettings.cs"));
    }

    [Fact]
    public void AllSixLanguagePacksContainTheSameStageFiveKeys()
    {
        var resources = Path.Combine(Repository, "src", "AcKrovy.Localization", "Resources");
        var files = new[]
        {
            "UiStrings.resx", "UiStrings.cs.resx", "UiStrings.en.resx",
            "UiStrings.de.resx", "UiStrings.pl.resx", "UiStrings.fr.resx",
        };
        var required = new[]
        {
            "Command_Roof_PersistedAndDisplaySaved", "Command_Roof_DisplayCurrent",
            "Command_Roof_DisplayMissing", "Command_Roof_DisplayStale",
            "Command_Roof_DisplayCreatePrompt", "Command_Roof_DisplayUpdatePrompt",
            "Command_Roof_DisplayCreated", "Command_Roof_DisplayUpdated",
            "Command_Roof_DisplayFailed", "Command_Roof_DisplayFutureSchema",
            "Command_Roof_GroupMissing", "Command_Roof_GroupRepairPrompt",
            "Command_Roof_GroupRepaired",
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
        File.ReadAllText(Path.Combine([Repository, .. path]))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
