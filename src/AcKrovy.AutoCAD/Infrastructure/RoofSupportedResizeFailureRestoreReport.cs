namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Runtime PRE/POST restore evidence for SupportedResize HardFailure finalization.</summary>
internal sealed record RoofSupportedResizeFailureRestoreReport(
    string OwnerHandle = "",
    bool SourceGeometryRestored = false,
    bool RoofDefinitionRestored = false,
    bool PhysicalInventoryRestored = false,
    bool AnnotationsRestored = false,
    bool GroupCanonical = false,
    int GroupMemberCount = 0,
    double? RigidFootprintEdge12Mm = null,
    double? SnapshotRigidFootprintEdge12Mm = null)
{
    public bool IsCompleteSuccess =>
        SourceGeometryRestored &&
        RoofDefinitionRestored &&
        PhysicalInventoryRestored &&
        AnnotationsRestored &&
        GroupCanonical;
}
