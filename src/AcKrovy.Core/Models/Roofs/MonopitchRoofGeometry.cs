using System.Globalization;

namespace AcKrovy.Core.Models.Roofs;

/// <summary>Deterministic one-plane roof geometry directed from LOW to HIGH.</summary>
public sealed class MonopitchRoofGeometry : IRoofGeometry
{
    internal MonopitchRoofGeometry(
        RoofSegment3D lowEave,
        RoofSegment3D highEave,
        IReadOnlyList<RoofPoint3D> boundaryPoints,
        RoofDirection2D lowToHighDirection,
        double spanMm,
        double slopeDegrees,
        double eaveHeightDifferenceMm)
    {
        LowEave = lowEave;
        HighEave = highEave;
        BoundaryPoints = boundaryPoints.ToArray();
        LowToHighDirection = lowToHighDirection;
        SpanMm = spanMm;
        SlopeDegrees = slopeDegrees;
        EaveHeightDifferenceMm = eaveHeightDifferenceMm;
        Signature = BuildSignature();
    }

    public RoofKind Kind => RoofKind.Monopitch;

    public RoofSegment3D LowEave { get; }

    public RoofSegment3D HighEave { get; }

    /// <summary>One canonical plane boundary: low start/end, high end/start.</summary>
    public IReadOnlyList<RoofPoint3D> BoundaryPoints { get; }

    public RoofDirection2D LowToHighDirection { get; }

    public RoofDirection2D OrientationDirection => LowToHighDirection;

    public double SpanMm { get; }

    public double SlopeDegrees { get; }

    public double PrimarySlopeDegrees => SlopeDegrees;

    /// <summary>Positive physical elevation rise from LOW to HIGH.</summary>
    public double EaveHeightDifferenceMm { get; }

    public string Signature { get; }

    private string BuildSignature()
    {
        var values = new[]
        {
            (double)Kind,
            LowToHighDirection.X,
            LowToHighDirection.Y,
            SpanMm,
            SlopeDegrees,
            EaveHeightDifferenceMm,
        }.Concat(BoundaryPoints.SelectMany(point => new[] { point.X, point.Y, point.Z }));
        return string.Join(
            ";",
            values.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
    }
}
