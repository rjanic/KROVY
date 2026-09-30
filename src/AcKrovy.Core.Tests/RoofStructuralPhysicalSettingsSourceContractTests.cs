using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofStructuralPhysicalSettingsSourceContractTests
{
    private static readonly string Store = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofPhysicalElevationStore.cs");
    private static readonly string Planner = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofAutomaticStructuralRafterPlanner.cs");
    private static readonly string PhysicalRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofPhysicalElevationRules.cs");
    private static readonly string ElevationRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofAbsoluteElevationRules.cs");

    [Fact]
    public void OwnerStore_AppendsStructuralFieldsInSchema4Only()
    {
        Assert.Contains("RoofPhysicalElevationSchema.Version3", Store);
        Assert.Contains("Schema3ValueCount = 9", Store);
        Assert.Contains("Schema4ValueCount = 12", Store);
        Assert.Contains("schemaVersion == RoofPhysicalElevationSchema.Version3", Store);
        Assert.Contains("values.Count != expectedCount", Store);
        Assert.Contains("values[9].TypeCode != DxfRealCode", Store);
        Assert.Contains("values[10].TypeCode != DxfAsciiStringCode", Store);
        Assert.Contains("values[11].TypeCode != DxfRealCode", Store);

        var encode = RoofUxSourceContractText.Member(
            Store, "internal static IReadOnlyList<TypedValue> EncodePayload", "public static void Write(");
        var join = encode.IndexOf("canonical.RidgeJoinMode.ToString()", StringComparison.Ordinal);
        var width = encode.IndexOf("canonical.StructuralWidthMm", StringComparison.Ordinal);
        var mode = encode.IndexOf("canonical.StructuralHeightMode.ToString()", StringComparison.Ordinal);
        var height = encode.IndexOf("canonical.StructuralExplicitHeightMm", StringComparison.Ordinal);
        Assert.True(join >= 0 && width > join && mode > width && height > mode);
    }

    [Fact]
    public void OwnerStore_ValidatesPayloadAndKeepsExistingOwnershipScope()
    {
        Assert.Contains("RoofDefinitionStore.Read(source).Data is null", Store);
        Assert.Contains("source.GetXDataForApplication(RegAppName)", Store);
        Assert.Contains("RoofPhysicalElevationRules.Validate(", Store);
        Assert.Contains("RoofStructuralPhysicalSettings.DefaultWidthMm", Store);
        Assert.Contains("RoofStructuralHeightMode.Automatic", Store);
        Assert.Contains("RoofPhysicalElevationError.UnsupportedSchemaVersion", Store);
        Assert.Contains("RoofPhysicalElevationError.MalformedValueType", Store);
        Assert.Contains("RoofPhysicalElevationError.UnexpectedTrailingValue", Store);
        Assert.Contains("ReadForeignXData(source)", Store);
    }

    [Fact]
    public void StructuralLineWidth_IsDerivedFromOwnerPhysicalState()
    {
        Assert.Contains("RoofAbsoluteElevationState? ownerPhysicalState", Planner);
        Assert.Contains("WidthMm = ownerPhysicalState?.StructuralWidthMm", Planner);
        Assert.Contains("RoofStructuralPhysicalSettings.DefaultWidthMm", Planner);
        Assert.DoesNotContain("HipPhysicalWidthMm", Planner);
    }

    [Fact]
    public void StateConversionsAndElevationChanges_PreserveOwnerStructuralSettings()
    {
        var fromState = RoofUxSourceContractText.Member(
            PhysicalRules, "public static RoofPhysicalElevationData CreateFromState", "public static RoofAbsoluteElevationState ToState");
        var toState = RoofUxSourceContractText.Member(
            PhysicalRules, "public static RoofAbsoluteElevationState ToState", "public static RoofAbsoluteElevationState MissingStoreDefault");
        var switchMode = RoofUxSourceContractText.Member(
            ElevationRules, "public static RoofAbsoluteElevationState SwitchMode", "public static RoofAbsoluteElevationState RecalculateForGeometry");
        var recalculate = RoofUxSourceContractText.Member(
            ElevationRules, "public static RoofAbsoluteElevationState RecalculateForGeometry", "public static RoofAbsoluteElevationState WithPhysical3DEnabled");

        foreach (var field in new[]
                 {
                     "StructuralWidthMm", "StructuralHeightMode", "StructuralExplicitHeightMm",
                 })
        {
            Assert.Contains("state." + field, fromState);
            Assert.Contains("data." + field, toState);
            Assert.Contains("current." + field, switchMode);
            Assert.Contains("current." + field, recalculate);
        }
    }
}
