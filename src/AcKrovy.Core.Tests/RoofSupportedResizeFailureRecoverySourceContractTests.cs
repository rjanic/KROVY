using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofSupportedResizeFailureRecoverySourceContractTests
{
    private static readonly string Repository = RepositoryRoot();
    private static readonly string ResizeService = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string RecoveryService = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofUnsupportedStretchRecoveryService.cs");
    private static readonly string ContactRules = Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofStructuralOrdinaryContactRules.cs");
    private static readonly string RecoveryRules = Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofSupportedResizeFailureRecoveryRules.cs");
    private static readonly string Polyhedron = Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofStructuralRafterPolyhedronService.cs");
    private static readonly string PhysicalBuilder = Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofStructuralRafterPhysicalBuilder.cs");

    [Fact]
    public void ContactRules_SkipNonIntersectingTranslatedCuts()
    {
        Assert.Contains("enum Classification", ContactRules);
        Assert.Contains("NonContact", ContactRules);
        Assert.Contains("Corrupt", ContactRules);
        Assert.Contains("TrySelectContacts", ContactRules);
        Assert.Contains("PlanTouchesStructuralEdge", ContactRules);
        Assert.Contains("RoofStructuralOrdinaryContactRules.TrySelectContacts", Polyhedron);
        Assert.Contains("RoofStructuralOrdinaryContactRules.TrySelectContacts", PhysicalBuilder);
        Assert.DoesNotContain(
            "member?.StructuralCut?.TopologyEdgeIndex == resolved.TopologyEdgeIndex).ToArray()",
            Polyhedron);
    }

    [Fact]
    public void HardFailureFinalization_RestoresAggregateAndResetsRuntime()
    {
        Assert.Contains("TryRestoreSupportedResizeFailureAggregate(", RecoveryService);
        Assert.Contains("RestorePolylineGeometry(owner, entry.Assembly.RoofSource)", RecoveryService);
        Assert.Contains("RoofDefinitionStore.Write(owner, transaction, entry.Definition)", RecoveryService);
        Assert.Contains("TryRestoreTimberLines(", RecoveryService);
        Assert.Contains("TryRestoreAnnotations(", RecoveryService);
        Assert.DoesNotContain("TryReconcileInTransaction(",
            Segment(RecoveryService,
                "SupportedResize HardFailure finalization",
                "Restores only StructuralGenerated Hip/Valley"));
        Assert.Contains("FinalizeSupportedResizeHardFailure(", ResizeService);
        Assert.Contains("ROOF_RESIZE_FAILURE_RECOVERY", ResizeService);
        Assert.Contains("ROOF_RESIZE_HARDFAILURE_DETAIL", ResizeService);
        Assert.Contains("sourceGeometryRestored=", ResizeService);
        Assert.Contains("roofDefinitionRestored=", ResizeService);
        Assert.Contains("physicalInventoryRestored=", ResizeService);
        Assert.Contains("groupCanonical=", ResizeService);
        Assert.Contains("IsRecoveryVerdictOk(", ResizeService);
        Assert.Contains("runtimeReset=", ResizeService);
        Assert.Contains("pendingResize=", ResizeService);
        Assert.Contains("nextCommandReady=", ResizeService);
        Assert.Contains("SourceHandledOwnersThisCommand.Clear()", Segment(
            ResizeService,
            "private static void FinalizeSupportedResizeHardFailure",
            "private static ResizeApplyResult HardFailureAt"));
    }

    [Fact]
    public void FailureInjectionSeam_AndCoreRecoveryPhaseMachineExist()
    {
        Assert.Contains("InjectStructuralPhysicalFailureOnce", ResizeService);
        Assert.Contains("OrdinaryCutNotOnStructuralSideFailure", RecoveryRules);
        Assert.Contains("TryRecoverFromHardFailure", RecoveryRules);
        Assert.Contains("TryExecuteAfterRecovery", RecoveryRules);
        Assert.Contains("OrdinaryGripStretch", RecoveryRules);
        Assert.Contains("NextCommandReady", RecoveryRules);
        Assert.Contains("IsRecoveryVerdictOk", RecoveryRules);
        Assert.Contains("HardFailureAt(", ResizeService);
    }

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, "missing start marker: " + start);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, "missing end marker: " + end);
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Repository }.Concat(parts).ToArray()));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
