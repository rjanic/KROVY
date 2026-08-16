using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOriginalCopyResizeParitySourceContractTests
{
    private static readonly string Persistence = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofDefinitionPersistence.cs");
    private static readonly string Resize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Rigid = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGroupGripRigidTransformService.cs");
    private static readonly string Live = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");

    [Fact]
    public void OrientationFlippedRestore_IsDocumentedInPersistence()
    {
        Assert.Contains("orientation-flipped", Persistence);
        Assert.Contains("TryResolveSourceTopologyForRestore", Persistence);
        Assert.Contains("MatchesOrientationFlippedRigid", Persistence);
        Assert.Contains("ExplainClassify", Persistence);
    }

    [Fact]
    public void HostKeepsExplainClassifyInPersistence_WithoutAutomaticResizeClassifySpam()
    {
        Assert.Contains("ExplainClassify", Persistence);
        Assert.DoesNotContain("AK_DEV_ROOF_RESIZE_CLASSIFY", Resize);
        Assert.DoesNotContain("AK_DEV_ROOF_RESIZE_APPLY", Resize);
        Assert.DoesNotContain("WriteClassifyDiag", Resize);
        Assert.Contains("RoofDefinitionPersistence.Classify", Resize);
        Assert.Contains("TryReplaceForSupportedResize", Resize);
    }

    [Fact]
    public void ClassicStretch_DoesNotRequireGripRigidPath()
    {
        Assert.Contains("IsGripStretchCommand", Resize + Live);
        var process = RoofUxSourceContractText.Member(
            Resize,
            "public static IReadOnlyCollection<ObjectId> Process",
            "public static bool TryBeginGroupedUndo");
        Assert.Contains("IsGripStretchCommand(globalCommandName)", process);
        Assert.DoesNotContain("IsUndoGroupingSourceCommand(globalCommandName))\r\n            {\r\n                displayTamperOwners = TryAcceptRigidGroupTransforms", process);
    }

    [Fact]
    public void NoNewReactorsOrDeepCloneHooks()
    {
        var source = Persistence + Resize + Rigid + Live;
        Assert.DoesNotContain("DatabaseReactor", source);
        Assert.DoesNotContain("ObjectOverrule", source);
        Assert.DoesNotContain("BeginDeepClone", source);
        Assert.DoesNotContain("EndDeepClone", source);
        Assert.DoesNotContain("IdMapping", source);
    }
}
