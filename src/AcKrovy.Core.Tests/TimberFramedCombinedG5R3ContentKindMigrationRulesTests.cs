using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Same-family R3 Combined content-kind migration contracts.
/// Portable rules only — AutoCAD host validation remains required.
/// </summary>
public sealed class TimberFramedCombinedG5R3ContentKindMigrationRulesTests
{
    public static TheoryData<
        TimberFramedBlockContentKind,
        TimberFramedBlockContentKind,
        TimberFramedBlockContentDimensionColumnSide> SameSideTransitions { get; } =
        new()
        {
            {
                TimberFramedBlockContentKind.Circle,
                TimberFramedBlockContentKind.Rectangle,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide
            },
            {
                TimberFramedBlockContentKind.Circle,
                TimberFramedBlockContentKind.Slot,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide
            },
            {
                TimberFramedBlockContentKind.Rectangle,
                TimberFramedBlockContentKind.Circle,
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide
            },
            {
                TimberFramedBlockContentKind.Rectangle,
                TimberFramedBlockContentKind.Slot,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide
            },
            {
                TimberFramedBlockContentKind.Slot,
                TimberFramedBlockContentKind.Circle,
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide
            },
            {
                TimberFramedBlockContentKind.Slot,
                TimberFramedBlockContentKind.Rectangle,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide
            },
        };

