using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source-of-truth contracts: AK_ROOF_PURLINS must consume the recovered roof
/// rafter recipe (not global TimberElementDefaults) for preview and Apply.
/// </summary>
public sealed class RoofAutomaticPurlinRafterRecipeSourceContractTests
{
    private static readonly string Resolver = Read(
        "Infrastructure",
        "RoofAutomaticPurlinRafterDimensionsResolver.cs");
    private static readonly string Workflow = Read(
        "Infrastructure",
        "RoofAutomaticPurlinCommandWorkflow.cs");
    private static readonly string Materialization = Read(
        "Infrastructure",
        "RoofAutomaticPurlinMaterializationService.cs");
    private static readonly string DebugDialog = Read(
        "Commands",
        "AutoCadAutomaticPurlinDialogCommands.cs");
    private static readonly string ViewModel = Read(
        "UI",
        "AutomaticPurlinDialogViewModel.cs");
    private static readonly string Persistence = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofPurlinLayoutPersistenceRules.cs");

    [Fact]
    public void Resolver_RecoversRoofRecipeBeforeProfileFallback()
    {
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner(", Resolver);
        Assert.Contains("RoofGeneratedRafterSetService.TryRecoverRecipe(", Resolver);
        Assert.Contains("RecoveredRoofRecipe", Resolver);
        Assert.Contains("ProfileDefaultFallback", Resolver);
        Assert.Contains("rafter-recipe-ambiguous", Resolver);
        Assert.Contains("recipe.WidthMm", Resolver);
        Assert.Contains("recipe.HeightMm", Resolver);
        var tryResolve = Member(
            Resolver,
            "public static bool TryResolve(",
            "public static bool TryResolveForOwner(");
        Assert.True(
            tryResolve.IndexOf("ordinaryRafterIds.Count == 0", StringComparison.Ordinal) <
            tryResolve.IndexOf("SourceKind.ProfileDefaultFallback", StringComparison.Ordinal));
        Assert.True(
            tryResolve.IndexOf("TryRecoverRecipe(", StringComparison.Ordinal) <
            tryResolve.IndexOf("SourceKind.RecoveredRoofRecipe", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionWorkflow_PassesResolvedRafterDefaultsIntoViewModel()
    {
        Assert.Contains("TryResolveRafterDefaults(", Workflow);
        Assert.Contains("RoofAutomaticPurlinRafterDimensionsResolver.TryResolveForOwner(", Workflow);
        Assert.Contains("AutomaticPurlin_RafterRecipeAmbiguous", Workflow);
        Assert.DoesNotContain("Command_RoofRafters_RecipeAmbiguous", Workflow);
        Assert.DoesNotContain(
            "TimberElementDefaults.For(TimberElementType.Rafter, defaultProfile),",
            Workflow);
        Assert.Contains("rafterDefaults,", Workflow);
        Assert.Contains("new AutomaticPurlinDialogViewModel(", Workflow);
    }

    [Fact]
    public void Materialization_UsesSameResolverForPlanningHeight()
    {
        Assert.Contains(
            "RoofAutomaticPurlinPlanningRafterResolver.TryResolve(",
            Materialization);
        Assert.Contains("planningRafter.HeightMm", Materialization);
        Assert.DoesNotContain(
            "TimberElementDefaults.For(TimberElementType.Rafter, defaultProfile).HeightMm",
            Materialization);
        Assert.Contains("rafter-recipe-ambiguous", Materialization + Resolver);
        Assert.Contains(
            "RoofAutomaticPurlinRafterDimensionsResolver.TryResolve(",
            Read("Infrastructure", "RoofAutomaticPurlinPlanningRafterResolver.cs"));
    }

    [Fact]
    public void PreviewAndApply_ShareResolverRatherThanIndependentDefaults()
    {
        Assert.Contains("RoofAutomaticPurlinRafterDimensionsResolver", Workflow);
        Assert.Contains("RoofAutomaticPurlinPlanningRafterResolver", Materialization);
        Assert.Contains("RoofAutomaticPurlinRafterDimensionsResolver", DebugDialog);
        Assert.Equal(
            1,
            Count(Resolver, "RoofGeneratedRafterSetService.TryRecoverRecipe("));
    }

    [Fact]
    public void ViewModel_ConsumesInjectedRafterWidthAndHeightOnly()
    {
        Assert.Contains("_rafterWidthMm = _initialRafterWidthMm", ViewModel);
        Assert.Contains("_rafterHeightMm = _initialRafterHeightMm", ViewModel);
        Assert.DoesNotContain("TimberElementDefaults.For(", ViewModel);
        Assert.DoesNotContain("TryRecoverRecipe", ViewModel);
    }

    [Fact]
    public void AmbiguousRecipe_DoesNotSilentlyPreferProfileDefault()
    {
        var resolve = Member(
            Resolver,
            "public static bool TryResolve(",
            "public static bool TryResolveForOwner(");
        Assert.Contains("if (ordinaryRafterIds.Count == 0)", resolve);
        Assert.Contains("TryRecoverRecipe(", resolve);
        Assert.Contains("AmbiguousFailure", resolve);
        Assert.DoesNotContain("ProfileDefaultFallback", SegmentAfter(resolve, "TryRecoverRecipe("));
    }

    [Fact]
    public void PersistedPurlinLayout_StoresManualProfileWithoutCadObjectIds()
    {
        Assert.Contains("ManualRafterWidthMm", Persistence);
        Assert.Contains("ManualRafterHeightMm", Persistence);
        Assert.Contains("RafterSourcePolicy", Persistence);
        Assert.DoesNotContain("ObjectId", Persistence);
        Assert.DoesNotContain("TryRecoverRecipe", Persistence);
        Assert.Contains("CreateNewDraftDefaults", Persistence);
    }

    [Fact]
    public void RecipeTwoHundredByTwoFifty_OverridesCanonicalEightyByOneSixty()
    {
        var profile = TimberElementDefaults.For(TimberElementType.Rafter);
        Assert.Equal(80d, profile.WidthMm);
        Assert.Equal(160d, profile.HeightMm);

        var recovered = profile with
        {
            WidthMm = 200d,
            HeightMm = 250d,
        };
        Assert.Equal(200d, recovered.WidthMm);
        Assert.Equal(250d, recovered.HeightMm);
        Assert.Equal(62.5d, recovered.HeightMm * 0.25d, 9);
        Assert.Equal(125d, recovered.HeightMm * 0.5d, 9);
        Assert.Equal(250d, recovered.HeightMm * 1.0d, 9);
    }

    private static string SegmentAfter(string source, string marker)
    {
        var index = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Missing marker: {marker}");
        return source[(index + marker.Length)..];
    }

    private static string Read(string folder, string fileName) =>
        RoofUxSourceContractText.Read("src", "AcKrovy.AutoCAD", folder, fileName);

    private static string Member(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);

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
