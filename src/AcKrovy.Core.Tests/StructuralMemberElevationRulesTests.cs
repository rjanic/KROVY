using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Focused unit tests for StructuralMemberElevationRules.
/// Covers: lower+upper→slope, lower+slope→upper, upper+slope→lower,
/// 45°, 30°, negative elevation, zero, reversed direction,
/// equal endpoint elevation, endpoint semantic stability,
/// SH/OS/VH round-trips for 80×160 at 45° and 30°, another section,
/// mirrored/reversed frame, and UI-rounding drift guard.
/// </summary>
public sealed class StructuralMemberElevationRulesTests
{
    private const double Tol = 0.0001d; // 0.1 µm — should be well within floating-point precision

    // =========================================================================
    // 1–3. Basic calculation modes
    // =========================================================================

    [Fact]
    public void LowerZ_UpperZ_DerivesSlope()
    {
        // 3000 mm run, 3000 mm rise → 45°
        var slope = StructuralMemberElevationRules.DeriveSlopeDegrees(
            axisStartMm: 0d, axisEndMm: 3000d, planLengthMm: 3000d);
        Assert.Equal(45d, slope, precision: 6);
    }

    [Fact]
    public void LowerZ_Slope_DerivesUpperZ()
    {
        // start at 0, slope 45°, run 3000 mm → end at 3000
        var upper = StructuralMemberElevationRules.DeriveEndFromStartAndSlope(
            axisStartMm: 0d, planLengthMm: 3000d, slopeDegrees: 45d);
        Assert.Equal(3000d, upper, Tol);
    }

    [Fact]
    public void UpperZ_Slope_DerivesLowerZ()
    {
        // end at 3000, slope 45°, run 3000 mm → start at 0
        var lower = StructuralMemberElevationRules.DeriveStartFromEndAndSlope(
            axisEndMm: 3000d, planLengthMm: 3000d, slopeDegrees: 45d);
        Assert.Equal(0d, lower, Tol);
    }

    // =========================================================================
    // 4. 45° case
    // =========================================================================

    [Fact]
    public void Case_45Degrees_Roundtrip()
    {
        const double run = 4000d;
        const double rise = 4000d;
        var slope = StructuralMemberElevationRules.DeriveSlopeDegrees(0, rise, run);
        Assert.Equal(45d, slope, precision: 6);
        var upper2 = StructuralMemberElevationRules.DeriveEndFromStartAndSlope(0, run, slope);
        Assert.Equal(rise, upper2, Tol);
        var lower2 = StructuralMemberElevationRules.DeriveStartFromEndAndSlope(rise, run, slope);
        Assert.Equal(0d, lower2, Tol);
    }

    // =========================================================================
    // 5. 30° case
    // =========================================================================

    [Fact]
    public void Case_30Degrees_Roundtrip()
    {
        const double run = 5000d;
        var rise = run * Math.Tan(30d * Math.PI / 180d);
        var slope = StructuralMemberElevationRules.DeriveSlopeDegrees(0, rise, run);
        Assert.Equal(30d, slope, precision: 5);
        var upper2 = StructuralMemberElevationRules.DeriveEndFromStartAndSlope(0, run, slope);
        Assert.Equal(rise, upper2, Tol);
    }

    // =========================================================================
    // 6. Negative elevation
    // =========================================================================

    [Fact]
    public void NegativeElevation_SlopeCalculation()
    {
        // Start at −500 mm, end at +500 mm, run 1000 mm → 45°
        var slope = StructuralMemberElevationRules.DeriveSlopeDegrees(-500d, 500d, 1000d);
        Assert.Equal(45d, slope, precision: 6);
    }

    [Fact]
    public void NegativeElevation_BothNegative()
    {
        // Fully below datum: start −3000, end −1000, run 2000 → 45°
        var slope = StructuralMemberElevationRules.DeriveSlopeDegrees(-3000d, -1000d, 2000d);
        Assert.Equal(45d, slope, precision: 6);
    }

