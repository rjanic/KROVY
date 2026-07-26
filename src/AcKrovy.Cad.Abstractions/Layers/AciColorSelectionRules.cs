using System.Globalization;

namespace AcKrovy.Cad.Abstractions.Layers;

public static class AciColorSelectionRules
{
    public static bool TryParseLayerIndex(string? text, out int index)
    {
        if (int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out index) &&
            AciColorPalette.IsLayerColorIndex(index))
        {
            return true;
        }

        index = 0;
        return false;
    }

    public static int ResolveDialogResult(
        int originalIndex,
        int pendingIndex,
        bool confirmed)
    {
        if (!AciColorPalette.IsLayerColorIndex(originalIndex))
        {
            throw new ArgumentOutOfRangeException(nameof(originalIndex));
        }

        return confirmed && AciColorPalette.IsLayerColorIndex(pendingIndex)
            ? pendingIndex
            : originalIndex;
    }
}
