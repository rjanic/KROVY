namespace AcKrovy.Core.Services;

/// <summary>
/// Grip lifecycle for R3 Combined content presentation (layer C).
/// Distinct from CREATE readable planning and from source-axis rotation refresh.
/// When the source physical axis is unchanged, knee STRETCH may reshape leader
/// geometry (A) and swap R3_RIGHT↔R3_LEFT (B). Layer C must then follow the
/// FINAL post-stretch landing (knee→frame) via
/// <see cref="TimberFramedBlockContentReadableOrientationRules.Decide"/> —
/// not restore the pre-grip CREATE presentation while the leader rotates.
/// Source-element length-only refresh remains a separate preserve path.
/// </summary>
public static class TimberFramedBlockContentGripPresentationRules
{
    public const double AngleToleranceRadians = 1e-9d;

    /// <summary>
    /// White radial matrix used for CREATE → knee STRETCH orientation proofs.
    /// </summary>
    public static IReadOnlyList<double> RadialSourceAnglesDegrees { get; } =
    [
        0d, 35d, -35d, 90d, 135d, 180d, 225d, 270d, 315d,
    ];

    /// <summary>
    /// True when source Start→End orientation is unchanged across a grip op
    /// (modulo 360°, FP tolerance). Annotation knee grip still runs; presentation
    /// syncs to final landing rather than locking the pre-grip angle.
    /// </summary>
    public static bool SourcePhysicalAxisUnchanged(
        double sourceAngleBeforeRadians,
        double sourceAngleAfterRadians,
        double toleranceRadians = AngleToleranceRadians)
    {
        if (double.IsNaN(sourceAngleBeforeRadians) ||
            double.IsInfinity(sourceAngleBeforeRadians) ||
            double.IsNaN(sourceAngleAfterRadians) ||
            double.IsInfinity(sourceAngleAfterRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceAngleBeforeRadians));
        }

        if (toleranceRadians < 0d ||
            double.IsNaN(toleranceRadians) ||
            double.IsInfinity(toleranceRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(toleranceRadians));
        }

