using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Post-create stabilization for G5 BlockContent MLeaders.
/// Production default is create-order + optional graphics refresh.
/// Epsilon ±1° is DEBUG/test-only and never the default create path.
/// </summary>
internal enum AutoCadFramedBlockContentStabilizationMode
{
    /// <summary>A: create → attrs → vertices → final TransformBy only.</summary>
    CreateOrderOnly = 0,

    /// <summary>B: A + RecordGraphicsModified(true) without geometry change.</summary>
    RecordGraphicsRefresh = 1,

    /// <summary>C: B + reopen ForWrite in a follow-up transaction and verify.</summary>
    ReopenVerify = 2,

    /// <summary>
    /// D: ±1° TransformBy around attachment. DEBUG/host-test only.
    /// </summary>
    EpsilonRotate = 3,
}

/// <summary>
/// Immutable host request for one G5 BlockContent MLeader.
/// Ownership / refresh lifecycle fields are intentionally absent — those belong
/// to later P4/P6 wiring, not the create contract.
/// </summary>
internal sealed record AutoCadFramedBlockContentAnnotationRequest(
    double AttachmentX,
    double AttachmentY,
    double ElementAxisRadians,
    TimberLeaderHorizontalSide Side,
    TimberFramedBlockContentKind ContentKind,
    TimberFramedBlockContentPresentation Presentation,
    double FrameWidthMm,
    double FrameHeightMm,
    double DimensionColumnEnvelopeWidthMm,
    int AnnotationScaleDenominator,
    double ItemPaperHeightMm,
    double DimensionPaperHeightMm,
    string ItemTextStyleName,
    string DimensionTextStyleName,
    ObjectId ItemTextStyleId,
    ObjectId DimensionTextStyleId,
    string ItemNoText,
    string WidthText,
    string HeightText,
    double FirstSegmentLengthModelMm,
    double LandingLengthModelMm,
    ObjectId LayerId,
    AutoCadFramedBlockContentStabilizationMode StabilizationMode =
        AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh)
{
    public const double EpsilonRotateRadians = Math.PI / 180d;

    public AutoCadFramedBlockContentAnnotationRequest Normalize()
    {
        TimberFramedBlockContentDefinitionRules.ValidateRequest(
            ContentKind,
            Presentation);
        if (!TimberAnnotationScaleRules.IsValidDenominator(
                AnnotationScaleDenominator))
        {
            throw new ArgumentOutOfRangeException(
                nameof(AnnotationScaleDenominator));
        }

        if (!TimberAnnotationTextSettingsRules.IsValidTextStyleName(
                ItemTextStyleName))
        {
            throw new ArgumentException(
                "Item text style identity is required.",
                nameof(ItemTextStyleName));
        }

        if (!TimberAnnotationTextSettingsRules.IsValidTextStyleName(
                DimensionTextStyleName))
        {
            throw new ArgumentException(
                "Dimension text style identity is required.",
                nameof(DimensionTextStyleName));
        }

        if (!TimberAnnotationTextSettingsRules.IsValidItemCodePaperHeightMm(
                ItemPaperHeightMm))
        {
            throw new ArgumentOutOfRangeException(nameof(ItemPaperHeightMm));
        }

        if (!TimberAnnotationTextSettingsRules.IsValidDimensionPaperHeightMm(
                DimensionPaperHeightMm))
        {
            throw new ArgumentOutOfRangeException(nameof(DimensionPaperHeightMm));
        }

        if (ItemTextStyleId.IsNull || !ItemTextStyleId.IsValid)
        {
            throw new ArgumentException(
                "Item TextStyleId must be a valid ObjectId.",
                nameof(ItemTextStyleId));
        }

        if (Presentation == TimberFramedBlockContentPresentation.Combined &&
            (DimensionTextStyleId.IsNull || !DimensionTextStyleId.IsValid))
        {
            throw new ArgumentException(
                "Dimension TextStyleId must be a valid ObjectId for Combined.",
                nameof(DimensionTextStyleId));
        }

        if (string.IsNullOrWhiteSpace(ItemNoText))
        {
            throw new ArgumentException(
                "ITEM_NO text is required.",
                nameof(ItemNoText));
        }

        if (Presentation == TimberFramedBlockContentPresentation.Combined &&
            (string.IsNullOrWhiteSpace(WidthText) ||
             string.IsNullOrWhiteSpace(HeightText)))
        {
            throw new ArgumentException(
                "Combined presentation requires non-empty WIDTH and HEIGHT text.");
        }

        if (Presentation == TimberFramedBlockContentPresentation.ItemOnly &&
            (!string.IsNullOrEmpty(WidthText) || !string.IsNullOrEmpty(HeightText)))
        {
            throw new ArgumentException(
                "ItemOnly presentation must not supply WIDTH/HEIGHT text.",
                nameof(WidthText));
        }

        if (FirstSegmentLengthModelMm <= 0d ||
            double.IsNaN(FirstSegmentLengthModelMm) ||
            double.IsInfinity(FirstSegmentLengthModelMm))
        {
            throw new ArgumentOutOfRangeException(
                nameof(FirstSegmentLengthModelMm));
        }

        // G5 BlockContent layout requires a positive landing (dogleg) length;
        // legacy FramedItemLandingDistanceMm=0 is not valid on this path.
        if (LandingLengthModelMm <= 0d ||
            double.IsNaN(LandingLengthModelMm) ||
            double.IsInfinity(LandingLengthModelMm))
        {
            throw new ArgumentOutOfRangeException(nameof(LandingLengthModelMm));
        }

        if (LayerId.IsNull || !LayerId.IsValid)
        {
            throw new ArgumentException(
                "LayerId must be a valid ObjectId.",
                nameof(LayerId));
        }

        if (!Enum.IsDefined(
                typeof(AutoCadFramedBlockContentStabilizationMode),
                StabilizationMode))
        {
            throw new ArgumentOutOfRangeException(nameof(StabilizationMode));
        }

        if (ContentKind != TimberFramedBlockContentKind.Plain)
        {
            if (FrameWidthMm <= 0d || FrameHeightMm <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(FrameWidthMm),
                    "Framed kinds require positive resolved frame size.");
            }
        }

        return this with
        {
            ItemTextStyleName = ItemTextStyleName.Trim(),
            DimensionTextStyleName = DimensionTextStyleName.Trim(),
            ItemNoText = ItemNoText.Trim(),
            WidthText = WidthText?.Trim() ?? string.Empty,
            HeightText = HeightText?.Trim() ?? string.Empty,
            AnnotationScaleDenominator =
                TimberAnnotationScaleRules.NormalizeDenominator(
                    AnnotationScaleDenominator),
        };
    }

    public Point3d AttachmentWorld => new(AttachmentX, AttachmentY, 0d);

    public double BlockScale =>
        TimberAnnotationScaleRules.GetScaleFactor(AnnotationScaleDenominator);

    /// <summary>
    /// AttrRef height written on the instance (paper × default 1:50).
    /// BlockScale carries the per-element ScaleFactor once (P2 / production).
    /// Effective on-screen model height = AttrRef × BlockScale = paper × denom.
    /// </summary>
    public double ItemAttributeBaselineHeightMm =>
        TimberFramedBlockContentDefinitionRules.CalculateBaselineItemModelHeightMm(
            ItemPaperHeightMm);

    public double DimensionAttributeBaselineHeightMm =>
        TimberFramedBlockContentDefinitionRules
            .CalculateBaselineDimensionModelHeightMm(DimensionPaperHeightMm);

    public double ItemEffectiveModelHeightMm =>
        TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            ItemPaperHeightMm,
            AnnotationScaleDenominator);

    public double DimensionEffectiveModelHeightMm =>
        TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            DimensionPaperHeightMm,
            AnnotationScaleDenominator);
}

