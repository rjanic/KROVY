using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDisplayEraseProtectionTests
{
    [Theory]
    [InlineData("MOVE", RoofEditState.Locked, true)]
    [InlineData("ROTATE", RoofEditState.Locked, true)]
    [InlineData("SCALE", RoofEditState.Locked, true)]
    [InlineData("STRETCH", RoofEditState.Locked, true)]
    [InlineData("GRIP_STRETCH", RoofEditState.Locked, true)]
    [InlineData("ERASE", RoofEditState.Locked, true)]
    [InlineData("MOVE", RoofEditState.Unlocked, false)]
    [InlineData("ERASE", RoofEditState.Unlocked, false)]
    public void DisplayTamperRepair_AllowsEraseOnLockedRoofs(
        string command,
        RoofEditState editState,
        bool expected)
    {
        Assert.Equal(expected, RoofDisplayTamperRepairRules.ShouldRepair(editState, command));
    }

    [Fact]
    public void UndoRedo_NeverRepairsDisplayTamper()
    {
        Assert.False(RoofDisplayTamperRepairRules.ShouldRepair(RoofEditState.Locked, "U"));
        Assert.False(RoofDisplayTamperRepairRules.ShouldRepair(RoofEditState.Locked, "UNDO"));
        Assert.False(RoofDisplayTamperRepairRules.ShouldRepair(RoofEditState.Locked, "REDO"));
        Assert.False(RoofDisplayTamperRepairRules.ShouldRepair(RoofEditState.Locked, "MREDO"));
    }

    [Theory]
    [InlineData(RoofEditState.Locked, "ERASE", true)]
    [InlineData(RoofEditState.Unlocked, "ERASE", false)]
    [InlineData(RoofEditState.Locked, "U", false)]
    [InlineData(RoofEditState.Locked, "UNDO", false)]
    [InlineData(RoofEditState.Locked, "REDO", false)]
    [InlineData(RoofEditState.Locked, "MREDO", false)]
    [InlineData(RoofEditState.Locked, "MOVE", false)]
    public void LockedSourceErase_RestoresOnlyForLockedErase(
        RoofEditState editState,
        string command,
        bool expected)
    {
        Assert.Equal(
            expected,
            RoofDisplayErasePreCommandMapRules.ShouldRestoreLockedSourceErase(editState, command));
    }

    [Theory]
    [InlineData(RoofKind.SimpleGable)]
    [InlineData(RoofKind.Monopitch)]
    [InlineData(RoofKind.AsymmetricGable)]
    [InlineData(RoofKind.Hip)]
    public void LockedSourceErase_IsKindAgnostic(RoofKind kind)
    {
        _ = kind;
        Assert.True(RoofDisplayErasePreCommandMapRules.ShouldRestoreLockedSourceErase(
            RoofEditState.Locked,
            "ERASE"));
    }

    [Fact]
    public void Snapshot_CapturesDisplayHandles()
    {
        var source = new RoofUnsupportedStretchSourceSnapshotData(
            "H1",
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10)],
            true,
            0d,
            0, 0, 1);
        var assembly = new RoofUnsupportedStretchAssemblySnapshotData(
            source,
            Array.Empty<RoofUnsupportedStretchTimberLineSnapshotData>(),
            Array.Empty<RoofUnsupportedStretchAnnotationSnapshotData>(),
            ["D1", "D2"]);

        Assert.True(RoofUnsupportedStretchRecoveryRules.IsEligibleAssembly(assembly));
        Assert.Equal(2, assembly.DisplayHandles!.Count);
        Assert.Contains("D1", assembly.DisplayHandles);
        Assert.Contains("D2", assembly.DisplayHandles);
    }

    [Fact]
    public void Snapshot_RejectsDuplicateDisplayHandles()
    {
        var source = new RoofUnsupportedStretchSourceSnapshotData(
            "H1",
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10)],
            true,
            0d,
            0, 0, 1);
        var assembly = new RoofUnsupportedStretchAssemblySnapshotData(
            source,
            Array.Empty<RoofUnsupportedStretchTimberLineSnapshotData>(),
            Array.Empty<RoofUnsupportedStretchAnnotationSnapshotData>(),
            ["D1", "D1"]);

        Assert.False(RoofUnsupportedStretchRecoveryRules.IsEligibleAssembly(assembly));
    }

    [Fact]
    public void Snapshot_RejectsEmptyDisplayHandles()
    {
        var source = new RoofUnsupportedStretchSourceSnapshotData(
            "H1",
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10)],
            true,
            0d,
            0, 0, 1);
        var assembly = new RoofUnsupportedStretchAssemblySnapshotData(
            source,
            Array.Empty<RoofUnsupportedStretchTimberLineSnapshotData>(),
            Array.Empty<RoofUnsupportedStretchAnnotationSnapshotData>(),
            [" ", "D1"]);

        Assert.False(RoofUnsupportedStretchRecoveryRules.IsEligibleAssembly(assembly));
    }

    [Theory]
    [InlineData(RoofEraseMappedKind.Display, false, RoofEditState.Locked, "ERASE", true)]
    [InlineData(RoofEraseMappedKind.Display, true, RoofEditState.Locked, "ERASE", false)]
    [InlineData(RoofEraseMappedKind.Source, false, RoofEditState.Locked, "ERASE", false)]
    [InlineData(RoofEraseMappedKind.GeneratedTimber, false, RoofEditState.Locked, "ERASE", false)]
    [InlineData(RoofEraseMappedKind.Display, false, RoofEditState.Unlocked, "ERASE", false)]
    [InlineData(RoofEraseMappedKind.Display, false, RoofEditState.Locked, "U", false)]
    public void PreCommandMap_ClassifiesLockedDisplayEraseTamper(
        RoofEraseMappedKind kind,
        bool sourceErased,
        RoofEditState editState,
        string command,
        bool expected)
    {
        Assert.Equal(
            expected,
            RoofDisplayErasePreCommandMapRules.ShouldClassifyLockedDisplayEraseTamper(
                kind,
                sourceErased,
                editState,
                command));
    }

    [Fact]
    public void UnlockedSourceErase_RemainsIntentionalDeletion()
    {
        Assert.True(RoofDisplayErasePreCommandMapRules.IsUnlockedIntentionalSourceDeletion(
            RoofEditState.Unlocked,
            RoofEraseMappedKind.Source));
        Assert.False(RoofDisplayErasePreCommandMapRules.IsUnlockedIntentionalSourceDeletion(
            RoofEditState.Locked,
            RoofEraseMappedKind.Source));
        Assert.False(RoofDisplayErasePreCommandMapRules.IsUnlockedIntentionalSourceDeletion(
            RoofEditState.Unlocked,
            RoofEraseMappedKind.Display));
    }
}

