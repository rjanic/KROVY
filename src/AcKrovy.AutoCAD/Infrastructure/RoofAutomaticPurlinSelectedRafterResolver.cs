using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Resolves any valid KROVY Rafter into dialog-level Width/Height for the current
/// AK_ROOF_PURLINS session. Dimension source only — does not mutate timber,
/// recipes, ownership, or the open roof owner.
/// </summary>
internal static class RoofAutomaticPurlinSelectedRafterResolver
{
    internal const string NonRafterRejected = "non-rafter-timber-rejected";
    internal const string UnreadableMetadata = "rafter-metadata-unreadable";

    public readonly record struct SelectedRafterDimensions(
        double WidthMm,
        double HeightMm,
        string ElementId,
        string? SourceOwnerReference);

    public static bool TryResolveRafterDimensions(
        Database database,
        Transaction transaction,
        ObjectId selectedId,
        out SelectedRafterDimensions dimensions,
        out string failureReason)
    {
        dimensions = default;
        failureReason = string.Empty;
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);

        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                selectedId,
                OpenMode.ForRead,
                out var entity,
                database) ||
            entity is null)
        {
            failureReason = UnreadableMetadata;
            return false;
        }

        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        if (!metadataStore.TryRead(entity, out var timber) ||
            timber is null)
        {
            failureReason = UnreadableMetadata;
            return false;
        }

        if (timber.ElementType != TimberElementType.Rafter)
        {
            failureReason = NonRafterRejected;
            return false;
        }

        if (!IsFinitePositive(timber.WidthMm) || !IsFinitePositive(timber.HeightMm))
        {
            failureReason = UnreadableMetadata;
            return false;
        }

        string? sourceOwner = null;
        var generated = RoofGeneratedTimberStore.Read(entity);
        if (generated.Data is not null)
        {
            sourceOwner = generated.Data.RoofOwnerReference;
        }

        dimensions = new SelectedRafterDimensions(
            timber.WidthMm,
            timber.HeightMm,
            timber.ElementId ?? string.Empty,
            sourceOwner);
        return true;
    }

    /// <summary>
    /// True only when generated ownership metadata proves the rafter belongs to
    /// <paramref name="dialogOwnerReference"/>. Missing ownership cannot be
    /// invented — treat as external (manual W×H copy only).
    /// </summary>
    public static bool IsCurrentRoofGeneratedRafter(
        SelectedRafterDimensions selected,
        string dialogOwnerReference) =>
        !string.IsNullOrWhiteSpace(dialogOwnerReference) &&
        !string.IsNullOrWhiteSpace(selected.SourceOwnerReference) &&
        string.Equals(
            selected.SourceOwnerReference,
            dialogOwnerReference,
            StringComparison.OrdinalIgnoreCase);

#if DEBUG
    /// <summary>
    /// DEBUG-only rafter-pick diagnostic. Must never be written via
    /// <see cref="Editor.WriteMessage"/> immediately before the AK_ROOF_PURLINS
    /// modal dialog reopens — AutoCAD surfaces that last message as a floating tip
    /// over the WPF window (looks like a grey ToolTip). Route to Debug only.
    /// </summary>
    public static void WriteSelectionDiagnostic(
        Editor? editor,
        string dialogOwnerReference,
        SelectedRafterDimensions selected,
        double recipeWidthMm,
        double recipeHeightMm)
    {
        // Intentionally ignore Editor: command-line WriteMessage leaks into the
        // reopened modal WPF UI as an AutoCAD cursor tip after rafter pick.
        _ = editor;
        var differs =
            Math.Abs(selected.WidthMm - recipeWidthMm) > 1e-9 ||
            Math.Abs(selected.HeightMm - recipeHeightMm) > 1e-9;
        System.Diagnostics.Debug.WriteLine(
            "ROOF_PURLIN_RAFTER_PICK" +
            $" dialogOwner={dialogOwnerReference}" +
            $" sourceOwner={selected.SourceOwnerReference ?? "-"}" +
            $" elementId={selected.ElementId}" +
            $" width={Format(selected.WidthMm)}" +
            $" height={Format(selected.HeightMm)}" +
            $" recipeWidth={Format(recipeWidthMm)}" +
            $" recipeHeight={Format(recipeHeightMm)}" +
            $" differsFromRecipe={(differs ? "1" : "0")}" +
            $" currentRoofGenerated={(IsCurrentRoofGeneratedRafter(selected, dialogOwnerReference) ? "1" : "0")}");
    }

    private static string Format(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
#else
    public static void WriteSelectionDiagnostic(
        Editor? editor,
        string dialogOwnerReference,
        SelectedRafterDimensions selected,
        double recipeWidthMm,
        double recipeHeightMm)
    {
        _ = editor;
        _ = dialogOwnerReference;
        _ = selected;
        _ = recipeWidthMm;
        _ = recipeHeightMm;
    }
#endif

    private static bool IsFinitePositive(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
}
