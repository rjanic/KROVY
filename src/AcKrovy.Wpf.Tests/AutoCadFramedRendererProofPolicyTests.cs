#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadFramedRendererProofPolicyTests
{
    [Fact]
    public void Matrix_ContainsStaticSuccessCasesAndSeparateDynamicEContract()
    {
        var cases = AutoCadFramedRendererProofPolicy.Cases;

        Assert.Equal(
            new[] { "A", "B", "C", "D", "F", "G", "H1", "H2", "H3" },
            cases.Select(proofCase => proofCase.Token));
        Assert.Equal("E",
            AutoCadFramedRendererProofPolicy.RectangleLargeCaseTemplate.Token);
        Assert.Contains("NOT TESTED",
            AutoCadFramedRendererProofPolicy.FailureCaseNotTested,
            StringComparison.Ordinal);
        Assert.Equal(
            TimberAnnotationMode.DimensionsWithItemNumber,
            cases.Single(proofCase => proofCase.Token == "F").Mode);
        Assert.Equal(
            TimberMainAnnotationComponentRole.FramedItem,
            cases.Single(proofCase => proofCase.Token == "F").FramedRole);
    }

    [Fact]
    public void Matrix_EncodesRequiredReuseStyleFrameRectangleAndBatchRelationships()
    {
        var byToken = AutoCadFramedRendererProofPolicy.Cases
            .ToDictionary(proofCase => proofCase.Token);

        Assert.Equal(byToken["A"].ItemStyle, byToken["B"].ItemStyle);
        Assert.Equal(byToken["A"].StyleSlot, byToken["B"].StyleSlot);
        Assert.Equal(byToken["A"].ItemNumberPaperHeightMm,
            byToken["B"].ItemNumberPaperHeightMm);
        Assert.NotEqual(byToken["A"].Denominator, byToken["B"].Denominator);
        Assert.NotEqual(byToken["A"].StyleSlot, byToken["C"].StyleSlot);
        Assert.NotEqual(byToken["A"].ItemNumberPaperHeightMm,
            byToken["C"].ItemNumberPaperHeightMm);
        Assert.Equal(ItemNumberLeaderStyle.Slot, byToken["D"].ItemStyle);
        Assert.Equal(ItemNumberLeaderStyle.Rectangle,
            AutoCadFramedRendererProofPolicy.RectangleLargeCaseTemplate.ItemStyle);
        Assert.All(new[] { "H1", "H2", "H3" }, token =>
        {
            Assert.Equal(byToken["A"].ItemStyle, byToken[token].ItemStyle);
            Assert.Equal(byToken["A"].StyleSlot, byToken[token].StyleSlot);
            Assert.Equal(byToken["A"].ItemNumberPaperHeightMm,
                byToken[token].ItemNumberPaperHeightMm);
        });
    }

    [Fact]
    public void ESelection_UsesResolveBasedLargeTokenVT1234()
    {
        Assert.Equal("VT1234",
            AutoCadFramedRendererProofPolicy.RectangleLargeCaseTemplate.ItemText);
        Assert.Contains(
            AutoCadFramedRendererProofPolicy.RectangleLargeFitCandidates,
            candidate => candidate.ItemText == "VT1234");
        Assert.Equal(
            TimberItemLeaderBlockSize.Large,
            TimberItemLeaderBlockDefinitionRules.Resolve(
                ItemNumberLeaderStyle.Rectangle,
                "VT1234").Size);
    }

    [Fact]
    public void ESelection_ReturnsNotTestedInsteadOfFalsePassForExtremeFonts()
    {
        var allTooWide = AutoCadFramedRendererProofPolicy
            .SelectRectangleLargeFitCandidate(
                _ => 1200d,
                mediumInnerWidthMm: 584d,
                largeInnerWidthMm: 1123d);
        var allTooNarrow = AutoCadFramedRendererProofPolicy
            .SelectRectangleLargeFitCandidate(
                _ => 500d,
                mediumInnerWidthMm: 584d,
                largeInnerWidthMm: 1123d);

        Assert.False(allTooWide.IsTested);
        Assert.False(allTooNarrow.IsTested);
        Assert.All(allTooWide.Attempts,
            attempt => Assert.False(attempt.MatchesRequestedRange));
        Assert.All(allTooNarrow.Attempts,
            attempt => Assert.False(attempt.MatchesRequestedRange));
    }

    [Fact]
    public void JSelection_UsesCanonicalValidWidestTokenOrReturnsNotTested()
    {
        var overflow = AutoCadFramedRendererProofPolicy
            .SelectRectangleOverflowCandidate(
                _ => 1800d,
                largeInnerWidthMm: 1123d);
        var noOverflow = AutoCadFramedRendererProofPolicy
            .SelectRectangleOverflowCandidate(
                _ => 1000d,
                largeInnerWidthMm: 1123d);

        Assert.True(overflow.IsTested);
        Assert.Equal("WWWWWWWW2147483647",
            overflow.SelectedCandidate?.ItemText);
        Assert.Equal("WWWWWWWW",
            overflow.SelectedCandidate?.Prefix);
        Assert.True(overflow.Attempts.Single().IsValidProductionToken);
        Assert.False(noOverflow.IsTested);
    }
}
#endif
