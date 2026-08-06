#if DEBUG
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Structured result for reusable dogleg / content-side normalize (DEBUG).
/// </summary>
internal readonly struct AutoCadFramedBlockContentNormalizeResult
{
    public AutoCadFramedBlockContentNormalizeResult(
        bool applied,
        bool changed,
        string reason,
        ObjectId beforeBlockContentId,
        ObjectId afterBlockContentId,
        double attachmentDrift,
        double kneeDrift,
        double blockPositionDrift)
    {
        Applied = applied;
        Changed = changed;
        Reason = reason;
        BeforeBlockContentId = beforeBlockContentId;
        AfterBlockContentId = afterBlockContentId;
        AttachmentDrift = attachmentDrift;
        KneeDrift = kneeDrift;
        BlockPositionDrift = blockPositionDrift;
    }

    public bool Applied { get; }

    public bool Changed { get; }

    public string Reason { get; }

    public ObjectId BeforeBlockContentId { get; }

    public ObjectId AfterBlockContentId { get; }

    public double AttachmentDrift { get; }

    public double KneeDrift { get; }

    public double BlockPositionDrift { get; }

    public static AutoCadFramedBlockContentNormalizeResult NoOp(
        string reason,
        ObjectId blockContentId,
        Point3d attachment,
        Point3d knee,
        Point3d blockPosition) =>
        new(
            applied: true,
            changed: false,
            reason,
            blockContentId,
            blockContentId,
            0d,
            0d,
            0d);

    public static AutoCadFramedBlockContentNormalizeResult Failed(
        string reason,
        ObjectId blockContentId) =>
        new(
            applied: false,
            changed: false,
            reason,
            blockContentId,
            blockContentId,
            0d,
            0d,
            0d);
}
#endif
