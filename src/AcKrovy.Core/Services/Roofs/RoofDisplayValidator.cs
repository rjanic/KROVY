using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Validates a selected roof's disposable display without mutating it.</summary>
public static class RoofDisplayValidator
{
    public static RoofDisplayValidationResult Validate(
        string ownerReference,
        IReadOnlyList<RoofDisplayEdge> expectedEdges,
        string expectedSignature,
        IReadOnlyList<RoofDisplayObservation> observations)
    {
        if (string.IsNullOrWhiteSpace(ownerReference))
        {
            throw new ArgumentException("Owner reference is required.", nameof(ownerReference));
        }
        if (expectedEdges is null)
        {
            throw new ArgumentNullException(nameof(expectedEdges));
        }
        if (expectedSignature is null)
        {
            throw new ArgumentNullException(nameof(expectedSignature));
        }
        if (observations is null)
        {
            throw new ArgumentNullException(nameof(observations));
        }
        if (expectedEdges.Count != SimpleGableRoofWireframe.EdgeCount)
        {
            throw new ArgumentException("Expected wireframe must contain seven edges.", nameof(expectedEdges));
        }
        if (observations.Count == 0)
        {
            return new RoofDisplayValidationResult(
                RoofDisplayState.Missing,
                RoofDisplayValidationIssue.MissingChild | RoofDisplayValidationIssue.MissingRole);
        }

        var issues = RoofDisplayValidationIssue.None;
        if (observations.Count < SimpleGableRoofWireframe.EdgeCount)
        {
            issues |= RoofDisplayValidationIssue.MissingChild;
        }
        else if (observations.Count > SimpleGableRoofWireframe.EdgeCount)
        {
            issues |= RoofDisplayValidationIssue.ExtraChild;
        }

        var expectedByRole = expectedEdges.ToDictionary(edge => edge.Role);
        var observedRoles = new HashSet<RoofDisplayEdgeRole>();
        foreach (var observation in observations)
        {
            if (!string.Equals(observation.OwnerReference, ownerReference, StringComparison.OrdinalIgnoreCase))
            {
                issues |= RoofDisplayValidationIssue.WrongOwner;
                continue;
            }
            if (!observation.IsNativeLine)
            {
                issues |= RoofDisplayValidationIssue.UnsupportedEntityType;
                continue;
            }
            if (observation.MetadataError != RoofDisplayDataDecodeError.None || observation.Data is null)
            {
                issues |= observation.MetadataError == RoofDisplayDataDecodeError.UnsupportedFutureSchema
                    ? RoofDisplayValidationIssue.UnsupportedFutureSchema
                    : RoofDisplayValidationIssue.MalformedMetadata;
                continue;
            }

            var data = observation.Data;
            if (!observedRoles.Add(data.Role))
            {
                issues |= RoofDisplayValidationIssue.DuplicateRole;
            }
            if (!IsFinite(observation.Segment))
            {
                issues |= RoofDisplayValidationIssue.NonFiniteGeometry;
                continue;
            }
            if (!expectedByRole.TryGetValue(data.Role, out var expected) ||
                !SegmentsEqual(expected.Segment, observation.Segment))
            {
                issues |= RoofDisplayValidationIssue.GeometryMismatch;
                if (!string.Equals(
                        data.GenerationSignature,
                        expectedSignature,
                        StringComparison.Ordinal))
                {
                    issues |= RoofDisplayValidationIssue.SignatureMismatch;
                }
            }
        }

        if (expectedByRole.Keys.Any(role => !observedRoles.Contains(role)))
        {
            issues |= RoofDisplayValidationIssue.MissingRole;
        }

        return new RoofDisplayValidationResult(
            issues == RoofDisplayValidationIssue.None
                ? RoofDisplayState.Current
                : RoofDisplayState.Stale,
            issues);
    }

    private static bool SegmentsEqual(RoofSegment3D expected, RoofSegment3D actual) =>
        PointsEqual(expected.Start, actual.Start) && PointsEqual(expected.End, actual.End) ||
        PointsEqual(expected.Start, actual.End) && PointsEqual(expected.End, actual.Start);

    private static bool PointsEqual(RoofPoint3D first, RoofPoint3D second)
    {
        const double tolerance = SimpleGableRoofGeometryTolerance.CoordinateToleranceMm;
        return Math.Abs(first.X - second.X) <= tolerance &&
               Math.Abs(first.Y - second.Y) <= tolerance &&
               Math.Abs(first.Z - second.Z) <= tolerance;
    }

    private static bool IsFinite(RoofSegment3D segment) =>
        IsFinite(segment.Start.X) && IsFinite(segment.Start.Y) && IsFinite(segment.Start.Z) &&
        IsFinite(segment.End.X) && IsFinite(segment.End.Y) && IsFinite(segment.End.Z);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
