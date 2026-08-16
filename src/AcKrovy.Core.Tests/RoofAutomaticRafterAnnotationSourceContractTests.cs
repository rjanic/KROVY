using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticRafterAnnotationSourceContractTests
{
    private static readonly string Workflow = Read("RoofRafterCommandWorkflow.cs");
    private static readonly string Replacement = Read("RoofGeneratedRafterSetService.cs");
    private static readonly string SourceCreation =
        Read("TimberSourceLineCreationService.cs");
    private static readonly string ItemIdentity =
        Read("TimberElementItemIdentityService.cs");
    private static readonly string BatchAnnotations =
        Read("TimberCreatedElementAnnotationService.cs");
    private static readonly string AnnotationService = Read("TimberAnnotationService.cs");
    private static readonly string ArrowRenderer = Read("SlopeArrowService.cs");

    [Fact]
    public void FreshGenerationPassesOnlyExactCreationResultToCanonicalAnnotationPath()
    {
        Assert.Contains("RoofGeneratedRafterSetService.Materialize(", Workflow);
        Assert.Contains(
            "TimberSourceLineCreationService.Create(",
            Replacement);
        Assert.Contains(
            "TimberCreatedElementAnnotationService.EnsureForCreatedElements(",
            Replacement);
        Assert.Contains("created,", Replacement);
        Assert.Contains("createdElements", BatchAnnotations);
        Assert.Contains("TimberAnnotationService.EnsureForElement(", BatchAnnotations);
    }

    [Fact]
    public void CreationResultIsProjectedBackToExactCreatedObjectIds()
    {
        Assert.Contains("var createdIds = new List<ObjectId>(requests.Count)", SourceCreation);
        Assert.Contains("createdIds.Add(id)", SourceCreation);
        Assert.Contains("var synchronizedDataById =", SourceCreation);
        Assert.Contains("createdIds.ToDictionary(", SourceCreation);
        Assert.Contains("id => synchronizedDataById[id]", SourceCreation);
        Assert.DoesNotContain(
            "return TimberElementItemIdentityService.SynchronizeElementIds(",
            SourceCreation);
    }

    [Fact]
    public void DrawingWideIdentityMeasurementCannotExpandAnnotationTargets()
    {
        Assert.Contains("DrawingScanner.FindAllTimberElements", ItemIdentity);
        Assert.Contains("targetSet.Contains(entry.Id)", ItemIdentity);
        Assert.Contains("result[entry.Id] = updatedData", ItemIdentity);
        Assert.Contains("createdIds.ToDictionary(", SourceCreation);
        Assert.DoesNotContain("DrawingScanner", BatchAnnotations);
        Assert.DoesNotContain("SynchronizeElementIds", BatchAnnotations);
    }

    [Fact]
    public void BatchPathCannotScanOrRepairUnrelatedDrawingElements()
    {
        var production = Workflow + BatchAnnotations;
        Assert.DoesNotContain("DrawingScanner", production);
        Assert.DoesNotContain("ModelSpace", BatchAnnotations);
        Assert.DoesNotContain("FindByOwner", BatchAnnotations);
        Assert.DoesNotContain("UpdateAll", BatchAnnotations);
        Assert.DoesNotContain("CreateMissing", BatchAnnotations);
        Assert.Contains("foreach (var (sourceId, data) in annotatedElements)", BatchAnnotations);
        Assert.Contains("transaction.GetObject(sourceId, OpenMode.ForRead)", BatchAnnotations);
    }

    [Fact]
    public void NoAnnotationsCreatesNoAnnotationOwnedArtifactsOrCleanupPass()
    {
        var data = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            AnnotationMode = TimberAnnotationMode.NoAnnotations,
        };
        var plan = TimberAnnotationRefreshPlanner.Create(data, false);

        Assert.False(plan.EnsureLabel);
        Assert.False(plan.ReconcileSlopeArrow);
        Assert.False(plan.ReconcileSlopeAngleText);
        Assert.Contains("TimberAnnotationModeRules.Normalize(item.Value.AnnotationMode)", BatchAnnotations);
        Assert.Contains("TimberAnnotationMode.NoAnnotations", BatchAnnotations);
        Assert.True(
            BatchAnnotations.IndexOf("annotatedElements.Length == 0", StringComparison.Ordinal) <
            BatchAnnotations.IndexOf("AutoCadAnnotationPresentationBatchContext.Create", StringComparison.Ordinal));
        Assert.DoesNotContain("DeleteFor", BatchAnnotations);
        Assert.DoesNotContain("DeleteDuplicates", BatchAnnotations);
    }

    [Theory]
    [InlineData(TimberAnnotationMode.ItemNumberLeader)]
    [InlineData(TimberAnnotationMode.DimensionsLeader)]
    [InlineData(TimberAnnotationMode.FullLabel)]
    [InlineData(TimberAnnotationMode.DimensionsWithItemNumber)]
    public void VisibleNewElementModesUseNormalProductionPlanner(
        TimberAnnotationMode mode)
    {
        var data = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            AnnotationMode = mode,
        };

        var plan = TimberAnnotationRefreshPlanner.Create(data, false);

        Assert.True(plan.EnsureLabel || plan.ReconcileSlopeArrow || plan.ReconcileSlopeAngleText);
        Assert.Contains("TimberAnnotationRefreshPlanner.Create", AnnotationService);
        Assert.Contains("ElementLabelService.UpsertForElement", AnnotationService);
    }

    [Fact]
    public void FramedPresetsRemainInsideExistingRenderer()
    {
        Assert.Contains("ItemLeaderVariantCatalog", AnnotationService);
        Assert.DoesNotContain("BlockReference", BatchAnnotations);
        Assert.DoesNotContain("MLeader", BatchAnnotations);
        Assert.DoesNotContain("Polyline", BatchAnnotations);
        Assert.DoesNotContain("RafterRoofFace", BatchAnnotations);
    }

    [Fact]
    public void EffectivePerElementSettingsAndCurrentDefaultsAreReused()
    {
        Assert.Contains("item.Value.AnnotationMode", BatchAnnotations);
        Assert.Contains("AutoCadAnnotationPresentationBatchContext.Create(", BatchAnnotations);
        Assert.Contains("defaultProfile", BatchAnnotations);
        Assert.Contains("presentationBatchContext", BatchAnnotations);
        Assert.Contains("TimberElementDefaults.For(", Replacement);
        Assert.Contains("defaultProfile) with", Replacement);
    }

    [Fact]
    public void GenerationAndAnnotationsShareOneCommitAndRollbackTogether()
    {
        var createStart = Workflow.IndexOf(
            "private static RoofRafterCreationResult TryCreateRafters",
            StringComparison.Ordinal);
        var createBody = Workflow[createStart..];
        var materialize = createBody.IndexOf(
            "RoofGeneratedRafterSetService.Materialize(",
            StringComparison.Ordinal);
        var commit = createBody.IndexOf("transaction.Commit();", StringComparison.Ordinal);

        Assert.True(materialize >= 0 && commit > materialize);
        Assert.Equal(1, Count(createBody, "transaction.Commit();"));
        Assert.Contains("TimberSourceLineCreationService.Create(", Replacement);
        Assert.Contains(
            "TimberCreatedElementAnnotationService.EnsureForCreatedElements(",
            Replacement);
        Assert.True(
            Replacement.IndexOf("TimberSourceLineCreationService.Create(", StringComparison.Ordinal) <
            Replacement.IndexOf(
                "TimberCreatedElementAnnotationService.EnsureForCreatedElements(",
                StringComparison.Ordinal));
        Assert.DoesNotContain("StartTransaction", BatchAnnotations);
        Assert.DoesNotContain("Commit", BatchAnnotations);
    }

    [Fact]
    public void ExistingSetRefusalAndCancelCannotReachAnnotationMaterialization()
    {
        var firstMaterializeCall = Workflow.IndexOf(
            "RoofGeneratedRafterSetService.Materialize(",
            StringComparison.Ordinal);
        var prefix = Workflow[..firstMaterializeCall];
        Assert.Contains("selectedRoof.ExistingGeneratedRafterCount > 0", prefix);
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner(", prefix);
        Assert.Contains("AcApp.ShowModalWindow(dialog) != true", prefix);
        Assert.Contains("return;", prefix);
        Assert.Contains("return RoofRafterCreationResult.Failure(", prefix);
    }

    [Fact]
    public void PreferencesRemainSuccessOnlyAndNoDuplicatesAreRequested()
    {
        var success = Workflow.IndexOf("if (result.IsSuccess)", StringComparison.Ordinal);
        var save = Workflow.IndexOf("SettingsUiPreferencesStore.Save", StringComparison.Ordinal);
        Assert.True(success >= 0 && save > success);
        Assert.Equal(1, Count(Workflow, "RoofGeneratedRafterSetService.Materialize("));
        Assert.Equal(1, Count(Replacement, "EnsureForCreatedElements("));
        Assert.Equal(1, Count(BatchAnnotations, "EnsureForElement("));
        Assert.DoesNotContain("DeleteDuplicates", Workflow + Replacement + BatchAnnotations);
    }

    [Fact]
    public void SlopeContractsAndStableArchitectureRemainUnchanged()
    {
        Assert.Contains("IsSlopeDirectionReversed = true", Replacement);
        Assert.Contains("new Point3d(rafter.PlanStart.X", Replacement);
        Assert.Contains("new Point3d(rafter.PlanEnd.X", Replacement);
        Assert.DoesNotContain("RoofGeneratedTimber", ArrowRenderer);
        Assert.DoesNotContain("ObjectModified", Workflow + Replacement + BatchAnnotations);
        Assert.DoesNotContain("CommandEnded", Workflow + Replacement + BatchAnnotations);
        Assert.DoesNotContain("RoofDisplayGroupService", BatchAnnotations);
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
        Assert.Equal(2, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
    }

    private static string Read(string fileName) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", fileName);

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0;
             (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0;
             index += value.Length)
        {
            count++;
        }
        return count;
    }
}
