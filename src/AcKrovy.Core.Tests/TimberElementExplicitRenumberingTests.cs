using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementExplicitRenumberingTests
{
    [Fact]
    public void Renumber_OrdersEachTypeByCuttingLengthAndCompactsGaps()
    {
        var result = Renumber(
            Measurement("K8", TimberElementType.Rafter, 80, 160, 4800),
            Measurement("K2", TimberElementType.Rafter, 80, 160, 3200),
            Measurement("K5", TimberElementType.Rafter, 80, 160, 4100));

        Assert.Equal(new[] { "K3", "K1", "K2" }, result.Select(item => item.ElementId));
    }

    [Fact]
    public void Renumber_UsesIndependentNumberSequencesForElementTypes()
    {
        var result = Renumber(
            Measurement("K9", TimberElementType.Rafter, 80, 160, 4000),
            Measurement("P7", TimberElementType.WallPlate, 140, 140, 6000),
            Measurement("K4", TimberElementType.Rafter, 80, 160, 3000),
            Measurement("P2", TimberElementType.WallPlate, 140, 140, 5000));

        Assert.Equal(new[] { "K2", "P2", "K1", "P1" }, result.Select(item => item.ElementId));
    }

    [Fact]
    public void Renumber_EqualSignaturesReceiveTheSameItemNumber()
    {
        var result = Renumber(
            Measurement("K7", TimberElementType.Rafter, 80, 160, 3500),
            Measurement("K9", TimberElementType.Rafter, 80, 160, 3500));

        Assert.Equal(new[] { "K1", "K1" }, result.Select(item => item.ElementId));
        Assert.Equal(result[0].Signature, result[1].Signature);
    }

    [Fact]
    public void Renumber_DifferentSignaturesReceiveDifferentItemNumbers()
    {
        var result = Renumber(
            Measurement("K1", TimberElementType.Rafter, 80, 160, 3500),
            Measurement("K1", TimberElementType.Rafter, 100, 160, 3500));

        Assert.Equal(new[] { "K1", "K2" }, result.Select(item => item.ElementId));
    }

    [Fact]
    public void Renumber_EqualLengthsUseSectionAndMaterialAsDeterministicTieBreakers()
    {
        var source = new[]
        {
            Measurement("K9", TimberElementType.Rafter, 100, 160, 4000, "Smrek C24"),
            Measurement("K8", TimberElementType.Rafter, 80, 180, 4000, "Smrek C24"),
            Measurement("K7", TimberElementType.Rafter, 80, 160, 4000, "Smrek C30"),
            Measurement("K6", TimberElementType.Rafter, 80, 160, 4000, "Smrek C24"),
        };

        var forward = Renumber(source).ToDictionary(item => item.Signature, item => item.ElementId);
        var reverse = Renumber(source.Reverse().ToArray()).ToDictionary(item => item.Signature, item => item.ElementId);

        Assert.Equal(forward.Count, reverse.Count);
        Assert.All(forward, item => Assert.Equal(item.Value, reverse[item.Key]));
        Assert.Equal("K1", forward[TimberElementSignature.FromMeasurement(source[3])]);
        Assert.Equal("K2", forward[TimberElementSignature.FromMeasurement(source[2])]);
        Assert.Equal("K3", forward[TimberElementSignature.FromMeasurement(source[1])]);
        Assert.Equal("K4", forward[TimberElementSignature.FromMeasurement(source[0])]);
    }

    [Fact]
    public void Renumber_UsesFinalCuttingLengthRatherThanPlanLength()
    {
        var shorterPlanButLongerCut = Measurement(
            "K1", TimberElementType.Rafter, 80, 160, cuttingLengthMm: 4200, planLengthMm: 3000);
        var longerPlanButShorterCut = Measurement(
            "K2", TimberElementType.Rafter, 80, 160, cuttingLengthMm: 3800, planLengthMm: 3600);

        var result = Renumber(shorterPlanButLongerCut, longerPlanButShorterCut);

        Assert.Equal(new[] { "K2", "K1" }, result.Select(item => item.ElementId));
    }

    [Fact]
    public void Renumber_IsIdempotent()
    {
        var first = Renumber(
            Measurement("K8", TimberElementType.Rafter, 80, 160, 4200),
            Measurement("K3", TimberElementType.Rafter, 80, 160, 3200));
        var second = Renumber(first.Select(ApplyAssignment).ToArray());

        Assert.Equal(first.Select(item => item.ElementId), second.Select(item => item.ElementId));
        Assert.All(second, item => Assert.False(item.IsChanged));
    }

    [Fact]
    public void Renumber_ChangesOnlyElementIdInTimberMetadata()
    {
        var original = Measurement("K8", TimberElementType.Rafter, 80, 160, 4200).Data with
        {
            RoofPlaneId = "R7",
            CuttingAllowanceMm = 250,
            IsSlopeDirectionReversed = true,
            Note = "Preserve me",
        };
        var assignment = Renumber(new TimberElementMeasurement(
            original,
            PlanLengthMm: 4100,
            ActualLengthMm: 4100,
            CuttingLengthMm: 4200,
            VolumeM3: 0.055))[0];
        var updated = assignment.Measurement.Data with { ElementId = assignment.ElementId };

        Assert.Equal(original with { ElementId = updated.ElementId }, updated);
        Assert.Equal("K1", updated.ElementId);
    }

    [Fact]
    public void Renumber_UpdatedItemNumberFlowsToLabelAndReport()
    {
        var assignments = Renumber(
            Measurement("K9", TimberElementType.Rafter, 80, 160, 4200),
            Measurement("K4", TimberElementType.Rafter, 80, 160, 3200));
        var updated = assignments.Select(ApplyAssignment).ToArray();

        var label = TimberElementLabelFormatter.Format(updated[0].Data, updated[0]);
        var report = TimberReportBuilder.Build(updated);

        Assert.StartsWith("K2\\P", label);
        Assert.Equal(new[] { "K1", "K2" }, report.Lines.Select(line => line.ElementId));
    }

    [Fact]
    public void StableAutomaticNumbering_RemainsGapPreservingWithoutExplicitRenumber()
    {
        var measurements = new[]
        {
            Measurement("K1", TimberElementType.Rafter, 80, 160, 3000),
            Measurement("K4", TimberElementType.Rafter, 80, 160, 4000),
        };

        var automatic = TimberElementItemNumbering.AssignElementIds(measurements);

        Assert.Equal(new[] { "K1", "K4" }, automatic.Select(item => item.ElementId));
    }

    [Fact]
    public void Renumber_EmptyDrawingProducesAnEmptyPlan()
    {
        Assert.Empty(Renumber());
    }

    [Fact]
    public void Renumber_PreservesInputOrderUsedToMapPhysicalEntities()
    {
        var sourceHandles = new[] { "A1", "B2", "C3" };
        var result = Renumber(
            Measurement("K9", TimberElementType.Rafter, 80, 160, 5000),
            Measurement("K8", TimberElementType.Rafter, 80, 160, 3000),
            Measurement("K7", TimberElementType.Rafter, 80, 160, 4000));

        var mappedHandles = result.Select((_, index) => sourceHandles[index]);

        Assert.Equal(sourceHandles, mappedHandles);
        Assert.Equal(new[] { "K3", "K1", "K2" }, result.Select(item => item.ElementId));
    }

    [Fact]
    public void CopyAfterRenumber_WithSameSignatureKeepsSharedItemNumber()
    {
        var renumbered = ApplyAssignment(Renumber(
            Measurement("K8", TimberElementType.Rafter, 80, 160, 4200))[0]);

        var automatic = TimberElementItemNumbering.AssignElementIds([renumbered, renumbered]);

        Assert.Equal(new[] { "K1", "K1" }, automatic.Select(item => item.ElementId));
    }

    [Fact]
    public void LiveGeometrySynchronizationAfterRenumber_DoesNotCompactUnchangedItems()
    {
        var candidates = new[]
        {
            new TimberElementItemNumberingCandidate(
                Measurement("K1", TimberElementType.Rafter, 80, 160, 3000),
                IsChanged: false),
            new TimberElementItemNumberingCandidate(
                Measurement("K2", TimberElementType.Rafter, 80, 160, 4500),
                IsChanged: true),
            new TimberElementItemNumberingCandidate(
                Measurement("K3", TimberElementType.Rafter, 80, 160, 5000),
                IsChanged: false),
        };

        var synchronized = TimberElementItemNumbering.AssignElementIds(candidates);

        Assert.Equal(new[] { "K1", "K2", "K3" }, synchronized.Select(item => item.ElementId));
    }

    [Fact]
    public void Renumber_UpdatedPostItemNumberFlowsToDedicatedPostLabel()
    {
        var assignment = Renumber(
            Measurement("S9", TimberElementType.Post, 140, 140, 2500))[0];
        var updated = ApplyAssignment(assignment);

        var label = TimberPostFootprintLabelFormatter.Format(updated.Data, updated.ActualLengthMm);

        Assert.StartsWith("S1\\P", label);
    }

    [Theory]
    [InlineData(null, "Áno", "sk-SK", false)]
    [InlineData("", "Áno", "sk-SK", false)]
    [InlineData("No", "Áno", "sk-SK", false)]
    [InlineData("Yes", "Áno", "sk-SK", true)]
    [InlineData("a", "Áno", "sk-SK", true)]
    [InlineData("A", "Áno", "sk-SK", true)]
    [InlineData("á", "Áno", "sk-SK", true)]
    [InlineData("Á", "Áno", "sk-SK", true)]
    [InlineData("ano", "Áno", "sk-SK", true)]
    [InlineData("Áno", "Áno", "sk-SK", true)]
    [InlineData("áno", "Áno", "sk-SK", true)]
    [InlineData("áNO", "Áno", "sk-SK", true)]
    [InlineData("Ano", "Áno", "sk-SK", true)]
    [InlineData("aNO", "Áno", "sk-SK", true)]
    [InlineData("n", "Áno", "sk-SK", false)]
    [InlineData("N", "Áno", "sk-SK", false)]
    [InlineData("Nie", "Áno", "sk-SK", false)]
    [InlineData("nie", "Áno", "sk-SK", false)]
    [InlineData("Oui", "Oui", "fr-FR", true)]
    [InlineData("Ano", "Yes", "en-US", false)]
    [InlineData("A", "Yes", "en-US", false)]
    public void ConfirmationRules_DefaultAndNegativeAnswersDoNotExecute(
        string? result,
        string localizedYes,
        string cultureName,
        bool expected)
    {
        Assert.Equal(expected, RenumberConfirmationRules.IsConfirmed(
            result,
            localizedYes,
            CultureInfo.GetCultureInfo(cultureName)));
    }

    [Theory]
    [InlineData("sk-SK", true)]
    [InlineData("cs-CZ", false)]
    [InlineData("en-US", false)]
    [InlineData("de-DE", false)]
    [InlineData("pl-PL", false)]
    [InlineData("fr-FR", false)]
    public void SlovakAsciiAlias_IsRegisteredOnlyForSlovak(string cultureName, bool expected)
    {
        Assert.Equal(
            expected,
            RenumberConfirmationRules.SupportsSlovakAsciiYesAlias(
                CultureInfo.GetCultureInfo(cultureName)));
    }

    private static IReadOnlyList<TimberElementRenumberingAssignment> Renumber(
        params TimberElementMeasurement[] measurements) =>
        TimberElementItemNumbering.RenumberElementIdsByCuttingLength(measurements);

    private static TimberElementMeasurement ApplyAssignment(TimberElementRenumberingAssignment assignment) =>
        assignment.Measurement with
        {
            Data = assignment.Measurement.Data with { ElementId = assignment.ElementId },
        };

    private static TimberElementMeasurement Measurement(
        string elementId,
        TimberElementType type,
        double widthMm,
        double heightMm,
        double cuttingLengthMm,
        string material = "Smrek C24",
        double? planLengthMm = null)
    {
        var data = new TimberElementData
        {
            ElementId = elementId,
            ElementType = type,
            WidthMm = widthMm,
            HeightMm = heightMm,
            Material = material,
        };

        return new TimberElementMeasurement(
            data,
            PlanLengthMm: planLengthMm ?? cuttingLengthMm,
            ActualLengthMm: cuttingLengthMm,
            CuttingLengthMm: cuttingLengthMm,
            VolumeM3: widthMm * heightMm * cuttingLengthMm / 1_000_000_000d);
    }
}
