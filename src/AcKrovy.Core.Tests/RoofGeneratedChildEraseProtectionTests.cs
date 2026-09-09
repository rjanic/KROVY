using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedChildEraseProtectionTests
{
    [Theory]
    [InlineData(RoofEraseMappedKind.GeneratedTimber)]
    [InlineData(RoofEraseMappedKind.GeneratedAnnotation)]
    public void LockedErase_RestoresOnlyGeneratedProtectedKinds(RoofEraseMappedKind kind)
    {
        Assert.True(RoofDisplayErasePreCommandMapRules.ShouldRestoreLockedGeneratedChildErase(
            RoofEditState.Locked,
            kind,
            "ERASE"));
    }

    [Theory]
    [InlineData(RoofEraseMappedKind.Source)]
    [InlineData(RoofEraseMappedKind.Display)]
    [InlineData(RoofEraseMappedKind.Unknown)]
    public void LockedErase_GeneratedRuleDoesNotBroadenScope(RoofEraseMappedKind kind)
    {
        Assert.False(RoofDisplayErasePreCommandMapRules.ShouldRestoreLockedGeneratedChildErase(
            RoofEditState.Locked,
            kind,
            "ERASE"));
    }

    [Theory]
    [InlineData(RoofEraseMappedKind.GeneratedTimber)]
    [InlineData(RoofEraseMappedKind.GeneratedAnnotation)]
    public void UnlockedErase_PreservesExistingDeleteBehavior(RoofEraseMappedKind kind)
    {
        Assert.False(RoofDisplayErasePreCommandMapRules.ShouldRestoreLockedGeneratedChildErase(
            RoofEditState.Unlocked,
            kind,
            "ERASE"));
    }

    [Theory]
    [InlineData("U")]
    [InlineData("UNDO")]
    [InlineData("REDO")]
    [InlineData("MREDO")]
    public void UndoRedo_NeverRestoresGeneratedChildren(string command)
    {
        Assert.False(RoofDisplayErasePreCommandMapRules.ShouldRestoreLockedGeneratedChildErase(
            RoofEditState.Locked,
            RoofEraseMappedKind.GeneratedTimber,
            command));
        Assert.False(RoofDisplayErasePreCommandMapRules.ShouldRestoreLockedGeneratedChildErase(
            RoofEditState.Locked,
            RoofEraseMappedKind.GeneratedAnnotation,
            command));
    }

    [Theory]
    [InlineData("MOVE")]
    [InlineData("ROTATE")]
    [InlineData("STRETCH")]
    [InlineData("GRIP_STRETCH")]
    [InlineData("SCALE")]
    public void NonEraseCommands_DoNotEnterExactUnErasePath(string command)
    {
        Assert.False(RoofDisplayErasePreCommandMapRules.ShouldRestoreLockedGeneratedChildErase(
            RoofEditState.Locked,
            RoofEraseMappedKind.GeneratedTimber,
            command));
    }

    [Theory]
    [InlineData(RoofKind.SimpleGable)]
    [InlineData(RoofKind.Monopitch)]
    [InlineData(RoofKind.AsymmetricGable)]
    [InlineData(RoofKind.Hip)]
    public void GeneratedEraseProtection_IsRoofKindAgnostic(RoofKind kind)
    {
        _ = kind;
        Assert.True(RoofDisplayErasePreCommandMapRules.ShouldRestoreLockedGeneratedChildErase(
            RoofEditState.Locked,
            RoofEraseMappedKind.GeneratedTimber,
            "ERASE"));
    }
}

public sealed class RoofGeneratedChildEraseSourceContractTests
{
    private static readonly string Map = Read("RoofDisplayErasePreCommandMapService.cs");
    private static readonly string Live = Read("LiveGeometrySynchronizationService.cs");
    private static readonly string Resize = Read("RoofLiveResizeService.cs");
    private static readonly string Diagnostics = Read("RoofGeneratedMemberManualEditDiag.cs");
    private static readonly string Group = Read("RoofDisplayGroupService.cs");

