using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed class AutoCadAnnotationScaleService
{
    public TimberAnnotationScaleContext Context { get; }

    private AutoCadAnnotationScaleService(TimberAnnotationScaleContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public static AutoCadAnnotationScaleService Create(
        Database database,
        Transaction transaction,
        TimberElementDefaultProfile defaultProfile)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(defaultProfile);

        var drawingStore = new AutoCadDrawingAnnotationScaleStore(database, transaction);
        var hasDrawingValue = drawingStore.TryRead(out var drawingDenominator);
        var context = TimberAnnotationScaleResolver.ResolveContext(
            hasDrawingValue,
            drawingDenominator,
            defaultProfile.AnnotationScaleDenominator);

        return new AutoCadAnnotationScaleService(context);
    }

    public double ScaleLength(double lengthMm) => Context.ScaleLength(lengthMm);

    public double ScaleTextHeight(double textHeightMm) => Context.ScaleLength(textHeightMm);
}
