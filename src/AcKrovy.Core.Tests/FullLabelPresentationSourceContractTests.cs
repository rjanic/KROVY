using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class FullLabelPresentationSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void FullLabelPaths_PreparePresentationBeforeAnyForWriteMutation()
    {
        var element = Member(
            ElementLabelSource(),
            "public static bool UpsertForElement(");
        var post = Member(
            ElementLabelSource(),
            "public static bool UpsertForPostFootprint(");
        var upsert = Member(
            ElementLabelSource(),
            "private static bool UpsertLabel(");

        foreach (var path in new[] { element, post })
        {
            var prepare = path.IndexOf(
                "AutoCadFullLabelPresentationPolicy.TryPrepare(",
                StringComparison.Ordinal);
            var failure = path.IndexOf(
                "return false;",
                prepare,
                StringComparison.Ordinal);
            var upsertCall = path.IndexOf(
                "return UpsertLabel(",
                prepare,
                StringComparison.Ordinal);

            Assert.True(prepare >= 0 && failure >= 0 && upsertCall >= 0);
            Assert.True(prepare < failure && failure < upsertCall);
        }

        var create = upsert.IndexOf("new MText()", StringComparison.Ordinal);
        var modelWrite = upsert.IndexOf(
            "OpenMode.ForWrite",
            StringComparison.Ordinal);
        var existingWrite = upsert.IndexOf(
            "transaction.GetObject(existingLabelId, OpenMode.ForWrite",
            StringComparison.Ordinal);
        Assert.True(create >= 0 && modelWrite >= 0 && existingWrite >= 0);
    }

    [Fact]
    public void FullLabel_SetsExplicitTextStyleIdAndModelHeightFromPresentation()
    {
        var element = Member(
            ElementLabelSource(),
            "public static bool UpsertForElement(");
        var appearance = Member(
            ElementLabelSource(),
            "private static void ApplyLabelAppearance(");
        var policy = PolicySource();

        Assert.Contains(
            "resolvedTextStyleId: fullLabelPresentation.TextStyleId",
            element);
        Assert.Contains(
            "fullLabelPresentation.ModelHeightMm",
            element);
        Assert.Contains("label.TextHeight = textHeightMm", appearance);
        Assert.Contains(
            "label.TextStyleId = textStyleId",
            appearance);
        Assert.Contains(
            "presentationContext.LabelAndDimensionModelHeight",
            policy);
        Assert.Contains(
            "presentationContext.ResolvedTextStyleId",
            policy);
        Assert.Contains("ObjectId TextStyleId", policy);
        Assert.Contains("ResolvedTextStyleName", policy);
        Assert.Contains("ModelHeightMm", policy);
        Assert.Contains("HasExplicitTextSettings", policy);
        Assert.Contains(
            "public static bool TryPrepare(",
            policy);
        Assert.DoesNotContain("database.Textstyle =", ElementLabelSource());
        Assert.DoesNotContain("database.Textstyle =", policy);
    }

    [Fact]
    public void CombinedPrimaryMText_DoesNotReceiveResolvedFullLabelStyle()
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
    }

    [Fact]
    public void NativeAndFramedLeaderPaths_RemainOutsideFullLabelPresentation()
    {
        var element = Member(
            ElementLabelSource(),
            "public static bool UpsertForElement(");

        Assert.Contains("return UpsertLeader(", element);
        Assert.Contains("return UpsertCombinedLeader(", element);
        Assert.Equal(
            1,
            CountOccurrences(
                element,
                "AutoCadFullLabelPresentationPolicy.TryPrepare("));
        Assert.DoesNotContain(
            "AcKrovyItemLeaderBlockVariantService",
            PolicySource());
        Assert.DoesNotContain(
            "CreateBlockMLeader",
            PolicySource());
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(
                   value,
                   index,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    [Fact]
    public void FailurePreservesExistingAnnotationAndNeverChangesDatabaseTextstyle()
    {
        var policy = PolicySource();
        var element = Member(
            ElementLabelSource(),
            "public static bool UpsertForElement(");

        Assert.Contains("HasCompatibleStyle", policy);
        Assert.Contains("has no compatible text style", policy);
        Assert.Contains("different database", policy);
        Assert.Contains("return false;", element);
        Assert.Contains("\"FullLabelPresentation\"", element);
        Assert.Contains("AcKrovyDiagnostics.Warning(", element);
        Assert.DoesNotContain("database.Textstyle =", ElementLabelSource());
        Assert.DoesNotContain(
            "database.Textstyle",
            PolicySource());
    }

    [Fact]
    public void DefaultContractHeights_RemainCompatibleWithLegacyTypography()
    {
        Assert.Equal(
            125d,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                TimberAnnotationTextSettingsRules
                    .DefaultLabelAndDimensionPaperHeightMm,
                50));
        Assert.Equal(
            250d,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                TimberAnnotationTextSettingsRules
                    .DefaultLabelAndDimensionPaperHeightMm,
                100));
        Assert.Equal(
            125d,
            TimberDimensionTypographyRules.CalculateTextHeightMm(1d));
        Assert.Equal(
            250d,
            TimberDimensionTypographyRules.CalculateTextHeightMm(2d));
    }

    [Fact]
    public void CopyWblockAndSourceHandleLifecycle_RemainUnchanged()
    {
        var store = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure", "ElementLabelStore.cs");
        var service = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "TimberAnnotationService.cs");

        Assert.Contains("SourceHandle", store);
        Assert.Contains("ElementLabelStore.Write(", ElementLabelSource());
        Assert.Contains(
            "presentationBatchContext.ResolveForElement(data)",
            service);
        Assert.Contains(
            "ElementLabelService.UpsertForElement(",
            service);
        Assert.Contains(
            "ElementLabelService.UpsertForPostFootprint(",
            service);
    }

    private static string ElementLabelSource() =>
        Source("src", "AcKrovy.AutoCAD", "Infrastructure",
            "ElementLabelService.cs");

    private static string PolicySource() =>
        Source("src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadFullLabelPresentationPolicy.cs");

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
                depth++;
                began = true;
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
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot }.Concat(parts).ToArray()));

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
