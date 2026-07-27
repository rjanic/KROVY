using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementEditRules
{
    public static bool HasRequestedChange(TimberElementPatch patch)
    {
        if (patch is null)
        {
            throw new ArgumentNullException(nameof(patch));
        }

        return patch.ElementType is not null ||
               patch.WidthMm is not null ||
               patch.HeightMm is not null ||
               patch.SlopeDegrees is not null ||
               patch.RoofPlaneId is not null ||
               patch.CuttingAllowanceMm is not null ||
               patch.LengthCalculationMode is not null ||
               patch.ManualLengthMm is not null ||
               patch.Material is not null ||
               patch.Note is not null ||
               patch.IsSlopeDirectionReversed is not null;
    }

    public static bool TryCreateEffectiveChange(
        TimberElementData source,
        TimberElementPatch patch,
        bool useDefaultCuttingAllowanceByType,
        TimberElementDefaultProfile defaultProfile,
        out TimberElementData updated)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (patch is null)
        {
            throw new ArgumentNullException(nameof(patch));
        }

        if (defaultProfile is null)
        {
            throw new ArgumentNullException(nameof(defaultProfile));
        }

        updated = HasEffectivePatchValue(source, patch)
            ? TimberElementPatcher.Apply(source, patch)
            : source;

        if (useDefaultCuttingAllowanceByType)
        {
            updated = TimberElementDefaultApplicator.ApplyCuttingAllowance(
                updated,
                defaultProfile);
        }

        return updated != source;
    }

    private static bool HasEffectivePatchValue(
        TimberElementData source,
        TimberElementPatch patch) =>
        patch.ElementType is { } elementType && elementType != source.ElementType ||
        patch.WidthMm is { } widthMm && widthMm != source.WidthMm ||
        patch.HeightMm is { } heightMm && heightMm != source.HeightMm ||
        patch.SlopeDegrees is { } slopeDegrees && slopeDegrees != source.SlopeDegrees ||
        patch.RoofPlaneId is { } roofPlaneId &&
            !string.Equals(roofPlaneId, source.RoofPlaneId, StringComparison.Ordinal) ||
        patch.CuttingAllowanceMm is { } cuttingAllowanceMm &&
            cuttingAllowanceMm != source.CuttingAllowanceMm ||
        patch.LengthCalculationMode is { } lengthCalculationMode &&
            lengthCalculationMode != source.LengthCalculationMode ||
        patch.ManualLengthMm is { } manualLengthMm &&
            manualLengthMm != source.ManualLengthMm ||
        patch.Material is { } material &&
            !string.Equals(material, source.Material, StringComparison.Ordinal) ||
        patch.Note is { } note &&
            !string.Equals(note, source.Note, StringComparison.Ordinal) ||
        patch.IsSlopeDirectionReversed is { } isSlopeDirectionReversed &&
            isSlopeDirectionReversed != source.IsSlopeDirectionReversed;
}
