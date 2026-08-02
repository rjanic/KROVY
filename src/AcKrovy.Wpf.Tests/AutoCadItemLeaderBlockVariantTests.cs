using System.Globalization;
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadItemLeaderBlockVariantTests
{
    [Fact]
    public void Key_UsesImmutableValueEqualityForCompleteSemanticIdentity()
    {
        var reference = Key();

        Assert.Equal(reference, Key());
        Assert.NotEqual(reference, Key(frame: AutoCadItemLeaderBlockFrameKind.Slot));
        Assert.NotEqual(reference, Key(style: "Krovy"));
        Assert.NotEqual(reference, Key(height: 2.8d));
        Assert.NotEqual(
            Key(frame: AutoCadItemLeaderBlockFrameKind.Slot),
            Key(
                frame: AutoCadItemLeaderBlockFrameKind.Slot,
                size: TimberItemLeaderBlockSize.Medium));
        Assert.All(
            typeof(AutoCadItemLeaderBlockVariantKey).GetProperties(),
            property => Assert.Null(property.SetMethod));
        Assert.Empty(typeof(AutoCadItemLeaderBlockVariantKey).GetConstructors());
    }

    [Fact]
    public void Key_UsesExactResolverCanonicalNameCasing()
    {
        Assert.NotEqual(Key(style: "Standard"), Key(style: "standard"));
        Assert.Equal("Standard", Key(style: "  Standard  ").ResolvedCanonicalTextStyleName);
    }

    [Fact]
    public void Key_RejectsInvalidGeometryStyleHeightAndBaseDenominator()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AutoCadItemLeaderBlockVariantKey.Create(
                AutoCadItemLeaderBlockFrameKind.Circle,
                TimberItemLeaderBlockSize.Small,
                "Standard",
                2.7d,
                geometryVersion: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AutoCadItemLeaderBlockVariantKey.Create(
                AutoCadItemLeaderBlockFrameKind.Circle,
                TimberItemLeaderBlockSize.Small,
                "Standard",
                2.7d,
                baseDenominator: 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => Key(height: 99d));
        Assert.Throws<ArgumentException>(() => Key(style: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Key(size: TimberItemLeaderBlockSize.Medium));
    }

    [Fact]
    public void CanonicalHeight_IsInvariantRoundTripAndDistinguishesAdjacentDoubles()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sk-SK");
            var first = Key(height: 2.7d);
            var second = Key(height: Math.BitIncrement(2.7d));

            Assert.Equal("2.7", first.CanonicalPaperHeight);
            Assert.Equal(
                first.ItemNumberPaperHeightMm,
                double.Parse(first.CanonicalPaperHeight, CultureInfo.InvariantCulture));
            Assert.NotEqual(first.CanonicalPaperHeight, second.CanonicalPaperHeight);
            Assert.NotEqual(
                AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(first),
                AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(second));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Name_IsDeterministicSafeBoundedAndContainsNoRawStyleName()
    {
        var key = Key(style: "Štýl krokvy / Japanese 日本語");

        var first = AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(key);
        var second = AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(key);

        Assert.Equal(first, second);
        Assert.StartsWith("AK_ITEM_CIR_G1_H2700_S", first, StringComparison.Ordinal);
        Assert.True(AutoCadItemLeaderBlockVariantNamePolicy.IsSafeSymbolName(first));
        Assert.InRange(
            first.Length,
            1,
            AutoCadItemLeaderBlockVariantNamePolicy.MaximumSafeSymbolNameLength);
        Assert.DoesNotContain("krokvy", first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("日本語", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Name_ChangesForEveryDefinitionDimension()
    {
        var reference = Name(Key());

        Assert.NotEqual(
            reference,
            Name(Key(
                frame: AutoCadItemLeaderBlockFrameKind.Slot,
                size: TimberItemLeaderBlockSize.Small)));
        Assert.NotEqual(
            reference,
            Name(Key(
                frame: AutoCadItemLeaderBlockFrameKind.Rectangle,
                size: TimberItemLeaderBlockSize.Large)));
        Assert.NotEqual(reference, Name(Key(style: "Krovy")));
        Assert.NotEqual(reference, Name(Key(height: 3.2d)));
    }

    [Fact]
    public void CollisionNames_AreDeterministicDistinctAndSafe()
    {
        var key = Key();
        var canonical = Name(key);
        var collision1 =
            AutoCadItemLeaderBlockVariantNamePolicy.CreateCollisionName(key, 1);
        var repeated =
            AutoCadItemLeaderBlockVariantNamePolicy.CreateCollisionName(key, 1);
        var collision2 =
            AutoCadItemLeaderBlockVariantNamePolicy.CreateCollisionName(key, 2);

        Assert.Equal(collision1, repeated);
        Assert.NotEqual(canonical, collision1);
        Assert.NotEqual(collision1, collision2);
        Assert.True(AutoCadItemLeaderBlockVariantNamePolicy.IsSafeSymbolName(collision1));
    }

    [Fact]
    public void FingerprintPayload_IsExactVersionedAndUnambiguous()
    {
        var key = Key(style: "Štýl", height: Math.BitIncrement(2.7d));
        var payload =
            AutoCadItemLeaderBlockVariantNamePolicy.CreateFingerprintPayload(key);

        Assert.Equal(
            "schema=1|geometry=1|frame=CIR|size=S|styleLength=4|style=Štýl|" +
            "paperHeightMm=2.7000000000000006|baseDenominator=50",
            payload);
    }

    [Fact]
    public void CollisionPolicy_ReusesMatchingCanonicalDefinition()
    {
        var inspected = new List<string>();
        var decision = AutoCadItemLeaderBlockVariantCollisionPolicy.Select(
            Key(),
            name =>
            {
                inspected.Add(name);
                return AutoCadItemLeaderBlockVariantCandidateState.Matching;
            });

        Assert.Equal(
            AutoCadItemLeaderBlockVariantCollisionDecisionKind.Reuse,
            decision.Kind);
        Assert.False(decision.IsCollision);
        Assert.Equal(Name(Key()), decision.CandidateName);
        Assert.Single(inspected);
    }

    [Fact]
    public void CollisionPolicy_NeverMutatesInvalidCanonicalAndCreatesStableSuffix()
    {
        var states = new Queue<AutoCadItemLeaderBlockVariantCandidateState>(
        [
            AutoCadItemLeaderBlockVariantCandidateState.Invalid,
            AutoCadItemLeaderBlockVariantCandidateState.Missing,
        ]);
        var decision = AutoCadItemLeaderBlockVariantCollisionPolicy.Select(
            Key(),
            _ => states.Dequeue());

        Assert.Equal(
            AutoCadItemLeaderBlockVariantCollisionDecisionKind.Create,
            decision.Kind);
        Assert.True(decision.IsCollision);
        Assert.Equal(1, decision.CollisionAttempt);
        Assert.Equal(
            AutoCadItemLeaderBlockVariantNamePolicy.CreateCollisionName(Key(), 1),
            decision.CandidateName);
    }

    [Fact]
    public void CollisionPolicy_RepeatedLookupReusesSameCollisionName()
    {
        AutoCadItemLeaderBlockVariantCandidateState Inspect(string name) =>
            name == Name(Key())
                ? AutoCadItemLeaderBlockVariantCandidateState.Invalid
                : AutoCadItemLeaderBlockVariantCandidateState.Matching;

        var first = AutoCadItemLeaderBlockVariantCollisionPolicy.Select(
            Key(),
            Inspect);
        var second = AutoCadItemLeaderBlockVariantCollisionPolicy.Select(
            Key(),
            Inspect);

        Assert.Equal(first, second);
        Assert.Equal(
            AutoCadItemLeaderBlockVariantCollisionDecisionKind.Reuse,
            first.Kind);
        Assert.True(first.IsCollision);
    }

    [Fact]
    public void CollisionPolicy_ExhaustsOnlyAfterEveryDeterministicCandidateIsInvalid()
    {
        var inspections = 0;
        var decision = AutoCadItemLeaderBlockVariantCollisionPolicy.Select(
            Key(),
            _ =>
            {
                inspections++;
                return AutoCadItemLeaderBlockVariantCandidateState.Invalid;
            });

        Assert.Equal(
            AutoCadItemLeaderBlockVariantCollisionDecisionKind.Exhausted,
            decision.Kind);
        Assert.Equal(
            AutoCadItemLeaderBlockVariantCollisionPolicy.MaximumCollisionAttempts + 1,
            inspections);
    }

    [Fact]
    public void BatchIndex_ReusesOneEntryPerKeyAndSeparatesDifferentKeys()
    {
        var identity = new AutoCadDatabaseIdentityToken(0x1234);
        var index = new AutoCadItemLeaderBlockVariantBatchIndex<string>(identity);
        var firstKey = Key();
        var secondKey = Key(height: 3.2d);

        index.Add(identity, firstKey, "id-A", Name(firstKey), false);
        index.Add(identity, firstKey, "id-A", Name(firstKey), false);
        index.Add(identity, secondKey, "id-B", Name(secondKey), false);

        Assert.Equal(2, index.Count);
        Assert.True(index.TryGet(identity, firstKey, out var first));
        Assert.Equal("id-A", first!.DefinitionId);
        Assert.True(index.TryGet(identity, secondKey, out var second));
        Assert.Equal("id-B", second!.DefinitionId);
    }

    [Fact]
    public void BatchIndex_RejectsDatabaseMismatchAndContradictoryEntry()
    {
        var identity = new AutoCadDatabaseIdentityToken(0x1234);
        var foreign = new AutoCadDatabaseIdentityToken(0x5678);
        var index = new AutoCadItemLeaderBlockVariantBatchIndex<string>(identity);
        var key = Key();
        index.Add(identity, key, "id-A", Name(key), false);

        Assert.Throws<ArgumentException>(() =>
            index.TryGet(foreign, key, out _));
        Assert.Throws<ArgumentException>(() =>
            index.Add(foreign, key, "id-A", Name(key), false));
        Assert.Throws<InvalidOperationException>(() =>
            index.Add(identity, key, "id-B", Name(key), false));
    }

    [Fact]
    public void DefinitionAttribute_ValidItemNoStyleAndBaseHeightPass()
    {
        var result = Validate(Attribute(), "AK_PROOF_ARIAL", 100d);

        Assert.True(result.IsValid);
        Assert.Equal(
            AutoCadItemLeaderBlockVariantValidationReasonCode.Valid,
            result.ReasonCode);
        Assert.All(result.Fields, field => Assert.True(field.Passed));
    }

    [Theory]
    [InlineData(0, 0, 1, null, "ItemNoMissing")]
    [InlineData(1, 0, 1, "WRONG_TAG", "ItemNoWrongTag")]
    [InlineData(2, 2, 1, "ITEM_NO", "ItemNoDuplicate")]
    public void DefinitionInventory_MissingWrongOrDuplicateItemNoFailsSpecifically(
        int attributeCount,
        int itemNoCount,
        int frameCount,
        string? soleTag,
        string expectedReason)
    {
        var result = AutoCadItemLeaderBlockVariantInventoryValidationPolicy
            .Evaluate(attributeCount, itemNoCount, frameCount, soleTag);

        Assert.False(result.IsValid);
        Assert.Equal(expectedReason, result.ReasonCode.ToString());
    }

    [Fact]
    public void DefinitionAttribute_HostDerivedPositionIsDiagnosticNotIdentity()
    {
        var normalizedAfterClose = Attribute() with
        {
            PositionX = -31.125d,
            PositionY = -48.75d,
        };

        var result = Validate(normalizedAfterClose, "AK_PROOF_ARIAL", 100d);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(
            result.Fields,
            field => field.PropertyName.StartsWith(
                "Position",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("A", "AK_PROOF_ARIAL", 100d)]
    [InlineData("B", "AK_PROOF_TIMES", 160d)]
    [InlineData("C", "AK_PROOF_ARIAL", 100d)]
    [InlineData("D", "AK_PROOF_ARIAL", 100d)]
    [InlineData("E", "AK_PROOF_ARIAL", 100d)]
    public void DefinitionAttribute_ProofMatrixUsesDefinitionBaseHeightOnly(
        string token,
        string style,
        double expectedHeight)
    {
        var result = Validate(
            Attribute(style: style, height: expectedHeight),
            style,
            expectedHeight);

        Assert.True(result.IsValid, $"Case {token}: {result.Reason}");
    }

    [Fact]
    public void DefinitionAttribute_CDoesNotScaleDefinitionHeightByBlockScale()
    {
        var validBaseHeight = Validate(Attribute(height: 100d), "AK_PROOF_ARIAL", 100d);
        var incorrectlyScaled = Validate(Attribute(height: 200d), "AK_PROOF_ARIAL", 100d);

        Assert.True(validBaseHeight.IsValid);
        Assert.False(incorrectlyScaled.IsValid);
        Assert.Equal(
            AutoCadItemLeaderBlockVariantValidationReasonCode
                .ItemNoWrongDefinitionHeight,
            incorrectlyScaled.ReasonCode);
    }

    [Fact]
    public void DefinitionAttribute_StyleFailuresAreSpecific()
    {
        var wrongName = Validate(
            Attribute(style: "AK_PROOF_TIMES"),
            "AK_PROOF_ARIAL",
            100d);
        var invalidId = Validate(
            Attribute() with { TextStyleIdIsValid = false },
            "AK_PROOF_ARIAL",
            100d);
        var foreignId = Validate(
            Attribute() with { TextStyleBelongsToDatabase = false },
            "AK_PROOF_ARIAL",
            100d);

        Assert.Equal(
            AutoCadItemLeaderBlockVariantValidationReasonCode
                .ItemNoWrongCanonicalTextStyle,
            wrongName.ReasonCode);
        Assert.Equal(
            AutoCadItemLeaderBlockVariantValidationReasonCode
                .ItemNoInvalidTextStyleId,
            invalidId.ReasonCode);
        Assert.Equal(
            AutoCadItemLeaderBlockVariantValidationReasonCode
                .ItemNoTextStyleDatabaseMismatch,
            foreignId.ReasonCode);
    }

    [Fact]
    public void DefinitionAttribute_HeightUsesNarrowDatabaseTolerance()
    {
        var tolerance = AutoCadItemLeaderBlockVariantAttributeValidationPolicy
            .DatabaseDoubleTolerance;

        Assert.True(Validate(
            Attribute(height: 100d + tolerance / 2d),
            "AK_PROOF_ARIAL",
            100d).IsValid);
        Assert.False(Validate(
            Attribute(height: 100d + tolerance * 2d),
            "AK_PROOF_ARIAL",
            100d).IsValid);
        Assert.False(Validate(
            Attribute(height: 160d),
            "AK_PROOF_ARIAL",
            100d).IsValid);
        Assert.False(Validate(
            Attribute(height: 200d),
            "AK_PROOF_ARIAL",
            100d).IsValid);
    }

    [Fact]
    public void DefinitionAttribute_WrongImmutableFlagReportsExpectedAndActual()
    {
        var result = Validate(
            Attribute() with { LockPositionInBlock = false },
            "AK_PROOF_ARIAL",
            100d);

        Assert.False(result.IsValid);
        Assert.Equal(
            AutoCadItemLeaderBlockVariantValidationReasonCode
                .ItemNoWrongImmutableProperty,
            result.ReasonCode);
        var failure = Assert.Single(result.Fields, field => !field.Passed);
        Assert.Equal("LockPositionInBlock", failure.PropertyName);
        Assert.Equal("True", failure.Expected);
        Assert.Equal("False", failure.Actual);
        Assert.Equal("exact", failure.Tolerance);
    }

    private static AutoCadItemLeaderBlockVariantAttributeValidation Validate(
        AutoCadItemLeaderBlockVariantAttributeSnapshot snapshot,
        string expectedStyle,
        double expectedHeight) =>
        AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Evaluate(
            snapshot,
            expectedStyle,
            expectedHeight);

    private static AutoCadItemLeaderBlockVariantAttributeSnapshot Attribute(
        string style = "AK_PROOF_ARIAL",
        double height = 100d) =>
        new(
            true,
            TimberItemLeaderBlockDefinitionRules.AttributeTag,
            TimberItemLeaderBlockDefinitionRules.AttributeTag,
            string.Empty,
            height,
            "style-id",
            true,
            true,
            true,
            style,
            0d,
            "False",
            0d,
            0d,
            0d,
            0d,
            0d,
            0d,
            0d,
            "TextCenter",
            "TextVerticalMid",
            true,
            false,
            false,
            false,
            false,
            false,
            false,
            true);

    private static AutoCadItemLeaderBlockVariantKey Key(
        AutoCadItemLeaderBlockFrameKind frame =
            AutoCadItemLeaderBlockFrameKind.Circle,
        TimberItemLeaderBlockSize size = TimberItemLeaderBlockSize.Small,
        string style = "Standard",
        double height = 2.7d) =>
        AutoCadItemLeaderBlockVariantKey.Create(frame, size, style, height);

    private static string Name(AutoCadItemLeaderBlockVariantKey key) =>
        AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(key);
}
