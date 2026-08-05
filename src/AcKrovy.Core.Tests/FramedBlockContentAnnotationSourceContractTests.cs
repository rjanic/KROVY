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
        Assert.StartsWith("#if DEBUG", contentSideCommands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", contentSideCommands, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", contentSide, StringComparison.Ordinal);
        Assert.EndsWith("#endif", contentSide, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_FBC_NORMALIZE_CONTENT_SIDE", contentSideCommands);
        Assert.Contains("ResolveDimensionColumnSideFromContentLocalX(", contentSide);
        Assert.Contains("TryClassifyDimensionColumnSide(", contentSide);
        Assert.Contains("AcKrovyFramedBlockContentDefinitionService.Ensure(", contentSide);
        Assert.Contains("leader.BlockContentId =", contentSide);
        Assert.Contains("SetBlockAttribute(", contentSide);
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
