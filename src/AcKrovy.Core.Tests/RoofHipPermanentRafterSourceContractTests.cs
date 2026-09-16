using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofHipPermanentRafterSourceContractTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void HipCreateRoutesThroughExistingGeneratedMaterializerWithoutHostGeometry()
    {
        var workflow = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofRafterCommandWorkflow.cs");
        var window = Read(
            "src", "AcKrovy.AutoCAD", "UI",
            "RoofRafterWindow.xaml.cs");
        var service = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofGeneratedRafterSetService.cs");
        var liveResize = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofLiveResizeService.cs");
        var adapter = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofFaceRafterMaterializationAdapter.cs");
        var validator = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofRafterRequestValidator.cs");
        var rules = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofRafterMaterializationRules.cs");

        Assert.Contains("not HipRoofGeometry", workflow);
        Assert.Contains("RoofGeneratedRafterSetService.Materialize", workflow);
        Assert.Contains("LockDocument()", workflow);
        Assert.Equal(1, Count(workflow, "transaction.Commit();"));
        Assert.Contains("Command_RoofRafters_ReplacementDeferred", workflow);
        Assert.Contains("RoofRafterRequestValidator.Validate", window);
        Assert.Contains("CreateButton.IsEnabled = validation.IsValid", window);
        Assert.DoesNotContain("hipPreviewOnly", window);
        Assert.DoesNotContain("RoofRafterWindow_HipPreviewOnly", window);
        Assert.Contains("RoofRafterLayoutSolver.Solve", validator);
        Assert.Contains("RoofFaceRafterMaterializationAdapter", Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofRafterLayoutSolver.cs"));
        Assert.Contains("IsConsistentHip", rules);
        Assert.Contains("faceLayout.Signature", adapter);
        Assert.DoesNotContain("LShape", adapter);
        Assert.DoesNotContain("UShape", adapter);
        Assert.DoesNotContain("TShape", adapter);
        Assert.DoesNotContain("BoundingBox", adapter + window + workflow);
        Assert.DoesNotContain("CreateHipRafterLayout", workflow + window);
        Assert.DoesNotContain("HIP_RAFTER_OWNER", workflow + service);
        Assert.DoesNotContain("HipRafterData", workflow + service + adapter);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", service);
        Assert.Contains("TimberCreatedElementAnnotationService.EnsureForCreatedElements", service);
        Assert.Contains("var isHip = classification.Geometry is HipRoofGeometry", liveResize);
        Assert.Contains("TryReplaceForSupportedResize", liveResize);
        Assert.Contains("forceRegenerateOnSourceResize: true", liveResize);
        Assert.DoesNotContain("if (!isHip)", liveResize);
        Assert.DoesNotContain("ObjectAppended", workflow);
        Assert.DoesNotContain("CommandEnded", workflow);
        Assert.DoesNotContain("ObjectModified", workflow);
    }

    [Fact]
    public void HipPermanentCreationReusesSchemaOneGeneratedMetadataWithoutSchemaBump()
    {
        var generated = Read(
            "src", "AcKrovy.Core", "Models", "Roofs",
            "RoofGeneratedTimberData.cs");
        var schema = Read(
            "src", "AcKrovy.Core", "Models", "Roofs",
            "RoofGeneratedTimberDataSchema.cs");
        var timberSchema = Read(
            "src", "AcKrovy.Core", "Models",
            "TimberElementDataSchema.cs");
        var freshness = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofGeneratedTimberFreshness.cs");
        var props = Read("Directory.Build.props");
        var roofSchema = Read(
            "src", "AcKrovy.Core", "Models", "Roofs",
            "RoofDefinitionDataSchema.cs");
        var spacingStore = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadRoofRafterSpacingStore.cs");
        var spacingPayload = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofRafterSettingsPayload.cs");

        Assert.Contains("<AcKrovyVersion>0.23.0</AcKrovyVersion>", props);
        Assert.Contains("public const int CurrentVersion = 1", schema);
        Assert.Contains("public const int CurrentVersion = 7", timberSchema);
        Assert.Contains("public const int CurrentVersion = 5", roofSchema);
        Assert.Contains("public const int SchemaVersion = 1", spacingPayload);
        Assert.Contains("RoofRafterSettingsPayload.SchemaVersion", spacingStore);
        Assert.Contains("RafterRoofFace RoofFace", generated);
        Assert.Contains("int StationIndex", generated);
        Assert.Contains("int StationCount", generated);
        Assert.Contains("string RoofOwnerReference", generated);
        Assert.Contains("ROOF_FACE_RAFTER_LAYOUT_V2", freshness);
        Assert.Contains("MatchesPersistedLayoutIdentity", freshness);
        Assert.Contains("RoofGeneratedLayoutFingerprint", File.ReadAllText(Path.Combine(
            Root,
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofGeneratedLayoutFingerprint.cs")));
        Assert.DoesNotContain("StationIntervalIndex", generated);
    }

    [Fact]
    public void TransientCleanupAndCancelRemainNoWriteBeforeCreate()
    {
        var workflow = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofRafterCommandWorkflow.cs");
        var dialogResult = workflow.IndexOf(
            "AcApp.ShowModalWindow(dialog)",
            StringComparison.Ordinal);
        var create = workflow.IndexOf(
            "TryCreateRafters(",
            dialogResult,
            StringComparison.Ordinal);
        Assert.True(dialogResult >= 0 && create > dialogResult);
        var beforeCreate = workflow[..create];
        Assert.Contains("preview.Refresh(null)", beforeCreate);
        Assert.Contains("hipPreview.Refresh(null)", beforeCreate);
        Assert.DoesNotContain("EnsureRegAppRegistered", beforeCreate);
        Assert.DoesNotContain("OpenMode.ForWrite", beforeCreate);
        Assert.DoesNotContain("AppendEntity", beforeCreate);
        Assert.Contains("if (!accepted || dialog.Request is null)", workflow);
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        for (var index = 0; (index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0; index += needle.Length)
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
