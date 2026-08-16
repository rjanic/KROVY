using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Original vs copy-equivalent resize classification parity.
/// COPY must not be required as an accidental repair for supported eave-side STRETCH.
/// </summary>
public sealed class RoofOriginalCopyResizeParityTests
{
    [Fact]
    public void FreshOriginal_EaveSideEnlarge_IsSupportedResize()
    {
        AssertEave(Rect(), 2000d, RoofSourceChangeKind.SupportedResize);
    }

    [Fact]
    public void FreshOriginal_EaveSideShrink_IsSupportedResize()
    {
        AssertEave(Rect(), -1500d, RoofSourceChangeKind.SupportedResize);
    }

    [Fact]
    public void CopiedEquivalentPayload_EaveSideEnlarge_MatchesOriginal()
    {
        var original = Rect();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        Assert.True(RoofDefinitionDataCodec.TryDecode(
            RoofDefinitionDataCodec.Encode(data),
            out var copyData,
            out _));
        var resized = StretchEave(original, 2000d);
        var originalKind = Classify(resized, data).Kind;
        var copyKind = Classify(resized, copyData!).Kind;
        Assert.Equal(RoofSourceChangeKind.SupportedResize, originalKind);
        Assert.Equal(originalKind, copyKind);
    }

    [Fact]
    public void CopiedEquivalentPayload_EaveSideShrink_MatchesOriginal()
    {
        var original = Rect();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        Assert.True(RoofDefinitionDataCodec.TryDecode(
            RoofDefinitionDataCodec.Encode(data),
            out var copyData,
            out _));
        var resized = StretchEave(original, -1200d);
        Assert.Equal(Classify(resized, data).Kind, Classify(resized, copyData!).Kind);
        Assert.Equal(RoofSourceChangeKind.SupportedResize, Classify(resized, data).Kind);
    }

    [Fact]
    public void OrientationFlipped_SameRectangle_IsRigidEquivalent_NotUnsupported()
    {
        var original = Rect();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var flipped = Reverse(original);
        var classification = Classify(flipped, data);
        Assert.Equal(RoofSourceChangeKind.RigidEquivalent, classification.Kind);
        Assert.Contains("resolvePath=orientation-flipped", Explain(flipped, data));
    }

    [Fact]
    public void OrientationFlipped_ThenEaveStretch_IsSupportedResize_NotUnsupported()
    {
        var original = Rect();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var flippedResized = StretchEave(Reverse(original), 2000d);
        var classification = Classify(flippedResized, data);
        Assert.Equal(RoofSourceChangeKind.SupportedResize, classification.Kind);
        Assert.True(Restore(flippedResized, data).IsValid);
        Assert.Equal(35d, Restore(flippedResized, data).Geometry!.SlopeDegrees);
    }

    [Fact]
    public void ClockwiseOriginal_EaveStretch_IsSupportedResize()
    {
        var original = Reverse(Rect());
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        Assert.Equal(
            RoofSourceChangeKind.SupportedResize,
            Classify(StretchEave(original, 1800d), data).Kind);
    }

    [Fact]
    public void GableEndResize_UnchangedSupported()
    {
        var original = Rect();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var resized = StretchGable(original, 2000d);
        Assert.Equal(RoofSourceChangeKind.SupportedResize, Classify(resized, data).Kind);
    }

    [Fact]
    public void RotatedRoof_EaveStretch_Supported()
    {
        var original = Transform(Rect(), 30d, 400d, -250d);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var resized = StretchEaveAlongDepth(original, 2000d);
        Assert.Equal(RoofSourceChangeKind.SupportedResize, Classify(resized, data).Kind);
    }

    [Fact]
    public void PersistedRidgeFamily_RetainedAcrossOrientationFlipRestore()
    {
        var original = Rect();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var before = Restore(original, data).Geometry!;
        var flippedResized = StretchEave(Reverse(original), 2000d);
        var after = Restore(flippedResized, data).Geometry!;
        AssertParallel(before.RidgeDirection, after.RidgeDirection);
    }

    [Fact]
    public void Trapezoid_StillUnsupported()
    {
        var original = Rect();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var trap = new RoofFootprintInput(
            [new(0, 0), new(10000, 0), new(9000, 6000), new(1000, 6000)],
            true,
            false,
            true);
        Assert.Equal(RoofSourceChangeKind.Unsupported, Classify(trap, data).Kind);
    }

