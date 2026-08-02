using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class PlainItemLeaderPresentationSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Policy_ExposesItemNumberHeightAndResolvedStyleContract()
    {
        var policy = PolicySource();

        Assert.Contains("public static bool TryPrepare(", policy);
        Assert.Contains("presentationContext.ItemNumberModelHeight", policy);
        Assert.Contains(
            "presentationContext.ResolvedTextStyleId",
            policy);
        Assert.Contains("ObjectId TextStyleId", policy);
        Assert.Contains("ResolvedTextStyleName", policy);
        Assert.Contains("ItemNumberPaperHeightMm", policy);
        Assert.Contains("AnnotationScaleDenominator", policy);
        Assert.Contains("ResolutionKind", policy);
        Assert.Contains("IsFallback", policy);
        Assert.Contains("HasExplicitTextSettings", policy);
        Assert.Contains("HasCompatibleStyle", policy);
        Assert.Contains(
            "IsValidItemNumberPaperHeightMm",
            policy);
        Assert.DoesNotContain("database.Textstyle =", policy);
        Assert.DoesNotContain("database.Textstyle", policy);
        Assert.DoesNotContain("LabelAndDimensionModelHeight", policy);
    }

    [Fact]
    public void Policy_FailsOnNullContextNoCompatibleStyleAndDatabaseMismatch()
    {
        var policy = PolicySource();

        Assert.Contains(
            "requires an annotation presentation context",
            policy);
        Assert.Contains("has no compatible text style", policy);
        Assert.Contains("different database", policy);
        Assert.Contains("null", policy);
        Assert.Contains("or erased", policy);
        Assert.Contains("outside the contract range", policy);
    }

    [Theory]
    [InlineData(50, 135d)]
    [InlineData(100, 270d)]
    public void DefaultItemNumberHeights_MatchLegacyTypographyParity(
        int denominator,
        double expectedHeightMm)
    {
        var fromSettings =
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                TimberAnnotationTextSettingsRules.DefaultItemNumberPaperHeightMm,
                denominator);
        var fromLegacy =
            TimberItemNumberTypographyRules.CalculateTextHeightMm(
                denominator /
                (double)TimberAnnotationScaleRules.DefaultDenominator);

        Assert.Equal(expectedHeightMm, fromSettings);
        Assert.Equal(expectedHeightMm, fromLegacy);
        Assert.Equal(
            expectedHeightMm,
            TimberAnnotationTextSettingsRules.DefaultItemNumberPaperHeightMm *
            denominator);
    }

    [Theory]
    [InlineData(3d, 50, 150d)]
    [InlineData(2d, 100, 200d)]
    public void ExplicitPaperHeight_ScalesByDenominator(
        double paperHeightMm,
        int denominator,
        double expectedHeightMm)
    {
        Assert.Equal(
            expectedHeightMm,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                paperHeightMm,
                denominator));
    }

    [Fact]
    public void ProductionNativePath_IsNotWiredToPlainItemLeaderPolicy()
    {
        var labels = ElementLabelSource();
        var styleService = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AcKrovyMLeaderStyleService.cs");

        Assert.DoesNotContain(
            "AutoCadPlainItemLeaderPresentationPolicy",
            labels);
        Assert.DoesNotContain(
            "AutoCadPlainItemLeaderPresentationPreparation",
            labels);
        Assert.Contains(
            "text.TextStyleId = database.Textstyle",
            Member(labels, "private static MText CreateLeaderMText("));
        Assert.Contains(
            "leader.TextStyleId = database.Textstyle",
            Member(styleService, "public static void ApplyInstanceProperties("));
        Assert.Contains(
            "CreateLeaderMText(",
            Member(labels, "private static MLeader CreateNativeMLeader("));
        Assert.Contains(
            "CreateLeaderMText(",
            Member(labels, "private static bool TryUpdateNativeLeader("));
        Assert.DoesNotContain(
            "AutoCadPlainItemLeaderPresentationPolicy.TryPrepare(",
            Member(labels, "private static bool UpsertLeader("));
    }

    [Fact]
    public void FramedAndFullLabelPaths_RemainOutsidePlainItemLeaderPolicy()
    {
        var policy = PolicySource();
        var labels = ElementLabelSource();

        Assert.DoesNotContain("AcKrovyItemLeaderBlockVariantService", policy);
        Assert.DoesNotContain("CreateBlockMLeader", policy);
        Assert.DoesNotContain("LabelAndDimensionModelHeight", policy);
        Assert.Contains(
            "AutoCadFullLabelPresentationPolicy.TryPrepare(",
            labels);
        Assert.Contains(
            "PrepareFramedItemLeaderVariant(",
            labels);
    }

    private static string PolicySource() =>
        Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadPlainItemLeaderPresentationPolicy.cs");

    private static string ElementLabelSource() =>
        Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "ElementLabelService.cs");

    private static string Member(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing member {signature}");
        var brace = source.IndexOf('{', start);
        Assert.True(brace >= 0);
        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces for {signature}");
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(
            new[] { RepositoryRoot }.Concat(parts).ToArray()));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("Repository root not found.");
    }
}
