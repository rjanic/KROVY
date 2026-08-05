using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// ModelSpace dogleg/landing geometry for G5 BlockContent MLeaders.
/// Not a BTR variant concern — leader Left/Right share the same column-side BTR.
/// Dimension-column local-X side is a separate R2 Combined variant key.
///
/// Two separate signed quantities along annotation tangent T:
/// <list type="bullet">
/// <item>
/// <see cref="TryResolveLeaderKneeSide"/> =
/// sign(dot(knee − attachment, T)) → <see cref="TimberLeaderTangentSign"/>
/// </item>
/// <item>
/// <see cref="TryResolveContentDoglegSide"/> =
/// sign(dot(BlockPosition − knee, T)) → <see cref="TimberLeaderTangentSign"/>
/// </item>
/// </list>
/// Native <c>DoglegDirection</c> comes from BlockPosition − knee (ConnectBase
/// content placement), never from LeaderKneeSide alone. The cancelled rule
/// PositiveT → +T / NegativeT → −T (formerly Right/Left) is intentionally not used.
/// </summary>
public static class TimberFramedBlockContentDoglegRules
{
    /// <summary>
    /// Near-zero projection tolerance (mm) so side classification does not
    /// chatter when a point sits nearly on the tangent through the origin.
    /// </summary>
    public const double GeometricSideToleranceMm = 1.0d;

    /// <summary>
    /// Layout horizontal side sign: Right = +1, Left = −1 (world/local N).
    /// Distinct from <see cref="TimberLeaderTangentSign"/> projection signs.
    /// </summary>
    public static double SideSign(TimberLeaderHorizontalSide side) =>
        side == TimberLeaderHorizontalSide.Right ? 1d : -1d;

    public static double SideSign(TimberLeaderTangentSign sign) =>
        sign == TimberLeaderTangentSign.PositiveT ? 1d : -1d;

    public static TimberLeaderHorizontalSide Opposite(TimberLeaderHorizontalSide side) =>
        side == TimberLeaderHorizontalSide.Left
            ? TimberLeaderHorizontalSide.Right
            : TimberLeaderHorizontalSide.Left;

    public static TimberLeaderTangentSign Opposite(TimberLeaderTangentSign sign) =>
        sign == TimberLeaderTangentSign.NegativeT
            ? TimberLeaderTangentSign.PositiveT
            : TimberLeaderTangentSign.NegativeT;

    /// <summary>
    /// LeaderKneeSide: signed projection of (knee − attachment) onto T.
    /// PositiveT when dot &gt; 0, NegativeT when dot &lt; 0 — not screen Left/Right.
    /// </summary>
    public static bool TryResolveLeaderKneeSide(
        TimberPlanarPoint attachment,
        TimberPlanarPoint knee,
        TimberPlanarVector tangent,
        out TimberLeaderTangentSign side,
        double toleranceMm = GeometricSideToleranceMm) =>
        TryResolveSignedSide(
            knee.X - attachment.X,
            knee.Y - attachment.Y,
            tangent,
            out side,
            toleranceMm);

    /// <summary>
    /// ContentDoglegSide: signed projection of (BlockPosition − knee) onto T.
    /// PositiveT when dot &gt; 0, NegativeT when dot &lt; 0 — not screen Left/Right.
    /// </summary>
    public static bool TryResolveContentDoglegSide(
        TimberPlanarPoint knee,
        TimberPlanarPoint blockPosition,
        TimberPlanarVector tangent,
        out TimberLeaderTangentSign side,
        double toleranceMm = GeometricSideToleranceMm) =>
        TryResolveSignedSide(
            blockPosition.X - knee.X,
            blockPosition.Y - knee.Y,
            tangent,
            out side,
            toleranceMm);

    /// <summary>
    /// Backward-compatible alias for <see cref="TryResolveLeaderKneeSide"/>.
    /// </summary>
    public static bool TryResolveGeometricSide(
        TimberPlanarPoint attachment,
        TimberPlanarPoint knee,
        TimberPlanarVector tangent,
        out TimberLeaderTangentSign side,
        double toleranceMm = GeometricSideToleranceMm) =>
        TryResolveLeaderKneeSide(attachment, knee, tangent, out side, toleranceMm);

    /// <summary>
    /// Unit dogleg direction from content placement (BlockPosition − knee).
    /// </summary>
    public static bool TryResolveContentDoglegDirection(
        TimberPlanarPoint knee,
        TimberPlanarPoint blockPosition,
        out TimberPlanarVector doglegDirectionUnit)
    {
        var dx = blockPosition.X - knee.X;
        var dy = blockPosition.Y - knee.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            doglegDirectionUnit = default;
            return false;
        }

