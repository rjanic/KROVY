using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class FramedRendererProofSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void CommandsPolicyAndService_AreEntirelyDebugOnly()
    {
        foreach (var source in new[] { Commands(), Policy(), Service() })
        {
            var trimmed = source.Trim();
            Assert.StartsWith("#if DEBUG", trimmed, StringComparison.Ordinal);
            Assert.EndsWith("#endif", trimmed, StringComparison.Ordinal);
        }
        Assert.Contains("AK_DEV_FRAMED_RENDERER_CREATE", Commands());
        Assert.Contains("AK_DEV_FRAMED_RENDERER_VERIFY", Commands());
        Assert.Contains("AK_DEV_FRAMED_RENDERER_CLEAN", Commands());
    }

    [Fact]
    public void Create_UsesTheRealProductionRendererAndNoParallelMLeaderCreation()
    {
        var create = Member(Service(), "public static void Create(");

        Assert.Contains("TimberAnnotationService.EnsureForElement(", create);
        Assert.Contains("AutoCadAnnotationPresentationBatchContext.Create(", create);
        Assert.Contains("batch.ItemLeaderVariantCatalog.Count", create);
        Assert.DoesNotContain("new MLeader", create);
        Assert.DoesNotContain("modelSpace.AppendEntity(leader)", create);
        Assert.Contains("transaction.Commit();", create);
        Assert.Contains("readTransaction", create);
        Assert.Contains("VerifyCore(", create);
    }

    [Fact]
    public void Architecture2027Preflight_CountsOnlyRealExactModelSpaceEntities()
    {
        var create = Member(Service(), "public static void Create(");

        Assert.Contains("blockTable[BlockTableRecord.ModelSpace]", Service());
        Assert.Contains(".OfType<Entity>()", create);
        Assert.Contains("!entity.IsErased", create);
        Assert.Contains("entity.OwnerId == modelSpace.ObjectId", create);
        Assert.DoesNotContain("database.CurrentSpaceId", create);
        Assert.DoesNotContain("LayoutDictionary", create);
        Assert.DoesNotContain("PaperSpace", create);
        Assert.DoesNotContain("foreach (ObjectId id in blockTable)", create);
    }

    [Fact]
    public void Verify_IsReadOnlyPersistentAndUsesDedicatedManifest()
    {
        var verify = Member(Service(), "public static void Verify(");
        var core = Member(Service(), "private static bool VerifyCore(");

        Assert.Contains("StartOpenCloseTransaction()", verify);
        Assert.DoesNotContain("OpenMode.ForWrite", verify + core);
        Assert.DoesNotContain("UpgradeOpen", verify + core);
        Assert.DoesNotContain("Commit();", verify + core);
        Assert.Contains("ReadManifest(database, transaction)", core);
        Assert.Contains("ManifestDictionaryKey", Service());
        Assert.Contains("SuiteIdentifier", Policy());
        Assert.Contains("FailureCaseNotTested", Policy());
    }

    [Fact]
    public void ProofChecksRelationshipsCombinedLegacyGeometryAndBatchReuse()
    {
        var core = Member(Service(), "private static bool VerifyCore(");

        Assert.Contains("RequireSame(blockByToken, \"A\", \"B\")", core);
        Assert.Contains("RequireSame(blockByToken, \"A\", \"C\")", core);
        Assert.Contains("RequireDifferent(blockByToken, \"A\", \"D\")", core);
        Assert.Contains("RequireDifferent(blockByToken, \"A\", \"E\")", core);
        Assert.Contains("RequireSame(blockByToken, \"H1\", \"H2\")", core);
        Assert.Contains("RequireSame(blockByToken, \"H1\", \"H3\")", core);
        Assert.Contains("ValidateExistingDefinitionDetailed(", core);
        Assert.Contains("combined dimensions MText component is missing", core);
        Assert.Contains("legacy definition was removed or migration did not occur", core);
    }

    [Fact]
    public void RectangleESelectsResolveBasedLargeToken()
    {
        var policy = Policy();
        var create = Member(Service(), "public static void Create(");
        var verify = Member(Service(), "private static bool VerifyCore(");

        Assert.Contains("VT1234", policy);
        Assert.Contains("RectangleLargeCaseTemplate", policy);
        Assert.Contains("runtimeCases.Insert(", create);
        Assert.Contains("TimberItemLeaderBlockSize.Large", create);
        Assert.Contains("Resolve(", create);
        Assert.Contains("Resolve(", verify);
        Assert.Contains("TimberItemLeaderBlockSize.Large", verify);
        Assert.DoesNotContain("SelectRectangleLargeFitCandidate(", create);
        Assert.DoesNotContain("Medium-overflow/Large-fit", verify);
    }

    [Fact]
    public void RectangleJProvesCircleDiameterInvariantForLongToken()
    {
        var overflow = Member(
            Service(),
            "private static AutoCadFramedRendererOverflowCaseManifest\n" +
            "        RunExpectedOverflowCase(");

        Assert.Contains("ItemNumberLeaderStyle.Circle", overflow);
        Assert.Contains("TimberItemLeaderBlockSize.Small", overflow);
        Assert.Contains("Resolve(", overflow);
        Assert.Contains("CircleLongInvariantText", overflow + Policy());
    }

    [Fact]
    public void ManifestSeparatesESuccessFromJExpectedFailureAndVerifyNeedsNoJEntity()
    {
        var policy = Policy();
        var verify = Member(Service(), "private static bool VerifyCore(");
        var verifyJ = Member(
            Service(),
            "private static void VerifyExpectedOverflowManifest(");

        Assert.Contains("RectangleCaseE", policy);
        Assert.Contains("OverflowCaseJ", policy);
        Assert.Contains("ExpectedCreatedEntityCount", policy);
        Assert.Contains("ModelSpaceEntityDelta", policy);
        Assert.Contains("BlockDefinitionDelta", policy);
        Assert.Contains("VariantCatalogDelta", policy);
        Assert.DoesNotContain("ObjectId", policy);
        Assert.DoesNotContain("Handle", policy);
        Assert.Contains("VerifyExpectedOverflowManifest(", verify);
        Assert.Contains("sourceByToken.ContainsKey(\"J\")", verifyJ);
        Assert.Contains("manifest.Cases.Any(item => item.Token == \"J\")", verifyJ);
        Assert.Contains("ContainsFramedItemToken(", verifyJ);
        Assert.DoesNotContain("OpenMode.ForWrite", verify + verifyJ);
        Assert.DoesNotContain("UpgradeOpen", verify + verifyJ);
        Assert.DoesNotContain("Commit();", verify + verifyJ);
    }

    [Fact]
    public void ProofNamesStayOffProductSurfaces()
    {
        var excludedRoots = new[]
        {
            Path.Combine(RepositoryRoot, "src", "AcKrovy.AutoCAD", "UI"),
            Path.Combine(RepositoryRoot, "src", "AcKrovy.Localization"),
            Path.Combine(RepositoryRoot, "deploy"),
        };
        foreach (var root in excludedRoots)
        {
            Assert.All(
                Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Where(path => new[] { ".cs", ".xaml", ".resx", ".xml" }
                        .Contains(Path.GetExtension(path),
                            StringComparer.OrdinalIgnoreCase)),
                path => Assert.DoesNotContain(
                    "AK_DEV_FRAMED_RENDERER",
                    File.ReadAllText(path)));
        }
    }

    private static string Commands() => Source(
        "src", "AcKrovy.AutoCAD", "Commands",
        "AutoCadFramedRendererProofCommands.cs");
    private static string Policy() => Source(
        "src", "AcKrovy.AutoCAD", "Infrastructure",
        "AutoCadFramedRendererProofPolicy.cs");
    private static string Service() => Source(
        "src", "AcKrovy.AutoCAD", "Infrastructure",
        "AutoCadFramedRendererProofService.cs");

    private static string Source(params string[] segments) =>
        Normalize(File.ReadAllText(Path.Combine([RepositoryRoot, .. segments])));

    private static string Member(string source, string declarationPrefix)
    {
        source = Normalize(source);
        var start = source.IndexOf(
            Normalize(declarationPrefix),
            StringComparison.Ordinal);
        Assert.True(start >= 0, $"Member not found: {declarationPrefix}");
        var brace = source.IndexOf('{', start);
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
