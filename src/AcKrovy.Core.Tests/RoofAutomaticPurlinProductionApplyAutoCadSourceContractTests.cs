using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticPurlinProductionApplyAutoCadSourceContractTests
{
    private static readonly string Commands = ReadAutoCad("Commands", "AcKrovyCommands.cs");
    private static readonly string Workflow = ReadAutoCad(
        "Infrastructure",
        "RoofAutomaticPurlinCommandWorkflow.cs");
    private static readonly string ApplyService = ReadAutoCad(
        "Infrastructure",
        "RoofAutomaticPurlinProductionApplyService.cs");
    private static readonly string Materializer = ReadAutoCad(
        "Infrastructure",
        "RoofAutomaticPurlinMaterializationService.cs");
    private static readonly string PersistenceRules = File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "src",
        "AcKrovy.Core",
        "Services",
        "Roofs",
        "RoofPurlinLayoutPersistenceRules.cs"));
    private static readonly string CommandCatalog = File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "src",
        "AcKrovy.Localization",
        "CommandUiCatalog.cs"));

    [Fact]
    public void ProductionCommand_IsRegisteredForCliAndRibbon()
    {
        Assert.Contains("public const string RoofPurlins = \"AK_ROOF_PURLINS\";", CommandCatalog);
        Assert.Contains("AcKrovyCommandNames.RoofPurlins, CommandFlags.Modal | CommandFlags.Redraw", Commands);
        Assert.Contains("RoofAutomaticPurlinCommandWorkflow.Run(ActiveDocument())", Commands);

        var ribbonCommands = Member(
            CommandCatalog,
            "public static IReadOnlyList<CommandUiDescriptor> RibbonCommands",
            "public static IReadOnlyList<CommandUiDescriptor> ClassicToolbarCommands");
        Assert.Contains("RoofPurlins", ribbonCommands);
    }

    [Fact]
    public void ExplicitApply_OwnsOneLockOneTransactionAndOneSuccessCommit()
    {
        Assert.Equal(1, Count(ApplyService, "document.LockDocument()"));
        Assert.Equal(1, Count(ApplyService, "StartTransaction()"));
        Assert.Equal(1, Count(ApplyService, "transaction.Commit()"));
        AssertOrdered(
            ApplyService,
            "InspectExistingOwnerStateInTransaction",
            "PrepareInTransaction",
            "MaterializePreparedInTransaction",
            "RoofPurlinLayoutStore.Write",
            "RoofRelativeElevationDatumStore.Write",
            "transaction.Commit()");
    }

    [Fact]
    public void Apply_RevalidatesMalformedDuplicateAndPreviewParityBeforeMutation()
    {
        Assert.Contains("storedLayout.Exists && storedLayout.Data is null", ApplyService);
        Assert.Contains("storedDatum.Exists && storedDatum.Data is null", ApplyService);
        Assert.Contains("RoofPurlinLayoutPersistenceRules.ValidateForWrite", ApplyService);
        Assert.Contains("RoofRelativeElevationDatumRules.Validate", ApplyService);
        Assert.Contains("InspectExistingOwnerStateInTransaction", ApplyService);
        Assert.Contains("previewPlan.Items.Count == 0 && existingState.ExistingCount == 0", ApplyService);
        Assert.Contains("expectedPreviewPlan", Materializer);
        Assert.Contains("preview-production-plan-mismatch", Materializer);
        Assert.Contains("PlansEquivalent(expectedPreviewPlan, planned.Plan)", Materializer);
        var reconcile = Member(
            Materializer,
            "private static RoofAutomaticPurlinMaterializationResult Reconcile(",
            "private static bool TryReadExisting(");
        AssertOrdered(
            reconcile,
            "ScanForOwner",
            "stale.Erase()",
            "modelSpace.AppendEntity(line)");
    }

    [Fact]
    public void UnchangedOwnerMetadata_IsNotRewritten()
    {
        Assert.Contains(
            "AreEquivalentForOwnerWrite(stored.Data, desired)",
            ApplyService);
        Assert.Contains("stored.Data == desired", ApplyService);
        Assert.Contains("layoutWrite != RoofAutomaticPurlinOwnerWriteResult.Unchanged", ApplyService);
        Assert.Contains("datumWrite != RoofAutomaticPurlinOwnerWriteResult.Unchanged", ApplyService);
        Assert.Contains("owner-metadata-postcondition-failed", ApplyService);
        Assert.Contains("AreEquivalentForOwnerWrite", ApplyService);
        Assert.Contains("ManualRafterWidthMm", PersistenceRules);
        Assert.Contains("RafterSourcePolicy", PersistenceRules);
        Assert.Contains("AcknowledgedActualKind", PersistenceRules);
    }

    [Fact]
    public void WallPlateLowerEdgeHeightChange_IsPartOfLayoutEqualityAndOwnerWrite()
    {
        Assert.Contains("WallPlateLowerEdgeHeightMm", PersistenceRules);
        Assert.Contains("AreEquivalentForOwnerWrite", PersistenceRules);
        Assert.Contains("ResolveWallPlatePlacement", PersistenceRules);
        Assert.Contains("RoofPurlinLayoutStore.Write(owner, transaction, layoutValidation.Layout)", ApplyService);
        Assert.Contains(
            "includeWallPlates: layoutValidation.Layout.WallPlateEnabled",
            ApplyService);
        Assert.Contains(
            "MaterializePreparedInTransaction(\n" +
            "                    document,\n" +
            "                    transaction,\n" +
            "                    owner,\n" +
            "                    preparation,\n" +
            "                    layoutValidation.Layout,",
            ApplyService.Replace("\r\n", "\n"));
        AssertOrdered(
            Member(
                Materializer,
                "private static RoofAutomaticPurlinMaterializationResult Reconcile(",
                "private static bool TryReadExisting("),
            "var geometryChanged =",
            "member.Existing.Line.StartPoint = member.Start",
            "member.Existing.Line.EndPoint = member.End");
    }

    [Fact]
    public void PreviewIsDisposedBeforeWriteAndDialogClosesOnlyAfterSuccess()
    {
        var handler = Member(
            Workflow,
            "void ApplyRequested(",
            "void CloseForDocumentDestruction");
        AssertOrdered(
            handler,
            "previewSession?.Dispose()",
            "RoofAutomaticPurlinProductionApplyService.Apply",
            "if (!result.IsSuccess)",
            "activeWindow.CompleteSuccessfulApply()");
        Assert.Contains("activeWindow.CompleteFailedApply()", handler);
        Assert.Contains("finally\n        {\n            previewSession?.Dispose()", Workflow.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ProductionPathHasNoAutomaticRoofLifecycleRegistration()
    {
        var combined = Workflow + ApplyService;
        foreach (var token in new[]
                 {
                     "CommandEnded",
                     "ObjectModified",
                     "ObjectAppended",
                     "ObjectErased",
                     "DeepClone",
                     "Wblock",
                 })
        {
            Assert.DoesNotContain(token, combined);
        }
    }

    [Fact]
    public void ApplyDiagnosticsExposeOwnerWritesAndReconciliationSummary()
    {
        Assert.Contains("ROOF_PURLIN_LAYOUT_WRITE", Workflow);
        Assert.Contains("ROOF_RELATIVE_DATUM_WRITE", Workflow);
        Assert.Contains("ROOF_PURLIN_APPLY", Workflow);
        foreach (var field in new[]
                 {
                     "owner=", "desired=", "actual=", "wallPlate=", "ridge=", "intermediate=",
                     "created=", "existing=", "updated=", "staleRemoved=",
                     "groupCanonical=", "result=",
                 })
        {
            Assert.Contains(field, Workflow);
        }
    }

    [Fact]
    public void ProductionApplyUsesPersistedWallPlateFlagAndNotDebugCommandCode()
    {
        Assert.Contains(
            "includeWallPlates: layoutValidation.Layout.WallPlateEnabled",
            ApplyService);
        Assert.Contains("first.WallPlateEnabled == second.WallPlateEnabled", PersistenceRules);
        Assert.Contains("WallPlatesEnabled = WallPlateEnabled", ReadAutoCad(
            "UI",
            "AutomaticPurlinDialogViewModel.cs"));
        Assert.DoesNotContain("AK_DEBUG_PURLIN_MATERIALIZE", Workflow + ApplyService);
        Assert.DoesNotContain("AutoCadAutomaticPurlinMaterializationCommands", Workflow + ApplyService);
    }

    private static void AssertOrdered(string source, params string[] values)
    {
        var previous = -1;
        foreach (var value in values)
        {
            var current = source.IndexOf(value, StringComparison.Ordinal);
            Assert.True(current > previous, value);
            previous = current;
        }
    }

    private static string Member(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, startMarker);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, endMarker);
        return source[start..end];
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string ReadAutoCad(string folder, string fileName) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            folder,
            fileName));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
