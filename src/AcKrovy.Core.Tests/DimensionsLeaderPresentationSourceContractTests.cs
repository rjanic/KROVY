using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class DimensionsLeaderPresentationSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Policy_ExposesLabelAndDimensionHeightAndResolvedStyleContract()
    {
        var policy = PolicySource();

        Assert.Contains("public static bool TryPrepare(", policy);
        Assert.Contains(
            "presentationContext.LabelAndDimensionModelHeight",
            policy);
        Assert.Contains(
            "presentationContext.ResolvedTextStyleId",
            policy);
        Assert.Contains("ObjectId TextStyleId", policy);
        Assert.Contains("ResolvedTextStyleName", policy);
        Assert.Contains("LabelAndDimensionPaperHeightMm", policy);
        Assert.Contains("AnnotationScaleDenominator", policy);
        Assert.Contains("ResolutionKind", policy);
        Assert.Contains("IsFallback", policy);
        Assert.Contains("HasExplicitTextSettings", policy);
        Assert.Contains("HasCompatibleStyle", policy);
        Assert.Contains(
            "IsValidDimensionPaperHeightMm",
            policy);
        Assert.DoesNotContain("database.Textstyle =", policy);
        Assert.DoesNotContain("database.Textstyle", policy);
        Assert.DoesNotContain("ItemNumberModelHeight", policy);
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
    [InlineData(50, 125d)]
    [InlineData(100, 250d)]
    public void DefaultLabelAndDimensionHeights_MatchLegacyTypographyParity(
        int denominator,
        double expectedHeightMm)
    {
        var fromSettings =
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
                denominator);
        var fromLegacy =
            TimberDimensionTypographyRules.CalculateTextHeightMm(
                denominator /
                (double)TimberAnnotationScaleRules.DefaultDenominator);

        Assert.Equal(expectedHeightMm, fromSettings);
        Assert.Equal(expectedHeightMm, fromLegacy);
        Assert.Equal(
            expectedHeightMm,
            TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm *
            denominator);
    }

    [Theory]
    [InlineData(3d, 50, 150d)]
    [InlineData(2d, 100, 200d)]
    public void ExplicitPaperHeight_ScalesByDenominatorWithoutScaleFactor(
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
    public void StandaloneDimensionsLeader_IsWiredBeforeForWrite()
    {
        var upsert = Member(
            ElementLabelSource(),
            "private static bool UpsertLeader(");
        var create = Member(
            ElementLabelSource(),
            "private static MLeader CreateNativeMLeader(");
        var update = Member(
            ElementLabelSource(),
            "private static bool TryUpdateNativeLeader(");
        var styleService = Member(
            Source(
                "src", "AcKrovy.AutoCAD", "Infrastructure",
                "AcKrovyMLeaderStyleService.cs"),
            "public static void ApplyInstanceProperties(");
        var mText = Member(
            ElementLabelSource(),
            "private static MText CreateLeaderMText(");

        var prepare = upsert.IndexOf(
            "AutoCadDimensionsLeaderPresentationPolicy.TryPrepare(",
            StringComparison.Ordinal);
        var failure = upsert.IndexOf(
            "return false;",
            prepare,
            StringComparison.Ordinal);
        var existingWrite = upsert.IndexOf(
            "transaction.GetObject(existingId, OpenMode.ForWrite",
            StringComparison.Ordinal);
        Assert.True(prepare >= 0 && failure >= 0 && existingWrite >= 0);
        Assert.True(prepare < failure && failure < existingWrite);
        Assert.Contains("usesDimensionsLeaderPresentation", upsert);
        Assert.Contains(
            "TimberAnnotationMode.DimensionsLeader",
            upsert);
        Assert.Contains(
            "TimberMainAnnotationComponentRole.Primary",
            upsert);
        Assert.Contains("\"DimensionsLeaderPresentation\"", upsert);
        Assert.Contains(
            "dimensionsLeaderPresentation.ModelHeightMm",
            create);
        Assert.Contains(
            "dimensionsLeaderPresentation.ModelHeightMm",
            update);
        Assert.DoesNotContain(
            "dimensionsLeaderPresentation.ModelHeightMm *",
            create + update);
        Assert.Contains(
            "dimensionsLeaderPresentation?.TextStyleId",
            create);
        Assert.Contains(
            "dimensionsLeaderPresentation?.TextStyleId",
            update);
        Assert.Contains(
            "resolvedTextStyleId ?? database.Textstyle",
            mText);
        Assert.Contains(
            "resolvedTextStyleId ?? database.Textstyle",
            styleService);
        Assert.DoesNotContain("database.Textstyle =", ElementLabelSource());
        Assert.DoesNotContain(
            "database.Textstyle =",
            Source(
                "src", "AcKrovy.AutoCAD", "Infrastructure",
                "AcKrovyMLeaderStyleService.cs"));
    }

    [Fact]
    public void SharedNativeApi_RemainsOptInAndPreservesPlainItemPaths()
    {
        var upsert = Member(
            ElementLabelSource(),
            "private static bool UpsertLeader(");
        var create = Member(
            ElementLabelSource(),
            "private static MLeader CreateNativeMLeader(");
        var update = Member(
            ElementLabelSource(),
            "private static bool TryUpdateNativeLeader(");
        var combined = Member(
            ElementLabelSource(),
            "private static bool UpsertCombinedLeader(");

        Assert.Contains(
            "AutoCadDimensionsLeaderPresentationPreparation? dimensionsLeaderPresentation =",
            create);
        Assert.Contains(
            "AutoCadDimensionsLeaderPresentationPreparation? dimensionsLeaderPresentation =",
            update);
        Assert.Contains("= null", create);
        Assert.Contains("= null", update);
        Assert.Contains("usesPlainItemPresentation", upsert);
        Assert.Contains(
            "AutoCadPlainItemLeaderPresentationPolicy.TryPrepare(",
            upsert);
        Assert.Contains(
            "AutoCadPlainItemLeaderPresentationPolicy.TryPrepare(",
            combined);
        Assert.DoesNotContain(
            "AutoCadDimensionsLeaderPresentationPolicy.TryPrepare(",
            combined);
        Assert.DoesNotContain(
            "LabelAndDimensionModelHeight",
            combined);
        Assert.Contains(
            "baseTextHeightMm * presentationScaleFactor",
            create);
        Assert.Contains(
            "baseTextHeightMm * presentationScaleFactor",
            update);
    }

    [Fact]
    public void CombinedPrimaryDimensionsMText_RemainsOutsideDimensionsLeaderPolicy()
    {
        var combined = Member(
            ElementLabelSource(),
            "private static bool UpsertCombinedLeader(");
        var primary = combined.IndexOf(
            "var primaryCreated = UpsertLabel(",
            StringComparison.Ordinal);
        Assert.True(primary >= 0);
        var call = ExtractInvocation(combined, primary);
        Assert.DoesNotContain("resolvedTextStyleId", call);
        Assert.Contains(
            "TimberCombinedDimensionTypographyRules.CalculateTextHeightMm(",
            combined);
        Assert.DoesNotContain(
            "AutoCadDimensionsLeaderPresentationPolicy",
            combined);
    }

    [Fact]
    public void Replacement_RemainsCreateBeforeErase()
    {
        var upsert = Member(
            ElementLabelSource(),
            "private static bool UpsertLeader(");
        var create = upsert.IndexOf(
            "CreateNativeMLeader(",
            StringComparison.Ordinal);
        var erase = upsert.IndexOf(
            "EraseMainAnnotation(",
            StringComparison.Ordinal);
        Assert.True(create >= 0 && erase >= 0 && create < erase);
        Assert.Contains("WriteLeaderMetadata(", upsert);
        Assert.Contains("SourceHandle", upsert);
    }

    [Fact]
    public void FramedFullLabelAndItemPaths_RemainOutsideDimensionsLeaderPolicy()
    {
        var policy = PolicySource();
        var labels = ElementLabelSource();

        Assert.DoesNotContain("AcKrovyItemLeaderBlockVariantService", policy);
        Assert.DoesNotContain("CreateBlockMLeader", policy);
        Assert.DoesNotContain("ItemNumberModelHeight", policy);
        Assert.Contains(
            "AutoCadFullLabelPresentationPolicy.TryPrepare(",
            labels);
        Assert.Contains(
            "AutoCadPlainItemLeaderPresentationPolicy.TryPrepare(",
            labels);
        Assert.Contains(
            "PrepareFramedItemLeaderVariant(",
            labels);
    }

    private static string PolicySource() =>
        Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadDimensionsLeaderPresentationPolicy.cs");

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

    private static string ExtractInvocation(string source, int start)
    {
        var depth = 0;
        var began = false;
        for (var i = start; i < source.Length; i++)
        {
            var ch = source[i];
            if (ch == '(')
            {
                began = true;
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
                if (began && depth == 0)
                {
                    return source[start..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException("Unbalanced invocation.");
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
