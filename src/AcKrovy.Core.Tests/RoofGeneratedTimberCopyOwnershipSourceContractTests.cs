using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedTimberCopyOwnershipSourceContractTests
{
    private static readonly string GeneratedStore = Read("RoofGeneratedTimberStore.cs");
    private static readonly string Replacement = Read("RoofGeneratedRafterSetService.cs");
    private static readonly string DisplayStore = Read("RoofDisplayStore.cs");
    private static readonly string Group = Read("RoofDisplayGroupService.cs");
    private static readonly string Resize = Read("RoofLiveResizeService.cs");
    private static readonly string Workflow = Read("RoofRafterCommandWorkflow.cs");
    private static readonly string OwnershipRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofGeneratedTimberOwnershipRules.cs");

    [Fact]
    public void SameDwgCopy_GeneratedUsesCanonicalAsciiOwnerNotSoftPointer()
    {
        // The generated child no longer carries a 1005 soft pointer: Entity.XData is
        // replayed as a single undo/redo mutation, so a failed 1005 replay would drop the
        // canonical identity. Same-DWG COPY rebinds via geometry; the display store still
        // uses the remappable soft pointer.
        Assert.Contains("DxfCode.ExtendedDataHandle", DisplayStore);
        Assert.Contains("cloneSafeOwnerReference", GeneratedStore);
        Assert.Contains(
            "data = data with { RoofOwnerReference = cloneSafeOwnerReference }",
            GeneratedStore);
        Assert.DoesNotContain("new TypedValue(DxfOwnerHandleCode, ownerReference)", GeneratedStore);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
    }

    [Fact]
    public void FindByOwner_UsesEffectiveOwnerFromReadIncludingRemappedHandle()
    {
        var discovery = Member(
            GeneratedStore,
            "public static IReadOnlyList<ObjectId> FindByOwner",
            "private static List<TypedValue> ReadForeignXData");
        Assert.Contains("var stored = Read(entity)", discovery);
        Assert.Contains("stored.Data.RoofOwnerReference", discovery);
        Assert.Contains("TryNormalizeOwnerReference(ownerReference", discovery);
        Assert.Contains("OpenMode.ForRead", discovery);
        Assert.DoesNotContain("OpenMode.ForWrite", discovery);
    }

    [Fact]
    public void Replacement_RequiresUniqueStationsBeforeErase()
    {
        Assert.Contains(
            "RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(",
            Replacement);
        Assert.Contains("SkippedAmbiguousRecipe", Replacement);
        var replace = Member(
            Replacement,
            "public static ReplacementOutcome TryReplaceForSupportedResize",
            "public static IReadOnlyDictionary<ObjectId, TimberElementData> Materialize");
        var eraseIndex = replace.IndexOf("EraseGeneratedSet(", StringComparison.Ordinal);
        var uniqueIndex = replace.IndexOf(
            "HasUniqueMemberStations(",
            StringComparison.Ordinal);
        Assert.True(uniqueIndex >= 0 && eraseIndex > uniqueIndex);
    }

    [Fact]
    public void OriginalAndCopiedSetsStayIndependentOnSupportedResize()
    {
        Assert.Contains("FindByOwner(", Replacement);
        Assert.Contains("owner.Handle.ToString()", Replacement + Workflow + Resize);
        Assert.Contains("TryReplaceForSupportedResize(", Resize);
        Assert.DoesNotContain("BeginDeepClone", GeneratedStore + Replacement + Resize);
        Assert.DoesNotContain("EndDeepClone", GeneratedStore + Replacement + Resize);
        Assert.DoesNotContain("IdMapping", GeneratedStore + Replacement);
    }

    [Fact]
    public void AnnotationsEraseOnlyMatchedGeneratedSourceHandles()
    {
        Assert.Contains("ElementLabelService.DeleteForSourceHandle(", Replacement);
        Assert.Contains("SlopeAnnotationService.DeleteForSourceHandle(", Replacement);
        Assert.Contains("PostFootprintPerpendicularAnnotationService.DeleteForSourceHandle(", Replacement);
        Assert.Contains("entity.Erase()", Replacement);
        var eraseIndex = Replacement.IndexOf(
            "private static void EraseGeneratedSet",
            StringComparison.Ordinal);
        Assert.True(eraseIndex >= 0);
        var erase = Replacement[eraseIndex..];
        Assert.DoesNotContain("FindByOwner(", erase);
    }

    [Fact]
    public void GroupMembershipRemainsSourcePlusSevenDisplayLines()
    {
        Assert.Contains("internal const int ExpectedMemberCount = 8", Group);
        Assert.DoesNotContain("EnsureGroup(", Replacement);
        Assert.DoesNotContain("RoofDisplayGroupService", Replacement + GeneratedStore);
        Assert.DoesNotContain("RoofGeneratedTimber", Group);
    }

    [Fact]
    public void CrossDwgAndWblockStayWithoutGenericCloneArchitecture()
    {
        var source = GeneratedStore + Replacement + DisplayStore + Group + OwnershipRules;
        Assert.DoesNotContain("BeginDeepClone", source);
        Assert.DoesNotContain("EndDeepClone", source);
        Assert.DoesNotContain("DatabaseReactor", source);
        Assert.DoesNotContain("ObjectOverrule", source);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Equal(3, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
    }

    [Fact]
    public void AkRoofRaftersStillDiscoversCurrentSetByOwnerReference()
    {
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner(", Workflow);
        Assert.Contains("expectedOwnerReference", Workflow);
        Assert.Contains("RoofGeneratedRafterSetService.IsGeneratedSetStale(", Workflow);
        Assert.Contains("Command_RoofRafters_ReplacementDeferred", Workflow);
    }

    private static string Read(string fileName) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", fileName);

    private static string Member(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);
}
