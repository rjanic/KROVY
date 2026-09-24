using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticStructuralRafterMaterializationSourceContractTests
{
    private static readonly string Service = Read("Infrastructure", "RoofAutomaticStructuralRafterMaterializationService.cs");
    private static readonly string Collector = Read("Infrastructure", "RoofAssemblyGroupMemberCollector.cs");
    private static readonly string Workflow = Read("Infrastructure", "RoofRafterCommandWorkflow.cs");
    private static readonly string Trace = Read("Infrastructure", "RoofAutomaticStructuralRafterTrace.cs");

    [Fact]
    public void Materializer_AnnotatesEverySurvivingDesiredMemberThroughExistingPipeline()
    {
        Assert.Contains("annotationTargets[existing.Id] = timberData", Service);
        Assert.Contains("annotationTargets[id] = newTimberData", Service);
        Assert.Contains(
            "TimberCreatedElementAnnotationService.EnsureForCreatedElements(",
            Service);
        Assert.Contains("annotationTargets,", Service);
        Assert.True(
            Service.IndexOf(
                "TimberCreatedElementAnnotationService.EnsureForCreatedElements(",
                StringComparison.Ordinal) <
            Service.IndexOf(
                "RoofAssemblyGroupSyncService.TrySyncForOwner",
                StringComparison.Ordinal));
        Assert.Contains("TimberAnnotationService.DeleteForSourceHandle(", Service);
        Assert.True(
            Service.IndexOf("TimberAnnotationService.DeleteForSourceHandle(", StringComparison.Ordinal) <
            Service.IndexOf("stale.Erase()", StringComparison.Ordinal));
        Assert.DoesNotContain("TimberAnnotationMode.NoAnnotations", Service);
        Assert.DoesNotContain("RoofAutomaticPurlinMaterializationRules", Service);
    }

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
        Assert.Contains("var desiredByKey = desired.ToDictionary", Service);
        Assert.Contains("!desiredByKey.ContainsKey", Service);
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
        var create = Member(
            Workflow,
            "private static RoofRafterCreationResult TryCreateRafters(",
            "private static bool IsGeneratedSetStale(");
        var ordinary = create.IndexOf(
            "RoofGeneratedRafterSetService.Materialize(",
            StringComparison.Ordinal);
        var structural = create.IndexOf(
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(",
            StringComparison.Ordinal);
        var commit = create.IndexOf("transaction.Commit();", StringComparison.Ordinal);

        Assert.True(ordinary >= 0 && structural > ordinary && commit > structural);
        Assert.Contains("if (!structuralRafters.IsSuccess)", create);
        Assert.Contains("currentValidation.Layout.Rafters.Count + automaticStructuralRafterCount", create);
        Assert.Equal(1, Count(create, "transaction.Commit();"));
    }

    [Fact]
    public void ExistingOrdinaryHipSet_OpensEditAndReconcilesStructuralOnApply()
    {
        var run = Member(
            Workflow,
            "public static void Run(",
            "private static bool TryRecoverExistingRecipe(");
        Assert.Contains("var isEdit = selectedRoof.ExistingGeneratedRafterCount > 0", run);
        Assert.Contains("TryRecoverExistingRecipe(", run);
        Assert.Contains("TryReplaceRafters(", run);
        Assert.DoesNotContain(
            "RoofAutomaticStructuralRafterMaterializationService.Materialize(",
            run);
        Assert.DoesNotContain("Command_RoofRafters_ReplacementDeferred", run);

        var replace = Member(
            Workflow,
            "private static RoofRafterCreationResult TryReplaceRafters(",
            "private static RoofRafterCreationResult TryCreateRafters(");
        AssertOrdered(
            replace,
            "TryReplaceWithEditedRecipe(",
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(",
            "transaction.Commit();");
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
    public void Service_RemainsDesiredStateReconcilerWithoutOwningLiveEventHooks()
    {
        Assert.Contains("Authoritative desired-state materialization", Service);
        Assert.DoesNotContain("LiveGeometrySynchronizationService", Service);
        Assert.DoesNotContain("CommandEnded", Service);
        Assert.DoesNotContain("ObjectModified", Service);
        Assert.DoesNotContain("Undo", Service, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LiveSupportedResize_ReusesMaterializeInTransactionForExistingStructuralSet()
    {
        var resize = Read("Infrastructure", "RoofLiveResizeService.cs");
        var apply = Member(
            resize,
            "private static ResizeApplyResult TryApplyResize",
            "private static IReadOnlyCollection<ObjectId> TryAcceptRigidGroupTransforms");
        Assert.Contains(
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(",
            apply);
        Assert.Contains("existingStructuralCount > 0", apply);
        Assert.Contains("if (!structural.IsSuccess)", apply);
        Assert.Contains("ResizeApplyResult.HardFailure", apply);
        Assert.DoesNotContain("RoofAutomaticPurlinMaterializationService", apply);
        Assert.True(
            resize.IndexOf(
                "if (LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName))",
                StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void HostTruthTrace_IsDebugOnlyAndCoversEveryStructuralPipelineStage()
    {
        Assert.StartsWith("#if DEBUG", Trace.TrimStart());
        Assert.EndsWith("#endif", Trace.TrimEnd());
        Assert.Contains("ROOF_STRUCT_INPUT", Trace);
        Assert.Contains("ROOF_STRUCT_FOOTPRINT", Trace);
        Assert.Contains("ROOF_STRUCT_TOPOLOGY", Trace);
        Assert.Contains("ROOF_STRUCT_DESIRED", Trace);
        Assert.Contains("ROOF_STRUCT_EXISTING", Trace);
        Assert.Contains("ROOF_STRUCT_RECONCILE", Trace);
        Assert.Contains("ROOF_STRUCT_FINAL", Trace);
        Assert.Contains("ROOF_STRUCT_FINAL_SUMMARY", Trace);
        Assert.Contains("desiredEqualsFinal", Trace);
        Assert.Contains("reuse-unchanged", Service);
        Assert.Contains("update-geometry", Service);
        Assert.Contains("create", Service);
        Assert.Contains("stale-erase", Service);
        Assert.Contains("duplicate-block", Service);
        Assert.Contains("malformed-block", Service);
        AssertDebugGuarded(Service, "RoofAutomaticStructuralRafterTrace.WriteTopologyAndDesired");
        AssertDebugGuarded(Service, "RoofAutomaticStructuralRafterTrace.WriteExistingSet");
        AssertDebugGuarded(Service, "RoofAutomaticStructuralRafterTrace.WriteFinalSetAndSummary");
    }

    [Fact]
    public void ReconciliationContract_UpdatesSameKeyGeometryAndChecksDesiredFinalEquality()
    {
        Assert.Contains("!SamePoint(existing.Line.StartPoint, start)", Service);
        Assert.Contains("!SamePoint(existing.Line.EndPoint, end)", Service);
        Assert.Contains("existing.Line.StartPoint = start", Service);
        Assert.Contains("existing.Line.EndPoint = end", Service);
        Assert.Contains("actualIds.Count == desired.Count", Service);
        Assert.Contains("duplicates == 0", Service);
        Assert.Contains("missing == 0", Service);
        Assert.Contains("groupCanonical", Service);
        Assert.Contains("VerifyMembers(database, transaction, metadataStore, actualIds, desiredByKey)", Service);
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

    private static string Member(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, startMarker);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, endMarker);
        return source[start..end];
    }

    private static void AssertOrdered(string source, params string[] values)
    {
        var previous = -1;
        foreach (var value in values)
        {
            var current = source.IndexOf(value, StringComparison.Ordinal);
            Assert.True(current > previous, value);
            previous = current;
        }
    }

    private static void AssertDebugGuarded(string source, string value)
    {
        var valueIndex = source.IndexOf(value, StringComparison.Ordinal);
        Assert.True(valueIndex >= 0, value);
        var guardIndex = source.LastIndexOf("#if DEBUG", valueIndex, StringComparison.Ordinal);
        var endIndex = source.LastIndexOf("#endif", valueIndex, StringComparison.Ordinal);
        Assert.True(guardIndex > endIndex, value + " must remain DEBUG-only.");
    }
}
