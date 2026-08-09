namespace AcKrovy.Core.Services;



/// <summary>

/// Standalone Plain / DimensionsLeader / framed ItemOnly only.

/// Distinguishes (A) AK_LABELS / user annotation grip content-only preserve from

/// (B) source timber MOVE/STRETCH/ROTATE that must rebuild absolute CREATE

/// canonical placement via OrientAroundAnchor — not endpoint-only reattach

/// that preserved prior manual content offsets.

/// <para>

/// Detection uses persisted automatic content bookkeeping + physical Start→End

/// axis — not live attachment vs canonical, so a user-moved annotation stays

/// put across AK_LABELS when the source is unchanged.

/// </para>

/// Must not be used by R3 Combined production.

/// </summary>

public static class TimberStandaloneNativeLeaderSourceSyncRules

{

    public const double PlacementToleranceMm = 0.5d;



    public const double AngleToleranceRadians =

        TimberStandaloneNativeLeaderOrientationRules.AngleToleranceRadians;



    public static TimberStandaloneNativeLeaderSourceSyncDecision Evaluate(

        double? previousAutomaticTextX,

        double? previousAutomaticTextY,

        double? previousPhysicalRotationRadians,

        double newAutomaticTextX,

        double newAutomaticTextY,

        double newPhysicalRotationRadians,

        double placementToleranceMm = PlacementToleranceMm,

        double angleToleranceRadians = AngleToleranceRadians)

    {

        if (double.IsNaN(newAutomaticTextX) ||

            double.IsNaN(newAutomaticTextY) ||

            double.IsInfinity(newAutomaticTextX) ||

            double.IsInfinity(newAutomaticTextY) ||

            double.IsNaN(newPhysicalRotationRadians) ||

            double.IsInfinity(newPhysicalRotationRadians))

        {

            throw new ArgumentOutOfRangeException(

                nameof(newPhysicalRotationRadians),

                "Automatic text and physical rotation must be finite.");

        }



        // Legacy / first content-only write: seed Automatic* without treating as

        // source change so AK_LABELS does not rebuild a user-moved annotation.

        if (previousAutomaticTextX is null ||

            previousAutomaticTextY is null ||

            previousPhysicalRotationRadians is null)

        {

            return new TimberStandaloneNativeLeaderSourceSyncDecision(

                SourceGeometryChanged: false,

                RequiresCanonicalRebuild: false,

                RequiresOrientationSync: false,

                OrientationDeltaRadians: 0d);

        }



        var automaticMoved =

            Math.Abs(previousAutomaticTextX.Value - newAutomaticTextX) >

                placementToleranceMm ||

            Math.Abs(previousAutomaticTextY.Value - newAutomaticTextY) >

                placementToleranceMm;



        var previousPhysical =

            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(

                previousPhysicalRotationRadians.Value);

        var newPhysical =

            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(

                newPhysicalRotationRadians);

        var physicalDelta =

            TimberAnnotationReadabilityRules.NormalizeAngleDelta(

                newPhysical - previousPhysical);

        var axisChanged = Math.Abs(physicalDelta) > angleToleranceRadians;



        if (!automaticMoved && !axisChanged)

        {

            return new TimberStandaloneNativeLeaderSourceSyncDecision(

                SourceGeometryChanged: false,

                RequiresCanonicalRebuild: false,

                RequiresOrientationSync: false,

                OrientationDeltaRadians: 0d);

        }



        var previousTransform =

            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(

                previousPhysical);

        var newTransform =

            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(

                newPhysical);

        var orientationDelta =

            TimberAnnotationReadabilityRules.NormalizeAngleDelta(

                newTransform - previousTransform);

        var orientationChanged =

            Math.Abs(orientationDelta) > angleToleranceRadians;



        return new TimberStandaloneNativeLeaderSourceSyncDecision(

            SourceGeometryChanged: true,

            RequiresCanonicalRebuild: true,

            RequiresOrientationSync: orientationChanged,

            OrientationDeltaRadians: orientationDelta);

    }



    /// <summary>

    /// Rigid rotate of a world point about a pivot — retained for Core tests /

    /// diagnostics. Host standalone refresh no longer uses this for content

    /// preserve-on-stretch; source change rebuilds CREATE canonical instead.

    /// </summary>

    public static (double X, double Y) RotateAround(

        double x,

        double y,

        double pivotX,

        double pivotY,

        double deltaRadians)

    {

        if (double.IsNaN(x) || double.IsNaN(y) ||

            double.IsNaN(pivotX) || double.IsNaN(pivotY) ||

            double.IsNaN(deltaRadians) ||

            double.IsInfinity(x) || double.IsInfinity(y) ||

            double.IsInfinity(pivotX) || double.IsInfinity(pivotY) ||

            double.IsInfinity(deltaRadians))

        {

            throw new ArgumentOutOfRangeException(nameof(deltaRadians));

        }



        if (Math.Abs(deltaRadians) <= AngleToleranceRadians)

        {

            return (x, y);

        }



        var cos = Math.Cos(deltaRadians);

        var sin = Math.Sin(deltaRadians);

        var dx = x - pivotX;

        var dy = y - pivotY;

        return (

            pivotX + (dx * cos) - (dy * sin),

            pivotY + (dx * sin) + (dy * cos));

    }

}



public readonly record struct TimberStandaloneNativeLeaderSourceSyncDecision(

    bool SourceGeometryChanged,

    bool RequiresCanonicalRebuild,

    bool RequiresOrientationSync,

    double OrientationDeltaRadians);


