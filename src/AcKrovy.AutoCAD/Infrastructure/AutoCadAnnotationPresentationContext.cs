using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.AutoCAD.Settings;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed record AutoCadAnnotationPresentationValues
{
    public TimberAnnotationScaleContext AnnotationScaleContext { get; }
    public TimberAnnotationTextSettings EffectiveTextSettings { get; }
    public bool HasExplicitTextSettings { get; }
    public double LabelAndDimensionModelHeight { get; }
    public double ItemNumberModelHeight { get; }
    public double SlopeAngleModelHeight { get; }
    public int AnnotationScaleDenominator => AnnotationScaleContext.Denominator;

    private AutoCadAnnotationPresentationValues(
        TimberAnnotationScaleContext annotationScaleContext,
        TimberAnnotationTextSettings effectiveTextSettings,
        bool hasExplicitTextSettings,
        double labelAndDimensionModelHeight,
        double itemNumberModelHeight,
        double slopeAngleModelHeight)
    {
        AnnotationScaleContext = annotationScaleContext ??
            throw new ArgumentNullException(nameof(annotationScaleContext));
        EffectiveTextSettings = effectiveTextSettings ??
            throw new ArgumentNullException(nameof(effectiveTextSettings));
        HasExplicitTextSettings = hasExplicitTextSettings;
        LabelAndDimensionModelHeight = labelAndDimensionModelHeight;
        ItemNumberModelHeight = itemNumberModelHeight;
        SlopeAngleModelHeight = slopeAngleModelHeight;
    }

    public static AutoCadAnnotationPresentationValues Create(
        TimberAnnotationScaleContext annotationScaleContext,
        TimberElementData data)
    {
        ArgumentNullException.ThrowIfNull(annotationScaleContext);
        ArgumentNullException.ThrowIfNull(data);

        var hasExplicitTextSettings = data.AnnotationTextSettings is not null;
        var effectiveTextSettings =
            TimberAnnotationTextSettingsRules.NormalizeStored(
                data.AnnotationTextSettings) ??
            TimberAnnotationTextSettingsRules.Default;

        return new AutoCadAnnotationPresentationValues(
            annotationScaleContext,
            effectiveTextSettings,
            hasExplicitTextSettings,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                effectiveTextSettings.DimensionPaperHeightMm,
                annotationScaleContext.Denominator),
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                effectiveTextSettings.ItemCodePaperHeightMm,
                annotationScaleContext.Denominator),
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                effectiveTextSettings.SlopePaperHeightMm,
                annotationScaleContext.Denominator));
    }

    public double GetModelHeight(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => ItemNumberModelHeight,
            TimberAnnotationTextRole.Dimension => LabelAndDimensionModelHeight,
            TimberAnnotationTextRole.Slope => SlopeAngleModelHeight,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
}

/// <summary>
/// One annotation text role resolved against the current database: its own text
/// style plus its own paper and model height. Roles are independent, so a
/// renderer must consume exactly the role it draws.
/// </summary>
internal sealed record AutoCadAnnotationTextRolePresentation
{
    public TimberAnnotationTextRole Role { get; }
    public string? RequestedTextStyleName { get; }
    public ObjectId? ResolvedTextStyleId { get; }
    public string? ResolvedTextStyleName { get; }
    public AutoCadTextStyleResolutionKind ResolutionKind { get; }
    public AutoCadTextStyleRequestStatus RequestStatus { get; }
    public bool IsFallback { get; }
    public bool HasCompatibleStyle { get; }
    public double PaperHeightMm { get; }
    public double ModelHeightMm { get; }

    private AutoCadAnnotationTextRolePresentation(
        TimberAnnotationTextRole role,
        AutoCadTextStyleResolution textStyleResolution,
        double paperHeightMm,
        double modelHeightMm)
    {
        ArgumentNullException.ThrowIfNull(textStyleResolution);
        Role = role;
        RequestedTextStyleName = textStyleResolution.RequestedTextStyleName;
        ResolvedTextStyleId = textStyleResolution.ResolvedTextStyleId;
        ResolvedTextStyleName = textStyleResolution.ResolvedTextStyleName;
        ResolutionKind = textStyleResolution.ResolutionKind;
        RequestStatus = textStyleResolution.RequestStatus;
        IsFallback = textStyleResolution.IsFallback;
        HasCompatibleStyle = textStyleResolution.HasCompatibleStyle;
        PaperHeightMm = paperHeightMm;
        ModelHeightMm = modelHeightMm;
    }

