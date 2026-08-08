using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class FramedBlockContentAnnotationSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void CreateService_UsesLayoutCalculatorAndDefinitionService()
    {
        var service = Normalize(ServiceSource());
        var create = Member(
            service,
            "public static AutoCadFramedBlockContentAnnotationResult Create(");
        var createLeader = Member(
            service,
            "private static AutoCadFramedBlockContentAnnotationResult CreateLeader(");

        Assert.Contains(
            "TimberFramedBlockContentLayoutCalculator.Calculate(",
            create);
        Assert.Contains(
            "AcKrovyFramedBlockContentDefinitionService.Ensure(",
            create);
        Assert.Contains("FromWorldSide(", create);
        Assert.Contains("DesiredWorldSide", create);
        Assert.Contains("DimensionColumnSide", create);
        Assert.Contains("ContentType.BlockContent", createLeader);
        Assert.Contains("leader.SetBlockAttribute(", service);
        Assert.Contains("Matrix3d.Rotation(", createLeader);
        Assert.Contains("Vector3d.ZAxis", createLeader);
        Assert.Contains("layout.ReadableAngleRadians", createLeader);
        Assert.Contains(
            "Matrix3d.Rotation(readable, Vector3d.ZAxis, attachment)",
            createLeader);
        // G5C confirmed host contract: ConnectBase for Combined BlockContent.
        Assert.Contains(
            "BlockConnectionType.ConnectBase",
            createLeader);
        Assert.DoesNotContain(
            "BlockConnectionType.ConnectExtents",
            createLeader);
        // Legacy Combined helper flashes ConnectExtents + Left −X dogleg;
        // G5 create must never call it (LEFT grip-STRETCH AttrRef drift).
        Assert.DoesNotContain(
            "ApplyCombinedBlockInstanceProperties(",
            createLeader);
        Assert.DoesNotContain(
            "ApplyBlockInstanceProperties(",
            createLeader);
        Assert.Contains("ApplyCreateDogleg(", createLeader);
        // Production R3 create: CREATE-only 60° finalize; no K→D→I / full canonical stack.
        Assert.DoesNotContain("TryCorrectCombinedContentSide(", createLeader);
        Assert.DoesNotContain("ApplyCanonicalWorldGeometry(leader, layout)", createLeader);
        Assert.Contains("FinalizeCreateFirstSegmentSixtyDegrees(", createLeader);
        Assert.Contains(
            "TimberFramedCombinedG5CreateFirstSegmentRules",
            service);
        // CREATE 60° finalize must re-seat landing along readable +T (not stale BP).
        Assert.Contains("BuildCorrectedLandingEnd(", service);
        Assert.DoesNotContain(
            "Preserve world content anchor; only sync dogleg direction from",
            service);
        Assert.Contains(
            "TimberFramedCombinedG5ContentVariantRules.FromWorldSide(",
            create);
        // After ALL CREATE geometry is final, re-resolve R3 content variant from
        // FINAL world geometry — same helper as post-knee-STRETCH grip.
        Assert.Contains(
            "EnsureCorrectR3ContentVariantAfterCreateFinalize(",
            createLeader);
        Assert.Contains(
            "EnsureCorrectR3ContentVariantFromFinalGeometry(",
            service);
        var finalizeCall = createLeader.IndexOf(
            "FinalizeCreateFirstSegmentSixtyDegrees(",
            StringComparison.Ordinal);
        var contentVariantCall = createLeader.IndexOf(
            "EnsureCorrectR3ContentVariantAfterCreateFinalize(",
            StringComparison.Ordinal);
        Assert.True(finalizeCall > 0);
        Assert.True(
            contentVariantCall > finalizeCall,
            "CREATE content-variant ensure must run AFTER 60°/landing finalize.");
        // Content-variant ensure must not re-enter 60° / landing finalize helpers.
        var ensureBody = Member(
            service,
            "private static void EnsureCorrectR3ContentVariantAfterCreateFinalize(");
        Assert.DoesNotContain("FinalizeCreateFirstSegmentSixtyDegrees(", ensureBody);
        Assert.DoesNotContain("BuildCorrectedLandingEnd(", ensureBody);
        Assert.DoesNotContain("TryResolveCreateFinalization", ensureBody);
        Assert.DoesNotContain("SetFirstVertex(", ensureBody);
        Assert.DoesNotContain("SetLastVertex(", ensureBody);
        Assert.DoesNotContain("ApplyCreateDogleg(", ensureBody);
    }

    [Fact]
    public void CreateService_CreatesOnlyMLeaderBlockContentEntities()
    {
        var service = Normalize(ServiceSource());

        Assert.Contains("new MLeader()", service);
        Assert.DoesNotContain("new MText()", service);
        Assert.DoesNotContain("new DBText()", service);
        Assert.DoesNotContain("new BlockReference(", service);
        Assert.DoesNotContain("new Group(", service);
        Assert.DoesNotContain("AK_G5C_", service);
        Assert.DoesNotContain("AK_DEV_FRAMED_G5", service);
    }

    [Fact]
    public void CreateService_IsRoutedFromElementLabelServiceForFramedCombinedOnly()
    {
        var service = ServiceSource();
        var policy = PolicySource();
        var labels = ElementLabelServiceSource();
        var productionPolicy = ProductionPolicySource();
        var combined = service + policy;

        Assert.DoesNotContain("ElementLabelService.Upsert", service);
        Assert.DoesNotContain("ElementLabelService.Update", service);
        Assert.DoesNotContain("AutoCadFramedG4CompositeService", combined);
        Assert.DoesNotContain("LiveGeometrySynchronizationService", combined);
        Assert.DoesNotContain("TimberAnnotationRefreshPlanner", combined);
        Assert.DoesNotContain("XData", combined);
        Assert.DoesNotContain("CurrentVersion", combined);
        Assert.Contains(
            "AutoCadFramedBlockContentAnnotationService.Create(",
            labels);
        Assert.Contains(
            "AutoCadFramedBlockContentProductionPolicy.UsesG5CombinedFramed(",
            labels);
        Assert.Contains("UpsertG5CombinedFramedLeader(", labels);
        Assert.Contains("G5RendererGeneration", productionPolicy);
        Assert.Contains("UsesG5CombinedFramed(", productionPolicy);
        Assert.DoesNotContain("AK_LABELS", combined);
        Assert.DoesNotContain("AK_LABELSELECTED", combined);
        Assert.DoesNotContain("SourceHandle", policy);
        Assert.DoesNotContain("ElementId", policy);
        Assert.DoesNotContain("record AutoCadFramedBlockContentAnnotationRequest(\n" +
            "    string SourceHandle", policy);
        Assert.DoesNotContain("MigrationGeneration", policy);
    }

    [Fact]
    public void G5CombinedRefresh_PreservesExistingPlacementBeforeContentUpdate()
    {
        var upsert = Member(
            ElementLabelServiceSource(),
            "private static bool UpsertG5CombinedFramedLeader(");
        var update = Member(
            ElementLabelServiceSource(),
            "private static bool TryUpdateG5CombinedInPlace(");
        var write = Member(
            ElementLabelServiceSource(),
            "private static void WriteG5CombinedMetadata(");

        Assert.Contains(
            "TimberFramedCombinedG5RefreshPlacementRules.ManualOffsetMayMoveAnchor",
            upsert);
        Assert.Contains(
            "TryBuildG5CombinedRequest(",
            upsert);
        Assert.Contains("automaticPlacement", upsert);
        Assert.DoesNotContain("Anchor = placement.Anchor + manualDelta", upsert);

        var build = Member(
            ElementLabelServiceSource(),
            "private static bool TryBuildG5CombinedRequest(");
        Assert.Contains(
            "TimberFramedCombinedG5CreatePlacementRules.ResolveCreateLayoutSide",
            build);
        Assert.Contains("TryGetSourceElementAxisRadians", build);
        Assert.DoesNotContain("placement.Side,", build);

        var tryUpdate = upsert.IndexOf(
            "TryUpdateG5CombinedInPlace(",
            StringComparison.Ordinal);
        var erase = upsert.IndexOf(
            "EraseMainAnnotation(",
            tryUpdate,
            StringComparison.Ordinal);
        var create = upsert.IndexOf(
            "AutoCadFramedBlockContentAnnotationService.Create(",
            tryUpdate,
            StringComparison.Ordinal);
        Assert.True(tryUpdate >= 0 && erase > tryUpdate && create > erase);

        Assert.Contains(
            "TimberFramedCombinedG5RefreshPlacementRules.ShouldPreserveExistingPlacement(",
            update);
        Assert.Contains("automaticPlacement.Anchor.X", update);
        Assert.Contains("ApplyG5CombinedAttributeValues(", update);
        Assert.Contains("WriteG5CombinedMetadata(", update);
        Assert.Contains(
            ".DecideFromPersistedMetadata(",
            update);
        Assert.Contains("AnnotationRebuildRequired", update);
        Assert.DoesNotContain("TransformRelativePlacement(", update);
        Assert.DoesNotContain("TransformRelativeDirection(", update);
        Assert.DoesNotContain("SetLastVertex(", update);
        Assert.DoesNotContain("SetDogleg(", update);
        Assert.Contains(
            "TimberFramedCombinedG5SourceRotationRules.ResolveRefreshPresentationRadians(",
            update);
        Assert.Contains(
            "CaptureBlockContentPresentationRadians(",
            update);
        Assert.Contains(
            "TryResolveWorldContentXAxis(",
            update);
        Assert.Contains(
            "TryRestoreBlockContentPresentationAfterRefresh(",
            update);
        Assert.Contains(
            "ResolveContentOnlyRefreshPresentation(",
            update);
        var applyAttributes = update.IndexOf(
            "ApplyG5CombinedAttributeValues(",
            StringComparison.Ordinal);
        var restorePresentation = update.IndexOf(
            "TryRestoreBlockContentPresentationAfterRefresh(",
            StringComparison.Ordinal);
        Assert.True(applyAttributes >= 0 && restorePresentation > applyAttributes);
        // Length-only refresh must correct measured world-after-content to the
        // exact world-before-refresh presentation in relative BR space.
        Assert.DoesNotContain(
            "ApplyReadableBlockContentOrientation(",
            update);
        Assert.DoesNotContain("TryCorrectCombinedContentSide(", update);
        Assert.DoesNotContain("!placementEval.Current.IsCorrect", update);

        Assert.Contains("ReadG5Attachment(leader)", write);
        Assert.Contains("leader.BlockPosition", write);
        Assert.Contains("TextX = liveBlockPosition.X", write);
        Assert.Contains("AnchorX = liveAttachment.X", write);
        Assert.Contains("LocalManualOffsetAlongAxisMm = 0d", write);
        Assert.Contains("RotationRadians = sourcePhysicalAxis", write);
        Assert.Contains("PlacementRotationRadians = placementRotation", write);
    }

    [Fact]
    public void SourceRotation_RebuildsThroughCanonicalProductionCreateBeforeAnyInPlaceMutation()
    {
        var labels = ElementLabelServiceSource();
        var upsert = Member(
            labels,
            "private static bool UpsertG5CombinedFramedLeader(");
        var update = Member(
            labels,
            "private static bool TryUpdateG5CombinedInPlace(");
        var inspect = Normalize(Read(
            "src/AcKrovy.AutoCAD/Commands/AutoCadFramedG5ProductionAnnotationInspectCommands.cs"));
        var trace = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/ElementLabelSourceRotationRebuildTrace.cs"));

        var decide = update.IndexOf(
            ".DecideFromPersistedMetadata(",
            StringComparison.Ordinal);
        var rebuildGate = update.IndexOf(
            "sourceRotationRebuildDecision.AnnotationRebuildRequired",
            decide,
            StringComparison.Ordinal);
        var preserveMeasurement = update.IndexOf(
            "TryResolveWorldContentXAxis(",
            StringComparison.Ordinal);
        var contentUpdate = update.IndexOf(
            "ApplyG5CombinedAttributeValues(",
            StringComparison.Ordinal);
        Assert.True(
            decide >= 0 && rebuildGate > decide && preserveMeasurement > rebuildGate &&
            contentUpdate > preserveMeasurement);

        Assert.DoesNotContain("TransformRelativePlacement(", update);
        Assert.DoesNotContain("leader.TransformBy(", update);
        Assert.DoesNotContain("BlockPosition =", update);
        Assert.DoesNotContain("SetLastVertex(", update);

        var tryUpdate = upsert.IndexOf("TryUpdateG5CombinedInPlace(", StringComparison.Ordinal);
        var erase = upsert.IndexOf("EraseMainAnnotation(", tryUpdate, StringComparison.Ordinal);
        var create = upsert.IndexOf(
            "AutoCadFramedBlockContentAnnotationService.Create(",
            erase,
            StringComparison.Ordinal);
        var metadata = upsert.IndexOf("WriteG5CombinedMetadata(", create, StringComparison.Ordinal);
        var completeTrace = upsert.IndexOf(
            "CompleteSourceRotationRebuildTrace(",
            metadata,
            StringComparison.Ordinal);
        Assert.True(
            tryUpdate >= 0 && erase > tryUpdate && create > erase &&
            metadata > create && completeTrace > metadata);

        Assert.Contains("SourceAxisBeforeDeg=", inspect);
        Assert.Contains("SourceAxisAfterDeg=", inspect);
        Assert.Contains("SourceAxisDeltaDeg=", inspect);
        Assert.Contains("PhysicalSourceAxisBeforeDeg=", inspect);
        Assert.Contains("PhysicalSourceAxisAfterDeg=", inspect);
        Assert.Contains("PhysicalSourceAxisDeltaDeg=", inspect);
        Assert.Contains("SourceAxisSemantics=PhysicalStartToEnd", inspect);
        Assert.Contains("SourceRotationDetected=", inspect);
        Assert.Contains("AnnotationRebuildRequired=", inspect);
        Assert.Contains("AnnotationRebuilt=", inspect);
        Assert.Contains("OldAnnotationHandle=", inspect);
        Assert.Contains("NewAnnotationHandle=", inspect);
        Assert.Contains("RebuildReason=", inspect);
        Assert.Contains("StringComparer.OrdinalIgnoreCase", trace);
    }

    [Fact]
    public void SourceRotationBatch_IsDistinctAndAnnotationGripNeverQueuesSourceRebuild()
    {
        var liveGeometry = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs"));
        var objectModified = Member(
            liveGeometry,
            "private void ObjectModified(");
        var filter = Member(
            liveGeometry,
            "private static IReadOnlyList<ObjectId> FilterTimberElementIds(");
        var refresh = Member(
            liveGeometry,
            "private static void RefreshTimberElements(");

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

        Assert.Contains("foreach (var id in ids.Distinct())", filter);
        Assert.Contains("foreach (var id in timberIds)", refresh);
        Assert.Contains(
            "TimberAnnotationService.DeleteDuplicatesForExistingSourceHandles(",
            refresh);
        Assert.Contains("LiveGeometryCommandRules.IsUndoRedoCommand(", refresh);
    }

    [Fact]
    public void AttributeHeights_UseBaselineAttrRefAndBlockScaleNotDenomInBtr()
    {
        var service = Normalize(ServiceSource());
        var policy = Normalize(PolicySource());
        var apply = Member(
            service,
            "private static IReadOnlyList<string> ApplyAttributeValues(");

        Assert.Contains("ItemAttributeBaselineHeightMm", apply);
        Assert.Contains("DimensionAttributeBaselineHeightMm", apply);
        Assert.Contains("leader.BlockScale = new Scale3d(blockScale)", service);
        Assert.Contains(
            "TimberAnnotationScaleRules.GetScaleFactor(AnnotationScaleDenominator)",
            policy);
        Assert.DoesNotContain("attribute.TextStyleId =", apply);
        Assert.DoesNotContain("EpsilonRotateRadians", apply);
    }

    [Fact]
    public void Stabilization_EpsilonIsNotDefaultProductionPath()
    {
        var service = Normalize(ServiceSource());
        var policy = Normalize(PolicySource());
        var stabilize = Member(
            service,
            "private static void ApplyStabilization(");

        Assert.Contains(
            "StabilizationMode =\n        AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh)",
            policy);
        Assert.Contains("RecordGraphicsModified(true)", stabilize);
        Assert.Contains("EpsilonRotate", stabilize);
        Assert.Contains(
            "mode != AutoCadFramedBlockContentStabilizationMode.EpsilonRotate",
            stabilize);
        Assert.DoesNotContain(
            "StabilizationMode =\n        AutoCadFramedBlockContentStabilizationMode.EpsilonRotate)",
            policy);
    }

    [Fact]
    public void DebugCreateVerify_IsDebugGuardedAndUsesProductionCreate()
    {
        var commands = CommandsSource().Trim();
        var verify = VerifyServiceSource().Trim();

        Assert.StartsWith("#if DEBUG", commands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", commands, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", verify, StringComparison.Ordinal);
        Assert.EndsWith("#endif", verify, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_FBC_CREATE_VERIFY", commands);
        Assert.Contains("AK_DEV_FBC_CREATE_CLEAN", commands);
        Assert.Contains(
            "AutoCadFramedBlockContentAnnotationService.Create(",
            verify);

        var stretchDiagCommands = StretchDiagCommandsSource().Trim();
        var stretchDiag = StretchDiagServiceSource().Trim();
        Assert.StartsWith("#if DEBUG", stretchDiagCommands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", stretchDiagCommands, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", stretchDiag, StringComparison.Ordinal);
        Assert.EndsWith("#endif", stretchDiag, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_FBC_LEFT_STRETCH_DIAG", stretchDiagCommands);
        Assert.Contains("GetBlockAttribute(", stretchDiag);
        Assert.Contains("BlockConnectionType", stretchDiag);
        Assert.Contains("AlignmentPoint", stretchDiag);
        Assert.Contains("WorldToBlockLocal(", stretchDiag);
        Assert.Contains("ResolveEffectiveBlockContentRotationRadians(", stretchDiag);
        Assert.Contains("World-space K→D→I", stretchDiag);
        Assert.Contains("TryEvaluate(", stretchDiag);
        Assert.DoesNotContain("SetBlockAttribute(", stretchDiag);
        Assert.DoesNotContain("attribute.Position =", stretchDiag);
        Assert.DoesNotContain("attribute.AlignmentPoint =", stretchDiag);
        Assert.DoesNotContain("leader.TransformBy(", stretchDiag);

        var normalizeCommands = NormalizeDoglegCommandsSource().Trim();
        var normalize = NormalizeDoglegServiceSource().Trim();
        Assert.StartsWith("#if DEBUG", normalizeCommands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", normalizeCommands, StringComparison.Ordinal);
        Assert.False(
            normalize.StartsWith("#if DEBUG", StringComparison.Ordinal),
            "Shared dogleg normalize must be available in Release for production GripOverrule.");
        Assert.Contains("AK_DEV_FBC_NORMALIZE_DOGLEG", normalizeCommands);
        Assert.Contains(
            "TimberFramedBlockContentDoglegRules.TryNormalizeDoglegGeometry(",
            normalize);
        Assert.Contains("LandingPointsTowardAttachment", normalize);
        Assert.Contains("LeaderKneeSide", normalize);
        Assert.Contains("ContentDoglegSide", normalize);
        Assert.Contains("PositiveT", normalize);
        Assert.Contains("NegativeT", normalize);
        Assert.Contains("WorldLeft", normalize);
        Assert.Contains("WorldRight", normalize);
        Assert.Contains("TimberLeaderTangentSign", normalize);
        Assert.Contains("MeasureConnectBaseContentOffsetMm", normalize);
        Assert.Contains("leader.SetDogleg(", normalize);
        Assert.Contains("SetLastVertex(", normalize);
        Assert.Contains("PERSISTED_AFTER", normalize);
        Assert.Contains("transaction.Commit()", normalize);
        Assert.Contains("TryNormalizeDogleg(", normalize);
        Assert.DoesNotContain("TryResolveDoglegGeometry(", normalize);
        Assert.DoesNotContain("ResolveDoglegDirection(", normalize);
        Assert.DoesNotContain("SetBlockAttribute(", normalize);
        Assert.DoesNotContain("attribute.Position =", normalize);
        Assert.DoesNotContain("attribute.AlignmentPoint =", normalize);
        Assert.DoesNotContain("Erase(", normalize);
        // May reaffirm the same BlockContentId if host drifted — never swap families.
        Assert.Contains("BlockContentId", normalize);
        Assert.DoesNotContain("CreateRawKey(", normalize);

        var contentSideCommands = NormalizeContentSideCommandsSource().Trim();
        var contentSide = NormalizeContentSideServiceSource().Trim();
        var columnPlacement = DimensionColumnPlacementServiceSource().Trim();
        Assert.StartsWith("#if DEBUG", contentSideCommands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", contentSideCommands, StringComparison.Ordinal);
        Assert.False(
            contentSide.StartsWith("#if DEBUG", StringComparison.Ordinal),
            "Shared content-side normalize must be available in Release for production GripOverrule.");
        Assert.Contains("AK_DEV_FBC_NORMALIZE_CONTENT_SIDE", contentSideCommands);
        Assert.Contains("TryCorrectCombinedContentSide(", contentSide);
        Assert.Contains("FormatEvaluationDiagnostics(", contentSide);
        Assert.Contains("IsContentSideNoOp(", contentSide);
        Assert.Contains("TryEvaluate(", columnPlacement);
        Assert.Contains("EvaluateMirroredDimensionColumnPlacement(", columnPlacement);
        Assert.Contains("TryParseR2VariantKey(", columnPlacement);
        Assert.Contains(
            "EnsureCorrectR3ContentVariantFromFinalGeometry(",
            columnPlacement);
        Assert.Contains("TrySwapR3ContentVariantIfSideChanged(", columnPlacement);
        Assert.Contains("PreserveBlockContentPresentationRotation(", columnPlacement);
        Assert.Contains(
            "ResolvePreservedPresentationRadians(",
            columnPlacement);
        var restoreReadable = Member(
            columnPlacement,
            "private static void RestoreReadableContentOrientation(");
        Assert.Contains(
            "ResolvePreservedPresentationRadians(",
            restoreReadable);
        Assert.DoesNotContain(
            "ApplyReadableBlockContentOrientation(",
            restoreReadable);
        Assert.DoesNotContain(
            "TimberFramedBlockContentReadableOrientationRules.Decide(",
            restoreReadable);
        Assert.Contains(
            "CaptureBlockContentPresentationRadians(",
            columnPlacement);
        Assert.Contains("TryResolveRequiredContentVariant(", columnPlacement);
        Assert.Contains("knee/frame", columnPlacement);
        Assert.Contains("TryResolveEffectiveBlockLocalXAxis(", columnPlacement);
        Assert.Contains("TryResolveWorldContentXAxis(", columnPlacement);
        Assert.Contains(
            "TryRestoreBlockContentPresentationAfterRefresh(",
            columnPlacement);
        Assert.Contains(
            "ResolveContentOnlyRefreshPresentation(",
            columnPlacement);
        Assert.Contains("AcKrovyFramedBlockContentDefinitionService.Ensure(", columnPlacement);
        Assert.Contains("leader.BlockContentId =", columnPlacement);
        Assert.Contains("SetBlockAttribute(", columnPlacement);
        var r3Swap = Member(
            columnPlacement,
            "public static bool TrySwapR3ContentVariantIfSideChanged(");
        Assert.DoesNotContain("RestoreGeometry(", r3Swap);
        Assert.DoesNotContain("SetFirstVertex(", r3Swap);
        Assert.DoesNotContain("SetLastVertex(", r3Swap);
        Assert.DoesNotContain("SetDogleg(", r3Swap);
        Assert.Contains("post-swap K→D→I failed", columnPlacement);
        Assert.Contains("TryNormalizeContentSide(", contentSide);
        Assert.Contains("TryNormalizeDogleg(", NormalizeDoglegServiceSource());
        Assert.Contains("changed=True", contentSide);
        Assert.Contains("changed=False", contentSide);
        Assert.Contains("PERSISTED_AFTER", contentSide);
        Assert.Contains("attachmentDrift=", contentSide);
        Assert.Contains("kneeDrift=", contentSide);
        Assert.Contains("bpDrift=", contentSide);
        Assert.DoesNotContain("TryNormalizeDoglegGeometry(", contentSide);
        Assert.DoesNotContain("attribute.Position =", contentSide);
        Assert.DoesNotContain("attribute.AlignmentPoint =", contentSide);
        Assert.DoesNotContain("Erase(", contentSide);
        Assert.DoesNotContain("new MLeader()", contentSide);
        // Visual authority is world K→D→I — not effectiveLocalX / BlockRotation.
        Assert.DoesNotContain(
            "TryClassifyRequiredDimensionColumnSide(",
            contentSide);
        Assert.DoesNotContain(
            "ResolveEffectiveBlockContentRotationRadians(",
            contentSide);
        Assert.DoesNotContain(
            "TryClassifyDimensionColumnSide(",
            contentSide);
        Assert.DoesNotContain(
            "Degenerate BlockPosition − knee (content local X ~ 0).",
            contentSide);
        Assert.Contains("ValidateCombinedWorldColumnPlacement(", verify);
        Assert.Contains("FormatEvaluationDiagnostics(", verify);
        Assert.Contains("K→D→I", verify);

        var lifecycleCommands = StretchNormalizeLifecycleCommandsSource().Trim();
        var lifecycle = StretchNormalizeLifecycleServiceSource().Trim();
        var liveGeometry = LiveGeometrySynchronizationSource();
        Assert.StartsWith("#if DEBUG", lifecycleCommands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", lifecycleCommands, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", lifecycle, StringComparison.Ordinal);
        Assert.EndsWith("#endif", lifecycle, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_FBC_LIFECYCLE_TRACE_ON", lifecycleCommands);
        Assert.Contains("AK_DEV_FBC_LIFECYCLE_TRACE_OFF", lifecycleCommands);
        Assert.Contains("AK_DEV_FBC_LIFECYCLE_PROOF_ON", lifecycleCommands);
        Assert.Contains("AK_DEV_FBC_LIFECYCLE_PROOF_OFF", lifecycleCommands);
        Assert.Contains("AK_DEV_FBC_LIFECYCLE_STATUS", lifecycleCommands);
        Assert.Contains("AK_DEV_FBC_LIFECYCLE_CONFIRM", lifecycleCommands);
        Assert.Contains("AK_DEV_FBC_LIFECYCLE_TEST_ON", lifecycleCommands);
        Assert.Contains("AK_DEV_FBC_LIFECYCLE_TEST_OFF", lifecycleCommands);
        Assert.Contains("Lifecycle test armed for GRIP_STRETCH.", lifecycleCommands);
        Assert.Contains("Lifecycle test disabled.", lifecycleCommands);
        Assert.Contains("TryNormalizeDogleg(", lifecycle);
        Assert.Contains("TryNormalizeContentSide(", lifecycle);
        Assert.Contains("DoglegStep", lifecycle);
        Assert.Contains("ContentSideStep", lifecycle);
        Assert.Contains("UndoBlockerReason", lifecycle);
        Assert.Contains("RunDeferredProcessorForTest(", lifecycle);
        Assert.Contains("EnqueueAndDrainNormalizeForTest(", lifecycle);
        Assert.Contains("BeginAutotestIsolation(", lifecycle);
        Assert.Contains("EndAutotestIsolation(", lifecycle);
        Assert.Contains("ArmLifecycleTest(", lifecycle);
        Assert.Contains("ProcessCommandEnded(", liveGeometry);
        Assert.Contains("TraceQueueMLeader(", liveGeometry);
        Assert.Contains("AutoCadRedoDiagService.OnCommandWillStart(", liveGeometry);
        Assert.Contains("AutoCadRedoDiagService.OnCommandEnded(", liveGeometry);
        Assert.Contains("AutoCadRedoDiagService.OnLiveGeometryRefreshBegin(", liveGeometry);
        Assert.Contains("AutoCadRedoDiagService.OnLiveGeometryRefreshEnd(", liveGeometry);
        Assert.Contains("LiveGeometryCommandRules.IsUndoRedoCommand(", liveGeometry);
        Assert.Contains("OnLiveGeometryRefreshSkippedUndoRedo(", liveGeometry);
        Assert.Contains("ClearPendingLiveGeometryState(", liveGeometry);
        Assert.Contains("_ignoreCurrentCommand = isUndoRedo;", liveGeometry);
        Assert.Contains(
            "Keep ignore armed after U/UNDO/REDO/MREDO",
            liveGeometry);
        Assert.Contains(
            "AutoCadFramedBlockContentGripPassthroughProofService.RemoveSession(",
            liveGeometry);
        Assert.Contains(
            "AutoCadFramedBlockContentGripReadonlyProofService.RemoveSession(",
            liveGeometry);
        Assert.Contains(
            "AutoCadFramedBlockContentGripNormalizeProofService.RemoveSession(",
            liveGeometry);
        Assert.DoesNotContain("SendStringToExecute(", lifecycle);

        var gripUndoCommands = GripUndoProofCommandsSource().Trim();
        var gripUndo = GripUndoProofServiceSource().Trim();
        Assert.StartsWith("#if DEBUG", gripUndoCommands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", gripUndoCommands, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", gripUndo, StringComparison.Ordinal);
        Assert.EndsWith("#endif", gripUndo, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_FBC_UNDO_PROOF_SETUP", gripUndoCommands);
        Assert.Contains("AK_DEV_FBC_UNDO_PROOF_STATUS", gripUndoCommands);
        Assert.Contains("AK_DEV_FBC_UNDO_PROOF_OFF", gripUndoCommands);
        Assert.Contains("AK_DEV_FBC_UNDO_PROOF_CLEAN", gripUndoCommands);
        Assert.Contains("class FramedBlockContentGripUndoOverrule : GripOverrule", gripUndo);
        Assert.Contains("private static readonly bool FullNormalizeGripOverruleAllowed = false", gripUndo);
        Assert.Contains("HARD-DISABLED", gripUndo);
        Assert.Contains("ForceUnregisterAll(", gripUndo);
        Assert.Contains(
            "AutoCadFramedBlockContentGripReadonlyProofService.ForceUnregisterAll()",
            gripUndo);
        Assert.Contains(
            "AutoCadFramedBlockContentGripNormalizeProofService.ForceUnregisterAll()",
            gripUndo);
        Assert.Contains("ForceUnregisterOverrule(", gripUndo);
        Assert.Contains("Overruling = false", gripUndo);
        Assert.Contains("entity.GetGripPoints(gripPoints, osnapModes, geomIds)", gripUndo);
        Assert.Contains("entity.GetGripPoints(", gripUndo);
        Assert.Contains("curViewUnitSize,", gripUndo);
        Assert.Contains("GetGripPointsFlags bitFlags)", gripUndo);
        Assert.Contains("base.MoveGripPointsAt(entity, indices, offset)", gripUndo);
        Assert.Contains(
            "base.MoveGripPointsAt(entity, grips, offset, bitFlags)",
            gripUndo);
        Assert.Contains("NormalizeAfterNativeGrip(", gripUndo);
        Assert.Contains("TryNormalizeDogleg(", gripUndo);
        Assert.Contains("TryNormalizeContentSide(", gripUndo);
        Assert.Contains("ForceP4aLifecycleOff(", gripUndo);
        Assert.Contains("DebugRegAppName = \"AK_DEV_FBC_UNDO_PROOF\"", gripUndo);
        // Hard-disable gate must precede any AddOverrule registration path.
        var gate = gripUndo.IndexOf(
            "FullNormalizeGripOverruleAllowed = false",
            StringComparison.Ordinal);
        var enableProof = gripUndo.IndexOf(
            "private static void EnableProof(",
            StringComparison.Ordinal);
        var addOverrule = gripUndo.IndexOf(
            "Overrule.AddOverrule(",
            StringComparison.Ordinal);
        Assert.True(gate > 0);
        Assert.True(enableProof > gate);
        Assert.True(addOverrule > enableProof);
        Assert.Contains("if (!FullNormalizeGripOverruleAllowed)", gripUndo);
        // Write moment still after native offset (base.MoveGripPointsAt first).
        var classicMove = gripUndo.IndexOf(
            "base.MoveGripPointsAt(entity, indices, offset)",
            StringComparison.Ordinal);
        var gripDataMove = gripUndo.IndexOf(
            "base.MoveGripPointsAt(entity, grips, offset, bitFlags)",
            StringComparison.Ordinal);
        var afterMove = gripUndo.IndexOf(
            "AfterNativeGripMove(entity, before)",
            StringComparison.Ordinal);
        Assert.True(classicMove > 0);
        Assert.True(gripDataMove > classicMove);
        Assert.True(afterMove > classicMove);
        // Native grips must be forwarded — never invent a custom grip set.
        Assert.DoesNotContain("new GripData(", gripUndo);
        Assert.DoesNotContain("grips.Clear()", gripUndo);
        Assert.DoesNotContain("gripPoints.Clear()", gripUndo);
        Assert.DoesNotContain("SendStringToExecute(", gripUndo);
        // No programmatic grip inventory while overrule could be registered.
        Assert.DoesNotContain("QueryGrip", gripUndo);
        Assert.DoesNotContain("GripInventory", gripUndo);

        var gripPassCommands = GripPassthroughProofCommandsSource().Trim();
        var gripPass = GripPassthroughProofServiceSource().Trim();
        Assert.StartsWith("#if DEBUG", gripPassCommands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", gripPassCommands, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", gripPass, StringComparison.Ordinal);
        Assert.EndsWith("#endif", gripPass, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_FBC_GRIP_PASSTHROUGH_SETUP", gripPassCommands);
        Assert.Contains("AK_DEV_FBC_GRIP_PASSTHROUGH_OFF", gripPassCommands);
        Assert.Contains("AK_DEV_FBC_GRIP_PASSTHROUGH_CLEAN", gripPassCommands);
        Assert.Contains(
            "class FramedBlockContentGripPassthroughOverrule : GripOverrule",
            gripPass);
        Assert.Contains("DebugRegAppName = \"AK_DEV_FBC_GRIP_PASSTHROUGH\"", gripPass);
        Assert.Contains("base.GetGripPoints(entity, gripPoints, osnapModes, geomIds)", gripPass);
        Assert.Contains("base.GetGripPoints(", gripPass);
        Assert.Contains("base.MoveGripPointsAt(entity, indices, offset)", gripPass);
        Assert.Contains(
            "base.MoveGripPointsAt(entity, grips, offset, bitFlags)",
            gripPass);
        Assert.Contains("dbObject.ObjectId == _trackedLeaderId", gripPass);
        Assert.Contains("ForceUnregisterAll(", gripPass);
        Assert.Contains("RegisterOverrule(", gripPass);
        // Pass-through must not run normalize / dogleg / content-side / queue.
        Assert.DoesNotContain("TryNormalizeDogleg(", gripPass);
        Assert.DoesNotContain("TryNormalizeContentSide(", gripPass);
        Assert.DoesNotContain("NormalizeAfterNativeGrip(", gripPass);
        Assert.DoesNotContain("new GripData(", gripPass);
        Assert.DoesNotContain("grips.Clear()", gripPass);
        Assert.DoesNotContain("StartTransaction(", Member(gripPass, "public override bool IsApplicable(RXObject overruledSubject)"));
        Assert.DoesNotContain("GetObject(", Member(gripPass, "public override bool IsApplicable(RXObject overruledSubject)"));
        Assert.DoesNotContain("SendStringToExecute(", gripPass);
        // Create before register: RegisterOverrule call site after Create commit path.
        var createCall = gripPass.IndexOf(
            "AutoCadFramedBlockContentAnnotationService.Create(",
            StringComparison.Ordinal);
        var registerCall = gripPass.IndexOf(
            "RegisterOverrule(leaderId)",
            StringComparison.Ordinal);
        Assert.True(createCall > 0);
        Assert.True(registerCall > createCall);

        var gripReadonlyCommands = GripReadonlyProofCommandsSource().Trim();
        var gripReadonly = GripReadonlyProofServiceSource().Trim();
        Assert.StartsWith("#if DEBUG", gripReadonlyCommands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", gripReadonlyCommands, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", gripReadonly, StringComparison.Ordinal);
        Assert.EndsWith("#endif", gripReadonly, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_FBC_GRIP_READONLY_SETUP", gripReadonlyCommands);
        Assert.Contains("AK_DEV_FBC_GRIP_READONLY_STATUS", gripReadonlyCommands);
        Assert.Contains("AK_DEV_FBC_GRIP_READONLY_OFF", gripReadonlyCommands);
        Assert.Contains("AK_DEV_FBC_GRIP_READONLY_CLEAN", gripReadonlyCommands);
        Assert.Contains(
            "class FramedBlockContentGripReadonlyOverrule : GripOverrule",
            gripReadonly);
        Assert.Contains("DebugRegAppName = \"AK_DEV_FBC_GRIP_READONLY\"", gripReadonly);
        Assert.Contains("base.GetGripPoints(entity, gripPoints, osnapModes, geomIds)", gripReadonly);
        Assert.Contains("base.MoveGripPointsAt(entity, indices, offset)", gripReadonly);
        Assert.Contains(
            "base.MoveGripPointsAt(entity, grips, offset, bitFlags)",
            gripReadonly);
        Assert.Contains("dbObject.ObjectId == _trackedLeaderId", gripReadonly);
        Assert.Contains("TryInspectAfterNativeGripMove(", gripReadonly);
        Assert.Contains("WouldNormalizeDogleg", gripReadonly);
        Assert.Contains("WouldNormalizeContentSide", gripReadonly);
        Assert.Contains("currentPlacementCorrect", gripReadonly);
        Assert.Contains("BaseMoveCompletedCount", gripReadonly);
        Assert.Contains("InspectionSuccessCount", gripReadonly);
        Assert.Contains("InspectionTransientSkipCount", gripReadonly);
        Assert.Contains("InspectionNotApplicableCount", gripReadonly);
        Assert.Contains("LastExceptionType", gripReadonly);
        Assert.Contains("LastExceptionMessage", gripReadonly);
        Assert.Contains("LastExceptionStack", gripReadonly);
        Assert.Contains("FirstExceptionCallbackIndex", gripReadonly);
        Assert.Contains("LastExceptionCallbackIndex", gripReadonly);
        Assert.Contains("StageDReadiness", gripReadonly);
        Assert.Contains("StageDZeroExceptionReady", gripReadonly);
        Assert.Contains("StageEArmDecision", gripReadonly);
        Assert.Contains("StageEArmReason", gripReadonly);
        Assert.Contains("TransientGeometryNotReady", gripReadonly);
        Assert.Contains("ForceP4aLifecycleOff(", gripReadonly);
        Assert.Contains("ForceUnregisterAll(", gripReadonly);
        Assert.DoesNotContain("TryNormalizeDogleg(", gripReadonly);
        Assert.DoesNotContain("TryNormalizeContentSide(", gripReadonly);
        Assert.DoesNotContain("new GripData(", gripReadonly);
        Assert.DoesNotContain("grips.Clear()", gripReadonly);
        Assert.DoesNotContain("SendStringToExecute(", gripReadonly);
        Assert.DoesNotContain("ProcessCommandEnded(", gripReadonly);
        Assert.DoesNotContain(
            "StartTransaction(",
            Member(gripReadonly, "public override bool IsApplicable(RXObject overruledSubject)"));
        var readonlyInspect = Member(
            gripReadonly,
            "private static TimberFramedBlockContentGripReadOnlyInspectionOutcome");
        Assert.Contains("TryInspectAfterNativeGripMove(", readonlyInspect);
        Assert.DoesNotContain("UpgradeOpen(", readonlyInspect);
        Assert.DoesNotContain("OpenMode.ForWrite", readonlyInspect);
        Assert.DoesNotContain("TryNormalizeDogleg(", readonlyInspect);
        Assert.DoesNotContain("TryNormalizeContentSide(", readonlyInspect);
        Assert.DoesNotContain("StartTransaction(", readonlyInspect);
        Assert.DoesNotContain("GetObject(leader.ObjectId", readonlyInspect);
        Assert.DoesNotContain("GetObject(leader.ObjectId,", readonlyInspect);
        var readonlyClassicMove = gripReadonly.IndexOf(
            "base.MoveGripPointsAt(entity, indices, offset)",
            StringComparison.Ordinal);
        var readonlyInspectCall = gripReadonly.IndexOf(
            "TryInspectAfterNativeGripMove(\r\n                            document,\r\n                            leader,",
            StringComparison.Ordinal);
        if (readonlyInspectCall < 0)
        {
            readonlyInspectCall = gripReadonly.IndexOf(
                "var outcome = TryInspectAfterNativeGripMove(",
                StringComparison.Ordinal);
        }

        Assert.True(readonlyClassicMove > 0);
        Assert.True(readonlyInspectCall > readonlyClassicMove);

        var gripNormalizeCommands = GripNormalizeProofCommandsSource().Trim();
        var gripNormalize = GripNormalizeProofServiceSource().Trim();
        Assert.StartsWith("#if DEBUG", gripNormalizeCommands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", gripNormalizeCommands, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", gripNormalize, StringComparison.Ordinal);
        Assert.EndsWith("#endif", gripNormalize, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_FBC_GRIP_NORMALIZE_SETUP", gripNormalizeCommands);
        Assert.Contains("AK_DEV_FBC_GRIP_NORMALIZE_STATUS", gripNormalizeCommands);
        Assert.Contains("AK_DEV_FBC_GRIP_NORMALIZE_OFF", gripNormalizeCommands);
        Assert.Contains("AK_DEV_FBC_GRIP_NORMALIZE_CLEAN", gripNormalizeCommands);
        Assert.DoesNotContain("AK_DEV_FBC_GRIP_NORMALIZE_REDO_TRACE_ON", gripNormalizeCommands);
        Assert.DoesNotContain("AK_DEV_FBC_GRIP_NORMALIZE_REDO_TRACE_OFF", gripNormalizeCommands);

        var redoDiagCommands = RedoDiagCommandsSource().Trim();
        var redoDiag = RedoDiagServiceSource().Trim();
        Assert.StartsWith("#if DEBUG", redoDiagCommands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", redoDiagCommands, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", redoDiag, StringComparison.Ordinal);
        Assert.EndsWith("#endif", redoDiag, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_REDO_DIAG_ON", redoDiagCommands);
        Assert.Contains("AK_DEV_REDO_DIAG_STATUS", redoDiagCommands);
        Assert.Contains("AK_DEV_REDO_DIAG_OFF", redoDiagCommands);
        Assert.Contains("AK_DEV_FBC_GRIP_REGISTRATION_STATUS", redoDiagCommands);
        Assert.Contains("UndoCommandObserved", redoDiag);
        Assert.Contains("LiveGeometryRefreshExecutedAfterUndo", redoDiag);
        Assert.Contains("LiveGeometryRefreshSkippedUndoRedo", redoDiag);
        Assert.Contains("WritesAfterUndo", redoDiag);
        Assert.Contains("MutationsAfterUndo", redoDiag);
        Assert.Contains("OnLiveGeometryRefreshSkippedUndoRedo(", redoDiag);
        Assert.Contains("public static bool IsUndoRedoCommand(", LiveGeometryCommandRulesSource());
        Assert.Contains("ak_dev_redo_diag_", redoDiag);
        Assert.DoesNotContain("StartTransaction(", redoDiag);
        Assert.DoesNotContain("LockDocument(", redoDiag);
        Assert.DoesNotContain("OpenMode.ForWrite", redoDiag);
        Assert.Contains("IsOverruleRegistered", GripPassthroughProofServiceSource());
        Assert.Contains("IsOverruleRegistered", GripNormalizeProofServiceSource());
        Assert.Contains("AutoCadRedoDiagService.NoteDllInit(", PluginEntrySource());
        Assert.Contains(
            "class FramedBlockContentGripNormalizeOverrule : GripOverrule",
            gripNormalize);
        Assert.Contains("DebugRegAppName = \"AK_DEV_FBC_GRIP_NORMALIZE\"", gripNormalize);
        Assert.Contains("base.GetGripPoints(entity, gripPoints, osnapModes, geomIds)", gripNormalize);
        Assert.Contains("base.MoveGripPointsAt(entity, indices, offset)", gripNormalize);
        Assert.Contains(
            "base.MoveGripPointsAt(entity, grips, offset, bitFlags)",
            gripNormalize);
        Assert.DoesNotContain("OnGripStatusChanged(Entity entity, GripStatus status)", gripNormalize);
        Assert.DoesNotContain("GripStatus.GripsDone", gripNormalize);
        Assert.DoesNotContain("PersistentNormalizeWriteCount", gripNormalize);
        Assert.DoesNotContain("WritesDuringUndo", gripNormalize);
        Assert.DoesNotContain("WritesDuringRedo", gripNormalize);
        Assert.DoesNotContain("PreviewCallbackCount", gripNormalize);
        Assert.DoesNotContain("FinalizeCallbackCount", gripNormalize);
        Assert.DoesNotContain("DecidePersistentWritePolicy(", gripNormalize);
        Assert.DoesNotContain("AppendRedoTrace(", gripNormalize);
        Assert.Contains("dbObject.ObjectId == _trackedLeaderId", gripNormalize);
        Assert.Contains("TryNormalizeAfterNativeMove(", gripNormalize);
        Assert.Contains("TryNormalizeDogleg(", gripNormalize);
        Assert.Contains("TryNormalizeContentSide(", gripNormalize);
        Assert.Contains("NormalizeChangedCount", gripNormalize);
        Assert.Contains("NormalizeNoOpCount", gripNormalize);
        Assert.Contains("TransientSkipCount", gripNormalize);
        Assert.Contains("BaseMoveCompletedCount", gripNormalize);
        Assert.Contains("K→D→I already correct", gripNormalize);
        Assert.Contains(
            "InspectWriteOpenCallbackMLeader",
            Read("src/AcKrovy.Core/Services/TimberFramedBlockContentGripStageProofRules.cs"));
        Assert.DoesNotContain("ReopenApplicableMLeader", gripNormalize);
        Assert.Contains("ForceP4aLifecycleOff(", gripNormalize);
        Assert.Contains("ForceUnregisterAll(", gripNormalize);
        Assert.Contains("FormatNormalizeState(", gripNormalize);
        Assert.Contains("CallbackFailed", gripNormalize);
        Assert.Contains("SameUndoHostSequenceDeferredDocumentation", gripNormalize);
        Assert.Contains(
            "POST→U→PRE→MREDO→POST",
            Read("src/AcKrovy.Core/Services/TimberFramedBlockContentGripStageProofRules.cs"));
        Assert.DoesNotContain(
            "PersistentNormalizeWriteCount=1",
            Read("src/AcKrovy.Core/Services/TimberFramedBlockContentGripStageProofRules.cs"));
        Assert.Contains("DecideStageEArm(", gripNormalize);
        Assert.Contains("StageDReadiness", gripNormalize);
        Assert.Contains("StageEArmDecision", gripNormalize);
        Assert.Contains("StageEArmReason", gripNormalize);
        Assert.Contains("StageEImplementationMode", gripNormalize);
        Assert.Contains("AbortPartialSetup(", gripNormalize);
        Assert.Contains(
            "StageEBlockedUnresolvedExceptionsMessage",
            Read("src/AcKrovy.Core/Services/TimberFramedBlockContentGripStageProofRules.cs"));
        Assert.Contains(
            "Stage E blocked: read-only grip proof has unresolved callback exceptions.",
            Read("src/AcKrovy.Core/Services/TimberFramedBlockContentGripStageProofRules.cs"));
        Assert.Contains(
            "current build uses direct callback entity and non-throwing transient handling",
            Read("src/AcKrovy.Core/Services/TimberFramedBlockContentGripStageProofRules.cs"));
        Assert.DoesNotContain(
            "with ExceptionCount=0 and InspectionSuccessCount>=1 first.",
            gripNormalize);
        Assert.DoesNotContain("new GripData(", gripNormalize);
        Assert.DoesNotContain("grips.Clear()", gripNormalize);
        Assert.DoesNotContain("SendStringToExecute(", gripNormalize);
        Assert.DoesNotContain("ProcessCommandEnded(", gripNormalize);
        Assert.DoesNotContain(
            "StartTransaction(",
            Member(gripNormalize, "public override bool IsApplicable(RXObject overruledSubject)"));
        var normalizeBody = Member(
            gripNormalize,
            "private static TimberFramedBlockContentGripNormalizeOutcome");
        Assert.Contains("TryNormalizeAfterNativeMove(", normalizeBody);
        Assert.DoesNotContain("GetObject(leader.ObjectId", normalizeBody);
        Assert.DoesNotContain("GetObject(leader.ObjectId,", normalizeBody);
        Assert.DoesNotContain("OpenMode.ForWrite", normalizeBody);
        var doglegInNormalize = normalizeBody.IndexOf(
            "TryNormalizeDogleg(",
            StringComparison.Ordinal);
        var contentInNormalize = normalizeBody.IndexOf(
            "TryNormalizeContentSide(",
            StringComparison.Ordinal);
        Assert.True(doglegInNormalize > 0);
        Assert.True(contentInNormalize > doglegInNormalize);
        var normalizeCallbackBody = Member(
            gripNormalize,
            "private static void RunNormalizeCallback(Entity entity, Action baseMove)");
        var normalizeClassicMove = normalizeCallbackBody.IndexOf(
            "base.MoveGripPointsAt(entity, indices, offset)",
            StringComparison.Ordinal);
        if (normalizeClassicMove < 0)
        {
            // Classic move is in the overload that invokes RunNormalizeCallback;
            // within the callback, baseMove() stands in for base.MoveGripPointsAt.
            normalizeClassicMove = normalizeCallbackBody.IndexOf(
                "baseMove();",
                StringComparison.Ordinal);
        }

        var normalizeAfterCall = normalizeCallbackBody.IndexOf(
            "TryNormalizeAfterNativeMove(",
            StringComparison.Ordinal);
        Assert.True(normalizeClassicMove > 0);
        Assert.True(normalizeAfterCall > normalizeClassicMove);
        Assert.DoesNotContain("isFinalizeCallback", normalizeCallbackBody);
        Assert.DoesNotContain("isFinalizeCallback", gripNormalize);
        // Undo proof remains hard-disabled; Stage E does not re-arm it.
        Assert.Contains(
            "FullNormalizeGripOverruleAllowed = false",
            GripUndoProofServiceSource());

        var pluginEntry = Read("src/AcKrovy.AutoCAD/PluginEntry.cs");
        Assert.Contains(
            "AutoCadFramedBlockContentGripReadonlyProofService.ForceUnregisterAll()",
            pluginEntry);
        Assert.Contains(
            "AutoCadFramedBlockContentGripNormalizeProofService.ForceUnregisterAll()",
            pluginEntry);
        Assert.Contains(
            "AutoCadFramedBlockContentGripPassthroughProofService.ForceUnregisterAll()",
            pluginEntry);
        Assert.Contains(
            "AutoCadFramedBlockContentProductionGripNormalizeService.RegisterOnce()",
            pluginEntry);
        Assert.Contains(
            "AutoCadFramedBlockContentProductionGripNormalizeService.Unregister()",
            pluginEntry);

        var productionGripCommands = ProductionGripNormalizeCommandsSource().Trim();
        var productionGrip = ProductionGripNormalizeServiceSource().Trim();
        Assert.StartsWith("#if DEBUG", productionGripCommands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", productionGripCommands, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_FBC_PRODUCTION_GRIP_STATUS", productionGripCommands);
        Assert.False(
            productionGrip.StartsWith("#if DEBUG", StringComparison.Ordinal),
            "Production GripOverrule must compile in Release.");
        Assert.Contains(
            "class FramedBlockContentProductionGripNormalizeOverrule : GripOverrule",
            productionGrip);
        Assert.Contains("RegisterOnce(", productionGrip);
        Assert.Contains("Unregister(", productionGrip);
        Assert.Contains("base.GetGripPoints(entity, gripPoints, osnapModes, geomIds)", productionGrip);
        Assert.Contains("base.MoveGripPointsAt(entity, indices, offset)", productionGrip);
        Assert.Contains(
            "base.MoveGripPointsAt(entity, grips, offset, bitFlags)",
            productionGrip);
        Assert.Contains("TryNormalizeAfterNativeMove(", productionGrip);
        Assert.Contains("TryNormalizeR3ContentVariantOnly(", productionGrip);
        Assert.Contains(
            "EnsureCorrectR3ContentVariantFromFinalGeometry(",
            productionGrip);
        Assert.Contains("TrySyncPresentationFromFinalLanding(", productionGrip);
        Assert.Contains(
            "TryResolveWorldContentXAxis(",
            productionGrip);
        Assert.Contains(
            "ResolveFinalContentPresentation(",
            productionGrip);
        Assert.Contains("R3ReferencePresentationRevision", productionGrip);
        Assert.Contains(
            "preserveAdoptedReferenceVerticalFamily",
            productionGrip);
        Assert.Contains(
            "PreserveBlockContentPresentationRotation(",
            productionGrip);
        Assert.Contains(
            "TimberFramedBlockContentGripPresentationRules",
            productionGrip);
        Assert.DoesNotContain("TryCapturePreGripPresentationRadians(", productionGrip);
        Assert.Contains("IsR3ContentVariantOnlyPath(", productionGrip);
        Assert.Contains("TryNormalizeDogleg(", productionGrip);
        Assert.Contains("TryNormalizeContentSide(", productionGrip);
        Assert.Contains("IsProductionApplicableBlockContent(", productionGrip);
        Assert.Contains("HasAnyDebugProofMarker(", productionGrip);
        Assert.Contains("DebugProofRegAppNames", productionGrip);
        Assert.DoesNotContain("GripStatus.GripsDone", productionGrip);
        Assert.DoesNotContain("SendStringToExecute(", productionGrip);
        Assert.DoesNotContain("ProcessCommandEnded(", productionGrip);
        Assert.DoesNotContain(
            "StartTransaction(",
            Member(productionGrip, "public override bool IsApplicable(RXObject overruledSubject)"));
        var productionNormalizeBody = Member(
            productionGrip,
            "private static TimberFramedBlockContentGripNormalizeOutcome");
        Assert.Contains("TryNormalizeAfterNativeMove(", productionNormalizeBody);
        Assert.DoesNotContain("GetObject(leader.ObjectId", productionNormalizeBody);
        Assert.DoesNotContain("GetObject(leader.ObjectId,", productionNormalizeBody);
        Assert.DoesNotContain("OpenMode.ForWrite", productionNormalizeBody);
        var productionCallbackBody = Member(
            productionGrip,
            "private static void RunNormalizeCallback(Entity entity, Action baseMove)");
        Assert.DoesNotContain(
            "TryCapturePreGripPresentationRadians(",
            productionCallbackBody);
        var productionAfter = productionCallbackBody.IndexOf(
            "TryNormalizeAfterNativeMove(",
            StringComparison.Ordinal);
        var productionBaseMoveBeforeNormalize = productionCallbackBody.LastIndexOf(
            "baseMove();",
            productionAfter,
            StringComparison.Ordinal);
        // Native move first (leader authority), then post-move landing sync + R3.
        Assert.True(productionBaseMoveBeforeNormalize > 0);
        Assert.True(productionAfter > productionBaseMoveBeforeNormalize);
        var r3Idx = productionGrip.IndexOf(
            "TryNormalizeR3ContentVariantOnly(",
            StringComparison.Ordinal);
        Assert.True(r3Idx > 0);
        var r3Slice = productionGrip.Substring(
            r3Idx,
            Math.Min(3500, productionGrip.Length - r3Idx));
        Assert.Contains("TrySyncPresentationFromFinalLanding(", r3Slice);
        Assert.Contains(
            "EnsureCorrectR3ContentVariantFromFinalGeometry(",
            r3Slice);
        var syncBefore = r3Slice.IndexOf(
            "TrySyncPresentationFromFinalLanding(",
            StringComparison.Ordinal);
        var ensureAfter = r3Slice.IndexOf(
            "EnsureCorrectR3ContentVariantFromFinalGeometry(",
            StringComparison.Ordinal);
        Assert.True(syncBefore > 0);
        Assert.True(ensureAfter > syncBefore);
        Assert.DoesNotContain("SetFirstVertex(", r3Slice);
        Assert.DoesNotContain("SetLastVertex(", r3Slice);
        Assert.Contains(
            "effectiveContentWorldAngleRadians:",
            productionGrip);
        Assert.Contains(
            "IsUndoRedoCommand(",
            LiveGeometryCommandRulesSource());
        Assert.Contains(
            "normalized.Equals(\"U\", StringComparison.OrdinalIgnoreCase)",
            LiveGeometryCommandRulesSource());
        Assert.Contains(
            "normalized.Equals(\"UNDO\", StringComparison.OrdinalIgnoreCase)",
            LiveGeometryCommandRulesSource());
        Assert.Contains(
            "normalized.Equals(\"REDO\", StringComparison.OrdinalIgnoreCase)",
            LiveGeometryCommandRulesSource());
        Assert.Contains(
            "normalized.Equals(\"MREDO\", StringComparison.OrdinalIgnoreCase)",
            LiveGeometryCommandRulesSource());
        Assert.Contains("TrimStart('_', '.', '\\'')", LiveGeometryCommandRulesSource());

        var autotestCommands = AutotestCommandsSource().Trim();
        var autotest = AutotestServiceSource().Trim();
        Assert.StartsWith("#if DEBUG", autotestCommands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", autotestCommands, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", autotest, StringComparison.Ordinal);
        Assert.EndsWith("#endif", autotest, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_FBC_AUTOTEST_ALL", autotestCommands);
        Assert.Contains("AK_DEV_FBC_AUTOTEST_CLEAN", autotestCommands);
        Assert.Contains("TryNormalizeDogleg(", autotest);
        Assert.Contains("TryNormalizeContentSide(", autotest);
        Assert.Contains("AutoCadFramedBlockContentAnnotationService.Create(", autotest);
        Assert.Contains("TryComputeSyntheticKneeOnlyCrossing(", autotest);
        Assert.Contains("TryInjectOppositeCombinedBtr(", autotest);
        Assert.Contains("SyntheticCrossingSetup", autotest);
        Assert.Contains("ContentSideWrongBtr", autotest);
        Assert.Contains("RecordFixtureFailure(", autotest);
        Assert.Contains("BeginAutotestIsolation(", autotest);
        Assert.Contains("EnqueueAndDrainNormalizeForTest(", autotest);
        Assert.Contains("DoglegGeometry", autotest);
        Assert.Contains("ContentSideForbiddenDrift", autotest);
        Assert.Contains("RunnerIsolation", autotest);
        Assert.Contains("DebugRegAppName = \"AK_DEV_FBC_AUTOTEST\"", autotest);
        Assert.Contains("FBC_AUTOTEST", autotest);
        Assert.DoesNotContain("TryComputeSyntheticOppositeSideCrossing(", autotest);
        Assert.DoesNotContain("SendStringToExecute(", autotest);
        Assert.DoesNotContain("Editor.GetEntity", autotest);

        Assert.Contains("ContentType.BlockContent", verify);
        Assert.Contains("DebugRegAppName = \"AK_DEV_FBC_CREATE\"", verify);
        Assert.Contains("oldDebugEntitiesFound=", verify);
        Assert.Contains("oldDebugEntitiesErased=", verify);
        Assert.Contains("currentRunMLeaders=", verify);
        Assert.Contains("standaloneBlockReference=", verify);
        Assert.Contains("EraseMarkedDebugEntities(", verify);
        Assert.Contains("MarkDebugEntity(", verify);
        Assert.Contains("ValidateCurrentRunInventory(", verify);
        Assert.DoesNotContain("ValidateModelSpaceInventory(", verify);
        Assert.DoesNotContain("ElementLabelService", verify);
        Assert.DoesNotContain("AK_LABELS", verify);
        Assert.DoesNotContain("AK_G5C_", verify);
        Assert.DoesNotContain("GetHashCode", verify);
        Assert.Contains("CombinedFramedLandingDistanceMm", verify);
        Assert.DoesNotContain("FramedItemLandingDistanceMm", verify);
        Assert.Contains("ValidateAttrRefRelativeDrift", verify);
        Assert.Contains("ValidateKneeStretchAttrRefRigidity", verify);
        Assert.Contains("ResolveGridPoint", verify);
        Assert.DoesNotContain(
            "AutoCadFramedBlockContentStabilizationMode.EpsilonRotate)",
            verify);
    }

    [Fact]
    public void CreateOrder_IsHorizontalThenAttributesThenFinalTransform()
    {
        var create = Member(
            Normalize(ServiceSource()),
            "private static AutoCadFramedBlockContentAnnotationResult CreateLeader(");
        var append = create.IndexOf(
            "modelSpace.AppendEntity(leader)",
            StringComparison.Ordinal);
        var attrs = create.IndexOf(
            "ApplyAttributeValues(",
            StringComparison.Ordinal);
        var transform = create.IndexOf(
            "leader.TransformBy(",
            StringComparison.Ordinal);
        var stabilize = create.IndexOf(
            "ApplyStabilization(",
            StringComparison.Ordinal);

        Assert.True(append > 0);
        Assert.True(attrs > append);
        Assert.True(transform > attrs);
        Assert.True(stabilize > transform);
        Assert.Contains("BlockRotation = 0d", create);
        Assert.Contains("layout.ReadableAngleRadians", create);
        Assert.Contains("leader.BlockRotation = 0d", create);
        Assert.Contains(
            "ResolveCreateReferenceFinalWorldPresentation(",
            create);
        var variantFinalize = create.IndexOf(
            "EnsureCorrectR3ContentVariantAfterCreateFinalize(",
            StringComparison.Ordinal);
        var referenceCorrection = create.IndexOf(
            "ResolveCreateReferenceFinalWorldPresentation(",
            StringComparison.Ordinal);
        Assert.True(
            variantFinalize >= 0 && referenceCorrection > variantFinalize,
            "90°/180° half-turn must be content-only after leader/variant finalization.");
        Assert.Contains("TryResolveWorldContentXAxis(", create);
        Assert.Contains("presentationDecision.TargetBlockRotation", create);
        Assert.Contains("leader.RecordGraphicsModified(true)", create);
        Assert.Contains(
            "TimberFramedBlockContentReadableOrientationRules",
            Read("src/AcKrovy.Core/Services/TimberFramedBlockContentReadableOrientationRules.cs"));
        Assert.DoesNotContain("ApplyCanonicalWorldGeometry(leader, layout)", create);
        Assert.DoesNotContain("TryCorrectCombinedContentSide(", create);
        // CREATE must not post-hoc ApplyReadable after TransformBy (ZLÉ double-orient).
        var transformIdx = create.IndexOf(
            "leader.TransformBy(",
            StringComparison.Ordinal);
        var applyReadableAfterTransform = create.IndexOf(
            "ApplyReadableBlockContentOrientation(",
            transformIdx,
            StringComparison.Ordinal);
        Assert.True(
            applyReadableAfterTransform < 0,
            "CREATE must keep BlockRotation=0 after TransformBy; presentation restore is refresh-only via PreserveBlockContentPresentationRotation.");
    }

    [Fact]
    public void ProductionInspect_ExposesCreateVerticalFinalWorldTrace()
    {
        var inspect = Read(
            "src/AcKrovy.AutoCAD/Commands/AutoCadFramedG5ProductionAnnotationInspectCommands.cs");

        Assert.Contains("VerticalRuleInputDeg=", inspect);
        Assert.Contains("VerticalRuleOutputDeg=", inspect);
        Assert.Contains("TransformByAngleDeg=", inspect);
        Assert.Contains("BlockRotationBeforeDeg=", inspect);
        Assert.Contains("BlockRotationRequestedDeg=", inspect);
        Assert.Contains("BlockRotationAfterDeg=", inspect);
        Assert.Contains("MLeaderBlockRotationDeg=", inspect);
        Assert.Contains("FrameWorldOrientationDeg=", inspect);
        Assert.Contains("ItemTextWorldAngleDeg=", inspect);
        Assert.Contains("WidthTextWorldAngleDeg=", inspect);
        Assert.Contains("HeightTextWorldAngleDeg=", inspect);
        Assert.Contains("TryGetCreatePresentationTrace(", inspect);
        Assert.Contains("CreateVerticalRuleCalled=", inspect);
        Assert.Contains("PresentationPath=", inspect);
        Assert.Contains("PresentationOperationSequence=", inspect);
    }

    [Fact]
    public void ProductionAutotest_RequiresNegativeVerticalThroughActualCreatePath()
    {
        var cases = Read(
            "src/AcKrovy.Core/Services/TimberFramedBlockContentAutotestRules.cs");
        var autotest = Normalize(AutotestServiceSource());
        var runCase = Member(autotest, "private static ObjectId RunCase(");
        var create = runCase.IndexOf(
            "AutoCadFramedBlockContentAnnotationService.Create(",
            StringComparison.Ordinal);
        var validate = runCase.IndexOf(
            "ValidateCreateReferenceFinalWorldAngles(",
            StringComparison.Ordinal);
        var createValidation = Member(
            autotest,
            "private static bool ValidateCreateReferenceFinalWorldAngles(");
        var refreshValidation = Member(
            autotest,
            "private static bool ValidateExistingReferenceUpdateAndSecondRefresh(");

        Assert.Contains("SLOT-COMB-R-270-D50", cases);
        Assert.True(create >= 0 && validate > create);
        Assert.Contains(
            "IsExpectedReferencePresentationCase(testCase)",
            createValidation);
        Assert.Contains("trace.SourcePhysicalAxisAngle", createValidation);
        Assert.Contains("trace.BlockRotationRequested", createValidation);
        Assert.Contains("trace.ReferenceRevisionAfter", createValidation);
        Assert.Contains(
            "IsExpectedReferencePresentationCase(testCase)",
            refreshValidation);
        Assert.DoesNotContain("IsReferenceHalfTurnSource(", createValidation);
        Assert.DoesNotContain("IsReferenceHalfTurnSource(", refreshValidation);
    }

    [Fact]
    public void ExistingR3Refresh_AdoptsReferenceWorldTargetAfterPreservationOnly()
    {
        var elementLabels = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/ElementLabelService.cs"));
        var update = Member(
            elementLabels,
            "private static bool TryUpdateG5CombinedInPlace(");
        var preserve = update.IndexOf(
            "TryRestoreBlockContentPresentationAfterRefresh(",
            StringComparison.Ordinal);
        var reference = update.IndexOf(
            "ApplyG5CombinedReferencePresentationAfterRefresh(",
            StringComparison.Ordinal);
        Assert.True(preserve >= 0 && reference > preserve);

        var correction = Member(
            elementLabels,
            "internal static int ApplyG5CombinedReferencePresentationAfterRefresh(");
        Assert.Contains("TryResolveWorldContentXAxis(", correction);
        Assert.Contains("ResolveCreateReferenceFinalWorldPresentation(", correction);
        Assert.Contains("ShouldAdoptReferencePresentation(", correction);
        Assert.Contains("TrySyncG5CombinedContentVariant(", correction);
        Assert.Contains("ReferencePresentationRevision", correction);
        Assert.DoesNotContain("SetFirstVertex(", correction);
        Assert.DoesNotContain("SetLastVertex(", correction);
        Assert.DoesNotContain("SetDogleg(", correction);
        Assert.DoesNotContain("DoglegLength =", correction);
        Assert.DoesNotContain("BlockPosition =", correction);
    }

    [Fact]
    public void ProductionVerticalWholeAnnotationHalfTurn_UsesOneSharedRigidActuator()
    {
        var create = Member(
            Normalize(ServiceSource()),
            "private static AutoCadFramedBlockContentAnnotationResult CreateLeader(");
        var update = Member(
            Normalize(ElementLabelServiceSource()),
            "private static bool TryUpdateG5CombinedInPlace(");
        var helper = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/AutoCadWholeMLeaderHalfTurnService.cs"));
        var debugCommand = Normalize(Read(
            "src/AcKrovy.AutoCAD/Commands/AutoCadRotateAnnotation180Commands.cs"));
        var inspect = Normalize(Read(
            "src/AcKrovy.AutoCAD/Commands/AutoCadFramedG5ProductionAnnotationInspectCommands.cs"));
        var labelStore = Normalize(Read(
            "src/AcKrovy.AutoCAD/Infrastructure/ElementLabelStore.cs"));

        var createFinalize = create.IndexOf(
            "EnsureCorrectR3ContentVariantAfterCreateFinalize(",
            StringComparison.Ordinal);
        var createWhole = create.LastIndexOf(
            "AutoCadWholeMLeaderHalfTurnService.TryApplyRequiredState(",
            StringComparison.Ordinal);
        Assert.True(createFinalize >= 0 && createWhole > createFinalize);

        var preserve = update.IndexOf(
            "TryRestoreBlockContentPresentationAfterRefresh(",
            StringComparison.Ordinal);
        var reference = update.IndexOf(
            "ApplyG5CombinedReferencePresentationAfterRefresh(",
            StringComparison.Ordinal);
        var updateWhole = update.IndexOf(
            "AutoCadWholeMLeaderHalfTurnService.TryApplyRequiredState(",
            StringComparison.Ordinal);
        var metadataWrite = update.IndexOf(
            "WriteG5CombinedMetadata(",
            StringComparison.Ordinal);
        Assert.True(
            preserve >= 0 && reference > preserve && updateWhole > reference &&
            metadataWrite > updateWhole);
        Assert.DoesNotContain("wholeAnnotationHalfTurnAppliedBefore", update);
        Assert.DoesNotContain("canonicalNewDogleg", update);
        Assert.DoesNotContain("entityDogleg", update);
        Assert.DoesNotContain("TransformRelativePlacement(", update);

        Assert.Equal(1, CountOccurrences(helper, "leader.TransformBy(rotation);"));
        Assert.Contains(
            "Matrix3d.Rotation(\n            Math.PI,\n            Vector3d.ZAxis,\n            before.Attachment)",
            helper);
        Assert.DoesNotContain("leader.BlockRotation =", helper);
        Assert.DoesNotContain("SetFirstVertex(", helper);
        Assert.DoesNotContain("SetLastVertex(", helper);
        Assert.DoesNotContain("leader.BlockPosition =", helper);
        Assert.DoesNotContain("ElementLabelStore.Write", helper);
        Assert.Contains("TryApplyRigidHalfTurn(", debugCommand);
        Assert.DoesNotContain("leader.TransformBy(", debugCommand);

        Assert.Contains("WholeAnnotationHalfTurnRequired=", inspect);
        Assert.Contains("WholeAnnotationHalfTurnApplied=", inspect);
        Assert.Contains("WholeAnnotationTransformAppliedThisOperation=", inspect);
        Assert.Contains("PresentationLifecyclePath=", inspect);
        Assert.DoesNotContain("WholeAnnotationHalfTurnApplied { get; init; }", labelStore);
        Assert.Contains("LabelMetadataSchemaVersion = 5", ProductionPolicySource());
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentAnnotationService.cs");

    private static string PolicySource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentAnnotationPolicy.cs");

    private static string ProductionPolicySource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentProductionPolicy.cs");

    private static string CommandsSource() => Read(
        "src/AcKrovy.AutoCAD/Commands/AutoCadFramedBlockContentCreateVerifyCommands.cs");

    private static string VerifyServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentCreateVerifyService.cs");

    private static string StretchDiagCommandsSource() => Read(
        "src/AcKrovy.AutoCAD/Commands/AutoCadFramedBlockContentLeftStretchDiagCommands.cs");

    private static string StretchDiagServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentLeftStretchDiagService.cs");

    private static string NormalizeDoglegCommandsSource() => Read(
        "src/AcKrovy.AutoCAD/Commands/AutoCadFramedBlockContentNormalizeDoglegCommands.cs");

    private static string NormalizeDoglegServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentNormalizeDoglegService.cs");

    private static string NormalizeContentSideCommandsSource() => Read(
        "src/AcKrovy.AutoCAD/Commands/AutoCadFramedBlockContentNormalizeContentSideCommands.cs");

    private static string NormalizeContentSideServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentNormalizeContentSideService.cs");

    private static string DimensionColumnPlacementServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentDimensionColumnPlacementService.cs");

    private static string StretchNormalizeLifecycleCommandsSource() => Read(
        "src/AcKrovy.AutoCAD/Commands/AutoCadFramedBlockContentStretchNormalizeLifecycleCommands.cs");

    private static string StretchNormalizeLifecycleServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentStretchNormalizeLifecycleService.cs");

    private static string GripUndoProofCommandsSource() => Read(
        "src/AcKrovy.AutoCAD/Commands/AutoCadFramedBlockContentGripUndoProofCommands.cs");

    private static string GripUndoProofServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentGripUndoProofService.cs");

    private static string GripPassthroughProofCommandsSource() => Read(
        "src/AcKrovy.AutoCAD/Commands/AutoCadFramedBlockContentGripPassthroughProofCommands.cs");

    private static string GripPassthroughProofServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentGripPassthroughProofService.cs");

    private static string GripReadonlyProofCommandsSource() => Read(
        "src/AcKrovy.AutoCAD/Commands/AutoCadFramedBlockContentGripReadonlyProofCommands.cs");

    private static string GripReadonlyProofServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentGripReadonlyProofService.cs");

    private static string GripNormalizeProofCommandsSource() => Read(
        "src/AcKrovy.AutoCAD/Commands/AutoCadFramedBlockContentGripNormalizeProofCommands.cs");

    private static string GripNormalizeProofServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentGripNormalizeProofService.cs");

    private static string ProductionGripNormalizeCommandsSource() => Read(
        "src/AcKrovy.AutoCAD/Commands/AutoCadFramedBlockContentProductionGripNormalizeCommands.cs");

    private static string ProductionGripNormalizeServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentProductionGripNormalizeService.cs");

    private static string RedoDiagCommandsSource() => Read(
        "src/AcKrovy.AutoCAD/Commands/AutoCadRedoDiagCommands.cs");

    private static string RedoDiagServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadRedoDiagService.cs");

    private static string PluginEntrySource() => Read(
        "src/AcKrovy.AutoCAD/PluginEntry.cs");

    private static string AutotestCommandsSource() => Read(
        "src/AcKrovy.AutoCAD/Commands/AutoCadFramedBlockContentAutotestCommands.cs");

    private static string AutotestServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentAutotestService.cs");

    private static string LiveGeometrySynchronizationSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs");

    private static string LiveGeometryCommandRulesSource() => Read(
        "src/AcKrovy.Core/Services/LiveGeometryCommandRules.cs");

    private static string ElementLabelServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/ElementLabelService.cs");

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relativePath));

    private static string Normalize(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Member(string source, string signature)
    {
        var normalized = Normalize(source);
        var start = normalized.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing member signature: {signature}");
        var brace = normalized.IndexOf('{', start);
        Assert.True(brace > start);
        var depth = 0;
        for (var i = brace; i < normalized.Length; i++)
        {
            if (normalized[i] == '{')
            {
                depth++;
            }
            else if (normalized[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return normalized[start..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces for {signature}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
