using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class FramedBaselineRestorationSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle, "K1", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Circle, "S1", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Circle, "W3", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Circle, "K10", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Circle, "S25", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Circle, "W99", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Slot, "K1", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Slot, "S1", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Slot, "W3", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Slot, "K10", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Slot, "S25", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Slot, "W99", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Rectangle, "K1", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Rectangle, "S1", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Rectangle, "W3", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Rectangle, "K10", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Rectangle, "S25", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Rectangle, "W99", TimberItemLeaderBlockSize.Small)]
    [InlineData(ItemNumberLeaderStyle.Slot, "VT12", TimberItemLeaderBlockSize.Medium)]
    [InlineData(ItemNumberLeaderStyle.Rectangle, "VT12", TimberItemLeaderBlockSize.Medium)]
    [InlineData(ItemNumberLeaderStyle.Slot, "VT1234", TimberItemLeaderBlockSize.Large)]
    [InlineData(ItemNumberLeaderStyle.Rectangle, "VT1234", TimberItemLeaderBlockSize.Large)]
    public void Resolve_MatchesMierkaBaselineTokenParity(
        ItemNumberLeaderStyle style,
        string token,
        TimberItemLeaderBlockSize expectedSize)
    {
        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(style, token);

        Assert.Equal(expectedSize, definition.Size);
        Assert.Equal(
            TimberItemLeaderBlockDefinitionRules.BaseFramedItemTextHeightAtScale50Mm,
            definition.TextHeightMm);
        if (style == ItemNumberLeaderStyle.Circle)
        {
            Assert.Equal(
                TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
                definition.WidthMm);
            Assert.Equal(
                TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
                definition.HeightMm);
        }
        else
        {
            Assert.Equal(
                expectedSize switch
                {
                    TimberItemLeaderBlockSize.Small =>
                        TimberItemLeaderBlockDefinitionRules.SmallFrameWidthMm,
                    TimberItemLeaderBlockSize.Medium =>
                        TimberItemLeaderBlockDefinitionRules.MediumFrameWidthMm,
                    TimberItemLeaderBlockSize.Large =>
                        TimberItemLeaderBlockDefinitionRules.LargeFrameWidthMm,
                    _ => throw new ArgumentOutOfRangeException(nameof(expectedSize)),
                },
                definition.WidthMm);
            Assert.Equal(
                TimberItemLeaderBlockDefinitionRules.FrameHeightMm,
                definition.HeightMm);
        }
    }

    [Fact]
    public void GeometryConstants_MatchFrozenMierkaBaseline()
    {
        Assert.Equal(520d, TimberItemLeaderBlockDefinitionRules.PreviousCircleDiameterMm);
        Assert.Equal(400d, TimberItemLeaderBlockDefinitionRules.CircleDiameterMm);
        Assert.Equal(
            400d / 520d,
            TimberItemLeaderBlockDefinitionRules.FramedGeometryReductionFactor);
        Assert.Equal(175d, TimberItemLeaderBlockDefinitionRules.FramedGeometrySizingTextHeightMm);
        Assert.Equal(
            350d,
            TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm);
        Assert.Equal(
            Math.PI / 3d,
            TimberItemLeaderLayoutCalculator.FramedFirstSegmentAngleRadians);
    }

    [Fact]
    public void ProductionEnsure_UsesResolveNotMeasuredWidth()
    {
        var variant = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AcKrovyItemLeaderBlockVariantService.cs"));
        var ensure = Member(variant, "public static AutoCadItemLeaderBlockVariantResult Ensure(");
        var ensureResolved = Member(
            variant,
            "internal static AutoCadItemLeaderBlockVariantResult EnsureResolved(");

        Assert.Contains("EnsureResolved(", ensure);
        Assert.DoesNotContain("AutoCadItemLeaderTextMeasurementService.Measure(", ensure);
        Assert.DoesNotContain("EvaluateMeasuredTextWidth(", ensure);
        Assert.DoesNotContain("TextOverflow(", ensure);
        Assert.Contains(
            "TimberItemLeaderBlockDefinitionRules.Resolve(",
            ensureResolved);
        Assert.DoesNotContain(
            "AutoCadItemLeaderTextMeasurementService.Measure(",
            ensureResolved);
        Assert.DoesNotContain("EvaluateMeasuredTextWidth(", ensureResolved);
    }

    [Fact]
    public void VariantKey_IsG3GeometryAndStableStyleWithoutHeight()
    {
        var key = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AutoCadItemLeaderBlockVariantKey.cs"));

        Assert.Contains("CurrentGeometryVersion = 3", key);
        Assert.Contains("TextStyleIdentity", key);
        Assert.DoesNotContain("ItemNumberPaperHeightMm", key);
        Assert.DoesNotContain("BaseDenominator", key);
        Assert.Contains("$\"AK_ITEM_{frame}{size}_G{key.GeometryVersion}_\"", key);
        Assert.Contains("schema=3|geometry=", key);
        Assert.Contains("|textStyle=", key);
        Assert.DoesNotContain("|paperHeightMm=", key);
    }

    [Fact]
    public void ProductionAttributeReference_InheritsG3StyleAndAppliesHeight()
    {
        var labels = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs"));
        var setter = Member(
            labels,
            "private static void SetItemNumberBlockAttribute(");
        var create = Member(
            labels,
            "private static MLeader CreateBlockMLeader(");

        Assert.Contains("OpenMode.ForRead", setter);
        Assert.DoesNotContain("OpenMode.ForWrite", setter);
        Assert.DoesNotContain("attributeDefinition.TextStyleId =", setter);
        Assert.Contains("attribute.SetAttributeFromBlock(", setter);
        Assert.Contains("attribute.TextString = contents;", setter);
        Assert.DoesNotContain("attribute.TextStyleId =", setter);
        Assert.Contains("attribute.Height = preparation.AttributeHeightMm;", setter);
        Assert.Contains(
            "leader.SetBlockAttribute(preparation.AttributeDefinitionId, attribute)",
            setter);
        Assert.Contains(
            "TimberAnnotationScaleRules.DefaultDenominator",
            labels);
        var append = create.IndexOf(
            "modelSpace.AppendEntity(leader)",
            StringComparison.Ordinal);
        var vertices = create.IndexOf(
            "leader.SetFirstVertex(leaderLineIndex, placement.Anchor)",
            StringComparison.Ordinal);
        var setAttribute = create.IndexOf(
            "SetItemNumberBlockAttribute(",
            StringComparison.Ordinal);
        Assert.True(
            append >= 0 &&
            vertices > append &&
            setAttribute > vertices);
    }

    [Fact]
    public void SharedDefinitionCreate_BakesG3StyleOnlyAtCreation()
    {
        var variant = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AcKrovyItemLeaderBlockVariantService.cs"));
        var create = Member(
            variant,
            "private static AutoCadItemLeaderBlockVariantResult CreateAndCache(");

        Assert.Contains("definition.TextHeightMm", create);
        Assert.Contains("textStyleId", create);
        Assert.Contains(
            "AddItemNumberAttribute(\n            database,\n            transaction,\n            block,\n            definition.TextHeightMm,\n            textStyleId)",
            create.Replace("\r\n", "\n"));
    }

    [Fact]
    public void SpecialSymbolBlocks_RemainUnchangedMarkers()
    {
        var slope = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "SlopeArrowService.cs"));

        Assert.Contains(
            "DECORAIR_ACADKROVY_HORIZONTAL_SLOPE_MARKER",
            slope);
        Assert.Contains(
            "DECORAIR_ACADKROVY_POST_90_MARKER_V3",
            slope);
        Assert.Contains("new Line(", slope);
        Assert.DoesNotContain("TextStyleId", slope);
    }

    [Fact]
    public void CombinedLandingAndSiblingPresentation_RemainPresent()
    {
        var labels = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs"));
        Assert.Contains(
            "CombinedFramedLandingDistanceMm",
            labels);

        Assert.True(File.Exists(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AutoCadPlainItemLeaderPresentationPolicy.cs")));
        Assert.True(File.Exists(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AutoCadDimensionsLeaderPresentationPolicy.cs")));
        Assert.True(File.Exists(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AutoCadFullLabelPresentationPolicy.cs")));
    }

    [Fact]
    public void HostBaselineProof_IsEntirelyDebugOnlyAndOffProductSurfaces()
    {
        var commandSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src", "AcKrovy.AutoCAD", "Commands",
            "AutoCadFramedBaselineProofCommands.cs")).Trim();
        var serviceSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadFramedBaselineProofService.cs")).Trim();
        var policySource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadFramedBaselineProofPolicy.cs")).Trim();

        Assert.StartsWith("#if DEBUG", commandSource, StringComparison.Ordinal);
        Assert.EndsWith("#endif", commandSource, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", serviceSource, StringComparison.Ordinal);
        Assert.EndsWith("#endif", serviceSource, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", policySource, StringComparison.Ordinal);
        Assert.EndsWith("#endif", policySource, StringComparison.Ordinal);

        Assert.Contains("AK_DEV_FRAMED_BASELINE_CREATE", commandSource);
        Assert.Contains("AK_DEV_FRAMED_BASELINE_VERIFY", commandSource);
        Assert.Contains("AK_DEV_FRAMED_BASELINE_CLEAN", commandSource);

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
                    "AK_DEV_FRAMED_BASELINE",
                    File.ReadAllText(path)));
        }
    }

    [Fact]
    public void HostBaselineProof_MatrixCoversAllFrameStylesAndTokenParity()
    {
        var policySource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadFramedBaselineProofPolicy.cs"));

        Assert.Contains("ItemNumberLeaderStyle.Circle", policySource);
        Assert.Contains("ItemNumberLeaderStyle.Rectangle", policySource);
        Assert.Contains("ItemNumberLeaderStyle.Slot", policySource);
        Assert.Contains("\"K1\"", policySource);
        Assert.Contains("\"K10\"", policySource);
        Assert.Contains("PaperHeightsMm", policySource);
        Assert.Contains("2.7d", policySource);
        Assert.Contains("3.5d", policySource);
        Assert.Contains("Denominators", policySource);
        Assert.Contains("50", policySource);
        Assert.Contains("100", policySource);
        Assert.Contains("SharedDefinitionPass", policySource);
        Assert.Contains("DenomOnlyBlockScalePass", policySource);
    }

    [Fact]
    public void HostBaselineProof_ServiceEnforcesOneTransactionReadOnlyVerifyAndClean()
    {
        var service = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadFramedBaselineProofService.cs"));

        var create = Member(service, "public static void Create()");
        var verify = Member(service, "public static void Verify()");
        var clean = Member(service, "public static void Clean()");

        Assert.Contains("StartTransaction()", create);
        Assert.Contains("transaction.Commit()", create);
        Assert.Contains("transaction.Abort()", create);
        Assert.Contains("StartOpenCloseTransaction()", create);
        Assert.Contains("EnsureResolved(", create);
        Assert.Contains("BlockContentId", service);
        Assert.Contains("NamedObjectsDictionaryId", service);

        Assert.Contains("StartOpenCloseTransaction()", verify);
        Assert.DoesNotContain("OpenMode.ForWrite", verify);
        Assert.DoesNotContain("Commit()", verify);

        Assert.Contains("Erase()", clean);
        Assert.Contains("transaction.Commit()", clean);
    }

    [Fact]
    public void HostBaselineProof_SharedDefinitionInvariantIsVerified()
    {
        var service = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadFramedBaselineProofService.cs"));

        Assert.Contains(
            "VerifySharedDefinitionInvariant(",
            service);
        Assert.Contains(
            "VerifyDenomOnlyBlockScaleInvariant(",
            service);
        Assert.Contains(
            "BaseFramedItemTextHeightAtScale50Mm",
            service);
        Assert.Contains(
            "AttributeReferenceHeightMm",
            service);
        Assert.Contains(
            "attribute.TextStyleId = style.TextStyleId",
            service);
        Assert.Contains(
            "attribute.Height = attributeHeight",
            service);
        Assert.Contains(
            "BlockScale = new Scale3d(slot.BlockScale)",
            service);
        Assert.DoesNotContain("OpenMode.ForWrite", Member(service,
            "private static bool VerifyCore("));
    }

    private static string Member(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
