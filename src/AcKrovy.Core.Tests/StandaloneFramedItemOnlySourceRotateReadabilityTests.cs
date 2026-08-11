using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Framed ItemOnly Circle/Rectangle/Slot: source ROTATE must converge to the
/// same absolute BlockRotation as fresh CREATE. Plain / Dimensions / FullLabel /
/// R3 Combined are control regressions only (must stay off this path).
/// </summary>
public sealed class StandaloneFramedItemOnlySourceRotateReadabilityTests
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
    public void CircleRectSlot_FreshCreate_MatchesPlainReadable(
        ItemNumberLeaderStyle style)
    {
        _ = style;
        AssertFramedMatchesPlain(physicalDegrees: 90d);
        AssertFramedMatchesPlain(physicalDegrees: 0d);
        AssertFramedMatchesPlain(physicalDegrees: 35d);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void SourceRotate180_MatchesFreshCreateReadable(
        ItemNumberLeaderStyle style)
    {
        _ = style;
        AssertRotateMatchesCreate(beforeDegrees: 90d, deltaDegrees: 180d);
        AssertRotateMatchesCreate(beforeDegrees: 0d, deltaDegrees: 180d);
        AssertRotateMatchesCreate(beforeDegrees: 35d, deltaDegrees: 180d);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void SourceRotatePlus90_MatchesFreshCreateReadable(
        ItemNumberLeaderStyle style)
    {
        _ = style;
        AssertRotateMatchesCreate(beforeDegrees: 0d, deltaDegrees: 90d);
        AssertRotateMatchesCreate(beforeDegrees: 90d, deltaDegrees: 90d);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void SourceRotateMinus90Or270_MatchesFreshCreateReadable(
        ItemNumberLeaderStyle style)
    {
        _ = style;
        AssertRotateMatchesCreate(beforeDegrees: 0d, deltaDegrees: -90d);
        AssertRotateMatchesCreate(beforeDegrees: 90d, deltaDegrees: 270d);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void SourceRotate180_FromOppositeStartEnd_MatchesCreate(
        ItemNumberLeaderStyle style)
    {
        _ = style;
        AssertRotateMatchesCreate(beforeDegrees: 180d, deltaDegrees: 180d);
        AssertRotateMatchesCreate(beforeDegrees: -90d, deltaDegrees: 180d);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void RepeatedRotate180_Twice_ReturnsCanonicalReadable(
        ItemNumberLeaderStyle style)
    {
        _ = style;
        var start = 90d * Math.PI / 180d;
        var afterOne = start + Math.PI;
        var afterTwo = afterOne + Math.PI;
        var create =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(start);
        var once =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(afterOne);
        var twice =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(afterTwo);
        Assert.Equal(create, once, 8);
        Assert.Equal(create, twice, 8);
        Assert.Equal(
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(
                    TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(afterTwo)),
            create,
            8);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void GripStretch_AbsoluteResolve_MatchesCreateForFinalPhysical(
        ItemNumberLeaderStyle style)
    {
        _ = style;
        // GRIP-STRETCH changes physical Start→End; absolute CREATE resolve only.
        AssertFramedMatchesPlain(physicalDegrees: -45d);
        AssertFramedMatchesPlain(physicalDegrees: 120d);
        var sync = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            previousAutomaticTextX: 100d,
            previousAutomaticTextY: 560d,
            previousPhysicalRotationRadians: Math.PI / 2d,
            newAutomaticTextX: 180d,
            newAutomaticTextY: 400d,
            newPhysicalRotationRadians: -Math.PI / 2d);
        Assert.True(sync.RequiresCanonicalRebuild);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void CopyThenRotate_MatchesCreateReadable(
        ItemNumberLeaderStyle style)
    {
        _ = style;
        // CREATE 90°, COPY + ROTATE 180° → physical 270°.
        AssertRotateMatchesCreate(beforeDegrees: 90d, deltaDegrees: 180d);
        AssertFramedMatchesPlain(physicalDegrees: 270d);
    }

    [Theory]
    [MemberData(nameof(FramedItemOnlyStyles))]
    public void AkLabelsAfterRotate_DoesNotFlip_AndIsIdempotent(
        ItemNumberLeaderStyle style)
    {
        _ = style;
        var physical = 3d * Math.PI / 2d;
        var afterRotate =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(physical);
        for (var i = 0; i < 5; i++)
        {
            // Unchanged Automatic*/axis → content-only; absolute resolve stable.
            var sync = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
                previousAutomaticTextX: 10d,
                previousAutomaticTextY: 20d,
                previousPhysicalRotationRadians: physical,
                newAutomaticTextX: 10d,
                newAutomaticTextY: 20d,
                newPhysicalRotationRadians: physical);
            Assert.False(sync.RequiresCanonicalRebuild);
            Assert.Equal(
                afterRotate,
                TimberStandaloneNativeLeaderOrientationRules
                    .ResolveFramedItemOnlyBlockRotationRadians(physical),
                8);
        }
    }

    [Fact]
    public void PiInvariantBlockRotation_IsWhyInPlaceAssignFailsAfterRotate180()
    {
        // Vertical 90°→270° and horizontal 0°→180° keep the same framed
        // BlockRotation (text presentation + π). In-place assign of that same
        // value cannot clear AutoCAD TransformBy residue — host must erase+Create.
        var br90 =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(Math.PI / 2d);
        var br270 =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(3d * Math.PI / 2d);
        var br0 =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(0d);
        var br180 =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(Math.PI);
        Assert.Equal(br90, br270, 8);
        Assert.Equal(br0, br180, 8);

        var syncVertical = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            0d,
            360d,
            Math.PI / 2d,
            0d,
            -360d,
            -Math.PI / 2d);
        Assert.True(syncVertical.RequiresCanonicalRebuild);

        var syncHorizontal = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            100d,
            560d,
            0d,
            -100d,
            560d,
            Math.PI);
        Assert.True(syncHorizontal.RequiresCanonicalRebuild);
    }

    [Fact]
    public void HostWiring_SourceRebuildForcesEraseCreate_NotInPlaceBlockRotation()
    {
        var service = Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AutoCadStandaloneFramedItemOnlyAnnotationService.cs");
        var update = Member(
            service,
            "public static bool TryUpdateInPlace(");
        Assert.Contains("RequiresCanonicalRebuild", update, StringComparison.Ordinal);
        Assert.Contains("return false;", update, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyCanonicalLayout(", service, StringComparison.Ordinal);
        Assert.DoesNotContain("leader.TransformBy(", service, StringComparison.Ordinal);

        var labels = Read(
            "src/AcKrovy.AutoCAD/Infrastructure/ElementLabelService.cs");
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
        Assert.Contains(
            "EraseMainAnnotation(",
            framedUpsert,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Controls_PlainDimensionsFullLabel_DoNotUseFramedBlockRotationHelper()
    {
        var labels = Read(
            "src/AcKrovy.AutoCAD/Infrastructure/ElementLabelService.cs");
        var plain = Member(
            labels,
            "private static void ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(");
        var dimensions = Member(
            labels,
            "private static void ApplyAbsoluteStandaloneDimensionsLeaderTextOrientation(");
        Assert.Contains("ResolveTextPresentationRadians(", plain, StringComparison.Ordinal);
        Assert.Contains(
            "ResolveTextPresentationRadians(",
            dimensions,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ResolveFramedItemOnlyBlockRotationRadians(",
            plain,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ResolveFramedItemOnlyBlockRotationRadians(",
            dimensions,
            StringComparison.Ordinal);

        // FullLabel placement path must not call framed ItemOnly BlockRotation.
        Assert.DoesNotContain(
            "ResolveFramedItemOnlyBlockRotationRadians(",
            Member(labels, "private static LabelPlacement CalculatePlacement("),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Controls_R3Combined_Unchanged_NoFramedItemOnlyBlockRotation()
    {
        foreach (var relative in new[]
                 {
                     "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentAnnotationService.cs",
                     "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentDimensionColumnPlacementService.cs",
                 })
        {
            var source = Read(relative);
            Assert.DoesNotContain(
                "ResolveFramedItemOnlyBlockRotationRadians(",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "FramedItemOnlyBlockContentBaseCorrectionRadians",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "createUsesCanonicalManualOffset",
                source,
                StringComparison.Ordinal);
        }
    }

    private static void AssertRotateMatchesCreate(
        double beforeDegrees,
        double deltaDegrees)
    {
        var before = beforeDegrees * Math.PI / 180d;
        var after = before + (deltaDegrees * Math.PI / 180d);
        var createAfter =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(after);
        var rotatePath =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveFramedItemOnlyBlockRotationRadians(after);
        Assert.Equal(createAfter, rotatePath, 8);
        AssertFramedMatchesPlain(
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(after) *
            180d /
            Math.PI);

        var sync = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
            previousAutomaticTextX: 0d,
            previousAutomaticTextY: 360d,
            previousPhysicalRotationRadians: before,
            newAutomaticTextX: 10d,
            newAutomaticTextY: -360d,
            newPhysicalRotationRadians: after);
        Assert.True(
            sync.RequiresCanonicalRebuild,
            $"Expected rebuild before={beforeDegrees} delta={deltaDegrees}");
    }

    private static void AssertFramedMatchesPlain(double physicalDegrees)
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
