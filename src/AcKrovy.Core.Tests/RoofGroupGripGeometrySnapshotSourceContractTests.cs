using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGroupGripGeometrySnapshotSourceContractTests
{
    private static readonly string Live = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string Snapshot = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGroupGripGeometrySnapshotService.cs");
    private static readonly string Adoption = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGroupGripResizeAdoptionService.cs");
    private static readonly string Rules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofGroupGripNativeObservationRules.cs");

    [Fact]
    public void ObjectModified_SnapshotCaptureRunsBeforeSuppressQueue()
    {
        Assert.Contains("TryCaptureNativeObjectModified", Live);
        Assert.Contains("TryCaptureNativeObjectModified", Snapshot);
        Assert.DoesNotContain("RoofGroupGripRawDiag", Live);
        Assert.DoesNotContain("AK_DEV_ROOF_GRIP_RAW", Live + Snapshot);

        var modified = RoofUxSourceContractText.Member(
            Live,
            "private void ObjectModified",
            "private void ObjectErased");

        var captureIdx = modified.IndexOf("TryCaptureNativeObjectModified", StringComparison.Ordinal);
        var suppressReturnIdx = modified.IndexOf(
            "if (_modifiedIds.IsSuppressed)",
            StringComparison.Ordinal);
        var queueIdx = modified.IndexOf("_modifiedIds.TryAdd", StringComparison.Ordinal);

        Assert.True(captureIdx >= 0, "snapshot capture missing");
        Assert.True(captureIdx < suppressReturnIdx, "snapshot must run before suppress return");
        Assert.True(suppressReturnIdx < queueIdx, "suppress return must precede queue TryAdd");
        Assert.Contains("StartPoint", Snapshot);
        Assert.Contains("EndPoint", Snapshot);
    }

    [Fact]
    public void SnapshotClearsOnCommandBoundariesAndFreezesBeforePluginProcess()
    {
        Assert.Contains("BeginCommandScope", Live + Snapshot);
        Assert.Contains("EndCommandScope", Live + Snapshot);
        Assert.Contains("FreezeAll", Live + Snapshot);
        Assert.Contains("CommandCancelled", Live);
        Assert.Contains("CommandFailed", Live);
        Assert.Contains("_frozen", Snapshot);
        Assert.Contains("CaptureFromImpliedSelection", Live);
        Assert.Contains("RoofLiveResizeService.Process", Live);
    }

    [Fact]
    public void InactiveCommandScope_SkipsSilentlyWithoutCliSpam()
    {
        Assert.Contains("modifiedIdsSuppressed", Snapshot);
        Assert.DoesNotContain("AK_DEV_ROOF_GRIP_SNAP", Snapshot);
        Assert.DoesNotContain("reason=command-scope-inactive", Snapshot);
        Assert.DoesNotContain("FormatEntityGeom", Snapshot);
        Assert.DoesNotContain("WriteMessage", Snapshot);
    }

    [Fact]
    public void Adoption_UsesSnapshotNotRestoredLiveDbAsObservedAuthority()
    {
        Assert.Contains("TryGetLatestObservedDisplayByRole", Adoption);
        Assert.Contains("snapshotObserved", Adoption);
        Assert.Contains("TryDeriveSupportedSideResize", Adoption);
        Assert.Contains("timing-case-C-transient-only", Adoption);
        Assert.Contains("HasMeaningfulDeltaFromExpected", Adoption + Rules);
        Assert.DoesNotContain("SendStringToExecute", Adoption + Snapshot + Live);
        Assert.DoesNotContain("BeginDeepClone", Adoption + Snapshot + Live);
        Assert.DoesNotContain("IdMapping", Adoption + Snapshot + Live);
    }

    [Fact]
    public void PluginSuppressSkipsSnapshotOverwrite()
    {
        Assert.Contains("modifiedIdsSuppressed", Snapshot);
        Assert.Contains("if (modifiedIdsSuppressed)", Snapshot);
        Assert.Contains("if (_frozen)", Snapshot);
    }

    [Fact]
    public void SingleObjectModifiedSubscription_QueuesIdsInLiveGeometryHandler()
    {
        Assert.Equal(1, CountOccurrences(Live, "Database.ObjectModified +="));
        Assert.Equal(1, CountOccurrences(Live, "Database.ObjectModified -="));
        Assert.Contains("_modifiedIds.TryAdd(entity.ObjectId)", Live);
        Assert.DoesNotContain("ObjectModified +=", Snapshot);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