public sealed class RoofDisplayEraseObjectErasedSourceContractTests
{
    private static readonly string Live = Read("LiveGeometrySynchronizationService.cs");
    private static readonly string Resize = Read("RoofLiveResizeService.cs");
    private static readonly string Snapshot = Read("RoofUnsupportedStretchRecoverySnapshotService.cs");
    private static readonly string Map = Read("RoofDisplayErasePreCommandMapService.cs");
    private static readonly string Diag = Read("RoofGeneratedMemberManualEditDiag.cs");

    [Fact]
    public void ObjectErased_ResolvesThroughPreCommandMap()
    {
        var erased = Member(Live, "private void ObjectErased", "private void CommandWillStart");
        Assert.Contains("RoofDisplayErasePreCommandMapService.TryResolve(", erased);
        Assert.Contains("ROOF_OBJECT_ERASED", Diag);
        Assert.Contains("WriteObjectErased(", erased);
        Assert.Contains("_erasedSourceHandles.TryAdd(handle)", erased);
        Assert.DoesNotContain("RoofDisplayStore.Read(entity)", erased);
    }

    [Fact]
    public void CommandWillStart_CapturesEraseMapAndClearsOnBoundary()
    {
        Assert.Contains("RoofDisplayErasePreCommandMapService.CaptureForErase(", Live);
        Assert.Contains("RoofDisplayErasePreCommandMapService.Clear(\"CommandEnded\")", Live);
        Assert.Contains("RoofDisplayErasePreCommandMapService.Clear(\"CommandCancelled\")", Live);
        Assert.Contains("RoofDisplayErasePreCommandMapService.Clear(\"CommandFailed\")", Live);
        Assert.Contains("Read-only capture — do not Commit", Map);
        Assert.Contains("SourcePreCommandState", Map);
        Assert.Contains("EditState", Map);
    }

