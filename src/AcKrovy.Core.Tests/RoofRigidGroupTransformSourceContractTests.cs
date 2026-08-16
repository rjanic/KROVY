using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRigidGroupTransformSourceContractTests
{
    private static readonly string Live = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string Resize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Baseline = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGroupGripPreCommandBaselineService.cs");
    private static readonly string Rigid = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGroupGripRigidTransformService.cs");
    private static readonly string Snapshot = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGroupGripGeometrySnapshotService.cs");
    private static readonly string Adoption = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGroupGripResizeAdoptionService.cs");
    private static readonly string Rules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofRigidGroupTransformRules.cs");
    private static readonly string DisplayTamper = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core.Tests", "RoofDisplayTamperStretchSourceContractTests.cs");
    private static readonly string DisplayRebuild = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core.Tests", "RoofDisplayRebuildIdempotencySourceContractTests.cs");
    private static readonly string CopyOwnership = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core.Tests", "RoofGeneratedTimberCopyOwnershipSourceContractTests.cs");

    [Fact]
    public void PreCommandBaseline_CapturedAtCommandWillStartBeforeObjectModified()
    {
        Assert.Contains("CaptureFromImpliedSelection", Live + Baseline);
        Assert.Contains("SelectImplied", Baseline);
        Assert.Contains("Do NOT clear implied selection", Baseline);
        var willStart = RoofUxSourceContractText.Member(
            Live,
            "private void CommandWillStart",
            "private void CommandEnded");
        Assert.True(
            willStart.IndexOf("CaptureFromImpliedSelection", StringComparison.Ordinal) >= 0);
        Assert.True(
            willStart.IndexOf("BeginCommandScope", StringComparison.Ordinal) <
            willStart.IndexOf("CaptureFromImpliedSelection", StringComparison.Ordinal));
    }

    [Fact]
    public void FirstNativeMutation_ComparesAgainstPreCommandGeometry()
    {
        Assert.Contains("TryGetPreCommandDisplayByRole", Adoption + Baseline);
        Assert.Contains("TRUE pre-command baseline", Snapshot + Adoption);
        Assert.Contains("ClassifyTimingCase", Snapshot + Adoption);
        Assert.DoesNotContain("changedFromPreCommand", Snapshot);
        Assert.DoesNotContain("AK_DEV_ROOF_GRIP_SNAP", Snapshot);
    }

    [Fact]
    public void RigidGroupTransform_AcceptedBeforeDisplayTamperWithoutWpf()
    {
        Assert.Contains("TryAcceptRigidGroupTransforms", Resize);
        Assert.Contains("TryAcceptRigidGroupTransform", Rigid);
        Assert.Contains("SourceHandledOwnersThisCommand", Resize);
        Assert.Contains("TryClassifyTranslation", Rigid + Rules);
        var process = RoofUxSourceContractText.Member(
            Resize,
            "public static IReadOnlyCollection<ObjectId> Process",
            "public static bool TryBeginGroupedUndo");
        Assert.True(
            process.IndexOf("TryAcceptRigidGroupTransforms", StringComparison.Ordinal) <
            process.IndexOf("TryAdoptGroupGripResizes", StringComparison.Ordinal));
        Assert.True(
            process.IndexOf("TryAcceptRigidGroupTransforms", StringComparison.Ordinal) <
            process.IndexOf("ApplyDisplayTampers", StringComparison.Ordinal));
        Assert.DoesNotContain(
            "Command_Roof_DisplayTamperNotificationTitle",
            Rigid);
    }

    [Fact]
    public void RigidAccept_KeepsSameSourceAndDoesNotRebuild()
    {
        Assert.Contains("OpenMode.ForRead", Rigid);
        Assert.DoesNotContain("RoofDisplayService.Rebuild", Rigid);
        Assert.DoesNotContain("WriteSourceVertices", Rigid);
        Assert.Contains("display-objectid-changed", Rigid);
        Assert.Contains("ExpectedMemberCount", Rigid + Baseline);
    }

    [Fact]
    public void CancelFailEnded_ClearBaseline_NextCommandCannotReuse()
    {
        Assert.Contains("Clear(\"CommandEnded\")", Live);
        Assert.Contains("Clear(\"CommandCancelled\")", Live);
        Assert.Contains("Clear(\"CommandFailed\")", Live);
        Assert.Contains("Clear(\"non-grip-command\")", Live);
        Assert.Contains("Clear(\"capture-start\")", Baseline);
    }

    [Fact]
    public void TimingCaseC_RequiresTruePreCommandBaseline()
    {
        Assert.Contains("timing-case-C-transient-only", Adoption);
        Assert.Contains("TryGetPreCommandDisplayByRole", Adoption);
        Assert.Contains("Without pre-command baseline we cannot prove transient-only", Adoption);
    }

    [Fact]
    public void NoNewReactorsOverrulesOrDeepCloneHooks()
    {
        var source = Live + Resize + Baseline + Rigid + Snapshot + Rules;
        Assert.DoesNotContain("DatabaseReactor", source);
        Assert.DoesNotContain("ObjectOverrule", source);
        Assert.DoesNotContain("BeginDeepClone", source);
        Assert.DoesNotContain("EndDeepClone", source);
        Assert.DoesNotContain("IdMapping", source);
        Assert.DoesNotContain("SendStringToExecute", source);
    }

    [Fact]
    public void DisplayLeakAndCopyOwnershipContractsStillPresent()
    {
        Assert.True(DisplayTamper.Length > 0);
        Assert.True(DisplayRebuild.Length > 0);
        Assert.True(CopyOwnership.Length > 0);
        Assert.Contains("DissociateOwnerFromForeignGroups", DisplayRebuild);
        Assert.Contains("TrySelectDisplayEraseMemberKeys", DisplayRebuild);
    }
}