        doglegDirectionUnit = new TimberPlanarVector(dx / length, dy / length);
        return true;
    }

    /// <summary>
    /// True when attachment and BlockPosition lie on the same side of the knee
    /// along the landing axis (landing points toward / past the attachment).
    /// Good RIGHT and create layouts keep them on opposite sides.
    /// </summary>
    public static bool LandingPointsTowardAttachment(
        TimberPlanarPoint attachment,
        TimberPlanarPoint knee,
        TimberPlanarPoint blockPosition,
        double toleranceMm = GeometricSideToleranceMm)
    {
        if (toleranceMm < 0d ||
            double.IsNaN(toleranceMm) ||
            double.IsInfinity(toleranceMm))
        {
            throw new ArgumentOutOfRangeException(nameof(toleranceMm));
        }

        var ax = attachment.X - knee.X;
        var ay = attachment.Y - knee.Y;
        var bx = blockPosition.X - knee.X;
        var by = blockPosition.Y - knee.Y;
        var contentLength = Math.Sqrt((bx * bx) + (by * by));
        if (contentLength <= TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            return false;
        }

        var dot = (ax * bx) + (ay * by);
        return dot > toleranceMm * contentLength;
    }

    /// <summary>
    /// Mirror BlockPosition across the knee, preserving |BlockPosition − knee|
    /// (ConnectBase half-frame×scale offset included — never hard-coded).
    /// </summary>
    public static TimberPlanarPoint MirrorBlockPositionAcrossKnee(
        TimberPlanarPoint knee,
        TimberPlanarPoint blockPosition) =>
        new(
            (2d * knee.X) - blockPosition.X,
            (2d * knee.Y) - blockPosition.Y);

    /// <summary>
    /// Create-path dogleg: direction and BlockPosition from layout landing
    /// (BlockPosition − knee), never from LeaderKneeSide.
    /// </summary>
    public static bool TryResolveCreateDoglegGeometry(
        TimberPlanarPoint knee,
        TimberPlanarPoint landingEnd,
        out TimberPlanarVector doglegDirection,
        out TimberPlanarPoint blockPosition)
    {
        blockPosition = landingEnd;
        if (!TryResolveContentDoglegDirection(knee, landingEnd, out doglegDirection))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Normalize existing host geometry:
    /// <list type="bullet">
    /// <item>If landing points toward attachment → mirror BlockPosition across knee</item>
    /// <item>DoglegDirection := normalize(BlockPosition − knee)</item>
    /// <item>Never rewrite BlockPosition as knee + direction × DoglegLength
    /// (that drops the ConnectBase frame-base offset and drags the knee)</item>
    /// </list>
    /// Returns <c>changed</c> when mirrored; direction is always the native
    /// content direction for the resulting BlockPosition.
    /// </summary>
    public static bool TryNormalizeDoglegGeometry(
        TimberPlanarPoint attachment,
        TimberPlanarPoint knee,
        TimberPlanarPoint blockPosition,
        out TimberPlanarVector doglegDirection,
        out TimberPlanarPoint normalizedBlockPosition,
        out bool mirrored,
        double toleranceMm = GeometricSideToleranceMm)
    {
        doglegDirection = default;
        normalizedBlockPosition = blockPosition;
        mirrored = false;

        if (LandingPointsTowardAttachment(attachment, knee, blockPosition, toleranceMm))
        {
            normalizedBlockPosition = MirrorBlockPositionAcrossKnee(knee, blockPosition);
            mirrored = true;
        }

        return TryResolveContentDoglegDirection(
            knee,
            normalizedBlockPosition,
            out doglegDirection);
    }

    /// <summary>
    /// Measured ConnectBase content offset along the dogleg ray:
    /// |BlockPosition − knee| − DoglegLength. Native good RIGHT / G5C settle
    /// leaves a positive frame half-extent×BlockScale remainder — do not
    /// hard-code 400 or 461.538.
    /// </summary>
    public static double MeasureConnectBaseContentOffsetMm(
        TimberPlanarPoint knee,
        TimberPlanarPoint blockPosition,
        double doglegLengthMm)
    {
        var dx = blockPosition.X - knee.X;
        var dy = blockPosition.Y - knee.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        return distance - doglegLengthMm;
    }

    private static bool TryResolveSignedSide(
        double dx,
        double dy,
        TimberPlanarVector tangent,
        out TimberLeaderTangentSign side,
        double toleranceMm)
    {
        if (toleranceMm < 0d ||
            double.IsNaN(toleranceMm) ||
            double.IsInfinity(toleranceMm))
        {
            throw new ArgumentOutOfRangeException(nameof(toleranceMm));
        }

        var tLength = tangent.Length;
        if (tLength <= TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            side = default;
            return false;
        }

        var tx = tangent.X / tLength;
        var ty = tangent.Y / tLength;
        var projection = (dx * tx) + (dy * ty);
        if (Math.Abs(projection) <= toleranceMm)
        {
            side = default;
            return false;
        }

        side = projection > 0d
            ? TimberLeaderTangentSign.PositiveT
            : TimberLeaderTangentSign.NegativeT;
        return true;
    }
}
