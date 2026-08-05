using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class FramedBlockContentDefinitionSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ProductionService_UsesAkKrovyFbcFamilyAndNeverMutatesExisting()
    {
        var service = Normalize(ServiceSource());
        var validate = Member(service, "internal static bool ValidateExistingDefinition(");
        var create = Member(service, "private static AutoCadFramedBlockContentResult CreateDefinition(");

        Assert.Contains("AK_KROVY_FBC", PolicySource());
        Assert.Contains("TimberFramedBlockContentVariantRules.CreateRawKey(", service);
        Assert.Contains("FamilyRevisionToken", service);
        Assert.Contains("DimensionColumnSide", service);
        Assert.Contains("CreateCollisionName", PolicySource());
        Assert.Contains("OpenMode.ForRead", validate);
        Assert.DoesNotContain("OpenMode.ForWrite", validate);
        Assert.DoesNotContain("UpgradeOpen", validate);
        Assert.DoesNotContain("Erase(", validate);
        Assert.DoesNotContain("EraseDefinitionContents", service);
        Assert.Contains("AcKrovyItemLeaderBlockService.AddFrameGeometry(", create);
        Assert.Contains("AppendPlainConnectionMarker(", create);
        Assert.Contains("new DBPoint(", service);
        Assert.Contains("IsMTextAttributeDefinition", service);
        Assert.Contains("ITEM_NO", DefinitionRulesSource());
        Assert.Contains("WIDTH", DefinitionRulesSource());
        Assert.Contains("HEIGHT", DefinitionRulesSource());
        Assert.Contains("CalculateDimensionColumnLocalX(", DefinitionRulesSource());
        Assert.Contains("dimensionColumnSide", DefinitionRulesSource());
    }

    [Fact]
    public void VariantKey_ExcludesSideDenominatorAngleAndProofPrefixes()
    {
        var variant = VariantRulesSource();
        var createRawKey = Member(
            Normalize(variant),
            "public static string CreateRawKey(");
        var policy = PolicySource();

        Assert.DoesNotContain("TimberLeaderHorizontalSide", variant);
        Assert.DoesNotContain("sideToken", variant);
        Assert.DoesNotContain("annotationScaleDenominator", createRawKey);
        Assert.DoesNotContain("ElementAxis", createRawKey);
        Assert.DoesNotContain("SourceHandle", createRawKey);
        Assert.DoesNotContain("ElementId", createRawKey);
        Assert.Contains("AK_KROVY_FBC", variant);
        Assert.Contains("FamilyRevisionToken = \"R2\"", variant);
        Assert.Contains("DimensionsNegativeXToken", variant);
        Assert.Contains("DimensionsPositiveXToken", variant);
        Assert.Contains("dimensionColumnSide", createRawKey);
        Assert.Contains("NameFamilyPrefix = \"AK_KROVY_FBC_\"", policy);
        Assert.Contains("IsProductionFamilyName", policy);
        Assert.Contains("!name.StartsWith(\"AK_G5C_\"", Normalize(policy));
        Assert.Contains("!name.StartsWith(\"AK_DEV_\"", Normalize(policy));
        Assert.DoesNotContain("return \"AK_G5C_", policy);
        Assert.DoesNotContain("return \"AK_DEV_", policy);
    }

    [Fact]
    public void ProductionPaths_DoNotRouteAnnotationsOrTouchLifecycleOwners()
    {
        var service = ServiceSource();
        var policy = PolicySource();

        Assert.DoesNotContain("ElementLabelService", service);
        Assert.DoesNotContain("AutoCadFramedG4CompositeService", service);
        Assert.DoesNotContain("LiveGeometrySynchronizationService", service);
        Assert.DoesNotContain("new MLeader", service);
        Assert.DoesNotContain("ModelSpace", service);
        Assert.DoesNotContain("XData", service);
        Assert.DoesNotContain("FullLabel", service + policy);
        Assert.DoesNotContain("AK_G5C_R", service + policy);
    }

    [Fact]
    public void DebugVerify_CreatesDefinitionsOnlyAndIsDebugGuarded()
    {
        var commands = CommandsSource().Trim();
        var verify = VerifyServiceSource().Trim();

        Assert.StartsWith("#if DEBUG", commands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", commands, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", verify, StringComparison.Ordinal);
        Assert.EndsWith("#endif", verify, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_FBC_DEFINITIONS_VERIFY", commands);
        Assert.Contains("AcKrovyFramedBlockContentDefinitionService.Ensure(", verify);
        Assert.DoesNotContain("new MLeader", verify);
        Assert.DoesNotContain("ModelSpace", verify);
    }

    [Fact]
    public void AttrDefContract_IsSingleLineCenteredNonConstant()
    {
        var service = ServiceSource();
        var append = Member(service, "private static void AppendAttribute(");

        Assert.Contains("TextHorizontalMode.TextCenter", append);
        Assert.Contains("TextVerticalMode.TextVerticalMid", append);
        Assert.Contains("attribute.Constant = false", append);
        Assert.Contains("attribute.Preset = false", append);
        Assert.Contains("attribute.TextString = string.Empty", append);
        Assert.Contains("LockPositionInBlock = true", append);
        Assert.DoesNotContain("IsMTextAttributeDefinition = true", append);
    }

    private static string ServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AcKrovyFramedBlockContentDefinitionService.cs");

    private static string PolicySource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentPolicy.cs");

    private static string VariantRulesSource() => Read(
        "src/AcKrovy.Core/Services/TimberFramedBlockContentVariantRules.cs");

    private static string DefinitionRulesSource() => Read(
        "src/AcKrovy.Core/Services/TimberFramedBlockContentDefinitionRules.cs");

    private static string CommandsSource() => Read(
        "src/AcKrovy.AutoCAD/Commands/AutoCadFramedBlockContentDefinitionVerifyCommands.cs");

    private static string VerifyServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentDefinitionVerifyService.cs");

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relativePath));

    private static string Normalize(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Member(string source, string signature)
    {
        var normalized = Normalize(source);
        var start = normalized.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing member signature: {signature}");
        var brace = normalized.IndexOf('{', start);
        Assert.True(brace > start);
        var depth = 0;
        for (var i = brace; i < normalized.Length; i++)
        {
            if (normalized[i] == '{')
            {
                depth++;
            }
            else if (normalized[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return normalized[start..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces for {signature}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
