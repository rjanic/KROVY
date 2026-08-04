#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadTextSettingsProofPolicyTests
{
    [Fact]
    public void Cases_AreDeterministicAndCoverRequiredFamilies()
    {
        var cases = AutoCadTextSettingsProofPolicy.Cases;
        Assert.Equal(17, cases.Count);
        Assert.Equal(
            [
                "IP", "IC", "IR", "ITF", "IAF", "IS", "CF", "FL", "DL", "SC",
                "SA", "IU",
                AutoCadTextSettingsProofPolicy.UserFramedToken,
                AutoCadTextSettingsProofPolicy.UserFramedTwinToken,
                "HZ", "PP", AutoCadTextSettingsProofPolicy.RoleIsolationToken,
            ],
            cases.Select(proofCase => proofCase.Token));

        Assert.Contains(
            cases,
            proofCase => proofCase.Kind == AutoCadTextSettingsProofKind.ItemPlain);
        Assert.Contains(
            cases,
            proofCase => proofCase.Kind == AutoCadTextSettingsProofKind.ItemCircle);
        Assert.Contains(
            cases,
            proofCase =>
                proofCase.Kind == AutoCadTextSettingsProofKind.ItemRectangle);
        Assert.Contains(
            cases,
            proofCase => proofCase.Kind == AutoCadTextSettingsProofKind.ItemSlot);
        Assert.Contains(
            cases,
            proofCase =>
                proofCase.Kind == AutoCadTextSettingsProofKind.CombinedFramed);
        Assert.Contains(
            cases,
            proofCase => proofCase.Kind == AutoCadTextSettingsProofKind.FullLabel);
        Assert.Contains(
            cases,
            proofCase =>
                proofCase.Kind == AutoCadTextSettingsProofKind.DimensionsLeader);
        Assert.Equal(
            2,
            cases.Count(proofCase =>
                proofCase.Kind == AutoCadTextSettingsProofKind.SlopeNumeric));
        Assert.Contains(
            cases,
            proofCase =>
                proofCase.Kind == AutoCadTextSettingsProofKind.ItemUserPreset);
        Assert.Contains(
            cases,
            proofCase =>
                proofCase.Kind == AutoCadTextSettingsProofKind.HorizontalMarker);
        Assert.Contains(
            cases,
            proofCase =>
                proofCase.Kind ==
                AutoCadTextSettingsProofKind.PostPerpendicular);
        Assert.Contains(cases, proofCase => proofCase.IsRoleIsolation);
    }

    [Fact]
    public void Cases_IncludeClassicArchScalesAndPerRoleCombinedSettings()
    {
        var classic = Assert.Single(
            AutoCadTextSettingsProofPolicy.Cases,
            proofCase => proofCase.Token == "IP");
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ClassicStyleName,
            classic.TextSettings.ItemCodeTextStyleName);
        Assert.Equal(50, classic.Denominator);
        Assert.Equal(
            TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
            classic.TextSettings.ItemCodePaperHeightMm);

        var archRect = Assert.Single(
            AutoCadTextSettingsProofPolicy.Cases,
            proofCase => proofCase.Token == "IR");
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
            archRect.TextSettings.ItemCodeTextStyleName);

        var slot = Assert.Single(
            AutoCadTextSettingsProofPolicy.Cases,
            proofCase => proofCase.Token == "IS");
        Assert.Equal(100, slot.Denominator);

        var combined = Assert.Single(
            AutoCadTextSettingsProofPolicy.Cases,
            proofCase => proofCase.Token == "CF");
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ClassicStyleName,
            combined.TextSettings.ItemCodeTextStyleName);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
            combined.TextSettings.DimensionTextStyleName);
        Assert.False(combined.TextSettings.HasSharedTextStyleName);

        var slopeArch = Assert.Single(
            AutoCadTextSettingsProofPolicy.Cases,
            proofCase => proofCase.Token == "SA");
        Assert.Equal(100, slopeArch.Denominator);
        Assert.Equal(
            AutoCadTextSettingsProofPolicy.SlopeArchTallerHeightMm,
            slopeArch.TextSettings.SlopePaperHeightMm);
    }

    [Fact]
    public void UserPreset_BuildsAppOwnedStyleName()
    {
        var preset = AutoCadTextSettingsProofPolicy.CreateUserPreset("Calibri");
        Assert.Equal(
            AutoCadTextSettingsProofPolicy.UserPresetStableId,
            preset.StableId);
        Assert.Equal(
            AutoCadTextSettingsProofPolicy.UserPresetDisplayName,
            preset.DisplayName);
        Assert.StartsWith(
            TimberAnnotationTextStylePresetRules.UserStyleNamePrefix,
            preset.AutoCadTextStyleName,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Calibri", preset.FontFile);

        var identity = AutoCadItemLeaderTextStyleIdentity.FromStoredStyleName(
            preset.AutoCadTextStyleName);
        Assert.Equal(AutoCadItemLeaderTextStyleIdentityKind.User, identity.Kind);
        Assert.StartsWith(
            "USER_",
            identity.CreateNameToken(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void FramedCases_CoverAllBuiltInAndUserG3Identities()
    {
        var styleNames = AutoCadTextSettingsProofPolicy.Cases
            .Where(proofCase =>
                !proofCase.UsesUserPreset &&
                (proofCase.Kind is AutoCadTextSettingsProofKind.ItemCircle or
                    AutoCadTextSettingsProofKind.ItemRectangle or
                    AutoCadTextSettingsProofKind.ItemSlot))
            .Select(proofCase => proofCase.TextSettings.ItemCodeTextStyleName)
            .Append(
                AutoCadTextSettingsProofPolicy.CreateUserPreset("Calibri")
                    .AutoCadTextStyleName);

        Assert.Equal(
            [
                "ARCHITECTURAL",
                "CLASSIC",
                "TECHNICAL",
                "ARIAL",
                "USER_G3_HOST_USER",
            ],
            styleNames
                .Select(AutoCadItemLeaderTextStyleIdentity.FromStoredStyleName)
                .Select(identity => identity.CreateNameToken())
                .Distinct()
                .OrderBy(token => Array.IndexOf(
                    [
                        "ARCHITECTURAL",
                        "CLASSIC",
                        "TECHNICAL",
                        "ARIAL",
                        "USER_G3_HOST_USER",
                    ],
                    token)));
    }

    [Fact]
    public void UserFramedCases_AreRectangleSmallWithSharedUserPreset()
    {
        var framed = AutoCadTextSettingsProofPolicy.Cases
            .Where(proofCase =>
                AutoCadTextSettingsProofPolicy.IsUserFramedToken(proofCase.Token))
            .ToArray();
        Assert.Equal(2, framed.Length);
        Assert.All(framed, proofCase =>
        {
            Assert.Equal(AutoCadTextSettingsProofKind.ItemRectangle, proofCase.Kind);
            Assert.Equal(ItemNumberLeaderStyle.Rectangle, proofCase.ItemStyle);
            Assert.True(proofCase.UsesUserPreset);
            Assert.Equal(50, proofCase.Denominator);
        });

        var resolved = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Rectangle,
            "K1");
        Assert.Equal(TimberItemLeaderBlockSize.Small, resolved.Size);
        var preset = AutoCadTextSettingsProofPolicy.CreateUserPreset("Calibri");
        var expectedBlockName =
            AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(
                AutoCadItemLeaderBlockVariantKey.FromDefinition(
                    resolved,
                    AutoCadItemLeaderTextStyleIdentity.FromStoredStyleName(
                        preset.AutoCadTextStyleName)));
        Assert.Contains(
            $"_G{AutoCadItemLeaderBlockVariantKey.CurrentGeometryVersion}_",
            expectedBlockName,
            StringComparison.Ordinal);
        Assert.Contains("USER_", expectedBlockName, StringComparison.Ordinal);
        Assert.Contains("RECT_S", expectedBlockName, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CLASSIC",
            expectedBlockName,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "_ARCH",
            expectedBlockName,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolvePreferredUserFont_SkipsBuiltInClassicAndArchitecturalFonts()
    {
        var chosen = AutoCadTextSettingsProofPolicy.ResolvePreferredUserFont(
            font => string.Equals(font, "Calibri", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(font, "Consolas", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Calibri", chosen);

        chosen = AutoCadTextSettingsProofPolicy.ResolvePreferredUserFont(
            font => string.Equals(font, "Arial", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    font,
                    "Times New Roman",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(font, "Consolas", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Consolas", chosen);
    }

    [Theory]
    [InlineData(2.7d, 50, 135d, 1d)]
    [InlineData(2.7d, 100, 270d, 2d)]
    public void FramedAttributeHeight_UsesPaperTimesDenominator(
        double paperHeightMm,
        int denominator,
        double expectedAttributeHeightMm,
        double expectedBlockScale)
    {
        Assert.Equal(
            TimberItemLeaderBlockDefinitionRules.BaseFramedItemTextHeightAtScale50Mm,
            AutoCadTextSettingsProofPolicy.ExpectedFramedDefinitionHeightMm);
        Assert.Equal(135d, AutoCadTextSettingsProofPolicy.ExpectedFramedDefinitionHeightMm);
        Assert.Equal(
            expectedAttributeHeightMm,
            AutoCadTextSettingsProofPolicy.ExpectedFramedAttributeHeightMm(
                paperHeightMm,
                denominator));
        Assert.Equal(
            expectedBlockScale,
            AutoCadTextSettingsProofPolicy.ExpectedBlockScale(denominator));
    }

    [Fact]
    public void CaseIs_ExpectsDenom100ModelAttributeHeightAndSharedG2Definition()
    {
        var slot = Assert.Single(
            AutoCadTextSettingsProofPolicy.Cases,
            proofCase => proofCase.Token == "IS");
        Assert.Equal(ItemNumberLeaderStyle.Slot, slot.ItemStyle);
        Assert.Equal(100, slot.Denominator);
        Assert.Equal(
            270d,
            AutoCadTextSettingsProofPolicy.ExpectedFramedAttributeHeightMm(
                slot.TextSettings.ItemCodePaperHeightMm,
                slot.Denominator));
        Assert.Equal(
            2d,
            AutoCadTextSettingsProofPolicy.ExpectedBlockScale(slot.Denominator));

        var resolved = TimberItemLeaderBlockDefinitionRules.Resolve(
            slot.ItemStyle,
            "K1");
        var expectedBlockName =
            AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(
                AutoCadItemLeaderBlockVariantKey.FromDefinition(resolved));
        Assert.EndsWith(
            $"_G{AutoCadItemLeaderBlockVariantKey.CurrentGeometryVersion}_CLASSIC",
            expectedBlockName,
            StringComparison.Ordinal);
        Assert.DoesNotContain("100", expectedBlockName, StringComparison.Ordinal);
        Assert.DoesNotContain("50", expectedBlockName, StringComparison.Ordinal);
    }

    [Fact]
    public void RoleIsolationPatch_ChangesDimensionAndSlopeLeavesItem()
    {
        var baseline = TimberAnnotationTextSettings.Shared(
            TimberAnnotationTextStylePresetRules.ClassicStyleName,
            TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
            TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
            TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm);
        var patched =
            AutoCadTextSettingsProofPolicy.CreateRoleIsolationPatchedSettings(
                baseline);

        Assert.Equal(
            baseline.ItemCodeTextStyleName,
            patched.ItemCodeTextStyleName);
        Assert.Equal(
            baseline.ItemCodePaperHeightMm,
            patched.ItemCodePaperHeightMm);
        Assert.Equal(
            TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
            patched.ItemCodePaperHeightMm);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
            patched.DimensionTextStyleName);
        Assert.Equal(
            AutoCadTextSettingsProofPolicy.RoleIsolationPatchedDimensionHeightMm,
            patched.DimensionPaperHeightMm);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
            patched.SlopeTextStyleName);
        Assert.Equal(
            AutoCadTextSettingsProofPolicy.RoleIsolationPatchedSlopeHeightMm,
            patched.SlopePaperHeightMm);
        Assert.Equal(
            135d,
            AutoCadTextSettingsProofPolicy.ExpectedFramedAttributeHeightMm(
                patched.ItemCodePaperHeightMm,
                50));
    }
}
#endif
