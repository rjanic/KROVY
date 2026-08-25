using AcKrovy.Core.Models.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class MonopitchRafterStage2D1SourceContractTests
{
    private static readonly string Root = RepositoryRoot();
    private static readonly string Replacement = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedRafterSetService.cs");
    private static readonly string Resize = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/RoofLiveResizeService.cs");
    private static readonly string Edit = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/RoofEditCommandWorkflow.cs");
    private static readonly string EditState = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/RoofEditStateCommandWorkflow.cs");
    private static readonly string ChildPolicy = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/RoofSourceResizeChildPolicyService.cs");
    private static readonly string Live = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs");

    [Fact]
    public void SharedReplacement_AcceptsNeutralGeometryAndSolvesNeutralLayout()
    {
        Assert.Contains("IRoofGeometry geometry", Replacement);
        Assert.Contains("RoofRafterLayoutSolver.Solve(", Replacement);
        Assert.Contains("Materialize(", Replacement);
        Assert.Contains("RoofRafterMaterializationRules.IsConsistent(geometry, layout)", Replacement);
        Assert.DoesNotContain("var layoutResult = SimpleGableRafterLayoutSolver.Solve(", Replacement);
    }

    [Fact]
    public void SupportedSourceResize_RoutesRestoredMonopitchGeometryThroughSharedReplacement()
    {
        var apply = Segment(
            Resize,
            "private static ResizeApplyResult TryApplyResize(",
            "private static IReadOnlyCollection<ObjectId> TryAcceptRigidGroupTransforms(");

        Assert.Contains("classification.Geometry", apply);
        Assert.Contains("RoofGeneratedRafterSetService.TryReplaceForSupportedResize(", apply);
        Assert.DoesNotContain("classification.Geometry is SimpleGableRoofGeometry", apply);
        Assert.Contains("RoofDefinitionPersistence.Create(", apply);
        Assert.Contains("RoofDisplayService.Rebuild(", apply);
        Assert.Contains("RoofSourceResizeChildPolicyService.Apply(", apply);
    }

    [Fact]
    public void SemanticEditAndEditState_UseTheSameNeutralReplacementBoundary()
    {
        var apply = Segment(
            Edit,
            "private static RoofGeneratedRafterSetService.ReplacementOutcome? TryApply(",
            "private static string GetSoftReplacementMessage(");
        var rebuild = Member(
            EditState,
            "private static bool TryRebuildGeneratedSet(");

        Assert.Contains("restored.Geometry", apply);
        Assert.Contains("forceRegenerateOnSourceResize: geometryChanged", apply);
        Assert.DoesNotContain("restored.Geometry is SimpleGableRoofGeometry", apply);
        Assert.Contains("restored.Geometry", rebuild);
        Assert.DoesNotContain("restored.Geometry is not SimpleGableRoofGeometry", rebuild);
    }

    [Fact]
    public void RecipeIdentityAnnotationsAndGroupRemainOnTheProvenSharedPath()
    {
        Assert.Contains("TryRecoverRecipe(", Replacement);
        Assert.Contains("timber.WidthMm", Replacement);
        Assert.Contains("timber.HeightMm", Replacement);
        Assert.Contains("generated.Data.RequestedMaximumSpacingMm", Replacement);
        Assert.Contains("timber.Material", Replacement);
        Assert.Contains("RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations", Replacement);
        Assert.Contains("CollectReservedElementIds(", Replacement);
        Assert.Contains("EraseGeneratedSet(", Replacement);
        Assert.Contains("ElementLabelService.DeleteForSourceHandle", Replacement);
        Assert.Contains("SlopeAnnotationService.DeleteForSourceHandle", Replacement);
        Assert.Contains("TimberCreatedElementAnnotationService.EnsureForCreatedElements", Replacement);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", Replacement);
        Assert.DoesNotContain("GeometricExtents", Replacement + Resize);
    }

    [Fact]
    public void AttachedManualReplay_IsEnabledOnlyByLaterSharedStage2D3Path()
    {
        Assert.DoesNotContain("geometry.Kind != RoofKind.Monopitch", Replacement);
        Assert.Contains("replayAttachedManualChildren: true", Resize);
        Assert.DoesNotContain("restored.Geometry.Kind != RoofKind.Monopitch", Edit);
    }

    [Fact]
    public void ChildPolicyDiagnostic_CountsSuccessfulReplacementAndUsesSharedReplaySwitch()
    {
        Assert.Contains("rafterOutcome,", Resize);
        Assert.DoesNotContain("childPolicyOutcome", Resize);
        Assert.Contains(
            "var generatedRebuilt = rafterOutcome == RoofGeneratedRafterSetService.ReplacementOutcome.Replaced",
            ChildPolicy);
        Assert.Contains(
            "if (generatedRebuilt > 0 && replayAttachedManualChildren)",
            ChildPolicy);
    }

    [Fact]
    public void AtomicityAndUndoRedoZeroDatabaseBoundaryRemainIntact()
    {
        var applyResizes = Segment(
            Resize,
            "private static void ApplyResizes(",
            "private static ResizeApplyResult TryApplyResize(");
        var commandEnded = Segment(
            Live,
            "private void CommandEnded(",
            "private void CommandCancelled(");

        Assert.Equal(1, Count(applyResizes, "StartTransaction()"));
        Assert.Equal(1, Count(applyResizes, "transaction.Commit()"));
        Assert.Contains("ResizeApplyResult.HardFailure", applyResizes);
        var editApply = Segment(
            Edit,
            "private static RoofGeneratedRafterSetService.ReplacementOutcome? TryApply(",
            "private static string GetSoftReplacementMessage(");
        var hardFailureIndex = editApply.IndexOf(
            "outcome == RoofGeneratedRafterSetService.ReplacementOutcome.Failed",
            StringComparison.Ordinal);
        var commitIndex = editApply.IndexOf("transaction.Commit()", StringComparison.Ordinal);
        Assert.True(hardFailureIndex >= 0);
        Assert.True(commitIndex > hardFailureIndex);
        Assert.Contains("return null;", editApply[hardFailureIndex..commitIndex]);
        Assert.Contains("IsUndoRedoCommand(e.GlobalCommandName)", commandEnded);
        Assert.DoesNotContain("LockDocument(", commandEnded);
        Assert.DoesNotContain("StartTransaction(", commandEnded);
        Assert.DoesNotContain("StartOpenCloseTransaction(", commandEnded);
        Assert.DoesNotContain("GetObject(", commandEnded);
        Assert.DoesNotContain("System.Timers", Replacement + Resize + Edit);
        Assert.DoesNotContain("DispatcherTimer", Replacement + Resize + Edit);
    }

    [Fact]
    public void VersionAndSchemaContractsRemainUnchanged()
    {
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Equal(3, RoofAttachedManualTimberDataSchema.CurrentVersion);
        Assert.Equal(3, (int)RoofKind.Monopitch);
        Assert.Contains("<AcKrovyVersion>0.23.0</AcKrovyVersion>", Read("Directory.Build.props"));
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

    private static string Member(string source, string start)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing member marker: {start}");
        return source[startIndex..];
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

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
