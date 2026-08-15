using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Materializes normal KROVY annotations for one exact caller-owned creation batch.
/// The caller keeps source creation and annotation creation in the same transaction.
/// </summary>
internal static class TimberCreatedElementAnnotationService
{
    public static void EnsureForCreatedElements(
        Database database,
        Transaction transaction,
        IReadOnlyDictionary<ObjectId, TimberElementData> createdElements,
        TimberElementDefaultProfile defaultProfile)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(createdElements);
        ArgumentNullException.ThrowIfNull(defaultProfile);

        var annotatedElements = createdElements
            .Where(item => TimberAnnotationModeRules.Normalize(item.Value.AnnotationMode) !=
                TimberAnnotationMode.NoAnnotations)
            .ToArray();
        if (annotatedElements.Length == 0)
        {
            return;
        }

        var presentationBatchContext =
            AutoCadAnnotationPresentationBatchContext.Create(
                database,
                transaction,
                defaultProfile);
        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
        foreach (var (sourceId, data) in annotatedElements)
        {
            if (transaction.GetObject(sourceId, OpenMode.ForRead) is not Entity sourceEntity)
            {
                throw new InvalidOperationException(
                    "A newly created timber source could not be opened for annotation.");
            }

            TimberAnnotationService.EnsureForElement(
                database,
                transaction,
                sourceEntity,
                data,
                presentationBatchContext,
                roundingStepMm: roundingStepMm);
        }
    }
}
