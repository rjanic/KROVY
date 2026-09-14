using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofBoundaryIdentityAutoCadSourceContractTests
{
    private static readonly string Store = ReadInfrastructure("RoofBoundaryIdentityStore.cs");
    private static readonly string Service = ReadInfrastructure("RoofBoundaryIdentityService.cs");
    private static readonly string RoofStore = ReadInfrastructure("RoofDefinitionStore.cs");
    private static readonly string LockedRecovery = ReadInfrastructure("RoofLiveResizeService.cs");

    [Fact]
    public void XDataContract_UsesDedicatedRegAppAndTypedOrderedPayload()
    {
        Assert.Contains("DECORAIR_ACADKROVY_ROOF_BOUNDARY_IDENTITY", Store);
        Assert.Contains("DxfCode.ExtendedDataInteger16", Store);
        Assert.Contains("DxfCode.ExtendedDataInteger32", Store);
        Assert.Contains("DxfCode.ExtendedDataAsciiString", Store);

        var write = Member(Store, "public static void Write(", "private static List<TypedValue>");
        AssertOrdered(
            write,
            "RegAppName));",
            "identity.SchemaVersion",
            "identity.PhysicalSegmentCount",
            "FormatWinding(identity.RawWinding)",
            "identity.BoundaryEdgeIds.Select");
        Assert.DoesNotContain("ObjectId", write);
        Assert.DoesNotContain("Handle", write);
    }

    [Fact]
    public void Read_IsStrictAndSideEffectFree()
    {
        var read = Member(Store, "public static RoofBoundaryIdentityStoreReadResult Read", "internal static RoofBoundaryIdentityStoreReadResult DecodePayload");
        Assert.Contains("GetXDataForApplication(RegAppName)", read);
        Assert.Contains("RoofDefinitionStore.Read(source).Data is null", read);
        Assert.Contains("ValidateCurrentSource", read);
        Assert.DoesNotContain("UpgradeOpen", read);
        Assert.DoesNotContain("EnsureRegAppRegistered", read);
        Assert.DoesNotContain(".XData =", read);
    }

    [Fact]
    public void Ensure_IsLazyCreateOnceAndMalformedDataIsNotOverwritten()
    {
        var ensure = Member(Service, "public static RoofBoundaryIdentityEnsureResult EnsureBoundaryIdentity", "internal enum RoofBoundaryIdentityEnsureStatus");
        var readIndex = ensure.IndexOf("RoofBoundaryIdentityStore.Read(source)", StringComparison.Ordinal);
        var existingIndex = ensure.IndexOf("RoofBoundaryIdentityEnsureResult.Existing", StringComparison.Ordinal);
        var invalidIndex = ensure.IndexOf("if (stored.Exists)", StringComparison.Ordinal);
        var writeIndex = ensure.IndexOf("RoofBoundaryIdentityStore.Write", StringComparison.Ordinal);

        Assert.True(readIndex >= 0 && existingIndex > readIndex);
        Assert.True(invalidIndex > existingIndex && writeIndex > invalidIndex);
        Assert.Contains("CreateSequential", ensure);
        Assert.Contains("source.UpgradeOpen()", ensure);
    }

    [Fact]
    public void ProofOnlyBoundaryIdentityCommand_IsRemovedAfterHostProof()
    {
        var commandPath = Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AutoCadRoofBoundaryIdentityDebugCommands.cs");
        Assert.False(File.Exists(commandPath));

        var allProduction = string.Join(
            "\n",
            Directory.GetFiles(
                    Path.Combine(RepositoryRoot(), "src", "AcKrovy.AutoCAD"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(path => !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
        Assert.Equal(3, CountOccurrences(allProduction, "EnsureBoundaryIdentity("));
        Assert.Contains(
            "RoofBoundaryIdentityService.EnsureBoundaryIdentity",
            File.ReadAllText(Path.Combine(
                RepositoryRoot(),
                "src",
                "AcKrovy.AutoCAD",
                "Infrastructure",
                "RoofAutomaticStructuralRafterMaterializationService.cs")));
        Assert.Contains(
            "RoofBoundaryIdentityService.EnsureBoundaryIdentity",
            File.ReadAllText(Path.Combine(
                RepositoryRoot(),
                "src",
                "AcKrovy.AutoCAD",
                "Infrastructure",
                "RoofAutomaticPurlinMaterializationService.cs")));
    }

    [Fact]
    public void ForeignXDataIncludingBoundaryIdentitySurvivesRoofDefinitionWrites()
    {
        var foreign = Member(RoofStore, "private static List<TypedValue> ReadForeignXData", "private static void EnsureRegAppRegistered");
        Assert.Contains("skipRoofSection", foreign);
        Assert.Contains("RegAppName", foreign);
        Assert.DoesNotContain("ROOF_BOUNDARY_IDENTITY", foreign);
    }

    [Fact]
    public void LockedExactSourceUnEraseDoesNotReplaceOrRewriteBoundaryIdentity()
    {
        var restore = Member(LockedRecovery, "private static bool TryUnEraseLockedSource", "private static bool ApplyGeneratedChildEraseTampers");
        Assert.Contains("owner.Erase(false)", restore);
        Assert.Contains("owner.ObjectId == ownerId", restore);
        Assert.Contains("owner.Handle.ToString()", restore);
        Assert.DoesNotContain("RoofBoundaryIdentity", restore);
        Assert.DoesNotContain(".XData =", restore);
    }

    [Fact]
    public void IdentityAuthority_IsNeverAttachedToDerivedEntities()
    {
        Assert.Contains("entity is not Polyline source", Store);
        Assert.Contains("RoofDefinitionStore.Read(source).Data is null", Store);
        Assert.DoesNotContain("Line", Service);
        Assert.DoesNotContain("MLeader", Service);
        Assert.DoesNotContain("RoofGeneratedTimberStore", Service);
        Assert.DoesNotContain("RoofDisplayStore", Service);
    }

    private static void AssertOrdered(string source, params string[] values)
    {
        var previous = -1;
        foreach (var value in values)
        {
            var current = source.IndexOf(value, StringComparison.Ordinal);
            Assert.True(current > previous, value);
            previous = current;
        }
    }

    private static string Member(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, startMarker);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string ReadInfrastructure(string fileName) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            fileName));

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

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }
}
