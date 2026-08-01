using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class AnnotationTextAutoCadSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Resolver_ReadsTextStyleTableOnlyAndHasNoWriteOperations()
    {
        var source = ResolverSource();
        var readCatalog = Segment(
            source,
            "public static AutoCadTextStyleCatalog ReadCatalog(",
            "private AutoCadTextStyleResolution Resolve(");

        Assert.Contains("database.TextStyleTableId", readCatalog);
        Assert.Contains("OpenMode.ForRead", readCatalog);
        Assert.Equal(1, CountOccurrences(readCatalog, "database.TextStyleTableId"));
        Assert.DoesNotContain("OpenMode.ForWrite", source);
        Assert.DoesNotContain("UpgradeOpen", source);
        Assert.DoesNotContain("new TextStyleTableRecord", source);
        Assert.DoesNotContain("database.Textstyle =", source);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", source);
        Assert.DoesNotContain("Commit(", source);
        Assert.DoesNotContain("DocumentLock", source);
        Assert.DoesNotContain("MLeaderStyle", source);
    }

    [Fact]
    public void Resolver_UsesHostObjectIdOnlyInAutoCadCatalogAndResult()
    {
        var resolver = ResolverSource();
        var coreProductionFiles = Directory.GetFiles(
            Path.Combine(RepositoryRoot, "src", "AcKrovy.Core"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.Contains("ObjectId TextStyleId", resolver);
        Assert.Contains("ObjectId? ResolvedTextStyleId", resolver);
        Assert.All(coreProductionFiles, file =>
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("Autodesk", source);
            Assert.DoesNotContain("ObjectId", source);
            Assert.DoesNotContain("TextStyleId", source);
        });
    }

    [Fact]
    public void Resolver_FiltersRequiredCompatibilityProperties()
    {
        var source = ResolverSource();

        Assert.Contains("StringComparer.OrdinalIgnoreCase", source);
        Assert.Contains("descriptor.TextSize == 0d", source);
        Assert.Contains("!descriptor.IsAnnotative", source);
        Assert.Contains("descriptor.IsValid", source);
        Assert.Contains("!descriptor.IsErased", source);
        Assert.Contains("record.Annotative == AnnotativeStates.True", source);
        Assert.Contains("record.TextSize", source);
        Assert.Contains("record!.Name", source);
        Assert.Contains("id == currentStyleId", source);
        Assert.Contains("descriptor.TextStyleId.IsValid", source);
        Assert.Contains("!descriptor.TextStyleId.IsErased", source);
        Assert.Contains(
            "ReferenceEquals(descriptor.TextStyleId.Database, database)",
            source);
    }

    [Fact]
    public void PolicyAndHostResults_DoNotAllowIndependentContradictoryFlags()
    {
        var source = ResolverSource();

        Assert.Contains("private AutoCadTextStyleSelection(", source);
        Assert.Contains("private AutoCadTextStyleResolution(", source);
        Assert.Equal(
            2,
            CountOccurrences(source, "public bool IsFallback => ResolutionKind is"));
        Assert.Contains(
            "public bool HasCompatibleStyle => ResolvedTextStyleName is not null",
            source);
        Assert.Contains(
            "public bool HasCompatibleStyle => ResolvedTextStyleId.HasValue",
            source);
        Assert.DoesNotContain("bool isFallback", source);
        Assert.DoesNotContain("bool hasCompatibleStyle", source);
    }

    [Fact]
    public void PresentationContext_UsesCoreNormalizationAndHeightAuthority()
    {
        var source = PresentationSource();

        Assert.Contains("TimberAnnotationTextSettingsRules.NormalizeStored(", source);
        Assert.Contains("TimberAnnotationTextSettingsRules.Default", source);
        Assert.Equal(
            3,
            CountOccurrences(
                source,
                "TimberAnnotationTextSettingsRules.CalculateModelHeightMm("));
        Assert.DoesNotContain("const double", source);
        Assert.DoesNotContain("2.5d", source);
        Assert.DoesNotContain("2.7d", source);
        Assert.DoesNotContain("1.6d", source);
        Assert.DoesNotContain("PrepareForWrite", source);
        Assert.DoesNotContain("metadataStore.Write", source);
        Assert.DoesNotContain("defaultProfileStore", source);
        Assert.Contains("public string? RequestedTextStyleName { get; }", source);
        Assert.Contains(
            "RequestedTextStyleName = textStyleResolution.RequestedTextStyleName",
            source);
        Assert.Contains(
            "public bool IsFallback => _textStyleResolution.IsFallback",
            source);
        Assert.Contains(
            "public bool HasCompatibleStyle => _textStyleResolution.HasCompatibleStyle",
            source);
    }

    [Fact]
    public void HostCatalogAndContexts_AreBoundToOneDatabaseWithoutDbObjectLeaks()
    {
        var resolver = ResolverSource();
        var presentation = PresentationSource();
        var combined = resolver + presentation;

        Assert.Contains("public Database Database { get; }", resolver);
        Assert.Contains("public Database Database { get; }", presentation);
        Assert.Contains(
            "ReferenceEquals(textStyleResolver.Database, database)",
            presentation);
        Assert.Contains(
            "ReferenceEquals(textStyleCatalog.Database, database)",
            presentation);
        Assert.Contains("public void EnsureDatabase(Database database)", presentation);
        Assert.DoesNotContain("private readonly Transaction", combined);
        Assert.DoesNotContain("private readonly DBObject", combined);
        Assert.DoesNotContain("TextStyleTableRecord Record", combined);
        Assert.DoesNotContain("TextStyleTable Table", combined);
        Assert.Contains("var captured = descriptors", resolver);
        Assert.Contains(".ToArray();", resolver);
    }

    [Fact]
    public void BatchContext_LoadsDrawingScaleAndTextStyleCatalogOnce()
    {
        var source = PresentationSource();
        var create = Segment(
            source,
            "public static AutoCadAnnotationPresentationBatchContext Create(",
            "public AutoCadAnnotationPresentationContext ResolveForElement(");
        var resolve = Segment(
            source,
            "public AutoCadAnnotationPresentationContext ResolveForElement(",
            "}\n");

        Assert.Equal(
            1,
            CountOccurrences(create, "AutoCadAnnotationScaleService.Create("));
        Assert.Equal(
            1,
            CountOccurrences(create, "AutoCadTextStyleResolver.ReadCatalog("));
        Assert.DoesNotContain("ReadCatalog(", resolve);
        Assert.DoesNotContain("AutoCadAnnotationScaleService.Create(", resolve);
        Assert.DoesNotContain("static AutoCadTextStyleCatalog", source);
    }

    [Fact]
    public void RendererServices_AreNotConnectedToNewResolverOrPresentationContext()
    {
        string[] rendererFiles =
        [
            "ElementLabelService.cs",
            "TimberAnnotationService.cs",
            "SlopeAnnotationService.cs",
            "SlopeAngleTextService.cs",
            "AcKrovyMLeaderStyleService.cs",
            "AcKrovyItemLeaderBlockService.cs",
        ];

        foreach (var file in rendererFiles)
        {
            var source = Source(
                "src",
                "AcKrovy.AutoCAD",
                "Infrastructure",
                file);
            Assert.DoesNotContain("AutoCadTextStyleResolver", source);
            Assert.DoesNotContain("AutoCadAnnotationPresentationContext", source);
            Assert.DoesNotContain("AutoCadAnnotationPresentationBatchContext", source);
        }
    }

    [Fact]
    public void NewInfrastructure_DoesNotWriteMetadataProfilesOrDrawingSettings()
    {
        var source = ResolverSource() + PresentationSource();

        Assert.DoesNotContain("ElementDataStore", source);
        Assert.DoesNotContain("TimberElementDefaultProfileStore", source);
        Assert.DoesNotContain("AutoCadDrawingAnnotationScaleStore.Write", source);
        Assert.DoesNotContain("JsonSerializer", source);
        Assert.DoesNotContain("Xrecord", source);
        Assert.DoesNotContain("SetAt(", source);
        Assert.DoesNotContain("Editor", source);
        Assert.DoesNotContain("Alert", source);
    }

    [Fact]
    public void IndirectObjectIdHelper_DoesNotWriteOrRetainDatabaseObjects()
    {
        var helper = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AutoCadObjectIdAccess.cs");
        var resolver = ResolverSource();

        Assert.DoesNotContain("UpgradeOpen", helper);
        Assert.DoesNotContain("OpenMode.ForWrite", helper);
        Assert.DoesNotContain("static readonly", helper);
        Assert.Contains("OpenMode.ForRead", resolver);
        Assert.Contains("out var record", resolver);
        Assert.DoesNotContain("TextStyleTableRecord", PresentationSource());
    }

    private static string ResolverSource() => Source(
        "src",
        "AcKrovy.AutoCAD",
        "Infrastructure",
        "AutoCadTextStyleResolver.cs");

    private static string PresentationSource() => Source(
        "src",
        "AcKrovy.AutoCAD",
        "Infrastructure",
        "AutoCadAnnotationPresentationContext.cs");

    private static string Source(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. segments]));

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(
            end,
            startIndex + start.Length,
            StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        Assert.True(endIndex > startIndex, $"End marker not found: {end}");
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

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
