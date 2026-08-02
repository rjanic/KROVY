using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class CombinedPlainRefreshSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ReadLabels_ClassifiesCombinedPlainFramedItemAsLeader()
    {
        var resolve = Member(
            ElementLabelSource(),
            "private static TimberMainAnnotationRepresentation ResolveMainAnnotationRepresentation(");

        Assert.Contains("TimberMainAnnotationComponentRole.FramedItem", resolve);
        Assert.Contains("ItemNumberLeaderStyle.Plain", resolve);
        Assert.Contains(
            "TimberMainAnnotationRepresentation.Leader",
            resolve);
        Assert.Contains(
            "TimberMainAnnotationRepresentation.BlockLeader",
            resolve);

        var plainBranch = resolve.IndexOf(
            "ItemNumberLeaderStyle.Plain",
            StringComparison.Ordinal);
        var leaderReturn = resolve.IndexOf(
            "TimberMainAnnotationRepresentation.Leader",
            plainBranch,
            StringComparison.Ordinal);
        var blockReturn = resolve.IndexOf(
            "TimberMainAnnotationRepresentation.BlockLeader",
            plainBranch,
            StringComparison.Ordinal);
        Assert.True(plainBranch >= 0 && leaderReturn >= 0 && blockReturn >= 0);
        Assert.True(leaderReturn < blockReturn);
    }

    [Fact]
    public void CombinedPlainRefresh_FindsExistingLeaderBeforeCreateReplacement()
    {
        var upsert = Member(
            ElementLabelSource(),
            "private static bool UpsertLeader(");
        var find = Member(
            ElementLabelSource(),
            "private static ObjectId FindExistingLabelId(");

        Assert.Contains("ResolveMainAnnotationRepresentation(", ElementLabelSource());
        Assert.Contains("desiredRepresentation", find);
        Assert.Contains(
            "TimberMainAnnotationComponentRole.FramedItem",
            upsert);
        Assert.Contains("geometryMatches", upsert);
        Assert.Contains("TryUpdateNativeLeader(", upsert);

        var geometry = upsert.IndexOf("geometryMatches", StringComparison.Ordinal);
        var tryUpdate = upsert.IndexOf(
            "TryUpdateNativeLeader(",
            geometry,
            StringComparison.Ordinal);
        var createNative = upsert.IndexOf(
            "CreateNativeMLeader(",
            tryUpdate,
            StringComparison.Ordinal);
        Assert.True(geometry >= 0 && tryUpdate >= 0 && createNative >= 0);
        Assert.True(tryUpdate < createNative);
    }

    [Theory]
    [InlineData(50, 350d)]
    [InlineData(100, 700d)]
    public void CombinedPlain_CreateAndRefreshShareLandingContract(
        int denominator,
        double expectedLandingMm)
    {
        var scale = TimberAnnotationScaleRules.GetScaleFactor(denominator);
        var landing =
            TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
            scale;
        Assert.Equal(expectedLandingMm, landing);

        var combined = Member(
            ElementLabelSource(),
            "private static bool UpsertCombinedLeader(");
        var apply = Member(
            ElementLabelSource(),
            "private static LeaderPlacement ApplyCombinedLandingDistance(");

        Assert.Contains("combinedLandingDistanceMm:", combined);
        Assert.Contains("CombinedFramedLandingDistanceMm *", combined);
        Assert.Contains("combinedLandingDistanceMm", apply);
        Assert.Equal(
            0d,
            TimberNativeLeaderStyleRules.Settings.LandingDistance);
    }

    [Fact]
    public void FramedCombinedStyles_RemainBlockLeaderClassification()
    {
        foreach (var style in new[]
                 {
                     ItemNumberLeaderStyle.Circle,
                     ItemNumberLeaderStyle.Slot,
                     ItemNumberLeaderStyle.Rectangle,
                 })
        {
            Assert.NotEqual(ItemNumberLeaderStyle.Plain, style);
            Assert.True(
                TimberAnnotationModeRules.IsFramedItemLeader(
                    TimberAnnotationMode.DimensionsWithItemNumber,
                    style) ||
                ItemNumberLeaderStyleRules.Normalize(style) !=
                    ItemNumberLeaderStyle.Plain);
        }

        var resolve = Member(
            ElementLabelSource(),
            "private static TimberMainAnnotationRepresentation ResolveMainAnnotationRepresentation(");
        Assert.Contains(
            "TimberMainAnnotationRepresentation.BlockLeader",
            resolve);
    }

    [Fact]
    public void Replacement_RemainsCreateBeforeEraseWhenGeometryChanges()
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
    }

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
