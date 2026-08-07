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
        Assert.Contains("ResolveCreateDimensionColumnSide(", create);
        Assert.Contains("DimensionColumnSide", create);
        Assert.Contains("ContentType.BlockContent", createLeader);
        Assert.Contains("leader.SetBlockAttribute(", service);
        Assert.Contains("Matrix3d.Rotation(", createLeader);
        Assert.Contains("Vector3d.ZAxis", createLeader);
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
        Assert.Contains("ApplyNormalizeDoglegFromLeader(", createLeader);
        Assert.Contains("TryCorrectCombinedContentSide(", createLeader);
        Assert.Contains(
            "TimberFramedBlockContentDoglegRules.TryResolveCreateDoglegGeometry(",
            service);
        Assert.Contains(
            "TimberFramedBlockContentDoglegRules.TryNormalizeDoglegGeometry(",
            service);
        Assert.Contains("landingEnd", service);
        Assert.DoesNotContain("ApplyGeometricDogleg(", service);
        Assert.DoesNotContain("TryResolveDoglegGeometry(", service);
        Assert.DoesNotContain("ResolveDoglegDirection(", service);
        Assert.DoesNotContain("ReassertDoglegFromGeometry(", service);
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
    public void CreateService_IsNotRoutedFromElementLabelServiceAndLeavesLifecycleAlone()
    {
        var service = ServiceSource();
        var policy = PolicySource();
        var labels = ElementLabelServiceSource();
        var combined = service + policy;

        Assert.DoesNotContain("ElementLabelService.", service);
        Assert.DoesNotContain("AutoCadFramedG4CompositeService", combined);
        Assert.DoesNotContain("LiveGeometrySynchronizationService", combined);
        Assert.DoesNotContain("TimberAnnotationRefreshPlanner", combined);
        Assert.DoesNotContain("XData", combined);
        Assert.DoesNotContain("CurrentVersion", combined);
        Assert.DoesNotContain(
            "AutoCadFramedBlockContentAnnotationService",
            labels);
        Assert.DoesNotContain("AK_LABELS", combined);
        Assert.DoesNotContain("AK_LABELSELECTED", combined);
        Assert.DoesNotContain("SourceHandle", policy);
        Assert.DoesNotContain("ElementId", policy);
        Assert.DoesNotContain("record AutoCadFramedBlockContentAnnotationRequest(\n" +
            "    string SourceHandle", policy);
        Assert.DoesNotContain("MigrationGeneration", policy);
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
        Assert.StartsWith("#if DEBUG", normalize, StringComparison.Ordinal);
        Assert.EndsWith("#endif", normalize, StringComparison.Ordinal);
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
        Assert.StartsWith("#if DEBUG", contentSide, StringComparison.Ordinal);
        Assert.EndsWith("#endif", contentSide, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_FBC_NORMALIZE_CONTENT_SIDE", contentSideCommands);
        Assert.Contains("TryCorrectCombinedContentSide(", contentSide);
        Assert.Contains("FormatEvaluationDiagnostics(", contentSide);
        Assert.Contains("IsContentSideNoOp(", contentSide);
        Assert.Contains("TryEvaluate(", columnPlacement);
        Assert.Contains("EvaluateMirroredDimensionColumnPlacement(", columnPlacement);
        Assert.Contains("TryParseR2VariantKey(", columnPlacement);
        Assert.Contains("AcKrovyFramedBlockContentDefinitionService.Ensure(", columnPlacement);
        Assert.Contains("leader.BlockContentId =", columnPlacement);
        Assert.Contains("SetBlockAttribute(", columnPlacement);
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
    }

    private static string ServiceSource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentAnnotationService.cs");

    private static string PolicySource() => Read(
        "src/AcKrovy.AutoCAD/Infrastructure/AutoCadFramedBlockContentAnnotationPolicy.cs");

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
