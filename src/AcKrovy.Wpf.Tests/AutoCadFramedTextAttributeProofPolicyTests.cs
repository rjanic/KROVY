#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadFramedTextAttributeProofPolicyTests
{
    [Fact]
    public void Cases_AreDeterministicAndUseOneBaseDenominator()
    {
        var cases = AutoCadFramedTextAttributeProofPolicy.Cases;

        Assert.Equal(3, cases.Count);
        Assert.Equal(
            ["AK23_PROOF_A", "AK23_PROOF_B", "AK23_PROOF_C"],
            cases.Select(proofCase => proofCase.Token));
        Assert.Equal(
            [
                AutoCadFramedTextAttributeProofStyleSlot.StyleA,
                AutoCadFramedTextAttributeProofStyleSlot.StyleB,
                AutoCadFramedTextAttributeProofStyleSlot.StyleA,
            ],
            cases.Select(proofCase => proofCase.StyleSlot));
        Assert.All(
            cases,
            proofCase => Assert.Equal(
                proofCase.ItemNumberPaperHeightMm *
                    TimberAnnotationScaleRules.DefaultDenominator,
                proofCase.BaseAttributeHeight));
    }

    [Theory]
    [InlineData("AK23_PROOF_A", 2d, 50, 100d, 1d, 100d)]
    [InlineData("AK23_PROOF_B", 3.2d, 50, 160d, 1d, 160d)]
    [InlineData("AK23_PROOF_C", 2.7d, 100, 135d, 2d, 270d)]
    public void Cases_HaveExpectedBaseScaleAndEffectiveHeight(
        string token,
        double paperHeight,
        int denominator,
        double baseHeight,
        double blockScale,
        double effectiveHeight)
    {
        var proofCase = Assert.Single(
            AutoCadFramedTextAttributeProofPolicy.Cases,
            candidate => candidate.Token == token);

        Assert.Equal(paperHeight, proofCase.ItemNumberPaperHeightMm);
        Assert.Equal(denominator, proofCase.AnnotationScaleDenominator);
        Assert.Equal(baseHeight, proofCase.BaseAttributeHeight);
        Assert.Equal(blockScale, proofCase.BlockScale);
        Assert.Equal(effectiveHeight, proofCase.EffectiveModelHeight);
        Assert.Equal(
            paperHeight * denominator,
            proofCase.EffectiveModelHeight);
    }

    [Fact]
    public void CaseC_DoesNotApplyDenominatorTwice()
    {
        var proofCase = AutoCadFramedTextAttributeProofPolicy.Cases[2];

        Assert.Equal(135d, proofCase.BaseAttributeHeight);
        Assert.NotEqual(
            proofCase.ItemNumberPaperHeightMm *
                proofCase.AnnotationScaleDenominator,
            proofCase.BaseAttributeHeight);
        Assert.Equal(
            proofCase.ItemNumberPaperHeightMm *
                proofCase.AnnotationScaleDenominator,
            proofCase.BaseAttributeHeight * proofCase.BlockScale);
    }

    [Fact]
    public void Payload_RoundTripsUnicodeStyleNameAcrossAsciiChunks()
    {
        var payload = Payload("Štýl krokvy 日本語");

        var chunks = AutoCadFramedTextAttributeProofPolicy.SerializePayload(payload);
        var parsed = AutoCadFramedTextAttributeProofPolicy.TryDeserializePayload(
            chunks,
            out var result);

        Assert.True(parsed);
        Assert.Equal(payload, result);
        Assert.All(
            chunks,
            chunk => Assert.InRange(
                chunk.Length,
                1,
                AutoCadFramedTextAttributeProofPolicy.XDataAsciiChunkLength));
    }

    [Fact]
    public void Payload_RejectsCorruptOrInconsistentData()
    {
        Assert.False(
            AutoCadFramedTextAttributeProofPolicy.TryDeserializePayload(
                ["not-base64"],
                out _));

        var invalid = Payload("Standard") with
        {
            ExpectedBaseAttributeHeight = 270d,
        };
        Assert.Throws<ArgumentException>(() =>
            AutoCadFramedTextAttributeProofPolicy.SerializePayload(invalid));
    }

    [Fact]
    public void SnapshotsMatch_UsesExistingComparisonTolerance()
    {
        var snapshot = Snapshot();
        var withinTolerance = snapshot with
        {
            Height = snapshot.Height +
                AcKrovy.Cad.Abstractions.Layers
                    .CadLayerScaleHydrationRules.ComparisonTolerance / 2d,
        };
        var outsideTolerance = snapshot with
        {
            Height = snapshot.Height +
                AcKrovy.Cad.Abstractions.Layers
                    .CadLayerScaleHydrationRules.ComparisonTolerance * 2d,
        };

        Assert.True(
            AutoCadFramedTextAttributeProofPolicy.SnapshotsMatch(
                snapshot,
                withinTolerance));
        Assert.False(
            AutoCadFramedTextAttributeProofPolicy.SnapshotsMatch(
                snapshot,
                outsideTolerance));
    }

    [Fact]
    public void ResultFactories_CreateOnlyConsistentStates()
    {
        var pass = AutoCadFramedTextAttributeProofCheckResult.Evaluated(
            "height",
            true,
            "100",
            "100");
        var fail = AutoCadFramedTextAttributeProofCheckResult.Evaluated(
            "height",
            false,
            "100",
            "160");
        var notTested = AutoCadFramedTextAttributeProofCheckResult.NotTested(
            "style variation",
            "Only one compatible style exists.");
        var invalid =
            AutoCadFramedTextAttributeProofCheckResult.InvalidEnvironment(
                "style",
                "Expected style was removed.");

        Assert.Equal(AutoCadFramedTextAttributeProofStatus.Pass, pass.Status);
        Assert.False(pass.IsFailure);
        Assert.Equal(AutoCadFramedTextAttributeProofStatus.Fail, fail.Status);
        Assert.True(fail.IsFailure);
        Assert.Equal(
            AutoCadFramedTextAttributeProofStatus.NotTested,
            notTested.Status);
        Assert.Null(notTested.Expected);
        Assert.Null(notTested.Actual);
        Assert.Equal(
            AutoCadFramedTextAttributeProofStatus.InvalidEnvironment,
            invalid.Status);
        Assert.True(invalid.IsInvalidEnvironment);
        Assert.Empty(
            typeof(AutoCadFramedTextAttributeProofCheckResult)
                .GetConstructors());
    }

    [Fact]
    public void ResultFactories_RejectInvalidCombinations()
    {
        Assert.Throws<ArgumentException>(() =>
            AutoCadFramedTextAttributeProofCheckResult.Evaluated(
                string.Empty,
                true,
                "expected",
                "actual"));
        Assert.Throws<ArgumentNullException>(() =>
            AutoCadFramedTextAttributeProofCheckResult.Evaluated(
                "check",
                true,
                null!,
                "actual"));
        Assert.Throws<ArgumentException>(() =>
            AutoCadFramedTextAttributeProofCheckResult.NotTested(
                "check",
                " "));
        Assert.Throws<ArgumentException>(() =>
            AutoCadFramedTextAttributeProofCheckResult.InvalidEnvironment(
                "check",
                string.Empty));
    }

    private static AutoCadFramedTextAttributeProofPayload Payload(
        string styleName) =>
        AutoCadFramedTextAttributeProofPolicy.CreatePayload(
            AutoCadFramedTextAttributeProofPolicy.Cases[0],
            styleName,
            "1A",
            "2B",
            distinctStyleComparisonExpected: true,
            Snapshot());

    private static AutoCadFramedTextAttributeDefinitionSnapshot Snapshot() =>
        new(
            "3C",
            "1A",
            "ITEM_NO",
            "Item number",
            135d,
            string.Empty,
            0d,
            0d,
            0d,
            0d,
            0d,
            0d,
            0d,
            1,
            2,
            false,
            false,
            true);
}
#endif
