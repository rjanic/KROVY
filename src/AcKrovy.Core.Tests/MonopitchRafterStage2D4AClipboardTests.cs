using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class MonopitchRafterStage2D4AClipboardTests
{
    private static readonly string Live = ReadAutoCad("LiveGeometrySynchronizationService.cs");
    private static readonly string Snapshot = ReadAutoCad(
        "RoofGeneratedCopyPreCommandSnapshotService.cs");
    private static readonly string GeneratedCopy = ReadAutoCad(
        "RoofGeneratedRafterCopyOwnershipRehydrationService.cs");
    private static readonly string AttachedCopy = ReadAutoCad(
        "RoofAttachedManualCopyCloneReinitializeService.cs");
    private static readonly string ProvenanceLifecycle = File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "src",
        "AcKrovy.Core",
        "Services",
        "Roofs",
        "RoofClipboardProvenanceLifecycle.cs"));

    [Theory]
    [InlineData("COPYCLIP")]
    [InlineData("_COPYCLIP")]
    [InlineData(".COPYCLIP")]
    [InlineData("_.COPYCLIP")]
    [InlineData("'_.COPYCLIP")]
    [InlineData("COPYBASE")]
    public void ClipboardSourceCommands_AreNormalized(string command)
    {
        Assert.True(LiveGeometryCommandRules.IsClipboardCopySourceCommand(command));
    }

    [Theory]
    [InlineData("PASTECLIP")]
    [InlineData("_PASTECLIP")]
    [InlineData(".PASTECLIP")]
    [InlineData("_.PASTECLIP")]
    [InlineData("'_.PASTECLIP")]
    [InlineData("PASTEORIG")]
    public void ClipboardPasteCommands_AreNormalized(string command)
    {
        Assert.True(LiveGeometryCommandRules.IsClipboardPasteCommand(command));
        Assert.True(LiveGeometryCommandRules.IsCopySourcePreservingCommand(command));
    }

    [Fact]
    public void NativeCopy_RemainsTheEstablishedOwnershipCommand()
    {
        Assert.True(LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand("COPY"));
        Assert.False(LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand("PASTECLIP"));
        Assert.False(LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand("COPYCLIP"));
    }

    [Theory]
    [InlineData("COPYCLIP", "PASTECLIP")]
    [InlineData("COPYBASE", "PASTECLIP")]
    [InlineData("COPYBASE", "PASTEORIG")]
    public void SupportedClipboardSourcePastePairs_ReachTheSameIndividualPolicy(
        string sourceCommand,
        string pasteCommand)
    {
        Assert.True(LiveGeometryCommandRules.IsClipboardCopySourceCommand(sourceCommand));
        var decision = RoofClipboardPasteOwnershipRules.Classify(
            pasteCommand,
            sameDrawingProvenanceProven: true,
            intelligentTimberCount: 1,
            appendedRoofOwnerDetected: false);
        Assert.True(decision.ShouldProcessIndividualTimber);
    }

    [Fact]
    public void SameDwgSingleTimber_IsTheOnlyClipboardPayloadAdopted()
    {
        var accepted = RoofClipboardPasteOwnershipRules.Classify(
            "_.PASTECLIP",
            sameDrawingProvenanceProven: true,
            intelligentTimberCount: 1,
            appendedRoofOwnerDetected: false);
        var foreign = RoofClipboardPasteOwnershipRules.Classify(
            "PASTECLIP",
            sameDrawingProvenanceProven: false,
            intelligentTimberCount: 1,
            appendedRoofOwnerDetected: false);
        var wholeRoof = RoofClipboardPasteOwnershipRules.Classify(
            "PASTECLIP",
            sameDrawingProvenanceProven: true,
            intelligentTimberCount: 13,
            appendedRoofOwnerDetected: true);
        var multipleTimber = RoofClipboardPasteOwnershipRules.Classify(
            "PASTEORIG",
            sameDrawingProvenanceProven: true,
            intelligentTimberCount: 2,
            appendedRoofOwnerDetected: false);

        Assert.Equal(
            RoofClipboardPasteOwnershipClassification.SameDwgIndividualTimber,
            accepted.Classification);
        Assert.True(accepted.ShouldProcessIndividualTimber);
        Assert.Equal(
            RoofClipboardPasteOwnershipClassification.ForeignOrUnknownProvenance,
            foreign.Classification);
        Assert.False(foreign.ShouldProcessIndividualTimber);
        Assert.Equal(
            RoofClipboardPasteOwnershipClassification.WholeRoofOutOfScope,
            wholeRoof.Classification);
        Assert.False(wholeRoof.ShouldProcessIndividualTimber);
        Assert.Equal(
            RoofClipboardPasteOwnershipClassification.NonIndividualPayloadOutOfScope,
            multipleTimber.Classification);
        Assert.False(multipleTimber.ShouldProcessIndividualTimber);
    }

    [Fact]
    public void ThirteenGenerated_PasteOneGenerated_DetachesOnlyCloneAndKeepsUniqueRecipe()
    {
        const string owner = "2912";
        var sourceHandles = Enumerable.Range(0, 13)
            .Select(index => $"G{index:X2}")
            .ToArray();
        var logicalKeys = Enumerable.Range(0, 13)
            .Select(index => RoofGeneratedRafterCopyDetachRules.FormatLogicalKey(
                RafterRoofFace.Face0,
                index))
            .ToArray();
        var appended = new RoofGeneratedRafterCopyDetachRules.AppendedGeneratedLine(
            "CLONE",
            owner,
            RafterRoofFace.Face0,
            12);
        var decision = RoofClipboardPasteOwnershipRules.Classify(
            "PASTECLIP",
            sameDrawingProvenanceProven: true,
            intelligentTimberCount: 1,
            appendedRoofOwnerDetected: false);

        var detached = RoofGeneratedRafterCopyDetachRules.FindAppendedCloneDetachHandles(
            new Dictionary<string, IReadOnlyCollection<string>>
            {
                [owner] = logicalKeys,
            },
            sourceHandles,
            [appended],
            Array.Empty<string>());
        var generatedBeforePaste = Enumerable.Range(0, 13)
            .Select(index => Generated(owner, index))
            .ToArray();
        var incorrectOldResult = generatedBeforePaste
            .Append(Generated(owner, 12))
            .ToArray();
        var generatedAfter = generatedBeforePaste.ToArray();

        Assert.True(decision.ShouldProcessIndividualTimber);
        Assert.Equal(["CLONE"], detached);
        Assert.False(RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(
            incorrectOldResult));
        Assert.True(RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(
            generatedAfter));
        Assert.Equal(13, generatedAfter.Length);
        Assert.Single(detached); // one new AttachedManual Origin.Copy
        Assert.DoesNotContain("CLONE", sourceHandles);
    }

    [Fact]
    public void GeneratedClipboardPayload_ThreePastesKeepThirteenGeneratedAndCreateThreeUniqueChildren()
    {
        const string owner = "2912";
        const uint clipboardRevision = 101;
        var document = new object();
        var database = new object();
        var lifecycle = new RoofClipboardProvenanceLifecycle<object, object, object>();
        lifecycle.BeginCopy(document, database, "COPYCLIP", new object());
        Assert.True(lifecycle.CompleteCopy(clipboardRevision));
        var before = Layout(Geometry(6000d, 10800d, 0d, 35d));
        var after = Layout(Geometry(7600d, 10800d, 0d, 35d));
        var sourceHandles = Enumerable.Range(0, 13)
            .Select(index => $"G{index:X2}")
            .ToArray();
        var logicalKeys = before.Rafters
            .Select(item => RoofGeneratedRafterCopyDetachRules.FormatLogicalKey(
                item.Face,
                item.StationIndex))
            .ToArray();
        var generated = Enumerable.Range(0, 13)
            .Select(index => Generated(owner, index))
            .ToArray();
        var sourceAnchor = before.Rafters[12];
        var sourceStart = new RoofPoint3D(
            sourceAnchor.PlanStart.X,
            sourceAnchor.PlanStart.Y,
            0d);
        var sourceEnd = new RoofPoint3D(
            sourceAnchor.PlanEnd.X,
            sourceAnchor.PlanEnd.Y,
            0d);
        var children = new List<RoofAttachedManualTimberData>();

        for (var pasteNumber = 1; pasteNumber <= 3; pasteNumber++)
        {
            var provenance = lifecycle.BeginPaste(document, database, clipboardRevision);
            var classification = RoofClipboardPasteOwnershipRules.Classify(
                "PASTECLIP",
                provenance.IsValid,
                intelligentTimberCount: 1,
                appendedRoofOwnerDetected: false);
            var cloneHandle = $"CLONE-{pasteNumber}";
            var detached = RoofGeneratedRafterCopyDetachRules.FindAppendedCloneDetachHandles(
                new Dictionary<string, IReadOnlyCollection<string>>
                {
                    [owner] = logicalKeys,
                },
                sourceHandles,
                [new RoofGeneratedRafterCopyDetachRules.AppendedGeneratedLine(
                    cloneHandle,
                    owner,
                    RafterRoofFace.Face0,
                    12)],
                Array.Empty<string>());
            var pastedStart = OffsetFromAnchor(
                sourceStart,
                sourceEnd,
                100d * pasteNumber,
                125d * pasteNumber);
            var pastedEnd = OffsetFromAnchor(
                sourceStart,
                sourceEnd,
                -80d * pasteNumber,
                125d * pasteNumber,
                fromEnd: true);
            Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(
                sourceStart,
                sourceEnd,
                pastedStart,
                pastedEnd,
                out var relative));

            Assert.True(classification.ShouldProcessIndividualTimber);
            Assert.Equal([cloneHandle], detached);
            children.Add(new RoofAttachedManualTimberData(
                RoofAttachedManualTimberDataSchema.CurrentVersion,
                owner,
                cloneHandle,
                RoofTimberChildRole.AttachedManual,
                sourceAnchor.LogicalKey,
                relative,
                RoofAttachedManualOrigin.Copy));
            lifecycle.CompletePaste();

            Assert.True(lifecycle.HasDurableProvenance);
            Assert.Equal(13, generated.Length);
            Assert.True(RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(generated));
            Assert.Equal(pasteNumber, children.Count);
        }

        Assert.Equal(3, children.Select(child => child.ChildIdentity).Distinct().Count());
        Assert.Equal(3, children.Select(child => child.RelativeSegment).Distinct().Count());
        var rebuiltAnchor = Assert.Single(
            after.Rafters,
            item => item.LogicalKey == sourceAnchor.LogicalKey);
        var rebuiltStart = new RoofPoint3D(
            rebuiltAnchor.PlanStart.X,
            rebuiltAnchor.PlanStart.Y,
            0d);
        var rebuiltEnd = new RoofPoint3D(
            rebuiltAnchor.PlanEnd.X,
            rebuiltAnchor.PlanEnd.Y,
            0d);
        foreach (var child in children)
        {
            Assert.Equal(RoofAttachedManualOrigin.Copy, child.Origin);
            Assert.NotNull(child.RelativeSegment);
            Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
                rebuiltStart,
                rebuiltEnd,
                child.RelativeSegment!,
                out _,
                out _));
        }
    }

    [Fact]
    public void ClipboardDetachThenSourceResize_KeepsThirteenUniqueGeneratedAndReplaysChild()
    {
        const string owner = "2912";
        var before = Layout(Geometry(6000d, 10800d, 0d, 35d));
        var after = Layout(Geometry(7600d, 10800d, 0d, 35d));
        Assert.Equal(13, before.Rafters.Count);
        Assert.Equal(13, after.Rafters.Count);
        var source = before.Rafters[6];
        var rebuiltAnchor = Assert.Single(
            after.Rafters,
            item => item.LogicalKey == source.LogicalKey);
        var sourceStart = new RoofPoint3D(source.PlanStart.X, source.PlanStart.Y, 0d);
        var sourceEnd = new RoofPoint3D(source.PlanEnd.X, source.PlanEnd.Y, 0d);
        var rebuiltStart = new RoofPoint3D(
            rebuiltAnchor.PlanStart.X,
            rebuiltAnchor.PlanStart.Y,
            0d);
        var rebuiltEnd = new RoofPoint3D(
            rebuiltAnchor.PlanEnd.X,
            rebuiltAnchor.PlanEnd.Y,
            0d);
        var pastedStart = OffsetFromAnchor(sourceStart, sourceEnd, 180d, 125d);
        var pastedEnd = OffsetFromAnchor(
            sourceStart,
            sourceEnd,
            -120d,
            125d,
            fromEnd: true);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(
            sourceStart,
            sourceEnd,
            pastedStart,
            pastedEnd,
            out var relative));

        var generatedAfterResize = after.Rafters
            .Select(item => Generated(owner, item.StationIndex))
            .ToArray();
        Assert.Equal(13, generatedAfterResize.Length);
        Assert.True(RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(
            generatedAfterResize));
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
            rebuiltStart,
            rebuiltEnd,
            relative,
            out var replayedStart,
            out var replayedEnd));
        Assert.NotEqual(sourceEnd, rebuiltEnd);
        Assert.Equal(pastedStart, replayedStart);
        Assert.Equal(pastedEnd, replayedEnd);
    }

    [Fact]
    public void AttachedManualClipboardClone_GetsFreshIdentityAndCopyOrigin()
    {
        var anchor = new RoofGeneratedMemberKey(
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            4);
        var original = new RoofAttachedManualTimberData(
            RoofAttachedManualTimberDataSchema.CurrentVersion,
            "2912",
            "CHILD-A",
            RoofTimberChildRole.AttachedManual,
            anchor,
            new RoofAttachedManualRelativeSegment(10d, 20d, 0d, 1000d, 20d, 0d),
            RoofAttachedManualOrigin.Split);
        var pasted = original with
        {
            ChildIdentity = "CHILD-B",
            Origin = RoofAttachedManualOrigin.Copy,
            RelativeSegment = new RoofAttachedManualRelativeSegment(
                110d,
                220d,
                0d,
                1110d,
                220d,
                0d),
        };

        Assert.Equal("CHILD-A", original.ChildIdentity);
        Assert.Equal("CHILD-B", pasted.ChildIdentity);
        Assert.NotEqual(original.ChildIdentity, pasted.ChildIdentity);
        Assert.Equal(RoofAttachedManualOrigin.Copy, pasted.Origin);
        Assert.NotEqual(original.RelativeSegment, pasted.RelativeSegment);
        Assert.Equal(original.RoofOwnerReference, pasted.RoofOwnerReference);
        Assert.Equal(original.AnchorGeneratedMemberKey, pasted.AnchorGeneratedMemberKey);
    }

    [Fact]
    public void AttachedManualClipboardPayload_TwoPastesGetIndependentCopyIdentityAnchorAndRelativeGeometry()
    {
        const uint clipboardRevision = 101;
        var document = new object();
        var database = new object();
        var lifecycle = new RoofClipboardProvenanceLifecycle<object, object, object>();
        lifecycle.BeginCopy(document, database, "COPYCLIP", new object());
        Assert.True(lifecycle.CompleteCopy(clipboardRevision));
        var sourceAnchor = new RoofGeneratedMemberKey(
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            4);
        var source = new RoofAttachedManualTimberData(
            RoofAttachedManualTimberDataSchema.CurrentVersion,
            "2912",
            "SOURCE-CHILD",
            RoofTimberChildRole.AttachedManual,
            sourceAnchor,
            new RoofAttachedManualRelativeSegment(10d, 20d, 0d, 1000d, 20d, 0d),
            RoofAttachedManualOrigin.Copy);
        var pasted = new List<RoofAttachedManualTimberData>();

        for (var pasteNumber = 1; pasteNumber <= 2; pasteNumber++)
        {
            var provenance = lifecycle.BeginPaste(document, database, clipboardRevision);
            var classification = RoofClipboardPasteOwnershipRules.Classify(
                "PASTECLIP",
                provenance.IsValid,
                intelligentTimberCount: 1,
                appendedRoofOwnerDetected: false);
            Assert.True(classification.ShouldProcessIndividualTimber);
            pasted.Add(source with
            {
                ChildIdentity = $"PASTED-CHILD-{pasteNumber}",
                AnchorGeneratedMemberKey = sourceAnchor with
                {
                    StationIndex = sourceAnchor.StationIndex + pasteNumber,
                },
                RelativeSegment = new RoofAttachedManualRelativeSegment(
                    10d + pasteNumber,
                    20d * pasteNumber,
                    0d,
                    1000d + pasteNumber,
                    20d * pasteNumber,
                    0d),
                Origin = RoofAttachedManualOrigin.Copy,
            });
            lifecycle.CompletePaste();
        }

        Assert.Equal(2, pasted.Select(child => child.ChildIdentity).Distinct().Count());
        Assert.Equal(2, pasted.Select(child => child.AnchorGeneratedMemberKey).Distinct().Count());
        Assert.Equal(2, pasted.Select(child => child.RelativeSegment).Distinct().Count());
        Assert.All(pasted, child =>
        {
            Assert.Equal(source.RoofOwnerReference, child.RoofOwnerReference);
            Assert.Equal(RoofAttachedManualOrigin.Copy, child.Origin);
        });
    }

    [Theory]
    [InlineData(1, true, RoofClipboardPasteOwnershipClassification.WholeRoofOutOfScope)]
    [InlineData(2, false, RoofClipboardPasteOwnershipClassification.NonIndividualPayloadOutOfScope)]
    public void RepeatedExcludedPayload_IsClassifiedFreshAndNeverBecomesIndividual(
        int intelligentTimberCount,
        bool appendedRoofOwnerDetected,
        RoofClipboardPasteOwnershipClassification expected)
    {
        const uint clipboardRevision = 101;
        var document = new object();
        var database = new object();
        var lifecycle = new RoofClipboardProvenanceLifecycle<object, object, object>();
        lifecycle.BeginCopy(document, database, "COPYCLIP", new object());
        Assert.True(lifecycle.CompleteCopy(clipboardRevision));

        for (var pasteNumber = 1; pasteNumber <= 2; pasteNumber++)
        {
            var provenance = lifecycle.BeginPaste(document, database, clipboardRevision);
            var classification = RoofClipboardPasteOwnershipRules.Classify(
                "PASTECLIP",
                provenance.IsValid,
                intelligentTimberCount,
                appendedRoofOwnerDetected);

            Assert.Equal(expected, classification.Classification);
            Assert.False(classification.ShouldProcessIndividualTimber);
            lifecycle.CompletePaste();
            Assert.True(lifecycle.HasDurableProvenance);
        }
    }

    [Fact]
    public void RotatedParentOverride_RepeatedClipboardChildrenUseFinalAnchorOnce()
    {
        var before = Layout(Geometry(6000d, 9000d, 30d, 35d));
        var after = Layout(Geometry(7600d, 9400d, 30d, 35d));
        var source = before.Rafters[4];
        var rebuilt = Assert.Single(after.Rafters, item => item.LogicalKey == source.LogicalKey);
        var parentOverride = new RoofGeneratedMemberOverride(
            source.LogicalKey,
            Suppressed: false,
            AlongMm: 125d,
            LateralMm: 210d,
            RotationRadians: 0d,
            StartOffsetMm: 80d,
            EndOffsetMm: -60d);
        Assert.True(RoofGeneratedMemberOverrideRules.TryApplyToLayout(
            source,
            0d,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
            new RoofManualOverrideSet([parentOverride]),
            out var finalBefore,
            out var suppressedBefore));
        Assert.True(RoofGeneratedMemberOverrideRules.TryApplyToLayout(
            rebuilt,
            0d,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
            new RoofManualOverrideSet([parentOverride]),
            out var finalAfter,
            out var suppressedAfter));
        Assert.False(suppressedBefore);
        Assert.False(suppressedAfter);
        var finalBeforeGeometry = Assert.IsType<RoofGeneratedMemberGeometry>(finalBefore);
        var finalAfterGeometry = Assert.IsType<RoofGeneratedMemberGeometry>(finalAfter);

        var replayedChildren = new List<(RoofPoint3D Start, RoofPoint3D End)>();
        for (var pasteNumber = 1; pasteNumber <= 2; pasteNumber++)
        {
            var pastedStart = OffsetFromAnchor(
                finalBeforeGeometry.Start,
                finalBeforeGeometry.End,
                250d * pasteNumber,
                175d * pasteNumber);
            var pastedEnd = OffsetFromAnchor(
                finalBeforeGeometry.Start,
                finalBeforeGeometry.End,
                -175d * pasteNumber,
                175d * pasteNumber,
                fromEnd: true);
            Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(
                finalBeforeGeometry.Start,
                finalBeforeGeometry.End,
                pastedStart,
                pastedEnd,
                out var relative));
            Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
                finalAfterGeometry.Start,
                finalAfterGeometry.End,
                relative,
                out var replayedStart,
                out var replayedEnd));

            Assert.NotEqual(pastedStart, replayedStart);
            Assert.NotEqual(pastedEnd, replayedEnd);
            Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
                finalAfterGeometry.Start,
                finalAfterGeometry.End,
                relative,
                out var replayedAgainStart,
                out var replayedAgainEnd));
            Assert.Equal(replayedStart, replayedAgainStart);
            Assert.Equal(replayedEnd, replayedAgainEnd);
            replayedChildren.Add((replayedStart, replayedEnd));
        }

        Assert.Equal(2, replayedChildren.Distinct().Count());
    }

    [Fact]
    public void HostRouting_UsesExactTrackedDocument_NotDatabaseWrapperOrOwnerHandle()
    {
        var activation = Member(
            Snapshot,
            "public static bool TryActivateForClipboardPaste",
            "public static void CompleteClipboardPaste");
        Assert.Contains("ClipboardLifecycle.BeginPaste(", activation);
        Assert.Contains(
            "ReferenceEquals(provenance.SourceDocument, targetDocument)",
            ProvenanceLifecycle);
        Assert.Contains("var sameDrawing = sameDocument;", ProvenanceLifecycle);
        Assert.Contains("var valid = sameDrawing;", ProvenanceLifecycle);
        Assert.Contains("databaseReferenceEqual", ProvenanceLifecycle);
        Assert.DoesNotContain("var valid = sameDocument &&", ProvenanceLifecycle);
        Assert.DoesNotContain("OwnerReference", activation);
        Assert.DoesNotContain("GetObjectId", activation);
        Assert.Contains("CaptureForClipboardCopy(", Live);
        Assert.Contains("CompleteClipboardCopy(", Live);
        Assert.Contains("TryActivateForClipboardPaste(", Live);
        Assert.Contains("CompleteClipboardPaste()", Live);
        Assert.DoesNotContain("InvalidateClipboardProvenanceForInterveningCommand", Live);
        Assert.Contains("GetClipboardSequenceNumber", Snapshot);
    }

    [Fact]
    public void HostRouting_ClassifiesActualIntelligentTimbers_AndWritesDebugDiagnostics()
    {
        var refresh = Member(
            Live,
            "private void RefreshCandidates(",
            "private static void RefreshTimberElements(");
        Assert.Contains("CountAppendedIntelligentRoofTimbers", refresh);
        Assert.Contains("appendedIntelligentTimberCount", refresh);
        Assert.Contains("RoofGeneratedTimberStore.Read(entity).Data", Live);
        Assert.Contains("RoofAttachedManualTimberStore.Read(entity).Data", Live);
        Assert.Contains("ROOF_CLIPBOARD_PROVENANCE", Snapshot);
        Assert.Contains("phase=capture", Snapshot);
        Assert.Contains("phase=paste-start", Snapshot);
        Assert.Contains("clipboardRevision=", Snapshot);
        Assert.Contains("databaseReferenceEqual=", Snapshot);
        Assert.Contains("sameDrawing=", Snapshot);
        Assert.DoesNotContain("sameDatabase=", Snapshot);
        Assert.Contains("ROOF_CLIPBOARD_CLASSIFY", Live);
        Assert.Contains("intelligentTimberCount=", Live);
        Assert.Contains("roofSourceCount=", Live);
        Assert.Contains("eligibleIndividualTimber=", Live);
        Assert.Contains("document.Editor.WriteMessage", Snapshot);
        Assert.Contains("document.Editor.WriteMessage", Live);
    }

    [Fact]
    public void HostRouting_DrainsAppendedStateAndClassifiesEveryPasteIndependently()
    {
        var refresh = Member(
            Live,
            "private void RefreshCandidates(",
            "private static void RefreshTimberElements(");

        Assert.Contains("var appendedTimberIds = _appendedTimberIds.Drain();", refresh);
        Assert.Contains("var appendedRoofOwnerIds = _appendedRoofOwnerIds.Drain();", refresh);
        Assert.Contains("var appendedLabelIds = _appendedLabelIds.Drain();", refresh);
        Assert.Contains("var appendedSlopeArrowIds = _appendedSlopeArrowIds.Drain();", refresh);
        Assert.Contains("var appendedSlopeAngleTextIds = _appendedSlopeAngleTextIds.Drain();", refresh);
        Assert.Contains("CountAppendedIntelligentRoofTimbers", refresh);
        Assert.Contains("RoofClipboardPasteOwnershipRules.Classify(", refresh);
        Assert.DoesNotContain("DurableClassification", Snapshot + ProvenanceLifecycle);
    }

    [Fact]
    public void DocumentDestruction_ClearsProvenanceByExactTrackedDocument()
    {
        var destruction = Member(
            Live,
            "private static void DocumentToBeDestroyed",
            "private static void Attach");
        var dispose = Member(
            Live,
            "public void Dispose()",
            "private void CommandWillStart");
        var clear = Member(
            Snapshot,
            "public static void ClearForDocument",
            "private static uint ReadClipboardRevision");

        Assert.Contains("tracker.Dispose();", destruction);
        Assert.Contains("Trackers.Remove(e.Document);", destruction);
        Assert.Contains(
            "RoofGeneratedCopyPreCommandSnapshotService.ClearForDocument(_document);",
            dispose);
        Assert.Contains("ReferenceEquals(active.SourceDocument, document)", clear);
        Assert.Contains("ClipboardLifecycle.ClearForDocument(document);", clear);
        Assert.DoesNotContain("document.Database", clear);
    }

    [Fact]
    public void HostRouting_ExcludesWholeRoofAndForeignPayloadBeforeIndividualServices()
    {
        var refresh = Member(
            Live,
            "private void RefreshCandidates(",
            "private static void RefreshTimberElements(");
        var classify = refresh.IndexOf("RoofClipboardPasteOwnershipRules.Classify(", StringComparison.Ordinal);
        var gate = refresh.IndexOf(
            "if (nativeCopy || clipboardDecision.ShouldProcessIndividualTimber)",
            StringComparison.Ordinal);
        var attached = refresh.IndexOf(
            "RoofAttachedManualCopyCloneReinitializeService.Process(",
            StringComparison.Ordinal);
        var generated = refresh.IndexOf(
            "RoofGeneratedRafterCopyOwnershipRehydrationService.Process(",
            StringComparison.Ordinal);
        Assert.True(classify >= 0 && gate > classify && attached > gate && generated > attached);
        Assert.Contains("_appendedRoofOwnerIds", Live);
        Assert.Contains("appendedRoofOwnerIds.Count > 0", refresh);
        Assert.Contains("if (nativeCopy)", refresh);
        Assert.DoesNotContain("RoofWholeRoofCopyRebindService.Process", Member(
            refresh,
            "if (nativeCopy || clipboardDecision.ShouldProcessIndividualTimber)",
            "// MIRROR:"));
    }

    [Fact]
    public void ClipboardServices_UseFreshCloneGeometryIdentityAnnotationsAndCanonicalGroup()
    {
        Assert.Contains("cloneLine.Handle.ToString()", AttachedCopy);
        Assert.Contains("cloneLine.StartPoint", AttachedCopy);
        Assert.Contains("cloneLine.EndPoint", AttachedCopy);
        Assert.Contains("RoofAttachedManualOrigin.Copy", AttachedCopy);
        Assert.Contains("SelectNearestMirrorAnchor", AttachedCopy);
        Assert.Contains("RoofAttachedManualTimberStore.TryClear", GeneratedCopy);
        Assert.Contains("RoofAttachedManualLifecycleService.CreateAnchoredData", GeneratedCopy);
        Assert.Contains("sourceLine.StartPoint", GeneratedCopy);
        Assert.Contains("cloneLine.StartPoint", GeneratedCopy);
        Assert.Contains("EnsureAttachedManualPresentation", GeneratedCopy);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwnerReference", GeneratedCopy);
        Assert.Contains("EraseAppendedAnnotationCopies", Live);
        Assert.DoesNotContain("new Group", GeneratedCopy + AttachedCopy);
        Assert.Contains("IsOwnerUnlocked", GeneratedCopy);
    }

    [Fact]
    public void AnnotationCloneCleanup_PrecedesCanonicalCopyInitialization()
    {
        var refresh = Member(
            Live,
            "private static void RefreshTimberElements(",
            "private static void TraceClipboardClassification");
        var erase = refresh.IndexOf("EraseAppendedAnnotationCopies(", StringComparison.Ordinal);
        var initialize = refresh.IndexOf(
            "TimberElementCopyInitializationService.InitializeLocalCopies(",
            StringComparison.Ordinal);
        var ensure = refresh.IndexOf("TimberAnnotationService.EnsureForElement(", StringComparison.Ordinal);
        Assert.True(erase >= 0 && initialize > erase && ensure > initialize);
    }

    [Fact]
    public void ClipboardWritesSharePasteUndoMark_AndUndoRedoStayZeroDb()
    {
        var willStart = Member(Live, "private void CommandWillStart", "private void CommandEnded");
        var ended = Member(Live, "private void CommandEnded", "private void CommandCancelled");
        Assert.Contains("_sameDwgClipboardPasteForCurrentCommand", willStart);
        Assert.Contains("TryBeginGroupedUndo", willStart);
        Assert.Contains("IsUndoRedoCommand(e.GlobalCommandName)", ended);
        Assert.Contains("ClearPendingLiveGeometryState", ended);
        Assert.Contains("IsClipboardPasteCommand(e.GlobalCommandName)", ended);
        Assert.DoesNotContain("StartTransaction(", ended);
        Assert.Contains("public void CompletePaste() => _activePaste = null;", ProvenanceLifecycle);
        Assert.DoesNotContain("Application.Idle", Live + Snapshot + GeneratedCopy + AttachedCopy);
        Assert.DoesNotContain("new Timer", Live + Snapshot + GeneratedCopy + AttachedCopy);
    }

    [Fact]
    public void ScopeFreeze_ScaleWblockInsertAndSchemasRemainUnchanged()
    {
        var newPolicy = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.Core",
            "Services",
            "Roofs",
            "RoofClipboardPasteOwnershipRules.cs"));
        Assert.False(RoofGeneratedMemberEditCommandRules
            .IsSupportedUnlockedGeneratedTimberCommand("SCALE", RoofKind.Monopitch));
        Assert.DoesNotContain("WBLOCK", newPolicy + Snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT", newPolicy + Snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EXPLODE", newPolicy + Snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Equal(3, RoofAttachedManualTimberDataSchema.CurrentVersion);
        Assert.Equal(3, (int)RoofKind.Monopitch);
    }

    private static RoofPoint3D OffsetFromAnchor(
        RoofPoint3D start,
        RoofPoint3D end,
        double along,
        double lateral,
        bool fromEnd = false)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var ux = dx / length;
        var uy = dy / length;
        var origin = fromEnd ? end : start;
        return new RoofPoint3D(
            origin.X + ux * along - uy * lateral,
            origin.Y + uy * along + ux * lateral,
            origin.Z);
    }

    private static RoofGeneratedTimberData Generated(string owner, int stationIndex) =>
        new(
            RoofGeneratedTimberDataSchema.CurrentVersion,
            owner,
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            stationIndex,
            13,
            900d,
            "layout");

    private static RoofRafterLayout Layout(MonopitchRoofGeometry geometry)
    {
        var result = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(900d, 80d));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Layout!;
    }

    private static MonopitchRoofGeometry Geometry(
        double runLength,
        double stationLength,
        double rotationDegrees,
        double slopeDegrees)
    {
        var radians = rotationDegrees * Math.PI / 180d;
        Assert.True(RoofDirection2D.TryCreate(Math.Cos(radians), Math.Sin(radians), out var run));
        Assert.True(RoofDirection2D.TryCreate(-run.Y, run.X, out var station));
        RoofPoint2D Point(double u, double v) =>
            new(run.X * u + station.X * v, run.Y * u + station.Y * v);
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [
                Point(0d, 0d),
                Point(runLength, 0d),
                Point(runLength, stationLength),
                Point(0d, stationLength),
            ],
            true));
        Assert.True(footprint.IsValid, footprint.Error.ToString());
        var solved = MonopitchRoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!,
            new RoofParameters(slopeDegrees, SlopeDirection: run),
            RoofKind.Monopitch));
        Assert.True(solved.IsValid, solved.Error.ToString());
        return Assert.IsType<MonopitchRoofGeometry>(solved.Geometry);
    }

    private static string ReadAutoCad(string fileName) => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "src",
        "AcKrovy.AutoCAD",
        "Infrastructure",
        fileName));

    private static string Member(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing start marker: {start}");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Missing end marker: {end}");
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
