using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticStructuralRafterPlannerTests
{
    public static IEnumerable<object[]> MaterializationFixtures()
    {
        yield return ["rectangle", Points((0, 0), (10000, 0), (10000, 6000), (0, 6000)), 1, 4, 0, 4];
        yield return ["L", Points((0, 0), (8000, 0), (8000, 3000), (3000, 3000), (3000, 8000), (0, 8000)), 2, 5, 1, 6];
        yield return ["U", Points((0, 0), (10000, 0), (10000, 9000), (7000, 9000), (7000, 3000), (3000, 3000), (3000, 9000), (0, 9000)), 3, 6, 2, 8];
        yield return ["T", Points((0, 0), (10000, 0), (10000, 3000), (6500, 3000), (6500, 9000), (3500, 9000), (3500, 3000), (0, 3000)), 3, 6, 2, 8];
        yield return ["stepped", Points((0, 0), (11000, 0), (11000, 2500), (8000, 2500), (8000, 5000), (5000, 5000), (5000, 8500), (0, 8500)), 4, 6, 2, 8];
        yield return ["HOST 291A", Points((47947.813661, 12295.184331), (47947.813661, 20038.646240), (57988.858409, 20038.646240), (57988.858409, 15403.104447), (52849.740922, 15403.104447), (52849.740922, 12295.184331)), 2, 6, 1, 7];
    }

    [Theory]
    [MemberData(nameof(MaterializationFixtures))]
    public void Create_MapsCompleteStructuralDesiredSet(
        string name,
        RoofPoint2D[] points,
        int ridge,
        int hip,
        int valley,
        int total)
    {
        var resolution = Resolve(points);
        var plan = RoofAutomaticStructuralRafterPlanner.Create(resolution);

        Assert.True(plan.IsValid, name + ": " + plan.Error);
        Assert.Equal(ridge, resolution.Edges.Count(
            edge => edge.StructuralRole == RoofStructuralRole.Ridge));
        Assert.Equal(total, plan.Items.Count);
        Assert.Equal(hip, plan.Items.Count(item => item.ElementType == TimberElementType.HipRafter));
        Assert.Equal(valley, plan.Items.Count(item => item.ElementType == TimberElementType.ValleyRafter));
        Assert.Equal(total, plan.Items.Select(item => item.LogicalKey).Distinct().Count());
        Assert.DoesNotContain(
            plan.Items,
            item => item.LogicalKey.Role == RoofStructuralRole.Ridge);
    }

    [Fact]
    public void Create_UsesApprovedDefaultsAndNoAnnotations()
    {
        var plan = Create(Points((0, 0), (8000, 0), (8000, 3000), (3000, 3000), (3000, 8000), (0, 8000)));

        Assert.All(plan.Items.Where(item => item.ElementType is TimberElementType.HipRafter or TimberElementType.ValleyRafter), item =>
        {
            Assert.Equal(80d, item.TimberData.WidthMm);
            Assert.Equal(160d, item.TimberData.HeightMm);
            Assert.Equal(100d, item.TimberData.CuttingAllowanceMm);
        });
        Assert.All(plan.Items, item =>
        {
            Assert.Equal("Smrek C24", item.TimberData.Material);
            Assert.Equal(TimberAnnotationMode.NoAnnotations, item.TimberData.AnnotationMode);
            Assert.Equal(LengthCalculationMode.PlanLength, item.TimberData.LengthCalculationMode);
            Assert.Equal(0d, item.TimberData.SlopeDegrees);
        });
    }

    [Fact]
    public void GeneratedAutomaticRafterLength_IsExactlyAuthoritativeSegment3DLength()
    {
        var plan = Create(Points((0, 0), (8000, 0), (8000, 3000), (3000, 3000), (3000, 8000), (0, 8000)));
        var diagonal = Assert.Single(
            plan.Items,
            item => item.LogicalKey.Role == RoofStructuralRole.Valley);
        var planDx = diagonal.Segment3D.End.X - diagonal.Segment3D.Start.X;
        var planDy = diagonal.Segment3D.End.Y - diagonal.Segment3D.Start.Y;
        var projectedLength = Math.Sqrt(planDx * planDx + planDy * planDy);

        Assert.True(diagonal.True3DLengthMm > projectedLength);
        var measurement = TimberCalculator.Measure(
            diagonal.TimberData,
            diagonal.True3DLengthMm);
        Assert.Equal(diagonal.Segment3D.LengthMm, measurement.ActualLengthMm, 9);
        Assert.NotEqual(
            TimberCalculator.CalculateSlopeCorrectedLengthMm(projectedLength, 35d),
            measurement.ActualLengthMm);
    }

    [Fact]
    public void NewTypes_WorkInReportCsvAndDynamicSettingsEnumeration()
    {
        var types = new[]
        {
            TimberElementType.HipRafter,
            TimberElementType.ValleyRafter,
        };
        var measurements = types.Select((type, index) =>
        {
            var data = TimberElementDefaults.For(type) with
            {
                ElementId = TimberElementIdentityRules.CreateElementId(type, index + 1),
                LengthCalculationMode = LengthCalculationMode.PlanLength,
            };
            return TimberCalculator.Measure(data, 4000d + index * 100d);
        }).ToArray();

        var report = TimberReportBuilder.Build(measurements);
        var csv = TimberCsvFormatter.Format(
            measurements,
            TimberCsvExportMode.Individual,
            TimberCsvLocalizationProvider.Create(CultureInfo.GetCultureInfo("sk-SK")),
            CultureInfo.GetCultureInfo("sk-SK"));

        Assert.Equal(2, report.SourceElementCount);
        Assert.Equal(2, report.Lines.Count);
        Assert.Equal(2, csv.RowCount);
        Assert.Contains("Nárožná krokva", csv.Content);
        Assert.Contains("Úžľabná krokva", csv.Content);
        Assert.All(types, type => Assert.Contains(type, Enum.GetValues<TimberElementType>()));
        Assert.All(types, type => Assert.NotNull(
            AcKrovy.Cad.Abstractions.Layers.ElementLayerProfile.CreateDefault().GetStyle(type)));
    }

    [Fact]
    public void EnumValues_AppendWithoutChangingPersistedExistingValues()
    {
        Assert.Equal(7, (int)TimberElementType.Custom);
        Assert.Equal(8, (int)TimberElementType.HipRafter);
        Assert.Equal(9, (int)TimberElementType.ValleyRafter);
        Assert.DoesNotContain("RidgeMember", Enum.GetNames<TimberElementType>());
    }

    [Fact]
    public void RidgeIdentity_RemainsResolvedButIsNotAnAutomaticRafterRole()
    {
        var resolution = Resolve(Points((0, 0), (10000, 0), (10000, 6000), (0, 6000)));

        Assert.Contains(
            resolution.Edges,
            edge => edge.StructuralRole == RoofStructuralRole.Ridge);
        Assert.False(RoofAutomaticStructuralRafterPlanner.TryMapType(
            RoofStructuralRole.Ridge,
            out _));
        Assert.DoesNotContain(
            RoofAutomaticStructuralRafterPlanner.Create(resolution).Items,
            item => item.LogicalKey.Role == RoofStructuralRole.Ridge);
    }

    private static RoofAutomaticStructuralRafterPlanResult Create(RoofPoint2D[] points) =>
        RoofAutomaticStructuralRafterPlanner.Create(Resolve(points));

    private static RoofStructuralEdgeResolutionResult Resolve(RoofPoint2D[] points)
    {
        var input = new RoofFootprintInput(points, IsClosed: true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid);
        var identity = RoofBoundaryIdentityRules.CreateSequential(
            normalized.EdgeProvenance.Count,
            normalized.Validation.SourceOrientation).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        var solved = HipRoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(30),
            RoofKind.Hip));
        var geometry = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        return RoofStructuralEdgeIdentityResolver.Resolve(geometry, provenance);
    }

    private static RoofPoint2D[] Points(params (double X, double Y)[] points) =>
        points.Select(point => new RoofPoint2D(point.X, point.Y)).ToArray();
}
