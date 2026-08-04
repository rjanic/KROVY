#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

public sealed class AutoCadTextSettingsProofCommands
{
    [CommandMethod("AK_DEV_TEXT_STYLE_AUDIT", CommandFlags.Modal)]
    public void AuditStyles() => AutoCadTextStyleAuditService.Run();

    [CommandMethod("AK_DEV_TEXT_FRESH_DRAWING_CREATE", CommandFlags.Modal)]
    public void FreshDrawingCreate() =>
        AutoCadTextSettingsProofService.FreshDrawingCreate();

    [CommandMethod("AK_DEV_TEXT_FRESH_DRAWING_VERIFY", CommandFlags.Modal)]
    public void FreshDrawingVerify() =>
        AutoCadTextSettingsProofService.FreshDrawingVerify();

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
