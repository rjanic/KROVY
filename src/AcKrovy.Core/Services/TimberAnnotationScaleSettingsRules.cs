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
    double DimensionPaperTextHeightMm,
    double ItemNumberTextHeightMm,
    double ItemNumberPaperTextHeightMm,
    double SlopeTextHeightMm,
    double SlopePaperTextHeightMm,
    double FramedBlockScale);

public sealed record TimberAnnotationScaleLegacyMigrationPlan(
    bool WriteDrawingOverride,
    int DrawingDenominator,
    int PreviousEffectiveDenominator,
    int NewEffectiveDenominator)
{
    public bool RefreshDrawing =>
        PreviousEffectiveDenominator != NewEffectiveDenominator;
}

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
        var normalizedDenominator =
            TimberAnnotationScaleRules.NormalizeDenominator(denominator);
        var scaleFactor = TimberAnnotationScaleRules.GetScaleFactor(
            normalizedDenominator);
        var dimensionModel =
            TimberDimensionTypographyRules.CalculateTextHeightMm(scaleFactor);
        var itemNumberModel =
            TimberItemNumberTypographyRules.CalculateTextHeightMm(scaleFactor);
        var slopeModel =
            TimberSlopeAnnotationPresentationRules.CalculateTextHeightMm(scaleFactor);
        return new TimberAnnotationScalePreview(
            dimensionModel,
            dimensionModel / normalizedDenominator,
            itemNumberModel,
            itemNumberModel / normalizedDenominator,
            slopeModel,
            slopeModel / normalizedDenominator,
            scaleFactor);
    }

    public static TimberAnnotationScaleLegacyMigrationPlan CreateLegacyMigrationPlan(
        bool hasDrawingOverride,
        bool hasManagedTimberElements,
        int legacyUserDefaultDenominator)
    {
        var legacyDenominator = TimberAnnotationScaleRules.NormalizeDenominator(
            legacyUserDefaultDenominator);
        var shouldPinLegacyValue =
            !hasDrawingOverride &&
            hasManagedTimberElements &&
            legacyDenominator != TimberAnnotationScaleRules.DefaultDenominator;
        var previousEffective = shouldPinLegacyValue
            ? legacyDenominator
            : TimberAnnotationScaleRules.DefaultDenominator;

        return new(
            shouldPinLegacyValue,
            shouldPinLegacyValue
                ? legacyDenominator
                : TimberAnnotationScaleRules.DefaultDenominator,
            previousEffective,
            previousEffective);
    }

    public static TimberAnnotationScalePersistencePlan CreatePersistencePlan(
        bool hasDrawingOverride,
        int drawingDenominator,
        int loadedUserDefaultDenominator,
        TimberDrawingAnnotationScaleChange drawingChange,
        int requestedDrawingDenominator,
        int requestedUserDefaultDenominator)
    {
        _ = loadedUserDefaultDenominator;
        _ = requestedUserDefaultDenominator;
        var fixedDefault = TimberAnnotationScaleRules.DefaultDenominator;
        var oldDrawing = TimberAnnotationScaleRules.NormalizeDenominator(
            drawingDenominator);
        var oldEffective = hasDrawingOverride ? oldDrawing : fixedDefault;

        if (drawingChange == TimberDrawingAnnotationScaleChange.SetOverride)
        {
            var requested = TimberAnnotationScaleRules.NormalizeDenominator(
                requestedDrawingDenominator);
            return new(
                !hasDrawingOverride || oldDrawing != requested,
                false,
                requested,
                false,
                fixedDefault,
                oldEffective,
                requested);
        }

        if (drawingChange == TimberDrawingAnnotationScaleChange.ClearOverride)
        {
            return new(
                false,
                hasDrawingOverride,
                oldEffective,
                false,
                fixedDefault,
                oldEffective,
                fixedDefault);
        }

        return new(
            false,
            false,
            oldEffective,
            false,
            fixedDefault,
            oldEffective,
            oldEffective);
    }
}