    [Fact]
    public void Snapshot_CapturesDisplayHandlesWithoutTimberGate()
    {
        Assert.Contains("Display children are independent of generated timber", Snapshot);
        Assert.DoesNotContain(
            "if (timberSourceHandles.Count > 0)\r\n        {\r\n            foreach (ObjectId id in modelSpace)",
            Snapshot);
        Assert.DoesNotContain(
            "if (timberSourceHandles.Count > 0)\n        {\n            foreach (ObjectId id in modelSpace)",
            Snapshot);
    }

    [Fact]
    public void Inspect_UsesPreCommandMapBeforeAssemblyDisplayHandles()
    {
        var inspect = Member(Resize, "private static InspectionPlan Inspect", "private static bool HasErasedGeneratedTimber");
        Assert.Contains("RoofDisplayErasePreCommandMapService.CollectDisplayOwners(", inspect);
        Assert.Contains("RoofDisplayErasePreCommandMapService.CollectLockedSourceEraseOwners(", inspect);
        Assert.Contains("RoofDisplayErasePreCommandMapService.IsOwnerSourceErased(", inspect);
        Assert.Contains("LockedDisplayEraseTamper", Resize);
        Assert.Contains("Recovered|ok", Resize);
    }

    [Fact]
    public void LockedSourceErase_UsesAllowErasedUnEraseSameObject()
    {
        Assert.Contains("ApplySourceEraseTampers(", Resize);
        Assert.Contains("TryUnEraseLockedSource(", Resize);
        Assert.Contains("TryGetObjectAllowErased<Polyline>(", Resize);
        Assert.Contains("owner.Erase(false)", Resize);
        Assert.Contains("LockedSourceEraseTamper", Resize);
        Assert.Contains("ROOF_SOURCE_ERASE_TAMPER", Diag);
        Assert.Contains("ROOF_SOURCE_ERASE_REPAIR", Diag);
        Assert.Contains("sameObjectId", Diag);
        Assert.Contains("sameHandle", Diag);
        Assert.DoesNotContain("SourceEraseWins", Resize);
    }

    [Fact]
    public void SourceErase_RunsBeforeDisplayTamper()
    {
        var process = Member(Resize, "public static IReadOnlyCollection<ObjectId> Process", "public static bool TryBeginGroupedUndo");
        var sourceIdx = process.IndexOf("ApplySourceEraseTampers(", StringComparison.Ordinal);
        var displayIdx = process.IndexOf("ApplyDisplayTampers(", StringComparison.Ordinal);
        Assert.True(sourceIdx > 0);
        Assert.True(displayIdx > sourceIdx);
    }

    [Fact]
    public void ObjectErased_IsWiredToDocumentDatabase()
    {
        Assert.Contains("_document.Database.ObjectErased += ObjectErased;", Live);
        Assert.Contains("_document.Database.ObjectErased -= ObjectErased;", Live);
        Assert.Contains("documents.DocumentCreated += DocumentCreated;", Live);
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
