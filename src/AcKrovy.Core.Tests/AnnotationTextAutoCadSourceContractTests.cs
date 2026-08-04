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
        Assert.Contains("!double.IsFinite(descriptor.TextSize)", source);
        Assert.Contains("descriptor.TextSize != 0d", source);
        Assert.Contains("if (descriptor.IsAnnotative)", source);
        Assert.Contains("descriptor.IsValid", source);
        Assert.Contains("!descriptor.IsErased", source);
        Assert.Contains("hostState = record.Annotative", source);
        Assert.Contains("AnnotativeStates.True =>", source);
        Assert.Contains("AnnotativeStates.False =>", source);
        Assert.Contains("AnnotativeStates.NotApplicable =>", source);
        Assert.Contains("ErrorStatus.NotApplicable", source);
        Assert.Contains("record.TextSize", source);
        Assert.Contains("record!.Name", source);
        Assert.Contains("id == currentStyleId", source);
        Assert.Contains("descriptor.TextStyleId.IsValid", source);
        Assert.Contains("!descriptor.TextStyleId.IsErased", source);
        Assert.Contains("AutoCadDatabaseIdentity.IsSame(", source);
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
    public void HostCatalog_DistinguishesTableFailureAndPerRecordRejection()
    {
        var source = ResolverSource();

        Assert.Contains("ReadCatalogWithDiagnostics(", source);
        Assert.Contains("bool TableReadSucceeded", source);
        Assert.Contains("string? TableFailureReason", source);
        Assert.Contains("TextStyleTable read failed:", source);
        Assert.Contains("Record read failed:", source);
        Assert.Contains("Annotative getter failed:", source);
        Assert.Contains("AutoCadTextStyleDiagnosticEntry", source);
        Assert.Contains("compatibility.Reason", source);
    }

    [Fact]
    public void PresentationContext_UsesCoreNormalizationAndHeightAuthority()
    {
        var source = PresentationSource();

        Assert.Contains("TimberAnnotationTextSettingsRules.NormalizeStored(", source);
        Assert.Contains(
            "TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings()",
            source);
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
        Assert.Contains("public bool IsFallback { get; }", source);
        Assert.Contains("public bool HasCompatibleStyle { get; }", source);
        Assert.Contains("IsFallback = textStyleResolution.IsFallback", source);
        Assert.Contains(
            "HasCompatibleStyle = textStyleResolution.HasCompatibleStyle",
            source);
        Assert.Contains(
            "public AutoCadAnnotationTextRolePresentation ForRole(",
            source);
        Assert.Contains("TimberAnnotationTextRole.ItemCode", source);
        Assert.Contains("TimberAnnotationTextRole.Dimension", source);
        Assert.Contains("TimberAnnotationTextRole.Slope", source);
    }

    [Fact]
    public void HostCatalogAndContexts_AreBoundToOneDatabaseWithoutDbObjectLeaks()
    {
        var resolver = ResolverSource();
        var presentation = PresentationSource();
        var combined = resolver + presentation;

        Assert.Contains("public Database Database { get; }", resolver);
        Assert.Contains("public Database Database { get; }", presentation);
        Assert.Contains("AutoCadDatabaseIdentity.IsSame(", resolver);
        Assert.Contains("AutoCadDatabaseIdentity.IsSame(", presentation);
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
    public void Segment_IsLineEndingAgnostic()
    {
        const string sourceLf =
            "public AutoCadAnnotationPresentationContext ResolveForElement(\n" +
            "    TimberElementData data)\n" +
            "{\n" +
            "    return AutoCadAnnotationPresentationContext.Create(\n" +
            "        Database,\n" +
            "        annotationScaleContext,\n" +
            "        data,\n" +
            "        _textStyleResolver);\n" +
            "}\n";

        var reference = Segment(
            sourceLf,
            "public AutoCadAnnotationPresentationContext ResolveForElement(",
            "}\n");

        var sourceCrlf = sourceLf.Replace("\n", "\r\n");
        Assert.Equal(
            reference,
            Segment(
                sourceCrlf,
                "public AutoCadAnnotationPresentationContext ResolveForElement(",
                "}\n"));

        var sourceCr = sourceLf.Replace("\n", "\r");
        Assert.Equal(
            reference,
            Segment(
                sourceCr,
                "public AutoCadAnnotationPresentationContext ResolveForElement(",
                "}\n"));
    }

    [Fact]
    public void Stage4B_ConnectsPresentationContextOnlyToFramedRendererOrchestration()
    {
        var labels = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "ElementLabelService.cs");
        var orchestration = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "TimberAnnotationService.cs");
        Assert.Contains("AutoCadAnnotationPresentationContext", labels);
        Assert.Contains("AutoCadAnnotationPresentationBatchContext", orchestration);
        Assert.DoesNotContain("AutoCadTextStyleResolver", labels + orchestration);

        // Slope numeric text consumes the presentation context through its own
        // policy, never through AutoCadTextStyleResolver directly.
        foreach (var file in new[]
                 {
                     "SlopeAnnotationService.cs",
                     "SlopeAngleTextService.cs",
                 })
        {
            var source = Source(
                "src",
                "AcKrovy.AutoCAD",
                "Infrastructure",
                file);
            Assert.DoesNotContain("AutoCadTextStyleResolver", source);
            Assert.DoesNotContain(
                "AutoCadAnnotationPresentationBatchContext",
                source);
        }

        Assert.Contains(
            "AutoCadAnnotationPresentationContext",
            Source(
                "src",
                "AcKrovy.AutoCAD",
                "Infrastructure",
                "SlopeAngleTextService.cs"));
        Assert.Contains(
            "AutoCadSlopeTextPresentationPolicy.TryPrepare(",
            Source(
                "src",
                "AcKrovy.AutoCAD",
                "Infrastructure",
                "SlopeAngleTextService.cs"));

        string[] excludedRendererFiles =
        [
            "AcKrovyMLeaderStyleService.cs",
            "AcKrovyItemLeaderBlockService.cs",
        ];

        foreach (var file in excludedRendererFiles)
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

    [Fact]
    public void DatabaseIdentity_UsesOneNativeHostAuthorityWithoutCachingOrPersistence()
    {
        var identity = DatabaseIdentitySource();
        var resolver = ResolverSource();
        var presentation = PresentationSource();
        var objectIdAccess = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AutoCadObjectIdAccess.cs");

        Assert.Contains("database.UnmanagedObject", identity);
        Assert.Contains("database.IsDisposed", identity);
        Assert.Contains("database.UnmanagedObject == IntPtr.Zero", identity);
        Assert.Contains("expected.Value.Value == actual.Value.Value", identity);
        Assert.Contains("ReferenceEquals(expected, actual)", identity);
        Assert.DoesNotContain("FingerprintGuid", identity);
        Assert.DoesNotContain("Filename", identity);
        Assert.DoesNotContain("Document.Name", identity);
        Assert.DoesNotContain("static readonly", identity);
        Assert.DoesNotContain("Dictionary", identity);
        Assert.DoesNotContain("JsonSerializer", identity);
        Assert.DoesNotContain("Xrecord", identity);
        Assert.Contains("AutoCadDatabaseIdentity.IsSame(", resolver);
        Assert.Contains("AutoCadDatabaseIdentity.IsSame(", presentation);
        Assert.Contains("AutoCadDatabaseIdentity.IsSame(", objectIdAccess);
        Assert.DoesNotContain(
            "ReferenceEquals(descriptor.TextStyleId.Database, database)",
            resolver);
        Assert.DoesNotContain(
            "ReferenceEquals(textStyleResolver.Database, database)",
            presentation);
        Assert.DoesNotContain(
            "ReferenceEquals(textStyleCatalog.Database, database)",
            presentation);
    }

    [Fact]
    public void CatalogResolverAndPresentation_KeepForeignDatabaseRejection()
    {
        var resolver = ResolverSource();
        var presentation = PresentationSource();

        Assert.Contains("private static bool IsBoundToDatabase(", resolver);
        Assert.Contains("private static bool IsUsableInDatabase(", resolver);
        Assert.Contains(
            "Resolved text style belongs to a different database.",
            presentation);
        Assert.Contains(
            "Text-style resolver belongs to a different database.",
            presentation);
        Assert.Contains(
            "Annotation presentation context belongs to a different database.",
            presentation);
        Assert.Contains(
            "Text-style catalog belongs to a different database.",
            presentation);
        Assert.True(
            CountOccurrences(resolver + presentation, "AutoCadDatabaseIdentity.IsSame(") >= 6);
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

    private static string DatabaseIdentitySource() => Source(
        "src",
        "AcKrovy.AutoCAD",
        "Infrastructure",
        "AutoCadDatabaseIdentity.cs");

    private static string Source(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. segments]));

    private static string Segment(string source, string start, string end)
    {
        source = NormalizeLineEndings(source);
        start = NormalizeLineEndings(start);
        end = NormalizeLineEndings(end);

        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(
            end,
            startIndex + start.Length,
            StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        Assert.True(endIndex > startIndex, $"End marker not found: {end}");
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static string NormalizeLineEndings(string source) =>
        source.Replace("\r\n", "\n").Replace("\r", "\n");

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
