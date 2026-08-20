using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOwnerSelectionResolverSourceContractTests
{
    private static readonly string Resolver = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofOwnerSelectionResolver.cs");
    private static readonly string Workflow = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofCommandWorkflow.cs");

    [Fact]
    public void DirectLightweightPolylineResolvesToItself()
    {
        Assert.Contains("if (selected is Polyline polyline)", Resolver);
        Assert.Contains("Success(selectedId, selectedThroughDisplayChild: false)", Resolver);
    }

    [Fact]
    public void EveryValidDisplayRoleUsesTheSameMetadataOwnerPath()
    {
        Assert.Contains("var display = RoofDisplayStore.Read(selected)", Resolver);
        Assert.Contains("display.Data.OwnerReference", Resolver);
        Assert.Contains("Success(ownerId, selectedThroughDisplayChild: true)", Resolver);
        Assert.DoesNotContain("RoofDisplayEdgeRole.Ridge", Resolver);
        Assert.DoesNotContain("RoofDisplayEdgeRole.Eave", Resolver);
        Assert.DoesNotContain("RoofDisplayEdgeRole.Gable", Resolver);
    }

    [Fact]
    public void UnrelatedAndMalformedOrFutureDisplayDataAreRejectedSafely()
    {
        Assert.Contains("RoofOwnerSelectionError.UnrelatedObject", Resolver);
        Assert.Contains("RoofOwnerSelectionError.MalformedDisplayMetadata", Resolver);
        Assert.Contains("RoofOwnerSelectionError.UnsupportedFutureDisplaySchema", Resolver);
        Assert.Contains("Command_Roof_SelectionInvalid", Workflow);
        Assert.Contains("Command_Roof_SelectionInvalidDisplay", Workflow);
        Assert.Contains("Command_Roof_SelectionFutureDisplay", Workflow);
        Assert.Contains("TryResolveUnlockIndicatorOwner", Resolver);
        Assert.DoesNotContain("KROV_ROOF_UNLOCK_ICON", Resolver);
    }

    [Fact]
    public void InvalidMissingErasedAndNonPolylineOwnersFailWithoutCleanup()
    {
        Assert.Contains("long.TryParse", Resolver);
        Assert.Contains("RoofOwnerSelectionError.InvalidOwnerReference", Resolver);
        Assert.Contains("RoofOwnerSelectionError.MissingOwner", Resolver);
        Assert.Contains("owner is not Polyline", Resolver);
        Assert.Contains("RoofOwnerSelectionError.OwnerIsNotPolyline", Resolver);
        Assert.Contains("Command_Roof_SelectionOrphan", Workflow);
        Assert.DoesNotContain("Erase", Resolver);
    }

    [Fact]
    public void ChildGeometryIsNeverUsedToResolveSemanticOwner()
    {
        Assert.DoesNotContain("StartPoint", Resolver);
        Assert.DoesNotContain("EndPoint", Resolver);
        Assert.DoesNotContain("Line line", Resolver);
        Assert.DoesNotContain("Distance", Resolver);
        Assert.DoesNotContain("SimpleGableRoofGeometry", Resolver);
    }

    [Fact]
    public void MovedOrCorruptChildCanStillResolveByMetadataBeforeDisplayValidation()
    {
        var selectionIndex = Workflow.IndexOf("RoofOwnerSelectionResolver.Resolve", StringComparison.Ordinal);
        var validationIndex = Workflow.IndexOf("RoofDisplayService.Inspect", StringComparison.Ordinal);
        Assert.True(selectionIndex >= 0 && validationIndex > selectionIndex);
        Assert.DoesNotContain("RoofDisplayValidator", Resolver);
    }

    [Fact]
    public void ResolutionPathIsStrictlyReadOnly()
    {
        Assert.Contains("OpenMode.ForRead", Resolver);
        Assert.DoesNotContain("OpenMode.ForWrite", Resolver);
        Assert.DoesNotContain("UpgradeOpen", Resolver);
        Assert.DoesNotContain("EnsureRegAppRegistered", Resolver);
        Assert.DoesNotContain("XData =", Resolver);
        Assert.DoesNotContain("Commit", Resolver);
        Assert.DoesNotContain("DocumentLock", Resolver);
    }

    [Fact]
    public void WorkflowAcceptsAnyEntityAndRoutesResolvedOwnerThroughExistingPath()
    {
        Assert.DoesNotContain("prompt.AddAllowedClass", Workflow);
        Assert.DoesNotContain("prompt.SetRejectMessage", Workflow);
        Assert.Contains("ownerId = resolution.OwnerId", Workflow);
        Assert.Contains("transaction.GetObject(ownerId, OpenMode.ForRead) is not Polyline polyline", Workflow);
        Assert.Contains("editor.SetImpliedSelection([ownerId])", Workflow);
    }
}
