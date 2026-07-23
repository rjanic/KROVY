using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementPatcher
{
    public static TimberElementData Apply(TimberElementData source, TimberElementPatch patch)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (patch is null)
        {
            throw new ArgumentNullException(nameof(patch));
        }

        var result = source with
        {
            ElementType = patch.ElementType ?? source.ElementType,
            WidthMm = patch.WidthMm ?? source.WidthMm,
            HeightMm = patch.HeightMm ?? source.HeightMm,
            SlopeDegrees = patch.SlopeDegrees ?? source.SlopeDegrees,
            IsSlopeDirectionReversed = patch.IsSlopeDirectionReversed ?? source.IsSlopeDirectionReversed,
            RoofPlaneId = patch.RoofPlaneId ?? source.RoofPlaneId,
            CuttingAllowanceMm = patch.CuttingAllowanceMm ?? source.CuttingAllowanceMm,
            LengthCalculationMode = patch.LengthCalculationMode ?? source.LengthCalculationMode,
            ManualLengthMm = patch.ManualLengthMm ?? source.ManualLengthMm,
            Material = patch.Material ?? source.Material,
            Note = patch.Note ?? source.Note,
        };

        return result.ElementType == TimberElementType.Custom
            ? result
            : result with
            {
                CustomElementTypeId = null,
                CustomElementTypeName = null,
                CustomElementTypePrefix = null,
            };
    }
}