    public static AutoCadAnnotationTextRolePresentation Create(
        TimberAnnotationTextRole role,
        AutoCadTextStyleResolution textStyleResolution,
        AutoCadAnnotationPresentationValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new AutoCadAnnotationTextRolePresentation(
            role,
            textStyleResolution,
            values.EffectiveTextSettings.GetPaperHeightMm(role),
            values.GetModelHeight(role));
    }

    public string DescribeDiagnostics() =>
        $"role={Role}; requested={RequestedTextStyleName ?? "<none>"}; " +
        $"resolved={ResolvedTextStyleName ?? "<none>"}; " +
        $"kind={ResolutionKind}; request={RequestStatus}; " +
        $"paper={PaperHeightMm:R}; model={ModelHeightMm:R}";
}

internal sealed record AutoCadAnnotationPresentationContext
{
    private readonly AutoCadAnnotationTextRolePresentation _itemCodeText;
    private readonly AutoCadAnnotationTextRolePresentation _framedItemCodeText;
    private readonly AutoCadAnnotationTextRolePresentation _dimensionText;
    private readonly AutoCadAnnotationTextRolePresentation _slopeText;

    public Database Database { get; }
    public TimberAnnotationScaleContext AnnotationScaleContext { get; }
    public int AnnotationScaleDenominator => AnnotationScaleContext.Denominator;
    public TimberAnnotationTextSettings EffectiveTextSettings { get; }
    public bool HasExplicitTextSettings { get; }

    /// <summary>Item code role (K1, P8). Framed and Plain item renderers.</summary>
    public AutoCadAnnotationTextRolePresentation ItemCodeText => _itemCodeText;
    public AutoCadAnnotationTextRolePresentation FramedItemCodeText =>
        _framedItemCodeText;

    /// <summary>
    /// Dimension role (80/160). Standalone DimensionsLeader, the combined
    /// dimensions component and the FullLabel MText, whose frozen single-MText
    /// layout carries one height for the whole label.
    /// </summary>
    public AutoCadAnnotationTextRolePresentation DimensionText => _dimensionText;

    /// <summary>Numeric slope role (35°). Slope angle DBText only.</summary>
    public AutoCadAnnotationTextRolePresentation SlopeText => _slopeText;

    public double LabelAndDimensionModelHeight { get; }
    public double ItemNumberModelHeight { get; }
    public double SlopeAngleModelHeight { get; }

    private AutoCadAnnotationPresentationContext(
        Database database,
        AutoCadAnnotationPresentationValues values,
        AutoCadTextStyleResolution itemCodeResolution,
        AutoCadTextStyleResolution framedItemCodeResolution,
        AutoCadTextStyleResolution dimensionResolution,
        AutoCadTextStyleResolution slopeResolution)
    {
        Database = database ?? throw new ArgumentNullException(nameof(database));
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(itemCodeResolution);
        ArgumentNullException.ThrowIfNull(framedItemCodeResolution);
        ArgumentNullException.ThrowIfNull(dimensionResolution);
        ArgumentNullException.ThrowIfNull(slopeResolution);
        EnsureResolutionDatabase(database, itemCodeResolution);
        EnsureResolutionDatabase(database, framedItemCodeResolution);
        EnsureResolutionDatabase(database, dimensionResolution);
        EnsureResolutionDatabase(database, slopeResolution);

        AnnotationScaleContext = values.AnnotationScaleContext;
        EffectiveTextSettings = values.EffectiveTextSettings;
        HasExplicitTextSettings = values.HasExplicitTextSettings;
        LabelAndDimensionModelHeight = values.LabelAndDimensionModelHeight;
        ItemNumberModelHeight = values.ItemNumberModelHeight;
        SlopeAngleModelHeight = values.SlopeAngleModelHeight;
        _itemCodeText = AutoCadAnnotationTextRolePresentation.Create(
            TimberAnnotationTextRole.ItemCode,
            itemCodeResolution,
            values);
        _framedItemCodeText = AutoCadAnnotationTextRolePresentation.Create(
            TimberAnnotationTextRole.ItemCode,
            framedItemCodeResolution,
            values);
        _dimensionText = AutoCadAnnotationTextRolePresentation.Create(
            TimberAnnotationTextRole.Dimension,
            dimensionResolution,
            values);
        _slopeText = AutoCadAnnotationTextRolePresentation.Create(
            TimberAnnotationTextRole.Slope,
            slopeResolution,
            values);
    }

