using AcKrovy.Core.Services.Roofs;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofClipboardProvenanceLifecycleTests
{
    [Fact]
    public void SuccessfulCopy_PublishesOnlyAtCommandEnded_AndSurvivesHarmlessCommands()
    {
        var state = State();
        var document = new object();
        var database = new object();
        var payload = new object();

        state.BeginCopy(document, database, "COPYCLIP", payload);

        Assert.False(state.HasDurableProvenance);
        Assert.True(state.CompleteCopy(101));
        Assert.True(state.HasDurableProvenance);
        // PAN/ZOOM/view commands intentionally cause no provenance transition.
        Assert.True(state.BeginPaste(document, database, 101).IsValid);
        Assert.Same(payload, state.ActivePayload);
    }

    [Fact]
    public void CancelledCopy_DoesNotPublishProvenance() =>
        AssertCancelledOrFailedCopyDoesNotPublish();

    [Fact]
    public void FailedCopy_DoesNotPublishProvenance() =>
        AssertCancelledOrFailedCopyDoesNotPublish();

    private static void AssertCancelledOrFailedCopyDoesNotPublish()
    {
        var state = State();
        var document = new object();
        var database = new object();
        state.BeginCopy(document, database, "COPYCLIP", new object());

        state.CancelOrFailCopy(document);

        Assert.False(state.HasDurableProvenance);
        Assert.False(state.BeginPaste(document, database, 101).IsValid);
    }

    [Fact]
    public void CopyA_RepeatedPasteA_RemainsValidWhileClipboardRevisionIsUnchanged()
    {
        var state = State();
        var documentA = new object();
        var databaseA = new object();
        state.BeginCopy(documentA, databaseA, "COPYCLIP", new object());
        Assert.True(state.CompleteCopy(101));

        for (var pasteNumber = 1; pasteNumber <= 3; pasteNumber++)
        {
            var decision = state.BeginPaste(documentA, databaseA, 101);

            Assert.True(decision.IsValid);
            Assert.True(decision.ClipboardRevisionMatches);
            state.CompletePaste();
            Assert.True(state.HasDurableProvenance);
        }
    }

    [Fact]
    public void CopyA_PasteB_ThenPasteA_RejectsBAndReusesA()
    {
        var state = State();
        var documentA = new object();
        var databaseA = new object();
        var documentB = new object();
        var databaseB = new object();
        state.BeginCopy(documentA, databaseA, "COPYCLIP", new object());
        Assert.True(state.CompleteCopy(101));

        var foreign = state.BeginPaste(documentB, databaseB, 101);
        state.CompletePaste();
        var backInA = state.BeginPaste(documentA, databaseA, 101);

        Assert.False(foreign.IsValid);
        Assert.False(foreign.SameDocument);
        Assert.False(foreign.DatabaseReferenceEqual);
        Assert.False(foreign.SameDrawing);
        Assert.True(backInA.IsValid);
    }

    [Theory]
    [InlineData("COPYCLIP", "PASTECLIP")]
    [InlineData("COPYBASE", "PASTECLIP")]
    [InlineData("COPYBASE", "PASTEORIG")]
    public void SupportedClipboardPairs_RemainValidForRepeatedPastes(
        string sourceCommand,
        string pasteCommand)
    {
        var state = State();
        var document = new object();
        var database = new object();
        Assert.True(LiveGeometryCommandRules.IsClipboardCopySourceCommand(sourceCommand));
        Assert.True(LiveGeometryCommandRules.IsClipboardPasteCommand(pasteCommand));
        state.BeginCopy(document, database, sourceCommand, new object());
        Assert.True(state.CompleteCopy(101));

        for (var pasteNumber = 1; pasteNumber <= 3; pasteNumber++)
        {
            Assert.True(state.BeginPaste(document, database, 101).IsValid);
            state.CompletePaste();
            Assert.True(state.HasDurableProvenance);
        }
    }

    [Fact]
    public void CopyA_ThenSuccessfulCopyB_ReplacesProvenanceWithB()
    {
        var state = State();
        var documentA = new object();
        var databaseA = new object();
        var documentB = new object();
        var databaseB = new object();
        state.BeginCopy(documentA, databaseA, "COPYCLIP", new object());
        Assert.True(state.CompleteCopy(101));
        state.BeginCopy(documentB, databaseB, "COPYBASE", new object());
        Assert.True(state.CompleteCopy(102));

        Assert.False(state.BeginPaste(documentA, databaseA, 102).IsValid);
        Assert.True(state.BeginPaste(documentB, databaseB, 102).IsValid);
    }

    [Fact]
    public void CancelledReplacementCopy_PreservesPreviouslyPublishedClipboardPayload()
    {
        var state = State();
        var document = new object();
        var database = new object();
        var originalPayload = new object();
        state.BeginCopy(document, database, "COPYCLIP", originalPayload);
        Assert.True(state.CompleteCopy(101));
        state.BeginCopy(document, database, "COPYCLIP", new object());

        state.CancelOrFailCopy(document);
        var decision = state.BeginPaste(document, database, 101);

        Assert.True(decision.IsValid);
        Assert.Same(originalPayload, state.ActivePayload);
    }

    [Fact]
    public void CancelledEligiblePaste_RetainsTokenForRetry() =>
        AssertCancelledOrFailedPasteRetainsToken();

    [Fact]
    public void FailedEligiblePaste_RetainsTokenForRetry() =>
        AssertCancelledOrFailedPasteRetainsToken();

    private static void AssertCancelledOrFailedPasteRetainsToken()
    {
        var state = State();
        var document = new object();
        var database = new object();
        state.BeginCopy(document, database, "COPYCLIP", new object());
        Assert.True(state.CompleteCopy(101));
        Assert.True(state.BeginPaste(document, database, 101).IsValid);

        state.CancelOrFailPaste();

        Assert.True(state.HasDurableProvenance);
        Assert.True(state.BeginPaste(document, database, 101).IsValid);
    }

    [Fact]
    public void ClipboardReplacement_InvalidatesStaleSameDwgToken()
    {
        var state = State();
        var document = new object();
        var database = new object();
        state.BeginCopy(document, database, "COPYCLIP", new object());
        Assert.True(state.CompleteCopy(101));

        var decision = state.BeginPaste(document, database, 102);

        Assert.False(decision.IsValid);
        Assert.False(decision.ClipboardRevisionMatches);
        Assert.Equal("clipboard-replaced", decision.DiagnosticResult);
        Assert.False(state.HasDurableProvenance);
    }

    [Fact]
    public void SameDocumentWithDifferentDatabaseWrapper_IsValidOpenDrawing()
    {
        var state = State();
        var document = new object();
        var databaseA = new object();
        var databaseB = new object();
        state.BeginCopy(document, databaseA, "COPYCLIP", new object());
        Assert.True(state.CompleteCopy(101));

        var decision = state.BeginPaste(document, databaseB, 101);

        Assert.True(decision.IsValid);
        Assert.True(decision.SameDocument);
        Assert.False(decision.DatabaseReferenceEqual);
        Assert.True(decision.SameDrawing);
        Assert.Equal("same-dwg", decision.DiagnosticResult);
    }

    [Fact]
    public void DifferentDocumentsWithSameDatabaseWrapper_AreCrossDwgAndRejected()
    {
        var state = State();
        var documentA = new object();
        var documentB = new object();
        var equivalentLookingDatabase = new object();
        state.BeginCopy(documentA, equivalentLookingDatabase, "COPYCLIP", new object());
        Assert.True(state.CompleteCopy(101));

        var decision = state.BeginPaste(documentB, equivalentLookingDatabase, 101);

        Assert.False(decision.IsValid);
        Assert.False(decision.SameDocument);
        Assert.True(decision.DatabaseReferenceEqual);
        Assert.False(decision.SameDrawing);
        Assert.Equal("different-document", decision.DiagnosticResult);
    }

    [Fact]
    public void DestroyingSourceDocumentClearsAllReferencedState()
    {
        var state = State();
        var document = new object();
        var database = new object();
        state.BeginCopy(document, database, "COPYCLIP", new object());
        Assert.True(state.CompleteCopy(101));

        state.ClearForDocument(document);

        Assert.False(state.HasDurableProvenance);
        Assert.False(state.BeginPaste(document, database, 101).IsValid);
    }

    [Fact]
    public void ReplacementDocumentCannotReuseClearedProvenance_EvenWithSameDatabaseWrapper()
    {
        var state = State();
        var destroyedDocument = new object();
        var replacementDocument = new object();
        var database = new object();
        state.BeginCopy(destroyedDocument, database, "COPYCLIP", new object());
        Assert.True(state.CompleteCopy(101));

        state.ClearForDocument(destroyedDocument);
        var decision = state.BeginPaste(replacementDocument, database, 101);

        Assert.False(decision.IsValid);
        Assert.False(decision.HasProvenance);
        Assert.Equal("missing-provenance", decision.DiagnosticResult);
    }

    private static RoofClipboardProvenanceLifecycle<object, object, object> State() => new();
}