    [Fact]
    public void ExplainClassify_DistinguishesNativeAndFlippedPaths()
    {
        var original = Rect();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        Assert.Contains("resolvePath=native", Explain(original, data));
        Assert.Contains("resolvePath=orientation-flipped", Explain(Reverse(original), data));
    }

    private static void AssertEave(
        RoofFootprintInput original,
        double delta,
        RoofSourceChangeKind expected)
    {
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        Assert.Equal(expected, Classify(StretchEave(original, delta), data).Kind);
    }

    private static RoofSourceChangeClassification Classify(
        RoofFootprintInput source,
        RoofDefinitionData data) =>
        RoofDefinitionPersistence.Classify(source, Val(source), data);

    private static string Explain(RoofFootprintInput source, RoofDefinitionData data) =>
        RoofDefinitionPersistence.ExplainClassify(source, Val(source), data);

    private static RoofDefinitionRestoreResult Restore(
        RoofFootprintInput source,
        RoofDefinitionData data) =>
        RoofDefinitionPersistence.Restore(source, Val(source), data);

    private static RoofFootprint Val(RoofFootprintInput source)
    {
        var result = RoofFootprintValidator.Validate(source);
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Footprint!;
    }

    private static RoofDefinitionData Create(
        RoofFootprintInput source,
        double slope,
        RoofRidgeEdgeFamily family)
    {
        var footprint = Val(source);
        var v = source.Vertices!;
        var (dx, dy) = family == RoofRidgeEdgeFamily.SourceEdge01
            ? (v[1].X - v[0].X, v[1].Y - v[0].Y)
            : (v[2].X - v[1].X, v[2].Y - v[1].Y);
        Assert.True(RoofDirection2D.TryCreate(dx, dy, out var direction));
        var solved = SimpleGableRoofGeometrySolver.Solve(
            new RoofDefinition(footprint, new RoofParameters(slope, direction)));
        Assert.True(solved.IsValid, solved.Error.ToString());
        return RoofDefinitionPersistence.Create(source, footprint, solved.Geometry!);
    }

    private static RoofFootprintInput Rect() =>
        new([new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)], true, false, true);

    private static RoofFootprintInput Reverse(RoofFootprintInput source) =>
        new(source.Vertices!.Reverse().ToArray(), true, false, true);

    private static RoofFootprintInput StretchEave(RoofFootprintInput source, double delta)
    {
        var v = source.Vertices!.ToArray();
        return new(
            [v[0], v[1], new(v[2].X, v[2].Y + delta), new(v[3].X, v[3].Y + delta)],
            true,
            false,
            true);
    }

    private static RoofFootprintInput StretchGable(RoofFootprintInput source, double delta)
    {
        var v = source.Vertices!.ToArray();
        return new(
            [v[0], new(v[1].X + delta, v[1].Y), new(v[2].X + delta, v[2].Y), v[3]],
            true,
            false,
            true);
    }

    private static RoofFootprintInput StretchEaveAlongDepth(RoofFootprintInput source, double delta)
    {
        var v = source.Vertices!.ToArray();
        var e12x = v[2].X - v[1].X;
        var e12y = v[2].Y - v[1].Y;
        var len = Math.Sqrt(e12x * e12x + e12y * e12y);
        var ux = e12x / len;
        var uy = e12y / len;
        return new(
            [
                v[0],
                v[1],
                new(v[2].X + ux * delta, v[2].Y + uy * delta),
                new(v[3].X + ux * delta, v[3].Y + uy * delta),
            ],
            true,
            false,
            true);
    }

    private static RoofFootprintInput Transform(
        RoofFootprintInput source,
        double degrees,
        double dx,
        double dy)
    {
        var radians = degrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new(
            source.Vertices!
                .Select(v => new RoofPoint2D(
                    v.X * cos - v.Y * sin + dx,
                    v.X * sin + v.Y * cos + dy))
                .ToArray(),
            true,
            false,
            true);
    }

    private static void AssertParallel(RoofDirection2D first, RoofDirection2D second) =>
        Assert.True(Math.Abs(first.X * second.Y - first.Y * second.X) < 1e-6d);
}
