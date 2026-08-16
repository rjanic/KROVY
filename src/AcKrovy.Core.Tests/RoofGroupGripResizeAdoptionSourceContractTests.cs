using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGroupGripResizeAdoptionSourceContractTests
{
    private static readonly string Resize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Adoption = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGroupGripResizeAdoptionService.cs");
    private static readonly string Rules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofGroupGripResizeAdoptionRules.cs");
    private static readonly string CommandRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "LiveGeometryCommandRules.cs");
    private static readonly string Rehydration = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterCopyOwnershipRehydrationService.cs");

    [Fact]
    public void GripStretchOnly_TriggersAdoptionAttemptBeforeDisplayTamper()
    {
        Assert.Contains("IsGripStretchCommand", CommandRules);
        Assert.Contains("IsGripStretchCommand(globalCommandName)", Resize);
        Assert.Contains("TryAdoptGroupGripResizes", Resize);
        Assert.Contains("ApplyDisplayTampers", Resize);
        var process = RoofUxSourceContractText.Member(
            Resize,
            "public static IReadOnlyCollection<ObjectId> Process",
            "public static bool TryBeginGroupedUndo");
        Assert.True(
            process.IndexOf("TryAdoptGroupGripResizes", StringComparison.Ordinal) <
            process.IndexOf("ApplyDisplayTampers", StringComparison.Ordinal));
    }

    [Fact]
    public void Adoption_RequiresRigidEquivalentThenWritesSameSourceAndReusesTryApplyResize()
    {
        Assert.Contains("RoofSourceChangeKind.RigidEquivalent", Adoption);
        Assert.Contains("WriteSourceVertices", Adoption);
        Assert.Contains("TryApplyResize", Resize);
        Assert.Contains("SourceHandledOwnersThisCommand", Resize);
        Assert.Contains("TryAdoptGroupGripResizes", Resize);
        Assert.Contains("TryDeriveSupportedSideResize", Adoption + Rules);
        Assert.Contains("TryGetLatestObservedDisplayByRole", Adoption);
    }

    [Fact]
    public void Adoption_RejectsAmbiguityAndFallsBackToDisplayTamper()
    {
        Assert.Contains("RejectionReason", Adoption);
        Assert.Contains("Command_Roof_DisplayTamperNotificationTitle", Resize);
        Assert.Contains("not-unique-side-resize", Rules);
        Assert.Contains("observed-not-wireframe-of-adopted", Rules);
    }

    [Fact]
    public void NoParallelGeometryPipelineOrCommandInjection()
    {
        var source = Adoption + Resize + Rules;
        Assert.DoesNotContain("SendStringToExecute", source);
        Assert.DoesNotContain("SimpleGableRoofGeometrySolver.Solve", Adoption);
        Assert.Contains("RoofDefinitionPersistence.Classify", Adoption);
        Assert.Contains("TryApplyResize", Resize);
        Assert.DoesNotContain("BeginDeepClone", source);
        Assert.DoesNotContain("EndDeepClone", source);
        Assert.DoesNotContain("IdMapping", source);
        Assert.DoesNotContain("DatabaseReactor", source);
        Assert.DoesNotContain("ObjectOverrule", source);
    }

    [Fact]
    public void CopyRafterRehydrationUntouched()
    {
        Assert.DoesNotContain("RoofGroupGripResizeAdoption", Rehydration);
        Assert.DoesNotContain("TryDeriveSupportedSideResize", Rehydration);
    }
}
