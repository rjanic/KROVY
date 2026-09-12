using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticStructuralRafterMaterializationSourceContractTests
{
    private static readonly string Service = Read("Infrastructure", "RoofAutomaticStructuralRafterMaterializationService.cs");
    private static readonly string Collector = Read("Infrastructure", "RoofAssemblyGroupMemberCollector.cs");
    private static readonly string Workflow = Read("Infrastructure", "RoofRafterCommandWorkflow.cs");

    [Fact]
    public void ExplicitMaterializer_UsesIdentityPlanAndAuthoritativeThreeDimensionalEndpoints()
    {
        Assert.Contains("RoofBoundaryIdentityService.EnsureBoundaryIdentity", Service);
        Assert.Contains("RoofStructuralEdgeIdentityResolver.Resolve", Service);
        Assert.Contains("RoofAutomaticStructuralRafterPlanner.Create", Service);
        Assert.Contains("MapPoint(item.Segment3D.Start", Service);
        Assert.Contains("MapPoint(item.Segment3D.End", Service);
        Assert.Contains("line.Length - item.True3DLengthMm", Service);
        Assert.DoesNotContain("CalculateSlopeCorrectedLengthMm", Service);
        Assert.DoesNotContain("35d", Service);
    }

    [Fact]
    public void Materializer_PreservesMatchingEntityAndElementIdWhileRemovingStaleMembers()
    {
        Assert.Contains("survivorByKey.TryGetValue(item.LogicalKey", Service);
        Assert.Contains("existing.Line.StartPoint = start", Service);
        Assert.Contains("existing.TimberData.ElementId", Service);
        Assert.Contains("stale.Erase()", Service);
        Assert.Contains("ElementNumberingService.GetNextNumber", Service);
        Assert.DoesNotContain("SynchronizeElementIds", Service);
    }

    [Fact]
    public void Materializer_WritesBothSchemasAndExcludesOrdinaryGeneratedMetadata()
    {
        Assert.Contains("RoofStructuralGeneratedStore.WriteAtomic", Service);
        Assert.Contains("TimberElementDataSchema.CurrentVersion", Service);
        Assert.Contains("RoofStructuralGeneratedDataSchema.CurrentVersion", Service);
        Assert.Contains("RoofGeneratedTimberStore.Read(line).Exists", Service);
        Assert.Contains("RoofAttachedManualTimberStore.Read(line).Exists", Service);
        Assert.DoesNotContain("RoofGeneratedTimberStore.Write", Service);
        Assert.DoesNotContain("RoofGeneratedTimberData", Service);
    }

    [Fact]
    public void ProductionRafterCommand_ComposesOrdinaryAndStructuralRaftersInOneTransaction()
    {
        var ordinary = Workflow.IndexOf(
            "RoofGeneratedRafterSetService.Materialize(",
            StringComparison.Ordinal);
        var structural = Workflow.IndexOf(
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(",
            StringComparison.Ordinal);
        var commit = Workflow.IndexOf("transaction.Commit();", StringComparison.Ordinal);

        Assert.True(ordinary >= 0 && structural > ordinary && commit > structural);
        Assert.Contains("if (!structuralRafters.IsSuccess)", Workflow);
        Assert.Contains("currentValidation.Layout.Rafters.Count + automaticStructuralRafterCount", Workflow);
        Assert.Equal(1, Count(Workflow, "transaction.Commit();"));
    }

    [Fact]
    public void AutomaticStructuralRafterMaterializer_DoesNotMaterializeRidge()
    {
        Assert.Contains("RoofStructuralEdgeIdentityResolver.Resolve", Service);
        Assert.Contains("RoofAutomaticStructuralRafterPlanner.Create", Service);
        Assert.DoesNotContain("TimberElementType.Purlin", Service);
        Assert.DoesNotContain("RidgeMember", Service);
    }

    [Fact]
    public void CanonicalGroupCollector_ClassifiesStructuralGeneratedSeparately()
    {
        Assert.Contains("RoofStructuralGeneratedStore.FindByOwner", Collector);
        Assert.Contains("StructuralGeneratedCount", Collector);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", Service);
        Assert.Contains("RoofDisplayGroupService.Inspect", Service);
    }

    [Fact]
    public void Service_IsStaticExplicitOnlyAndNotConnectedToLiveLifecycle()
    {
        Assert.Contains("Authoritative explicit desired-state materialization", Service);
        Assert.DoesNotContain("LiveGeometrySynchronizationService", Service);
        Assert.DoesNotContain("CommandEnded", Service);
        Assert.DoesNotContain("ObjectModified", Service);
        Assert.DoesNotContain("Undo", Service, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(string folder, string fileName) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            folder,
            fileName));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;
}
