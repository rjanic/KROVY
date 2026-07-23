namespace AcKrovy.Localization;

public static class TimberMaterialEditRules
{
    public static bool ShouldActivateApplyFlag(
        bool isInitializing,
        string? selectedStoredValue,
        string? originalStoredValue) =>
        !isInitializing &&
        !string.Equals(
            selectedStoredValue,
            originalStoredValue,
            StringComparison.Ordinal);

    public static string? ResolvePatchValue(
        bool isMaterialChangeEnabled,
        string? selectedStoredValue)
    {
        if (!isMaterialChangeEnabled ||
            string.IsNullOrWhiteSpace(selectedStoredValue))
        {
            return null;
        }

        return selectedStoredValue;
    }
}
