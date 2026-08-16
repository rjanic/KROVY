using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGroupGripNativeObservationRulesTests
{
    [Fact]
    public void CompleteSevenRoles_Required()
    {
        Assert.False(RoofGroupGripNativeObservationRules.IsCompleteSevenRoles(null));
        Assert.False(RoofGroupGripNativeObservationRules.IsCompleteSevenRoles(
            new Dictionary<RoofDisplayEdgeRole, RoofSegment3D>()));
        Assert.True(RoofGroupGripNativeObservationRules.IsCompleteSevenRoles(Complete(0d)));
    }

    [Fact]
    public void ZeroDelta_IsRejectedAsNotMeaningful()
    {
        var map = Complete(0d);
        Assert.False(
            RoofGroupGripNativeObservationRules.HasMeaningfulDeltaFromExpected(
                map,
                map,
                RoofGroupGripResizeAdoptionRules.GripAdoptionToleranceMm));
    }

    [Fact]
    public void ChangedEndpoint_IsMeaningfulDelta()
    {
        var expected = Complete(0d);
        var observed = Complete(500d);
        Assert.True(
            RoofGroupGripNativeObservationRules.HasMeaningfulDeltaFromExpected(
                expected,
                observed,
                RoofGroupGripResizeAdoptionRules.GripAdoptionToleranceMm));
    }

    [Fact]
    public void IncompleteObserved_IsNotMeaningfulDelta()
    {
        var expected = Complete(0d);
        var observed = Complete(500d);
        observed.Remove(RoofDisplayEdgeRole.Ridge);
        Assert.False(
            RoofGroupGripNativeObservationRules.HasMeaningfulDeltaFromExpected(
                expected,
                observed,
                RoofGroupGripResizeAdoptionRules.GripAdoptionToleranceMm));
    }

    private static Dictionary<RoofDisplayEdgeRole, RoofSegment3D> Complete(double dx)
    {
        RoofSegment3D Seg(double x0, double y0, double x1, double y1) =>
            new(
                new RoofPoint3D(x0 + dx, y0, 0d),
                new RoofPoint3D(x1 + dx, y1, 0d));

        return new Dictionary<RoofDisplayEdgeRole, RoofSegment3D>
        {
            [RoofDisplayEdgeRole.Ridge] = Seg(0, 3000, 10000, 3000),
            [RoofDisplayEdgeRole.Eave0] = Seg(0, 0, 10000, 0),
            [RoofDisplayEdgeRole.Eave1] = Seg(0, 6000, 10000, 6000),
            [RoofDisplayEdgeRole.GableSlope00] = Seg(0, 3000, 0, 0),
            [RoofDisplayEdgeRole.GableSlope01] = Seg(10000, 3000, 10000, 0),
            [RoofDisplayEdgeRole.GableSlope10] = Seg(0, 3000, 0, 6000),
            [RoofDisplayEdgeRole.GableSlope11] = Seg(10000, 3000, 10000, 6000),
        };
    }
}
