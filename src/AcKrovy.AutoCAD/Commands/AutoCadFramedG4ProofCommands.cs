#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

public sealed class AutoCadFramedG4ProofCommands
{
    [CommandMethod("AK_DEV_FRAMED_G4_CLEAN", CommandFlags.Modal)]
    public void Clean() => AutoCadFramedG4ProofService.Clean();

    [CommandMethod("AK_DEV_FRAMED_G4_CREATE", CommandFlags.Modal)]
    public void Create() => AutoCadFramedG4ProofService.Create();

    [CommandMethod("AK_DEV_FRAMED_G4_VERIFY", CommandFlags.Modal)]
    public void Verify() => AutoCadFramedG4ProofService.Verify();

    [CommandMethod("AK_DEV_FRAMED_G4_MIGRATE_CREATE", CommandFlags.Modal)]
    public void MigrateCreate() => AutoCadFramedG4ProofService.MigrateCreate();

    [CommandMethod("AK_DEV_FRAMED_G4_MIGRATE_VERIFY", CommandFlags.Modal)]
    public void MigrateVerify() => AutoCadFramedG4ProofService.MigrateVerify();
}
#endif
