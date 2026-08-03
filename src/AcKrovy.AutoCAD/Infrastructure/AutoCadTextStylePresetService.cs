using System.IO;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum AutoCadTextStylePresetEnsureKind
{
    AlreadyMatched,
    Created,
    Updated,
    FontUnavailable,
    Failed,
}

internal sealed record AutoCadTextStylePresetEnsureResult(
    string StyleName,
    string FontFile,
    AutoCadTextStylePresetEnsureKind Kind,
    ObjectId? TextStyleId,
    string? DiagnosticReason);

/// <summary>
/// Creates and idempotently maintains app-owned AutoCAD text styles for the
/// built-in Klasický / Architektonický presets and for user-defined presets.
/// Never mutates AutoCAD's system "Standard" style.
/// </summary>
internal static class AutoCadTextStylePresetService
{
    private const double AngleToleranceRadians = 1e-9d;
    private const double WidthTolerance = 1e-9d;

    public static AutoCadTextStylePresetEnsureResult EnsureBuiltIn(
        Database database,
        Transaction transaction,
        TimberAnnotationBuiltInTextStylePreset preset)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        var definition = TimberAnnotationTextStylePresetRules.GetBuiltIn(preset);
        return EnsureStyle(
            database,
            transaction,
            definition.AutoCadTextStyleName,
            definition.FontFile,
            definition.WidthFactor,
            definition.ObliqueAngleDegrees);
    }

    public static AutoCadTextStylePresetEnsureResult EnsureUserPreset(
        Database database,
        Transaction transaction,
        TimberAnnotationUserTextStylePreset preset)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(preset);
        var normalized =
            TimberAnnotationTextStylePresetRules.ValidateAndNormalizeUserPreset(
                preset);
        return EnsureStyle(
            database,
            transaction,
            normalized.AutoCadTextStyleName,
            normalized.FontFile,
            normalized.WidthFactor,
            normalized.ObliqueAngleDegrees);
    }

    public static IReadOnlyList<AutoCadTextStylePresetEnsureResult> EnsureRequiredStyles(
        Database database,
        Transaction transaction,
        TimberAnnotationTextSettings settings,
        IEnumerable<TimberAnnotationUserTextStylePreset>? userPresets = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(settings);

        var normalized =
            TimberAnnotationTextSettingsRules.ValidateAndNormalize(settings);
        var results = new List<AutoCadTextStylePresetEnsureResult>();
        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            normalized.ItemCodeTextStyleName,
            normalized.DimensionTextStyleName,
            normalized.SlopeTextStyleName,
        };

        foreach (var styleName in requested)
        {
            if (TimberAnnotationTextStylePresetRules.TryResolveBuiltInByStyleName(
                    styleName,
                    out var builtIn) &&
                builtIn is not null)
            {
                results.Add(EnsureStyle(
                    database,
                    transaction,
                    builtIn.AutoCadTextStyleName,
                    builtIn.FontFile,
                    builtIn.WidthFactor,
                    builtIn.ObliqueAngleDegrees));
                continue;
            }

            var user = userPresets?
                .FirstOrDefault(preset =>
                    string.Equals(
                        preset.AutoCadTextStyleName,
                        styleName,
                        StringComparison.OrdinalIgnoreCase));
            if (user is not null)
            {
                results.Add(EnsureUserPreset(database, transaction, user));
            }
        }

        return results;
    }

    public static AutoCadTextStylePresetEnsureResult EnsureStyle(
        Database database,
        Transaction transaction,
        string styleName,
        string fontFile,
        double widthFactor,
        double obliqueAngleDegrees)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);

        var normalizedName =
            TimberAnnotationTextSettingsRules.ValidateAndNormalizeTextStyleName(
                styleName,
                nameof(styleName));
        if (string.Equals(
                normalizedName,
                TimberAnnotationTextSettingsRules.DefaultTextStyleName,
                StringComparison.OrdinalIgnoreCase))
        {
            return new AutoCadTextStylePresetEnsureResult(
                normalizedName,
                fontFile,
                AutoCadTextStylePresetEnsureKind.Failed,
                null,
                "AutoCAD system style Standard must not be modified.");
        }

        if (!TimberAnnotationTextStylePresetRules.IsAppOwnedStyleName(normalizedName))
        {
            return new AutoCadTextStylePresetEnsureResult(
                normalizedName,
                fontFile,
                AutoCadTextStylePresetEnsureKind.Failed,
                null,
                "Only app-owned AK_KROVY_* text styles may be created or updated.");
        }

        var normalizedFont =
            TimberAnnotationTextStylePresetRules.ValidateAndNormalizeFontFile(
                fontFile);
        if (!AutoCadFontDiscoveryService.IsFontAvailable(normalizedFont))
        {
            return new AutoCadTextStylePresetEnsureResult(
                normalizedName,
                normalizedFont,
                AutoCadTextStylePresetEnsureKind.FontUnavailable,
                null,
                $"Font '{normalizedFont}' is not available to AutoCAD/Windows.");
        }

        var textStyleTable = (TextStyleTable)transaction.GetObject(
            database.TextStyleTableId,
            OpenMode.ForRead);
        var obliqueRadians = obliqueAngleDegrees * Math.PI / 180d;

        if (textStyleTable.Has(normalizedName))
        {
            var existingId = textStyleTable[normalizedName];
            var existing = (TextStyleTableRecord)transaction.GetObject(
                existingId,
                OpenMode.ForRead);
            if (MatchesDefinition(
                    existing,
                    normalizedFont,
                    widthFactor,
                    obliqueRadians))
            {
                return new AutoCadTextStylePresetEnsureResult(
                    existing.Name,
                    normalizedFont,
                    AutoCadTextStylePresetEnsureKind.AlreadyMatched,
                    existingId,
                    null);
            }

            existing.UpgradeOpen();
            ApplyDefinition(existing, normalizedFont, widthFactor, obliqueRadians);
            return new AutoCadTextStylePresetEnsureResult(
                existing.Name,
                normalizedFont,
                AutoCadTextStylePresetEnsureKind.Updated,
                existingId,
                null);
        }

        textStyleTable.UpgradeOpen();
        var created = new TextStyleTableRecord
        {
            Name = normalizedName,
        };
        ApplyDefinition(created, normalizedFont, widthFactor, obliqueRadians);
        var createdId = textStyleTable.Add(created);
        transaction.AddNewlyCreatedDBObject(created, true);
        return new AutoCadTextStylePresetEnsureResult(
            created.Name,
            normalizedFont,
            AutoCadTextStylePresetEnsureKind.Created,
            createdId,
            null);
    }

    private const byte BackwardsFlag = 2;
    private const byte UpsideDownFlag = 4;

    private static bool MatchesDefinition(
        TextStyleTableRecord record,
        string fontFile,
        double widthFactor,
        double obliqueRadians)
    {
        if (record.TextSize != 0d ||
            record.IsVertical ||
            IsBackwards(record) ||
            IsUpsideDown(record))
        {
            return false;
        }

        if (Math.Abs(record.XScale - widthFactor) > WidthTolerance ||
            Math.Abs(record.ObliquingAngle - obliqueRadians) > AngleToleranceRadians)
        {
            return false;
        }

        if (TryReadTrueTypeTypeface(record, out var typeface) &&
            !string.IsNullOrWhiteSpace(typeface))
        {
            return string.Equals(
                typeface.Trim(),
                fontFile,
                StringComparison.OrdinalIgnoreCase);
        }

        var fileName = record.FileName?.Trim() ?? string.Empty;
        return string.Equals(fileName, fontFile, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                Path.GetFileNameWithoutExtension(fileName),
                fontFile,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyDefinition(
        TextStyleTableRecord record,
        string fontFile,
        double widthFactor,
        double obliqueRadians)
    {
        record.TextSize = 0d;
        record.XScale = widthFactor;
        record.ObliquingAngle = obliqueRadians;
        record.IsVertical = false;
        record.FlagBits = (byte)(record.FlagBits & ~(BackwardsFlag | UpsideDownFlag));
        record.BigFontFileName = string.Empty;
        try
        {
            // charset 0 = ANSI_CHARSET; pitchAndFamily 34 = VARIABLE | FF_SWISS
            record.Font = new FontDescriptor(
                fontFile,
                false,
                false,
                0,
                34);
        }
        catch (AcadException)
        {
            record.FileName = fontFile;
        }
    }

    private static bool TryReadTrueTypeTypeface(
        TextStyleTableRecord record,
        out string? typeface)
    {
        typeface = null;
        try
        {
            var font = record.Font;
            typeface = font.TypeFace;
            return !string.IsNullOrWhiteSpace(typeface);
        }
        catch (AcadException)
        {
            return false;
        }
    }

    private static bool IsBackwards(TextStyleTableRecord record) =>
        (record.FlagBits & BackwardsFlag) != 0;

    private static bool IsUpsideDown(TextStyleTableRecord record) =>
        (record.FlagBits & UpsideDownFlag) != 0;
}
