using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source-contract guards for classic annotation-only ROTATE/MOVE:
/// presentation edit must not enter the timber source canonical refresh path.
/// </summary>
public sealed class LiveGeometryAnnotationPresentationSourceContractTests
{
    [Fact]
    public void LiveGeometry_ClassifiesAnnotationPresentationSeparatelyFromSourceRefresh()
    {
        var liveGeometry = Normalize(LiveGeometrySource());
        var refresh = Member(liveGeometry, "private static void RefreshTimberElements(");
        var classifier = Normalize(Read(
            "src/AcKrovy.Core/Services/LiveGeometryModificationClassifier.cs"));
        var kind = Normalize(Read(
            "src/AcKrovy.Core/Services/LiveGeometryModificationKind.cs"));

        Assert.Contains("LiveGeometryModificationKind", kind);
        Assert.Contains("AnnotationPresentationChanged", kind);
        Assert.Contains("SourceGeometryChanged", kind);

        Assert.Contains("Classify(", classifier);
        Assert.Contains("ShouldPreserveAnnotationPresentationOnly(", classifier);
        Assert.Contains("ShouldRunSourceCanonicalRefresh(", classifier);

        Assert.Contains("LiveGeometryModificationClassifier.Classify(", refresh);
        Assert.Contains(
            "ShouldPreserveAnnotationPresentationOnly(",
            refresh);
        Assert.Contains("PersistFramedManualOffsets(", refresh);
        Assert.Contains("annotation_presentation_only", refresh);
        Assert.Contains("skipped EnsureForElement/", refresh);
        Assert.Contains("FindAllTimberElements/SynchronizeElementIds/", refresh);
    }

    [Fact]
    public void AnnotationPresentationOnlyPath_DoesNotInvokeWholeDrawingRefreshApis()
    {
        var refresh = Member(
            Normalize(LiveGeometrySource()),
            "private static void RefreshTimberElements(");

        var presentationGate = refresh.IndexOf(
            "ShouldPreserveAnnotationPresentationOnly(",
            StringComparison.Ordinal);
        Assert.True(presentationGate >= 0);

        var elseGate = refresh.IndexOf(
            "else\n                    {",
            presentationGate,
            StringComparison.Ordinal);
        Assert.True(elseGate > presentationGate);

        var presentationBranch = refresh.Substring(
            presentationGate,
            elseGate - presentationGate);

        Assert.Contains("PersistFramedManualOffsets(", presentationBranch);
        Assert.DoesNotContain("DrawingScanner.FindAllTimberElements(", presentationBranch);
        Assert.DoesNotContain("TimberAnnotationService.EnsureForElement(", presentationBranch);
        Assert.DoesNotContain("SynchronizeElementIds(", presentationBranch);
        Assert.DoesNotContain(
            "DeleteDuplicatesForExistingSourceHandles(",
            presentationBranch);
        Assert.DoesNotContain(
            "DeleteInsertedWithoutCurrentSourceHandles(",
            presentationBranch);
        Assert.DoesNotContain(
            "AutoCadAnnotationPresentationBatchContext.Create(",
            presentationBranch);
    }

    [Fact]
    public void SourceRefreshPath_PrefersModifiedTimberOverFindAll()
    {
        var refresh = Member(
            Normalize(LiveGeometrySource()),
            "private static void RefreshTimberElements(");
        var rules = Normalize(Read(
            "src/AcKrovy.Core/Services/LiveGeometryCommandRules.cs"));

        Assert.Contains("SelectSourceRefreshCandidates<", rules);
        Assert.Contains("modifiedTimberIds.Count > 0", rules);
        Assert.Contains("SelectSourceRefreshCandidates(", refresh);
        Assert.Contains("modifiedTimberIds", refresh);
        Assert.Contains("usedFindAllFallback=", refresh);
        Assert.Contains("ensureTargets=", refresh);
        Assert.Contains("drawingTimberMeasured=", refresh);

        // COPY/PASTE still initializes; pure ROTATE skips handle re-scan.
        Assert.Contains("appendedTimberIds.Count > 0", refresh);
        Assert.Contains("InitializeLocalCopies(", refresh);
    }

    [Fact]
    public void SourceRefreshPath_StillRunsEnsureAndDuplicateCleanup()
    {
        var refresh = Member(
            Normalize(LiveGeometrySource()),
            "private static void RefreshTimberElements(");

        var elseGate = refresh.IndexOf(
            "else\n                    {",
            refresh.IndexOf(
                "ShouldPreserveAnnotationPresentationOnly(",
                StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.True(elseGate >= 0);

        var sourcePath = refresh.Substring(elseGate);
        Assert.Contains("TimberAnnotationService.EnsureForElement(", sourcePath);
        Assert.Contains(
            "TimberAnnotationService.DeleteDuplicatesForExistingSourceHandles(",
            sourcePath);
        Assert.Contains(
            "LiveGeometryCommandRules.RequiresFullTimberAnnotationRefresh(",
            Normalize(LiveGeometrySource()));
        Assert.Contains("SelectSourceRefreshCandidates(", sourcePath);
    }

    [Fact]
    public void ObjectModified_StillQueuesMLeaderAsAnnotationNotSource()
    {
        var objectModified = Member(
            Normalize(LiveGeometrySource()),
            "private void ObjectModified(");

        var mleaderGate = objectModified.IndexOf(
            "if (entity is MLeader)",
            StringComparison.Ordinal);
        var gripQueue = objectModified.IndexOf(
            "_modifiedFramedLabelIds.TryAdd(entity.ObjectId);",
            mleaderGate,
            StringComparison.Ordinal);
        var returnAfterGripQueue = objectModified.IndexOf(
            "return;",
            gripQueue,
            StringComparison.Ordinal);
        var sourceQueue = objectModified.IndexOf(
            "_modifiedIds.TryAdd(entity.ObjectId);",
            StringComparison.Ordinal);

        Assert.True(
            mleaderGate >= 0 && gripQueue > mleaderGate &&
            returnAfterGripQueue > gripQueue && sourceQueue > returnAfterGripQueue);
    }

    [Fact]
    public void UndoRedoGuards_RemainAroundLiveGeometryRefresh()
    {
        var liveGeometry = Normalize(LiveGeometrySource());
        Assert.Contains("LiveGeometryCommandRules.IsUndoRedoCommand(", liveGeometry);
        Assert.Contains("ClearPendingLiveGeometryState(", liveGeometry);
        Assert.Contains("OnLiveGeometryRefreshSkippedUndoRedo(", liveGeometry);
    }

    [Fact]
    public void DebugTiming_UsesExistingAcKrovyLogSink()
    {
        var liveGeometry = LiveGeometrySource();
        Assert.Contains("LiveGeometryTiming", liveGeometry);
        Assert.Contains("Diagnostics.AcKrovyDiagnostics.Info(", liveGeometry);
        Assert.Contains("System.Diagnostics.Stopwatch.StartNew()", liveGeometry);
    }

    private static string LiveGeometrySource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs");

    private static string Read(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(path);
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Member(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing member signature: {signature}");

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

        throw new InvalidOperationException($"Unbalanced braces for {signature}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AcKrovy.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
