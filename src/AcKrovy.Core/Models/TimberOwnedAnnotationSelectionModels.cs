namespace AcKrovy.Core.Models;

/// <summary>
/// Production main-annotation representations supported by future Edit kóty
/// transforms. Circle/Rectangle/Slot stay on <see cref="FrameStyle"/> rather
/// than duplicating kinds.
/// </summary>
public enum TimberOwnedAnnotationRepresentationKind
{
    PlainItemOnly = 0,
    DimensionsOnly = 1,
    FramedItemOnly = 2,
    CombinedPlain = 3,
    R3Combined = 4,
}

/// <summary>
/// Neutral physical entity classification for owned annotation components.
/// Host adapters map CAD runtime types onto these values.
/// </summary>
public enum TimberOwnedAnnotationEntityKind
{
    Unknown = 0,
    MLeader = 1,
    MText = 2,
    Other = 3,
}

public enum TimberOwnedAnnotationSelectionDisposition
{
    Accepted = 0,
    Skipped = 1,
    Rejected = 2,
}

public enum TimberOwnedAnnotationSkipReason
{
    None = 0,
    DuplicateSelection = 1,
    UnrelatedEntity = 2,
    TimberSourceEntity = 3,
    AuxiliaryAnnotation = 4,
    NoLabelMetadata = 5,
    AlreadyConsumedByGroup = 6,
}

public enum TimberOwnedAnnotationRejectReason
{
    None = 0,
    UnsupportedRepresentation = 1,
    LegacyG4Composite = 2,
    RoleMismatch = 3,
    EntityKindMismatch = 4,
    IncompleteCombinedPlain = 5,
    AmbiguousOwnership = 6,
    UnresolvedSource = 7,
    DeadOrInvalidSource = 8,
    RendererGenerationMismatch = 9,
    ContentTypeMismatch = 10,
    ModeStyleMismatch = 11,
}

/// <summary>
/// CAD-neutral probe for one physical owned annotation component.
/// </summary>
public sealed record TimberOwnedAnnotationComponentProbe
{
    public string ComponentKey { get; init; } = string.Empty;
    public string SourceHandle { get; init; } = string.Empty;
    public string ElementId { get; init; } = string.Empty;
    public TimberAnnotationMode AnnotationMode { get; init; }
    public ItemNumberLeaderStyle ItemNumberLeaderStyle { get; init; }
    public TimberMainAnnotationComponentRole ComponentRole { get; init; }
    public int? RendererGeneration { get; init; }
    public TimberOwnedAnnotationEntityKind EntityKind { get; init; }
    public bool IsBlockContentMLeader { get; init; }
    public bool IsMTextContentMLeader { get; init; }
}

/// <summary>
/// CAD-neutral diagnostic snapshot for one complete logical annotation group.
/// </summary>
public sealed record TimberOwnedAnnotationGroupSnapshot
{
    public string LogicalGroupKey { get; init; } = string.Empty;
    public TimberOwnedAnnotationRepresentationKind RepresentationKind { get; init; }
    public ItemNumberLeaderStyle? FrameStyle { get; init; }
    public string SourceHandle { get; init; } = string.Empty;
    public string ElementId { get; init; } = string.Empty;
    public TimberAnnotationMode AnnotationMode { get; init; }
    public ItemNumberLeaderStyle ItemNumberLeaderStyle { get; init; }
    public int? RendererGeneration { get; init; }
    public int ComponentCount { get; init; }
    public string? MainLeaderComponentKey { get; init; }
    public IReadOnlyList<TimberOwnedAnnotationComponentSnapshot> Components { get; init; } =
        Array.Empty<TimberOwnedAnnotationComponentSnapshot>();
    public double? LiveAttachmentX { get; init; }
    public double? LiveAttachmentY { get; init; }
    public double? LiveContentWorldAngleRadians { get; init; }
    public bool ContentOrientationIsAbsoluteWorld { get; init; }
    public double? SourcePhysicalAxisAngleRadians { get; init; }
    public bool SourceResolved { get; init; }
}

public sealed record TimberOwnedAnnotationComponentSnapshot
{
    public string ComponentKey { get; init; } = string.Empty;
    public TimberMainAnnotationComponentRole ComponentRole { get; init; }
    public TimberOwnedAnnotationEntityKind EntityKind { get; init; }
    public bool IsMainLeader { get; init; }
}

public sealed record TimberOwnedAnnotationAcceptedGroup(
    TimberOwnedAnnotationGroupSnapshot Snapshot);

public sealed record TimberOwnedAnnotationSkippedItem(
    string? ComponentKey,
    TimberOwnedAnnotationSkipReason Reason);

public sealed record TimberOwnedAnnotationRejectedGroup(
    string? LogicalGroupKey,
    string? SourceHandle,
    TimberOwnedAnnotationRejectReason Reason,
    IReadOnlyList<string> ComponentKeys);

public sealed record TimberOwnedAnnotationSelectionEvaluation(
    IReadOnlyList<TimberOwnedAnnotationAcceptedGroup> Accepted,
    IReadOnlyList<TimberOwnedAnnotationSkippedItem> Skipped,
    IReadOnlyList<TimberOwnedAnnotationRejectedGroup> Rejected);
