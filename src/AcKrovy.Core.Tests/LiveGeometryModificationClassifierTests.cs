using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class LiveGeometryModificationClassifierTests
{
    [Fact]
    public void AnnotationOnlyRotate_IsPresentationChange_NotSourceRebuild()
    {
        var kind = LiveGeometryModificationClassifier.Classify(
            modifiedTimberSourceCount: 0,
            modifiedAnnotationPresentationCount: 1,
            appendedTimberCount: 0,
            erasedSourceHandleCount: 0,
            requiresFullTimberAnnotationRefresh: true);

        Assert.Equal(LiveGeometryModificationKind.AnnotationPresentationChanged, kind);
        Assert.True(LiveGeometryModificationClassifier.ShouldPreserveAnnotationPresentationOnly(kind));
        Assert.False(LiveGeometryModificationClassifier.ShouldRunSourceCanonicalRefresh(kind));
    }

    [Fact]
    public void AnnotationOnlyMove_IsPresentationChange()
    {
        var kind = LiveGeometryModificationClassifier.Classify(
            modifiedTimberSourceCount: 0,
            modifiedAnnotationPresentationCount: 2,
            appendedTimberCount: 0,
            erasedSourceHandleCount: 0,
            requiresFullTimberAnnotationRefresh: false);

        Assert.Equal(LiveGeometryModificationKind.AnnotationPresentationChanged, kind);
        Assert.False(LiveGeometryModificationClassifier.ShouldRunSourceCanonicalRefresh(kind));
    }

    [Fact]
    public void SourceRotate_StillQueuesSourceCanonicalRefresh()
    {
        var kind = LiveGeometryModificationClassifier.Classify(
            modifiedTimberSourceCount: 1,
            modifiedAnnotationPresentationCount: 0,
            appendedTimberCount: 0,
            erasedSourceHandleCount: 0,
            requiresFullTimberAnnotationRefresh: true);

        Assert.Equal(LiveGeometryModificationKind.SourceGeometryChanged, kind);
        Assert.True(LiveGeometryModificationClassifier.ShouldRunSourceCanonicalRefresh(kind));
        Assert.False(LiveGeometryModificationClassifier.ShouldPreserveAnnotationPresentationOnly(kind));
    }

    [Fact]
    public void SourceRotateWithCoModifiedAnnotation_StillSourceRefresh()
    {
        var kind = LiveGeometryModificationClassifier.Classify(
            modifiedTimberSourceCount: 1,
            modifiedAnnotationPresentationCount: 1,
            appendedTimberCount: 0,
            erasedSourceHandleCount: 0,
            requiresFullTimberAnnotationRefresh: true);

        Assert.Equal(LiveGeometryModificationKind.SourceGeometryChanged, kind);
        Assert.True(LiveGeometryModificationClassifier.ShouldRunSourceCanonicalRefresh(kind));
    }

    [Fact]
    public void SourceMove_StillQueuesIncrementalSourceRefresh()
    {
        var kind = LiveGeometryModificationClassifier.Classify(
            modifiedTimberSourceCount: 1,
            modifiedAnnotationPresentationCount: 0,
            appendedTimberCount: 0,
            erasedSourceHandleCount: 0,
            requiresFullTimberAnnotationRefresh: false);

        Assert.Equal(LiveGeometryModificationKind.SourceGeometryChanged, kind);
    }

    [Fact]
    public void ErasedSource_IsSourceGeometryChanged()
    {
        var kind = LiveGeometryModificationClassifier.Classify(
            modifiedTimberSourceCount: 0,
            modifiedAnnotationPresentationCount: 0,
            appendedTimberCount: 0,
            erasedSourceHandleCount: 1,
            requiresFullTimberAnnotationRefresh: false);

        Assert.Equal(LiveGeometryModificationKind.SourceGeometryChanged, kind);
    }

    [Fact]
    public void AppendedTimber_IsSourceGeometryChanged()
    {
        var kind = LiveGeometryModificationClassifier.Classify(
            modifiedTimberSourceCount: 0,
            modifiedAnnotationPresentationCount: 0,
            appendedTimberCount: 1,
            erasedSourceHandleCount: 0,
            requiresFullTimberAnnotationRefresh: false);

        Assert.Equal(LiveGeometryModificationKind.SourceGeometryChanged, kind);
    }

    [Fact]
    public void RotateWithoutTimberOrAnnotation_KeepsFullRefreshFallbackSignal()
    {
        // Legacy ROTATE safety net when ObjectModified captured no timber.
        var kind = LiveGeometryModificationClassifier.Classify(
            modifiedTimberSourceCount: 0,
            modifiedAnnotationPresentationCount: 0,
            appendedTimberCount: 0,
            erasedSourceHandleCount: 0,
            requiresFullTimberAnnotationRefresh: true);

        Assert.Equal(LiveGeometryModificationKind.SourceGeometryChanged, kind);
    }

    [Fact]
    public void IdleCommand_IsNone()
    {
        var kind = LiveGeometryModificationClassifier.Classify(
            modifiedTimberSourceCount: 0,
            modifiedAnnotationPresentationCount: 0,
            appendedTimberCount: 0,
            erasedSourceHandleCount: 0,
            requiresFullTimberAnnotationRefresh: false);

        Assert.Equal(LiveGeometryModificationKind.None, kind);
        Assert.False(LiveGeometryModificationClassifier.ShouldRunSourceCanonicalRefresh(kind));
        Assert.False(LiveGeometryModificationClassifier.ShouldPreserveAnnotationPresentationOnly(kind));
    }

    [Theory]
    [InlineData("ROTATE", true)]
    [InlineData("_ROTATE", true)]
    [InlineData("MOVE", false)]
    public void RotateCommand_StillRequiresFullTimberAnnotationRefreshFlag(
        string commandName,
        bool expected)
    {
        Assert.Equal(
            expected,
            LiveGeometryCommandRules.RequiresFullTimberAnnotationRefresh(commandName));
    }
}
