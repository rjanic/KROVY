using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationTextStylePresetRulesTests
{
    [Fact]
    public void GetBuiltInDefinitions_ExposesExactOrderedBuiltInsNamesAndFonts()
    {
        var definitions = TimberAnnotationTextStylePresetRules.GetBuiltInDefinitions();

        Assert.Equal(
            [
                TimberAnnotationBuiltInTextStylePreset.Architectural,
                TimberAnnotationBuiltInTextStylePreset.Classic,
                TimberAnnotationBuiltInTextStylePreset.Technical,
                TimberAnnotationBuiltInTextStylePreset.Arial,
            ],
            definitions.Select(definition => definition.BuiltInPreset));

        var classic = TimberAnnotationTextStylePresetRules.GetBuiltIn(
            TimberAnnotationBuiltInTextStylePreset.Classic);
        Assert.Equal(TimberAnnotationTextStylePresetRules.ClassicStableId, classic.StableId);
        Assert.Equal(TimberAnnotationTextStylePresetKind.BuiltIn, classic.Kind);
        Assert.Equal(TimberAnnotationBuiltInTextStylePreset.Classic, classic.BuiltInPreset);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ClassicLocalizationKey,
            classic.LocalizationKey);
        Assert.Null(classic.DisplayName);
        Assert.Equal(TimberAnnotationTextStylePresetRules.ClassicStyleName, classic.AutoCadTextStyleName);
        Assert.Equal(TimberAnnotationTextStylePresetRules.ClassicFontFile, classic.FontFile);
        Assert.Equal(TimberAnnotationTextStylePresetRules.DefaultWidthFactor, classic.WidthFactor);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.DefaultObliqueAngleDegrees,
            classic.ObliqueAngleDegrees);

        var architectural = TimberAnnotationTextStylePresetRules.GetBuiltIn(
            TimberAnnotationBuiltInTextStylePreset.Architectural);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ArchitecturalStableId,
            architectural.StableId);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ArchitecturalLocalizationKey,
            architectural.LocalizationKey);
        Assert.Null(architectural.DisplayName);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
            architectural.AutoCadTextStyleName);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ArchitecturalFontFile,
            architectural.FontFile);

        var technical = TimberAnnotationTextStylePresetRules.GetBuiltIn(
            TimberAnnotationBuiltInTextStylePreset.Technical);
        Assert.Equal("AK_KROVY_TECHNICAL", technical.AutoCadTextStyleName);
        Assert.Equal("isocp.shx", technical.FontFile);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.DefaultWidthFactor,
            technical.WidthFactor);
        Assert.Equal(1.0d, technical.WidthFactor);

        var arial = TimberAnnotationTextStylePresetRules.GetBuiltIn(
            TimberAnnotationBuiltInTextStylePreset.Arial);
        Assert.Equal("AK_KROVY_ARIAL", arial.AutoCadTextStyleName);
        Assert.Equal("Arial", arial.FontFile);

        Assert.Equal("AK_KROVY_ARCHITECTURAL", architectural.AutoCadTextStyleName);
        Assert.Equal("Arial Narrow", architectural.FontFile);
        Assert.Equal("romans.shx", classic.FontFile);
    }

    [Theory]
    [InlineData("AK_KROVY_CLASSIC", TimberAnnotationBuiltInTextStylePreset.Classic)]
    [InlineData("ak_krovy_architectural", TimberAnnotationBuiltInTextStylePreset.Architectural)]
    [InlineData("AK_KROVY_TECHNICAL", TimberAnnotationBuiltInTextStylePreset.Technical)]
    [InlineData("ak_krovy_arial", TimberAnnotationBuiltInTextStylePreset.Arial)]
    public void TryResolveBuiltInByStyleName_ResolvesCaseInsensitively(
        string styleName,
        TimberAnnotationBuiltInTextStylePreset expected)
    {
        Assert.True(
            TimberAnnotationTextStylePresetRules.TryResolveBuiltInByStyleName(
                styleName,
                out var definition));
        Assert.Equal(expected, definition!.BuiltInPreset);
    }

    [Theory]
    [InlineData("classic", TimberAnnotationBuiltInTextStylePreset.Classic)]
    [InlineData("ARCHITECTURAL", TimberAnnotationBuiltInTextStylePreset.Architectural)]
    [InlineData("technical", TimberAnnotationBuiltInTextStylePreset.Technical)]
    [InlineData("ARIAL", TimberAnnotationBuiltInTextStylePreset.Arial)]
    public void TryResolveBuiltInByStableId_ResolvesCaseInsensitively(
        string stableId,
        TimberAnnotationBuiltInTextStylePreset expected)
    {
        Assert.True(
            TimberAnnotationTextStylePresetRules.TryResolveBuiltInByStableId(
                stableId,
                out var definition));
        Assert.Equal(expected, definition!.BuiltInPreset);
    }

    [Fact]
    public void CreateFreshProfileTextSettings_UsesClassicStyleAndDefaultHeights()
    {
        var settings = TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings();

        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ClassicStyleName,
            settings.ItemCodeTextStyleName);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ClassicStyleName,
            settings.DimensionTextStyleName);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ClassicStyleName,
            settings.SlopeTextStyleName);
        Assert.Equal(
            TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
            settings.ItemCodePaperHeightMm);
        Assert.Equal(
            TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
            settings.DimensionPaperHeightMm);
        Assert.Equal(
            TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm,
            settings.SlopePaperHeightMm);
        Assert.True(settings.HasSharedTextStyleName);
        Assert.NotEqual(TimberAnnotationTextSettingsRules.Default, settings);
        Assert.Equal("Standard", TimberAnnotationTextSettingsRules.Default.ItemCodeTextStyleName);
    }

    [Fact]
    public void IsAppOwnedStyleName_RecognizesBuiltInsAndUserPrefix()
    {
        Assert.True(TimberAnnotationTextStylePresetRules.IsBuiltInStyleName("AK_KROVY_CLASSIC"));
        Assert.True(
            TimberAnnotationTextStylePresetRules.IsAppOwnedStyleName(
                "AK_KROVY_ARCHITECTURAL"));
        Assert.True(
            TimberAnnotationTextStylePresetRules.IsAppOwnedStyleName(
                "AK_KROVY_TECHNICAL"));
        Assert.True(
            TimberAnnotationTextStylePresetRules.IsAppOwnedStyleName(
                "AK_KROVY_ARIAL"));
        Assert.False(TimberAnnotationTextStylePresetRules.IsAppOwnedStyleName("AK_KROVY_ARCH"));
        Assert.True(
            TimberAnnotationTextStylePresetRules.IsAppOwnedStyleName("AK_KROVY_USER_ABC123"));
        Assert.False(TimberAnnotationTextStylePresetRules.IsAppOwnedStyleName("Standard"));
        Assert.False(TimberAnnotationTextStylePresetRules.IsAppOwnedStyleName("ISOCP"));
    }

    [Fact]
    public void GenerateUserAutoCadTextStyleName_SanitizesToUppercaseAlphanumericAndUnderscore()
    {
        var styleName = TimberAnnotationTextStylePresetRules.GenerateUserAutoCadTextStyleName(
            "a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        Assert.Equal(
            "AK_KROVY_USER_A1B2C3D4_E5F6_7890_ABCD_EF1234567890",
            styleName);
        Assert.Equal(
            styleName,
            TimberAnnotationTextStylePresetRules.BuildAutoCadTextStyleName(
                "a1b2c3d4-e5f6-7890-abcd-ef1234567890"));
    }

    [Fact]
    public void ValidateAndNormalizeUserPreset_TrimsFieldsAndBuildsDeterministicStyleName()
    {
        var stableId = "decorair-01";
        var preset = new TimberAnnotationUserTextStylePreset
        {
            StableId = $"  {stableId}  ",
            DisplayName = "  Decorair technický  ",
            FontFile = "  Arial Narrow  ",
            AutoCadTextStyleName = "IGNORED_NAME",
            WidthFactor = 0.8d,
            ObliqueAngleDegrees = 12d,
        };

        var normalized = TimberAnnotationTextStylePresetRules.ValidateAndNormalizeUserPreset(preset);

        Assert.Equal(stableId, normalized.StableId);
        Assert.Equal("Decorair technický", normalized.DisplayName);
        Assert.Equal("Arial Narrow", normalized.FontFile);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.BuildAutoCadTextStyleName(stableId),
            normalized.AutoCadTextStyleName);
        Assert.Equal(0.8d, normalized.WidthFactor);
        Assert.Equal(12d, normalized.ObliqueAngleDegrees);
    }

    [Fact]
    public void DisplayNameRename_DoesNotChangeAutoCadTextStyleNameWhenStableIdUnchanged()
    {
        var stableId = "project-font-a";
        var original = TimberAnnotationTextStylePresetRules.ValidateAndNormalizeUserPreset(
            new TimberAnnotationUserTextStylePreset
            {
                StableId = stableId,
                DisplayName = "Projektant Arial Narrow",
                FontFile = "Arial Narrow",
                WidthFactor = 1d,
                ObliqueAngleDegrees = 0d,
            });

        var renamed = TimberAnnotationTextStylePresetRules.ValidateAndNormalizeUserPreset(
            new TimberAnnotationUserTextStylePreset
            {
                StableId = stableId,
                DisplayName = "Firemný popis",
                FontFile = original.FontFile,
                AutoCadTextStyleName = "SHOULD_BE_REBUILT_FROM_STABLE_ID",
                WidthFactor = original.WidthFactor,
                ObliqueAngleDegrees = original.ObliqueAngleDegrees,
            });

        Assert.Equal(original.AutoCadTextStyleName, renamed.AutoCadTextStyleName);
        Assert.Equal("Firemný popis", renamed.DisplayName);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.BuildAutoCadTextStyleName(stableId),
            renamed.AutoCadTextStyleName);
    }

    [Fact]
    public void EnsureUniqueDisplayName_IsCaseInsensitiveAmongUserPresets()
    {
        var existing = new[]
        {
            new TimberAnnotationUserTextStylePreset
            {
                StableId = "one",
                DisplayName = "Decorair",
                FontFile = "Arial",
            },
        };

        Assert.Equal(
            "decorair (2)",
            TimberAnnotationTextStylePresetRules.EnsureUniqueDisplayName(
                "decorair",
                existing));
        Assert.Equal(
            "Other",
            TimberAnnotationTextStylePresetRules.EnsureUniqueDisplayName(
                "Other",
                existing));
    }

    [Fact]
    public void ValidateAndNormalizeUserPreset_RejectsReservedBuiltInStableIds()
    {
        Assert.Throws<ArgumentException>(() =>
            TimberAnnotationTextStylePresetRules.ValidateAndNormalizeUserPreset(
                new TimberAnnotationUserTextStylePreset
                {
                    StableId = "classic",
                    DisplayName = "Mine",
                    FontFile = "Arial",
                }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Name\nLine")]
    public void IsValidDisplayName_RejectsEmptyWhitespaceAndControlCharacters(string name) =>
        Assert.False(TimberAnnotationTextStylePresetRules.IsValidDisplayName(name));

    [Theory]
    [InlineData(0.099d)]
    [InlineData(10.001d)]
    [InlineData(double.NaN)]
    public void IsValidWidthFactor_RejectsOutOfRange(double value) =>
        Assert.False(TimberAnnotationTextStylePresetRules.IsValidWidthFactor(value));

    [Theory]
    [InlineData(-85.001d)]
    [InlineData(85.001d)]
    [InlineData(double.PositiveInfinity)]
    public void IsValidObliqueAngle_RejectsOutOfRange(double value) =>
        Assert.False(TimberAnnotationTextStylePresetRules.IsValidObliqueAngle(value));

    [Fact]
    public void NormalizeLibrary_DropsInvalidPresetsDedupesByStableIdAndRebuildsStyleNames()
    {
        var library = new TimberAnnotationTextStylePresetLibrary
        {
            Version = 0,
            Presets =
            [
                new TimberAnnotationUserTextStylePreset
                {
                    StableId = "dup",
                    DisplayName = "First",
                    FontFile = "Arial",
                    AutoCadTextStyleName = "OLD_NAME",
                    WidthFactor = 1d,
                    ObliqueAngleDegrees = 0d,
                },
                new TimberAnnotationUserTextStylePreset
                {
                    StableId = "dup",
                    DisplayName = "Second",
                    FontFile = "Times New Roman",
                    AutoCadTextStyleName = "OTHER",
                    WidthFactor = 1.2d,
                    ObliqueAngleDegrees = 5d,
                },
                new TimberAnnotationUserTextStylePreset
                {
                    StableId = "bad",
                    DisplayName = "",
                    FontFile = "Arial",
                },
                new TimberAnnotationUserTextStylePreset
                {
                    StableId = "keep-me",
                    DisplayName = "Keep",
                    FontFile = "Consolas",
                    WidthFactor = 1d,
                    ObliqueAngleDegrees = 0d,
                },
            ],
        };

        var normalized = library.Normalize();

        Assert.Equal(TimberAnnotationTextStylePresetLibrary.CurrentVersion, normalized.Version);
        Assert.Equal(2, normalized.Presets.Count);

        var duplicate = Assert.Single(
            normalized.Presets,
            preset => preset.StableId == "dup");
        Assert.Equal("Second", duplicate.DisplayName);
        Assert.Equal("Times New Roman", duplicate.FontFile);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.BuildAutoCadTextStyleName("dup"),
            duplicate.AutoCadTextStyleName);

        var kept = Assert.Single(
            normalized.Presets,
            preset => preset.StableId == "keep-me");
        Assert.Equal("Keep", kept.DisplayName);
    }

    [Fact]
    public void NormalizeLibrary_EnsuresUniqueDisplayNamesCaseInsensitively()
    {
        var library = new TimberAnnotationTextStylePresetLibrary
        {
            Presets =
            [
                new TimberAnnotationUserTextStylePreset
                {
                    StableId = "a",
                    DisplayName = "Same",
                    FontFile = "Arial",
                    WidthFactor = 1d,
                    ObliqueAngleDegrees = 0d,
                },
                new TimberAnnotationUserTextStylePreset
                {
                    StableId = "b",
                    DisplayName = "same",
                    FontFile = "Arial",
                    WidthFactor = 1d,
                    ObliqueAngleDegrees = 0d,
                },
            ],
        };

        var normalized = library.Normalize();
        var names = normalized.Presets.Select(preset => preset.DisplayName).ToList();

        Assert.Contains("Same", names);
        Assert.Contains("same (2)", names);
    }

    [Fact]
    public void CreateDefaultLibrary_IsEmptyVersionOne()
    {
        var library = TimberAnnotationTextStylePresetLibrary.CreateDefault();

        Assert.Equal(1, library.Version);
        Assert.Empty(library.Presets);
    }

    [Fact]
    public void PrepareForWrite_UpgradesStoredVersionWhileNormalizeKeepsIt()
    {
        var library = new TimberAnnotationTextStylePresetLibrary
        {
            Version = 1,
            Presets =
            [
                new TimberAnnotationUserTextStylePreset
                {
                    StableId = "x",
                    DisplayName = "X",
                    FontFile = "Arial",
                    WidthFactor = 1d,
                    ObliqueAngleDegrees = 0d,
                },
            ],
        };

        Assert.Equal(1, library.Normalize().Version);
        Assert.Equal(
            TimberAnnotationTextStylePresetLibrary.CurrentVersion,
            library.PrepareForWrite().Version);
    }

    [Fact]
    public void ToDefinition_MapsUserPresetWithoutLocalizationKey()
    {
        var definition = TimberAnnotationTextStylePresetRules.ToDefinition(
            new TimberAnnotationUserTextStylePreset
            {
                StableId = "user-1",
                DisplayName = "Custom",
                FontFile = "Arial",
                WidthFactor = 1d,
                ObliqueAngleDegrees = 0d,
            });

        Assert.Equal(TimberAnnotationTextStylePresetKind.User, definition.Kind);
        Assert.Null(definition.BuiltInPreset);
        Assert.Null(definition.LocalizationKey);
        Assert.Equal("Custom", definition.DisplayName);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.BuildAutoCadTextStyleName("user-1"),
            definition.AutoCadTextStyleName);
    }
}
