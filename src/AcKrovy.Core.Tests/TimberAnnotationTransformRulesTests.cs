using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationTransformRulesTests
{
    private const double Deg = Math.PI / 180d;

    [Theory]
    [InlineData(0d, 45d, 45d)]
    [InlineData(45d, 45d, 90d)]
    [InlineData(90d, 90d, 180d)]
    [InlineData(180d, 180d, 0d)]
    [InlineData(-10d, 45d, 35d)]
    [InlineData(350d, 45d, 35d)]
    [InlineData(-180d, 90d, -90d)]
    public void RotateRelative_AddsDeltaAndNormalizes(
        double currentDeg,
        double deltaDeg,
        double expectedTargetDeg)
    {
        var decision = TimberAnnotationTransformRules.Resolve(
            TimberAnnotationTransformRequest.RotateRelative(deltaDeg * Deg),
            currentDeg * Deg);

        Assert.Equal(
            TimberAnnotationTransformKind.RotateRelative,
            decision.Request.Kind);
        AssertWorldAngle(expectedTargetDeg * Deg, decision.TargetContentWorldAngleRadians);
        AssertWorldAngle(deltaDeg * Deg, decision.RotationDeltaRadians);
        AssertWorldAngle(currentDeg * Deg, decision.CurrentContentWorldAngleRadians);
    }

    [Fact]
    public void RotateRelative_RepeatedPlus45_AccumulatesThenWraps()
    {
        var angle = 0d;
        for (var i = 0; i < 8; i++)
        {
            var decision = TimberAnnotationTransformRules.Resolve(
                TimberAnnotationTransformRequest.RotateRelative(Math.PI / 4d),
                angle);
            angle = decision.TargetContentWorldAngleRadians;
        }

        AssertWorldAngle(0d, angle);
    }

    [Fact]
    public void RotateRelative_RepeatedPlus90_AccumulatesThenWraps()
    {
        var angle = 0d;
        for (var i = 0; i < 4; i++)
        {
            var decision = TimberAnnotationTransformRules.Resolve(
                TimberAnnotationTransformRequest.RotateRelative(Math.PI / 2d),
                angle);
            angle = decision.TargetContentWorldAngleRadians;
        }

        AssertWorldAngle(0d, angle);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(17d)]
    [InlineData(45d)]
    [InlineData(90d)]
    [InlineData(135d)]
    [InlineData(180d)]
    [InlineData(225d)]
    [InlineData(270d)]
    [InlineData(359d)]
    [InlineData(-45d)]
    [InlineData(-90d)]
    [InlineData(-180d)]
    public void Horizontal_SetsAbsoluteZero_AndIsIdempotent(double currentDeg)
    {
        var first = TimberAnnotationTransformRules.Resolve(
            TimberAnnotationTransformRequest.Horizontal(),
            currentDeg * Deg);
        AssertWorldAngle(0d, first.TargetContentWorldAngleRadians);

        var second = TimberAnnotationTransformRules.Resolve(
            TimberAnnotationTransformRequest.Horizontal(),
            first.TargetContentWorldAngleRadians);
        AssertWorldAngle(0d, second.TargetContentWorldAngleRadians);
        AssertWorldAngle(0d, second.RotationDeltaRadians);
        Assert.True(
            TimberAnnotationTransformRules.AreWorldAnglesEqual(
                second.CurrentContentWorldAngleRadians,
                second.TargetContentWorldAngleRadians));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(30d)]
    [InlineData(90d)]
    [InlineData(120d)]
    [InlineData(180d)]
    [InlineData(270d)]
    [InlineData(-45d)]
    [InlineData(450d)]
    public void Vertical_SetsAbsolutePlusNinety_AndIsIdempotent(double currentDeg)
    {
        var first = TimberAnnotationTransformRules.Resolve(
            TimberAnnotationTransformRequest.Vertical(),
            currentDeg * Deg);
        AssertWorldAngle(Math.PI / 2d, first.TargetContentWorldAngleRadians);

        var second = TimberAnnotationTransformRules.Resolve(
            TimberAnnotationTransformRequest.Vertical(),
            first.TargetContentWorldAngleRadians);
        AssertWorldAngle(Math.PI / 2d, second.TargetContentWorldAngleRadians);
        AssertWorldAngle(0d, second.RotationDeltaRadians);
    }

    [Theory]
    [InlineData(0d, 0d, 0d)]
    [InlineData(90d, 0d, -90d)]
    [InlineData(45d, 0d, -45d)]
    [InlineData(30d, 0d, -30d)]
    [InlineData(0d, 45d, 90d)]
    [InlineData(45d, 45d, 45d)]
    [InlineData(90d, 45d, 0d)]
    [InlineData(135d, 45d, -45d)]
    [InlineData(0d, 90d, 180d)]
    [InlineData(90d, 90d, 90d)]
    [InlineData(180d, 90d, 0d)]
    [InlineData(45d, 90d, 135d)]
    [InlineData(0d, 135d, -90d)]
    [InlineData(135d, 135d, 135d)]
    [InlineData(90d, 135d, 180d)]
    [InlineData(45d, 180d, -45d)]
    [InlineData(180d, 180d, 180d)]
    [InlineData(90d, 180d, -90d)]
    [InlineData(-45d, 0d, 45d)]
    [InlineData(350d, 0d, 10d)]
    [InlineData(370d, 10d, 10d)]
    [InlineData(359.999d, 0d, -359.999d)]
    public void MirrorContentAngle_MatchesTwoAlphaMinusTheta(
        double thetaDeg,
        double alphaDeg,
        double expectedDeg)
    {
        var mirrored = TimberAnnotationTransformRules.MirrorContentWorldAngleRadians(
            thetaDeg * Deg,
            alphaDeg * Deg);
        AssertWorldAngle(expectedDeg * Deg, mirrored);

        var decision = TimberAnnotationTransformRules.Resolve(
            TimberAnnotationTransformRequest.MirrorAcrossSourceAxis(),
            thetaDeg * Deg,
            alphaDeg * Deg);
        AssertWorldAngle(expectedDeg * Deg, decision.TargetContentWorldAngleRadians);
        Assert.Equal(
            TimberAnnotationTransformKind.MirrorAcrossSourceAxis,
            decision.Request.Kind);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(45d)]
    [InlineData(90d)]
    [InlineData(135d)]
    [InlineData(180d)]
    public void Mirror_WhenThetaEqualsSourceAxis_LeavesAngleUnchanged(
        double axisDeg)
    {
        var mirrored = TimberAnnotationTransformRules.MirrorContentWorldAngleRadians(
            axisDeg * Deg,
            axisDeg * Deg);
        AssertWorldAngle(axisDeg * Deg, mirrored);
    }

    [Theory]
    [InlineData(0d, 90d, -90d)]
    [InlineData(90d, 0d, 180d)]
    [InlineData(45d, 135d, -45d)]
    public void Mirror_WhenThetaPerpendicularToSourceAxis_ReflectsCorrectly(
        double alphaDeg,
        double thetaDeg,
        double expectedDeg)
    {
        AssertWorldAngle(
            expectedDeg * Deg,
            TimberAnnotationTransformRules.MirrorContentWorldAngleRadians(
                thetaDeg * Deg,
                alphaDeg * Deg));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(1e-12d)]
    [InlineData(-1e-12d)]
    [InlineData(360d)]
    [InlineData(-360d)]
    [InlineData(720d)]
    [InlineData(180d)]
    [InlineData(-180d)]
    [InlineData(90d)]
    [InlineData(270d)]
    [InlineData(-90d)]
    public void NormalizeWorldAngle_IsDeterministicAroundBoundaries(double degrees)
    {
        var radians = degrees * Deg;
        var once = TimberAnnotationTransformRules.NormalizeWorldAngleRadians(radians);
        var twice = TimberAnnotationTransformRules.NormalizeWorldAngleRadians(once);
        Assert.Equal(once, twice);
        // Interval is (−π, π]: Atan2 never returns −π; −π inputs map to +π.
        Assert.True(once > -Math.PI && once <= Math.PI);
    }

    [Fact]
    public void NormalizeWorldAngle_MapsNegativePiToPositivePi()
    {
        AssertWorldAngle(
            Math.PI,
            TimberAnnotationTransformRules.NormalizeWorldAngleRadians(-Math.PI));
    }

    [Fact]
    public void ReflectPoint_AcrossHorizontalAxis_FlipsY()
    {
        var reflected = TimberAnnotationTransformRules.ReflectPointAcrossAxis(
            new TimberPlanarPoint(3d, 4d),
            new TimberPlanarPoint(0d, 0d),
            0d);
        AssertPoint(new TimberPlanarPoint(3d, -4d), reflected);
    }

    [Fact]
    public void ReflectPoint_AcrossVerticalAxis_FlipsX()
    {
        var reflected = TimberAnnotationTransformRules.ReflectPointAcrossAxis(
            new TimberPlanarPoint(3d, 4d),
            new TimberPlanarPoint(0d, 0d),
            Math.PI / 2d);
        AssertPoint(new TimberPlanarPoint(-3d, 4d), reflected);
    }

    [Fact]
    public void ReflectPoint_AcrossFortyFiveDegreeAxis()
    {
        var reflected = TimberAnnotationTransformRules.ReflectPointAcrossAxis(
            new TimberPlanarPoint(2d, 0d),
            new TimberPlanarPoint(0d, 0d),
            Math.PI / 4d);
        AssertPoint(new TimberPlanarPoint(0d, 2d), reflected);
    }

    [Fact]
    public void ReflectPoint_OnAxis_StaysUnchanged()
    {
        var onAxis = new TimberPlanarPoint(5d, 5d);
        var reflected = TimberAnnotationTransformRules.ReflectPointAcrossAxis(
            onAxis,
            new TimberPlanarPoint(0d, 0d),
            Math.PI / 4d);
        AssertPoint(onAxis, reflected);
    }

    [Fact]
    public void ReflectPoint_UsesOffsetOrigin()
    {
        var reflected = TimberAnnotationTransformRules.ReflectPointAcrossAxis(
            new TimberPlanarPoint(4d, 6d),
            new TimberPlanarPoint(1d, 2d),
            0d);
        AssertPoint(new TimberPlanarPoint(4d, -2d), reflected);
    }

    [Fact]
    public void RequestFactories_ExposeGenericAnglesWithoutHardCodedOnlyButtons()
    {
        var custom = TimberAnnotationTransformRequest.RotateRelative(0.3d);
        Assert.Equal(TimberAnnotationTransformKind.RotateRelative, custom.Kind);
        Assert.Equal(0.3d, custom.AngleRadians);

        var absolute = TimberAnnotationTransformRequest.SetWorldOrientation(1.1d);
        Assert.Equal(TimberAnnotationTransformKind.SetWorldOrientation, absolute.Kind);
        Assert.Equal(1.1d, absolute.AngleRadians);

        var mirror = TimberAnnotationTransformRequest.MirrorAcrossSourceAxis();
        Assert.Equal(TimberAnnotationTransformKind.MirrorAcrossSourceAxis, mirror.Kind);
        Assert.Null(mirror.AngleRadians);

        Assert.Equal(0d, TimberAnnotationTransformRequest.Horizontal().AngleRadians);
        Assert.Equal(
            Math.PI / 2d,
            TimberAnnotationTransformRequest.Vertical().AngleRadians);
    }

    [Fact]
    public void Resolve_RejectsNonFiniteAngles()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberAnnotationTransformRequest.RotateRelative(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberAnnotationTransformRequest.SetWorldOrientation(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberAnnotationTransformRules.NormalizeWorldAngleRadians(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberAnnotationTransformRules.Resolve(
                TimberAnnotationTransformRequest.Horizontal(),
                double.NegativeInfinity));
    }

    [Fact]
    public void CoreTransformModel_HasNoAutodeskAssemblyDependency()
    {
        var references = typeof(TimberAnnotationTransformRules)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(references, name =>
            name.StartsWith("Autodesk.AutoCAD", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("AcMgd", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("AcDbMgd", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("AcCoreMgd", StringComparison.OrdinalIgnoreCase));

        var modelSource = File.ReadAllText(
            Path.Combine(
                FindRepoRoot(),
                "src",
                "AcKrovy.Core",
                "Models",
                "TimberAnnotationTransformRequest.cs"));
        var rulesSource = File.ReadAllText(
            Path.Combine(
                FindRepoRoot(),
                "src",
                "AcKrovy.Core",
                "Services",
                "TimberAnnotationTransformRules.cs"));
        Assert.DoesNotContain("Autodesk", modelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ObjectId", modelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Point3d", modelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Matrix3d", modelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MLeader", modelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Autodesk", rulesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ObjectId", rulesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Point3d", rulesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Matrix3d", rulesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MLeader", rulesSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "R3ReferencePresentationRevision",
            rulesSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TimberFramedBlockContentWholeAnnotationHalfTurnRules",
            rulesSource,
            StringComparison.Ordinal);
    }

    private static void AssertWorldAngle(double expected, double actual)
    {
        var expectedNormalized =
            TimberAnnotationTransformRules.NormalizeWorldAngleRadians(expected);
        Assert.True(
            TimberAnnotationTransformRules.AreWorldAnglesEqual(expectedNormalized, actual),
            $"Expected {expectedNormalized} rad, actual {actual} rad.");
    }

    private static void AssertPoint(TimberPlanarPoint expected, TimberPlanarPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ACAD_krovy.sln")) ||
                File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
