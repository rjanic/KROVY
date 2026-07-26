namespace AcKrovy.Cad.Abstractions.Layers;

public static class AciColorPickerRules
{
    public static IReadOnlyList<int> BasicIndices { get; } =
        Enumerable.Range(1, 9).ToArray();

    public static IReadOnlyList<int> MainPaletteIndices { get; } =
        Enumerable.Range(10, 240).ToArray();

    public static IReadOnlyList<int> GrayscaleIndices { get; } =
        Enumerable.Range(250, 6).ToArray();

    public const int MainPaletteColumns = 24;
    public const int MainPaletteRows = 10;

    public static bool IsValid(int index) => index is >= 1 and <= 255;
}

public sealed class AciColorPickerState
{
    public int OriginalAciIndex { get; private set; } = 1;
    public int PendingAciIndex { get; private set; } = 1;
    public int SelectedAciIndex { get; private set; } = 1;

    public void Open(int selectedAciIndex)
    {
        if (!AciColorPickerRules.IsValid(selectedAciIndex))
        {
            throw new ArgumentOutOfRangeException(nameof(selectedAciIndex));
        }

        OriginalAciIndex = selectedAciIndex;
        PendingAciIndex = selectedAciIndex;
        SelectedAciIndex = selectedAciIndex;
    }

    public bool TrySetPending(int pendingAciIndex)
    {
        if (!AciColorPickerRules.IsValid(pendingAciIndex))
        {
            return false;
        }

        PendingAciIndex = pendingAciIndex;
        return true;
    }

    public bool Commit()
    {
        if (!AciColorPickerRules.IsValid(PendingAciIndex))
        {
            return false;
        }

        SelectedAciIndex = PendingAciIndex;
        OriginalAciIndex = SelectedAciIndex;
        return true;
    }

    public void Cancel()
    {
        PendingAciIndex = OriginalAciIndex;
        SelectedAciIndex = OriginalAciIndex;
    }
}