internal enum AutoCadFramedBlockContentAnnotationResultKind
{
    Created,
    InvalidRequest,
    DefinitionFailed,
    DatabaseMismatch,
    HostFailure,
}

internal sealed record AutoCadFramedBlockContentAnnotationResult(
    AutoCadFramedBlockContentAnnotationResultKind Kind,
    ObjectId? LeaderId,
    string? LeaderHandle,
    string? ResolvedBlockName,
    ObjectId? BlockTableRecordId,
    ContentType? ContentType,
    int LeaderCount,
    int VertexCount,
    IReadOnlyList<string> AttributeTags,
    double ItemAttrRefHeightMm,
    double DimensionAttrRefHeightMm,
    double ItemEffectiveModelHeightMm,
    double DimensionEffectiveModelHeightMm,
    double BlockScale,
    Point3d? AttachmentWorld,
    Point3d? KneeWorld,
    Point3d? LandingEndWorld,
    double ReadableAngleRadians,
    double RowClearGapModelMm,
    AutoCadFramedBlockContentStabilizationMode StabilizationMode,
    double AttachmentDriftMm,
    double KneeDriftMm,
    double LandingDriftMm,
    string DiagnosticReason)
{
    public bool Succeeded =>
        Kind == AutoCadFramedBlockContentAnnotationResultKind.Created &&
        LeaderId is ObjectId id &&
        !id.IsNull;

    public static AutoCadFramedBlockContentAnnotationResult Fail(
        AutoCadFramedBlockContentAnnotationResultKind kind,
        AutoCadFramedBlockContentStabilizationMode stabilizationMode,
        string reason) =>
        new(
            kind,
            null,
            null,
            null,
            null,
            null,
            0,
            0,
            Array.Empty<string>(),
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            null,
            null,
            null,
            double.NaN,
            double.NaN,
            stabilizationMode,
            0d,
            0d,
            0d,
            reason);
}
