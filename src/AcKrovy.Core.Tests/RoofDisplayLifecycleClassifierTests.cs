using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDisplayLifecycleClassifierTests
{
    [Fact]
    public void CurrentDisplayWithoutGroup_IsRehydratableNotMissing()
    {
        var validation = new RoofDisplayValidationResult(
            RoofDisplayState.Current,
            RoofDisplayValidationIssue.None);

        var kind = RoofDisplayLifecycleClassifier.Classify(validation, groupIsCurrent: false);

        Assert.Equal(RoofDisplayLifecycleKind.GroupMissingRehydratable, kind);
        Assert.NotEqual(RoofDisplayLifecycleKind.MissingDisplay, kind);
    }

    [Fact]
    public void CurrentDisplayWithCurrentGroup_RemainsReadOnlyCurrent()
    {
        var validation = new RoofDisplayValidationResult(
            RoofDisplayState.Current,
            RoofDisplayValidationIssue.None);

        Assert.Equal(
            RoofDisplayLifecycleKind.Current,
            RoofDisplayLifecycleClassifier.Classify(validation, groupIsCurrent: true));
    }

    [Fact]
    public void TrueMissingDisplay_KeepsCreateDisplayWorkflow()
    {
        var validation = new RoofDisplayValidationResult(
            RoofDisplayState.Missing,
            RoofDisplayValidationIssue.MissingChild | RoofDisplayValidationIssue.MissingRole);

        Assert.Equal(
            RoofDisplayLifecycleKind.MissingDisplay,
            RoofDisplayLifecycleClassifier.Classify(validation, groupIsCurrent: false));
    }

    [Fact]
    public void StaleOrCorruptDisplay_IsNotGrouped()
    {
        var stale = new RoofDisplayValidationResult(
            RoofDisplayState.Stale,
            RoofDisplayValidationIssue.GeometryMismatch);
        var duplicate = new RoofDisplayValidationResult(
            RoofDisplayState.Stale,
            RoofDisplayValidationIssue.DuplicateRole | RoofDisplayValidationIssue.ExtraChild);

        Assert.Equal(
            RoofDisplayLifecycleKind.StaleDisplay,
            RoofDisplayLifecycleClassifier.Classify(stale, groupIsCurrent: false));
        Assert.Equal(
            RoofDisplayLifecycleKind.StaleDisplay,
            RoofDisplayLifecycleClassifier.Classify(duplicate, groupIsCurrent: false));
    }

    [Fact]
    public void FutureDisplaySchema_WinsOverMissingGroup()
    {
        var validation = new RoofDisplayValidationResult(
            RoofDisplayState.Stale,
            RoofDisplayValidationIssue.UnsupportedFutureSchema);

        Assert.Equal(
            RoofDisplayLifecycleKind.UnsupportedFutureSchema,
            RoofDisplayLifecycleClassifier.Classify(validation, groupIsCurrent: false));
    }
}
