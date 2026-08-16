using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGroupCopyOwnershipSourceContractTests
{
    private static readonly string DisplayStore = Read("RoofDisplayStore.cs");
    private static readonly string DefinitionStore = Read("RoofDefinitionStore.cs");
    private static readonly string Resolver = Read("RoofOwnerSelectionResolver.cs");
    private static readonly string Group = Read("RoofDisplayGroupService.cs");
    private static readonly string RafterWorkflow = Read("RoofRafterCommandWorkflow.cs");
    private static readonly string GeneratedStore = Read("RoofGeneratedTimberStore.cs");

    [Fact]
    public void DisplayOwnerUsesAutoCadCloneTranslatedSoftPointerHandle()
    {
        Assert.Contains("DxfCode.ExtendedDataHandle", DisplayStore);
        Assert.Contains("new TypedValue(DxfOwnerHandleCode, ownerReference)", DisplayStore);
        Assert.Contains("cloneSafeOwnerReference", DisplayStore);
        Assert.Contains("data = data with { OwnerReference = cloneSafeOwnerReference }", DisplayStore);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
    }

    [Fact]
    public void SourceOnlyCopyRemainsAValidIndependentSemanticOwner()
    {
        var directPolyline = Resolver.IndexOf("if (selected is Polyline)", StringComparison.Ordinal);
        var displayRead = Resolver.IndexOf("RoofDisplayStore.Read(selected)", StringComparison.Ordinal);

        Assert.True(directPolyline >= 0 && displayRead > directPolyline);
        Assert.Contains("Success(selectedId, selectedThroughDisplayChild: false)", Resolver);
        Assert.DoesNotContain("ExtendedDataHandle", DefinitionStore);
        Assert.DoesNotContain("OwnerReference", DefinitionStore);
    }

    [Fact]
    public void FullRoofGroupKeepsExactlyOneOwnerAndSevenDisplayChildren()
    {
        Assert.Contains("internal const int ExpectedMemberCount = 8", Group);
        Assert.Contains("var expected = new HashSet<ObjectId>(childIds) { ownerId }", Group);
        Assert.Contains("actual.Length == ExpectedMemberCount", Group);
        Assert.Contains("expected.SetEquals(actual)", Group);
        Assert.Contains("group.Append(ownerId)", Group);
        Assert.Contains("group.Append(childId)", Group);
    }

    [Fact]
    public void CopiedDisplayResolutionUsesTranslatedOwnerAndNeverWritesOriginalMetadata()
    {
        Assert.Contains("display.Data.OwnerReference", Resolver);
        Assert.Contains("RoofDisplayGroupService.TryResolveLegacyCopiedOwner", Resolver);
        Assert.Contains("database.GetObjectId(false, new Handle(handleValue), 0)", Resolver);
        Assert.DoesNotContain("OpenMode.ForWrite", Resolver);
        Assert.DoesNotContain("UpgradeOpen", Resolver);
        Assert.DoesNotContain("RoofDisplayStore.Write", Resolver);
        Assert.DoesNotContain("transaction.Commit", Resolver);
    }

    [Fact]
    public void LegacyCopiedGroupFallbackRequiresTheCompleteUnambiguousRoofTopology()
    {
        Assert.Contains("public static bool TryResolveLegacyCopiedOwner", Group);
        Assert.Contains("members.Length != ExpectedMemberCount", Group);
        Assert.Contains("members.Distinct().Count() != ExpectedMemberCount", Group);
        Assert.Contains("RoofDefinitionStore.Read(member).Data is null", Group);
        Assert.Contains("member is not Line", Group);
        Assert.Contains("displayRoles.Add(display.Data.Role)", Group);
        Assert.Contains("displayRoles.Count != ExpectedMemberCount - 1", Group);
        Assert.Contains("generationSignature", Group);
        Assert.Contains("candidates.Count != 1", Group);
        Assert.DoesNotContain("OpenMode.ForWrite", Member(
            Group,
            "public static bool TryResolveLegacyCopiedOwner",
            "private static string BuildGroupName"));
    }

    [Fact]
    public void CopiedRoofStartsWithoutRaftersAndCanCreateAnIndependentSet()
    {
        Assert.Contains("var ownerReference = owner.Handle.ToString()", RafterWorkflow);
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner(", RafterWorkflow);
        Assert.Contains("generatedIds.Count", RafterWorkflow);
        Assert.Contains("expectedOwnerReference", RafterWorkflow);
        Assert.Contains("RoofGeneratedTimberStore.Write(", RafterWorkflow);
        Assert.DoesNotContain("DxfCode.ExtendedDataHandle", GeneratedStore);
        Assert.DoesNotContain("RoofDisplayGroupService", RafterWorkflow + GeneratedStore);
        Assert.DoesNotContain(".Erase(", RafterWorkflow + GeneratedStore);
    }

    [Fact]
    public void CopyOwnershipFixAddsNoCloneReactorOrSchemaChange()
    {
        var source = DisplayStore + Resolver + Group + RafterWorkflow + GeneratedStore;

        Assert.DoesNotContain("BeginDeepClone", source);
        Assert.DoesNotContain("EndDeepClone", source);
        Assert.DoesNotContain("IdMapping", source);
        Assert.DoesNotContain("ObjectModified", source);
        Assert.DoesNotContain("CommandEnded", source);
        Assert.Equal(2, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
    }

    private static string Read(string fileName) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", fileName);

    private static string Member(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);
}
