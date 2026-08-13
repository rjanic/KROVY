using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDefinitionPersistenceSourceContractTests
{
    private static readonly string Repository = RepositoryRoot();
    private static readonly string Workflow = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofCommandWorkflow.cs");
    private static readonly string Store = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDefinitionStore.cs");

    [Fact]
    public void ReadPath_IsDedicatedAndHasNoRegistrationOrWriteOperation()
    {
        var readPath = Segment(Store, "public static RoofDefinitionStoreReadResult Read", "public static void Write");
        Assert.Contains("GetXDataForApplication(RegAppName)", readPath);
        Assert.DoesNotContain("EnsureRegAppRegistered", readPath);
        Assert.DoesNotContain("ForWrite", readPath);
        Assert.DoesNotContain("UpgradeOpen", readPath);
        Assert.DoesNotContain("XData =", readPath);
        Assert.DoesNotContain("Commit", readPath);
    }

    [Fact]
    public void WritePath_IsAfterExplicitConfirmationAndUsesOneShortCommit()
    {
        var confirmIndex = Workflow.IndexOf("ConfirmPersistence(editor)", StringComparison.Ordinal);
        var persistIndex = Workflow.IndexOf("TryPersist(", confirmIndex, StringComparison.Ordinal);
        var writeIndex = Workflow.IndexOf("RoofDefinitionStore.Write", StringComparison.Ordinal);
        Assert.True(confirmIndex >= 0);
        Assert.True(persistIndex > confirmIndex);
        Assert.True(writeIndex > persistIndex);
        Assert.Contains("PromptKeywordOptions", Workflow);
        Assert.Contains("GetKeywords", Workflow);
        Assert.Contains("OpenMode.ForWrite", Workflow);
        Assert.Equal(1, CountOccurrences(Workflow, "transaction.Commit();"));
        Assert.DoesNotContain("GetKeywords", Segment(Workflow, "private static bool TryPersist", "private static bool TryPromptParameters"));
    }

    [Fact]
    public void Writer_ReplacesOnlyRoofChunkAndPreservesForeignXData()
    {
        Assert.Contains("DECORAIR_ACADKROVY_ROOF", Store);
        Assert.Contains("var retained = ReadForeignXData(entity);", Store);
        Assert.Contains("if (!skipRoofSection)", Store);
        Assert.Contains("retained.Add(value);", Store);
        Assert.Contains("retained.Add(new TypedValue(DxfRegAppNameCode, RegAppName))", Store);
        Assert.Equal(1, CountOccurrences(Store, "entity.XData = buffer;"));
        Assert.DoesNotContain("DECORAIR_ACADKROVY\"", Store);
        Assert.DoesNotContain("TimberElementDataSchema", Store + Workflow);
        Assert.DoesNotContain("TimberDrawingSettings", Store + Workflow);
    }

    [Fact]
    public void Workflow_RejectsInvalidFutureUnsupportedAndStaleMetadataWithoutOverwrite()
    {
        Assert.Contains("storedDefinition.Data is null", Workflow);
        Assert.Contains("UnsupportedFutureSchema", Workflow);
        Assert.Contains("UnsupportedRoofKind", Workflow);
        Assert.Contains("RoofDefinitionRestoreError.StaleFootprint", Workflow);
        Assert.Contains("if (RoofDefinitionStore.Read(owner).Exists)", Workflow);
        Assert.Contains("Command_Roof_PersistConflict", Workflow);
    }

    [Fact]
    public void NoPermanentRoofEntityAppendPathWasIntroduced()
    {
        var source = Store + Workflow;
        Assert.DoesNotContain("AppendEntity", source);
        Assert.DoesNotContain("BlockTableRecord", source);
        Assert.DoesNotContain("ModelSpace", source);
        Assert.DoesNotContain("PaperSpace", source);
        Assert.DoesNotContain("new Line(", source);
        Assert.DoesNotContain("3DFace", source);
        Assert.DoesNotContain("Polyline3d", source);
        Assert.DoesNotContain("Solid3d", source);
        Assert.DoesNotContain("BlockReference", source);
        Assert.DoesNotContain("TimberElement", source);
    }

    [Fact]
    public void ReadSelectionAndPreviewRemainOutsideWriteScope()
    {
        var selectionPath = Segment(Workflow, "while (true)", "private static void ShowPreview");
        Assert.Contains("OpenMode.ForRead", selectionPath);
        Assert.Contains("RoofDefinitionStore.Read(polyline)", selectionPath);
        Assert.DoesNotContain("OpenMode.ForWrite", selectionPath);
        Assert.DoesNotContain("transaction.Commit", selectionPath);
        Assert.Contains("ShowPreview(document, restored.Geometry", selectionPath);
        Assert.Contains("ShowPreview(document, geometryResult.Geometry", selectionPath);
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
