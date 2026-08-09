using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class OwnedAnnotationSelectionSourceContractTests
{
    private static readonly string ServiceSource = File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "src",
        "AcKrovy.AutoCAD",
        "Infrastructure",
        "AutoCadOwnedAnnotationSelectionService.cs"));

    [Fact]
    public void SelectionService_ExistsAsReadOnlyInfrastructure()
    {
        Assert.Contains(
            "internal static class AutoCadOwnedAnnotationSelectionService",
            ServiceSource);
        Assert.Contains(
            "StartOpenCloseTransaction()",
            ServiceSource);
        Assert.Contains("OpenMode.ForRead", ServiceSource);
        Assert.DoesNotContain("OpenMode.ForWrite", ServiceSource);
        Assert.DoesNotContain("UpgradeOpen(", ServiceSource);
        Assert.DoesNotContain("ElementLabelStore.Write(", ServiceSource);
        Assert.DoesNotContain("ElementDataStore.Write(", ServiceSource);
        Assert.DoesNotContain("TransformBy(", ServiceSource);
        Assert.DoesNotContain("AK_LABELS", ServiceSource);
        Assert.DoesNotContain("UpdateLabelsForChangedEntities(", ServiceSource);
        Assert.DoesNotContain("ElementLabelService.", ServiceSource);
    }

    [Fact]
    public void SelectionService_UsesCoreClassificationAndSupportedKinds()
    {
        Assert.Contains("TimberOwnedAnnotationSelectionRules.Evaluate(", ServiceSource);
        Assert.Contains(
            "TimberOwnedAnnotationRepresentationKind.PlainItemOnly",
            ServiceSource);
        Assert.Contains(
            "TimberOwnedAnnotationRepresentationKind.DimensionsOnly",
            ServiceSource);
        Assert.Contains(
            "TimberOwnedAnnotationRepresentationKind.FramedItemOnly",
            ServiceSource);
        Assert.Contains(
            "TimberOwnedAnnotationRepresentationKind.CombinedPlain",
            ServiceSource);
        Assert.Contains(
            "TimberOwnedAnnotationRepresentationKind.R3Combined",
            ServiceSource);
        Assert.Contains("ContentType.MTextContent", ServiceSource);
        Assert.Contains("ContentType.BlockContent", ServiceSource);
    }

    [Fact]
    public void SelectionService_SkipsUnrelatedTimberAndAuxiliaryEntities()
    {
        Assert.Contains("TimberSourceEntity", ServiceSource);
        Assert.Contains("AuxiliaryAnnotation", ServiceSource);
        Assert.Contains("NoLabelMetadata", ServiceSource);
        Assert.Contains("IsSupportedTimberGeometry(", ServiceSource);
        Assert.Contains("SlopeArrowStore.TryRead(", ServiceSource);
        Assert.Contains("SlopeAngleTextStore.TryRead(", ServiceSource);
        Assert.Contains("PostFootprintPerpendicularAnnotationStore.TryRead(", ServiceSource);
    }

    [Fact]
    public void SelectionService_ExpandsCombinedPlainThroughSourceHandleLookup()
    {
        Assert.Contains("ReadOwnedProbesBySourceHandle(", ServiceSource);
        Assert.Contains("SourceHandle", ServiceSource);
        Assert.Contains("DeadOrInvalidSource", ServiceSource);
        Assert.Contains("TryResolveLiveSource(", ServiceSource);
    }

    [Fact]
    public void SelectionService_DoesNotIntroduceProductionCommandsOrRibbon()
    {
        Assert.DoesNotContain("CommandMethod", ServiceSource);
        Assert.DoesNotContain("AK_LABELROTATE", ServiceSource);
        Assert.DoesNotContain("Ribbon", ServiceSource);
    }

    [Fact]
    public void ElementLabelService_WasNotModifiedForStage2()
    {
        // Stage 2 must not alter production refresh / AK_LABELS behavior.
        // Guard: selection service must not call into ElementLabelService.
        Assert.DoesNotContain("ElementLabelService", ServiceSource);
    }

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

        throw new InvalidOperationException("Repository root was not found.");
    }
}