    // =========================================================================
    // 7. Exact zero
    // =========================================================================

    [Fact]
    public void ExactZero_ZeroSlope()
    {
        // Horizontal: start=0, end=0, run=5000 → slope=0, pitch=0
        var slope = StructuralMemberElevationRules.DeriveSlopeDegrees(0, 0, 5000d);
        Assert.Equal(0d, slope, Tol);
        var haz = StructuralMemberElevationRules.HeightAxisZ(0d);
        Assert.Equal(1d, haz, Tol);
    }

    // =========================================================================
    // 8. Reversed Plan2D direction (axis end < axis start)
    // =========================================================================

    [Fact]
    public void ReversedPlanDirection_NegativeSlope()
    {
        // Start at 3000, end at 0: downhill → negative slope
        var slope = StructuralMemberElevationRules.DeriveSlopeDegrees(3000d, 0d, 3000d);
        Assert.Equal(-45d, slope, precision: 6);
    }

    [Fact]
    public void ReversedPlanDirection_DeriveFromSlope()
    {
        // If slope is −45° and end is 0, start should be 3000
        var start = StructuralMemberElevationRules.DeriveStartFromEndAndSlope(0d, 3000d, -45d);
        Assert.Equal(3000d, start, Tol);
    }

    // =========================================================================
    // 9. Equal endpoint elevation (horizontal)
    // =========================================================================

    [Fact]
    public void EqualEndpointElevation_ZeroSlope()
    {
        var slope = StructuralMemberElevationRules.DeriveSlopeDegrees(1500d, 1500d, 3000d);
        Assert.Equal(0d, slope, Tol);
        // IsStartTheEaveEnd returns true when equal (deterministic)
        Assert.True(StructuralMemberElevationRules.IsStartTheEaveEnd(1500d, 1500d));
    }

    // =========================================================================
    // 10. Endpoint semantic stability (IsStartTheEaveEnd)
    // =========================================================================

    [Fact]
    public void EndpointSemantics_LowerIsEave()
    {
        Assert.True(StructuralMemberElevationRules.IsStartTheEaveEnd(100d, 3000d));
        Assert.False(StructuralMemberElevationRules.IsStartTheEaveEnd(3000d, 100d));
    }

    // =========================================================================
    // 11. SH → OS → VH → SH round-trip (generic)
    // =========================================================================

    [Fact]
    public void SH_OS_VH_RoundTrip_Generic()
    {
        const double heightMm = 200d;
        const double pitch = 30d;
        var haz = StructuralMemberElevationRules.HeightAxisZ(pitch);
        var osAxis = 1000d; // some arbitrary axis elevation

        // OS → VH
        var vh = StructuralMemberElevationRules.AxisToUpperFaceZ(osAxis, heightMm, haz);
        // VH → OS
        var osFromVh = StructuralMemberElevationRules.UpperFaceToAxisZ(vh, heightMm, haz);
        Assert.Equal(osAxis, osFromVh, Tol);

        // OS → SH
        var sh = StructuralMemberElevationRules.AxisToLowerFaceZ(osAxis, heightMm, haz);
        // SH → OS
        var osFromSh = StructuralMemberElevationRules.LowerFaceToAxisZ(sh, heightMm, haz);
        Assert.Equal(osAxis, osFromSh, Tol);

        // VH → OS → SH → OS → VH: must return to original VH
        var os2 = StructuralMemberElevationRules.UpperFaceToAxisZ(vh, heightMm, haz);
        var sh2 = StructuralMemberElevationRules.AxisToLowerFaceZ(os2, heightMm, haz);
        var os3 = StructuralMemberElevationRules.LowerFaceToAxisZ(sh2, heightMm, haz);
        var vh2 = StructuralMemberElevationRules.AxisToUpperFaceZ(os3, heightMm, haz);
        Assert.Equal(vh, vh2, Tol);
    }