    public static AutoCadAnnotationPresentationContext Create(
        Database database,
        TimberAnnotationScaleContext annotationScaleContext,
        TimberElementData data,
        AutoCadTextStyleResolver textStyleResolver,
        IReadOnlySet<string>? availableUserPresetStyleNames = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(annotationScaleContext);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(textStyleResolver);
        if (!AutoCadDatabaseIdentity.IsSame(
                database,
                textStyleResolver.Database))
        {
            throw new ArgumentException(
                "Text-style resolver belongs to a different database.",
                nameof(textStyleResolver));
        }

        var values = AutoCadAnnotationPresentationValues.Create(
            annotationScaleContext,
            data);

        return new AutoCadAnnotationPresentationContext(
            database,
            values,
            ResolveRole(
                textStyleResolver,
                values,
                TimberAnnotationTextRole.ItemCode,
                availableUserPresetStyleNames),
            ResolveFramedItemCodeRole(
                textStyleResolver,
                values,
                availableUserPresetStyleNames),
            ResolveRole(
                textStyleResolver,
                values,
                TimberAnnotationTextRole.Dimension,
                availableUserPresetStyleNames),
            ResolveRole(
                textStyleResolver,
                values,
                TimberAnnotationTextRole.Slope,
                availableUserPresetStyleNames));
    }

    public AutoCadAnnotationTextRolePresentation ForRole(
        TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => _itemCodeText,
            TimberAnnotationTextRole.Dimension => _dimensionText,
            TimberAnnotationTextRole.Slope => _slopeText,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

    public void EnsureDatabase(Database database)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (!AutoCadDatabaseIdentity.IsSame(Database, database))
        {
            throw new InvalidOperationException(
                "Annotation presentation context belongs to a different database.");
        }
    }

    /// <summary>
    /// Each role owns its persisted style name, so a role that is missing or
    /// incompatible falls back on its own without dragging the other two roles
    /// to the same fallback. Legacy elements without explicit settings keep the
    /// single current-style priority for every role.
    /// </summary>
    private static AutoCadTextStyleResolution ResolveRole(
        AutoCadTextStyleResolver textStyleResolver,
        AutoCadAnnotationPresentationValues values,
        TimberAnnotationTextRole role,
        IReadOnlySet<string>? availableUserPresetStyleNames)
    {
        if (!values.HasExplicitTextSettings)
        {
            return textStyleResolver.ResolveLegacy();
        }

        var storedStyleName =
            values.EffectiveTextSettings.GetTextStyleName(role);
        if (storedStyleName.StartsWith(
                TimberAnnotationTextStylePresetRules.UserStyleNamePrefix,
                StringComparison.OrdinalIgnoreCase) &&
            availableUserPresetStyleNames is not null &&
            !availableUserPresetStyleNames.Contains(storedStyleName))
        {
            return textStyleResolver.ResolveExplicit(
                TimberAnnotationTextStylePresetRules.ClassicStyleName);
        }

        return textStyleResolver.ResolveExplicit(storedStyleName);
    }

