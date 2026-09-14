using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticPurlinPersistenceAutoCadSourceContractTests
{
    private static readonly string LayoutStore = ReadInfrastructure("RoofPurlinLayoutStore.cs");
    private static readonly string GeneratedStore =
        ReadInfrastructure("RoofAutomaticPurlinGeneratedStore.cs");
    private static readonly string DatumStore =
        ReadInfrastructure("RoofRelativeElevationDatumStore.cs");

    [Fact]
    public void LayoutStore_UsesDedicatedExactTypedSchemaOnePayload()
    {
        Assert.Contains("DECORAIR_ACADKROVY_ROOF_PURLIN_LAYOUT", LayoutStore);
        Assert.Contains("DxfCode.ExtendedDataRegAppName", LayoutStore);
        Assert.Contains("DxfCode.ExtendedDataInteger16", LayoutStore);
        Assert.Contains("DxfCode.ExtendedDataInteger32", LayoutStore);
        Assert.Contains("DxfCode.ExtendedDataAsciiString", LayoutStore);
        Assert.Contains("DxfCode.ExtendedDataReal", LayoutStore);
        var encode = Member(
            LayoutStore,
            "internal static IReadOnlyList<TypedValue> EncodePayload",
            "public static void Write(");
        AssertOrdered(
            encode,
            "new(DxfRegAppNameCode, RegAppName)",
            "checked((short)RoofPurlinLayoutSchema.CurrentVersion)",
            "checked((short)(canonical.RidgeEnabled",
            "new(DxfInt32Code, canonical.IntermediateItems.Count)",
            "item.LayoutItemId",
            "item.Enabled",
            "item.PlacementMode switch",
            "item.PlacementValueMm",
            "item.ReferenceRidgeKey",
            "item.SeatingDepth");
    }

    [Fact]
    public void GeneratedStore_UsesDedicatedExactRoleSpecificPayloads()
    {
        Assert.Contains(
            "DECORAIR_ACADKROVY_ROOF_AUTOMATIC_PURLIN_GENERATED",
            GeneratedStore);
        Assert.Contains("private const int RidgeValueCount = 6", GeneratedStore);
        Assert.Contains("private const int IntermediateValueCount = 8", GeneratedStore);
        var encode = Member(
            GeneratedStore,
            "internal static IReadOnlyList<TypedValue> EncodePayload",
            "public static void Write(");
        AssertOrdered(
            encode,
            "new(DxfRegAppNameCode, RegAppName)",
            "checked((short)RoofAutomaticPurlinGeneratedDataSchema.CurrentVersion)",
            "new(DxfAsciiStringCode, canonical.RoofOwnerReference)",
            "FormatRole(canonical.GeneratorRole)");
        AssertOrdered(
            encode,
            "case RoofAutomaticPurlinRidgeKey",
            "ridge.StructuralKey.BoundaryEdgeIdA",
            "ridge.StructuralKey.BoundaryEdgeIdB");
        AssertOrdered(
            encode,
            "case RoofAutomaticPurlinIntermediateKey",
            "new TypedValue(DxfAsciiStringCode, intermediate.LayoutItemId)",
            "intermediate.SourceFaceBoundaryEdgeId));",
            "new TypedValue(DxfAsciiStringCode, endpointA)",
            "new TypedValue(DxfAsciiStringCode, endpointB)");
        Assert.DoesNotContain("ExtendedDataHandle", GeneratedStore);
        Assert.DoesNotContain("ObjectId", encode);
        Assert.DoesNotContain("RoofPoint", encode);
        Assert.DoesNotContain("Elevation", encode);
        Assert.DoesNotContain("Length", encode);
    }

    [Fact]
    public void RelativeDatumStore_UsesIndependentExactSchemaOnePayload()
    {
        Assert.Contains("DECORAIR_ACADKROVY_ROOF_RELATIVE_ELEVATION_DATUM", DatumStore);
        Assert.Contains("private const int ValueCount = 5", DatumStore);
        var encode = Member(
            DatumStore,
            "internal static IReadOnlyList<TypedValue> EncodePayload",
            "public static void Write(");
        Assert.Contains("RoofRelativeElevationDatumSchema.CurrentVersion", encode);
        AssertOrdered(
            encode,
            "new TypedValue(DxfRegAppNameCode, RegAppName)",
            "validated.Datum.ReferenceKind.ToString()",
            "validated.Datum.ReferenceRelativeElevationMm",
            "validated.Datum.ReferenceLocalZMm");
        Assert.DoesNotContain("FormatMetres", DatumStore);
    }

    [Fact]
    public void RelativeDatumRead_IsSideEffectFreeAndMissingRemainsDistinct()
    {
        var read = Member(
            DatumStore,
            "public static RoofRelativeElevationDatumStoreReadResult Read",
            "internal static RoofRelativeElevationDatumStoreReadResult DecodePayload");
        Assert.Contains("GetXDataForApplication(RegAppName)", read);
        Assert.Contains("RoofRelativeElevationDatumStoreReadResult.Missing", read);
        Assert.DoesNotContain("EnsureRegAppRegistered", read);
        Assert.DoesNotContain("UpgradeOpen", read);
        Assert.DoesNotContain(".XData =", read);
    }

    [Fact]
    public void LayoutStore_IsAuthoritativeOwnerOnlyAndReadIsSideEffectFree()
    {
        var read = Member(
            LayoutStore,
            "public static RoofPurlinLayoutStoreReadResult Read",
            "internal static RoofPurlinLayoutStoreReadResult DecodePayload");
        Assert.Contains("entity is not Polyline", read);
        Assert.Contains("RoofDefinitionStore.Read(source).Data is null", read);
        Assert.Contains("GetXDataForApplication(RegAppName)", read);
        Assert.DoesNotContain("EnsureRegAppRegistered", read);
        Assert.DoesNotContain("UpgradeOpen", read);
        Assert.DoesNotContain(".XData =", read);
        Assert.Contains("RoofAutomaticPurlinLayout.Empty", LayoutStore);
    }

    [Fact]
    public void BothStores_ValidateBeforeRegisteringOrMutating()
    {
        var layoutWrite = Member(LayoutStore, "public static void Write(",
            "private static List<TypedValue> ReadForeignXData");
        AssertOrdered(layoutWrite, "EncodePayload(layout)", "EnsureRegAppRegistered");
        Assert.Equal(1, CountOccurrences(layoutWrite, "source.XData ="));

        var generatedWrite = Member(GeneratedStore, "public static void Write(",
            "public static void WriteAtomic(");
        Assert.Contains("BuildSection(entity, transaction, data)", generatedWrite);
        Assert.Equal(1, CountOccurrences(generatedWrite, "entity.XData ="));
    }

    [Fact]
    public void BothStores_PreserveForeignXDataAndReplaceOnlyTheirOwnSection()
    {
        foreach (var store in new[] { LayoutStore, GeneratedStore, DatumStore })
        {
            var foreign = Member(
                store,
                "private static List<TypedValue> ReadForeignXData",
                "private static void EnsureRegAppRegistered");
            Assert.Contains("entity.XData", foreign);
            Assert.Contains("value.TypeCode == DxfRegAppNameCode", foreign);
            Assert.Contains("RegAppName", foreign);
            Assert.Contains("retained.Add(value)", foreign);
        }
    }

    [Fact]
    public void GeneratedStore_KeepsIndependentSectionAndSupportsAtomicTimberComposition()
    {
        Assert.DoesNotContain("RoofStructuralGeneratedStore", GeneratedStore);
        Assert.DoesNotContain("DECORAIR_ACADKROVY_ROOF_STRUCTURAL_GENERATED", GeneratedStore);
        Assert.Contains("ElementDataStore.BuildSection", GeneratedStore);
        Assert.Contains("public static void WriteAtomic", GeneratedStore);
        Assert.Equal(1, CountOccurrences(
            Member(GeneratedStore, "public static void WriteAtomic(",
                "public static IReadOnlyList<TypedValue> BuildSection("),
            "entity.XData ="));
    }

    [Fact]
    public void Stores_DoNotCreateEntitiesGroupsOrLifecycleHooks()
    {
        var combined = LayoutStore + "\n" + GeneratedStore;
        Assert.DoesNotContain("AppendEntity", combined);
        Assert.DoesNotContain("new Line", combined);
        Assert.DoesNotContain("Group", combined);
        Assert.DoesNotContain("CommandEnded", combined);
        Assert.DoesNotContain("ObjectAppended", combined);
        Assert.DoesNotContain("ObjectErased", combined);
    }

    [Fact]
    public void Stores_HaveNoLifecycleHookAndDebugEntryPointIsCompileTimeIsolated()
    {
        var command = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AutoCadAutomaticPurlinMaterializationCommands.cs"));
        Assert.StartsWith("#if DEBUG", command);
        Assert.Contains("AK_DEBUG_PURLIN_MATERIALIZE", command);
        Assert.DoesNotContain("CommandEnded", command + GeneratedStore + LayoutStore);
        Assert.DoesNotContain("ObjectAppended", command + GeneratedStore + LayoutStore);
        Assert.DoesNotContain("ObjectErased", command + GeneratedStore + LayoutStore);
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