    // =========================================================================
    // 12. 80×160 at 45° — section-specific round-trip
    // =========================================================================

    [Fact]
    public void Section80x160_At45Degrees_Roundtrip()
    {
        // widthMm = 80; not used directly in Z calculations but documents the section.
        const double heightMm = 160d;
        const double pitch = 45d;
        var haz = StructuralMemberElevationRules.HeightAxisZ(pitch); // cos(45°) ≈ 0.7071

        // Expected HeightAxisZ = cos(45°)
        Assert.Equal(Math.Cos(Math.PI / 4), haz, precision: 10);

        var axisZ = 2000d; // 2 m axis elevation

        var vh = StructuralMemberElevationRules.AxisToUpperFaceZ(axisZ, heightMm, haz);
        var sh = StructuralMemberElevationRules.AxisToLowerFaceZ(axisZ, heightMm, haz);

        // VH - SH = heightMm * HeightAxisZ (NOT naively heightMm in Z)
        Assert.Equal(heightMm * haz, vh - sh, Tol);

        // Round-trip
        Assert.Equal(axisZ, StructuralMemberElevationRules.UpperFaceToAxisZ(vh, heightMm, haz), Tol);
        Assert.Equal(axisZ, StructuralMemberElevationRules.LowerFaceToAxisZ(sh, heightMm, haz), Tol);
    }

    // =========================================================================
    // 13. 80×160 at 30° — section-specific round-trip
    // =========================================================================

    [Fact]
    public void Section80x160_At30Degrees_Roundtrip()
    {
        const double heightMm = 160d;
        const double pitch = 30d;
        var haz = StructuralMemberElevationRules.HeightAxisZ(pitch); // cos(30°) ≈ 0.866
        Assert.Equal(Math.Cos(Math.PI / 6), haz, precision: 10);

        var axisZ = 1500d;
        var vh = StructuralMemberElevationRules.AxisToUpperFaceZ(axisZ, heightMm, haz);
        var sh = StructuralMemberElevationRules.AxisToLowerFaceZ(axisZ, heightMm, haz);
        Assert.Equal(heightMm * haz, vh - sh, Tol);
        Assert.Equal(axisZ, StructuralMemberElevationRules.UpperFaceToAxisZ(vh, heightMm, haz), Tol);
        Assert.Equal(axisZ, StructuralMemberElevationRules.LowerFaceToAxisZ(sh, heightMm, haz), Tol);
    }

    // =========================================================================
    // 14. Another section height (120×200 at 20°)
    // =========================================================================

    [Fact]
    public void Section120x200_At20Degrees_Roundtrip()
    {
        const double heightMm = 200d;
        const double pitch = 20d;
        var haz = StructuralMemberElevationRules.HeightAxisZ(pitch);
        Assert.Equal(Math.Cos(20d * Math.PI / 180d), haz, precision: 10);

        var axisZ = -250d; // below datum
        var vh = StructuralMemberElevationRules.AxisToUpperFaceZ(axisZ, heightMm, haz);
        var sh = StructuralMemberElevationRules.AxisToLowerFaceZ(axisZ, heightMm, haz);
        Assert.Equal(heightMm * haz, vh - sh, Tol);
        Assert.Equal(axisZ, StructuralMemberElevationRules.UpperFaceToAxisZ(vh, heightMm, haz), Tol);
    }

    // =========================================================================
    // 15. Mirrored/reversed section frame (HeightAxisZ must be positive)
    // =========================================================================

    [Fact]
    public void HeightAxisZ_AlwaysPositiveForValidPitch()
    {
        foreach (var pitch in new[] { 0d, 10d, 20d, 30d, 45d, 60d, 80d })
        {
            var haz = StructuralMemberElevationRules.HeightAxisZ(pitch);
            Assert.True(haz > 0d, $"HeightAxisZ should be positive for pitch {pitch}°, got {haz}");
        }
    }

