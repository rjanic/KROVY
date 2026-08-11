using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// DimensionsOnly SetDogleg host failure: early ApplyInstanceProperties call
/// before AppendEntity, plus near-flush landings that pass tiny epsilons.
/// </summary>
public sealed class DimensionsOnlyDoglegInputGuardTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(1e-13d, 0d)]
    [InlineData(0d, -1e-13d)]
    [InlineData(double.NaN, 1d)]
    [InlineData(1d, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 0d)]
    public void InvalidDirection_IsRejectedBeforeSetDogleg(double dirX, double dirY)
    {
        Assert.False(
            TimberNativeMLeaderDoglegInputRules.TryNormalizeDirection(
                dirX,
                dirY,
                out _,
                out _));
        Assert.False(
            TimberNativeMLeaderDoglegInputRules.ShouldCallSetDogleg(
                100d,
                dirX,
                dirY,
                out _,
                out _));
    }

    [Theory]
    [InlineData(1e-6d)]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(1e-4d)]
    [InlineData(1d)]
    [InlineData(9.999d)]
    public void NearFlushOrInvalidLength_RejectsSetDogleg(double lengthMm)
    {
        Assert.False(
            TimberNativeMLeaderDoglegInputRules.ShouldCallSetDogleg(
                lengthMm,
                1d,
                0d,
                out _,
                out _));
    }

    [Theory]
    [InlineData("120\\P80")]
    [InlineData("120")]
    public void ShortDimensionsLanding_IsBelowPracticalSetDoglegMinimum(
        string dimensionText)
    {
        var placement = new TimberLeaderPlacement(0d, 0d, 1000d, 0d, 0d);
        var layout =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeDimensionsLeader(
                placement,
                dimensionText);
        layout = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            layout,
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                placement.RotationRadians));
        var landing =
            TimberItemLeaderLayoutCalculator.ResolveStandaloneNativeLanding(
                layout.KneeX,
                layout.KneeY,
                layout.ContentX,
                layout.ContentY);

        Assert.True(
            landing.LengthMm <
            TimberNativeMLeaderDoglegInputRules.MinimumSetDoglegLengthMm,
            $"landing={landing.LengthMm:R} text={dimensionText}");
        Assert.False(
            TimberNativeMLeaderDoglegInputRules.ShouldCallSetDogleg(
                landing.LengthMm,
                landing.DirX,
                landing.DirY,
                out _,
                out _));
    }

    [Theory]
    [InlineData("120x80")]
    [InlineData("160×240")]
    public void LongerDimensionsLanding_MayAuthorizeSetDogleg_ButOnlyAfterAppend(
        string dimensionText)
    {
        var placement = new TimberLeaderPlacement(0d, 0d, 1000d, 0d, 0d);
        var layout =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeDimensionsLeader(
                placement,
                dimensionText);
        var landing =
            TimberItemLeaderLayoutCalculator.ResolveStandaloneNativeLanding(
                layout.KneeX,
                layout.KneeY,
                layout.ContentX,
                layout.ContentY);
        Assert.True(
            TimberNativeMLeaderDoglegInputRules.ShouldCallSetDogleg(
                landing.LengthMm,
                landing.DirX,
                landing.DirY,
                out _,
                out _),
            $"Expected post-append-capable landing for text={dimensionText}, " +
            $"landing={landing.LengthMm:R}");
        Assert.True(
            TimberNativeMLeaderDoglegInputRules
                .DeferSetDoglegUntilDatabaseResidentForStandaloneNative);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(90d)]
    [InlineData(-90d)]
    [InlineData(270d)]
    [InlineData(180d)]
    public void OrientedDimensionsLanding_StillSuppressesSetDogleg(
        double physicalDegrees)
    {
        var placement = new TimberLeaderPlacement(
            0d,
            0d,
            1000d,
            0d,
            physicalDegrees * Math.PI / 180d);
        var layout =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeDimensionsLeader(
                placement,
                "120\\P80");
        layout = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            layout,
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                placement.RotationRadians));
        var landing =
            TimberItemLeaderLayoutCalculator.ResolveStandaloneNativeLanding(
                layout.KneeX,
                layout.KneeY,
                layout.ContentX,
                layout.ContentY);
        Assert.False(
            TimberNativeMLeaderDoglegInputRules.ShouldCallSetDogleg(
                landing.LengthMm,
                landing.DirX,
                landing.DirY,
                out _,
                out _));
    }

    [Fact]
    public void HealthyLengthAndUnitDirection_AuthorizesNormalizedSetDogleg()
    {
        Assert.True(
            TimberNativeMLeaderDoglegInputRules.ShouldCallSetDogleg(
                25d,
                3d,
                4d,
                out var unitX,
                out var unitY));
        Assert.Equal(0.6d, unitX, 10);
        Assert.Equal(0.8d, unitY, 10);
    }

    [Fact]
    public void CreateNativeMLeader_DefersStandaloneDoglegUntilAfterAppend()
    {
        Assert.True(
            TimberNativeMLeaderDoglegInputRules
                .DeferSetDoglegUntilDatabaseResidentForStandaloneNative);

        var create = Member(
            Read("src/AcKrovy.AutoCAD/Infrastructure/ElementLabelService.cs"),
            "private static MLeader CreateNativeMLeader(");
        Assert.Contains("isStandaloneNativeMText", create, StringComparison.Ordinal);
        Assert.Contains(
            "doglegLengthOverride = null",
            create,
            StringComparison.Ordinal);
        Assert.Contains(
            "doglegDirectionOverride = null",
            create,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "doglegLengthOverride = standaloneLandingLength",
            create,
            StringComparison.Ordinal);
        Assert.Contains(
            "ApplyStandaloneNativeMTextLanding(",
            create,
            StringComparison.Ordinal);

        // AppendEntity must happen before standalone landing SetDogleg.
        var append = create.IndexOf("AppendEntity(leader)", StringComparison.Ordinal);
        var landing = create.IndexOf(
            "ApplyStandaloneNativeMTextLanding(",
            StringComparison.Ordinal);
        Assert.True(append >= 0 && landing > append);
    }

    [Fact]
    public void HostWiring_GuardsSetDoglegInApplyInstancePropertiesAndLanding()
    {
        var style = Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AcKrovyMLeaderStyleService.cs");
        var apply = Member(style, "public static void ApplyInstanceProperties(");
        Assert.Contains("ShouldCallSetDogleg(", apply, StringComparison.Ordinal);
        Assert.Contains("if (canSetDogleg)", apply, StringComparison.Ordinal);
        Assert.Contains("LeaderIndexIsPresent(", apply, StringComparison.Ordinal);
        Assert.Contains("SetDoglegNotCalled", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("doglegLength > 1e-9d", apply, StringComparison.Ordinal);

        var labels = Read(
            "src/AcKrovy.AutoCAD/Infrastructure/ElementLabelService.cs");
        var landing = Member(
            labels,
            "private static void ApplyStandaloneNativeMTextLanding(");
        Assert.Contains("ShouldCallSetDogleg(", landing, StringComparison.Ordinal);
        Assert.Contains("if (canSetDogleg)", landing, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "(landing - knee).GetNormal()",
            landing,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PlainItemOnly_StillFinalizesDoglegAfterAppend_Control()
    {
        var create = Member(
            Read("src/AcKrovy.AutoCAD/Infrastructure/ElementLabelService.cs"),
            "private static MLeader CreateNativeMLeader(");
        Assert.Contains(
            "ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(",
            create,
            StringComparison.Ordinal);
        Assert.Contains(
            "ApplyStandaloneNativeMTextLanding(",
            create,
            StringComparison.Ordinal);

        var plain = Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AutoCadStandaloneFramedItemOnlyAnnotationService.cs");
        Assert.DoesNotContain(
            "CalculateStandaloneNativeDimensionsLandingLengthMm(",
            plain,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Controls_R3DoesNotUseStandaloneDoglegDeferralHelper()
    {
        var r3 = Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentAnnotationService.cs");
        Assert.DoesNotContain(
            "DeferSetDoglegUntilDatabaseResidentForStandaloneNative",
            r3,
            StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(
            Path.Combine(
                RepositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string Member(string source, string signature)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = normalized.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "Missing member: " + signature);
        var brace = normalized.IndexOf('{', start);
        Assert.True(brace > start);
        var depth = 0;
        for (var i = brace; i < normalized.Length; i++)
        {
            if (normalized[i] == '{')
            {
                depth++;
            }
            else if (normalized[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return normalized.Substring(start, i - start + 1);
                }
            }
        }

        throw new InvalidOperationException("Unbalanced braces for " + signature);
    }

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
