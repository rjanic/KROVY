using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public static class RoofDefinitionPersistence
{
    public static RoofDefinitionData Create(
        RoofFootprint footprint,
        SimpleGableRoofGeometry geometry)
    {
        if (footprint is null)
        {
            throw new ArgumentNullException(nameof(footprint));
        }

        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }
        var data = new RoofDefinitionData(
            RoofDefinitionDataSchema.CurrentVersion,
            RoofKind.SimpleGable,
            geometry.SlopeDegrees,
            geometry.RidgeDirection.X,
            geometry.RidgeDirection.Y,
            footprint.Signature);
        _ = RoofDefinitionDataCodec.Encode(data);
        return data;
    }

    public static RoofDefinitionRestoreResult Restore(
        RoofFootprint footprint,
        RoofDefinitionData data)
    {
        if (footprint is null)
        {
            throw new ArgumentNullException(nameof(footprint));
        }

        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        if (!RoofDefinitionDataCodec.TryValidate(data, out _))
        {
            return Invalid(RoofDefinitionRestoreError.InvalidDefinition);
        }

        if (!string.Equals(
                footprint.Signature,
                data.FootprintSignature,
                StringComparison.Ordinal))
        {
            return Invalid(RoofDefinitionRestoreError.StaleFootprint);
        }

        if (!RoofDirection2D.TryCreate(
                data.RidgeDirectionX,
                data.RidgeDirectionY,
                out var direction))
        {
            return Invalid(RoofDefinitionRestoreError.InvalidDefinition);
        }

        var solved = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(data.SlopeDegrees, direction)));
        return solved.IsValid && solved.Geometry is not null
            ? new RoofDefinitionRestoreResult(
                true,
                solved.Geometry,
                RoofDefinitionRestoreError.None)
            : Invalid(RoofDefinitionRestoreError.InvalidDefinition);
    }

    private static RoofDefinitionRestoreResult Invalid(RoofDefinitionRestoreError error) =>
        new(false, null, error);
}
