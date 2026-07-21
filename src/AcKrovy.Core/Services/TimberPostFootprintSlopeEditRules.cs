using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberPostFootprintSlopeEditRules
{
    public const double PerpendicularDisplaySlopeDegrees = 90d;

    public static bool UsesPerpendicularPresentation(
        IReadOnlyCollection<TimberElementData> elements)
    {
        if (elements is null)
        {
            throw new ArgumentNullException(nameof(elements));
        }

        return elements.Count > 0 &&
            elements.All(TimberPostFootprintMetadataRules.IsValidNewFootprintPost);
    }

    public static bool CanEditSlope(IReadOnlyCollection<TimberElementData> elements) =>
        !UsesPerpendicularPresentation(elements);

    public static bool CanEditSlopeDirection(IReadOnlyCollection<TimberElementData> elements) =>
        !UsesPerpendicularPresentation(elements);

    public static bool? ResolveSlopeDirectionPatch(
        IReadOnlyCollection<TimberElementData> elements,
        bool shouldApply,
        bool isReversed) =>
        CanEditSlopeDirection(elements) && shouldApply
            ? isReversed
            : null;

    public static double ResolveDisplaySlopeDegrees(
        TimberElementData data,
        IReadOnlyCollection<TimberElementData> elements)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return UsesPerpendicularPresentation(elements)
            ? PerpendicularDisplaySlopeDegrees
            : data.SlopeDegrees;
    }
}
