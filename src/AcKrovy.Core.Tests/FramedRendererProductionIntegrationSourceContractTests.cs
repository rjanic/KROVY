using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class FramedRendererProductionIntegrationSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ProductionFramedPath_UsesG4CompositeBeforeLegacyBlockLeaderMutation()
    {
        var member = Member(
            ElementLabelSource(),
            "private static bool UpsertLeader(");

        var g4 = member.IndexOf(
            "AutoCadFramedG4CompositePolicy.UsesG4Composite(",
            StringComparison.Ordinal);
        var prepare = member.IndexOf(
            "AutoCadFramedG4CompositeService.TryPrepare(",
            StringComparison.Ordinal);
        var upsert = member.IndexOf(
            "AutoCadFramedG4CompositeService.TryUpsert(",
            StringComparison.Ordinal);
        var legacyWrite = member.IndexOf(
            "TryUpdateBlockLeader(",
            StringComparison.Ordinal);

        Assert.True(g4 >= 0 && prepare > g4 && upsert > prepare);
        Assert.True(legacyWrite < 0 || upsert < legacyWrite);
        Assert.Contains("ContentType.NoneContent", G4ServiceSource());
        Assert.Contains("new DBText()", G4ServiceSource());
        Assert.DoesNotContain(
            "leader.BlockContentId = preparation.BlockTableRecordId",
            member.Substring(g4, Math.Min(1200, member.Length - g4)));
    }

    [Fact]
    public void G4FrameDefinitions_ContainNoAttributeDefinition()
    {
        var service = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AcKrovyItemLeaderFrameOnlyBlockService.cs");

        Assert.Contains("AddFrameGeometry(", service);
        Assert.DoesNotContain("AddItemNumberAttribute(", service);
        Assert.Contains(
            "G4 frame-only definitions must not contain AttributeDefinition",
            service);
        Assert.Contains("AK_ITEM_FRAME_", Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadItemLeaderFrameOnlyBlockKey.cs"));
        Assert.Contains("GeometryVersion = 4", Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadItemLeaderFrameOnlyBlockKey.cs"));
    }

    [Fact]
    public void G4Migration_ErasesLegacyBlockLeadersWithoutMutatingDefinitions()
    {
        var service = G4ServiceSource();

        Assert.Contains("IsLegacyG2G3BlockLeader(", service);
        Assert.Contains("ContentType.BlockContent", service);
        Assert.Contains("EraseEntityIfPresent(transaction, legacyId)", service);
        Assert.DoesNotContain("Purge(", service);
        Assert.DoesNotContain("AddItemNumberAttribute(", service);
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
        Assert.Contains("hasG4ItemCode", combined);
        Assert.Contains("!framedCreated", combined);
        Assert.Contains("framedOk", combined);
    }

    [Fact]
    public void ReadAnnotationEntities_IncludesG4ItemCodeDbText()
    {
        var member = Member(
            ElementLabelSource(),
            "private static IReadOnlyList<AnnotationEntityEntry> ReadAnnotationEntities(");

        Assert.Contains("DBText", member);
        Assert.Contains("MainAnnotationEntityType.DBText", member);
    }

    [Fact]
    public void ProductionSizing_UsesResolveNotMeasuredWidth_ForG4Frames()
    {
        var frameService = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AcKrovyItemLeaderFrameOnlyBlockService.cs");
        var key = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadItemLeaderFrameOnlyBlockKey.cs");
        var g3Key = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadItemLeaderBlockVariantKey.cs");

        Assert.Contains("TimberItemLeaderBlockDefinitionRules.Resolve(", frameService);
        Assert.DoesNotContain("AutoCadItemLeaderTextMeasurementService.Measure(", frameService);
        Assert.DoesNotContain("EvaluateMeasuredTextWidth(", frameService);
        Assert.DoesNotContain("MeasuredTextWidth", key);
        Assert.Contains("GeometryVersion = 4", key);
        Assert.Contains("CurrentGeometryVersion = 3", g3Key);
    }

    [Fact]
    public void G4PrepareFailure_ReturnsBeforeCompositeMutation()
    {
        var upsert = Member(ElementLabelSource(),
            "private static bool UpsertLeader(");
        var preparation = upsert.IndexOf(
            "AutoCadFramedG4CompositeService.TryPrepare(",
            StringComparison.Ordinal);
        var failureReturn = upsert.IndexOf(
            "if (g4Preparation is null)",
            preparation,
            StringComparison.Ordinal);
        var mutate = upsert.IndexOf(
            "AutoCadFramedG4CompositeService.TryUpsert(",
            StringComparison.Ordinal);

        Assert.True(preparation >= 0 && preparation < failureReturn &&
            failureReturn < mutate);
        Assert.Contains("return false;", upsert.Substring(
            failureReturn,
            mutate - failureReturn));
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
            "new AutoCadItemLeaderBlockVariantBatchCatalog(database)",
            context);
        Assert.Contains(
            "new AutoCadItemLeaderFrameOnlyBlockBatchCatalog(database)",
            context);
        Assert.Contains("ItemLeaderVariantCatalog", orchestration);
        Assert.DoesNotContain(
            "static AutoCadItemLeaderBlockVariantBatchCatalog",
            context);
        Assert.DoesNotContain(
            "static AutoCadItemLeaderFrameOnlyBlockBatchCatalog",
            context);
    }

    [Fact]
    public void LabelMetadataSchema_BumpsToFourWithoutTimberElementSchemaBump()
    {
        var labelStore = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "ElementLabelStore.cs");
        var elementSchema = Source(
            "src", "AcKrovy.Core", "Models",
            "TimberElementDataSchema.cs");
        var policy = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadFramedG4CompositePolicy.cs");

        Assert.Contains("public int SchemaVersion { get; init; } = 4;", labelStore);
        Assert.Contains("AnnotationGroupId", labelStore);
        Assert.Contains("RendererGeneration", labelStore);
        Assert.Contains("LabelMetadataSchemaVersion = 4", policy);
        Assert.Contains("CurrentVersion = 7", elementSchema);
    }

    private static string G4ServiceSource() =>
        Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadFramedG4CompositeService.cs");

    private static string ElementLabelSource() =>
        Source("src", "AcKrovy.AutoCAD", "Infrastructure", "ElementLabelService.cs");

    private static string Member(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing member signature: {signature}");
        var brace = source.IndexOf('{', start);
        Assert.True(brace > start);
        var depth = 0;
        for (var index = brace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces for {signature}");
    }

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(new[] { RepositoryRoot }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), path);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ACAD_krovy.sln")) ||
                File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
