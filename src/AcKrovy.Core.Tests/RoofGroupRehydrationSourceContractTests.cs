using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGroupRehydrationSourceContractTests
{
    private static readonly string Group = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayGroupService.cs");
    private static readonly string Display = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayService.cs");
    private static readonly string Store = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayStore.cs");
    private static readonly string Workflow = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofCommandWorkflow.cs");
    private static readonly string Resolver = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofOwnerSelectionResolver.cs");
    private static readonly string Classifier = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofDisplayLifecycleClassifier.cs");
    private static readonly string Association = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofTransferredDisplayAssociation.cs");

    [Fact]
    public void ValidSevenLineDisplayWithoutGroup_IsNotClassifiedAsMissingDisplay()
    {
        Assert.Contains("display.Lifecycle", Workflow);
        Assert.Contains("RoofDisplayLifecycleClassifier.Classify(Validation, Group.IsCurrent)", Display);
        Assert.Contains("RoofDisplayLifecycleKind.GroupMissingRehydratable", Workflow);
        Assert.Contains("Command_Roof_GroupMissing", Workflow);
        Assert.DoesNotContain("Command_Roof_DisplayMissing", RoofUxSourceContractText.Member(
            Workflow,
            "RoofDisplayLifecycleKind.GroupMissingRehydratable",
            "RoofDisplayLifecycleKind.UnsupportedFutureSchema"));
    }

    [Fact]
    public void RehydrationReusesExistingOwnerAndSevenDisplayObjectIds()
    {
        var rehydrate = RoofUxSourceContractText.Member(
            Workflow,
            "private static bool TryRehydrateGroup",
            "private static bool TryPromptRidgeDirection");
        Assert.Contains("inspection.ChildIds", rehydrate);
        Assert.Contains("owner.ObjectId", rehydrate);
        Assert.Contains("CreateGroupFromExistingValidatedDisplay", rehydrate);
        // Incremental group sync: the canonical group reuses the existing owner + 7
        // display members via the member collector, then appends only the diff.
        Assert.Contains("RoofAssemblyGroupMemberCollector.TryCollect", Group);
        Assert.Contains("foreach (var addId in toAdd)", Group);
        Assert.Contains("group.Append(addId)", Group);
        Assert.Contains("ExpectedMemberCount = 8", Group);
        Assert.DoesNotContain("new Line(", rehydrate);
        Assert.DoesNotContain("AppendEntity", rehydrate);
        Assert.DoesNotContain("child.Erase()", rehydrate);
        Assert.DoesNotContain("RoofDisplayStore.Write", rehydrate);
        Assert.DoesNotContain("ApplyDisplayLayer", rehydrate);
    }

    [Fact]
    public void YesCreatesOnlyTheMissingGroupFromValidatedMembers()
    {
        var rehydrate = RoofUxSourceContractText.Member(
            Workflow,
            "private static bool TryRehydrateGroup",
            "private static bool TryPromptRidgeDirection");
        Assert.Contains("CreateGroupFromExistingValidatedDisplay", rehydrate);
        Assert.Contains("transaction.Commit()", rehydrate);
        Assert.Equal(1, CountOccurrences(rehydrate, "transaction.Commit();"));
        Assert.Contains("AK_ROOF_", Group);
        Assert.Contains("owner.Handle.ToString().ToUpperInvariant()", Group);
        Assert.DoesNotContain("RoofGeneratedTimberStore", rehydrate + Group);
        Assert.DoesNotContain("ElementLabelService", rehydrate + Group);
        Assert.DoesNotContain("SlopeAngleTextService", rehydrate + Group);
        Assert.DoesNotContain("TimberElementStore", rehydrate + Group);
    }

    [Fact]
    public void NoOrCancelPerformsNoGroupWrite()
    {
        var repair = RoofUxSourceContractText.Member(
            Workflow,
            "RoofDisplayLifecycleKind.GroupMissingRehydratable",
            "RoofDisplayLifecycleKind.UnsupportedFutureSchema");
        Assert.Contains("if (!ConfirmYesNo(editor, \"Command_Roof_GroupRepairPrompt\"))", repair);
        Assert.Contains("return;", repair);
        Assert.True(repair.IndexOf("return;", StringComparison.Ordinal) <
                    repair.IndexOf("TryRehydrateGroup", StringComparison.Ordinal));
        Assert.DoesNotContain("Commit", repair[..repair.IndexOf("TryRehydrateGroup", StringComparison.Ordinal)]);
    }

    [Fact]
    public void ExistingValidGroupRemainsReadOnlyCurrent()
    {
        var current = RoofUxSourceContractText.Member(
            Workflow,
            "RoofDisplayLifecycleKind.Current",
            "RoofDisplayLifecycleKind.GroupMissingRehydratable");
        Assert.Contains("Command_Roof_DisplayCurrent", current);
        Assert.Contains("return;", current);
        Assert.DoesNotContain("TryRehydrateGroup", current);
        Assert.DoesNotContain("TryRebuildDisplay", current);
        Assert.DoesNotContain("Commit", current);
    }

    [Fact]
    public void TrueMissingDisplayKeepsCreateDisplayWorkflow()
    {
        Assert.Contains("RoofDisplayLifecycleKind.MissingDisplay", Workflow);
        Assert.Contains("Command_Roof_DisplayCreatePrompt", Workflow);
        Assert.Contains("TryRebuildDisplay(document, ownerId, out var displayFailureKey)", Workflow);
        Assert.Contains("new Line(", Display);
    }

    [Fact]
    public void StaleCorruptAndFutureDisplayAreNotGrouped()
    {
        Assert.Contains("RoofDisplayLifecycleKind.StaleDisplay", Classifier + Workflow);
        Assert.Contains("RoofDisplayLifecycleKind.UnsupportedFutureSchema", Classifier + Workflow);
        Assert.Contains("Command_Roof_DisplayStale", Workflow);
        Assert.Contains("Command_Roof_DisplayFutureSchema", Workflow);
        var rehydrate = RoofUxSourceContractText.Member(
            Workflow,
            "private static bool TryRehydrateGroup",
            "private static bool TryPromptRidgeDirection");
        Assert.Contains("if (!inspection.Validation.IsCurrent)", rehydrate);
        Assert.Contains("return false;", rehydrate);
        Assert.Contains("UnsupportedFutureSchema", Association);
        Assert.DoesNotContain("IsCurrent", RoofUxSourceContractText.Member(
            Association,
            "if (!validation.Issues.HasFlag(RoofDisplayValidationIssue.UnsupportedFutureSchema))",
            "private static IReadOnlyList<RoofDisplayObservation> RemapOwner"));
    }

    [Fact]
    public void FailedClipboardHandleRemapDoesNotInvalidateJsonDisplayMetadata()
    {
        Assert.Contains("values[index].TypeCode == DxfOwnerHandleCode", Store);
        Assert.Contains("TryNormalizeOwnerReference(", Store);
        Assert.Contains("cloneSafeOwnerReference = remappedOwnerReference", Store);
        Assert.Contains("continue;", RoofUxSourceContractText.Member(
            Store,
            "if (values[index].TypeCode == DxfOwnerHandleCode)",
            "return RoofDisplayStoreReadResult.Invalid("));
        Assert.Contains("ownerReferenceFromCloneHandle: cloneSafeOwnerReference is not null", Store);
    }

    [Fact]
    public void CopiedDisplayOwnerResolutionAndSourceOnlyCopyRemainProtected()
    {
        Assert.Contains("OwnerReferenceFromCloneHandle", Resolver + Store);
        Assert.Contains("TryResolveLegacyCopiedOwner", Resolver);
        Assert.Contains("TryResolveTransferredOwner", Resolver + Display);
        Assert.Contains("if (selected is Polyline polyline)", Resolver);
        Assert.Contains("Success(selectedId, selectedThroughDisplayChild: false)", Resolver);
        Assert.Contains("DxfCode.ExtendedDataHandle", Store);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
    }

    [Fact]
    public void RehydrationAddsNoReactorsHooksOrSchemaBump()
    {
        var source = Group + Display + Store + Workflow + Resolver + Classifier + Association;
        Assert.DoesNotContain("BeginDeepClone", source);
        Assert.DoesNotContain("EndDeepClone", source);
        Assert.DoesNotContain("IdMapping", source);
        Assert.DoesNotContain("DatabaseReactor", source);
        Assert.DoesNotContain("ObjectModified", source);
        Assert.DoesNotContain("CommandEnded", source);
        Assert.DoesNotContain("TransientNotificationService", RoofUxSourceContractText.Member(
            Workflow,
            "RoofDisplayLifecycleKind.GroupMissingRehydratable",
            "RoofDisplayLifecycleKind.UnsupportedFutureSchema"));
        Assert.Contains("TryGetExistingGroupDictionary", Group);
        Assert.DoesNotContain("GroupDictionaryId", RoofUxSourceContractText.Member(
            Group,
            "public static RoofDisplayGroupInspection Inspect",
            "public static void EnsureGroup"));
        Assert.Equal("0.23.0", ReadVersion());
    }

    private static int CountOccurrences(string value, string token) =>
        (value.Length - value.Replace(token, string.Empty, StringComparison.Ordinal).Length) /
        token.Length;

    private static string ReadVersion()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        var props = File.ReadAllText(Path.Combine(directory!.FullName, "Directory.Build.props"));
        const string prefix = "<AcKrovyVersion>";
        var start = props.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        var end = props.IndexOf("</AcKrovyVersion>", start, StringComparison.Ordinal);
        return props[start..end];
    }
}
