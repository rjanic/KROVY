using System.Xml.Linq;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRafterStretchRegenerationSourceContractTests
{
    private static readonly string Repository = RepositoryRoot();
    private static readonly string ResizeService = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string ReplacementService = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterSetService.cs");
    private static readonly string RafterWorkflow = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofRafterCommandWorkflow.cs");
    private static readonly string LiveGeometry = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string GeneratedCodec = Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofGeneratedTimberDataCodec.cs");
    private static readonly string RecipeRules = Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofRafterGenerationRecipeRules.cs");

    [Fact]
    public void SupportedResize_RoutesToSharedReplacementService()
    {
        Assert.Contains("RoofGeneratedRafterSetService.TryReplaceForSupportedResize(", ResizeService);
        Assert.Contains("TryRecoverRecipe(", ReplacementService);
        Assert.Contains("RoofRafterGenerationRecipeRules.TryUnify(", ReplacementService);
        Assert.Contains("EraseGeneratedSet(", ReplacementService);
        Assert.Contains("Materialize(", ReplacementService);
        Assert.Contains("RoofGeneratedRafterSetService.Materialize(", RafterWorkflow);
        Assert.DoesNotContain("AutomaticRafterPreferences", ReplacementService);
        Assert.DoesNotContain("SettingsUiPreferencesStore", ReplacementService);
    }

    [Fact]
    public void RecipeAuthority_UsesTimberAndGeneratedSpacingNotGlobalLastUsed()
    {
        Assert.Contains("timber.WidthMm", ReplacementService);
        Assert.Contains("timber.HeightMm", ReplacementService);
        Assert.Contains("timber.Material", ReplacementService);
        Assert.Contains("RequestedMaximumSpacingMm", ReplacementService);
        Assert.Contains("RequestedMaximumSpacingMm", GeneratedCodec);
        Assert.DoesNotContain("HeightMm", GeneratedCodec);
        Assert.DoesNotContain("Material", GeneratedCodec);
        Assert.Contains("public static bool TryUnify(", RecipeRules);
    }

    [Fact]
    public void Replacement_UsesExistingLayoutCreationAndAnnotationServices()
    {
        Assert.Contains("SimpleGableRafterLayoutSolver.Solve(", ReplacementService);
        Assert.Contains("TimberSourceLineCreationService.Create(", ReplacementService);
        Assert.Contains("TimberCreatedElementAnnotationService.EnsureForCreatedElements(", ReplacementService);
        Assert.Contains("IsSlopeDirectionReversed = true", ReplacementService);
        Assert.Contains("ElementLabelService.DeleteForSourceHandle(", ReplacementService);
        Assert.Contains("SlopeAnnotationService.DeleteForSourceHandle(", ReplacementService);
        Assert.Contains("entity.Erase()", ReplacementService);
        Assert.DoesNotContain("SimpleGableRafterLayoutSolver.Solve(", Segment(
            RafterWorkflow,
            "RoofGeneratedRafterSetService.Materialize(",
            "transaction.Commit();"));
    }

    [Fact]
    public void UnsupportedAndDisplayOnly_DoNotRegenerateRafters()
    {
        var unsupported = Segment(
            ResizeService,
            "if (plan.UnsupportedOwnerIds.Count > 0)",
            "IReadOnlyCollection<ObjectId> displayTamperOwners = plan.DisplayTamperOwnerIds");
        Assert.DoesNotContain("TryReplaceForSupportedResize(", unsupported);
        var display = Segment(
            ResizeService,
            "private static bool TryApplyDisplayTamper",
            "private static RoofSourceChangeClassification ClassifyOwner");
        Assert.DoesNotContain("TryReplaceForSupportedResize(", display);
        Assert.DoesNotContain("RoofGeneratedRafterSetService", display);
    }

    [Fact]
    public void HardFailure_AbortsResizeTransactionWithoutPartialCommit()
    {
        var apply = Segment(
            ResizeService,
            "private static void ApplyResizes",
            "private static ResizeApplyResult TryApplyResize");
        Assert.Contains("ResizeApplyResult.HardFailure", apply);
        Assert.Contains("return;", apply);
        Assert.Contains("Command_RoofRafters_GenerationFailed", apply);
        Assert.Contains("transaction.Commit()", apply);
    }

    [Fact]
    public void UndoRedoProtection_AndNoCommandInjectionRemain()
    {
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", ResizeService);
        Assert.DoesNotContain("SendStringToExecute", ResizeService + ReplacementService + LiveGeometry);
        Assert.DoesNotContain("Editor.Command(", ResizeService + ReplacementService);
        Assert.DoesNotContain("DatabaseReactor", ReplacementService);
        Assert.DoesNotContain("ObjectOverrule", ReplacementService);
        Assert.DoesNotContain("BeginDeepClone", ReplacementService);
        Assert.Contains("StartUndoMark", ResizeService);
        Assert.Contains("EndUndoMark", ResizeService);
    }

    [Fact]
    public void GeneratedRaftersRemainOutsideRoofGroup()
    {
        Assert.DoesNotContain("EnsureGroup(", ReplacementService);
        Assert.DoesNotContain("RoofDisplayGroupService", ReplacementService);
        Assert.Contains("ExpectedMemberCount = 8", Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayGroupService.cs"));
    }

    [Fact]
    public void NoSchemaBumpWasRequiredForRecipeRecovery()
    {
        Assert.Equal(1, AcKrovy.Core.Models.Roofs.RoofGeneratedTimberDataSchema.CurrentVersion);
        Assert.Equal(7, AcKrovy.Core.Models.TimberElementDataSchema.CurrentVersion);
        Assert.Equal(3, AcKrovy.Core.Models.Roofs.RoofDefinitionDataSchema.CurrentVersion);
        Assert.DoesNotContain("CurrentVersion = 2", Read(
            "src", "AcKrovy.Core", "Models", "Roofs", "RoofGeneratedTimberDataSchema.cs"));
    }

    [Fact]
    public void GeneratedTimberOwnerUsesRemappableSoftPointerWithoutSchemaBump()
    {
        var store = Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedTimberStore.cs");
        Assert.Contains("DxfCode.ExtendedDataHandle", store);
        Assert.Contains("cloneSafeOwnerReference", store);
        Assert.Contains(
            "data = data with { RoofOwnerReference = cloneSafeOwnerReference }",
            store);
        Assert.Contains(
            "RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(",
            ReplacementService);
        Assert.Equal(1, AcKrovy.Core.Models.Roofs.RoofGeneratedTimberDataSchema.CurrentVersion);
    }

    [Fact]
    public void AllSixLanguagePacksContainRecipeAmbiguousKey()
    {
        var resources = Path.Combine(Repository, "src", "AcKrovy.Localization", "Resources");
        var files = new[]
        {
            "UiStrings.resx", "UiStrings.cs.resx", "UiStrings.en.resx",
            "UiStrings.de.resx", "UiStrings.pl.resx", "UiStrings.fr.resx",
        };
        foreach (var file in files)
        {
            var keys = XDocument.Load(Path.Combine(resources, file))
                .Root!.Elements("data")
                .Select(element => (string?)element.Attribute("name"))
                .Where(name => name is not null)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("Command_RoofRafters_RecipeAmbiguous", keys);
        }
    }

    private static string Segment(string source, string start, string end)
    {
        source = Normalize(source);
        start = Normalize(start);
        end = Normalize(end);
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        Assert.True(endIndex > startIndex, $"End marker not found: {end}");
        return source[startIndex..endIndex];
    }

    private static string Read(params string[] path) =>
        Normalize(File.ReadAllText(Path.Combine([Repository, .. path])));

    private static string Normalize(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string RepositoryRoot()
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
