using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedCopySplitStretchSourceContractTests
{
    private static readonly string Manual = Read("RoofGeneratedMemberManualEditService.cs");
    private static readonly string Diag = Read("RoofGeneratedMemberManualEditDiag.cs");
    private static readonly string Rehydration = Read("RoofGeneratedRafterCopyOwnershipRehydrationService.cs");
    private static readonly string Live = Read("LiveGeometrySynchronizationService.cs");
    private static readonly string GeneratedStore = Read("RoofGeneratedTimberStore.cs");
    private static readonly string Recovery = Read("RoofUnsupportedStretchRecoveryService.cs");
    private static readonly string CommandRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofGeneratedMemberEditCommandRules.cs");
    private static readonly string OrphanRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofGeneratedRafterCopyOrphanRules.cs");
    private static readonly string SplitRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofGeneratedMemberSplitRules.cs");

    [Fact]
    public void CopyDetach_UsesAppendedCloneContext_AndClearsGeneratedOwnershipOnly()
    {
        Assert.Contains("FindAllStandaloneDetachMemberKeys(", Rehydration);
        Assert.Contains("FindAppendedCloneDetachHandles(", Rehydration);
        Assert.Contains("RoofGeneratedCopyPreCommandSnapshotService", Live);
        Assert.Contains("FindDuplicateStationDetachMemberKeys(", OrphanRules);
        Assert.Contains("CollectAppendedMemberKeys(appendedTimberIds)", Rehydration);
        Assert.Contains("RoofGeneratedTimberStore.TryClear(", Rehydration);
        Assert.Contains("detach-to-attached-manual", Rehydration);
        Assert.Contains("ROOF_GENERATED_COPY", Diag);
        Assert.Contains("ROOF_COPY_TRACE", Read("RoofGeneratedCopyLifecycleDiag.cs"));
        Assert.Contains("appendedTimberIds", Live);
        Assert.DoesNotContain("BeginDeepClone", Rehydration + Live + OrphanRules);
        Assert.DoesNotContain("AK_ROOF_ATTACH", Manual + Rehydration);
        Assert.DoesNotContain("AK_ROOF_DETACH", Manual + Rehydration);
    }

    [Fact]
    public void Split_PromotesAppendedFragment_AndKeepsSnapshotHandleGenerated()
    {
        Assert.Contains("IsSplitCommand", CommandRules);
        Assert.Contains("BREAK", CommandRules);
        Assert.Contains("TryPromoteSplitFragments", Manual);
        Assert.Contains("RoofGeneratedMemberSplitIdentityRules", Manual);
        Assert.Contains("TryFinalizeStandaloneFragments", Manual);
        Assert.Contains("EnsureForCreatedElements", Manual);
        Assert.Contains("ROOF_GENERATED_SPLIT", Diag);
        Assert.Contains("IsCollinearFragment", SplitRules + Manual);
        Assert.Contains("TryEraseUnsnapshotGeneratedDuplicates", Recovery);
        Assert.Contains("_appendedTimberIds.TryAdd(entity.ObjectId)", Live);
        Assert.Contains("_modifiedIds.TryAdd(entity.ObjectId)", Live);
        Assert.DoesNotContain("new Timer", Manual + Recovery);
        Assert.DoesNotContain("DatabaseReactor", Manual);
    }

    [Fact]
    public void ClassicStretch_ReusesManualEditClassifier_WhenUnlocked()
    {
        Assert.Contains("IsClassicStretch(globalCommandName)", Manual);
        Assert.Contains("isStretch || isBreak", Manual);
        Assert.Contains("UnrepresentableStretch", Manual);
        Assert.Contains("ROOF_MANUAL_EDIT_ACCEPT", Diag);
        Assert.Contains("ROOF_MANUAL_EDIT_REJECT", Diag);
        Assert.DoesNotContain("StretchOverride", Manual + CommandRules);
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("STRETCH"));
        Assert.Contains("classic-stretch-locked", Manual);
        Assert.DoesNotContain("classic-stretch-unsupported", Manual);
    }

    [Fact]
    public void SchemasAndCopyInitRemainUnchanged()
    {
        Assert.Equal(3, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
        Assert.DoesNotContain("RoofDefinitionStore.Write", Rehydration);
        Assert.DoesNotContain("ManualOverrides", Rehydration);
    }

    private static string Read(string fileName) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", fileName);
}
