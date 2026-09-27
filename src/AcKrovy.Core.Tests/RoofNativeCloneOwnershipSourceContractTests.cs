using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Adapter integration guards; these do not claim native HOST event execution.</summary>
public sealed class RoofNativeCloneOwnershipSourceContractTests
{
    private static string Read(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", name + ".cs"));
    }

    [Fact]
    public void NativeCallback_CapturesIdsWithoutDatabaseWrites()
    {
        var source = Read("LiveGeometrySynchronizationService");
        var callback = source[source.IndexOf("private void NativeRoofCloneMapping", StringComparison.Ordinal)..
            source.IndexOf("private void CommandWillStart", StringComparison.Ordinal)];
        Assert.Contains("IsUndoRedoCommand", callback);
        Assert.Contains("_nativeRoofClones.Observe(e.IdMapping)", callback);
        Assert.DoesNotContain("GetObject(", callback);
        Assert.DoesNotContain("StartTransaction(", callback);
        Assert.DoesNotContain("Commit(", callback);
        Assert.Contains("BeginDeepCloneTranslation -= NativeRoofCloneMapping", source);
    }

    [Fact]
    public void Snapshot_UsesExactMappingAndPreCommandProvenance()
    {
        var source = Read("RoofNativeCloneSnapshot");
        Assert.Contains("pair.IsCloned", source);
        Assert.Contains("RoofNativeCloneOwnershipRules.TryGetDisposableClones", source);
        Assert.Contains("owner.Required.Concat(owner.Disposable).Append(owner.Id)", source);
        Assert.DoesNotContain("OpenMode.ForWrite", source);
        Assert.DoesNotContain(".Commit()", source);
        Assert.DoesNotContain("GeometricExtents", source);
    }

    [Fact]
    public void CleanupAndCanonicalRebuild_ShareOneCommit()
    {
        var source = Read("RoofWholeRoofCopyRebindService");
        Assert.Contains("TryEraseNativeDisposableClones(database, transaction, pair)", source);
        Assert.Contains("foreach (var native in nativeClones)", source);
        Assert.DoesNotContain("EraseStaleDisplayClones", source);
        Assert.DoesNotContain("DefinitionsEquivalent", source);
        Assert.Contains("RoofDisplayService.Rebuild(", source);
        Assert.Contains("RoofPhysical3DLifecycleService.ReconcileOwnerInTransaction(", source);
        Assert.True(source.IndexOf("transaction.Commit();", StringComparison.Ordinal) <
            source.IndexOf("committed=true", StringComparison.Ordinal));
        var cleanup = source[source.IndexOf("private static bool TryEraseNativeDisposableClones", StringComparison.Ordinal)..
            source.IndexOf("private static void EraseGeneratedClones", StringComparison.Ordinal)];
        Assert.DoesNotContain("FindByOwner", cleanup);
        Assert.DoesNotContain("StartTransaction", cleanup);
        Assert.DoesNotContain("GeometricExtents", cleanup);
        Assert.Contains("id == pair.NewOwnerCandidate.PolylineId", cleanup);
    }
}
