namespace AcKrovy.Core.Models;

public enum TimberLeaderHorizontalSide
{
    Left,
    Right,
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
