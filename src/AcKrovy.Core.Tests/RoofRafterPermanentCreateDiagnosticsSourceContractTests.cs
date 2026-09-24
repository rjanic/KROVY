using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRafterPermanentCreateDiagnosticsSourceContractTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void DebugTraceCarriesRequestedRequestLayoutCandidateAndTerminalFields()
    {
        var diagnostics = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofRafterPermanentCreateDiagnostics.cs");

        Assert.Contains("#if DEBUG", diagnostics);
        Assert.Contains("ROOF_RAFTER_CREATE_REQUEST", diagnostics);
        Assert.Contains(" owner=", diagnostics);
        Assert.Contains(" kind=", diagnostics);
        Assert.Contains(" spacing=", diagnostics);
        Assert.Contains(" width=", diagnostics);
        Assert.Contains(" height=", diagnostics);
        Assert.Contains(" material=", diagnostics);
        Assert.Contains(" slope=", diagnostics);
        Assert.Contains(" signatureFingerprint=", diagnostics);
        Assert.Contains(" fullSignatureChars=", diagnostics);
        Assert.DoesNotContain(" signature={Safe(layout.Signature)}", diagnostics);
        Assert.Contains(" count=", diagnostics);

        Assert.Contains("ROOF_RAFTER_LAYOUT_SUMMARY", diagnostics);
        Assert.Contains(" total=", diagnostics);
        Assert.Contains(" eaveRidge=", diagnostics);
        Assert.Contains(" eaveHip=", diagnostics);
        Assert.Contains(" eaveValley=", diagnostics);
        Assert.Contains(" ridgeValley=", diagnostics);
        Assert.Contains(" minPlanLength=", diagnostics);
        Assert.Contains(" maxPlanLength=", diagnostics);
        Assert.Contains(" duplicateLogicalKeys=", diagnostics);
        Assert.Contains(" duplicateGeometry=", diagnostics);
        Assert.Contains(" zeroOrNearZero=", diagnostics);
        Assert.Contains(" invalidCoordinates=", diagnostics);

        Assert.Contains("ROOF_RAFTER_RIDGE_PAIR_SUMMARY", diagnostics);
        Assert.Contains(" ridgeComponentCount=", diagnostics);
        Assert.Contains(" pairedComponentCount=", diagnostics);
        Assert.Contains(" leftEndpointCount=", diagnostics);
        Assert.Contains(" rightEndpointCount=", diagnostics);
        Assert.Contains(" matchedStationCount=", diagnostics);
        Assert.Contains(" unmatchedLeft=", diagnostics);
        Assert.Contains(" unmatchedRight=", diagnostics);
        Assert.Contains(" maxPairGapMm=", diagnostics);
        Assert.Contains(" result=", diagnostics);
        Assert.Contains("ROOF_RAFTER_RIDGE_PAIR_FAIL", diagnostics);
        Assert.Contains("WriteRidgePairSummary", diagnostics);
        Assert.Contains("ROOF_RAFTER_PHASE_COMPONENT", diagnostics);
        Assert.Contains("ROOF_RAFTER_FACE_PHASE", diagnostics);
        Assert.Contains("WritePhasePlanSummary", diagnostics);
        Assert.Contains("DescribePhasePlan", diagnostics);

        Assert.Contains("ROOF_RAFTER_MATERIALIZE_FAIL", diagnostics);
        Assert.Contains(" logicalKey=", diagnostics);
        Assert.Contains(" stationIndex=", diagnostics);
        Assert.Contains(" startBoundaryRole=", diagnostics);
        Assert.Contains(" endBoundaryRole=", diagnostics);
        Assert.Contains(" startXY=", diagnostics);
        Assert.Contains(" endXY=", diagnostics);
        Assert.Contains(" service=", diagnostics);
        Assert.Contains(" exception=", diagnostics);
        Assert.Contains("ROOF_RAFTER_CREATE_FAIL", diagnostics);
        Assert.Contains(" transaction=rollback", diagnostics);
        Assert.Contains("ROOF_RAFTER_CREATE_SUCCESS", diagnostics);
        Assert.Contains(" transaction=commit", diagnostics);
    }

    [Fact]
    public void CreateWorkflowEmitsSummaryBeforeMaterializationAndTerminalAfterCommit()
    {
        var workflow = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofRafterCommandWorkflow.cs");
        var request = workflow.IndexOf(
            "RoofRafterPermanentCreateDiag.WriteRequest(",
            StringComparison.Ordinal);
        var summary = workflow.IndexOf(
            "RoofRafterPermanentCreateDiag.WriteLayoutSummary(",
            StringComparison.Ordinal);
        var materialize = workflow.IndexOf(
            "RoofGeneratedRafterSetService.Materialize(",
            summary,
            StringComparison.Ordinal);
        var commit = workflow.IndexOf(
            "transaction.Commit();",
            materialize,
            StringComparison.Ordinal);
        var success = workflow.IndexOf(
            "RoofRafterPermanentCreateDiag.WriteSuccess(",
            commit,
            StringComparison.Ordinal);

        Assert.True(request >= 0 && summary > request && materialize > summary);
        Assert.True(commit > materialize && success > commit);
        Assert.Contains("catch (RoofRafterMaterializationPhaseException ex)", workflow);
        Assert.Contains("ex.ServicePhase", workflow);
        Assert.Contains("ex.CandidateOrdinal", workflow);
        Assert.Contains("RoofRafterPermanentCreateDiag.WriteCreateFailure(", workflow);
        Assert.Contains("RoofRafterRequestValidator.ValidateAutomaticInputs", workflow);
        Assert.Contains("RoofRafterRequestValidator.ValidateHip", workflow);
        Assert.Contains("TimberMaterialCatalog.TryGetItem", workflow);
        Assert.Contains("RoofRafterMaterializationRules.IsConsistent", workflow);
        Assert.Equal(2, Count(workflow, "transaction.Commit();"));
    }

    [Fact]
    public void MaterializerPreservesCreationOrderAndReportsEachFailureBoundary()
    {
        var materializer = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofGeneratedRafterSetService.cs");
        var lineCreation = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "TimberSourceLineCreationService.cs");
        var annotations = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "TimberCreatedElementAnnotationService.cs");

        Assert.Contains("catch (TimberSourceLineCreationPhaseException ex)", materializer);
        Assert.Contains("catch (TimberCreatedElementAnnotationPhaseException ex)", materializer);
        Assert.Contains("RoofGeneratedMemberReplayPlanner.Create", materializer);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", materializer);
        Assert.Contains("RoofRafterPermanentCreateDiag.WriteMaterializeFailure(", materializer);
        Assert.Contains("TimberAnnotationService.EnsureForElement", annotations);
        Assert.Contains("new TimberCreatedElementAnnotationPhaseException(", annotations);
        Assert.Contains("candidateOrdinal,", annotations);

        var append = lineCreation.IndexOf("modelSpace.AppendEntity(line)", StringComparison.Ordinal);
        var atomic = lineCreation.IndexOf("RoofGeneratedTimberStore.WriteAtomic(", append, StringComparison.Ordinal);
        var add = lineCreation.IndexOf("transaction.AddNewlyCreatedDBObject(line, true);", atomic, StringComparison.Ordinal);
        Assert.True(append >= 0 && atomic > append && add > atomic);
        Assert.Contains("TimberElementItemIdentityService.ComputeFinalElementIds", lineCreation);
        Assert.Contains("Autodesk.AutoCAD.DatabaseServices.Line.ctor", lineCreation);
        Assert.Contains("BlockTableRecord.AppendEntity", lineCreation);
        Assert.Contains("AutoCadTimberElementMetadataStore.Write", lineCreation);
        Assert.Contains("AutoCadTimberLayerService.ApplyLayerForTimberType", lineCreation);
        Assert.Contains("TimberElementItemIdentityService.SynchronizeElementIds", lineCreation);
        Assert.Contains("new TimberSourceLineCreationPhaseException(phase, index, ex)", lineCreation);
    }

    [Fact]
    public void DiagnosticChangeAddsNoShapeBranchSchemaOrSecondStartupGroup()
    {
        var combined = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofRafterPermanentCreateDiagnostics.cs") + Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofGeneratedRafterSetService.cs") + Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofRafterCommandWorkflow.cs");
        var props = Read("Directory.Build.props");
        var schema = Read(
            "src", "AcKrovy.Core", "Models", "Roofs",
            "RoofGeneratedTimberDataSchema.cs");

        Assert.DoesNotContain("LShape", combined);
        Assert.DoesNotContain("UShape", combined);
        Assert.DoesNotContain("TShape", combined);
        Assert.DoesNotContain("PICKSTYLE", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StartupGroup", combined);
        Assert.Contains("<AcKrovyVersion>0.23.0</AcKrovyVersion>", props);
        Assert.Contains("public const int CurrentVersion = 1", schema);
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        for (var index = 0;
             (index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0;
             index += needle.Length)
        {
            count++;
        }

        return count;
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([Root, .. path]));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
