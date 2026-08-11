using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// DimensionsOnly + FullLabel directed text readability must match Plain ItemOnly.
/// Vertical 90° and 270° share BOTTOM→TOP presentation.
/// </summary>
public sealed class DimensionsOnlyAndFullLabelReadabilityParityTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static TheoryData<double> PhysicalDegreesCases { get; } =
        new()
        {
            { 0d },
            { 90d },
            { 180d },
            { 270d },
            { -90d },
            { 35d },
            { -35d },
        };

    [Theory]
    [MemberData(nameof(PhysicalDegreesCases))]
    public void DimensionsOnly_TextPresentation_MatchesPlainItemOnly(
        double physicalDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var plain =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(physical);
        var dimensions =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(physical);
        Assert.Equal(plain, dimensions, 12);
        Assert.True(
            TimberStandaloneNativeLeaderOrientationRules.IsCanonicalTextPresentation(
                physical,
                dimensions));
    }

    [Theory]
    [MemberData(nameof(PhysicalDegreesCases))]
    public void FullLabel_TextPresentation_MatchesPlainItemOnly(
        double physicalDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var plain =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(physical);
        // FullLabel host maps physical Start→End through the same presentation
        // helper (offset still uses calculator readable fold).
        var fullLabel =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(physical);
        Assert.Equal(plain, fullLabel, 12);
    }

    [Theory]
    [InlineData(90d)]
    [InlineData(270d)]
    [InlineData(-90d)]
    public void Vertical_DimensionsAndFullLabel_AreBottomToTop(double physicalDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var text =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(physical);
        Assert.Equal(
            TimberStandaloneNativeLeaderOrientationRules
                .CanonicalVerticalTextPresentationRadians,
            text,
            12);
    }

    [Theory]
    [MemberData(nameof(PhysicalDegreesCases))]
    public void CreateRotateGrip_ConvergeToSameReadablePresentation(
        double physicalDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
        var create =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(physical);
        var rotate =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(physical);
        var grip =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(physical);
        var copyRotate =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(physical);
        Assert.Equal(create, rotate, 12);
        Assert.Equal(create, grip, 12);
        Assert.Equal(create, copyRotate, 12);
    }

    [Fact]
    public void CopyRotate180_From90_To270_StaysBottomToTop_ForDimensionsAndFullLabel()
    {
        var create90 =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(Math.PI / 2d);
        var after270 =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(3d * Math.PI / 2d);
        Assert.Equal(create90, after270, 12);
        Assert.Equal(Math.PI / 2d, after270, 12);
    }

    [Fact]
    public void AkLabels_IdempotentTextPresentation()
    {
        var physical = 55d * Math.PI / 180d;
        var first =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(physical);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(
                first,
                TimberStandaloneNativeLeaderOrientationRules
                    .ResolveTextPresentationRadians(physical),
                12);
        }
    }

    [Fact]
    public void HostWiring_DimensionsUsesTextPresentation_FullLabelUsesSameHelper()
    {
        var labels = Read(
            "src/AcKrovy.AutoCAD/Infrastructure/ElementLabelService.cs");
        var dimensions = Member(
            labels,
            "private static void ApplyAbsoluteStandaloneDimensionsLeaderTextOrientation(");
        Assert.Contains(
            "ResolveTextPresentationRadians(",
            dimensions,
            StringComparison.Ordinal);

        var placement = Member(
            labels,
            "private static LabelPlacement CalculatePlacement(");
        Assert.Contains(
            "ResolveTextPresentationRadians(",
            placement,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "placement.RotationRadians",
            placement,
            StringComparison.Ordinal);

        var plain = Member(
            labels,
            "private static void ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(");
        Assert.Contains(
            "ResolveTextPresentationRadians(",
            plain,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Controls_PlainFramedItemOnlyAndR3HelpersUnchanged()
    {
        var rules = Read(
            "src/AcKrovy.Core/Services/TimberStandaloneNativeLeaderOrientationRules.cs");
        Assert.Contains(
            "FramedItemOnlyBlockContentBaseCorrectionRadians = Math.PI",
            rules,
            StringComparison.Ordinal);
        Assert.Contains(
            "CanonicalVerticalTextPresentationRadians = Math.PI / 2d",
            rules,
            StringComparison.Ordinal);

        var framed = Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AutoCadStandaloneFramedItemOnlyAnnotationService.cs");
        Assert.Contains(
            "ResolveFramedItemOnlyBlockRotationRadians(",
            framed,
            StringComparison.Ordinal);

        // R3 keep shared readable fold for BlockContent (not FullLabel text helper).
        var r3Rules = Read(
            "src/AcKrovy.Core/Services/TimberFramedBlockContentOrientationRules.cs");
        Assert.Contains(
            "NormalizeReadableRotationRadians(",
            r3Rules,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ResolveTextPresentationRadians(",
            r3Rules,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FullLabel_DoesNotBecomeLeaderRepresentation()
    {
        var labels = Read(
            "src/AcKrovy.AutoCAD/Infrastructure/ElementLabelService.cs");
        var placement = Member(
            labels,
            "private static LabelPlacement CalculatePlacement(");
        Assert.DoesNotContain("MLeader", placement, StringComparison.Ordinal);
        Assert.Contains(
            "TimberElementLabelPlacementCalculator.Calculate(",
            placement,
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
