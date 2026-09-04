using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofExternalImportPolicyTests
{
    [Fact]
    public void NeutralPayload_IsAllowedWithoutMetadataMutation() =>
        Assert.Equal(RoofExternalImportPayloadKind.Neutral, Decide().PayloadKind);

    [Fact]
    public void IndividualBatch_IsAllowed() =>
        Assert.Equal(RoofExternalImportPayloadKind.Individual, Decide(timbers: 2, annotations: 4).PayloadKind);

    [Theory]
    [InlineData(true, false, false, false, false, "malformed-or-unknown-krovy-payload")]
    [InlineData(false, true, false, false, false, "whole-roof-out-of-scope")]
    [InlineData(false, false, true, false, false, "orphan-roof-owned-state")]
    [InlineData(false, false, false, true, false, "conflicting-individual-roles")]
    [InlineData(false, false, false, false, true, "orphan-krovy-annotation")]
    public void UnsafePayload_IsRejectedFailClosed(
        bool malformed,
        bool roof,
        bool roofOwned,
        bool conflicts,
        bool orphan,
        string reason)
    {
        var result = RoofExternalImportPolicy.Classify(new(
            malformed, roof, roofOwned, conflicts, orphan, 1, 1));

        Assert.False(result.IsAllowed);
        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public void OnlyExactMappedNewDestinationMayBeMutated()
    {
        Assert.True(RoofExternalImportPolicy.MayMutate(new(true, true, true, true)));
        Assert.False(RoofExternalImportPolicy.MayMutate(new(true, true, false, true)));
        Assert.False(RoofExternalImportPolicy.MayMutate(new(true, false, true, true)));
        Assert.False(RoofExternalImportPolicy.MayMutate(new(false, true, true, true)));
        Assert.False(RoofExternalImportPolicy.MayMutate(new(true, true, true, false)));
    }

    [Fact]
    public void AppendedObject_InvalidOrErased_IsAbsent()
    {
        Assert.True(Absent(objectIsValid: false, objectIsErased: false, sentinel: false));
        Assert.True(Absent(objectIsValid: true, objectIsErased: true, sentinel: false));
        Assert.True(Absent(objectIsValid: true, objectIsErased: true, sentinel: true, ownerErased: false));
    }

    [Fact]
    public void ImportedLine_SurvivingValid_FailsEvenWhenOwnerErased()
    {
        Assert.False(Absent(
            objectIsValid: true,
            objectIsErased: false,
            sentinel: false,
            ownerIsValid: true,
            ownerErased: true));
    }

    [Fact]
    public void RootBtr_MustStillBeAbsentOrErased_IndependentOfSentinels()
    {
        // Ordinary BTR / entity path: only invalid or erased counts as absent.
        Assert.False(Absent(objectIsValid: true, objectIsErased: false, sentinel: false));
        Assert.True(Absent(objectIsValid: true, objectIsErased: true, sentinel: false));
    }

    [Fact]
    public void BlockBeginOrEnd_AbsentOnlyWhenOwningBtrIsRolledBack()
    {
        Assert.True(Absent(
            objectIsValid: true,
            objectIsErased: false,
            sentinel: true,
            ownerIsValid: true,
            ownerErased: true));
        Assert.True(Absent(
            objectIsValid: true,
            objectIsErased: false,
            sentinel: true,
            ownerIsValid: false,
            ownerErased: false));
        Assert.False(Absent(
            objectIsValid: true,
            objectIsErased: false,
            sentinel: true,
            ownerIsValid: true,
            ownerErased: false));
    }

    [Fact]
    public void UnrelatedEntity_CannotEscapeViaOwnerErasedAlone()
    {
        Assert.False(Absent(
            objectIsValid: true,
            objectIsErased: false,
            sentinel: false,
            ownerIsValid: false,
            ownerErased: true));
    }

    [Fact]
    public void NoBroadOwnerIsErasedPassRule_ForNonSentinels()
    {
        Assert.False(RoofExternalImportRollbackRules.IsAppendedObjectAbsent(
            objectIsValid: true,
            objectIsErased: false,
            isStructuralBlockTableRecordSentinel: false,
            ownerIsValid: true,
            ownerIsErased: true));
    }

    private static bool Absent(
        bool objectIsValid,
        bool objectIsErased,
        bool sentinel,
        bool ownerIsValid = true,
        bool ownerErased = false) =>
        RoofExternalImportRollbackRules.IsAppendedObjectAbsent(
            objectIsValid,
            objectIsErased,
            sentinel,
            ownerIsValid,
            ownerErased);

    private static RoofExternalImportDecision Decide(int timbers = 0, int annotations = 0) =>
        RoofExternalImportPolicy.Classify(new(false, false, false, false, false, timbers, annotations));
}
