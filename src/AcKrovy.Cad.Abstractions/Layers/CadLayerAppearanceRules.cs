namespace AcKrovy.Cad.Abstractions.Layers;

public static class CadLayerAppearanceRules
{
    public static bool Differs(
        int currentColorIndex,
        string? currentLinetypeName,
        int requestedColorIndex,
        string? requestedLinetypeName) =>
        currentColorIndex != requestedColorIndex ||
        !string.Equals(
            CadLinetypeNames.Normalize(currentLinetypeName),
            CadLinetypeNames.Normalize(requestedLinetypeName),
            StringComparison.OrdinalIgnoreCase);

    public static bool ShouldUpdateExisting(
        CadLayerUpdateMode updateMode,
        bool appearanceDiffers) =>
        appearanceDiffers && updateMode == CadLayerUpdateMode.UpdateExisting;
}
