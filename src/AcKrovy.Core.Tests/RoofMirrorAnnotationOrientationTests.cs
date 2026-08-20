using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Behavioral proof that the canonical annotation-orientation rules keep a mirrored timber
/// child's annotation readable. MIRROR reflects the member's physical Start->End axis;
/// the canonical resolver must fold that reflected axis back into the readable half-plane
/// so the label never reads upside-down. Repeated MIRROR must not alternate orientation.
/// This reuses the existing shared rules (no mirror-specific angle algorithm).
/// </summary>
public sealed class RoofMirrorAnnotationOrientationTests
{
    private const double HalfPi = Math.PI / 2d;

    [Fact]
    public void TextPresentation_IsAlwaysCanonical_ForAnyMirroredAxis()
    {
        // Mirroring reflects the member axis (θ -> π-θ vertical mirror, θ -> -θ horizontal
        // mirror). For any physical axis (including all reflections), the resolved text
        // presentation must be in the canonical readable half-plane [-π/2, π/2], with exact
        // vertical (90°/270°) pinned to +π/2 (BOTTOM->TOP). No axis may resolve upside-down.
        for (var degrees = 0; degrees < 360; degrees++)
        {
            var axis = degrees * Math.PI / 180d;
            var resolved = TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(axis);
            Assert.InRange(resolved, -HalfPi - 1e-9, HalfPi + 1e-9);

            // Exact vertical must converge to +π/2 (BOTTOM->TOP), never -π/2.
            if (TimberStandaloneNativeLeaderOrientationRules.IsExactVertical(axis))
            {
                Assert.Equal(HalfPi, resolved, 12);
            }
        }
    }

    [Fact]
    public void GeometryTransform_IsAlwaysCanonical_ForAnyMirroredAxis()
    {
        for (var degrees = 0; degrees < 360; degrees++)
        {
            var axis = degrees * Math.PI / 180d;
            var resolved = TimberStandaloneNativeLeaderOrientationRules
                .ResolveTransformRadians(axis);
            Assert.InRange(resolved, -HalfPi - 1e-9, HalfPi + 1e-9);
        }
    }

    [Fact]
    public void RepeatedMirror_DoesNotAlternateOrientation()
    {
        // Resolving is idempotent: applying the canonical fold to an already-canonical
        // value returns the same canonical value, so mirror-of-mirror never alternates
        // readable / upside-down.
        for (var degrees = 0; degrees < 360; degrees += 7)
        {
            var axis = degrees * Math.PI / 180d;
            var once = TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(axis);
            var twice = TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(once);
            Assert.Equal(once, twice, 12);
        }
    }

    [Fact]
    public void VerticalMemberMirroredLeftRight_RemainsReadable()
    {
        // Vertical member at +90°, and its mirror reflections (across vertical / horizontal)
        // that land on 270°/-90°, all resolve to the canonical +π/2 BOTTOM->TOP.
        Assert.Equal(HalfPi, ResolveText(90d), 12);
        Assert.Equal(HalfPi, ResolveText(270d), 12);
        Assert.Equal(HalfPi, ResolveText(-90d), 12);
    }

    [Fact]
    public void HorizontalMemberMirrored_RemainsReadable()
    {
        // Horizontal member at 0° and its mirror image at 180° both fold into the readable
        // half-plane (0° / -180°->0°).
        var horizontal = ResolveText(0d);
        var mirroredHorizontal = ResolveText(180d);
        Assert.InRange(horizontal, -HalfPi, HalfPi);
        Assert.InRange(mirroredHorizontal, -HalfPi, HalfPi);
    }

    [Fact]
    public void ArbitraryAngleMemberMirrored_RemainsReadable()
    {
        foreach (var angle in new[] { 15d, 45d, 135d, 200d, 300d, 350d })
        {
            var resolved = ResolveText(angle);
            Assert.InRange(resolved, -HalfPi - 1e-9, HalfPi + 1e-9);
            Assert.True(
                TimberStandaloneNativeLeaderOrientationRules.IsCanonicalTextPresentation(
                    angle * Math.PI / 180d,
                    resolved));
        }
    }

    [Fact]
    public void CanonicalDiffersByPi_OnlyWhenRequiredByReadability()
    {
        // The resolver applies exactly ±π folding to reach the readable half-plane; it never
        // introduces an arbitrary rotation. A member already readable (e.g. 30°) must not be
        // changed by π.
        var thirty = 30d * Math.PI / 180d;
        var resolvedThirty = TimberStandaloneNativeLeaderOrientationRules
            .ResolveTextPresentationRadians(thirty);
        Assert.Equal(thirty, resolvedThirty, 12);
    }

    private static double ResolveText(double degrees) =>
        TimberStandaloneNativeLeaderOrientationRules
            .ResolveTextPresentationRadians(degrees * Math.PI / 180d);
}
