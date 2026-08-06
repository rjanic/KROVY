using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentAutotestRulesTests
{
    [Fact]
    public void BuildCases_CoversRequiredMatrixAndDedupes()
    {
        var cases = TimberFramedBlockContentAutotestRules.BuildCases();
        Assert.True(cases.Count >= 15);
        Assert.True(
            TimberFramedBlockContentAutotestRules.TryValidateCoverage(
                cases,
                out var note),
            note);

        var duplicated = cases.Concat(cases).ToArray();
        var deduped = TimberFramedBlockContentAutotestRules.DedupeByKey(duplicated);
        Assert.Equal(cases.Count, deduped.Count);
        Assert.Equal(
            cases.Select(c => c.Key),
            deduped.Select(c => c.Key));
    }

    [Fact]
    public void Summary_FormatsPassFailAndCapsConsoleFailures()
    {
        var summary = new TimberFramedBlockContentAutotestSummary("run1", caseCount: 3);
        summary.DetailLogPath = @"C:\_scratch\ak_dev_fbc_autotest_run1.txt";
        summary.RecordPass("A", "Create", "ok");
        for (var i = 0; i < 12; i++)
        {
            summary.RecordFailure(
                $"CASE{i}",
                "Phase",
                "expected",
                $"actual{i}",
                TimberFramedBlockContentAutotestCategory.CreatePlacement);
        }

        summary.SealCoverageCategories(TimberFramedBlockContentAutotestRules.BuildCases());
        var console = summary.FormatConsoleSummary();
        Assert.Contains("RESULT=FAIL", console, StringComparison.Ordinal);
        Assert.Contains("AssertionsFailed=12", console, StringComparison.Ordinal);
        Assert.Contains("ProductFailures=12", console, StringComparison.Ordinal);
        Assert.Contains("FixtureFailures=0", console, StringComparison.Ordinal);
        Assert.Contains("DetailedLog=", console, StringComparison.Ordinal);
        Assert.Contains("Failures (max 10):", console, StringComparison.Ordinal);
        Assert.Equal(
            10,
            TimberFramedBlockContentAutotestRules.TakeConsoleFailures(summary.Failures)
                .Count);
        Assert.Contains("CASE0", console, StringComparison.Ordinal);
        Assert.DoesNotContain("CASE11", console, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_PassPath_HasRequiredCategoryLines()
    {
        var cases = TimberFramedBlockContentAutotestRules.BuildCases();
        var summary = new TimberFramedBlockContentAutotestSummary("runPass", cases.Count);
        summary.DetailLogPath = TimberFramedBlockContentAutotestRules.BuildDetailLogFileName(
            "runPass");
        summary.RecordPass("X", "Create", "ok");
        summary.MarkCategory(TimberFramedBlockContentAutotestCategory.CreatePlacement, true);
        summary.MarkCategory(TimberFramedBlockContentAutotestCategory.ContentSideService, true);
        summary.MarkCategory(TimberFramedBlockContentAutotestCategory.RightToLeft, true);
        summary.MarkCategory(TimberFramedBlockContentAutotestCategory.LeftToRight, true);
        summary.MarkCategory(TimberFramedBlockContentAutotestCategory.SyntheticCrossingSetup, true);
        summary.MarkCategory(TimberFramedBlockContentAutotestCategory.ItemOnly, true);
        summary.MarkCategory(TimberFramedBlockContentAutotestCategory.Persistence, true);
        summary.MarkCategory(TimberFramedBlockContentAutotestCategory.LifecycleProcessor, true);
        summary.MarkCategory(TimberFramedBlockContentAutotestCategory.SameHandle, true);
        summary.MarkCategory(TimberFramedBlockContentAutotestCategory.ForbiddenDrift, true);
        summary.MarkCategory(TimberFramedBlockContentAutotestCategory.ExternalEntities, true);
        summary.MarkCategory(TimberFramedBlockContentAutotestCategory.RunnerIsolation, true);
        summary.MarkCategory(TimberFramedBlockContentAutotestCategory.DoglegGeometry, true);
        summary.MarkCategory(
            TimberFramedBlockContentAutotestCategory.ContentSideForbiddenDrift,
            true);
        summary.ExternalLifecycleMutations = 0;
        summary.SealCoverageCategories(cases);

        var text = summary.FormatConsoleSummary();
        Assert.Contains("RESULT=PASS", text, StringComparison.Ordinal);
        Assert.Contains("ScenarioCases=", text, StringComparison.Ordinal);
        Assert.Contains("AssertionsPassed=", text, StringComparison.Ordinal);
        Assert.Contains("FixtureFailures=0", text, StringComparison.Ordinal);
        Assert.Contains("ProductFailures=0", text, StringComparison.Ordinal);
        Assert.Contains("CreatePlacement=PASS", text, StringComparison.Ordinal);
        Assert.Contains("ContentSideService=PASS", text, StringComparison.Ordinal);
        Assert.Contains("RightToLeft=PASS", text, StringComparison.Ordinal);
        Assert.Contains("LeftToRight=PASS", text, StringComparison.Ordinal);
        Assert.Contains("SyntheticCrossingSetup=PASS", text, StringComparison.Ordinal);
        Assert.Contains("CardinalAngles=PASS", text, StringComparison.Ordinal);
        Assert.Contains("NearCardinalAngles=PASS", text, StringComparison.Ordinal);
        Assert.Contains("Scales=PASS", text, StringComparison.Ordinal);
        Assert.Contains("ItemOnly=PASS", text, StringComparison.Ordinal);
        Assert.Contains("Persistence=PASS", text, StringComparison.Ordinal);
        Assert.Contains("LifecycleProcessor=PASS", text, StringComparison.Ordinal);
        Assert.Contains("SameHandle=PASS", text, StringComparison.Ordinal);
        Assert.Contains("ForbiddenDrift=PASS", text, StringComparison.Ordinal);
        Assert.Contains("ExternalEntities=PASS", text, StringComparison.Ordinal);
        Assert.Contains("RunnerIsolation=PASS", text, StringComparison.Ordinal);
        Assert.Contains("DoglegGeometry=PASS", text, StringComparison.Ordinal);
        Assert.Contains("ContentSideForbiddenDrift=PASS", text, StringComparison.Ordinal);
        Assert.Contains("ExternalLifecycleMutations=0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Failures (max 10):", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_FixtureFailure_DoesNotCountAsProductFailure()
    {
        var summary = new TimberFramedBlockContentAutotestSummary("fix", 1);
        summary.RecordFixtureFailure(
            "CASE",
            "SyntheticCrossingSetup",
            "currentPlacementCorrect=False",
            "True",
            TimberFramedBlockContentAutotestCategory.SyntheticCrossingSetup);
        summary.RecordFailure(
            "CASE",
            "Create",
            "ok",
            "bad",
            TimberFramedBlockContentAutotestCategory.CreatePlacement);

        Assert.Equal(1, summary.FixtureFailureCount);
        Assert.Equal(1, summary.ProductFailureCount);
        Assert.False(summary.OverallPass);
        var text = summary.FormatConsoleSummary();
        Assert.Contains("[Fixture]", text, StringComparison.Ordinal);
        Assert.Contains("[Product]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Marker_IsRunScopedAndRejectsForeignPayloads()
    {
        var payload = TimberFramedBlockContentAutotestRules.BuildMarkerPayload(
            "runA",
            "CIRCLE-COMB-R-90-D50");
        Assert.True(
            TimberFramedBlockContentAutotestRules.IsOwnAutotestMarkerForRun(
                payload,
                "runA"));
        Assert.False(
            TimberFramedBlockContentAutotestRules.IsOwnAutotestMarkerForRun(
                payload,
                "runB"));
        Assert.False(
            TimberFramedBlockContentAutotestRules.IsOwnAutotestMarker(
                "FBC_CREATE_VERIFY|token"));
        Assert.True(
            TimberFramedBlockContentAutotestMarker.TryParse(
                payload,
                TimberFramedBlockContentAutotestRules.DebugMarkerToken,
                out var marker));
        Assert.Equal("runA", marker.RunId);
        Assert.Equal("CIRCLE-COMB-R-90-D50", marker.CaseKey);
    }

    [Fact]
    public void SyntheticKneeOnlyCrossing_ReflectsKneeThroughBlockPosition()
    {
        var knee = new TimberPlanarPoint(100d, 50d);
        var blockPosition = new TimberPlanarPoint(300d, 50d);

        Assert.True(
            TimberFramedBlockContentAutotestRules.TryComputeSyntheticKneeOnlyCrossing(
                knee,
                blockPosition,
                out var newKnee,
                out var dogleg));

        // newK = I + (I - K) = (500, 50)
        Assert.Equal(500d, newKnee.X, 6);
        Assert.Equal(50d, newKnee.Y, 6);
        // BP unchanged by helper — dogleg from newK → BP points toward −X.
        Assert.True(dogleg.X < 0d);

        // Second crossing restores original knee.
        Assert.True(
            TimberFramedBlockContentAutotestRules.TryComputeSyntheticKneeOnlyCrossing(
                newKnee,
                blockPosition,
                out var restoredKnee,
                out _));
        Assert.Equal(knee.X, restoredKnee.X, 6);
        Assert.Equal(knee.Y, restoredKnee.Y, 6);
    }

    [Fact]
    public void SyntheticKneeOnlyCrossing_DoesNotMoveBlockPosition()
    {
        var knee = new TimberPlanarPoint(10d, 20d);
        var blockPosition = new TimberPlanarPoint(40d, 80d);
        Assert.True(
            TimberFramedBlockContentAutotestRules.TryComputeSyntheticKneeOnlyCrossing(
                knee,
                blockPosition,
                out var newKnee,
                out _));

        // Relative order K→I flips: old K is on one side of I, new K on the other.
        var oldDx = knee.X - blockPosition.X;
        var newDx = newKnee.X - blockPosition.X;
        Assert.True(oldDx * newDx < 0d || Math.Abs(oldDx) < 1e-9d);
        Assert.Equal(
            blockPosition.X + (blockPosition.X - knee.X),
            newKnee.X,
            9);
        Assert.Equal(
            blockPosition.Y + (blockPosition.Y - knee.Y),
            newKnee.Y,
            9);
    }

    [Fact]
    public void ItemOnly_CasesHaveNoCombinedPresentation()
    {
        var itemOnly = TimberFramedBlockContentAutotestRules.BuildCases()
            .Where(c =>
                c.Presentation == TimberFramedBlockContentPresentation.ItemOnly)
            .ToArray();
        Assert.Equal(3, itemOnly.Length);
        Assert.All(
            itemOnly,
            c => Assert.NotEqual(
                TimberFramedBlockContentKind.Plain,
                c.Kind));
        Assert.DoesNotContain(
            itemOnly,
            c => c.RequirePersistence || c.PreferLifecycleProcessor);
    }

    [Fact]
    public void LifecycleSession_ArmDisarmAndReentrancyResetOnException()
    {
        var session = new TimberFramedBlockContentStretchNormalizeSession();
        session.ArmLifecycleTest(
            TimberFramedBlockContentAutotestRules.GripStretchCommandName);

        Assert.True(session.TraceEnabled);
        Assert.True(session.ProofEnabled);
        Assert.Contains(
            TimberFramedBlockContentAutotestRules.GripStretchCommandName,
            session.ConfirmedCommandNames);
        Assert.Equal(0, session.QueuedCount);

        session.BeginCommand(
            TimberFramedBlockContentAutotestRules.GripStretchCommandName);
        Assert.True(session.TryQueueObjectKey("H1"));
        Assert.True(
            session.ShouldProcessEndedCommand(
                TimberFramedBlockContentAutotestRules.GripStretchCommandName));

        try
        {
            using (session.BeginProcessing())
            {
                Assert.True(session.IsProcessing);
                throw new InvalidOperationException("simulated");
            }
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        Assert.False(session.IsProcessing);

        session.DisarmLifecycleTest();
        Assert.False(session.TraceEnabled);
        Assert.False(session.ProofEnabled);
        Assert.Empty(session.ConfirmedCommandNames);
        Assert.Empty(session.ObservedCommandNames);
        Assert.Equal(0, session.QueuedCount);
        Assert.False(
            session.ShouldProcessEndedCommand(
                TimberFramedBlockContentAutotestRules.GripStretchCommandName));
    }

    [Fact]
    public void LifecycleSession_CaptureRestoreAndForceAutotestIsolation()
    {
        var session = new TimberFramedBlockContentStretchNormalizeSession();
        session.TraceEnabled = true;
        session.ProofEnabled = true;
        session.ConfirmCommand("GRIP_STRETCH");
        session.BeginCommand("GRIP_STRETCH");
        session.TryQueueObjectKey("H1");
        session.RememberObservedCommandIfQueued();

        var snapshot = session.CaptureExternalState();
        Assert.True(snapshot.TraceEnabled);
        Assert.True(snapshot.ProofEnabled);
        Assert.Equal(1, snapshot.QueuedCount);
        Assert.Contains("GRIP_STRETCH", snapshot.ConfirmedCommandNames);
        Assert.Contains("GRIP_STRETCH", snapshot.ObservedCommandNames);

        session.ForceAutotestIsolation();
        session.ClearConfirmedCommands();
        session.ClearObservedCommands();
        Assert.False(session.TraceEnabled);
        Assert.False(session.ProofEnabled);
        Assert.Equal(0, session.QueuedCount);
        Assert.Empty(session.ActiveCommandName);
        Assert.False(session.ShouldProcessEndedCommand("GRIP_STRETCH"));

        session.RestoreExternalState(snapshot);
        Assert.True(session.TraceEnabled);
        Assert.True(session.ProofEnabled);
        Assert.Contains("GRIP_STRETCH", session.ConfirmedCommandNames);
        Assert.Contains("GRIP_STRETCH", session.ObservedCommandNames);
        Assert.Equal("GRIP_STRETCH", session.ActiveCommandName);
        // Queue is intentionally cleared on restore; caller re-arms if needed.
        Assert.Equal(0, session.QueuedCount);
    }

    [Fact]
    public void PhaseDrift_AndBlockPositionOnDoglegHelpers()
    {
        var text = TimberFramedBlockContentAutotestRules.FormatPhaseDrift(
            "B→C",
            0d,
            0d,
            12.5d);
        Assert.Contains("B→C", text, StringComparison.Ordinal);
        Assert.Contains("bp=12.5", text, StringComparison.Ordinal);

        Assert.True(
            TimberFramedBlockContentAutotestRules.BlockPositionLiesOnDoglegDirection(
                new TimberPlanarPoint(0d, 0d),
                new TimberPlanarPoint(10d, 0d),
                new TimberPlanarVector(1d, 0d)));
        Assert.False(
            TimberFramedBlockContentAutotestRules.BlockPositionLiesOnDoglegDirection(
                new TimberPlanarPoint(0d, 0d),
                new TimberPlanarPoint(0d, 10d),
                new TimberPlanarVector(1d, 0d)));
    }

    [Fact]
    public void LifecycleQueue_DrainEmptiesAndSecondDrainIsNoOp()
    {
        var session = new TimberFramedBlockContentStretchNormalizeSession();
        session.ArmLifecycleTest("GRIP_STRETCH");
        session.BeginCommand("GRIP_STRETCH");
        Assert.True(session.TryQueueObjectKey("A"));
        Assert.True(session.TryQueueObjectKey("B"));
        Assert.False(session.TryQueueObjectKey("A"));

        var first = session.DrainQueue();
        Assert.Equal(2, first.Count);
        Assert.Equal(0, session.QueuedCount);
        Assert.Empty(session.DrainQueue());
    }

    [Fact]
    public void DetailLogFileName_UsesStablePrefix()
    {
        var name = TimberFramedBlockContentAutotestRules.BuildDetailLogFileName(
            "20260101_120000_001");
        Assert.Equal(
            "ak_dev_fbc_autotest_20260101_120000_001.txt",
            name);
        Assert.StartsWith(
            TimberFramedBlockContentAutotestRules.DetailLogFilePrefix,
            name,
            StringComparison.Ordinal);
    }
}