    [Fact]
    public void PreCommandMap_CapturesExactIdentityAndGeneratedPayload()
    {
        Assert.Contains("ObjectId EntityId", Map);
        Assert.Contains("string EntityHandle", Map);
        Assert.Contains("string OwnerHandle", Map);
        Assert.Contains("RoofGeneratedTimberData? GeneratedData", Map);
        Assert.Contains("TimberElementData? TimberData", Map);
        Assert.Contains("ElementDataStore.TryRead(line, transaction", Map);
    }

    [Fact]
    public void PreCommandMap_MapsOnlyGeneratedTimberNotAttachedManual()
    {
        Assert.Contains("RoofGeneratedTimberStore.Read(line)", Map);
        Assert.DoesNotContain("RoofAttachedManualTimberStore.Read(line)", Map);
    }

    [Fact]
    public void AnnotationOwnership_UsesSourceHandleWithoutGeometryLookup()
    {
        Assert.Contains("TryResolveAnnotationSourceHandle", Map);
        Assert.Contains("GeneratedSourceHandle", Map);
        Assert.DoesNotContain("GeometricExtents", Map);
        Assert.DoesNotContain("GetBoundingBox", Map);
        Assert.DoesNotContain("DistanceTo", Map);
    }

    [Fact]
    public void ObjectErased_OnlyQueuesMappedIdentity()
    {
        var erased = Member(Live, "private void ObjectErased", "private void CommandWillStart");
        Assert.Contains("TryResolve(handle", erased);
        Assert.Contains("_erasedSourceHandles.TryAdd(handle)", erased);
        Assert.DoesNotContain("Erase(false)", erased);
        Assert.DoesNotContain("StartTransaction", erased);
    }

    [Fact]
    public void ExactRepair_UnErasesTimberAndAnnotationDBObject()
    {
        Assert.Contains("TryGetObjectAllowErased<Entity>", Resize);
        Assert.Contains("TryUnEraseGeneratedTimber", Resize);
        Assert.Contains("TryUnEraseGeneratedAnnotation", Resize);
        Assert.Contains("entity.Erase(false)", Resize);
    }

    [Fact]
    public void ExactRepair_VerifiesObjectAndPersistentIdentity()
    {
        Assert.Contains("entity.ObjectId == entry.EntityId", Resize);
        Assert.Contains("entry.EntityHandle", Resize);
        Assert.Contains("generated == entry.GeneratedData", Resize);
        Assert.Contains("timberData == entry.TimberData", Resize);
        Assert.Contains("entry.GeneratedSourceHandle", Resize);
    }

    [Fact]
    public void ExactRepair_PreservesLogicalIdentityWithoutRegeneration()
    {
        var repair = Member(
            Resize,
            "private static bool ApplyGeneratedChildEraseTampers",
            "private static bool ApplyDisplayTampers");
        Assert.DoesNotContain("RoofGeneratedRafterSetService", repair);
        Assert.DoesNotContain("TryRecoverGeneratedMembersOnly", repair);
        Assert.DoesNotContain("RoofManualOverrideSet", repair);
        Assert.DoesNotContain("RoofDefinitionStore.Write", repair);
    }

    [Fact]
    public void CommandPriority_IsSourceThenDisplayThenGeneratedThenFallback()
    {
        var process = Member(Resize, "public static IReadOnlyCollection<ObjectId> Process", "public static bool TryBeginGroupedUndo");
        var source = process.IndexOf("ApplySourceEraseTampers(", StringComparison.Ordinal);
        var display = process.IndexOf("ApplyDisplayTampers(", StringComparison.Ordinal);
        var generated = process.IndexOf("ApplyGeneratedChildEraseTampers(", StringComparison.Ordinal);
        var fallback = process.IndexOf("RoofGeneratedMemberManualEditService.ProcessOwners(", StringComparison.Ordinal);
        Assert.True(source >= 0 && display > source && generated > display && fallback > generated);
    }

