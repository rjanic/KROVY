namespace AcKrovy.Core.Models;

public sealed record TimberElementSignature(
    TimberElementType ElementType,
    string Material,
    double WidthMm,
    double HeightMm,
    double CuttingLengthMm,
    string CustomElementTypeId = "")
{
    public static TimberElementSignature FromMeasurement(TimberElementMeasurement measurement)
    {
        if (measurement is null)
        {
            throw new ArgumentNullException(nameof(measurement));
        }

        return new TimberElementSignature(
            measurement.Data.ElementType,
            measurement.Data.Material,
            measurement.Data.WidthMm,
            measurement.Data.HeightMm,
            measurement.CuttingLengthMm,
            measurement.Data.ElementType == TimberElementType.Custom
                ? measurement.Data.CustomElementTypeId?.Trim() ?? string.Empty
                : string.Empty);
    }
}
