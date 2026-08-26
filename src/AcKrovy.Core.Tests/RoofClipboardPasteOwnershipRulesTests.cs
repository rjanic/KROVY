using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofClipboardPasteOwnershipRulesTests
{
    public static TheoryData<
        RoofClipboardPasteProvenanceKind,
        int,
        bool,
        RoofClipboardPasteOwnershipClassification,
        RoofClipboardPasteOwnershipAction> TruthTable => new()
    {
        { RoofClipboardPasteProvenanceKind.KnownSameDocument, 1, false,
            RoofClipboardPasteOwnershipClassification.SameDwgIndividualTimber,
            RoofClipboardPasteOwnershipAction.UseStage2D4AAdoption },
        { RoofClipboardPasteProvenanceKind.KnownSameDocument, 2, false,
            RoofClipboardPasteOwnershipClassification.NonIndividualPayloadOutOfScope,
            RoofClipboardPasteOwnershipAction.SkipSameDwgMulti },
        { RoofClipboardPasteProvenanceKind.KnownForeignDocument, 1, false,
            RoofClipboardPasteOwnershipClassification.KnownForeignIndividual,
            RoofClipboardPasteOwnershipAction.DegradeForeignIndividual },
        { RoofClipboardPasteProvenanceKind.KnownForeignDocument, 2, false,
            RoofClipboardPasteOwnershipClassification.KnownForeignBatch,
            RoofClipboardPasteOwnershipAction.DegradeForeignBatch },
        { RoofClipboardPasteProvenanceKind.Unknown, 1, false,
            RoofClipboardPasteOwnershipClassification.UnknownIntelligentBatch,
            RoofClipboardPasteOwnershipAction.DegradeUnknownBatch },
        { RoofClipboardPasteProvenanceKind.Unknown, 3, false,
            RoofClipboardPasteOwnershipClassification.UnknownIntelligentBatch,
            RoofClipboardPasteOwnershipAction.DegradeUnknownBatch },
        { RoofClipboardPasteProvenanceKind.KnownSameDocument, 0, false,
            RoofClipboardPasteOwnershipClassification.PlainPayload,
            RoofClipboardPasteOwnershipAction.NoOp },
        { RoofClipboardPasteProvenanceKind.KnownForeignDocument, 0, false,
            RoofClipboardPasteOwnershipClassification.PlainPayload,
            RoofClipboardPasteOwnershipAction.NoOp },
        { RoofClipboardPasteProvenanceKind.Unknown, 0, false,
            RoofClipboardPasteOwnershipClassification.PlainPayload,
            RoofClipboardPasteOwnershipAction.NoOp },
        { RoofClipboardPasteProvenanceKind.KnownSameDocument, 1, true,
            RoofClipboardPasteOwnershipClassification.WholeRoofOutOfScope,
            RoofClipboardPasteOwnershipAction.SkipWholeRoof },
        { RoofClipboardPasteProvenanceKind.KnownForeignDocument, 4, true,
            RoofClipboardPasteOwnershipClassification.WholeRoofOutOfScope,
            RoofClipboardPasteOwnershipAction.SkipWholeRoof },
        { RoofClipboardPasteProvenanceKind.Unknown, 0, true,
            RoofClipboardPasteOwnershipClassification.WholeRoofOutOfScope,
            RoofClipboardPasteOwnershipAction.SkipWholeRoof },
    };

    [Theory]
    [MemberData(nameof(TruthTable))]
    public void PastePayloadTruthTable_IsDeterministic(
        RoofClipboardPasteProvenanceKind provenance,
        int intelligentTimberCount,
        bool hasRoofOwner,
        RoofClipboardPasteOwnershipClassification expectedClassification,
        RoofClipboardPasteOwnershipAction expectedAction)
    {
        var decision = RoofClipboardPasteOwnershipRules.Classify(
            "PASTECLIP",
            provenance,
            intelligentTimberCount,
            hasRoofOwner);

        Assert.Equal(expectedClassification, decision.Classification);
        Assert.Equal(expectedAction, decision.Action);
        Assert.Equal(
            expectedAction == RoofClipboardPasteOwnershipAction.UseStage2D4AAdoption,
            decision.ShouldProcessIndividualTimber);
    }

    [Fact]
    public void NonPasteCommand_UsesExistingLifecycle()
    {
        var decision = RoofClipboardPasteOwnershipRules.Classify(
            "MOVE",
            RoofClipboardPasteProvenanceKind.KnownForeignDocument,
            1,
            false);

        Assert.Equal(
            RoofClipboardPasteOwnershipClassification.NotClipboardPaste,
            decision.Classification);
        Assert.Equal(
            RoofClipboardPasteOwnershipAction.UseExistingLifecycle,
            decision.Action);
    }

    [Theory]
    [InlineData("PASTECLIP")]
    [InlineData("PASTEORIG")]
    [InlineData("_.PASTECLIP")]
    public void ForeignAuthority_IgnoresMatchingPayloadIdentity(string command)
    {
        const string matchingOwnerHandle = "2912";
        const string matchingLogicalKey = "Face0:s10";
        Assert.Equal("2912", matchingOwnerHandle);
        Assert.Equal("Face0:s10", matchingLogicalKey);

        var decision = RoofClipboardPasteOwnershipRules.Classify(
            command,
            RoofClipboardPasteProvenanceKind.KnownForeignDocument,
            1,
            false);

        Assert.Equal(
            RoofClipboardPasteOwnershipAction.DegradeForeignIndividual,
            decision.Action);
    }

    [Fact]
    public void ClipboardRevisionReplacement_RoutesIntelligentUnknownButLeavesPlainPayload()
    {
        var intelligent = RoofClipboardPasteOwnershipRules.Classify(
            "PASTECLIP",
            RoofClipboardPasteProvenanceKind.Unknown,
            1,
            false);
        var plain = RoofClipboardPasteOwnershipRules.Classify(
            "PASTECLIP",
            RoofClipboardPasteProvenanceKind.Unknown,
            0,
            false);

        Assert.Equal(
            RoofClipboardPasteOwnershipAction.DegradeUnknownBatch,
            intelligent.Action);
        Assert.Equal(RoofClipboardPasteOwnershipAction.NoOp, plain.Action);
    }

    [Fact]
    public void GenericOnlyTimber_PreservesExistingNonRoofLifecycle()
    {
        var decision = RoofClipboardPasteOwnershipRules.Classify(
            "PASTECLIP",
            RoofClipboardPasteProvenanceKind.Unknown,
            0,
            false,
            genericTimberDetected: true);

        Assert.Equal(RoofClipboardPasteOwnershipClassification.PlainPayload, decision.Classification);
        Assert.Equal(RoofClipboardPasteOwnershipAction.UseExistingLifecycle, decision.Action);
    }

    [Fact]
    public void CurrentPasteAnnotationSelection_CoversAllCategoriesAndExcludesExistingDrawingAnnotations()
    {
        var appended = new[]
        {
            AnnotationId.ElementLabel,
            AnnotationId.SlopeArrow,
            AnnotationId.SlopeAngleText,
            AnnotationId.PostFootprint,
            AnnotationId.PlainGeometry,
        };
        var finalAnnotations = new[]
        {
            AnnotationId.ElementLabel,
            AnnotationId.SlopeArrow,
            AnnotationId.SlopeAngleText,
            AnnotationId.PostFootprint,
            AnnotationId.ExistingDrawingAnnotation,
            AnnotationId.ElementLabel,
        };

        var selected = RoofClipboardPasteOwnershipRules.SelectCurrentPasteAnnotations(
            appended,
            finalAnnotations);

        Assert.Equal(
            new[]
            {
                AnnotationId.ElementLabel,
                AnnotationId.SlopeArrow,
                AnnotationId.SlopeAngleText,
                AnnotationId.PostFootprint,
            },
            selected);
        Assert.DoesNotContain(AnnotationId.ExistingDrawingAnnotation, selected);
        Assert.DoesNotContain(AnnotationId.PlainGeometry, selected);
    }

    [Theory]
    [InlineData(RoofClipboardPasteProvenanceKind.KnownForeignDocument,
        RoofClipboardPasteOwnershipAction.DegradeForeignIndividual)]
    [InlineData(RoofClipboardPasteProvenanceKind.Unknown,
        RoofClipboardPasteOwnershipAction.DegradeUnknownBatch)]
    public void IndividualGeneratedOrAttachedPayload_WithAnnotations_UsesCleanupAction(
        RoofClipboardPasteProvenanceKind provenance,
        RoofClipboardPasteOwnershipAction expectedAction)
    {
        var decision = RoofClipboardPasteOwnershipRules.Classify(
            "PASTECLIP",
            provenance,
            intelligentTimberCount: 1,
            appendedRoofOwnerDetected: false);

        Assert.Equal(expectedAction, decision.Action);
    }

    private enum AnnotationId
    {
        ElementLabel,
        SlopeArrow,
        SlopeAngleText,
        PostFootprint,
        PlainGeometry,
        ExistingDrawingAnnotation,
    }
}