        var delta = TimberAnnotationReadabilityRules.NormalizeAngleDelta(
            sourceAngleAfterRadians - sourceAngleBeforeRadians);
        return Math.Abs(delta) <= toleranceRadians;
    }

    /// <summary>
    /// Absolute presentation delta (radians) folded to (−π, π].
    /// </summary>
    public static double PresentationDeltaRadians(
        double presentationBeforeRadians,
        double presentationAfterRadians)
    {
        if (double.IsNaN(presentationBeforeRadians) ||
            double.IsInfinity(presentationBeforeRadians) ||
            double.IsNaN(presentationAfterRadians) ||
            double.IsInfinity(presentationAfterRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(presentationBeforeRadians));
        }

        return TimberAnnotationReadabilityRules.NormalizeAngleDelta(
            presentationAfterRadians - presentationBeforeRadians);
    }

    /// <summary>
    /// Annotation knee STRETCH with unchanged source axis: content presentation
    /// must sync to the final post-stretch landing orientation (may differ from
    /// the pre-grip CREATE angle when the landing rotates).
    /// </summary>
    public static bool MustSyncPresentationToFinalLandingAfterKneeGrip(
        double sourceAngleBeforeRadians,
        double sourceAngleAfterRadians,
        double toleranceRadians = AngleToleranceRadians) =>
        SourcePhysicalAxisUnchanged(
            sourceAngleBeforeRadians,
            sourceAngleAfterRadians,
            toleranceRadians);

    /// <summary>
    /// Obsolete name retained for call-site migration; prefer
    /// <see cref="MustSyncPresentationToFinalLandingAfterKneeGrip"/>.
    /// </summary>
    public static bool MustPreservePresentationAfterKneeGrip(
        double sourceAngleBeforeRadians,
        double sourceAngleAfterRadians,
        double toleranceRadians = AngleToleranceRadians) =>
        MustSyncPresentationToFinalLandingAfterKneeGrip(
            sourceAngleBeforeRadians,
            sourceAngleAfterRadians,
            toleranceRadians);

    public static bool PresentationPreserved(
        double presentationBeforeRadians,
        double presentationAfterRadians,
        double toleranceRadians = AngleToleranceRadians)
    {
        if (toleranceRadians < 0d ||
            double.IsNaN(toleranceRadians) ||
            double.IsInfinity(toleranceRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(toleranceRadians));
        }

        return Math.Abs(
                   PresentationDeltaRadians(
                       presentationBeforeRadians,
                       presentationAfterRadians)) <=
               toleranceRadians;
    }

    /// <summary>
    /// Physical landing axis from final post-stretch knee→frame (second segment).
    /// </summary>
    public static bool TryResolveLandingPhysicalAngleRadians(
        double kneeX,
        double kneeY,
        double frameX,
        double frameY,
        out double landingPhysicalAngleRadians)
    {
        landingPhysicalAngleRadians = 0d;
        var dx = frameX - kneeX;
        var dy = frameY - kneeY;
        if (Math.Sqrt((dx * dx) + (dy * dy)) <=
            TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            return false;
        }

        landingPhysicalAngleRadians =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                Math.Atan2(dy, dx));
        return true;
    }

    /// <summary>
    /// Layer C presentation after knee STRETCH: Decide() from the final landing
    /// physical axis so frame/ITEM_NO/WIDTH/HEIGHT share the leader's orientation
    /// (readability half-plane [−90°, +90°] in harmony with that landing).
    /// </summary>
    public static bool TryResolvePresentationFromFinalLandingRadians(
        double kneeX,
        double kneeY,
        double frameX,
        double frameY,
        out double presentationRadians)
    {
        presentationRadians = 0d;
        if (!TryResolveLandingPhysicalAngleRadians(
                kneeX,
                kneeY,
                frameX,
                frameY,
                out var landingPhysical))
        {
            return false;
        }

        presentationRadians =
            TimberFramedBlockContentReadableOrientationRules
                .Decide(landingPhysical)
                .PresentationAngle;
        return true;
    }

    /// <summary>
    /// True when live presentation matches Decide(final landing).
    /// </summary>
    public static bool PresentationFollowsFinalLanding(
        double presentationRadians,
        double kneeX,
        double kneeY,
        double frameX,
        double frameY,
        double toleranceRadians = AngleToleranceRadians)
    {
        if (!TryResolvePresentationFromFinalLandingRadians(
                kneeX,
                kneeY,
                frameX,
                frameY,
                out var expected))
        {
            return false;
        }

        return PresentationPreserved(
            presentationRadians,
            expected,
            toleranceRadians);
    }

    /// <summary>
    /// Resolve the R3 post-grip content transform in the coordinate space used
    /// by <c>MLeader.BlockRotation</c>. CREATE may already carry a world-space
    /// basis from <c>TransformBy</c> while BlockRotation remains zero, so the
    /// desired world angle must be installed as a delta from the measured live
    /// content axis. Assigning the desired world angle directly would compose it
    /// with that existing basis and double-rotate frame and attributes.
    /// </summary>
    public static R3FinalContentPresentationDecision ResolveFinalContentPresentation(
        double currentWorldPresentationRadians,
        double currentBlockRotationRadians,
        double finalLandingPhysicalAngleRadians,
        bool preserveAdoptedReferenceVerticalFamily = false)
    {
        ValidateFinite(
            currentWorldPresentationRadians,
            nameof(currentWorldPresentationRadians));
        ValidateFinite(
            currentBlockRotationRadians,
            nameof(currentBlockRotationRadians));
        ValidateFinite(
            finalLandingPhysicalAngleRadians,
            nameof(finalLandingPhysicalAngleRadians));

        var currentWorld =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                currentWorldPresentationRadians);
        var currentBlock =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                currentBlockRotationRadians);
        var presentation =
            TimberFramedBlockContentReadableOrientationRules.Decide(
                finalLandingPhysicalAngleRadians);
        // Exact +90° and -90° are the same readable boundary candidates. Once
        // the persisted reference revision proves that CREATE adopted one
        // family, a knee grip ending exactly on that boundary must retain the
        // live family instead of blindly canonicalizing +90° back to -90°.
        // Non-boundary landings always keep the ordinary Decide(landing) path.
        var desiredPresentation =
            preserveAdoptedReferenceVerticalFamily &&
            IsExactVerticalBoundary(presentation.PhysicalAxisAngle) &&
            IsExactVerticalBoundary(currentWorld)
                ? currentWorld
                : presentation.PresentationAngle;
        var correction =
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                desiredPresentation - currentWorld);
        var targetBlock =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                currentBlock + correction);
        var existingBase =
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                currentWorld - currentBlock);
        var finalWorld =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                currentWorld + correction);
        var readableFlip = Math.Abs(
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                desiredPresentation - presentation.PhysicalAxisAngle)) >
            AngleToleranceRadians;
        var incomingLandingSide = readableFlip
            ? TimberFramedCombinedG5ContentVariantRules.LeftColumnSide
            : TimberFramedCombinedG5ContentVariantRules.RightColumnSide;

        return new R3FinalContentPresentationDecision(
            LandingPhysicalAngle: presentation.PhysicalAxisAngle,
            CurrentWorldPresentationAngle: currentWorld,
            ExistingContentBaseAngle: existingBase,
            CurrentBlockRotation: currentBlock,
            BlockRotationCorrection: correction,
            TargetBlockRotation: targetBlock,
            FinalWorldPresentationAngle: finalWorld,
            ReadableFlip: readableFlip,
            IncomingLandingSide: incomingLandingSide);
    }

    private static bool IsExactVerticalBoundary(double radians)
    {
        var physical = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            radians);
        return Math.Abs(Math.Abs(physical) - (Math.PI / 2d)) <=
            AngleToleranceRadians;
    }

    /// <summary>
    /// Resolve the world presentation angle from live BlockRotation vs AttrRef
    /// (CREATE BR≈0+AttrRef vs refresh BR=presentation+AttrRef≈0). Used by
    /// source-stretch capture and BTR-swap restore — not as knee-grip authority.
    /// </summary>
    public static double ResolvePreservedPresentationRadians(
        double preGripBlockRotationRadians,
        double? preGripItemAttributeRotationRadians,
        double angularToleranceRadians = AngleToleranceRadians)
    {
        if (double.IsNaN(preGripBlockRotationRadians) ||
            double.IsInfinity(preGripBlockRotationRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(preGripBlockRotationRadians));
        }

        if (angularToleranceRadians < 0d ||
            double.IsNaN(angularToleranceRadians) ||
            double.IsInfinity(angularToleranceRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(angularToleranceRadians));
        }

        var block = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            preGripBlockRotationRadians);
        if (preGripItemAttributeRotationRadians is null)
        {
            return block;
        }

        var attribute = preGripItemAttributeRotationRadians.Value;
        if (double.IsNaN(attribute) || double.IsInfinity(attribute))
        {
            throw new ArgumentOutOfRangeException(
                nameof(preGripItemAttributeRotationRadians));
        }

        var attr = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            attribute);
        if (Math.Abs(block) <= angularToleranceRadians)
        {
            return attr;
        }

        if (Math.Abs(attr) <= angularToleranceRadians)
        {
            return block;
        }

        // Both nonzero: CREATE TransformBy path keeps BR near-stale; AttrRef wins.
        return attr;
    }

    /// <summary>
    /// Expected CREATE presentation for a source axis (planning only).
    /// </summary>
    public static double ExpectedCreatePresentationRadians(
        double sourcePhysicalAxisRadians) =>
        TimberFramedBlockContentReadableOrientationRules
            .Decide(sourcePhysicalAxisRadians)
            .PresentationAngle;

    private static void ValidateFinite(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

/// <summary>
/// World-space R3 knee-grip presentation plus the relative BlockRotation needed
/// to install it on top of the live TransformBy/content basis.
/// </summary>
public sealed record R3FinalContentPresentationDecision(
    double LandingPhysicalAngle,
    double CurrentWorldPresentationAngle,
    double ExistingContentBaseAngle,
    double CurrentBlockRotation,
    double BlockRotationCorrection,
    double TargetBlockRotation,
    double FinalWorldPresentationAngle,
    bool ReadableFlip,
    AcKrovy.Core.Models.TimberFramedBlockContentDimensionColumnSide
        IncomingLandingSide);
