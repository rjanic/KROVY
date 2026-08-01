using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
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
                effectiveTextSettings.LabelAndDimensionPaperHeightMm,
                annotationScaleContext.Denominator),
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                effectiveTextSettings.ItemNumberPaperHeightMm,
                annotationScaleContext.Denominator),
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                effectiveTextSettings.SlopeAnglePaperHeightMm,
                annotationScaleContext.Denominator));
    }
}

internal sealed record AutoCadAnnotationPresentationContext
{
    private readonly AutoCadTextStyleResolution _textStyleResolution;

    public Database Database { get; }
    public TimberAnnotationScaleContext AnnotationScaleContext { get; }
    public int AnnotationScaleDenominator => AnnotationScaleContext.Denominator;
    public TimberAnnotationTextSettings EffectiveTextSettings { get; }
    public bool HasExplicitTextSettings { get; }
    public string? RequestedTextStyleName { get; }
    public ObjectId? ResolvedTextStyleId { get; }
    public string? ResolvedTextStyleName { get; }
    public AutoCadTextStyleResolutionKind TextStyleResolutionKind { get; }
    public AutoCadTextStyleRequestStatus TextStyleRequestStatus { get; }
    public bool IsFallback => _textStyleResolution.IsFallback;
    public bool HasCompatibleStyle => _textStyleResolution.HasCompatibleStyle;
    public double LabelAndDimensionModelHeight { get; }
    public double ItemNumberModelHeight { get; }
    public double SlopeAngleModelHeight { get; }

    private AutoCadAnnotationPresentationContext(
        Database database,
        AutoCadAnnotationPresentationValues values,
        AutoCadTextStyleResolution textStyleResolution)
    {
        Database = database ?? throw new ArgumentNullException(nameof(database));
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(textStyleResolution);
        _textStyleResolution = textStyleResolution;
        if (textStyleResolution.ResolvedTextStyleId is ObjectId textStyleId &&
            !AutoCadDatabaseIdentity.IsSame(database, textStyleId))
        {
            throw new ArgumentException(
                "Resolved text style belongs to a different database.",
                nameof(textStyleResolution));
        }

        AnnotationScaleContext = values.AnnotationScaleContext;
        EffectiveTextSettings = values.EffectiveTextSettings;
        HasExplicitTextSettings = values.HasExplicitTextSettings;
        RequestedTextStyleName = textStyleResolution.RequestedTextStyleName;
        ResolvedTextStyleId = textStyleResolution.ResolvedTextStyleId;
        ResolvedTextStyleName = textStyleResolution.ResolvedTextStyleName;
        TextStyleResolutionKind = textStyleResolution.ResolutionKind;
        TextStyleRequestStatus = textStyleResolution.RequestStatus;
        LabelAndDimensionModelHeight = values.LabelAndDimensionModelHeight;
        ItemNumberModelHeight = values.ItemNumberModelHeight;
        SlopeAngleModelHeight = values.SlopeAngleModelHeight;
    }

    public static AutoCadAnnotationPresentationContext Create(
        Database database,
        TimberAnnotationScaleContext annotationScaleContext,
        TimberElementData data,
        AutoCadTextStyleResolver textStyleResolver)
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
        var textStyleResolution = values.HasExplicitTextSettings
            ? textStyleResolver.ResolveExplicit(
                values.EffectiveTextSettings.TextStyleName)
            : textStyleResolver.ResolveLegacy();

        return new AutoCadAnnotationPresentationContext(
            database,
            values,
            textStyleResolution);
    }

    public void EnsureDatabase(Database database)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (!AutoCadDatabaseIdentity.IsSame(Database, database))
        {
            throw new InvalidOperationException(
                "Annotation presentation context belongs to a different database.");
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

    public Database Database { get; }
    public AutoCadTextStyleCatalog TextStyleCatalog { get; }

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
            _textStyleResolver);
    }
}
