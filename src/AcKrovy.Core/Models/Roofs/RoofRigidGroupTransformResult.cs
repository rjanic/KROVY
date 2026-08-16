namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Result of classifying a coherent rigid transform of a complete SimpleGable roof GROUP
/// (semantic source + seven display edges) relative to a true pre-command baseline.
/// </summary>
public sealed record RoofRigidGroupTransformResult(
    bool IsAccepted,
    string TransformKind,
    double DeltaX,
    double DeltaY,
    double DeltaZ,
    string RejectionReason)
{
    public static RoofRigidGroupTransformResult Reject(string reason) =>
        new(false, string.Empty, 0d, 0d, 0d, reason);

    public static RoofRigidGroupTransformResult AcceptTranslation(
        double deltaX,
        double deltaY,
        double deltaZ) =>
        new(true, "Translation", deltaX, deltaY, deltaZ, string.Empty);
}
