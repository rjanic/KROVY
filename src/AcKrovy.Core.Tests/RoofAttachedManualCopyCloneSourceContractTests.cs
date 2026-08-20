using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAttachedManualCopyCloneSourceContractTests
{
    private static readonly string Service = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualCopyCloneReinitializeService.cs");
    private static readonly string LiveSync = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs");

    [Fact]
    public void Service_GatedOnSameDwgCopy_NotUndoRedo()
    {
        Assert.Contains("IsSameDwgCopyOwnershipCommand(globalCommandName)", Service);
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", Service);
    }

    [Fact]
    public void Clone_RequiresCopyOrigin_NotSplit()
    {
        Assert.Contains("Origin != RoofAttachedManualOrigin.Copy", Service);
    }

    [Fact]
    public void Clone_GetsFreshChildIdentity_FromCloneHandle()
    {
        Assert.Contains("cloneLine.Handle.ToString()", Service);
    }

    [Fact]
    public void Clone_UsesFaceAwareNearestAnchor_AndCapturesFromCloneWcs()
    {
        Assert.Contains("SelectNearestMirrorAnchor", Service);
        Assert.Contains("RoofAttachedManualLifecycleService.CreateAnchoredData", Service);
        Assert.Contains("cloneLine.StartPoint", Service);
        Assert.Contains("cloneLine.EndPoint", Service);
        Assert.Contains("RoofAttachedManualLifecycleService.WriteAnchored", Service);
    }

    [Fact]
    public void Clone_PreservesCopyOrigin()
    {
        Assert.Contains("RoofAttachedManualOrigin.Copy", Service);
    }

    [Fact]
    public void Clone_RequiresAnchorKey()
    {
        Assert.Contains("AnchorGeneratedMemberKey is null", Service);
    }

    [Fact]
    public void Clone_EmitsCompactDiagnostic()
    {
        Assert.Contains("ROOF_ATTACHED_MANUAL_COPY", Service);
        Assert.Contains("relativeCaptured", Service);
        Assert.Contains("newCloneAnchor", Service);
    }

    [Fact]
    public void Service_RunsBeforeCopyRehydration()
    {
        var reinit = LiveSync.IndexOf(
            "RoofAttachedManualCopyCloneReinitializeService.Process(",
            StringComparison.Ordinal);
        var rehydration = LiveSync.IndexOf(
            "RoofGeneratedRafterCopyOwnershipRehydrationService.Process(",
            StringComparison.Ordinal);
        Assert.True(reinit >= 0, "reinit call not found");
        Assert.True(rehydration > reinit, "reinit must run before COPY rehydration");
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
