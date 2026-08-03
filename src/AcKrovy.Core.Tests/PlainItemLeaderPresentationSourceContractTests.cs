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
        Assert.Contains(
            "TimberAnnotationTextRole.ItemCode",
            policy);
        Assert.Contains("presentationContext.ForRole(Role)", policy);
        Assert.Contains("roleText.ModelHeightMm", policy);
        Assert.Contains("roleText.PaperHeightMm", policy);
        Assert.Contains("roleText.ResolvedTextStyleId", policy);
        Assert.DoesNotContain("TimberAnnotationTextRole.Dimension", policy);
        Assert.DoesNotContain("TimberAnnotationTextRole.Slope", policy);
        Assert.Contains("ObjectId TextStyleId", policy);
        Assert.Contains("ResolvedTextStyleName", policy);
        Assert.Contains("ItemNumberPaperHeightMm", policy);
        Assert.Contains("AnnotationScaleDenominator", policy);
        Assert.Contains("ResolutionKind", policy);
        Assert.Contains("IsFallback", policy);
        Assert.Contains("HasExplicitTextSettings", policy);
        Assert.Contains("HasCompatibleStyle", policy);
        Assert.Contains(
            "IsValidItemCodePaperHeightMm",
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
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
                denominator);
        var fromLegacy =
            TimberItemNumberTypographyRules.CalculateTextHeightMm(
                denominator /
                (double)TimberAnnotationScaleRules.DefaultDenominator);

        Assert.Equal(expectedHeightMm, fromSettings);
        Assert.Equal(expectedHeightMm, fromLegacy);
        Assert.Equal(
            expectedHeightMm,
            TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm *
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
    public void StandalonePlainItemNumberLeader_IsWiredBeforeForWrite()
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
            "AutoCadPlainItemLeaderPresentationPolicy.TryPrepare(",
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
        Assert.Contains("usesPlainItemPresentation", upsert);
        Assert.Contains(
            "TimberAnnotationMode.ItemNumberLeader",
            upsert);
        Assert.Contains("ItemNumberLeaderStyle.Plain", upsert);
        Assert.Contains(
            "TimberMainAnnotationComponentRole.Primary",
            upsert);
        Assert.Contains("preparedPlainItemPresentation", upsert);
        Assert.Contains("plainItemPresentation.ModelHeightMm", create);
        Assert.Contains("plainItemPresentation.ModelHeightMm", update);
        Assert.DoesNotContain(
            "plainItemPresentation.ModelHeightMm *",
            create + update);
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
    public void CombinedPlainItem_PreparesBeforeUpsertLeaderAndPreservesDimensionsOnFailure()
    {
        var combined = Member(
            ElementLabelSource(),
            "private static bool UpsertCombinedLeader(");
        var upsert = Member(
            ElementLabelSource(),
            "private static bool UpsertLeader(");

        var prepare = combined.IndexOf(
            "AutoCadPlainItemLeaderPresentationPolicy.TryPrepare(",
            StringComparison.Ordinal);
        var failure = combined.IndexOf(
            "return false;",
            prepare,
            StringComparison.Ordinal);
        var upsertLeader = combined.IndexOf(
            "UpsertLeader(",
            prepare,
            StringComparison.Ordinal);
        var upsertLabel = combined.IndexOf(
            "UpsertLabel(",
            upsertLeader,
            StringComparison.Ordinal);
        var deleteUnexpected = combined.IndexOf(
            "DeleteUnexpectedCompositeComponents(",
            upsertLabel,
            StringComparison.Ordinal);
        Assert.True(
            prepare >= 0 &&
            failure >= 0 &&
            upsertLeader >= 0 &&
            upsertLabel >= 0 &&
            deleteUnexpected >= 0);
        Assert.True(
            prepare < failure &&
            failure < upsertLeader &&
            upsertLeader < upsertLabel &&
            upsertLabel < deleteUnexpected);
        Assert.Contains("isCombinedPlainItem", combined);
        Assert.Contains("preparedPlainItemPresentation: plainItemPreparation", combined);
        Assert.Contains("combinedLandingDistanceMm: combinedLandingDistanceMm", combined);
        Assert.Contains(
            "CombinedFramedLandingDistanceMm *",
            combined);
        Assert.Contains("preserveCompositeSiblings: true", combined);
        Assert.Contains(
            "TimberMainAnnotationComponentRole.FramedItem",
            combined);
        Assert.Contains(
            "TimberMainAnnotationRepresentation.Leader",
            combined);
        Assert.Contains(
            "TimberAnnotationMode.DimensionsWithItemNumber",
            upsert);
        Assert.Contains(
            "TimberMainAnnotationComponentRole.FramedItem",
            upsert);
        Assert.Contains("usesPlainItemPresentation", upsert);
        Assert.Contains(
            "ResolveMainAnnotationRepresentation(",
            ElementLabelSource());
    }

    [Fact]
    public void DimensionsLeader_RemainsOnLegacyNativePath_AndFramedCombinedSkipsPlainPolicy()
    {
        var combined = Member(
            ElementLabelSource(),
            "private static bool UpsertCombinedLeader(");
        var upsertForElement = Member(
            ElementLabelSource(),
            "public static bool UpsertForElement(");
        var create = Member(
            ElementLabelSource(),
            "private static MLeader CreateNativeMLeader(");
        var update = Member(
            ElementLabelSource(),
            "private static bool TryUpdateNativeLeader(");

        Assert.Contains(
            "TimberAnnotationMode.DimensionsLeader",
            upsertForElement);
        Assert.Contains("return UpsertCombinedLeader(", upsertForElement);
        Assert.Contains(
            "TimberMainAnnotationRepresentation.BlockLeader",
            combined);
        Assert.Contains(
            "baseTextHeightMm * presentationScaleFactor",
            create);
        Assert.Contains(
            "baseTextHeightMm * presentationScaleFactor",
            update);
        Assert.Contains(
            "AutoCadPlainItemLeaderPresentationPreparation? plainItemPresentation = null",
            create);
        Assert.Contains(
            "AutoCadPlainItemLeaderPresentationPreparation? plainItemPresentation = null",
            update);
        Assert.DoesNotContain(
            "LabelAndDimensionModelHeight",
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
