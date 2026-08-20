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
        var directPolyline = Resolver.IndexOf("if (selected is Polyline polyline)", StringComparison.Ordinal);
        var displayRead = Resolver.IndexOf("RoofDisplayStore.Read(selected)", StringComparison.Ordinal);

        Assert.True(directPolyline >= 0 && displayRead > directPolyline);
        Assert.Contains("Success(selectedId, selectedThroughDisplayChild: false)", Resolver);
        Assert.DoesNotContain("ExtendedDataHandle", DefinitionStore);
        Assert.DoesNotContain("OwnerReference", DefinitionStore);
    }

    [Fact]
    public void FullRoofGroup_IncludesStructuralDisplayAndAssemblyMembers()
    {
        Assert.Contains("ExpectedStructuralDisplayChildCount = 7", Group);
        Assert.Contains("RoofAssemblyGroupMemberCollector.TryCollect", Group);
        Assert.Contains("expected.SetEquals(actual)", Group);
        Assert.Contains("group.Append(addId)", Group);
        Assert.Contains("group.Remove(removeId)", Group);
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
            "public static bool TryOpenCanonicalGroup"));
    }

    [Fact]
    public void GeneratedRafterOwnerUsesCanonicalAsciiOwnerNotSoftPointer()
    {
        // The generated child no longer carries a 1005 soft pointer (Entity.XData is
        // replayed as one undo/redo mutation); legacy 1005 payloads stay readable.
        Assert.Contains("DxfCode.ExtendedDataHandle", GeneratedStore);
        Assert.Contains("cloneSafeOwnerReference", GeneratedStore);
        Assert.Contains(
            "data = data with { RoofOwnerReference = cloneSafeOwnerReference }",
            GeneratedStore);
        Assert.DoesNotContain("new TypedValue(DxfOwnerHandleCode, ownerReference)", GeneratedStore);
        Assert.Contains("var ownerReference = owner.Handle.ToString()", RafterWorkflow);
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner(", RafterWorkflow);
        Assert.Contains("RoofGeneratedRafterSetService.Materialize(", RafterWorkflow);
        Assert.DoesNotContain("RoofDisplayGroupService", RafterWorkflow + GeneratedStore);
        Assert.DoesNotContain(".Erase(", RafterWorkflow);
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
        Assert.Equal(3, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
    }

    private static string Read(string fileName) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", fileName);

    private static string Member(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);
}
