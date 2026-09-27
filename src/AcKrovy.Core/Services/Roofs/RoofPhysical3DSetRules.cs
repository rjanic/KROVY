using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Exact owner/key/signature completeness, independent of CAD group membership.</summary>
public static class RoofPhysical3DSetRules
{
    public static bool ShouldEraseForSourceState(bool sourceResolved, bool isRoofSource, bool sourceIsErased) =>
        sourceResolved && isRoofSource && sourceIsErased;

    public static IReadOnlyList<RoofPhysical3DGeneratedData> ExpectedChildren(RoofPhysical3DModel model) =>
        model.Faces.Select(face => Child(model, RoofPhysical3DGeneratedRole.Face, face.PlaneId.Value))
            .Concat(model.Eaves.Select(edge => Child(model, RoofPhysical3DGeneratedRole.EaveEdge, edge.StructuralId)))
            .Concat(model.Hips.Select(edge => Child(model, RoofPhysical3DGeneratedRole.HipEdge, edge.StructuralId)))
            .Concat(model.Ridges.Select(edge => Child(model, RoofPhysical3DGeneratedRole.RidgeEdge, edge.StructuralId)))
            .ToArray();

    public static bool IsComplete(RoofPhysical3DModel model, IReadOnlyList<RoofPhysical3DGeneratedData?> actual)
    {
        var expected = ExpectedChildren(model);
        if (actual.Count != expected.Count || actual.Any(child => child is null)) return false;
        var remaining = expected.ToDictionary(child => (child.Role, child.StructuralId));
        foreach (var child in actual)
        {
            if (child is null || child.SchemaVersion != RoofPhysical3DGeneratedDataSchema.CurrentVersion ||
                !string.Equals(child.RoofOwnerReference, model.OwnerReference, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(child.GenerationSignature, model.GenerationSignature, StringComparison.Ordinal) ||
                !remaining.Remove((child.Role, child.StructuralId))) return false;
        }
        return remaining.Count == 0;
    }

    private static RoofPhysical3DGeneratedData Child(RoofPhysical3DModel model,
        RoofPhysical3DGeneratedRole role, string key) => RoofPhysical3DGeneratedDataRules.Create(
            model.OwnerReference, role, key, model.GenerationSignature);
}
