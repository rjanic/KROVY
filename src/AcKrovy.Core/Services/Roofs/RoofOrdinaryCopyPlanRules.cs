using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Ordinary COPY accepts rigid XY placement; Plan2D stays at Z=0
/// even when the native cursor displacement has a Z component.</summary>
public static class RoofOrdinaryCopyPlanRules
{
    public static bool TryAccept(RoofGeneratedMemberGeometry source,
        RoofGeneratedMemberGeometry native, out RoofGeneratedMemberGeometry accepted)
    {
        accepted = source;
        var coordinates = new[] { source.Start.X, source.Start.Y, source.Start.Z,
            source.End.X, source.End.Y, source.End.Z, native.Start.X, native.Start.Y,
            native.Start.Z, native.End.X, native.End.Y, native.End.Z };
        if (coordinates.Any(value => double.IsNaN(value) || double.IsInfinity(value)) ||
            Math.Abs(source.Start.Z) > RoofGeneratedMemberOverrideMath.LengthToleranceMm ||
            Math.Abs(source.End.Z) > RoofGeneratedMemberOverrideMath.LengthToleranceMm)
            return false;
        var planar = new RoofGeneratedMemberGeometry(
            new RoofPoint3D(native.Start.X, native.Start.Y, 0d),
            new RoofPoint3D(native.End.X, native.End.Y, 0d));
        return RoofGeneratedMemberOverrideMath.TryClassifyPureTranslation(
            source, planar, RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
            out _, out accepted, out _);
    }
}
