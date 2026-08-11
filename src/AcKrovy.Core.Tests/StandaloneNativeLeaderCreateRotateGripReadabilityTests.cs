using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Vertical 90°/270° text readability + non-vertical regression guards.
/// Host validation required for live MText/BlockRotation.
/// </summary>
public sealed class StandaloneNativeLeaderCreateRotateGripReadabilityTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static TheoryData<ItemNumberLeaderStyle?> StandaloneFamilies { get; } =
        new()
        {
            { ItemNumberLeaderStyle.Plain }, // framed without description / item
            { null }, // ONLY DESCRIPTION (DimensionsOnly)
            { ItemNumberLeaderStyle.Circle }, // framed + description family
            { ItemNumberLeaderStyle.Rectangle },
            { ItemNumberLeaderStyle.Slot },
        };

    [Theory]
    [MemberData(nameof(StandaloneFamilies))]
    public void Create90_TextIsBottomToTop(ItemNumberLeaderStyle? family)
    {
        _ = family;
        var text =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(Math.PI / 2d);
        Assert.Equal(
            TimberStandaloneNativeLeaderOrientationRules
                .CanonicalVerticalTextPresentationRadians,
            text,
            8);
    }

    [Theory]
    [MemberData(nameof(StandaloneFamilies))]
    public void CopyRotate180_SourceBecomes270_TextRemainsBottomToTop(
        ItemNumberLeaderStyle? family)
    {
        _ = family;
        // CREATE at 90°, then COPY + ROTATE 180° → physical 270°.
        var create90 =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(Math.PI / 2d);
        var afterCopyRotate270 =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(3d * Math.PI / 2d);
        var afterCopyRotateMinus90 =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(-Math.PI / 2d);

        Assert.Equal(create90, afterCopyRotate270, 8);
        Assert.Equal(create90, afterCopyRotateMinus90, 8);
        Assert.Equal(
            TimberStandaloneNativeLeaderOrientationRules
                .CanonicalVerticalTextPresentationRadians,
            afterCopyRotate270,
            8);
    }

    [Fact]
    public void VerticalGeometry_MayStillReflect90Vs270()
    {
        var geometry90 =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                Math.PI / 2d);
        var geometry270 =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                -Math.PI / 2d);
        Assert.Equal(Math.PI / 2d, geometry90, 8);
        Assert.Equal(-Math.PI / 2d, geometry270, 8);
        Assert.NotEqual(geometry90, geometry270, 8);
    }

    [Fact]
    public void VerticalText_90And270_ShareSamePresentation()
    {
        var text90 =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(Math.PI / 2d);
        var text270 =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(3d * Math.PI / 2d);
        Assert.Equal(text90, text270, 8);
        Assert.Equal(Math.PI / 2d, text90, 8);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(35d)]
    [InlineData(-35d)]
    [InlineData(45d)]
    [InlineData(89d)]
    [InlineData(91d)]
    [InlineData(150d)]
    [InlineData(180d)]
    [InlineData(269d)]
    [InlineData(271d)]
    public void NonVerticalAngles_TextEqualsGeometry_Unchanged(double degrees)
    {
        var physical = degrees * Math.PI / 180d;
        Assert.False(
            TimberStandaloneNativeLeaderOrientationRules.IsExactVertical(physical),
            "Guard must not treat non-vertical as vertical.");

        var geometry =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        var text =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(physical);
        var expectedReadable =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
                physical);

        Assert.Equal(expectedReadable, geometry, 10);
        Assert.Equal(geometry, text, 10);
    }

    [Fact]
    public void DimensionsOnly_UsesSameVerticalTextRuleAsPlain()
    {
        // ONLY DESCRIPTION shares Plain's MText presentation helper.
        var dimensionsOnly = ResolveFamilyText(itemStyle: null, Math.PI / 2d);
        var plain = ResolveFamilyText(ItemNumberLeaderStyle.Plain, Math.PI / 2d);
        var dimensions270 = ResolveFamilyText(itemStyle: null, -Math.PI / 2d);
        var plain270 = ResolveFamilyText(ItemNumberLeaderStyle.Plain, -Math.PI / 2d);

        Assert.Equal(plain, dimensionsOnly, 8);
        Assert.Equal(plain270, dimensions270, 8);
        Assert.Equal(dimensionsOnly, dimensions270, 8);
    }

    [Fact]
    public void FramedItemOnly_BlockRotationIsPlainTextPlusPiBase()
    {
        foreach (var style in new[]
                 {
                     ItemNumberLeaderStyle.Circle,
                     ItemNumberLeaderStyle.Rectangle,
                     ItemNumberLeaderStyle.Slot,
                 })
        {
            _ = style;
            foreach (var physical in new[]
                     {
                         0d,
                         Math.PI / 2d,
                         -Math.PI / 2d,
                         Math.PI,
                         35d * Math.PI / 180d,
                     })
            {
                var plain =
                    TimberStandaloneNativeLeaderOrientationRules
                        .ResolveTextPresentationRadians(physical);
                var framed =
                    TimberStandaloneNativeLeaderOrientationRules
                        .ResolveFramedItemOnlyBlockRotationRadians(physical);
                Assert.True(
                    TimberStandaloneNativeLeaderOrientationRules
                        .FramedItemOnlyMatchesPlainTextOrientation(
                            physical,
                            framed,
                            plain));
            }
        }
    }

    [Fact]
    public void HostSource_TextUsesResolveTextPresentation_GeometryUsesTransform()
    {
        var framed = Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AutoCadStandaloneFramedItemOnlyAnnotationService.cs");
        Assert.Contains(
            "ResolveFramedItemOnlyBlockRotationRadians(",
            framed,
            StringComparison.Ordinal);
        Assert.Contains("ResolveTransformRadians(", framed, StringComparison.Ordinal);
        Assert.Contains("geometryRotation", framed, StringComparison.Ordinal);
        Assert.Contains("blockRotation", framed, StringComparison.Ordinal);
        Assert.DoesNotContain("textRotation", framed, StringComparison.Ordinal);

        var labels = Read(
            "src/AcKrovy.AutoCAD/Infrastructure/ElementLabelService.cs");
        var dimensionsText = Member(
            labels,
            "private static void ApplyAbsoluteStandaloneDimensionsLeaderTextOrientation(");
        Assert.Contains(
            "ResolveTextPresentationRadians(",
            dimensionsText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ResolveTransformRadians(",
            dimensionsText,
            StringComparison.Ordinal);

        var plainText = Member(
            labels,
            "private static void ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(");
        Assert.Contains(
            "ResolveTextPresentationRadians(",
            plainText,
            StringComparison.Ordinal);

        var rules = Read(
            "src/AcKrovy.Core/Services/TimberStandaloneNativeLeaderOrientationRules.cs");
        Assert.Contains(
            "CanonicalVerticalTextPresentationRadians",
            rules,
            StringComparison.Ordinal);
        Assert.Contains(
            "Exact 90° and 270° share one BOTTOM→TOP",
            rules,
            StringComparison.Ordinal);
        Assert.Contains(
            "FramedItemOnlyBlockContentBaseCorrectionRadians",
            rules,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HistoricalDefect_270MustNotKeepOppositeMinusNinetyText()
    {
        Assert.NotEqual(
            -Math.PI / 2d,
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(-Math.PI / 2d),
            8);
    }

    private static double ResolveFamilyText(
        ItemNumberLeaderStyle? itemStyle,
        double physical)
    {
        _ = itemStyle;
        return TimberStandaloneNativeLeaderOrientationRules
            .ResolveTextPresentationRadians(physical);
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
