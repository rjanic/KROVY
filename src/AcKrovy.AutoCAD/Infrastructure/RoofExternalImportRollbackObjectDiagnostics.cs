#if DEBUG
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Read-only, supplemental evidence for the explicit C1 abort proof commands.</summary>
internal static class RoofExternalImportRollbackObjectDiagnostics
{
    internal sealed record CapturedObject(string Handle, string Type);

    internal static CapturedObject Capture(DBObject value) => new(
        Read(() => value.Handle.ToString()),
        Read(() => value.GetType().FullName ?? value.GetType().Name));

    internal static void Write(
        Editor editor,
        Transaction transaction,
        ObjectId id,
        CapturedObject? captured,
        bool absent)
    {
        // The caller has already sampled the unchanged absence predicate for ALL IDs.
        // Opening/metadata failures below are evidence only, never verdict inputs.
        var valid = Read(() => Bool(id.IsValid));
        var erased = Read(() => Bool(id.IsErased));
        var opened = false;
        var openError = "none";
        var runtimeType = "unavailable";
        var currentHandle = Read(() => id.Handle.ToString());
        var ownerId = "unavailable";
        var ownerValid = "unavailable";
        var ownerErased = "unavailable";
        try
        {
            var value = transaction.GetObject(id, OpenMode.ForRead, openErased: true);
            opened = value is not null;
            if (value is not null)
            {
                runtimeType = Read(() => value.GetType().FullName ?? value.GetType().Name);
                currentHandle = Read(() => value.Handle.ToString());
                ownerId = Read(() =>
                {
                    var owner = value.OwnerId;
                    ownerValid = Read(() => Bool(owner.IsValid));
                    ownerErased = Read(() => Bool(owner.IsErased));
                    return owner.ToString();
                });
            }
        }
        catch (System.Exception exception)
        {
            openError = Error(exception);
        }

        try
        {
            editor.WriteMessage("\nROOF_IMPORT_C1_ROLLBACK_OBJECT " +
                $"id={Read(() => id.ToString())} " +
                $"capturedHandle={captured?.Handle ?? "unavailable"} " +
                $"capturedType={captured?.Type ?? "unavailable"} " +
                $"isValid={valid} isErased={erased} canOpen={Bool(opened)} openError={openError} " +
                $"runtimeType={runtimeType} currentHandle={currentHandle} " +
                $"ownerId={ownerId} ownerIsValid={ownerValid} ownerIsErased={ownerErased} " +
                $"contributesFail={Bool(!absent)}");
        }
        catch { }
    }

    private static string Read(Func<string> read)
    {
        try { return read(); }
        catch (System.Exception exception) { return "unavailable:" + Error(exception); }
    }

    private static string Error(System.Exception exception) =>
        exception is Autodesk.AutoCAD.Runtime.Exception cadException
            ? cadException.ErrorStatus.ToString()
            : exception.GetType().Name;

    private static string Bool(bool value) => value ? "true" : "false";
}
#endif
