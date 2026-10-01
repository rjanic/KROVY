using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Checks actual adapter routing, separately from native HOST event evidence.</summary>
public sealed class RoofSupportedNativeMemberRoutingTests
{
    [Theory]
    [InlineData("COPY")]
    [InlineData("MIRROR")]
    public void NativeCloneCommands_DoNotRunClipboardAdoptionAgain(string command)
    {
        var decision = RoofClipboardPasteOwnershipRules.Classify(command,
            RoofClipboardPasteProvenanceKind.KnownSameDocument, 1, false);
        Assert.False(decision.ShouldProcessIndividualTimber);
        Assert.Equal(RoofClipboardPasteOwnershipClassification.NotClipboardPaste, decision.Classification);
    }

    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }

    [Theory]
    [InlineData("COPY")]
    [InlineData("MIRROR")]
    public void CloneCommands_ClaimNativeResultsBeforeGenericRecovery(string command)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand(command));
        var source = Read("LiveGeometrySynchronizationService.cs");
        // Normalize Windows newlines for the routing guard.
        source = source.Replace("\r\n", "\n");
        var whole = source.IndexOf("RoofWholeRoofCopyRebindService.Process", StringComparison.Ordinal);
        var route = source.IndexOf("if (nativeCopy || nativeMirror)\n", whole, StringComparison.Ordinal);
        var semantic = source.IndexOf("ProcessNativeMemberClones(globalCommandName", StringComparison.Ordinal);
        var fallback = source.IndexOf("RoofLiveResizeService.Process", StringComparison.Ordinal);
        Assert.True(whole >= 0 && route > whole && semantic > route && fallback > semantic);
        Assert.Contains("mirrorModifiedTimberIds ?? Array.Empty<ObjectId>()", source[route..fallback]);
        Assert.DoesNotContain("ProcessNativeMemberClones(globalCommandName", source[fallback..]);
        Assert.Contains("handledIds?.UnionWith(claimedModifiedIds)", source);
        Assert.Contains("handledIds?.UnionWith(unchangedCloneSources)", source);
        Assert.Contains("erasedSourceHandles = erasedSourceHandles.Where(handle => !handledSourceHandles.Contains(handle))", source[route..fallback]);
        Assert.Contains("claimedModifiedIds.Add(pair.Value)", source);
        Assert.Contains("snapshot.PreExistingPhysical.Where(id => id.IsErased)", source);
        Assert.Contains("RoofMirrorCloneDetachService.Process(_document, command, ownedNativeAdded.ToArray()", source);
        Assert.Contains("ownedModifiedIds.ToArray()", source);
        Assert.Contains("!snapshot.IsSourceChanged(owner)", source);
        Assert.Contains("snapshot.IsPreExisting(id)", source);
    }

    [Theory]
    [InlineData("BREAK")]
    [InlineData("BREAKATPOINT")]
    [InlineData("_.breakatpoint")]
    public void BreakOfAttachedManual_IsClassifiedIntoExistingSemanticSplitRoute(string command)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSplitCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(command));
        var source = Read("RoofLiveResizeService.cs");
        var start = source.IndexOf("private static void ClassifyModifiedGeneratedChildren", StringComparison.Ordinal);
        var classification = source[start..];
        var attached = classification.IndexOf("RoofAttachedManualTimberStore.FindByOwner", StringComparison.Ordinal);
        var flag = classification.IndexOf("generatedTimberModified = true", StringComparison.Ordinal);
        Assert.True(attached >= 0 && flag > attached);
        Assert.Contains("RoofGeneratedMemberManualEditService.ProcessOwners", source);
        var semantic = Read("RoofGeneratedMemberManualEditService.cs");
        Assert.True(semantic.IndexOf("TryPromoteSplitFragments(", StringComparison.Ordinal) <
            semantic.IndexOf("private static bool TryAttachManualSplitFragment", StringComparison.Ordinal));
        Assert.Contains("RoofAttachedManualLifecycleService.CreateAnchoredData(", semantic);
        Assert.Contains("? RoofAttachedManualIdentityRules.Resolve(survivingAttached) : null", semantic);
        Assert.Contains("!string.Equals(timber.EntityHandle, candidateAttached.ChildIdentity", semantic);
        Assert.Contains("RoofGeneratedMemberOverrideMath.GeometryEquals(unchangedBefore, fragment)", semantic);
        Assert.Contains("TryReconcileSemanticMembersInTransaction", semantic);
    }
}
