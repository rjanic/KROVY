using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class ElementLayerProfileTests
{
    [Theory]
    [InlineData(TimberElementType.Rafter, "KROKVA", 2)]
    [InlineData(TimberElementType.WallPlate, "POMURNICA", 30)]
    [InlineData(TimberElementType.Purlin, "VAZNICA", 4)]
    [InlineData(TimberElementType.Post, "STLPIK", 3)]
    [InlineData(TimberElementType.CollarTie, "KLIESTINA", 5)]
    [InlineData(TimberElementType.Brace, "VZPERA", 1)]
    [InlineData(TimberElementType.TieBeam, "VAZNY_TRAM", 6)]
    [InlineData(TimberElementType.Custom, "KROV_CUSTOM", 7)]
    public void CreateDefault_KeepsCurrentLayerNamesAndColors(
        TimberElementType type,
        string expectedLayerName,
        int expectedColorIndex)
    {
        var style = ElementLayerProfile.CreateDefault().GetStyle(type);

        Assert.Equal(expectedLayerName, style.LayerName);
        Assert.Equal(expectedColorIndex, style.ColorIndex);
    }

    [Fact]
    public void Normalize_TrimsStoredLayerName()
    {
        var profile = new ElementLayerProfile
        {
            Styles = new List<ElementLayerStyle>
            {
                new(TimberElementType.Rafter, "  KROV_KROKVA  ", 2),
            },
        };

        var normalized = profile.Normalize();

        Assert.Equal("KROV_KROKVA", normalized.GetStyle(TimberElementType.Rafter).LayerName);
    }

    [Fact]
    public void Normalize_UsesFallbackForMissingLayerName()
    {
        var profile = new ElementLayerProfile
        {
            Styles = new List<ElementLayerStyle>
            {
                new(TimberElementType.Rafter, "", 2),
            },
        };

        var normalized = profile.Normalize();

        Assert.Equal("KROKVA", normalized.GetStyle(TimberElementType.Rafter).LayerName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void Normalize_UsesFallbackForInvalidColorIndex(int invalidColorIndex)
    {
        var profile = new ElementLayerProfile
        {
            Styles = new List<ElementLayerStyle>
            {
                new(TimberElementType.Rafter, "KROV_KROKVA", invalidColorIndex),
            },
        };

        var normalized = profile.Normalize();

        Assert.Equal(2, normalized.GetStyle(TimberElementType.Rafter).ColorIndex);
    }

    [Fact]
    public void CreateDefault_RafterUsesDashDot()
    {
        var style = ElementLayerProfile.CreateDefault().GetStyle(TimberElementType.Rafter);

        Assert.Equal(CadLinetypeNames.DashDot, style.LinetypeName);
        Assert.Equal(0.5, style.LinetypeScale);
    }

    [Theory]
    [InlineData(TimberElementType.WallPlate)]
    [InlineData(TimberElementType.Purlin)]
    [InlineData(TimberElementType.Post)]
    [InlineData(TimberElementType.CollarTie)]
    [InlineData(TimberElementType.Brace)]
    [InlineData(TimberElementType.TieBeam)]
    [InlineData(TimberElementType.Custom)]
    public void CreateDefault_OtherTypesUseContinuous(TimberElementType type)
    {
        var style = ElementLayerProfile.CreateDefault().GetStyle(type);

        Assert.Equal(CadLinetypeNames.Continuous, style.LinetypeName);
        Assert.Equal(1.0, style.LinetypeScale);
    }

    [Fact]
    public void Normalize_OldProfileWithoutLinetypeUsesSafeTypeDefault()
    {
        const string legacyJson =
            """
            {
              "version": 1,
              "styles": [
                { "elementType": "Rafter", "layerName": "KROKVA", "colorIndex": 2 },
                { "elementType": "Post", "layerName": "STLPIK", "colorIndex": 3 }
              ]
            }
            """;
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        };

        var normalized = JsonSerializer
            .Deserialize<ElementLayerProfile>(legacyJson, options)!
            .Normalize();

        Assert.Equal(ElementLayerProfile.CurrentVersion, normalized.Version);
        Assert.Equal(CadLinetypeNames.DashDot, normalized.GetStyle(TimberElementType.Rafter).LinetypeName);
        Assert.Equal(CadLinetypeNames.Continuous, normalized.GetStyle(TimberElementType.Post).LinetypeName);
        Assert.Equal(0.5, normalized.GetStyle(TimberElementType.Rafter).LinetypeScale);
        Assert.Equal(1.0, normalized.GetStyle(TimberElementType.Post).LinetypeScale);
    }

    [Fact]
    public void Normalize_ProfileVersionTwoMigratesMissingScale()
    {
        const string json =
            """
            {
              "version": 2,
              "styles": [
                { "elementType": "Rafter", "layerName": "KROKVA", "colorIndex": 2, "linetypeName": "DASHDOT" },
                { "elementType": "Custom", "layerName": "KROV_CUSTOM", "colorIndex": 7, "linetypeName": "Continuous" }
              ]
            }
            """;
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        };

        var normalized = JsonSerializer.Deserialize<ElementLayerProfile>(json, options)!.Normalize();

        Assert.Equal(3, normalized.Version);
        Assert.Equal(0.5, normalized.GetStyle(TimberElementType.Rafter).LinetypeScale);
        Assert.Equal(1.0, normalized.GetStyle(TimberElementType.Custom).LinetypeScale);
    }

    [Fact]
    public void Normalize_TrimsStoredLinetypeName()
    {
        var profile = new ElementLayerProfile
        {
            Styles =
            [
                new ElementLayerStyle(TimberElementType.Rafter, "KROKVA", 2, "  CUSTOM_DASH  "),
            ],
        };

        Assert.Equal(
            "CUSTOM_DASH",
            profile.Normalize().GetStyle(TimberElementType.Rafter).LinetypeName);
    }

    [Fact]
    public void Serialize_NewProfilePersistsStableLinetypeName()
    {
        var json = JsonSerializer.Serialize(ElementLayerProfile.CreateDefault());

        Assert.Contains("\"LinetypeName\":\"DASHDOT\"", json);
        Assert.Contains("\"LinetypeScale\":0.5", json);
        Assert.DoesNotContain("ObjectId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acadiso.lin", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.009)]
    [InlineData(1000.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Normalize_InvalidLinetypeScaleUsesPerTypeDefault(double invalidScale)
    {
        var profile = new ElementLayerProfile
        {
            Version = 3,
            Styles =
            [
                new(
                    TimberElementType.Rafter,
                    "KROKVA",
                    2,
                    CadLinetypeNames.DashDot,
                    invalidScale),
                new(
                    TimberElementType.Post,
                    "STLPIK",
                    3,
                    CadLinetypeNames.Continuous,
                    invalidScale),
            ],
        }.Normalize();

        Assert.Equal(0.5, profile.GetStyle(TimberElementType.Rafter).LinetypeScale);
        Assert.Equal(1.0, profile.GetStyle(TimberElementType.Post).LinetypeScale);
    }

    [Fact]
    public void ConflictRules_AllowSharedLayerWithIdenticalAppearance()
    {
        ElementLayerStyle[] styles =
        [
            new(TimberElementType.Rafter, "SHARED", 2, CadLinetypeNames.DashDot),
            new(TimberElementType.Brace, "shared", 2, "dashdot"),
        ];

        Assert.False(ElementLayerProfileConflictRules.TryFindConflict(styles, out _));
    }

    [Theory]
    [InlineData(3, "DASHDOT")]
    [InlineData(2, "Continuous")]
    public void ConflictRules_RejectSharedLayerWithDifferentAppearance(
        int secondColor,
        string secondLinetype)
    {
        ElementLayerStyle[] styles =
        [
            new(TimberElementType.Rafter, "SHARED", 2, CadLinetypeNames.DashDot),
            new(TimberElementType.Brace, "shared", secondColor, secondLinetype),
        ];

        Assert.True(ElementLayerProfileConflictRules.TryFindConflict(styles, out var layerName));
        Assert.Equal("SHARED", layerName, ignoreCase: true);
    }

    [Fact]
    public void LinetypeResolution_ExistingDefinitionIsNotReloaded()
    {
        var loadCalls = 0;

        var result = CadLinetypeResolutionRules.Resolve(
            CadLinetypeNames.DashDot,
            name => string.Equals(name, CadLinetypeNames.DashDot, StringComparison.OrdinalIgnoreCase),
            _ =>
            {
                loadCalls++;
                return true;
            });

        Assert.False(result.UsedFallback);
        Assert.Equal(0, loadCalls);
    }

    [Fact]
    public void LinetypeResolution_MissingSupportedDefinitionIsLoadedAndRechecked()
    {
        var loaded = false;

        var result = CadLinetypeResolutionRules.Resolve(
            CadLinetypeNames.DashDot,
            name => loaded &&
                string.Equals(name, CadLinetypeNames.DashDot, StringComparison.OrdinalIgnoreCase),
            _ => loaded = true);

        Assert.False(result.UsedFallback);
        Assert.Equal(CadLinetypeNames.DashDot, result.AppliedLinetypeName);
    }

    [Fact]
    public void LinetypeResolution_LoadFailureFallsBackToContinuous()
    {
        var result = CadLinetypeResolutionRules.Resolve(
            CadLinetypeNames.DashDot,
            name => string.Equals(name, CadLinetypeNames.Continuous, StringComparison.OrdinalIgnoreCase),
            _ => false);

        Assert.True(result.UsedFallback);
        Assert.Equal(CadLinetypeNames.Continuous, result.AppliedLinetypeName);
    }

    [Fact]
    public void LayerAppearance_PreserveExistingRejectsSilentLayerUpdate()
    {
        var differs = CadLayerAppearanceRules.Differs(2, "Continuous", 2, "DASHDOT");

        Assert.True(differs);
        Assert.False(CadLayerAppearanceRules.ShouldUpdateExisting(
            CadLayerUpdateMode.PreserveExisting,
            differs));
        Assert.True(CadLayerAppearanceRules.ShouldUpdateExisting(
            CadLayerUpdateMode.UpdateExisting,
            differs));
    }

    [Fact]
    public void LayerAppearance_EqualExistingLayerNeedsNoWrite()
    {
        var differs = CadLayerAppearanceRules.Differs(2, "dashdot", 2, "DASHDOT");

        Assert.False(differs);
        Assert.False(CadLayerAppearanceRules.ShouldUpdateExisting(
            CadLayerUpdateMode.UpdateExisting,
            differs));
    }
}
