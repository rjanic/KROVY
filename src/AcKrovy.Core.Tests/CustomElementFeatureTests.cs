using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class CustomElementFeatureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void CreateDefinition_GeneratesStableTechnicalIdAndNormalizesPrefix()
    {
        var definition = CustomElementDefinitionRules.Create(" Konzola ", " ko ");

        Assert.Equal("Konzola", definition.Name);
        Assert.Equal("KO", definition.Prefix);
        Assert.Equal(32, definition.Id.Length);
        Assert.Equal(definition.Id, (definition with { Name = "Konzola 2" }).Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("K-")]
    [InlineData("K 1")]
    [InlineData("123")]
    [InlineData("ABCDEFGHI")]
    [InlineData("K")]
    public void Definition_RejectsInvalidPrefix(string prefix)
    {
        Assert.Throws<ArgumentException>(() =>
            CustomElementDefinitionRules.Create("Konzola", prefix));
    }

    [Fact]
    public void Catalog_RejectsCaseInsensitiveDuplicatePrefix()
    {
        var first = new CustomElementDefinition("a", "Konzola", "KO");
        var second = new CustomElementDefinition("b", "Prievlak", "ko");

        Assert.Throws<ArgumentException>(() =>
            CustomElementDefinitionCatalogRules.Normalize([first, second]));
    }

    [Fact]
    public void SchemaVersionThree_RoundTripsSelfContainedCustomDefinition()
    {
        var source = CustomData("KO1", "definition-a", "Konzola", "KO");

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var loaded = Assert.IsType<TimberElementData>(
            JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions));

        Assert.Equal(3, loaded.SchemaVersion);
        Assert.Equal(TimberElementType.Custom, loaded.ElementType);
        Assert.Equal("definition-a", loaded.CustomElementTypeId);
        Assert.Equal("Konzola", loaded.CustomElementTypeName);
        Assert.Equal("KO", loaded.CustomElementTypePrefix);
        Assert.True(CustomElementDefinitionRules.TryFromElementData(loaded, out _));
    }

    [Fact]
    public void LegacyBuiltInPayload_RemainsReadableWithoutCustomFields()
    {
        const string json =
            """{"schemaVersion":2,"elementId":"K1","elementType":"Rafter","widthMm":80,"heightMm":160,"material":"Smrek C24"}""";

        var loaded = Assert.IsType<TimberElementData>(
            JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions));

        Assert.Equal(TimberElementType.Rafter, loaded.ElementType);
        Assert.Null(loaded.CustomElementTypeId);
        Assert.True(TimberElementDataVersioning.IsSupported(loaded));
    }

    [Fact]
    public void IncompleteCustomMetadata_IsRejectedByDefinitionRules()
    {
        var data = TimberElementDefaults.For(TimberElementType.Custom);

        Assert.False(CustomElementDefinitionRules.TryFromElementData(data, out _));
    }

    [Fact]
    public void Signature_UsesStableCustomIdAndNotDisplayNameOrPrefix()
    {
        var first = Measurement(CustomData("KO1", "same-id", "Konzola", "KO"), 3000);
        var renamed = Measurement(CustomData("ZZ1", "same-id", "Bracket", "ZZ"), 3000);
        var other = Measurement(CustomData("PR1", "other-id", "Konzola", "PR"), 3000);

        Assert.Equal(
            TimberElementSignature.FromMeasurement(first),
            TimberElementSignature.FromMeasurement(renamed));
        Assert.NotEqual(
            TimberElementSignature.FromMeasurement(first),
            TimberElementSignature.FromMeasurement(other));
    }

    [Fact]
    public void RenameDefinition_PreservesTechnicalIdentityPrefixAndElementId()
    {
        var originalDefinition =
            new CustomElementDefinition("definition-a", "Prievlak", "PR");
        var original = CustomData("PR2", "definition-a", "Prievlak", "PR");

        var renamedDefinition = CustomElementDefinitionRenameRules.Rename(
            originalDefinition,
            " Hlavný prievlak ");
        var renamed = CustomElementDefinitionRenameRules.Apply(
            original,
            renamedDefinition);

        Assert.Equal("definition-a", renamedDefinition.Id);
        Assert.Equal("PR", renamedDefinition.Prefix);
        Assert.Equal("Hlavný prievlak", renamedDefinition.Name);
        Assert.Equal(original.ElementId, renamed.ElementId);
        Assert.Equal(original.CustomElementTypeId, renamed.CustomElementTypeId);
        Assert.Equal(original.CustomElementTypePrefix, renamed.CustomElementTypePrefix);
        Assert.Equal(
            TimberElementSignature.FromMeasurement(Measurement(original, 3000)),
            TimberElementSignature.FromMeasurement(Measurement(renamed, 3000)));
    }

    [Fact]
    public void RenameDefinition_UpdatesEveryMatchingEntityAndLeavesOtherDefinitionUnchanged()
    {
        var elements = new[]
        {
            CustomData("PR1", "definition-a", "Prievlak", "PR"),
            CustomData("PR2", "definition-a", "Prievlak", "PR"),
            CustomData("KO1", "definition-b", "Konzola", "KO"),
        };
        var renamedDefinition =
            new CustomElementDefinition("definition-a", "Hlavný prievlak", "PR");

        var renamed = elements
            .Select(data => CustomElementDefinitionRenameRules.Apply(
                data,
                renamedDefinition))
            .ToList();

        Assert.All(
            renamed.Where(data => data.CustomElementTypeId == "definition-a"),
            data => Assert.Equal("Hlavný prievlak", data.CustomElementTypeName));
        Assert.Equal(
            "Konzola",
            renamed.Single(data => data.CustomElementTypeId == "definition-b")
                .CustomElementTypeName);
    }

    [Fact]
    public void RenameDefinition_RejectsBlankName()
    {
        var definition =
            new CustomElementDefinition("definition-a", "Prievlak", "PR");

        Assert.Throws<ArgumentException>(() =>
            CustomElementDefinitionRenameRules.Rename(definition, "   "));
    }

    [Fact]
    public void RenameDefinition_SameNormalizedNameIsNoOp()
    {
        var definition =
            new CustomElementDefinition("definition-a", "Prievlak", "PR");
        var data = CustomData("PR1", "definition-a", "Prievlak", "PR");
        var sameDefinition = CustomElementDefinitionRenameRules.Rename(
            definition,
            " Prievlak ");

        Assert.False(CustomElementDefinitionRenameRules.HasChanged(
            definition,
            sameDefinition));
        Assert.Same(
            data,
            CustomElementDefinitionRenameRules.Apply(data, sameDefinition));
    }

    [Fact]
    public void CatalogRename_UpdatesMatchingDefinitionWithoutChangingOtherDefinitions()
    {
        var catalog = new[]
        {
            new CustomElementDefinition("definition-a", "Prievlak", "PR"),
            new CustomElementDefinition("definition-b", "Konzola", "KO"),
        };

        var renamed = CustomElementDefinitionCatalogRules.ApplyRename(
            catalog,
            new CustomElementDefinition(
                "definition-a",
                "Hlavný prievlak",
                "PR"));

        Assert.Equal(2, renamed.Count);
        Assert.Equal(
            "Hlavný prievlak",
            renamed.Single(item => item.Id == "definition-a").Name);
        Assert.Equal(
            "Konzola",
            renamed.Single(item => item.Id == "definition-b").Name);
    }

    [Fact]
    public void RenamedDefinition_RemainsVisibleInReportAndInspectDisplay()
    {
        var original = CustomData("PR1", "definition-a", "Prievlak", "PR");
        var renamed = CustomElementDefinitionRenameRules.Apply(
            original,
            new CustomElementDefinition(
                "definition-a",
                "Hlavný prievlak",
                "PR"));

        var report = TimberReportBuilder.Build([Measurement(renamed, 3000)]);

        Assert.Equal(
            "Hlavný prievlak",
            Assert.Single(report.Lines).CustomElementTypeName);
        Assert.Equal(
            "Hlavný prievlak",
            TimberElementDisplayNameProvider.GetDisplayName(
                renamed,
                CultureInfo.GetCultureInfo("de-DE")));
    }

    [Fact]
    public void StableNumbering_UsesIndependentSeriesAndSharesMatchingSignature()
    {
        var assignments = TimberElementItemNumbering.AssignElementIds(
        [
            Measurement(CustomData("KO1", "a", "Konzola", "KO"), 3000),
            Measurement(CustomData("KO1", "a", "Konzola", "KO"), 3000),
            Measurement(CustomData("PR1", "b", "Prievlak", "PR"), 3000),
            Measurement(CustomData("KO1", "a", "Konzola", "KO"), 4000),
        ]);

        Assert.Equal(["KO1", "KO1", "PR1", "KO2"], assignments.Select(item => item.ElementId));
    }

    [Fact]
    public void ExplicitRenumber_CompactsEachCustomDefinitionIndependently()
    {
        var assignments = TimberElementItemNumbering.RenumberElementIdsByCuttingLength(
        [
            Measurement(CustomData("KO8", "a", "Konzola", "KO"), 4000),
            Measurement(CustomData("KO4", "a", "Konzola", "KO"), 2000),
            Measurement(CustomData("PR9", "b", "Prievlak", "PR"), 3000),
            Measurement(CustomData("PR2", "b", "Prievlak", "PR"), 1000),
        ]);

        Assert.Equal(["KO2", "KO1", "PR2", "PR1"], assignments.Select(item => item.ElementId));
    }

    [Fact]
    public void BuiltInNumbering_RemainsUnchanged()
    {
        var data = TimberElementDefaults.For(TimberElementType.Rafter) with { ElementId = "K4" };
        var result = TimberElementItemNumbering.AssignElementIds([Measurement(data, 3000)]);

        Assert.Equal("K4", Assert.Single(result).ElementId);
    }

    [Fact]
    public void Report_SeparatesDefinitionsAndCarriesPersistentCustomName()
    {
        var report = TimberReportBuilder.Build(
        [
            Measurement(CustomData("KO1", "a", "Konzola", "KO"), 3000),
            Measurement(CustomData("KO1", "a", "Konzola", "KO"), 3000),
            Measurement(CustomData("PR1", "b", "Prievlak", "PR"), 3000),
        ]);

        Assert.Equal(2, report.Lines.Count);
        Assert.Equal(2, report.Lines.Single(line => line.CustomElementTypeId == "a").Count);
        Assert.Equal("Konzola", report.Lines.Single(line => line.CustomElementTypeId == "a").CustomElementTypeName);
        Assert.Equal("Prievlak", report.Lines.Single(line => line.CustomElementTypeId == "b").CustomElementTypeName);
    }

    [Theory]
    [InlineData("sk-SK")]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("pl-PL")]
    [InlineData("fr-FR")]
    public void CustomDisplayName_IsNeverTranslated(string cultureName)
    {
        var data = CustomData("KO1", "a", "Konzola", "KO");

        Assert.Equal(
            "Konzola",
            TimberElementDisplayNameProvider.GetDisplayName(
                data,
                CultureInfo.GetCultureInfo(cultureName)));
    }

    [Fact]
    public void Label_UsesStandardThreeLineFormatWithoutCustomName()
    {
        var data = CustomData("KO1", "a", "Konzola", "KO");
        var label = TimberElementLabelFormatter.Format(data, Measurement(data, 3000));

        Assert.Equal("KO1\\P100x200\\P3000 mm", label);
        Assert.DoesNotContain("Konzola", label, StringComparison.Ordinal);
    }

    [Fact]
    public void MetadataCopy_RetainsCompleteDefinitionAndSignature()
    {
        var original = CustomData("KO1", "a", "Konzola", "KO");
        var copied = JsonSerializer.Deserialize<TimberElementData>(
            JsonSerializer.Serialize(original, JsonOptions),
            JsonOptions)!;

        Assert.Equal(original.CustomElementTypeId, copied.CustomElementTypeId);
        Assert.Equal(original.CustomElementTypeName, copied.CustomElementTypeName);
        Assert.Equal(original.CustomElementTypePrefix, copied.CustomElementTypePrefix);
        Assert.Equal(
            TimberElementSignature.FromMeasurement(Measurement(original, 3000)),
            TimberElementSignature.FromMeasurement(Measurement(copied, 3000)));
        Assert.Equal(
            "KO1\\P100x200\\P3000 mm",
            TimberElementLabelFormatter.Format(
                copied,
                Measurement(copied, 3000)));
    }

    private static TimberElementData CustomData(
        string elementId,
        string definitionId,
        string name,
        string prefix) =>
        CustomElementDefinitionRules.Apply(
            TimberElementDefaults.For(TimberElementType.Custom) with
            {
                ElementId = elementId,
                WidthMm = 100,
                HeightMm = 200,
            },
            new CustomElementDefinition(definitionId, name, prefix));

    private static TimberElementMeasurement Measurement(
        TimberElementData data,
        double cuttingLengthMm) =>
        new(
            data,
            PlanLengthMm: cuttingLengthMm,
            ActualLengthMm: cuttingLengthMm,
            CuttingLengthMm: cuttingLengthMm,
            VolumeM3: data.WidthMm * data.HeightMm * cuttingLengthMm / 1_000_000_000d);
}
