using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class FullLabelProofPostFootprintFixtureTests
{
    private const double PostFootprintSizeMm = 300d;
    private const int PostFootprintWidthEdgeIndex = 0;
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ValidPostFootprintSnapshot_DoesNotThrowWithoutPlanLength()
    {
        var data = CreateProductionPostFootprintSnapshot();

        var measurement = TimberCalculator.Measure(
            data,
            planLengthMm: null);

        Assert.Equal(
            TimberPostFootprintAssignmentRules.DefaultManualLengthMm,
            measurement.ActualLengthMm);
        Assert.Null(measurement.PlanLengthMm);
    }

    [Fact]
    public void ValidPostFootprintSnapshot_UsesProductionManualLengthMode()
    {
        var data = CreateProductionPostFootprintSnapshot();

        Assert.Equal(TimberElementType.Post, data.ElementType);
        Assert.Equal(
            LengthCalculationMode.ManualLength,
            data.LengthCalculationMode);
        Assert.Equal(
            LengthCalculationMode.ManualLength,
            TimberCalculator.ResolveLengthCalculationMode(data));
        Assert.Equal(
            TimberPostFootprintAssignmentRules.DefaultManualLengthMm,
            data.ManualLengthMm);
        Assert.Equal(PostFootprintWidthEdgeIndex, data.FootprintWidthEdgeIndex);
        Assert.Equal(PostFootprintSizeMm, data.WidthMm);
        Assert.Equal(PostFootprintSizeMm, data.HeightMm);
        Assert.True(TimberPostFootprintMetadataRules.IsValidNewFootprintPost(data));
    }

    [Fact]
    public void MissingManualLength_FallsBackToPlanLengthAndThrowsWithoutIt()
    {
        var data = CreateProductionPostFootprintSnapshot() with
        {
            ManualLengthMm = null,
            // Keep explicit ManualLength mode but clear the value so Measure
            // must fall back to RequirePlanLength, matching the broken proof.
            LengthCalculationMode = LengthCalculationMode.ManualLength,
        };

        // CreateMetadata would refill ManualLengthMm; emulate the old proof
        // which used AutoByElementType Post without ManualLengthMm.
        data = new TimberElementData
        {
            SchemaVersion = TimberElementDataSchema.CurrentVersion,
            ElementId = "FL-D-BROKEN",
            ElementType = TimberElementType.Post,
            WidthMm = 140d,
            HeightMm = 140d,
            FootprintWidthEdgeIndex = 0,
            AnnotationMode = TimberAnnotationMode.FullLabel,
            LengthCalculationMode = LengthCalculationMode.AutoByElementType,
            ManualLengthMm = null,
        };

        Assert.Equal(
            LengthCalculationMode.ManualLength,
            TimberCalculator.ResolveLengthCalculationMode(data));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            TimberCalculator.Measure(data, planLengthMm: null));
        Assert.Equal(
            "Pôdorysná dĺžka nie je pre tento spôsob výpočtu dostupná.",
            exception.Message);
    }

    [Fact]
    public void ProofService_UsesProductionCreateMetadataFactory()
    {
        var service = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadFullLabelProofService.cs");
        var policy = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadFullLabelProofPolicy.cs");

        Assert.Contains(
            "AutoCadFullLabelProofPolicy.CreatePostFootprintElementData(",
            service);
        Assert.Contains(
            "TimberPostFootprintAssignmentRules.CreateMetadata(",
            policy);
        Assert.Contains(
            "LengthCalculationMode.ManualLength",
            policy);
        Assert.Contains(
            "ManualLengthMm = null",
            policy);
        Assert.Contains(
            "PostFootprintSizeMm",
            policy);
        Assert.DoesNotContain(
            "WidthMm = proofCase.IsPostFootprint ? 140d",
            service);
    }

    [Fact]
    public void ProofCreate_KeepsSingleAtomicTransactionWithoutCompletedManifestOnFailure()
    {
        var create = Member(
            Source(
                "src", "AcKrovy.AutoCAD", "Infrastructure",
                "AutoCadFullLabelProofService.cs"),
            "public static void Create(");

        Assert.Contains("StartTransaction()", create);
        Assert.Contains("WriteManifest(", create);
        Assert.Contains("transaction.Commit();", create);
        var writeManifest = create.IndexOf(
            "WriteManifest(",
            StringComparison.Ordinal);
        var commit = create.IndexOf(
            "transaction.Commit();",
            StringComparison.Ordinal);
        Assert.True(writeManifest >= 0 && writeManifest < commit);
        Assert.Contains(
            "Partial entities were not committed",
            create);
        Assert.Contains(
            "No completed proof manifest",
            create);
        Assert.Contains(
            "rolled back",
            create);
        Assert.DoesNotContain("createCompleted=true", create);
    }

    [Fact]
    public void CasesAThroughC_RemainNonPostFootprint()
    {
        var policy = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadFullLabelProofPolicy.cs");
        Assert.Contains(
            "\"A\",\n            IsPostFootprint: false",
            policy.Replace("\r\n", "\n"));
        Assert.Contains(
            "\"B\",\n            IsPostFootprint: false",
            policy.Replace("\r\n", "\n"));
        Assert.Contains(
            "\"C\",\n            IsPostFootprint: false",
            policy.Replace("\r\n", "\n"));
        Assert.Contains(
            "\"D\",\n            IsPostFootprint: true",
            policy.Replace("\r\n", "\n"));
    }

    [Fact]
    public void PostFullLabelPresentationContract_StillComesFromPresentationContext()
    {
        var labels = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "ElementLabelService.cs");
        var post = Member(labels, "public static bool UpsertForPostFootprint(");

        Assert.Contains(
            "AutoCadFullLabelPresentationPolicy.TryPrepare(",
            post);
        Assert.Contains(
            "resolvedTextStyleId: fullLabelPresentation.TextStyleId",
            post);
        Assert.Contains(
            "fullLabelPresentation.ModelHeightMm",
            post);
        Assert.Contains(
            "planLengthMm: null",
            post);
    }

    private static TimberElementData CreateProductionPostFootprintSnapshot()
    {
        var points = new[]
        {
            new TimberRectangularFootprintPoint(0d, 0d),
            new TimberRectangularFootprintPoint(PostFootprintSizeMm, 0d),
            new TimberRectangularFootprintPoint(
                PostFootprintSizeMm,
                PostFootprintSizeMm),
            new TimberRectangularFootprintPoint(0d, PostFootprintSizeMm),
        };
        var geometry = Assert.IsType<TimberRectangularFootprintGeometry>(
            TimberRectangularFootprintValidator.Validate(points).Geometry);
        var dimensions = TimberRectangularFootprintEdgeRules.ResolveDimensions(
            geometry,
            PostFootprintWidthEdgeIndex);

        return TimberPostFootprintAssignmentRules.CreateMetadata(
            TimberElementDefaults.For(TimberElementType.Post) with
            {
                ElementId = "FL-D",
                AnnotationMode = TimberAnnotationMode.FullLabel,
                ManualLengthMm = null,
                RoofPlaneId = "AK_DEV",
            },
            dimensions);
    }

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
