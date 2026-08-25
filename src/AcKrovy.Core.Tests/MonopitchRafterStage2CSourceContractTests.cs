using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class MonopitchRafterStage2CSourceContractTests
{
    private static readonly string Root = RepositoryRoot();
    private static readonly string Workflow = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/RoofRafterCommandWorkflow.cs");
    private static readonly string Materializer = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedRafterSetService.cs");
    private static readonly string Creator = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/TimberSourceLineCreationService.cs");
    private static readonly string Window = Read(
        "src/AcKrovy.AutoCAD/UI/RoofRafterWindow.xaml.cs");

    [Fact]
    public void ValidMonopitchRequest_RevalidatesCurrentGeometryAndUsesOneCommit()
    {
        var create = Segment(
            Workflow,
            "private static RoofRafterCreationResult TryCreateRafters(",
            "private static bool IsGeneratedSetStale(");

        Assert.Contains("restored.Geometry.Kind != expectedRoofKind", create);
        Assert.Contains("not MonopitchRoofGeometry", create);
        Assert.Contains("RoofRafterRequestValidator.Validate(", create);
        Assert.Contains("restored.Geometry,", create);
        Assert.Contains("RoofRafterMaterializationRules.IsConsistent(", create);
        Assert.Equal(1, Count(create, "TransactionManager.StartTransaction()"));
        Assert.Equal(1, Count(create, "transaction.Commit();"));
        Assert.True(
            create.IndexOf("RoofGeneratedRafterSetService.Materialize(", StringComparison.Ordinal) <
            create.IndexOf("transaction.Commit();", StringComparison.Ordinal));
    }

    [Fact]
    public void SharedMaterializer_UsesNeutralGeometryAndExactlyTheExistingT2Core()
    {
        Assert.Contains("IRoofGeometry geometry", Materializer);
        Assert.Contains("RoofRafterLayout layout", Materializer);
        Assert.Contains("RoofRafterMaterializationRules.IsConsistent(geometry, layout)", Materializer);
        Assert.Contains("RoofGeneratedMemberReplayPlanner.Create", Materializer);
        Assert.Contains("foreach (var replayItem in replayPlan.Items)", Materializer);
        Assert.Contains("rafter.LogicalKey", Materializer);
        Assert.Contains("rafter.Face,", Materializer);
        Assert.Contains("rafter.StationIndex,", Materializer);
        Assert.Contains("rafter.StationCount,", Materializer);
        Assert.Contains("rafter.SlopeDegrees", Materializer);
        Assert.Contains("TimberSourceLineCreationService.Create(", Materializer);
        Assert.Contains("TimberCreatedElementAnnotationService.EnsureForCreatedElements(", Materializer);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", Materializer);

        var core = Segment(
            Materializer,
            "private static MaterializationResult MaterializeCore(",
            "private sealed record MaterializationResult(");
        Assert.Equal(1, Count(core, "TimberSourceLineCreationService.Create("));
        Assert.Equal(1, Count(core, "TimberCreatedElementAnnotationService.EnsureForCreatedElements("));
        Assert.Equal(1, Count(core, "RoofAssemblyGroupSyncService.TrySyncForOwner"));
    }

    [Fact]
    public void T2LineCreation_WritesCombinedMetadataBeforeRegisteringNewObject()
    {
        var append = Creator.IndexOf("modelSpace.AppendEntity(line)", StringComparison.Ordinal);
        var metadata = Creator.IndexOf("RoofGeneratedTimberStore.WriteAtomic(", StringComparison.Ordinal);
        var register = Creator.IndexOf(
            "transaction.AddNewlyCreatedDBObject(line, true)",
            metadata,
            StringComparison.Ordinal);

        Assert.True(append >= 0 && metadata > append && register > metadata);
        Assert.Contains("effectiveData,", Creator);
        Assert.Contains("secondarySection", Creator);
        Assert.DoesNotContain("DxfCode.ExtendedDataHandle", Creator);
    }

    [Fact]
    public void Scope_RemainsStage2COnly()
    {
        Assert.Contains("CreateButton.IsEnabled = validation.IsValid", Window);
        Assert.DoesNotContain("_geometry.Kind == RoofKind.Monopitch", Window);
        Assert.Contains("ExistingGeneratedRafterCount > 0", Workflow);
        Assert.Contains("Command_RoofRafters_ReplacementDeferred", Workflow);
        Assert.DoesNotContain("System.Timers", Workflow + Materializer);
        Assert.DoesNotContain("DispatcherTimer", Workflow + Materializer);
        Assert.DoesNotContain("GeometricExtents", Materializer);
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing start marker: {start}");
        Assert.True(endIndex > startIndex, $"Missing end marker: {end}");
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0;
             (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0;
             index += value.Length)
        {
            count++;
        }
        return count;
    }

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
