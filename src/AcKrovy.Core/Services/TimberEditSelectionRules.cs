namespace AcKrovy.Core.Services;

public sealed record TimberEditSelectionDecision<T>(
    bool UseImpliedSelection,
    IReadOnlyList<T> ValidItems,
    int RejectedItems);

public static class TimberEditSelectionRules
{
    public static TimberEditSelectionDecision<T> Evaluate<T>(
        IReadOnlyList<T> impliedItems,
        Func<T, bool> isValidTimberElement)
    {
        if (impliedItems is null)
        {
            throw new ArgumentNullException(nameof(impliedItems));
        }

        if (isValidTimberElement is null)
        {
            throw new ArgumentNullException(nameof(isValidTimberElement));
        }

        if (impliedItems.Count == 0)
        {
            return new TimberEditSelectionDecision<T>(
                UseImpliedSelection: false,
                ValidItems: Array.Empty<T>(),
                RejectedItems: 0);
        }

        var validItems = impliedItems
            .Where(isValidTimberElement)
            .ToArray();
        return new TimberEditSelectionDecision<T>(
            UseImpliedSelection: validItems.Length > 0,
            ValidItems: validItems,
            RejectedItems: impliedItems.Count - validItems.Length);
    }
}
