using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// HOST TEST A regression: existing AUTO without Elevation XRecord must never
/// initialize as synthetic 0/0/0 when physical pitch is known (e.g. 45°).
/// </summary>
public sealed class StructuralMemberElevationResolverHostRegressionTests
{
    private const double Tol = 0.001d;
    private const double PlanLengthMm = 4000d;
    private const double HeightMm = 160d;

    [Fact]
    public void ExistingAuto_WithoutElevationXRecord_45Degree_IsNotSyntheticZero()
    {
        // Upper-face eave at 0, ridge at 4000 over 4000 mm run → 45° (AUTO VH = roof plane).
        Assert.True(StructuralMemberElevationRules.TryDeriveFromUpperFaceEndpoints(
            upperFaceStartZMm: 0d,
            upperFaceEndZMm: 4000d,
            heightMm: HeightMm,
            planLengthMm: PlanLengthMm,
            displayReference: StructuralMemberElevationReferenceKind.OS,
            out var state));
        Assert.NotNull(state);
        Assert.False(
            Math.Abs(state!.AxisStartElevationMm) < Tol &&
            Math.Abs(state.AxisEndElevationMm) < Tol,
            "Must not invent synthetic 0/0 axis elevations for a pitched AUTO member.");
        var slope = StructuralMemberElevationRules.DeriveSlopeDegrees(
            state.AxisStartElevationMm, state.AxisEndElevationMm, PlanLengthMm);
        Assert.Equal(45d, Math.Abs(slope), precision: 5);
        Assert.True(StructuralMemberElevationRules.IsPlausibleForKnownPitch(state, PlanLengthMm, 45d));
    }

    [Fact]
    public void ExistingAuto_ReversedPlanDirection_StructuralLowerUpperCorrect()
    {
        // Fall reversed: Start is ridge (high), End is eave (low) → signed slope −45°.
        Assert.True(StructuralMemberElevationRules.TryDeriveFromUpperFaceEndpoints(
            upperFaceStartZMm: 4000d,
            upperFaceEndZMm: 0d,
            heightMm: HeightMm,
            planLengthMm: PlanLengthMm,
            displayReference: StructuralMemberElevationReferenceKind.OS,
            out var state));
        Assert.NotNull(state);
        var slope = StructuralMemberElevationRules.DeriveSlopeDegrees(
            state!.AxisStartElevationMm, state.AxisEndElevationMm, PlanLengthMm);
        Assert.Equal(-45d, slope, precision: 5);
        Assert.True(state.AxisStartElevationMm > state.AxisEndElevationMm);
        Assert.True(StructuralMemberElevationRules.IsStartTheEaveEnd(
            state.AxisEndElevationMm, state.AxisStartElevationMm));
        Assert.True(StructuralMemberElevationRules.IsPlausibleForKnownPitch(state, PlanLengthMm, 45d));
    }

    [Fact]
    public void SyntheticZero_AgainstKnown45Pitch_IsRejected()
    {
        var zero = StructuralMemberElevationRules.CreateSloped(0d, 0d);
        Assert.False(
            StructuralMemberElevationRules.IsPlausibleForKnownPitch(zero, PlanLengthMm, 45d),
            "Synthetic 0/0/0 must never be accepted for a known 45° member.");
    }

    [Fact]
    public void OsVhShOs_RoundTrip_NoDrift_NoGeometryChange()
    {
        Assert.True(StructuralMemberElevationRules.TryDeriveFromUpperFaceEndpoints(
            500d, 4500d, HeightMm, PlanLengthMm,
            StructuralMemberElevationReferenceKind.OS, out var state));
        Assert.NotNull(state);
        var slope0 = StructuralMemberElevationRules.DeriveSlopeDegrees(
            state!.AxisStartElevationMm, state.AxisEndElevationMm, PlanLengthMm);
        var haz = StructuralMemberElevationRules.HeightAxisZ(Math.Abs(slope0));

        var osStart = state.AxisStartElevationMm;
        var osEnd = state.AxisEndElevationMm;

        // OS → VH
        var vhStart = StructuralMemberElevationRules.ToDisplayElevationMm(
            osStart, StructuralMemberElevationReferenceKind.VH, HeightMm, haz);
        var vhEnd = StructuralMemberElevationRules.ToDisplayElevationMm(
            osEnd, StructuralMemberElevationReferenceKind.VH, HeightMm, haz);
        // VH → OS
        var osFromVhStart = StructuralMemberElevationRules.ToAxisElevationMm(
            vhStart, StructuralMemberElevationReferenceKind.VH, HeightMm, haz);
        var osFromVhEnd = StructuralMemberElevationRules.ToAxisElevationMm(
            vhEnd, StructuralMemberElevationReferenceKind.VH, HeightMm, haz);
        Assert.Equal(osStart, osFromVhStart, Tol);
        Assert.Equal(osEnd, osFromVhEnd, Tol);

        // OS → SH → OS
        var shStart = StructuralMemberElevationRules.ToDisplayElevationMm(
            osStart, StructuralMemberElevationReferenceKind.SH, HeightMm, haz);
        var shEnd = StructuralMemberElevationRules.ToDisplayElevationMm(
            osEnd, StructuralMemberElevationReferenceKind.SH, HeightMm, haz);
        var osFromShStart = StructuralMemberElevationRules.ToAxisElevationMm(
            shStart, StructuralMemberElevationReferenceKind.SH, HeightMm, haz);
        var osFromShEnd = StructuralMemberElevationRules.ToAxisElevationMm(
            shEnd, StructuralMemberElevationReferenceKind.SH, HeightMm, haz);
        Assert.Equal(osStart, osFromShStart, Tol);
        Assert.Equal(osEnd, osFromShEnd, Tol);

        // Full OS→VH→SH→OS
        var backOsStart = StructuralMemberElevationRules.ToAxisElevationMm(
            StructuralMemberElevationRules.ToDisplayElevationMm(
                StructuralMemberElevationRules.ToAxisElevationMm(
                    vhStart, StructuralMemberElevationReferenceKind.VH, HeightMm, haz),
                StructuralMemberElevationReferenceKind.SH, HeightMm, haz),
            StructuralMemberElevationReferenceKind.SH, HeightMm, haz);
        Assert.Equal(osStart, backOsStart, Tol);
    }

    [Fact]
    public void ResolverFailure_NeverReturnsSyntheticZeroAsSuccessToken()
    {
        // Empty/invalid inputs must fail — callers must not invent CreateSloped(0,0).
        Assert.False(StructuralMemberElevationRules.TryDeriveFromUpperFaceEndpoints(
            double.NaN, 0d, HeightMm, PlanLengthMm,
            StructuralMemberElevationReferenceKind.OS, out var failed));
        Assert.Null(failed);
        Assert.False(StructuralMemberElevationRules.IsPlausibleForKnownPitch(
            StructuralMemberElevationRules.CreateSloped(0d, 0d), PlanLengthMm, 45d));
    }

    [Fact]
    public void SourceContract_AkEditPath_DoesNotInventDefaultZero()
    {
        var commands = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src", "AcKrovy.AutoCAD", "Commands", "AcKrovyCommands.cs"));
        var workflow = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofOrdinaryElevationCommandWorkflow.cs"));
        Assert.DoesNotContain("default_zero", commands, StringComparison.Ordinal);
        Assert.DoesNotContain("default_zero", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateSloped(0d, 0d)", commands, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateSloped(0d, 0d)", workflow, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ACAD_KROVY.sln")) ||
                File.Exists(Path.Combine(dir.FullName, "AcKrovy.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, "src", "AcKrovy.AutoCAD")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
