using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum RoofExternalImportObjectRole
{
    Generated,
    AttachedManual,
    GenericTimber,
    ElementLabel,
    SlopeArrow,
    SlopeAngleText,
    PostFootprintPerpendicular,
}

internal sealed record RoofExternalImportObject(
    ObjectId SourceId,
    RoofExternalImportObjectRole Role,
    string? SourceHandle = null);

/// <summary>Immutable source authority frozen before target mutation.</summary>
internal sealed record RoofExternalImportManifest(
    string SourceIdentity,
    string DestinationBlockName,
    ObjectId SourceRootBlockId,
    IReadOnlyList<ObjectId> SourceObjectIds,
    IReadOnlyDictionary<ObjectId, IReadOnlyList<ObjectId>> DependencyGraph,
    IReadOnlyList<RoofExternalImportObject> IndividualTimbers,
    IReadOnlyList<RoofExternalImportObject> Annotations,
    IReadOnlyList<ObjectId> SupportBlockTableRecords,
    IReadOnlyList<string> ReachableUserBlockNames,
    IReadOnlyList<ObjectId> ExpectedCloneObjectIds,
    RoofExternalImportDecision Decision);
