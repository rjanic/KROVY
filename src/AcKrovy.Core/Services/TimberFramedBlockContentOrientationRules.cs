using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Effective G5 BlockContent world orientation. Create builds a horizontal
/// MLeader (<c>BlockRotation = 0</c>), applies attachment-pivot TransformBy for
/// leader geometry (layer A), and keeps <c>BlockRotation = 0</c> afterward
/// (G5C contract — presentation comes from TransformBy). Refresh paths that
/// rebuild points without TransformBy may set presentation via
/// <see cref="TimberFramedBlockContentReadableOrientationRules.Decide"/>.
/// Classifiers may still recover upright angle from AttrRef.Rotation when
/// BlockRotation is stale 0 after TransformBy-only paths.
/// </summary>
public static class TimberFramedBlockContentOrientationRules
{
    /// <summary>
    /// Create-path upright rotation (same fold as FullLabel / layout calculator).
    /// </summary>
    public static double ResolveEffectiveBlockContentRotationRadians(
        double worldOrientationRadians) =>
        TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
            worldOrientationRadians);

    /// <summary>
    /// Prefer AttrRef world rotation when BlockRotation is near-zero but the
    /// attribute shows a non-trivial upright orientation (exact 90° host case).
    /// When both agree within tolerance, either is fine; attribute wins on tie
    /// break so TransformBy-applied orientation is recovered.
    /// </summary>
    public static double ResolveEffectiveBlockContentRotationRadians(
        double blockRotationRadians,
        double? attributeRotationRadians,
        double angularToleranceRadians = 1e-9d)
    {
        if (double.IsNaN(blockRotationRadians) ||
            double.IsInfinity(blockRotationRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(blockRotationRadians));
        }

        if (attributeRotationRadians is null)
        {
            return ResolveEffectiveBlockContentRotationRadians(blockRotationRadians);
        }

        var attribute = attributeRotationRadians.Value;
        if (double.IsNaN(attribute) || double.IsInfinity(attribute))
        {
            throw new ArgumentOutOfRangeException(nameof(attributeRotationRadians));
        }

        if (angularToleranceRadians < 0d ||
            double.IsNaN(angularToleranceRadians) ||
            double.IsInfinity(angularToleranceRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(angularToleranceRadians));
        }

        var block = ResolveEffectiveBlockContentRotationRadians(blockRotationRadians);
        var attr = ResolveEffectiveBlockContentRotationRadians(attribute);
        var delta = Math.Abs(
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(attr - block));
        if (delta <= angularToleranceRadians)
        {
            return attr;
        }

        // Stale BlockRotation=0 after TransformBy: trust AttrRef.
        if (Math.Abs(block) <= angularToleranceRadians &&
            Math.Abs(attr) > angularToleranceRadians)
        {
            return attr;
        }

        // Prefer attribute as AutoCAD's effective content orientation.
        return attr;
    }

    public static TimberPlanarVector ResolveEffectiveBlockLocalXAxis(
        double effectiveRotationRadians)
    {
        if (double.IsNaN(effectiveRotationRadians) ||
            double.IsInfinity(effectiveRotationRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveRotationRadians));
        }

        // Caller supplies create-path / AttrRef effective angle — do not fold again
        // (near-cardinal 90.001° after one readability pass is still slightly > π/2).
        return new TimberPlanarVector(
            Math.Cos(effectiveRotationRadians),
            Math.Sin(effectiveRotationRadians));
    }

    public static TimberPlanarVector ResolveEffectiveBlockLocalXAxis(
        double blockRotationRadians,
        double? attributeRotationRadians,
        double angularToleranceRadians = 1e-9d)
    {
        var theta = ResolveEffectiveBlockContentRotationRadians(
            blockRotationRadians,
            attributeRotationRadians,
            angularToleranceRadians);
        return ResolveEffectiveBlockLocalXAxis(theta);
    }

    /// <summary>
    /// Classify required DIMNX/DIMPX side from world landing
    /// <c>BlockPosition − knee</c> projected onto the effective content local +X.
    /// Degenerate only when landing length is below geometric tolerance — not when
    /// projection onto a wrong (BlockRotation=0) axis is ~0.
    /// </summary>
    public static bool TryClassifyRequiredDimensionColumnSide(
        double landingWorldX,
        double landingWorldY,
        double effectiveLocalXAxisX,
        double effectiveLocalXAxisY,
        double geometricToleranceMm,
        out TimberFramedBlockContentDimensionColumnSide requiredSide,
        out double contentLocalX,
        out double landingLength,
        out TimberFramedBlockContentLandingClassifyFailure failure)
    {
        requiredSide = default;
        contentLocalX = 0d;
        landingLength = 0d;
        failure = TimberFramedBlockContentLandingClassifyFailure.None;

        if (double.IsNaN(landingWorldX) ||
            double.IsInfinity(landingWorldX) ||
            double.IsNaN(landingWorldY) ||
            double.IsInfinity(landingWorldY) ||
            double.IsNaN(effectiveLocalXAxisX) ||
            double.IsInfinity(effectiveLocalXAxisX) ||
            double.IsNaN(effectiveLocalXAxisY) ||
            double.IsInfinity(effectiveLocalXAxisY) ||
            double.IsNaN(geometricToleranceMm) ||
            double.IsInfinity(geometricToleranceMm) ||
            geometricToleranceMm < 0d)
        {
            failure = TimberFramedBlockContentLandingClassifyFailure.DegenerateLandingLength;
            return false;
        }

        landingLength = Math.Sqrt(
            (landingWorldX * landingWorldX) + (landingWorldY * landingWorldY));
        if (landingLength <= geometricToleranceMm)
        {
            failure = TimberFramedBlockContentLandingClassifyFailure.DegenerateLandingLength;
            return false;
        }

        var axisLength = Math.Sqrt(
            (effectiveLocalXAxisX * effectiveLocalXAxisX) +
            (effectiveLocalXAxisY * effectiveLocalXAxisY));
        if (axisLength <= geometricToleranceMm)
        {
            failure = TimberFramedBlockContentLandingClassifyFailure
                .EffectiveOrientationMismatch;
            return false;
        }

        var unitX = effectiveLocalXAxisX / axisLength;
        var unitY = effectiveLocalXAxisY / axisLength;
        contentLocalX = (landingWorldX * unitX) + (landingWorldY * unitY);

        if (Math.Abs(contentLocalX) <= geometricToleranceMm)
        {
            failure = TimberFramedBlockContentLandingClassifyFailure
                .EffectiveOrientationMismatch;
            return false;
        }

        // towardKnee = −landing; projection sign matches DefinitionRules:
        // contentLocalX > 0 → NegativeLocalX (column toward knee).
        requiredSide = TimberFramedBlockContentDefinitionRules
            .ResolveDimensionColumnSideFromContentLocalX(contentLocalX);
        return true;
    }

    public static string DescribeClassifyFailure(
        TimberFramedBlockContentLandingClassifyFailure failure) =>
        failure switch
        {
            TimberFramedBlockContentLandingClassifyFailure.DegenerateLandingLength =>
                "Degenerate BlockPosition − knee (landing length ~ 0).",
            TimberFramedBlockContentLandingClassifyFailure.EffectiveOrientationMismatch =>
                "Effective content orientation mismatch.",
            _ => "Landing classify failed.",
        };
}
