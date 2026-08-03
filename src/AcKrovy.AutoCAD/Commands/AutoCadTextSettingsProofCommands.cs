#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

public sealed class AutoCadTextSettingsProofCommands
{
    [CommandMethod("AK_DEV_TEXT_SETTINGS_CREATE", CommandFlags.Modal)]
    public void Create() => AutoCadTextSettingsProofService.Create();

    [CommandMethod("AK_DEV_TEXT_SETTINGS_VERIFY", CommandFlags.Modal)]
    public void Verify() => AutoCadTextSettingsProofService.Verify();

    [CommandMethod("AK_DEV_TEXT_SETTINGS_CLEAN", CommandFlags.Modal)]
    public void Clean() => AutoCadTextSettingsProofService.Clean();

    [CommandMethod("AK_DEV_TEXT_G3_CLEAN", CommandFlags.Modal)]
    public void G3Clean() => AutoCadTextSettingsProofService.Clean();

    [CommandMethod("AK_DEV_TEXT_G3_CREATE", CommandFlags.Modal)]
    public void G3Create() => AutoCadTextSettingsProofService.Create();

    [CommandMethod("AK_DEV_TEXT_G3_VERIFY", CommandFlags.Modal)]
    public void G3Verify() => AutoCadTextSettingsProofService.Verify();

    [CommandMethod("AK_DEV_TEXT_G3_MIGRATE_CREATE", CommandFlags.Modal)]
    public void G3MigrateCreate() =>
        AutoCadTextSettingsProofService.MigrateCreate();

    [CommandMethod("AK_DEV_TEXT_G3_MIGRATE_VERIFY", CommandFlags.Modal)]
    public void G3MigrateVerify() =>
        AutoCadTextSettingsProofService.MigrateVerify();
}
#endif
