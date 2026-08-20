using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedTimberStoreClearSourceContractTests
{
    private static readonly string Store = Read("RoofGeneratedTimberStore.cs");
    private static readonly string ElementStore = Read("ElementDataStore.cs");
    private static readonly string Rehydration = Read("RoofGeneratedRafterCopyOwnershipRehydrationService.cs");

    [Fact]
    public void Clear_UsesRegAppOnlySentinel_ToRemoveGeneratedApplication()
    {
        var clearPath = Segment(Store, "public static bool TryClear", "private static void WriteGeneratedClearFail");
        Assert.Contains("eraseLink", clearPath);
        Assert.Contains("eraseCanonical", clearPath);
        Assert.Contains("entity.XData = eraseLink", clearPath);
        Assert.Contains("entity.XData = eraseCanonical", clearPath);
        Assert.DoesNotContain("ReadForeignXData(entity)", clearPath);
    }

    [Fact]
    public void TryClear_GuardsEraseWithPresenceCheck_AndSkipsAbsentLegacyLink()
    {
        var clearPath = Segment(Store, "public static bool TryClear", "private static void WriteGeneratedClearFail");
        Assert.Contains("HasApplicationSection(entity, LinkRegAppName)", clearPath);
        Assert.Contains("HasApplicationSection(entity, RegAppName)", clearPath);
        Assert.Contains("RegisteredApplicationIdNotFound", clearPath);
        // The unsafe GetXDataForApplication(LinkRegAppName) residual read is removed.
        Assert.DoesNotContain("GetXDataForApplication(LinkRegAppName)", clearPath);
    }

    [Fact]
    public void DebugClearFail_EmitsMissingRegAppDiagnostic()
    {
        Assert.Contains("ROOF_COPY_XDATA_CLEAR", Store);
        Assert.Contains("missingApps=", Store);
        Assert.Contains("MissingRegApps", Store);
    }

    [Fact]
    public void TryClear_VerifiesGeneratedReadAbsent_AndRegAppBufferMissing()
    {
        var clearPath = Segment(Store, "public static bool TryClear", "private static void WriteGeneratedClearFail");
        Assert.Contains("var read = Read(entity)", clearPath);
        Assert.Contains("read.Data is not null", clearPath);
        Assert.Contains("GetXDataForApplication(RegAppName)", clearPath);
        Assert.Contains("generated-xdata-remains", clearPath);
    }

    [Fact]
    public void TryClear_DoesNotRemoveGenericTimberRegApp()
    {
        Assert.Contains("DECORAIR_ACADKROVY_ROOF_TIMBER", Store);
        Assert.Contains("DECORAIR_ACADKROVY\"", ElementStore);
        Assert.DoesNotContain("ElementDataStore.Clear", Store);
        var clearPath = Segment(Store, "public static bool TryClear", "private static void WriteGeneratedClearFail");
        Assert.DoesNotContain("ElementDataStore", clearPath);
    }

    [Fact]
    public void CopyDetach_UsesTryClear_AndFailsWhenGeneratedRemains()
    {
        Assert.Contains("RoofGeneratedTimberStore.TryClear(", Rehydration);
        Assert.Contains("generated-xdata-remains", Rehydration + Store);
    }

    [Fact]
    public void FindByOwner_UsesReadEffectiveOwner_AfterClearCloneExcluded()
    {
        var discovery = Segment(
            Store,
            "public static IReadOnlyList<ObjectId> FindByOwner",
            "public static void Clear");
        Assert.Contains("var stored = Read(entity)", discovery);
        Assert.Contains("stored.Data.RoofOwnerReference", discovery);
    }

    [Fact]
    public void DebugClearFail_EmitsRawPayload()
    {
        Assert.Contains("ROOF_GENERATED_CLEAR_FAIL", Store);
        Assert.Contains("DescribeRegAppXData", Store);
    }

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure", fileName));

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

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start token '{start}' not found.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End token '{end}' not found after '{start}'.");
        return source.Substring(startIndex, endIndex - startIndex);
    }
}