    [Fact]
    public void MixedErase_DeduplicatesByOwnerAndObjectId()
    {
        Assert.Contains("var owners = new HashSet<ObjectId>(timberOwnerIds)", Resize);
        Assert.Contains("owners.UnionWith(annotationOwnerIds)", Resize);
        Assert.Contains("var entries = new Dictionary<ObjectId, MappedEntity>()", Map);
        Assert.Contains("entries[mapped.EntityId] = mapped", Map);
    }

    [Fact]
    public void Repair_RestoresTimberBeforeAnnotationsAndSyncsGroupOncePerOwner()
    {
        var repair = Member(
            Resize,
            "private static bool ApplyGeneratedChildEraseTampers",
            "private static bool ApplyDisplayTampers");
        var timber = repair.IndexOf("foreach (var entry in timberEntries)", StringComparison.Ordinal);
        var annotation = repair.IndexOf("foreach (var entry in annotationEntries)", StringComparison.Ordinal);
        var group = repair.IndexOf("RoofAssemblyGroupSyncService.TrySyncForOwner", StringComparison.Ordinal);
        Assert.True(timber >= 0 && annotation > timber && group > annotation);
    }

    [Fact]
    public void GroupSync_IsCanonicalDiffBasedAndLeavesPickstyleAlone()
    {
        Assert.Contains("var expected = new HashSet<ObjectId>(memberIds)", Group);
        Assert.Contains("DissociateOwnerFromForeignGroups", Group);
        Assert.DoesNotContain("PICKSTYLE", Group);
        Assert.DoesNotContain("SetSystemVariable", Group);
    }

    [Fact]
    public void FailedExactRepair_DoesNotCommitOrFallThroughToRegeneration()
    {
        Assert.Contains("identityPreserved && canonical", Resize);
        Assert.Contains("failed-exact-unerase", Resize);
        Assert.Contains("generatedTimberEraseOwners.Contains(ownerId)", Resize);
        Assert.Contains("generatedAnnotationEraseOwners.Contains(ownerId)", Resize);
    }

    [Fact]
    public void CancelledAndFailedCommands_ClearPreCommandMapAndQueues()
    {
        Assert.Contains("ClearPendingLiveGeometryState();", Live);
        Assert.Contains("RoofDisplayErasePreCommandMapService.Clear(\"CommandCancelled\")", Live);
        Assert.Contains("RoofDisplayErasePreCommandMapService.Clear(\"CommandFailed\")", Live);
    }

    [Fact]
    public void InternalRecovery_IsInsideExistingEventSuppressionScope()
    {
        Assert.Contains("using (_erasedSourceHandles.Suppress())", Live);
        Assert.Contains("RoofLiveResizeService.Process(", Live);
    }

    [Fact]
    public void Diagnostics_AreCompactAndClassified()
    {
        Assert.Contains("ROOF_GENERATED_ERASE_TAMPER", Diagnostics);
        Assert.Contains("LockedGeneratedEraseTamper", Resize);
        Assert.Contains("ROOF_GENERATED_ERASE_REPAIR", Diagnostics);
        Assert.Contains("ROOF_GENERATED_ANNOTATION_ERASE_TAMPER", Diagnostics);
        Assert.Contains("ROOF_GENERATED_ANNOTATION_ERASE_REPAIR", Diagnostics);
        Assert.Contains("Recovered|ok", Resize);
    }

    [Fact]
    public void ProductionPath_HasNoShapeOrFixtureHeuristics()
    {
        var repair = Member(
            Resize,
            "private static bool ApplyGeneratedChildEraseTampers",
            "private static bool ApplyDisplayTampers");
        Assert.DoesNotContain("RoofKind.Hip", repair);
        Assert.DoesNotContain("NumberOfVertices", repair);
        Assert.DoesNotContain("Rectangle", repair);
        Assert.DoesNotContain("StationCount ==", repair);
        Assert.DoesNotContain("Expected", repair);
    }

    private static string Read(string fileName)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure", fileName);
        return File.ReadAllText(path);
    }

    private static string Member(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, "missing start: " + start);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, "missing end: " + end);
        return source[startIndex..endIndex];
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AcKrovy.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