    [Theory]
    [MemberData(nameof(SameSideTransitions))]
    public void SameSide_ContentKindChange_RequiresBlockReplacement(
        TimberFramedBlockContentKind currentKind,
        TimberFramedBlockContentKind requestedKind,
        TimberFramedBlockContentDimensionColumnSide side)
    {
        var currentKey = CreateCombinedKey(currentKind, side);
        Assert.True(
            TimberFramedBlockContentVariantRules.TryParseR3VariantKey(
                currentKey,
                out var currentParse));
        Assert.Equal(currentKind, currentParse.ContentKind);
        Assert.Equal(side, currentParse.ContentVariantSide);

        // Side already matches — the historical defect returned early here.
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.IsContentVariantMatch(
                currentParse.ContentVariantSide,
                side));

        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.ShouldReplaceR3ContentVariant(
                currentParse.ContentKind,
                currentParse.ContentVariantSide,
                requestedKind,
                side),
            "Side-only equality must not bypass content-kind replacement.");

        Assert.False(
            TimberFramedCombinedG5ContentVariantRules.IsR3ContentIdentityMatch(
                currentParse.ContentKind,
                currentParse.ContentVariantSide,
                requestedKind,
                side));

        var requestedKey = CreateCombinedKey(requestedKind, side);
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryVerifyFinalR3ContentIdentity(
                requestedKey,
                requestedKind,
                side,
                out _));
        Assert.False(
            TimberFramedCombinedG5ContentVariantRules.TryVerifyFinalR3ContentIdentity(
                currentKey,
                requestedKind,
                side,
                out var mismatchNote));
        Assert.Contains("content kind mismatch", mismatchNote, StringComparison.Ordinal);
    }

    [Fact]
    public void KnownBrokenCase_CircleRight_To_RectangleRight_RequiresReplacement()
    {
        var current = CreateCombinedKey(
            TimberFramedBlockContentKind.Circle,
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide);
        Assert.True(
            TimberFramedBlockContentVariantRules.TryParseR3VariantKey(
                current,
                out var parse));
        Assert.Equal(TimberFramedBlockContentKind.Circle, parse.ContentKind);
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            parse.ContentVariantSide);

        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.ShouldReplaceR3ContentVariant(
                parse.ContentKind,
                parse.ContentVariantSide,
                TimberFramedBlockContentKind.Rectangle,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide));
    }

    [Fact]
    public void SideChanging_CircleRight_To_RectangleLeft_RequiresReplacement()
    {
        AssertSideChangingReplacement(
            TimberFramedBlockContentKind.Circle,
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            TimberFramedBlockContentKind.Rectangle,
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide);
    }

    [Fact]
    public void SideChanging_SlotLeft_To_CircleRight_RequiresReplacement()
    {
        AssertSideChangingReplacement(
            TimberFramedBlockContentKind.Slot,
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
            TimberFramedBlockContentKind.Circle,
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide);
    }

    [Fact]
    public void IdenticalKindAndSide_IsNoOp()
    {
        var key = CreateCombinedKey(
            TimberFramedBlockContentKind.Rectangle,
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide);
        Assert.True(
            TimberFramedBlockContentVariantRules.TryParseR3VariantKey(key, out var parse));

        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.IsR3ContentIdentityMatch(
                parse.ContentKind,
                parse.ContentVariantSide,
                TimberFramedBlockContentKind.Rectangle,
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide));
        Assert.False(
            TimberFramedCombinedG5ContentVariantRules.ShouldReplaceR3ContentVariant(
                parse.ContentKind,
                parse.ContentVariantSide,
                TimberFramedBlockContentKind.Rectangle,
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide));
    }

    [Fact]
    public void SideOnlyMatch_WithoutKind_IsNotIdentityMatch()
    {
        Assert.False(
            TimberFramedCombinedG5ContentVariantRules.IsR3ContentIdentityMatch(
                currentKind: null,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
                TimberFramedBlockContentKind.Circle,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide));
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.ShouldReplaceR3ContentVariant(
                currentKind: null,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
                TimberFramedBlockContentKind.Circle,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide));
    }

    [Fact]
    public void HostSource_ComparesContentKindBeforeSuccessfulEarlyReturn()
    {
        // Host validation required: live AutoCAD BlockContentId replacement is
        // not proven by these portable source contracts.
        var columnPlacement = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentDimensionColumnPlacementService.cs"));
        var r3Swap = Member(
            columnPlacement,
            "public static bool TrySwapR3ContentVariantIfSideChanged(");

        Assert.Contains("parse.ContentKind", r3Swap, StringComparison.Ordinal);
        Assert.Contains("IsR3ContentIdentityMatch(", r3Swap, StringComparison.Ordinal);
        Assert.Contains(
            "Content-kind comparison MUST occur before any successful early return",
            r3Swap,
            StringComparison.Ordinal);
        Assert.Contains(
            "Side-only equality cannot bypass block replacement",
            r3Swap,
            StringComparison.Ordinal);

        var identityReturn = r3Swap.IndexOf(
            "IsR3ContentIdentityMatch(",
            StringComparison.Ordinal);
        var earlyReturnNote = r3Swap.IndexOf(
            "R3 content kind and variant already match requested identity",
            StringComparison.Ordinal);
        var ensureCall = r3Swap.IndexOf(
            "AcKrovyFramedBlockContentDefinitionService.Ensure(",
            StringComparison.Ordinal);
        Assert.True(identityReturn >= 0);
        Assert.True(earlyReturnNote > identityReturn);
        Assert.True(ensureCall > earlyReturnNote);

        Assert.DoesNotContain(
            "R3 content variant already matches knee-side landing",
            r3Swap,
            StringComparison.Ordinal);

        Assert.Contains("TryVerifyFinalR3ContentIdentity(", r3Swap, StringComparison.Ordinal);
        Assert.Contains(
            "post-replace R3 identity verify failed; restored original BlockContentId",
            r3Swap,
            StringComparison.Ordinal);

        var labels = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/ElementLabelService.cs"));
        var trySync = Member(
            labels,
            "private static bool TrySyncG5CombinedContentVariant(");
        Assert.Contains(
            "EnsureCorrectR3ContentVariantFromFinalGeometry(",
            trySync,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsContentKindMatch(",
            trySync,
            StringComparison.Ordinal);
        Assert.Contains(
            "final physical BTR content kind disagrees with requested style",
            trySync,
            StringComparison.Ordinal);
    }

    private static void AssertSideChangingReplacement(
        TimberFramedBlockContentKind currentKind,
        TimberFramedBlockContentDimensionColumnSide currentSide,
        TimberFramedBlockContentKind requestedKind,
        TimberFramedBlockContentDimensionColumnSide requestedSide)
    {
        var currentKey = CreateCombinedKey(currentKind, currentSide);
        Assert.True(
            TimberFramedBlockContentVariantRules.TryParseR3VariantKey(
                currentKey,
                out var parse));

        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.ShouldReplaceR3ContentVariant(
                parse.ContentKind,
                parse.ContentVariantSide,
                requestedKind,
                requestedSide));

        var requestedKey = CreateCombinedKey(requestedKind, requestedSide);
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryVerifyFinalR3ContentIdentity(
                requestedKey,
                requestedKind,
                requestedSide,
                out _));
    }

    private static string CreateCombinedKey(
        TimberFramedBlockContentKind kind,
        TimberFramedBlockContentDimensionColumnSide side) =>
        TimberFramedBlockContentVariantRules.CreateRawKey(
            kind,
            "MEDIUM",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            side);

    private static string Normalize(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Read(string relativePath)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string Member(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "Missing member: " + signature);
        var brace = source.IndexOf('{', start);
        Assert.True(brace > start);
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
                    return source.Substring(start, i - start + 1);
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