    private static AutoCadTextStyleResolution ResolveFramedItemCodeRole(
        AutoCadTextStyleResolver textStyleResolver,
        AutoCadAnnotationPresentationValues values,
        IReadOnlySet<string>? availableUserPresetStyleNames)
    {
        if (values.HasExplicitTextSettings)
        {
            var storedStyleName = values.EffectiveTextSettings.ItemCodeTextStyleName;
            var isDeletedUserPreset = storedStyleName.StartsWith(
                    TimberAnnotationTextStylePresetRules.UserStyleNamePrefix,
                    StringComparison.OrdinalIgnoreCase) &&
                availableUserPresetStyleNames is not null &&
                !availableUserPresetStyleNames.Contains(storedStyleName);
            var stored = textStyleResolver.ResolveExplicit(storedStyleName);
            if (!isDeletedUserPreset &&
                stored.RequestStatus == AutoCadTextStyleRequestStatus.Compatible)
            {
                return stored;
            }
        }

        // G3 framed fallback is deterministic: stored style, then Classic,
        // then the resolver's existing Standard/current/first-compatible chain.
        return textStyleResolver.ResolveExplicit(
            TimberAnnotationTextStylePresetRules.ClassicStyleName);
    }

    private static void EnsureResolutionDatabase(
        Database database,
        AutoCadTextStyleResolution textStyleResolution)
    {
        if (textStyleResolution.ResolvedTextStyleId is ObjectId textStyleId &&
            !AutoCadDatabaseIdentity.IsSame(database, textStyleId))
        {
            throw new ArgumentException(
                "Resolved text style belongs to a different database.",
                nameof(textStyleResolution));
        }
    }
}

/// <summary>
/// Immutable read-only inputs shared by one annotation refresh batch. The
/// instance and all ObjectIds it exposes are scoped to its database/transaction.
/// </summary>
internal sealed class AutoCadAnnotationPresentationBatchContext
{
    private readonly AutoCadAnnotationScaleService _annotationScaleService;
    private readonly AutoCadTextStyleResolver _textStyleResolver;
    private readonly IReadOnlySet<string> _availableUserPresetStyleNames;

    public Database Database { get; }
    public AutoCadTextStyleCatalog TextStyleCatalog { get; }
    public AutoCadAnnotationScaleService AnnotationScaleService =>
        _annotationScaleService;
    public AutoCadItemLeaderBlockVariantBatchCatalog ItemLeaderVariantCatalog { get; }

    private AutoCadAnnotationPresentationBatchContext(
        Database database,
        AutoCadAnnotationScaleService annotationScaleService,
        AutoCadTextStyleCatalog textStyleCatalog)
    {
        Database = database ?? throw new ArgumentNullException(nameof(database));
        _annotationScaleService = annotationScaleService ??
            throw new ArgumentNullException(nameof(annotationScaleService));
        TextStyleCatalog = textStyleCatalog ??
            throw new ArgumentNullException(nameof(textStyleCatalog));
        if (!AutoCadDatabaseIdentity.IsSame(
                database,
                textStyleCatalog.Database))
        {
            throw new ArgumentException(
                "Text-style catalog belongs to a different database.",
                nameof(textStyleCatalog));
        }
        _textStyleResolver = new AutoCadTextStyleResolver(textStyleCatalog);
        _availableUserPresetStyleNames = TimberAnnotationTextStylePresetLibraryStore
            .Load()
            .Normalize()
            .Presets
            .Select(preset => preset.AutoCadTextStyleName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ItemLeaderVariantCatalog =
            new AutoCadItemLeaderBlockVariantBatchCatalog(database);
    }

    public static AutoCadAnnotationPresentationBatchContext Create(
        Database database,
        Transaction transaction,
        TimberElementDefaultProfile defaultProfile)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(defaultProfile);

        var annotationScaleService = AutoCadAnnotationScaleService.Create(
            database,
            transaction,
            defaultProfile);
        var textStyleCatalog = AutoCadTextStyleResolver.ReadCatalog(
            database,
            transaction);

        return new AutoCadAnnotationPresentationBatchContext(
            database,
            annotationScaleService,
            textStyleCatalog);
    }

    public AutoCadAnnotationPresentationContext ResolveForElement(
        TimberElementData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var annotationScaleContext =
            _annotationScaleService.ResolveForElement(data);
        return AutoCadAnnotationPresentationContext.Create(
            Database,
            annotationScaleContext,
            data,
            _textStyleResolver,
            _availableUserPresetStyleNames);
    }
}
