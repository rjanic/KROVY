#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG P4A lifecycle trace / proof switches. Default OFF after NETLOAD.
/// </summary>
public sealed class AutoCadFramedBlockContentStretchNormalizeLifecycleCommands
{
    [CommandMethod("AK_DEV_FBC_LIFECYCLE_TRACE_ON", CommandFlags.Modal)]
    public void TraceOn()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var session =
            AutoCadFramedBlockContentStretchNormalizeLifecycleService.GetOrCreateSession(
                document);
        session.TraceEnabled = true;
        document.Editor.WriteMessage("\nAK_DEV_FBC_LIFECYCLE_TRACE_ON");
        AutoCadFramedBlockContentStretchNormalizeLifecycleService.WriteStatus(document);
    }

    [CommandMethod("AK_DEV_FBC_LIFECYCLE_TRACE_OFF", CommandFlags.Modal)]
    public void TraceOff()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var session =
            AutoCadFramedBlockContentStretchNormalizeLifecycleService.GetOrCreateSession(
                document);
        session.TraceEnabled = false;
        document.Editor.WriteMessage("\nAK_DEV_FBC_LIFECYCLE_TRACE_OFF");
    }

    [CommandMethod("AK_DEV_FBC_LIFECYCLE_PROOF_ON", CommandFlags.Modal)]
    public void ProofOn()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var session =
            AutoCadFramedBlockContentStretchNormalizeLifecycleService.GetOrCreateSession(
                document);
        session.ProofEnabled = true;
        document.Editor.WriteMessage("\nAK_DEV_FBC_LIFECYCLE_PROOF_ON");
        document.Editor.WriteMessage(
            "\nUNDO_BLOCKER: " +
            AutoCadFramedBlockContentStretchNormalizeLifecycleService.UndoBlockerReason);
        if (session.ConfirmedCommandNames.Count == 0)
        {
            document.Editor.WriteMessage(
                "\nNo confirmed commands yet. Run TRACE, grip-stretch once, then " +
                "AK_DEV_FBC_LIFECYCLE_CONFIRM.");
        }

        AutoCadFramedBlockContentStretchNormalizeLifecycleService.WriteStatus(document);
    }

    [CommandMethod("AK_DEV_FBC_LIFECYCLE_PROOF_OFF", CommandFlags.Modal)]
    public void ProofOff()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var session =
            AutoCadFramedBlockContentStretchNormalizeLifecycleService.GetOrCreateSession(
                document);
        session.ProofEnabled = false;
        document.Editor.WriteMessage("\nAK_DEV_FBC_LIFECYCLE_PROOF_OFF");
    }

    [CommandMethod("AK_DEV_FBC_LIFECYCLE_STATUS", CommandFlags.Modal)]
    public void Status()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        AutoCadFramedBlockContentStretchNormalizeLifecycleService.WriteStatus(document);
    }

    [CommandMethod("AK_DEV_FBC_LIFECYCLE_CONFIRM", CommandFlags.Modal)]
    public void Confirm()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var session =
            AutoCadFramedBlockContentStretchNormalizeLifecycleService.GetOrCreateSession(
                document);
        var editor = document.Editor;
        var options = new PromptStringOptions(
            "\nConfirm GlobalCommandName (empty = last observed): ")
        {
            AllowSpaces = false,
        };
        var result = editor.GetString(options);
        if (result.Status != PromptStatus.OK)
        {
            editor.WriteMessage("\nCONFIRM cancelled.");
            return;
        }

        var input = result.StringResult?.Trim() ?? string.Empty;
        bool confirmed;
        if (input.Length == 0)
        {
            confirmed = session.ConfirmLastObservedCommand();
            if (!confirmed)
            {
                editor.WriteMessage(
                    "\nCONFIRM failed: no observed command. Enable TRACE and grip-stretch first.");
                return;
            }
        }
        else
        {
            confirmed = session.ConfirmCommand(input);
        }

        editor.WriteMessage(
            confirmed
                ? "\nAK_DEV_FBC_LIFECYCLE_CONFIRM ok"
                : "\nAK_DEV_FBC_LIFECYCLE_CONFIRM already present or empty");
        AutoCadFramedBlockContentStretchNormalizeLifecycleService.WriteStatus(document);
    }

    [CommandMethod("AK_DEV_FBC_LIFECYCLE_TEST_ON", CommandFlags.Modal)]
    public void TestOn()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        AutoCadFramedBlockContentStretchNormalizeLifecycleService.ArmLifecycleTest(
            document);
        document.Editor.WriteMessage("\nLifecycle test armed for GRIP_STRETCH.");
        AutoCadFramedBlockContentStretchNormalizeLifecycleService.WriteStatus(document);
    }

    [CommandMethod("AK_DEV_FBC_LIFECYCLE_TEST_OFF", CommandFlags.Modal)]
    public void TestOff()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        AutoCadFramedBlockContentStretchNormalizeLifecycleService.DisarmLifecycleTest(
            document);
        document.Editor.WriteMessage("\nLifecycle test disabled.");
    }
}
#endif
