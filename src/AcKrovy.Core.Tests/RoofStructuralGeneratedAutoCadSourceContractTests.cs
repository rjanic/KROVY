using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofStructuralGeneratedAutoCadSourceContractTests
{
    private static readonly string Store = ReadInfrastructure("RoofStructuralGeneratedStore.cs");
    private static readonly string OrdinaryStore = ReadInfrastructure("RoofGeneratedTimberStore.cs");
    private static readonly string OrdinaryModel = ReadCoreModel("RoofGeneratedTimberData.cs");

    [Fact]
    public void StructuralStore_UsesDedicatedTypedExactPayload()
    {
        Assert.Contains("DECORAIR_ACADKROVY_ROOF_STRUCTURAL_GENERATED", Store);
        Assert.Contains("DxfCode.ExtendedDataInteger16", Store);
        Assert.Contains("DxfCode.ExtendedDataInteger32", Store);
        Assert.Contains("DxfCode.ExtendedDataAsciiString", Store);
        Assert.Contains("values.Count < 6", Store);
        Assert.Contains("values.Count > 6", Store);
        Assert.Contains("UnexpectedTrailingValue", Store);
        Assert.Contains("MalformedValueType", Store);

        var build = Member(Store, "public static IReadOnlyList<TypedValue> BuildSection", "private static List<TypedValue> ReadForeignXData");
        AssertOrdered(
            build,
            "RegAppName),",
            "canonical.SchemaVersion",
            "canonical.RoofOwnerReference",
            "FormatRole(canonical.StructuralRole)",
            "canonical.BoundaryEdgeIdA",
            "canonical.BoundaryEdgeIdB");
        Assert.DoesNotContain("ObjectId", build);
        Assert.DoesNotContain("TopologyEdgeIndex", build);
        Assert.DoesNotContain("SourceEdgeIndex", build);
        Assert.DoesNotContain("RoofPoint", build);
        Assert.DoesNotContain("Length", build);
        Assert.DoesNotContain("ExtendedDataHandle", Store);
    }

    [Fact]
    public void StructuralStore_ReadAndWriteShareStrictDomainValidation()
    {
        var decode = Member(Store, "internal static RoofStructuralGeneratedStoreReadResult DecodePayload", "public static void Write(");
        var build = Member(Store, "public static IReadOnlyList<TypedValue> BuildSection", "private static List<TypedValue> ReadForeignXData");
        Assert.Contains("RoofStructuralGeneratedDataRules.ValidateStored", decode);
        Assert.Contains("RoofStructuralGeneratedDataRules.ValidateStored", build);
        Assert.Contains("RoofStructuralGeneratedStoreReadResult.Valid", decode);
        Assert.Contains("RoofStructuralGeneratedStoreReadResult.Invalid", decode);
    }

    [Fact]
    public void StructuralRead_IsSideEffectFreeAndForeignXDataIsPreservedOnExplicitWrite()
    {
        var read = Member(Store, "public static RoofStructuralGeneratedStoreReadResult Read", "internal static RoofStructuralGeneratedStoreReadResult DecodePayload");
        var foreign = Member(Store, "private static List<TypedValue> ReadForeignXData", "private static void EnsureRegAppRegistered");
        Assert.Contains("GetXDataForApplication(RegAppName)", read);
        Assert.DoesNotContain("UpgradeOpen", read);
        Assert.DoesNotContain(".XData =", read);
        Assert.Contains("skipStructuralSection", foreign);
        Assert.Contains("retained.Add(value)", foreign);
    }

    [Fact]
    public void OnlyExplicitStructuralMaterializerWritesStructuralMetadata()
    {
        var allOtherProduction = string.Join(
            "\n",
            Directory.GetFiles(
                    Path.Combine(RepositoryRoot(), "src", "AcKrovy.AutoCAD"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(
                    "RoofStructuralGeneratedStore.cs",
                    StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.EndsWith(
                    "RoofAutomaticStructuralRafterMaterializationService.cs",
                    StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.EndsWith(
                    "RoofAssemblyGroupMemberCollector.cs",
                    StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.EndsWith(
                    "RoofAssemblyGroupSyncService.cs",
                    StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
        Assert.DoesNotContain("RoofStructuralGeneratedStore.Write(", allOtherProduction);
        Assert.DoesNotContain("RoofStructuralGeneratedStore.WriteAtomic(", allOtherProduction);
    }

    [Fact]
    public void OrdinaryGeneratedRafterContract_RemainsSeparateAndUnchanged()
    {
        Assert.Contains("DECORAIR_ACADKROVY_ROOF_TIMBER", OrdinaryStore);
        Assert.DoesNotContain("ROOF_STRUCTURAL_GENERATED", OrdinaryStore);
        Assert.DoesNotContain("RoofStructural", OrdinaryStore);
        Assert.DoesNotContain("RoofStructural", OrdinaryModel);
        Assert.Contains("RoofGeneratedTimberKind MemberKind", OrdinaryModel);
        Assert.Contains("RafterRoofFace RoofFace", OrdinaryModel);
        Assert.Contains("int StationIndex", OrdinaryModel);
    }

    [Fact]
    public void StoreWriter_UsesOneMergedXDataAssignment()
    {
        var write = Member(Store, "public static void Write(", "public static void WriteAtomic(");
        Assert.Equal(1, CountOccurrences(write, "entity.XData ="));
        Assert.Contains("ReadForeignXData(entity)", write);
        Assert.Contains("BuildSection(entity, transaction, data)", write);
    }

    [Fact]
    public void AtomicWriter_MergesTimberAndStructuralSectionsInOneAssignment()
    {
        var write = Member(Store, "public static void WriteAtomic(", "public static IReadOnlyList<ObjectId> FindByOwner");
        Assert.Equal(1, CountOccurrences(write, "entity.XData ="));
        Assert.Contains("ElementDataStore.BuildSection", write);
        Assert.Contains("BuildSection(entity, transaction, structuralData)", write);
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
        Assert.True(end > start, endMarker);
        return source[start..end];
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

    private static string ReadInfrastructure(string fileName) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            fileName));

    private static string ReadCoreModel(string fileName) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.Core",
            "Models",
            "Roofs",
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
}
