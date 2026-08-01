#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadFramedTextAttributeMatrixPolicyTests
{
    [Fact]
    public void Variants_AreDeterministicAndCoverRequiredOperationOrders()
    {
        var variants = AutoCadFramedTextAttributeMatrixPolicy.Variants;

        Assert.Equal(5, variants.Count);
        Assert.Equal(
            [
                AutoCadFramedTextAttributeMatrixVariantKind.PreDatabaseCurrent,
                AutoCadFramedTextAttributeMatrixVariantKind.AppendBeforeSet,
                AutoCadFramedTextAttributeMatrixVariantKind.GetModifySetAfterAppend,
                AutoCadFramedTextAttributeMatrixVariantKind.SecondWriteTransaction,
                AutoCadFramedTextAttributeMatrixVariantKind.BlockScaleAfterSet,
            ],
            variants.Select(variant => variant.Kind));
        Assert.Equal(
            [1d, 2d, 1d, 1d, 2d],
            variants.Select(variant => variant.ExpectedBlockScale));
        Assert.Equal(
            variants.Count,
            variants.Select(variant => variant.Token).Distinct().Count());
        Assert.Equal(
            variants[1].ExpectedBaseHeight,
            variants[4].ExpectedBaseHeight);
        Assert.Equal(
            variants[1].ExpectedBlockScale,
            variants[4].ExpectedBlockScale);
        Assert.NotEqual(
            variants[1].BlockScaleOrder,
            variants[4].BlockScaleOrder);
    }

    [Theory]
    [InlineData(135d, 1d, 135d, 135d)]
    [InlineData(270d, 2d, 135d, 270d)]
    public void Observation_NormalizesHostScaledHeightExactlyOnce(
        double rawHeight,
        double blockScale,
        double expectedBase,
        double expectedEffective)
    {
        var observation = Observation(rawHeight, blockScale, "DEFINITION");

        Assert.True(observation.HasValidBlockScale);
        Assert.Equal(expectedBase, observation.NormalizedBaseHeight);
        Assert.Equal(expectedEffective, observation.ActualEffectiveHeight);
        Assert.NotEqual(540d, observation.ActualEffectiveHeight);
    }

    [Fact]
    public void HostScaledDefinitionHeight_DoesNotPassPerInstanceHeight()
    {
        var variant = AutoCadFramedTextAttributeMatrixPolicy.Variants[1];
        Assert.Equal(100d, variant.ExpectedBaseHeight);
        Assert.Equal(200d, variant.ExpectedEffectiveHeight);

        var result = AutoCadFramedTextAttributeMatrixPolicy.Evaluate(
            variant,
            Observation(270d, 2d, "DEFINITION"),
            "PROOF",
            "DEFINITION",
            distinctStyleOverrideExpected: true);

        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCheckStatus.Fail,
            result.BaseHeightStatus);
        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCheckStatus.Fail,
            result.EffectiveHeightStatus);
        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCheckStatus.Fail,
            result.StyleStatus);
        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCheckStatus.Pass,
            result.BlockScaleStatus);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidBlockScale_CannotProduceHeightOrScalePass(double scale)
    {
        var variant = Variant();
        var observation = Observation(135d, scale, "PROOF");
        var result = AutoCadFramedTextAttributeMatrixPolicy.Evaluate(
            variant,
            observation,
            "PROOF",
            "DEFINITION",
            distinctStyleOverrideExpected: true);

        Assert.False(observation.HasValidBlockScale);
        Assert.Null(observation.NormalizedBaseHeight);
        Assert.Null(observation.ActualEffectiveHeight);
        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCheckStatus.Fail,
            result.BaseHeightStatus);
        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCheckStatus.Fail,
            result.EffectiveHeightStatus);
        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCheckStatus.Fail,
            result.BlockScaleStatus);
    }

    [Fact]
    public void DefinitionStyleMatch_CannotConfirmPerInstanceOverride()
    {
        var variant = Variant();
        var result = AutoCadFramedTextAttributeMatrixPolicy.Evaluate(
            variant,
            Observation(variant.ExpectedEffectiveHeight, 1d, "DEFINITION"),
            "DEFINITION",
            "DEFINITION",
            distinctStyleOverrideExpected: true);

        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCheckStatus.NotTested,
            result.StyleStatus);
    }

    [Fact]
    public void MissingDistinctStyle_MarksStyleAsNotTested()
    {
        var variant = Variant();
        var result = AutoCadFramedTextAttributeMatrixPolicy.Evaluate(
            variant,
            Observation(variant.ExpectedEffectiveHeight, 1d, "ONLY_STYLE"),
            "ONLY_STYLE",
            "OTHER_STYLE",
            distinctStyleOverrideExpected: false);

        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCheckStatus.NotTested,
            result.StyleStatus);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Candidate_RequiresPostCommitBaseHeightAndStylePass(
        bool baseHeightPassed,
        bool stylePassed,
        bool expectedCandidate)
    {
        var result = new AutoCadFramedTextAttributeMatrixVariantResult(
            Variant(),
            Phase(baseHeightPassed: true, stylePassed: true),
            Phase(baseHeightPassed, stylePassed));

        Assert.Equal(expectedCandidate, result.IsHostSupportedCandidate);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Candidate_RequiresPostCommitEffectiveHeightPass(
        bool effectiveHeightPassed,
        bool expectedCandidate)
    {
        var postCommit = Phase(baseHeightPassed: true, stylePassed: true) with
        {
            EffectiveHeightStatus = effectiveHeightPassed
                ? AutoCadFramedTextAttributeMatrixCheckStatus.Pass
                : AutoCadFramedTextAttributeMatrixCheckStatus.Fail,
        };
        var result = new AutoCadFramedTextAttributeMatrixVariantResult(
            Variant(),
            Phase(baseHeightPassed: true, stylePassed: true),
            postCommit);

        Assert.Equal(expectedCandidate, result.IsHostSupportedCandidate);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Candidate_RequiresPostCommitBlockScalePass(
        bool blockScalePassed,
        bool expectedCandidate)
    {
        var postCommit = Phase(baseHeightPassed: true, stylePassed: true) with
        {
            BlockScaleStatus = blockScalePassed
                ? AutoCadFramedTextAttributeMatrixCheckStatus.Pass
                : AutoCadFramedTextAttributeMatrixCheckStatus.Fail,
        };
        var result = new AutoCadFramedTextAttributeMatrixVariantResult(
            Variant(),
            Phase(baseHeightPassed: true, stylePassed: true),
            postCommit);

        Assert.Equal(expectedCandidate, result.IsHostSupportedCandidate);
    }

    [Fact]
    public void Candidate_UsesPostCommitIndependentlyFromPreCommit()
    {
        var result = new AutoCadFramedTextAttributeMatrixVariantResult(
            Variant(),
            Phase(baseHeightPassed: false, stylePassed: false),
            Phase(baseHeightPassed: true, stylePassed: true));

        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCheckStatus.Fail,
            result.PreCommit!.BaseHeightStatus);
        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCheckStatus.Pass,
            result.PostCommit!.BaseHeightStatus);
        Assert.True(result.IsHostSupportedCandidate);
    }

    [Fact]
    public void Outcome_ReportsAllVariantsFailingHeightAndStyle()
    {
        var results = Results(Phase(baseHeightPassed: false, stylePassed: false));

        Assert.Equal(
            AutoCadFramedTextAttributeMatrixOutcome
                .PerInstanceHeightAndStyleNotSupported,
            AutoCadFramedTextAttributeMatrixPolicy.DetermineOutcome(results));
    }

    [Fact]
    public void Outcome_ReportsMixedBaseHeightAndStyleResults()
    {
        var results = Results(Phase(baseHeightPassed: true, stylePassed: false));

        Assert.Equal(
            AutoCadFramedTextAttributeMatrixOutcome.MixedResults,
            AutoCadFramedTextAttributeMatrixPolicy.DetermineOutcome(results));
    }

    [Fact]
    public void CapabilitySummary_ReportsHostMatrixPropertiesSeparately()
    {
        var results = Results(Phase(baseHeightPassed: false, stylePassed: false));

        var summary = AutoCadFramedTextAttributeMatrixPolicy
            .SummarizeCapabilities(results);

        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCapabilityStatus.Supported,
            summary.Token);
        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCapabilityStatus
                .NotSupportedByTestedPaths,
            summary.BaseHeight);
        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCapabilityStatus
                .NotSupportedByTestedPaths,
            summary.TextStyle);
        Assert.Equal(
            AutoCadFramedTextAttributeMatrixCapabilityStatus.Supported,
            summary.BlockScale);
    }

    [Fact]
    public void DefinitionAudit_IdenticalSnapshotsHaveNoDiff()
    {
        var snapshot = DefinitionSnapshot();

        var result = AutoCadFramedTextAttributeMatrixPolicy
            .CompareDefinitionSnapshots(snapshot, snapshot with { });

        Assert.True(result.IntegrityPreserved);
        Assert.Empty(result.ChangedIntegrityFields);
        Assert.All(result.Fields, field => Assert.False(field.HasChanged));
    }

    [Fact]
    public void DefinitionAudit_NamesExactChangedField()
    {
        var before = DefinitionSnapshot();
        var after = before with { WidthFactor = 0.8d };

        var result = AutoCadFramedTextAttributeMatrixPolicy
            .CompareDefinitionSnapshots(before, after);

        Assert.False(result.IntegrityPreserved);
        Assert.Equal(["WidthFactor"], result.ChangedIntegrityFields);
        var changed = Assert.Single(result.Fields, field => field.HasChanged);
        Assert.Equal("WidthFactor", changed.FieldName);
        Assert.Equal("1", changed.Before);
        Assert.Equal("0.80000000000000004", changed.After);
    }

    [Fact]
    public void DefinitionAudit_RuntimeObjectIdDoesNotInvalidateIntegrity()
    {
        var before = DefinitionSnapshot();
        var after = before with { DiagnosticObjectId = "runtime-wrapper-B" };

        var result = AutoCadFramedTextAttributeMatrixPolicy
            .CompareDefinitionSnapshots(before, after);

        Assert.True(result.IntegrityPreserved);
        var objectId = Assert.Single(
            result.Fields,
            field => field.FieldName == "ObjectId");
        Assert.True(objectId.HasChanged);
        Assert.False(objectId.IsIntegrityRelevant);
    }

    [Fact]
    public void MatrixMarker_RoundTripsOnlyKnownSchemaAndVariant()
    {
        foreach (var expected in AutoCadFramedTextAttributeMatrixPolicy.Variants)
        {
            var marker = AutoCadFramedTextAttributeMatrixPolicy.CreateMarker(expected);

            Assert.True(
                AutoCadFramedTextAttributeMatrixPolicy.TryParseMarker(
                    marker,
                    out var actual));
            Assert.Same(expected, actual);
        }

        Assert.False(
            AutoCadFramedTextAttributeMatrixPolicy.TryParseMarker(
                "2|AK23_MATRIX_V1",
                out _));
        Assert.False(
            AutoCadFramedTextAttributeMatrixPolicy.TryParseMarker(
                "1|UNKNOWN",
                out _));
    }

    private static IReadOnlyCollection<
        AutoCadFramedTextAttributeMatrixVariantResult> Results(
            AutoCadFramedTextAttributeMatrixPhaseResult postCommit) =>
        AutoCadFramedTextAttributeMatrixPolicy.Variants
            .Select(variant => new AutoCadFramedTextAttributeMatrixVariantResult(
                variant,
                postCommit,
                postCommit))
            .ToArray();

    private static AutoCadFramedTextAttributeMatrixPhaseResult Phase(
        bool baseHeightPassed,
        bool stylePassed) =>
        new(
            AutoCadFramedTextAttributeMatrixCheckStatus.Pass,
            baseHeightPassed
                ? AutoCadFramedTextAttributeMatrixCheckStatus.Pass
                : AutoCadFramedTextAttributeMatrixCheckStatus.Fail,
            baseHeightPassed
                ? AutoCadFramedTextAttributeMatrixCheckStatus.Pass
                : AutoCadFramedTextAttributeMatrixCheckStatus.Fail,
            stylePassed
                ? AutoCadFramedTextAttributeMatrixCheckStatus.Pass
                : AutoCadFramedTextAttributeMatrixCheckStatus.Fail,
            AutoCadFramedTextAttributeMatrixCheckStatus.Pass);

    private static AutoCadFramedTextAttributeMatrixObservation Observation(
        double rawHeight,
        double scale,
        string styleHandle) =>
        new("AK23_MATRIX_V1", rawHeight, scale, styleHandle, styleHandle);

    private static AutoCadFramedTextAttributeDefinitionAuditSnapshot
        DefinitionSnapshot() =>
        new(
            "runtime-wrapper-A",
            "2A",
            "2B",
            "ITEM_NO",
            "ITEM_NO",
            string.Empty,
            "2C",
            "Standard",
            135d,
            0d,
            0d,
            0d,
            0d,
            0d,
            0d,
            0d,
            1d,
            0d,
            1,
            2,
            false,
            false,
            false,
            false,
            true,
            false,
            "0",
            1,
            "2D",
            0);

    private static AutoCadFramedTextAttributeMatrixCase Variant() =>
        AutoCadFramedTextAttributeMatrixPolicy.Variants[0];
}
#endif
