namespace AcKrovy.Core.Models;

public enum TimberLeaderHorizontalSide
{
    Left,
    Right,
}

/// <summary>
/// Sign of a planar vector's projection onto an oriented annotation tangent T:
/// <c>PositiveT</c> when <c>dot(v, T) &gt; 0</c>, <c>NegativeT</c> when
/// <c>dot(v, T) &lt; 0</c>. Not world/screen Left/Right.
/// </summary>
public enum TimberLeaderTangentSign
{
    NegativeT,
    PositiveT,
}

public enum TimberLeaderVerticalSide
{
    Down,
    Up,
}

public sealed record TimberLeaderPlaneBasis(
    double HorizontalX,
    double HorizontalY,
    double VerticalX,
    double VerticalY)
{
    public static TimberLeaderPlaneBasis WorldXY { get; } = new(1d, 0d, 0d, 1d);

    /// <summary>
    /// Element-aligned annotation plane: +H along readable element axis,
    /// +V rotated 90° CCW (same convention as WorldXY for rotation 0).
    /// </summary>
    public static TimberLeaderPlaneBasis FromRotationRadians(double rotationRadians)
    {
        var horizontalX = Math.Cos(rotationRadians);
        var horizontalY = Math.Sin(rotationRadians);
        return new TimberLeaderPlaneBasis(
            horizontalX,
            horizontalY,
            -horizontalY,
            horizontalX);
    }
}

public sealed record TimberItemLeaderLayout(
    double AnchorX,
    double AnchorY,
    double KneeX,
    double KneeY,
    double ContentX,
    double ContentY,
    TimberLeaderHorizontalSide Side,
    double EnvelopeWidthMm,
    double EnvelopeHeightMm);
