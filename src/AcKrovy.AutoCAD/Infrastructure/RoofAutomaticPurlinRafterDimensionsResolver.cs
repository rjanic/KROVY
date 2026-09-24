using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Resolves authoritative ordinary-rafter width/height for AK_ROOF_PURLINS.
/// Recoverable roof recipe wins; profile defaults are fallback only when no
/// ordinary generated set exists. Ambiguous recipes never silently fall back.
/// </summary>
internal static class RoofAutomaticPurlinRafterDimensionsResolver
{
    internal const string AmbiguousFailure = "rafter-recipe-ambiguous";
    internal const string RecoveredSourceToken = "RecoveredRoofRecipe";
    internal const string ProfileFallbackSourceToken = "ProfileDefaultFallback";

    public enum SourceKind
    {
        RecoveredRoofRecipe = 1,
        ProfileDefaultFallback = 2,
    }

    public readonly record struct Resolution(
        double WidthMm,
        double HeightMm,
        SourceKind Source)
    {
        public string SourceToken => Source == SourceKind.RecoveredRoofRecipe
            ? RecoveredSourceToken
            : ProfileFallbackSourceToken;

        public TimberElementData ApplyToProfileDefaults(TimberElementData profileDefaults) =>
            profileDefaults with
            {
                WidthMm = WidthMm,
                HeightMm = HeightMm,
            };
    }

    public static bool TryResolve(
        Database database,
        Transaction transaction,
        string ownerReference,
        TimberElementDefaultProfile defaultProfile,
        out Resolution resolution,
        out string failureReason,
        Editor? editor = null)
    {
        resolution = default;
        failureReason = string.Empty;
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(defaultProfile);
        if (string.IsNullOrWhiteSpace(ownerReference))
        {
            failureReason = "owner-reference-missing";
            return false;
        }

        var profileDefaults = TimberElementDefaults.For(
            TimberElementType.Rafter,
            defaultProfile);
        var generatedIds = RoofGeneratedTimberStore.FindByOwner(
            database,
            transaction,
            ownerReference);
        // Only ordinary generated rafters participate. Never treat profile defaults,
        // foreign timber, or a saved manual W×H as evidence of an actual roof recipe.
        var ordinaryRafterIds = CollectOrdinaryGeneratedRafterIds(
            database,
            transaction,
            generatedIds);
        if (ordinaryRafterIds.Count == 0)
        {
            resolution = new Resolution(
                profileDefaults.WidthMm,
                profileDefaults.HeightMm,
                SourceKind.ProfileDefaultFallback);
            WriteDiagnostic(editor, ownerReference, resolution);
            return true;
        }

        if (!RoofGeneratedRafterSetService.TryRecoverRecipe(
                database,
                transaction,
                ordinaryRafterIds,
                out var recipe))
        {
            failureReason = AmbiguousFailure;
            return false;
        }

        resolution = new Resolution(
            recipe.WidthMm,
            recipe.HeightMm,
            SourceKind.RecoveredRoofRecipe);
        WriteDiagnostic(editor, ownerReference, resolution);
        return true;
    }

    /// <summary>
    /// Keeps only ordinary generated rafter entities owned by the roof. Foreign timber
    /// and non-rafter generated members never count as an actual rafter recipe.
    /// </summary>
    private static IReadOnlyList<ObjectId> CollectOrdinaryGeneratedRafterIds(
        Database database,
        Transaction transaction,
        IReadOnlyList<ObjectId> generatedIds)
    {
        if (generatedIds.Count == 0)
        {
            return [];
        }

        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var rafterIds = new List<ObjectId>(generatedIds.Count);
        foreach (var id in generatedIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
                continue;
            }

            var generated = RoofGeneratedTimberStore.Read(entity);
            if (generated.Data is null ||
                generated.Data.MemberKind != RoofGeneratedTimberKind.Rafter ||
                !metadataStore.TryRead(entity, out var timber) ||
                timber is null ||
                timber.ElementType != TimberElementType.Rafter)
            {
                continue;
            }

            rafterIds.Add(id);
        }

        return rafterIds;
    }

    public static bool TryResolveForOwner(
        Database database,
        Transaction transaction,
        Polyline owner,
        TimberElementDefaultProfile defaultProfile,
        out Resolution resolution,
        out string failureReason,
        Editor? editor = null) =>
        TryResolve(
            database,
            transaction,
            owner.Handle.ToString(),
            defaultProfile,
            out resolution,
            out failureReason,
            editor);

#if DEBUG
    /// <summary>
    /// DEBUG-only rafter-source diagnostic. Must never use
    /// <see cref="Editor.WriteMessage"/> immediately before AK_ROOF_PURLINS
    /// opens — AutoCAD surfaces that last command-line message as a floating
    /// grey tip over the WPF dialog (same class of leak as ROOF_PURLIN_RAFTER_PICK).
    /// </summary>
    private static void WriteDiagnostic(
        Editor? editor,
        string ownerReference,
        Resolution resolution)
    {
        _ = editor;
        System.Diagnostics.Debug.WriteLine(
            "ROOF_PURLIN_RAFTER_SOURCE" +
            $" owner={ownerReference}" +
            $" source={resolution.SourceToken}" +
            $" width={Format(resolution.WidthMm)}" +
            $" height={Format(resolution.HeightMm)}");
    }

    private static string Format(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
#else
    private static void WriteDiagnostic(
        Editor? editor,
        string ownerReference,
        Resolution resolution)
    {
        _ = editor;
        _ = ownerReference;
        _ = resolution;
    }
#endif
}
