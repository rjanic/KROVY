using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class CombinedPlainLandingDistanceSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData(50, 350d)]
    [InlineData(100, 700d)]
    public void CombinedPlainLandingDistance_MatchesFramedCombinedParity(
        int denominator,
        double expectedLandingMm)
    {
        var scaleFactor = TimberAnnotationScaleRules.GetScaleFactor(denominator);
        Assert.Equal(
            expectedLandingMm,
            TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
            scaleFactor);
        Assert.Equal(
            expectedLandingMm,
            TimberNativeLeaderStyleRules.CombinedFramedSettings.LandingDistance *
            scaleFactor);
        Assert.Equal(0d, TimberNativeLeaderStyleRules.Settings.LandingDistance);
    }

    [Fact]
    public void CombinedPlain_PassesAuthoritativeLandingToNativeMLeaderPath()
    {
        var combined = Member(
            ElementLabelSource(),
            "private static bool UpsertCombinedLeader(");
        var create = Member(
            ElementLabelSource(),
            "private static MLeader CreateNativeMLeader(");
        var update = Member(
            ElementLabelSource(),
            "private static bool TryUpdateNativeLeader(");
        var apply = Member(
            StyleServiceSource(),
            "public static void ApplyInstanceProperties(");
        var applyCombined = Member(
            ElementLabelSource(),
            "private static LeaderPlacement ApplyCombinedLandingDistance(");
        var tryLanding = Member(
            ElementLabelSource(),
            "private static bool TryGetLandingSegment(");
        var dimensionsPlacement = Member(
            ElementLabelSource(),
            "private static LabelPlacement CalculateCombinedDimensionsTextPlacement(");

        Assert.Contains(
            "CombinedFramedLandingDistanceMm *",
            combined);
        Assert.Contains(
            "presentationScaleFactor",
            combined);
        Assert.Contains(
            "combinedLandingDistanceMm: combinedLandingDistanceMm",
            combined);
        Assert.Contains(
            "doglegLengthOverride: combinedLandingDistanceMm",
            create);
        Assert.Contains(
            "doglegLengthOverride: combinedLandingDistanceMm",
            update);
        Assert.Contains(
            "doglegLengthOverride ??",
            apply);
        Assert.Contains("leader.DoglegLength = doglegLength", apply);
        Assert.Contains("doglegLength > 1e-9d", apply);
        Assert.Contains("leader.SetDogleg(", apply);

        Assert.Contains("combinedLandingDistanceMm", applyCombined);
        Assert.Contains(
            "EnvelopeWidthMm / 2d +",
            applyCombined);
        Assert.DoesNotContain(
            "CombinedFramedLandingDistanceMm * presentationScaleFactor *",
            applyCombined);
        Assert.DoesNotContain(
            "combinedLandingDistanceMm + combinedLandingDistanceMm",
            applyCombined);

        Assert.Contains("leader.DoglegLength", tryLanding);
        Assert.Contains(
            "CalculateTextCenterOffsetFromLandingStartMm(",
            dimensionsPlacement);
        Assert.Contains(
            "landingStartPoint.DistanceTo(landingEndPoint)",
            dimensionsPlacement);
        Assert.Contains(
            "TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(",
            dimensionsPlacement);
        Assert.DoesNotContain(
            "RotationRadians: 0d",
            dimensionsPlacement);
        Assert.DoesNotContain(
            "landingDistanceMm <= 0",
            dimensionsPlacement);
    }

    [Fact]
    public void StandalonePlain_KeepsZeroLandingDistanceContract()
    {
        Assert.Equal(0d, TimberNativeLeaderStyleRules.Settings.LandingDistance);
        Assert.False(TimberNativeLeaderStyleRules.RequiresExplicitDoglegDirection);

        var apply = Member(
            StyleServiceSource(),
            "public static void ApplyInstanceProperties(");
        Assert.Contains(
            "settings.LandingDistance * presentationScaleFactor",
            apply);
        Assert.Contains("doglegLengthOverride ??", apply);

        var upsert = Member(
            ElementLabelSource(),
            "private static bool UpsertLeader(");
        Assert.Contains(
            "double? combinedLandingDistanceMm = null",
            upsert);
    }

    [Fact]
    public void FramedCombined_RemainsOnCombinedFramedSettingsPath()
    {
        var applyCombined = Member(
            StyleServiceSource(),
            "public static void ApplyCombinedBlockInstanceProperties(");
        Assert.Contains(
            "TimberNativeLeaderStyleRules.CombinedFramedSettings",
            applyCombined);
        Assert.Contains(
            "settings.LandingDistance * presentationScaleFactor",
            applyCombined);
        Assert.Equal(
            350d,
            TimberNativeLeaderStyleRules.CombinedFramedSettings.LandingDistance);
        Assert.Equal(
            350d,
            TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm);
    }

    [Fact]
    public void TextLocationOffset_IsLandingPlusHalfEnvelopeOnce()
    {
        const double envelopeWidthMm = 200d;
        foreach (var denominator in new[] { 50, 100 })
        {
            var scale = TimberAnnotationScaleRules.GetScaleFactor(denominator);
            var landing =
                TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
                scale;
            var contentDistance = envelopeWidthMm / 2d + landing;
            Assert.Equal(landing + envelopeWidthMm / 2d, contentDistance);
            Assert.NotEqual(landing * 2d + envelopeWidthMm / 2d, contentDistance);
            Assert.True(landing > 0d);
        }
    }

    [Fact]
    public void LandingDistanceGuard_StillRejectsNonPositiveValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberCombinedDimensionTypographyRules
                .CalculateTextCenterOffsetFromLandingStartMm(
                    0d,
                    100d,
                    125d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberCombinedDimensionTypographyRules
                .CalculateTextCenterOffsetFromLandingStartMm(
                    -1d,
                    100d,
                    125d));
        var offset =
            TimberCombinedDimensionTypographyRules
                .CalculateTextCenterOffsetFromLandingStartMm(
                    350d,
                    100d,
                    125d);
        Assert.True(offset >= 0d);
    }

    private static string ElementLabelSource() =>
        Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "ElementLabelService.cs");

    private static string StyleServiceSource() =>
        Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AcKrovyMLeaderStyleService.cs");

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
