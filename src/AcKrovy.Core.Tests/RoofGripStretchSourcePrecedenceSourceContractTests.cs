using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGripStretchSourcePrecedenceSourceContractTests
{
    private static readonly string ResizeService = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string LiveGeometry = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string CommandRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "LiveGeometryCommandRules.cs");
    private static readonly string Replacement = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterSetService.cs");
    private static readonly string Rehydration = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterCopyOwnershipRehydrationService.cs");

    [Fact]
    public void SourceGripStretch_AndClassicStretch_ShareUndoGrouping()
    {
        Assert.True(AcKrovy.Core.Services.LiveGeometryCommandRules.IsUndoGroupingSourceCommand("GRIP_STRETCH"));
        Assert.True(AcKrovy.Core.Services.LiveGeometryCommandRules.IsUndoGroupingSourceCommand("STRETCH"));
        Assert.Contains("\"GRIP_STRETCH\"", CommandRules);
        Assert.Contains("\"STRETCH\"", CommandRules);
    }

    [Fact]
    public void LiveGeometry_SuppressesObjectModifiedDuringRoofProcess()
    {
        Assert.Contains("using (_modifiedIds.Suppress())", LiveGeometry);
        Assert.Contains("RoofLiveResizeService.Process(", LiveGeometry);
        var processIndex = LiveGeometry.IndexOf(
            "RoofLiveResizeService.Process(",
            StringComparison.Ordinal);
        var suppressIndex = LiveGeometry.LastIndexOf(
            "using (_modifiedIds.Suppress())",
            processIndex,
            StringComparison.Ordinal);
        Assert.True(suppressIndex >= 0 && suppressIndex < processIndex);
    }

    [Fact]
    public void SourceHandledOwners_SuppressDeferredDisplayTamperForSameCommand()
    {
        Assert.Contains("SourceHandledOwnersThisCommand", ResizeService);
        Assert.Contains("BeginStretchCommandScope()", ResizeService);
        Assert.Contains("EndStretchCommandScope()", ResizeService);
        Assert.Contains("BeginStretchCommandScope()", LiveGeometry);
        Assert.Contains("EndStretchCommandScope()", LiveGeometry);
        var inspect = Member(
            ResizeService,
            "private static InspectionPlan Inspect(",
            "private static void ApplyResizes");
        Assert.Contains("SourceHandledOwnersThisCommand.Contains(ownerId)", inspect);
        Assert.Contains("resizeOwners.Contains(ownerId)", inspect);
        Assert.Contains("unsupportedOwners.Contains(ownerId)", inspect);
    }

    [Fact]
    public void SourceGripStretch_DoesNotEmitDisplayWarningOnSupportedResizeBranch()
    {
        var process = Member(
            ResizeService,
            "public static IReadOnlyCollection<ObjectId> Process(",
            "private static InspectionPlan Inspect(");
        var resizeBranch = Member(
            process,
            "if (plan.ResizeOwnerIds.Count > 0)",
            "if (plan.UnsupportedOwnerIds.Count > 0)");
        Assert.DoesNotContain("Command_Roof_DisplayTamperNotificationTitle", resizeBranch);
        Assert.Contains("SourceHandledOwnersThisCommand.Add(ownerId)", resizeBranch);
        Assert.Contains("ApplyResizes(", resizeBranch);
    }

    [Fact]
    public void RealDisplayOnlyGripStretch_StillRoutesDisplayWarningOnce()
    {
        var displayBranch = Member(
            ResizeService,
            "IReadOnlyCollection<ObjectId> displayTamperOwners = plan.DisplayTamperOwnerIds",
            "return plan.RelatedIds;");
        Assert.Contains("IsUndoGroupingSourceCommand(globalCommandName)", displayBranch);
        Assert.Contains("Command_Roof_DisplayTamperNotificationTitle", displayBranch);
        Assert.Equal(1, Count(displayBranch, "TransientNotificationService.Show("));
    }

    [Fact]
    public void UnsupportedSourceGrip_UsesUnsupportedWarningOnly()
    {
        var process = Member(
            ResizeService,
            "public static IReadOnlyCollection<ObjectId> Process(",
            "private static InspectionPlan Inspect(");
        var unsupportedBranch = Member(
            process,
            "if (plan.UnsupportedOwnerIds.Count > 0)",
            "IReadOnlyCollection<ObjectId> displayTamperOwners = plan.DisplayTamperOwnerIds");
        Assert.Contains("Command_Roof_UnsupportedStretchNotificationTitle", unsupportedBranch);
        Assert.DoesNotContain("Command_Roof_DisplayTamperNotificationTitle", unsupportedBranch);
        Assert.Contains("SourceHandledOwnersThisCommand.Add(ownerId)", unsupportedBranch);
    }

    [Fact]
    public void RafterRegenerationAndCopyOwnershipRemainOnExistingPaths()
    {
        Assert.Contains("TryReplaceForSupportedResize(", Replacement);
        Assert.Contains("FindByOwner(", Replacement);
        Assert.Contains("RoofGeneratedRafterCopyOwnershipRehydrationService.Process(", LiveGeometry);
        Assert.Contains("IsSameDwgCopyOwnershipCommand", Rehydration);
        Assert.DoesNotContain("BeginDeepClone", ResizeService + LiveGeometry + Replacement);
        Assert.DoesNotContain("EndDeepClone", ResizeService + LiveGeometry + Replacement);
        Assert.DoesNotContain("IdMapping", ResizeService + LiveGeometry + Replacement);
    }

    [Fact]
    public void UndoRedoStillSkipsRoofProcessWrites()
    {
        var processGuard = Member(
            ResizeService,
            "public static IReadOnlyCollection<ObjectId> Process(",
            "var plan = Inspect(");
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", processGuard);
        Assert.Contains("return Array.Empty<ObjectId>();", processGuard);
        Assert.DoesNotContain("ApplyDisplayTampers(", processGuard);
        Assert.DoesNotContain("ApplyResizes(", processGuard);
    }

    [Fact]
    public void SuppressionScopeClearsOnNextCommandBoundary()
    {
        Assert.Contains("BeginStretchCommandScope()", LiveGeometry);
        var willStart = Member(
            LiveGeometry,
            "private void CommandWillStart",
            "private void CommandEnded");
        Assert.Contains("BeginStretchCommandScope()", willStart);
        Assert.Contains("EndStretchCommandScope()", LiveGeometry);
        Assert.Contains("CommandCancelled", LiveGeometry);
        Assert.Contains("CommandFailed", LiveGeometry);
    }

    private static string Member(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);

    private static int Count(string value, string token) =>
        (value.Length - value.Replace(token, string.Empty, StringComparison.Ordinal).Length) /
        token.Length;
}
