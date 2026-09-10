using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>CAD-neutral creation, parsing, and strict validation rules.</summary>
public static class RoofBoundaryIdentityRules
{
    public const string ClockwiseToken = "CW";
    public const string CounterClockwiseToken = "CCW";

    public static RoofBoundaryIdentityValidationResult CreateSequential(
        int physicalSegmentCount,
        RoofPolygonOrientation rawWinding) =>
        Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            physicalSegmentCount,
            FormatWinding(rawWinding),
            Enumerable.Range(1, Math.Max(0, physicalSegmentCount)).ToArray());

    public static RoofBoundaryIdentityValidationResult Validate(
        int schemaVersion,
        int physicalSegmentCount,
        string? rawWindingToken,
        IReadOnlyList<int>? boundaryEdgeIds)
    {
        if (schemaVersion != RoofBoundaryIdentitySchema.CurrentVersion)
        {
            return Invalid(RoofBoundaryIdentityError.UnsupportedSchemaVersion);
        }

        if (physicalSegmentCount <= 0)
        {
            return Invalid(RoofBoundaryIdentityError.InvalidPhysicalSegmentCount);
        }

        if (!TryParseWinding(rawWindingToken, out var rawWinding))
        {
            return Invalid(RoofBoundaryIdentityError.MalformedWindingToken);
        }

        if (boundaryEdgeIds is null)
        {
            return Invalid(RoofBoundaryIdentityError.IncompletePayload);
        }

        if (boundaryEdgeIds.Count != physicalSegmentCount)
        {
            return Invalid(RoofBoundaryIdentityError.BoundaryEdgeIdCountMismatch);
        }

        if (boundaryEdgeIds.Any(id => id <= 0))
        {
            return Invalid(RoofBoundaryIdentityError.NonPositiveBoundaryEdgeId);
        }

        if (boundaryEdgeIds.Distinct().Count() != boundaryEdgeIds.Count)
        {
            return Invalid(RoofBoundaryIdentityError.DuplicateBoundaryEdgeId);
        }

        return new RoofBoundaryIdentityValidationResult(
            true,
            new RoofBoundaryIdentity(
                schemaVersion,
                physicalSegmentCount,
                rawWinding,
                Array.AsReadOnly(boundaryEdgeIds.ToArray())),
            RoofBoundaryIdentityError.None);
    }

    public static RoofBoundaryIdentityError ValidateCurrentSource(
        RoofBoundaryIdentity? identity,
        int currentPhysicalSegmentCount,
        RoofPolygonOrientation currentRawWinding)
    {
        if (identity is null)
        {
            return RoofBoundaryIdentityError.IncompletePayload;
        }

        var stored = Validate(
            identity.SchemaVersion,
            identity.PhysicalSegmentCount,
            FormatWinding(identity.RawWinding),
            identity.BoundaryEdgeIds);
        if (!stored.IsValid)
        {
            return stored.Error;
        }

        if (identity.PhysicalSegmentCount != currentPhysicalSegmentCount)
        {
            return RoofBoundaryIdentityError.CurrentPhysicalSegmentCountMismatch;
        }

        return identity.RawWinding == currentRawWinding
            ? RoofBoundaryIdentityError.None
            : RoofBoundaryIdentityError.CurrentRawWindingMismatch;
    }

    public static string FormatWinding(RoofPolygonOrientation winding) => winding switch
    {
        RoofPolygonOrientation.Clockwise => ClockwiseToken,
        RoofPolygonOrientation.CounterClockwise => CounterClockwiseToken,
        _ => string.Empty,
    };

    public static bool TryParseWinding(
        string? token,
        out RoofPolygonOrientation winding)
    {
        winding = token switch
        {
            ClockwiseToken => RoofPolygonOrientation.Clockwise,
            CounterClockwiseToken => RoofPolygonOrientation.CounterClockwise,
            _ => RoofPolygonOrientation.Undefined,
        };
        return winding != RoofPolygonOrientation.Undefined;
    }

    private static RoofBoundaryIdentityValidationResult Invalid(
        RoofBoundaryIdentityError error) => new(false, null, error);
}
