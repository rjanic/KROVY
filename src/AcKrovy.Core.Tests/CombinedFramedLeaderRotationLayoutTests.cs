using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class CombinedFramedLeaderRotationLayoutTests
{
    public static TheoryData<double, double, double, double> ReverseDirectionCases { get; } =
        new()
        {
            // A
            { 0d, 0d, 1000d, 700d },
            // B — reversed endpoints of A
            { 1000d, 700d, 0d, 0d },
            // C
            { 0d, 700d, 1000d, 0d },
            // D — reversed endpoints of C
            { 1000d, 0d, 0d, 700d },
        };

    [Theory]
    [InlineData(0d)]
    [InlineData(0.5235987755982988d)] // π/6
    [InlineData(1.5707963267948966d)] // π/2
    [InlineData(3.141592653589793d)] // π
    public void CalculateBlock_UsesElementAlignedPlaneNotWorldHorizontal(
        double elementAxisRadians)
    {
        var readable = TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
            elementAxisRadians);
        var placement = new TimberLeaderPlacement(
            AnchorX: 0d,
            AnchorY: 0d,
            TextX: 0d,
            TextY: 100d,
            RotationRadians: readable);
        var layout = TimberItemLeaderLayoutCalculator.CalculateBlock(
            placement,
            "K1",
            ItemNumberLeaderStyle.Circle,
            TimberLeaderHorizontalSide.Right,
            presentationScaleFactor: 1d);

        var dx = layout.KneeX - layout.AnchorX;
        var dy = layout.KneeY - layout.AnchorY;
        var segmentAngle = Math.Atan2(dy, dx);
        var axis = Math.Atan2(Math.Sin(readable), Math.Cos(readable));
        var delta = TimberAnnotationReadabilityRules.NormalizeAngleDelta(
            segmentAngle - axis);

        // First segment is 60° from the element axis in the element-aligned plane,
        // not from world +X.
        Assert.InRange(Math.Abs(Math.Abs(delta) - (Math.PI / 3d)), 0d, 1e-6);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(35d * Math.PI / 180d)]
    [InlineData(-35d * Math.PI / 180d)]
    [InlineData(Math.PI / 2d)]
    public void ResolveCombinedLandingDirection_FollowsElementAxisNotWorldX(
        double elementAxisRadians)
    {
        var readable = TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
            elementAxisRadians);
        var (x, y) = TimberItemLeaderLayoutCalculator.ResolveCombinedLandingDirection(
            readable,
            TimberLeaderHorizontalSide.Right);

        Assert.Equal(Math.Cos(readable), x, 12);
        Assert.Equal(Math.Sin(readable), y, 12);
        Assert.False(
            Math.Abs(readable) > 1e-9 &&
            Math.Abs(y) < 1e-9 &&
            Math.Abs(x - 1d) < 1e-9,
            "Landing must not collapse to world +X on a non-horizontal element axis.");
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(35d * Math.PI / 180d)]
    [InlineData(-35d * Math.PI / 180d)]
    [InlineData(Math.PI / 2d)]
    public void ResolveCombinedLandingDirection_LeftSideIsOppositeHorizontal(
        double elementAxisRadians)
    {
        var readable = TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
            elementAxisRadians);
        var right = TimberItemLeaderLayoutCalculator.ResolveCombinedLandingDirection(
            readable,
            TimberLeaderHorizontalSide.Right);
        var left = TimberItemLeaderLayoutCalculator.ResolveCombinedLandingDirection(
            readable,
            TimberLeaderHorizontalSide.Left);

        Assert.Equal(-right.X, left.X, 12);
        Assert.Equal(-right.Y, left.Y, 12);
    }

    [Theory]
    [MemberData(nameof(ReverseDirectionCases))]
    public void ReverseSourceDirections_WholeFramedLayoutSharesOneReadablePlane(
        double startX,
        double startY,
        double endX,
        double endY)
    {
        var placement = TimberLeaderPlacementCalculator.CalculateLinear(
            startX,
            startY,
            endX,
            endY,
            midpointX: (startX + endX) / 2d,
            midpointY: (startY + endY) / 2d);
        var side = endX < startX ||
            (Math.Abs(endX - startX) <= 1e-9 && endY < startY)
            ? TimberLeaderHorizontalSide.Left
            : TimberLeaderHorizontalSide.Right;
        var layout = TimberItemLeaderLayoutCalculator.CalculateBlock(
            placement,
            "K1",
            ItemNumberLeaderStyle.Circle,
            side,
            presentationScaleFactor: 1d);
        var landing = TimberItemLeaderLayoutCalculator.ResolveCombinedLandingDirection(
            placement.RotationRadians,
            side,
            layout.ContentX - layout.KneeX,
            layout.ContentY - layout.KneeY);

        var axis = Math.Atan2(
            Math.Sin(placement.RotationRadians),
            Math.Cos(placement.RotationRadians));
        // Side.Left builds the first segment from −H; measure against that signed axis.
        var signedAxis = side == TimberLeaderHorizontalSide.Left
            ? TimberAnnotationReadabilityRules.NormalizeAngleDelta(axis + Math.PI)
            : axis;
        var kneeAngle = Math.Atan2(
            layout.KneeY - layout.AnchorY,
            layout.KneeX - layout.AnchorX);
        var landingAngle = Math.Atan2(landing.Y, landing.X);
        var kneeDelta = TimberAnnotationReadabilityRules.NormalizeAngleDelta(
            kneeAngle - signedAxis);
        var landingDelta = TimberAnnotationReadabilityRules.NormalizeAngleDelta(
            landingAngle - axis);

        Assert.InRange(Math.Abs(Math.Abs(kneeDelta) - (Math.PI / 3d)), 0d, 1e-6);
        Assert.True(
            Math.Abs(landingDelta) < 1e-6 ||
            Math.Abs(Math.Abs(landingDelta) - Math.PI) < 1e-6,
            "Landing must stay on the element ±H axis, not a world-horizontal dogleg.");
        Assert.Equal(
            side == TimberLeaderHorizontalSide.Left ? -1 : 1,
            Math.Sign(
                landing.X * Math.Cos(axis) +
                landing.Y * Math.Sin(axis)));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(35d * Math.PI / 180d)]
    [InlineData(-35d * Math.PI / 180d)]
    [InlineData(Math.PI / 2d)]
    public void GeometricOrientation_IsDistinctFromReadabilityFlip(double rawAxisRadians)
    {
        var readable =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(rawAxisRadians);
        var flipped = TimberAnnotationReadabilityRules.IsReadabilityFlipped(rawAxisRadians);
        var basis = TimberLeaderPlaneBasis.FromRotationRadians(readable);

        // Layout plane uses the readable axis; flip is a text concern only.
        Assert.Equal(Math.Cos(readable), basis.HorizontalX, 12);
        Assert.Equal(Math.Sin(readable), basis.HorizontalY, 12);
        Assert.Equal(
            flipped,
            Math.Abs(
                TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                    readable - rawAxisRadians)) >
            1e-9);
    }

    [Fact]
    public void PlaneBasis_FromRotation_MatchesWorldXYAtZero()
    {
        var basis = TimberLeaderPlaneBasis.FromRotationRadians(0d);
        Assert.Equal(1d, basis.HorizontalX, precision: 12);
        Assert.Equal(0d, basis.HorizontalY, precision: 12);
        Assert.Equal(0d, basis.VerticalX, precision: 12);
        Assert.Equal(1d, basis.VerticalY, precision: 12);
    }

    [Fact]
    public void PlaneBasis_FromRotation_FollowsOppositeDirection()
    {
        var basis = TimberLeaderPlaneBasis.FromRotationRadians(Math.PI);
        Assert.Equal(-1d, basis.HorizontalX, precision: 12);
        Assert.Equal(0d, basis.HorizontalY, precision: 12);
        Assert.Equal(0d, basis.VerticalX, precision: 12);
        Assert.Equal(-1d, basis.VerticalY, precision: 12);
    }

    [Fact]
    public void ApplyCombinedLandingDistance_SourceContract_RejectsWorldXFallback()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "AcKrovy.AutoCAD",
                "Infrastructure",
                "ElementLabelService.cs"));
        var apply = Member(source, "private static LeaderPlacement ApplyCombinedLandingDistance(");
        Assert.Contains("ResolveCombinedLandingDirection(", apply);
        Assert.DoesNotContain("Vector3d.XAxis", apply);
        Assert.DoesNotContain("-Vector3d.XAxis", apply);
    }

    private static string Member(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing member: {signature}");
        var brace = source.IndexOf('{', start);
        Assert.True(brace > start);
        var depth = 0;
        for (var index = brace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces for {signature}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
