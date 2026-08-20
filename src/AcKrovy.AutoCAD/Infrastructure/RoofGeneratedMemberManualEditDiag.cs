#if DEBUG
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG-only rejected generated-member manual-edit telemetry. One line per failed owner.
/// </summary>
internal static class RoofGeneratedMemberManualEditDiag
{
    private const string Prefix = "ROOF_MANUAL_EDIT_REJECT";
    private const string AnnotationFailPrefix = "ROOF_MANUAL_EDIT_ANNOTATION_FAIL";

    public static void Write(
        Editor? editor,
        string? command,
        string? owner,
        string? handle,
        string? key,
        string state,
        string stage,
        string reason,
        string? before,
        string? after)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"{Prefix} command={Token(command)} owner={Token(owner)} handle={Token(handle)}" +
            $" key={Token(key)} state={Token(state)} stage={Token(stage)} reason={Token(reason)}" +
            $" before={Token(before)} after={Token(after)}";
        WriteLine(editor, line);
    }

    public static void WriteAnnotationFail(
        Editor? editor,
        string? command,
        string? owner,
        string? timber,
        string? key,
        string? annotation,
        string? kind,
        string stage,
        string reason,
        string? exception,
        string status)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"{AnnotationFailPrefix} command={Token(command)} owner={Token(owner)}" +
            $" timber={Token(timber)} key={Token(key)} annotation={Token(annotation)}" +
            $" kind={Token(kind)} stage={Token(stage)} reason={Token(reason)}" +
            $" exception={Token(exception)} status={Token(status)}";
        WriteLine(editor, line);
    }

    public static void WriteRecalc(
        Editor? editor,
        string? command,
        string? owner,
        int changed,
        int signatureGroupsChanged,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"ROOF_MANUAL_EDIT_RECALC command={Token(command)} owner={Token(owner)}" +
            $" changed={changed.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" signatureGroupsChanged={signatureGroupsChanged.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteRecalcItem(
        Editor? editor,
        string? handle,
        string? oldSignature,
        string? newSignature,
        string? oldNumber,
        string? newNumber)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"ROOF_MANUAL_EDIT_RECALC_ITEM handle={Token(handle)}" +
            $" oldSignature={Token(oldSignature)} newSignature={Token(newSignature)}" +
            $" oldNumber={Token(oldNumber)} newNumber={Token(newNumber)}";
        WriteLine(editor, line);
    }

    public static void WriteIdentitySync(
        Editor? editor,
        string? handle,
        string? key,
        string? oldReserved,
        string? finalElementId,
        string? newReserved,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"ROOF_MANUAL_IDENTITY_SYNC handle={Token(handle)} key={Token(key)}" +
            $" oldReserved={Token(oldReserved)} finalElementId={Token(finalElementId)}" +
            $" newReserved={Token(newReserved)} result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteAccept(
        Editor? editor,
        string? command,
        string? owner,
        int changed,
        string? key,
        string action)
    {
        if (editor is null)
        {
            return;
        }

        var line = string.Equals(action, "suppress", StringComparison.OrdinalIgnoreCase)
            ? $"ROOF_MANUAL_EDIT_ACCEPT command={Token(command)} owner={Token(owner)}" +
              $" key={Token(key)} action=suppress result=ok"
            : $"ROOF_MANUAL_EDIT_ACCEPT command={Token(command)} owner={Token(owner)}" +
              $" changed={changed.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
              $" result={Token(action)}";
        WriteLine(editor, line);
    }

    public static void WriteAttachedManualErase(
        Editor? editor,
        string? command,
        string? owner,
        string? handle,
        string? origin,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"ROOF_ATTACHED_MANUAL_ERASE command={Token(command)} owner={Token(owner)}" +
            $" handle={Token(handle)} origin={Token(origin)} action=permanent-delete" +
            $" annotationCleanup=true recoverySuppressed=true result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteNormalize(
        Editor? editor,
        string? command,
        string? owner,
        string? handle,
        double rawZDelta,
        double planeZ)
    {
        if (editor is null)
        {
            return;
        }

        var line = $"ROOF_MANUAL_EDIT_NORMALIZE command={Token(command)} owner={Token(owner)}" +
                   $" handle={Token(handle)} rawZDelta={rawZDelta.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}" +
                   $" planeZ={planeZ.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} result=projected";
        WriteLine(editor, line);
    }

    public static void WriteComposeFail(
        Editor? editor,
        string? command,
        string? owner,
        string? handle,
        string? key,
        string stage,
        string canonical,
        string baseline,
        string observed,
        double existingRotation,
        double existingAlong,
        double existingLateral,
        double existingStartOffset,
        double existingEndOffset,
        double candidateRotation,
        double candidateAlong,
        double candidateLateral,
        string replay,
        double maxErrorMm,
        string reason)
    {
        if (editor is null)
        {
            return;
        }

        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var line =
            $"ROOF_MANUAL_EDIT_COMPOSE_FAIL command={Token(command)} owner={Token(owner)}" +
            $" handle={Token(handle)} key={Token(key)} stage={Token(stage)}" +
            $" canonical={Token(canonical)} baseline={Token(baseline)} observed={Token(observed)}" +
            $" existingRotation={existingRotation.ToString("0.######", invariant)}" +
            $" existingAlong={existingAlong.ToString("0.###", invariant)}" +
            $" existingLateral={existingLateral.ToString("0.###", invariant)}" +
            $" existingStartOffset={existingStartOffset.ToString("0.###", invariant)}" +
            $" existingEndOffset={existingEndOffset.ToString("0.###", invariant)}" +
            $" candidateRotation={candidateRotation.ToString("0.######", invariant)}" +
            $" candidateAlong={candidateAlong.ToString("0.###", invariant)}" +
            $" candidateLateral={candidateLateral.ToString("0.###", invariant)}" +
            $" replay={Token(replay)}" +
            $" maxErrorMm={maxErrorMm.ToString("0.###", invariant)}" +
            $" reason={Token(reason)}";
        WriteLine(editor, line);
    }

    public static void WriteGeneratedCopy(
        Editor? editor,
        string? source,
        string? clone,
        string? owner,
        string action,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"ROOF_GENERATED_COPY source={Token(source)} clone={Token(clone)}" +
            $" owner={Token(owner)} action={Token(action)} result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteGeneratedSplit(
        Editor? editor,
        string? command,
        string? owner,
        string? generatedFragment,
        string? standaloneFragment,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"ROOF_GENERATED_SPLIT command={Token(command)} owner={Token(owner)}" +
            $" generatedFragment={Token(generatedFragment)}" +
            $" standaloneFragment={Token(standaloneFragment)} result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteAttachedManualSplit(
        Editor? editor,
        string? command,
        string? owner,
        string? sourceFragment,
        string? newFragment,
        string? anchor,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"ROOF_ATTACHED_MANUAL_SPLIT command={Token(command)} owner={Token(owner)}" +
            $" sourceRole=AttachedManual origin=Split" +
            $" sourceFragment={Token(sourceFragment)} newFragment={Token(newFragment)}" +
            $" anchor={Token(anchor)} result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteRecalcFail(
        Editor? editor,
        string? command,
        string? owner,
        string? handle,
        string stage,
        string reason)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"ROOF_MANUAL_EDIT_RECALC_FAIL command={Token(command)} owner={Token(owner)}" +
            $" handle={Token(handle)} stage={Token(stage)} reason={Token(reason)}";
        WriteLine(editor, line);
    }

    private static void WriteLine(Editor editor, string line)
    {
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    private static string Token(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return value.Trim()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(' ', '_');
    }
}
#endif
