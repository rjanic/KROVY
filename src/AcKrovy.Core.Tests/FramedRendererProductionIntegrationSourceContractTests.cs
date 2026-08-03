using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class FramedRendererProductionIntegrationSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ProductionFramedPath_PreparesVariantBeforeOpeningExistingLeaderForWrite()
    {
        var member = Member(
            ElementLabelSource(),
            "private static bool UpsertLeader(");
        var ensure = member.IndexOf(
            "PrepareFramedItemLeaderVariant(",
            StringComparison.Ordinal);
        var existingWrite = member.IndexOf(
            "transaction.GetObject(existingId, OpenMode.ForWrite",
            StringComparison.Ordinal);

        Assert.True(ensure >= 0 && ensure < existingWrite);
        Assert.Contains("if (framedPreparation is null)\n            {\n                return false;", member);
        Assert.Contains("TimberMainAnnotationRepresentation.BlockLeader", member);
        Assert.Contains("AutoCadFramedItemLeaderRendererPolicy.UsesImmutableVariant(", member);
    }

    [Fact]
    public void ProductionFramedPath_UsesVariantThenScaleThenItemToken()
    {
        var create = Member(ElementLabelSource(),
            "private static MLeader CreateBlockMLeader(");
        var content = create.IndexOf(
            "leader.BlockContentId = preparation.BlockTableRecordId",
            StringComparison.Ordinal);
        var scale = create.IndexOf(
            "leader.BlockScale = new Scale3d(presentationScaleFactor)",
            StringComparison.Ordinal);
        var token = create.IndexOf(
            "SetItemNumberBlockAttribute(",
            StringComparison.Ordinal);

        Assert.True(content >= 0 && content < scale && scale < token);
        Assert.DoesNotContain("AcKrovyItemLeaderBlockService.Ensure(", create);
        Assert.DoesNotContain(
            "(AttributeDefinition)transaction.GetObject",
            create);
        Assert.Contains("attribute.Height = preparation.AttributeHeightMm", ElementLabelSource());
        Assert.Contains("attribute.TextStyleId = preparation.TextStyleId", ElementLabelSource());
    }

    [Fact]
    public void ExistingAndLegacyBlockLeaders_AreMigratedWithoutDefinitionMutationOrPurge()
    {
        var update = Member(ElementLabelSource(),
            "private static bool TryUpdateBlockLeader(");

        Assert.Contains("leader.BlockContentId = preparation.BlockTableRecordId", update);
        Assert.Contains("mutationPlan.ShouldSetItemNumberToken", update);
        Assert.Contains("SetItemNumberBlockAttribute(", update);
        Assert.DoesNotContain(
            "(BlockTableRecord)transaction.GetObject",
            update);
        Assert.DoesNotContain("OpenMode.ForWrite", update);
        Assert.DoesNotContain("Erase(", update);
        Assert.DoesNotContain("Purge(", update);
        Assert.DoesNotContain("database.Textstyle", update);
    }

    [Fact]
    public void CombinedFailure_ReturnsBeforeChangingTheDimensionsComponent()
    {
        var combined = Member(ElementLabelSource(),
            "private static bool UpsertCombinedLeader(");
        var failure = combined.IndexOf(
            "if (framedVariantResult is { Succeeded: false })",
            StringComparison.Ordinal);
        var dimensions = combined.IndexOf(
            "CalculateCombinedDimensionsTextPlacement(",
            StringComparison.Ordinal);
        var primary = combined.IndexOf(
            "var primaryCreated = UpsertLabel(",
            StringComparison.Ordinal);

        Assert.True(failure >= 0 && failure < dimensions && dimensions < primary);
        Assert.Contains("return false;", combined.Substring(failure,
            dimensions - failure));
    }

    [Fact]
    public void ProductionSizing_UsesResolveNotMeasuredWidth()
    {
        var measurement = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadItemLeaderTextMeasurementService.cs");
        var variant = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AcKrovyItemLeaderBlockVariantService.cs");
        var key = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadItemLeaderBlockVariantKey.cs");

        Assert.Contains("using var text = new DBText()", measurement);
        Assert.Contains("TimberItemLeaderBlockDefinitionRules.Resolve(", variant);
        Assert.DoesNotContain("AutoCadItemLeaderTextMeasurementService.Measure(", variant);
        Assert.DoesNotContain("EvaluateMeasuredTextWidth(", variant);
        Assert.DoesNotContain("MeasuredTextWidth", key);
        Assert.DoesNotContain("AvailableInnerWidth", key);
        Assert.Contains("CurrentGeometryVersion = 2", key);
    }

    [Fact]
    public void OverflowReturnsBeforeExistingAnnotationIsOpenedForWrite()
    {
        var upsert = Member(ElementLabelSource(),
            "private static bool UpsertLeader(");
        var preparation = upsert.IndexOf(
            "PrepareFramedItemLeaderVariant(",
            StringComparison.Ordinal);
        var failureReturn = upsert.IndexOf(
            "if (framedPreparation is null)",
            preparation,
            StringComparison.Ordinal);
        var existingWrite = upsert.IndexOf(
            "transaction.GetObject(existingId, OpenMode.ForWrite",
            StringComparison.Ordinal);

        Assert.True(preparation >= 0 && preparation < failureReturn &&
            failureReturn < existingWrite);
        Assert.Contains("return false;", upsert.Substring(
            failureReturn,
            existingWrite - failureReturn));
    }

    [Fact]
    public void BatchCatalog_IsDatabaseBoundPerOrchestrationBatchAndNeverStatic()
    {
        var context = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadAnnotationPresentationContext.cs");
        var orchestration = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "TimberAnnotationService.cs");

        Assert.Contains(
            "public AutoCadItemLeaderBlockVariantBatchCatalog ItemLeaderVariantCatalog",
            context);
        Assert.Contains(
            "new AutoCadItemLeaderBlockVariantBatchCatalog(database)",
            context);
        Assert.DoesNotContain(
            "static AutoCadItemLeaderBlockVariantBatchCatalog",
            context);
        Assert.Contains(
            "presentationBatchContext.ItemLeaderVariantCatalog",
            orchestration);
        Assert.Contains(
            "presentationBatchContext.ResolveForElement(data)",
            orchestration);
    }

    [Fact]
    public void ScaleUsesCentralAuthorityAndDenominatorNeverEntersVariantKey()
    {
        var policy = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadFramedItemLeaderRendererPolicy.cs");
        var key = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadItemLeaderBlockVariantKey.cs");

        Assert.Contains("return annotationScaleContext.ScaleFactor;", policy);
        Assert.DoesNotContain("BaseDenominator", key);
        Assert.DoesNotContain("AnnotationScaleDenominator", key);
        Assert.DoesNotContain("ScaleFactor", key);
        Assert.Contains("CurrentGeometryVersion = 2", key);
    }

    [Fact]
    public void NonFramedProductionServices_RemainOutsideVariantIntegration()
    {
        string[] files =
        [
            "SlopeAnnotationService.cs",
            "SlopeAngleTextService.cs",
            "PostFootprintPerpendicularAnnotationService.cs",
            "AcKrovyMLeaderStyleService.cs",
        ];
        foreach (var file in files)
        {
            var source = Source(
                "src", "AcKrovy.AutoCAD", "Infrastructure", file);
            Assert.DoesNotContain("ItemLeaderBlockVariant", source);
        }

        var policy = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadFramedItemLeaderRendererPolicy.cs");
        Assert.Contains("TimberAnnotationModeRules.IsFramedItemLeader", policy);
        Assert.Contains("TimberMainAnnotationComponentRole.FramedItem", policy);
    }

    [Fact]
    public void ProtectedVersionsAndSettingsSurfacesRemainUnchanged()
    {
        Assert.Contains("<AcKrovyVersion>0.22.0</AcKrovyVersion>",
            Source("Directory.Build.props"));
        Assert.Contains("public const int CurrentVersion = 6;",
            Source("src", "AcKrovy.Core", "Models", "TimberElementDataSchema.cs"));
        Assert.Contains("public const int CurrentVersion = 2;",
            Source("src", "AcKrovy.Core", "Models", "TimberElementDefaultProfile.cs"));

        var changedProductionFiles = new[]
        {
            "LayerSettingsWindow.xaml",
            "LayerSettingsWindow.xaml.cs",
        };
        Assert.All(changedProductionFiles, file =>
            Assert.DoesNotContain(
                "ItemLeaderBlockVariant",
                Source("src", "AcKrovy.AutoCAD", "UI", file)));
    }

    [Fact]
    public void MemberExtraction_IsLineEndingAgnostic()
    {
        const string lf = "private static bool Example()\n{\n    return true;\n}\n";
        var expected = Member(lf, "private static bool Example(");
        Assert.Equal(expected, Member(lf.Replace("\n", "\r\n"),
            "private static bool Example("));
        Assert.Equal(expected, Member(lf.Replace("\n", "\r"),
            "private static bool Example("));
    }

    private static string ElementLabelSource() => Source(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "ElementLabelService.cs");

    private static string Source(params string[] segments) =>
        Normalize(File.ReadAllText(Path.Combine([RepositoryRoot, .. segments])));

    private static string Member(string source, string declarationPrefix)
    {
        source = Normalize(source);
        declarationPrefix = Normalize(declarationPrefix);
        var start = source.IndexOf(declarationPrefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Member not found: {declarationPrefix}");
        var brace = source.IndexOf('{', start);
        Assert.True(brace >= 0, $"Opening brace not found: {declarationPrefix}");
        var depth = 0;
        for (var index = brace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source.Substring(start, index - start + 1);
            }
        }
        throw new InvalidOperationException($"Closing brace not found: {declarationPrefix}");
    }

    private static string Normalize(string source) =>
        source.Replace("\r\n", "\n").Replace("\r", "\n");

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
