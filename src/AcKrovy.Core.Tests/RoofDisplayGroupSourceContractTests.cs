using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDisplayGroupSourceContractTests
{
    private static readonly string Group = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayGroupService.cs");
    private static readonly string Display = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayService.cs");
    private static readonly string Workflow = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofCommandWorkflow.cs");

    [Fact]
    public void GroupContainsExactlyOwnerAndSevenCurrentDisplayChildren()
    {
        Assert.Contains("internal const int ExpectedMemberCount = 8", Group);
        Assert.Contains("childIds.Count != ExpectedMemberCount - 1", Group);
        Assert.Contains("group.Append(ownerId)", Group);
        Assert.Contains("foreach (var childId in childIds)", Group);
        Assert.Contains("group.Append(childId)", Group);
        Assert.Contains("actual.Length == ExpectedMemberCount", Group);
        Assert.Contains("group.Selectable", Group);
    }

    [Fact]
    public void GroupNameIsDeterministicInternalAndOwnerHandleBased()
    {
        Assert.Contains("GroupNamePrefix = \"AK_ROOF_\"", Group);
        Assert.Contains("owner.Handle.ToString().ToUpperInvariant()", Group);
        Assert.DoesNotContain("UiStrings", Group);
        Assert.DoesNotContain("AppLanguageService", Group);
    }

    [Fact]
    public void CreationAndNormalizationOccurInsideExplicitDisplayWrite()
    {
        var rebuild = RoofUxSourceContractText.Member(
            Display,
            "public static bool Rebuild",
            "private static RoofPoint3D MapPoint");
        Assert.Contains("RoofDisplayGroupService.EnsureGroup", rebuild);
        Assert.DoesNotContain("StartTransaction", Group);
        Assert.DoesNotContain("transaction.Commit", Group + Display);
        Assert.Contains("ConfirmPersistence(editor)", Workflow);
        Assert.Contains("ConfirmDisplayPersistence(editor, isMissing)", Workflow);
        Assert.Contains("Command_Roof_GroupRepairPrompt", Workflow);
        Assert.Contains("CreateGroupFromExistingValidatedDisplay", Workflow + Group);
        Assert.Contains("TryRehydrateGroup", Workflow);
        Assert.DoesNotContain("TryRebuildDisplay(document, ownerId, out var groupFailureKey)", Workflow);
    }

    [Fact]
    public void RebuildClearsOldMembershipAndCannotAccumulateDuplicates()
    {
        Assert.Contains("group.Clear()", Group);
        Assert.Contains("childIds.Distinct().Count()", Group);
        Assert.Contains("newChildIds", Display);
        Assert.Contains("RoofDisplayGroupService.EnsureGroup", Display);
        Assert.Contains("CollectDisplayIdsToErase", Display);
        Assert.Contains("DissociateOwnerFromForeignGroups", Group);
    }

    [Fact]
    public void MissingOrDamagedGroupDoesNotInvalidateSemanticOrDisplayGeometry()
    {
        var inspect = RoofUxSourceContractText.Member(
            Display,
            "public static RoofDisplayInspection Inspect",
            "public static bool Rebuild");
        Assert.Contains("RoofDisplayValidator.Validate", inspect);
        Assert.Contains("RoofDisplayGroupService.Inspect", inspect);
        Assert.Contains("new RoofDisplayInspection(validation, group, childIds)", inspect);
        Assert.Contains("display.Lifecycle", Workflow);
        Assert.Contains("RoofDisplayLifecycleKind.GroupMissingRehydratable", Workflow);
        Assert.Contains("Command_Roof_GroupMissing", Workflow);
        Assert.Contains("TryRehydrateGroup", Workflow);
    }

    [Fact]
    public void GroupIsNeverGeometryInputAndOnlyValidatesLegacyCopyOwnershipTopology()
    {
        Assert.DoesNotContain("SimpleGableRoofGeometrySolver", Group);
        Assert.DoesNotContain("RoofDefinitionPersistence", Group);
        Assert.Contains("RoofDefinitionStore.Read(member).Data", Group);
        Assert.Contains("RoofDisplayStore.Read(member)", Group);
        Assert.DoesNotContain("RoofPolylineExtractor", Group);
        Assert.DoesNotContain("StartPoint", Group);
        Assert.DoesNotContain("EndPoint", Group);
    }

    [Fact]
    public void PickstyleRemainsUserControlledAndNoReactorWasAdded()
    {
        var source = Group + Display + Workflow;
        Assert.DoesNotContain("PICKSTYLE", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SetSystemVariable", source);
        Assert.DoesNotContain("DatabaseReactor", source);
        Assert.DoesNotContain("ObjectModified", source);
        Assert.DoesNotContain("CommandEnded", source);
    }
}