    // =========================================================================
    // 16. No UI-rounding drift (internal double precision)
    // =========================================================================

    [Fact]
    public void NoUIRoundingDrift_InternalPrecision()
    {
        // Simulate a round-trip through display formatting without precision loss.
        const double heightMm = 160d;
        const double pitch = 45d;
        var haz = StructuralMemberElevationRules.HeightAxisZ(pitch);

        // Start with an axis elevation that would display as a round number.
        var axisZ = 2500.123456789d; // NOT a round-metre number
        var vh = StructuralMemberElevationRules.AxisToUpperFaceZ(axisZ, heightMm, haz);
        var sh = StructuralMemberElevationRules.AxisToLowerFaceZ(axisZ, heightMm, haz);

        // The recovered axis from VH must be identical (not rounded to 3 decimal metres).
        var recovered = StructuralMemberElevationRules.UpperFaceToAxisZ(vh, heightMm, haz);
        Assert.Equal(axisZ, recovered, precision: 9); // double-precision tolerance
    }

    // =========================================================================
    // Validation
    // =========================================================================

    [Fact]
    public void IsValid_RejectsNull() => Assert.False(StructuralMemberElevationRules.IsValid(null));

    [Fact]
    public void IsValid_AcceptsValidSloped()
    {
        var state = StructuralMemberElevationRules.CreateSloped(0d, 3000d);
        Assert.True(StructuralMemberElevationRules.IsValid(state));
    }

    [Fact]
    public void IsValid_AcceptsValidUniform()
    {
        var state = StructuralMemberElevationRules.CreateUniform(1500d);
        Assert.True(StructuralMemberElevationRules.IsValid(state));
    }

    [Fact]
    public void IsValid_RejectsUniformWithDifferentEndpoints()
    {
        var state = new StructuralMemberElevationState(
            StructuralMemberElevationStateSchema.CurrentVersion,
            StructuralMemberElevationBehavior.UniformElevation,
            1000d, 2000d, // different endpoints on UniformElevation
            StructuralMemberElevationReferenceKind.OS);
        Assert.False(StructuralMemberElevationRules.IsValid(state));
    }

    [Fact]
    public void IsValid_RejectsNaNElevation()
    {
        var state = new StructuralMemberElevationState(
            StructuralMemberElevationStateSchema.CurrentVersion,
            StructuralMemberElevationBehavior.SlopedEndpoints,
            double.NaN, 0d,
            StructuralMemberElevationReferenceKind.OS);
        Assert.False(StructuralMemberElevationRules.IsValid(state));
    }

    // =========================================================================
    // Slope degree formatting
    // =========================================================================

    [Fact]
    public void FormatSlopeDegrees_Positive()
    {
        var text = StructuralMemberElevationRules.FormatSlopeDegrees(45d);
        Assert.Equal("45.00°", text);
    }

    [Fact]
    public void FormatSlopeDegrees_Negative_FormatsAsPositiveMagnitude()
    {
        // Fall direction is separate; display never uses a leading minus for slope.
        var text = StructuralMemberElevationRules.FormatSlopeDegrees(-30d);
        Assert.Equal("30.00°", text);
    }

    [Fact]
    public void TryParseSlopeDegrees_AcceptsComma()
    {
        Assert.True(StructuralMemberElevationRules.TryParseSlopeDegrees("45,00°", out var deg));
        Assert.Equal(45d, deg, precision: 5);
    }

    [Fact]
    public void TryParseSlopeDegrees_AcceptsDot()
    {
        Assert.True(StructuralMemberElevationRules.TryParseSlopeDegrees("30.00°", out var deg));
        Assert.Equal(30d, deg, precision: 5);
    }

    [Fact]
    public void TryParseSlopeDegrees_Rejects90()
    {
        Assert.False(StructuralMemberElevationRules.TryParseSlopeDegrees("90°", out _));
    }
}
