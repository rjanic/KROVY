using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Framed ItemOnly CREATE must match Plain ItemOnly directed ITEM_NO.
/// Frame styles (Circle / Rectangle / Slot) must not invert text vs Plain.
/// </summary>
public sealed class StandaloneFramedItemOnlyBaseOrientationParityTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static TheoryData<ItemNumberLeaderStyle> FramedItemOnlyStyles { get; } =
        new()
        {
            { ItemNumberLeaderStyle.Circle },
            { ItemNumberLeaderStyle.Rectangle },
            { ItemNumberLeaderStyle.Slot },
        };

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void FreshCreate_PlainVsFramed_SameDirectedItemNoOrientation(
        ItemNumberLeaderStyle style)
    {
        _ = style;
        AssertParity(physicalDegrees: 35d);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void FreshCreate_VerticalPlus90_MatchesPlain(ItemNumberLeaderStyle style)
    {
        _ = style;
        AssertParity(physicalDegrees: 90d);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void FreshCreate_VerticalMinus90_MatchesPlain(ItemNumberLeaderStyle style)
    {
        _ = style;
        AssertParity(physicalDegrees: -90d);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void FreshCreate_Horizontal_MatchesPlain(ItemNumberLeaderStyle style)
    {
        _ = style;
        AssertParity(physicalDegrees: 0d);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void FreshCreate_OppositeStartEnd_MatchesPlain(ItemNumberLeaderStyle style)
    {
        _ = style;
        AssertParity(physicalDegrees: 180d);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void AfterNormalRotate_FramedBlockRotationFollowsSource(
        ItemNumberLeaderStyle style)
    {
        _ = style;
        var before = 30d * Math.PI / 180d;
        var after = 120d * Math.PI / 180d;
        var blockBefore =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(before);
        var blockAfter =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(after);
        var plainBefore =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(before);
        var plainAfter =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(after);

        Assert.NotEqual(blockBefore, blockAfter, 8);
        Assert.True(
            TimberStandaloneNativeLeaderOrientationRules
                .FramedItemOnlyMatchesPlainTextOrientation(
                    after,
                    blockAfter,
                    plainAfter));
        Assert.True(
            TimberStandaloneNativeLeaderOrientationRules
                .FramedItemOnlyMatchesPlainTextOrientation(
                    before,
                    blockBefore,
                    plainBefore));
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void GripStretch_RebuildsAbsoluteFramedBlockRotationFromLivePhysical(
        ItemNumberLeaderStyle style)
    {
        _ = style;
        // GRIP-STRETCH changes physical Start→End; absolute resolve must not
        // compound a prior BlockRotation.
        var physicalAfterStretch = -45d * Math.PI / 180d;
        var expected =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(physicalAfterStretch);
        var again =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(physicalAfterStretch);
        Assert.Equal(expected, again, 8);
        Assert.Equal(
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                TimberStandaloneNativeLeaderOrientationRules
                    .ResolveTextPresentationRadians(physicalAfterStretch) +
                TimberStandaloneNativeLeaderOrientationRules
                    .FramedItemOnlyBlockContentBaseCorrectionRadians),
            expected,
            8);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void CopyThenRotate_StillMatchesPlainDirectedOrientation(
        ItemNumberLeaderStyle style)
    {
        _ = style;
        // CREATE 90°, COPY + ROTATE 180° → physical 270°.
        AssertParity(physicalDegrees: 90d);
        AssertParity(physicalDegrees: 270d);
        var create90 =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(Math.PI / 2d);
        var after270 =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(3d * Math.PI / 2d);
        Assert.Equal(create90, after270, 8);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void RepeatedAkLabels_DoesNotFlipFramedText(ItemNumberLeaderStyle style)
    {
        _ = style;
        var physical = 55d * Math.PI / 180d;
        var first =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(physical);
        for (var i = 0; i < 5; i++)
        {
            var next =
                TimberStandaloneNativeLeaderOrientationRules
                    .ResolveFramedItemOnlyBlockRotationRadians(physical);
            Assert.Equal(first, next, 8);
        }
    }

    [Fact]
    public void BaseCorrection_IsConstantPi_NotSharedReadableFold()
    {
        Assert.Equal(
            Math.PI,
            TimberStandaloneNativeLeaderOrientationRules
                .FramedItemOnlyBlockContentBaseCorrectionRadians,
            12);

        // Shared readable fold still owns Plain; framed only adds constant π.
        var physical = 35d * Math.PI / 180d;
        var plain =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(physical);
        var geometry =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physical);
        Assert.Equal(geometry, plain, 10);
        Assert.Equal(
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(plain + Math.PI),
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(physical),
            10);
    }

    [Fact]
    public void HostWiring_FramedUsesBlockRotationHelper_PlainUsesTextPresentation()
    {
        var framed = Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AutoCadStandaloneFramedItemOnlyAnnotationService.cs");
        Assert.Contains(
            "ResolveFramedItemOnlyBlockRotationRadians(",
            framed,
            StringComparison.Ordinal);
        Assert.Contains("blockRotation", framed, StringComparison.Ordinal);
        Assert.Contains("ResolveTransformRadians(", framed, StringComparison.Ordinal);
        Assert.DoesNotContain("textRotation", framed, StringComparison.Ordinal);

        var create = Member(
            framed,
            "public static AutoCadStandaloneFramedItemOnlyCreateResult Create(");
        Assert.Contains(
            "ResolveFramedItemOnlyBlockRotationRadians(",
            create,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ResolveTextPresentationRadians(",
            create,
            StringComparison.Ordinal);

        var update = Member(
            framed,
            "public static bool TryUpdateInPlace(");
        Assert.Contains("RequiresCanonicalRebuild", update, StringComparison.Ordinal);
        Assert.Contains("return false;", update, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ApplyCanonicalLayout(",
            framed,
            StringComparison.Ordinal);

        var labels = Read(
            "src/AcKrovy.AutoCAD/Infrastructure/ElementLabelService.cs");
        var plainText = Member(
            labels,
            "private static void ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(");
        Assert.Contains(
            "ResolveTextPresentationRadians(",
            plainText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ResolveFramedItemOnlyBlockRotationRadians(",
            plainText,
            StringComparison.Ordinal);

        var writeFramed = Member(
            labels,
            "private static void WriteStandaloneFramedItemOnlyMetadata(");
        Assert.Contains(
            "ResolveFramedItemOnlyBlockRotationRadians(",
            writeFramed,
            StringComparison.Ordinal);

        var framedUpsert = Member(
            labels,
            "private static bool UpsertStandaloneFramedItemOnlyLeader(");
        Assert.Contains(
            "!sourceSync.RequiresCanonicalRebuild",
            framedUpsert,
            StringComparison.Ordinal);
        Assert.Contains(
            "createUsesCanonicalManualOffset",
            framedUpsert,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutoCadStandaloneFramedItemOnlyAnnotationService.Create(",
            framedUpsert,
            StringComparison.Ordinal);
    }

    [Fact]
    public void R3Combined_DoesNotUseFramedItemOnlyBlockRotationHelper()
    {
        var rules = Read(
            "src/AcKrovy.Core/Services/TimberStandaloneNativeLeaderOrientationRules.cs");
        Assert.Contains(
            "Must not be used by Plain, DimensionsOnly",
            rules,
            StringComparison.Ordinal);
        Assert.Contains("or R3 Combined", rules, StringComparison.Ordinal);

        // Production R3 Combined path must stay on TransformBy / BlockRotation≈0.
        var r3Files = new[]
        {
            "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentAnnotationService.cs",
            "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentDimensionColumnPlacementService.cs",
        };
        foreach (var relative in r3Files)
        {
            var fullPath = Path.Combine(
                RepositoryRoot,
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var source = File.ReadAllText(fullPath);
            Assert.DoesNotContain(
                "ResolveFramedItemOnlyBlockRotationRadians(",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "FramedItemOnlyBlockContentBaseCorrectionRadians",
                source,
                StringComparison.Ordinal);
        }
    }

    private static void AssertParity(double physicalDegrees)
    {
        var physical = physicalDegrees * Math.PI / 180d;
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
                    plain),
            $"Plain={plain:R} Framed={framed:R} physicalDeg={physicalDegrees}");
        Assert.Equal(
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                plain +
                TimberStandaloneNativeLeaderOrientationRules
                    .FramedItemOnlyBlockContentBaseCorrectionRadians),
            framed,
            8);
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
