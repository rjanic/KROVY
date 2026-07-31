namespace AcKrovy.Core.Services;

public enum TimberAnnotationScalePreset
{
    Scale25,
    Scale50,
    Scale75,
    Scale100,
    Custom,
}

public enum TimberDrawingAnnotationScaleChange
{
    None,
    SetOverride,
    ClearOverride,
}

public sealed record TimberAnnotationScalePreview(
    double DimensionTextHeightMm,
    double ItemNumberTextHeightMm,
    double SlopeTextHeightMm,
    double FramedBlockScale);

public sealed record TimberAnnotationScalePersistencePlan(
    bool WriteDrawingOverride,
    bool RemoveDrawingOverride,
    int DrawingDenominator,
    bool SaveUserDefault,
    int UserDefaultDenominator,
    int PreviousEffectiveDenominator,
    int NewEffectiveDenominator)
{
    public bool RefreshDrawing =>
        PreviousEffectiveDenominator != NewEffectiveDenominator;
}

public static class TimberAnnotationScaleSettingsRules
{
    public static TimberAnnotationScalePreset GetPreset(int denominator) =>
        TimberAnnotationScaleRules.NormalizeDenominator(denominator) switch
        {
            25 => TimberAnnotationScalePreset.Scale25,
            50 => TimberAnnotationScalePreset.Scale50,
            75 => TimberAnnotationScalePreset.Scale75,
            100 => TimberAnnotationScalePreset.Scale100,
            _ => TimberAnnotationScalePreset.Custom,
        };

    public static int GetPresetDenominator(
        TimberAnnotationScalePreset preset,
        int customDenominator) =>
        preset switch
        {
            TimberAnnotationScalePreset.Scale25 => 25,
            TimberAnnotationScalePreset.Scale50 => 50,
            TimberAnnotationScalePreset.Scale75 => 75,
            TimberAnnotationScalePreset.Scale100 => 100,
            TimberAnnotationScalePreset.Custom
                when TimberAnnotationScaleRules.IsValidDenominator(customDenominator) =>
                customDenominator,
            _ => TimberAnnotationScaleRules.DefaultDenominator,
        };

    public static TimberAnnotationScalePreview CreatePreview(int denominator)
    {
        var scaleFactor = TimberAnnotationScaleRules.GetScaleFactor(denominator);
        return new TimberAnnotationScalePreview(
            TimberDimensionTypographyRules.CalculateTextHeightMm(scaleFactor),
            TimberItemNumberTypographyRules.CalculateTextHeightMm(scaleFactor),
            TimberSlopeAnnotationPresentationRules.CalculateTextHeightMm(scaleFactor),
            scaleFactor);
    }

    public static TimberAnnotationScalePersistencePlan CreatePersistencePlan(
        bool hasDrawingOverride,
        int drawingDenominator,
        int loadedUserDefaultDenominator,
        TimberDrawingAnnotationScaleChange drawingChange,
        int requestedDrawingDenominator,
        int requestedUserDefaultDenominator)
    {
        var oldDefault = TimberAnnotationScaleRules.NormalizeDenominator(
            loadedUserDefaultDenominator);
        var newDefault = TimberAnnotationScaleRules.NormalizeDenominator(
            requestedUserDefaultDenominator);
        var oldDrawing = TimberAnnotationScaleRules.NormalizeDenominator(
            drawingDenominator);
        var oldEffective = hasDrawingOverride ? oldDrawing : oldDefault;
        var defaultChanged = oldDefault != newDefault;

        if (drawingChange == TimberDrawingAnnotationScaleChange.SetOverride)
        {
            var requested = TimberAnnotationScaleRules.NormalizeDenominator(
                requestedDrawingDenominator);
            return new(
                !hasDrawingOverride || oldDrawing != requested,
                false,
                requested,
                defaultChanged,
                newDefault,
                oldEffective,
                requested);
        }

        if (drawingChange == TimberDrawingAnnotationScaleChange.ClearOverride)
        {
            return new(
                false,
                hasDrawingOverride,
                oldEffective,
                defaultChanged,
                newDefault,
                oldEffective,
                newDefault);
        }

        // Changing a user default must not silently change a drawing which was
        // inheriting the previous default. Pin its current effective value first.
        var pinInheritedValue = defaultChanged && !hasDrawingOverride;
        return new(
            pinInheritedValue,
            false,
            oldEffective,
            defaultChanged,
            newDefault,
            oldEffective,
            oldEffective);
    }
}
