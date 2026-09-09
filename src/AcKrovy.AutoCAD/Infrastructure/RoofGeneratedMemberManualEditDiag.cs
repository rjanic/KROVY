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

    public static void WriteReplay(
        Editor? editor,
        string? owner,
        string? roofKind,
        int storedOverrideCount,
        int resolvedOverrideCount,
        int geometryReplayCount,
        int suppressedCount,
        int dormantCount,
        int dormantMissingKeyCount,
        int dormantInvalidDomainCount,
        int duplicateKeyCount,
        int oldGeneratedCount,
        int newGeneratedCount,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var line =
            $"ROOF_GENERATED_OVERRIDE_REPLAY owner={Token(owner)} roofKind={Token(roofKind)}" +
            $" stored={storedOverrideCount.ToString(invariant)}" +
            $" resolved={resolvedOverrideCount.ToString(invariant)}" +
            $" geometryReplayed={geometryReplayCount.ToString(invariant)}" +
            $" suppressed={suppressedCount.ToString(invariant)}" +
            $" dormant={dormantCount.ToString(invariant)}" +
            $" dormantMissingKey={dormantMissingKeyCount.ToString(invariant)}" +
            $" dormantInvalidDomain={dormantInvalidDomainCount.ToString(invariant)}" +
            $" duplicateKeyCount={duplicateKeyCount.ToString(invariant)}" +
            $" oldGenerated={oldGeneratedCount.ToString(invariant)}" +
            $" newGenerated={newGeneratedCount.ToString(invariant)}" +
            $" transaction=caller-owned-pending result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteOverrideDomain(
        Editor? editor,
        string? owner,
        string? logicalKey,
        string? oldFace,
        string? newFace,
        string? storedStart,
        string? storedEnd,
        string? canonicalStart,
        string? canonicalEnd,
        bool startInsideFootprint,
        bool endInsideFootprint,
        bool startInsideFace,
        bool endInsideFace,
        bool segmentIntersectsFace,
        string? domainResult,
        string? reason)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_GENERATED_OVERRIDE_DOMAIN" +
            $" owner={Token(owner)}" +
            $" logicalKey={Token(logicalKey)}" +
            $" oldFace={Token(oldFace)}" +
            $" newFace={Token(newFace)}" +
            $" storedStart={Token(storedStart)}" +
            $" storedEnd={Token(storedEnd)}" +
            $" canonicalStart={Token(canonicalStart)}" +
            $" canonicalEnd={Token(canonicalEnd)}" +
            $" startInsideFootprint={(startInsideFootprint ? "1" : "0")}" +
            $" endInsideFootprint={(endInsideFootprint ? "1" : "0")}" +
            $" startInsideFace={(startInsideFace ? "1" : "0")}" +
            $" endInsideFace={(endInsideFace ? "1" : "0")}" +
            $" segmentIntersectsFace={(segmentIntersectsFace ? "1" : "0")}" +
            $" domainResult={Token(domainResult)}" +
            $" reason={Token(reason)}";
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
        string? origin,
        string? resolution,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            $"ROOF_ATTACHED_MANUAL_SPLIT command={Token(command)} owner={Token(owner)}" +
            $" sourceRole=AttachedManual origin={Token(origin)}" +
            $" sourceFragment={Token(sourceFragment)} newFragment={Token(newFragment)}" +
            $" anchor={Token(anchor)} resolution={Token(resolution)} result={Token(result)}";
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

    public static void WriteGeneratedTamper(
        Editor? editor,
        string? owner,
        string? command,
        int modifiedGeneratedCount,
        int modifiedAnnotationCount,
        bool sourceModified,
        string? editState,
        string? classification,
        string? action)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_GENERATED_TAMPER" +
            $" owner={Token(owner)}" +
            $" command={Token(command)}" +
            $" modifiedGeneratedCount={modifiedGeneratedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" modifiedAnnotationCount={modifiedAnnotationCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" sourceModified={(sourceModified ? "1" : "0")}" +
            $" editState={Token(editState)}" +
            $" classification={Token(classification)}" +
            $" action={Token(action)}";
        WriteLine(editor, line);
    }

    public static void WriteGeneratedTamperRepair(
        Editor? editor,
        string? owner,
        string? command,
        int oldGenerated,
        int newGenerated,
        bool repairedGeometry,
        bool repairedAnnotations,
        int groupMembers,
        string? result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_GENERATED_TAMPER_REPAIR" +
            $" owner={Token(owner)}" +
            $" command={Token(command)}" +
            $" oldGenerated={oldGenerated.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" newGenerated={newGenerated.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" repairedGeometry={(repairedGeometry ? "1" : "0")}" +
            $" repairedAnnotations={(repairedAnnotations ? "1" : "0")}" +
            $" groupMembers={groupMembers.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteObjectErased(
        Editor? editor,
        string? objectId,
        string? handle,
        bool erased,
        bool unerased,
        string? command,
        string? document,
        string? database,
        string? mappedKind,
        string? mappedOwner,
        string? action)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_OBJECT_ERASED" +
            $" objectId={Token(objectId)}" +
            $" handle={Token(handle)}" +
            $" erased={(erased ? "1" : "0")}" +
            $" unerased={(unerased ? "1" : "0")}" +
            $" command={Token(command)}" +
            $" document={Token(document)}" +
            $" database={Token(database)}" +
            $" mappedKind={Token(mappedKind)}" +
            $" mappedOwner={Token(mappedOwner)}" +
            $" action={Token(action)}";
        WriteLine(editor, line);
    }

    public static void WriteSourceEraseTamper(
        Editor? editor,
        string? owner,
        string? command,
        string? sourceObjectId,
        string? sourceHandle,
        string? editStateAtStart,
        bool sourceErased,
        int displayErasedCount,
        string? classification,
        string? action)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_SOURCE_ERASE_TAMPER" +
            $" owner={Token(owner)}" +
            $" command={Token(command)}" +
            $" sourceObjectId={Token(sourceObjectId)}" +
            $" sourceHandle={Token(sourceHandle)}" +
            $" editStateAtStart={Token(editStateAtStart)}" +
            $" sourceErased={(sourceErased ? "1" : "0")}" +
            $" displayErasedCount={displayErasedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" classification={Token(classification)}" +
            $" action={Token(action)}";
        WriteLine(editor, line);
    }

    public static void WriteSourceEraseRepair(
        Editor? editor,
        string? owner,
        bool sourceRestored,
        bool sameObjectId,
        bool sameHandle,
        bool displayRebuilt,
        int groupMembers,
        bool canonical,
        string? result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_SOURCE_ERASE_REPAIR" +
            $" owner={Token(owner)}" +
            $" sourceRestored={(sourceRestored ? "1" : "0")}" +
            $" sameObjectId={(sameObjectId ? "1" : "0")}" +
            $" sameHandle={(sameHandle ? "1" : "0")}" +
            $" displayRebuilt={(displayRebuilt ? "1" : "0")}" +
            $" groupMembers={groupMembers.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" canonical={(canonical ? "1" : "0")}" +
            $" result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteDisplayEraseTamper(
        Editor? editor,
        string? owner,
        string? command,
        int erasedDisplayCount,
        bool sourceErased,
        bool sourceModified,
        string? editState,
        string? classification,
        string? action)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_DISPLAY_ERASE_TAMPER" +
            $" owner={Token(owner)}" +
            $" command={Token(command)}" +
            $" erasedDisplayCount={erasedDisplayCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" sourceErased={(sourceErased ? "1" : "0")}" +
            $" sourceModified={(sourceModified ? "1" : "0")}" +
            $" editState={Token(editState)}" +
            $" classification={Token(classification)}" +
            $" action={Token(action)}";
        WriteLine(editor, line);
    }

    public static void WriteDisplayEraseRepair(
        Editor? editor,
        string? owner,
        string? command,
        int expectedDisplay,
        int restoredDisplay,
        int groupMembers,
        bool canonical,
        string? result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_DISPLAY_ERASE_REPAIR" +
            $" owner={Token(owner)}" +
            $" command={Token(command)}" +
            $" expectedDisplay={expectedDisplay.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" restoredDisplay={restoredDisplay.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" groupMembers={groupMembers.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" canonical={(canonical ? "1" : "0")}" +
            $" result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteGeneratedEraseTamper(
        Editor? editor,
        string? owner,
        string? command,
        int erasedGeneratedCount,
        int erasedAnnotationCount,
        string? editStateAtStart,
        bool sourceErased,
        string? classification,
        string? action)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_GENERATED_ERASE_TAMPER" +
            $" owner={Token(owner)}" +
            $" command={Token(command)}" +
            $" erasedGeneratedCount={erasedGeneratedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" erasedAnnotationCount={erasedAnnotationCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" editStateAtStart={Token(editStateAtStart)}" +
            $" sourceErased={(sourceErased ? "1" : "0")}" +
            $" classification={Token(classification)}" +
            $" action={Token(action)}";
        WriteLine(editor, line);
    }

    public static void WriteGeneratedEraseRepair(
        Editor? editor,
        string? owner,
        int restoredGenerated,
        bool sameObjectId,
        bool sameHandle,
        bool identityPreserved,
        int groupMembers,
        bool canonical,
        string? result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_GENERATED_ERASE_REPAIR" +
            $" owner={Token(owner)}" +
            $" restoredGenerated={restoredGenerated.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" sameObjectId={(sameObjectId ? "1" : "0")}" +
            $" sameHandle={(sameHandle ? "1" : "0")}" +
            $" identityPreserved={(identityPreserved ? "1" : "0")}" +
            $" groupMembers={groupMembers.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" canonical={(canonical ? "1" : "0")}" +
            $" result={Token(result)}";
        WriteLine(editor, line);
    }

    public static void WriteGeneratedAnnotationEraseTamper(
        Editor? editor,
        string? owner,
        string? command,
        int erasedAnnotationCount,
        string? editStateAtStart,
        string? classification,
        string? action)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_GENERATED_ANNOTATION_ERASE_TAMPER" +
            $" owner={Token(owner)}" +
            $" command={Token(command)}" +
            $" erasedAnnotationCount={erasedAnnotationCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" editStateAtStart={Token(editStateAtStart)}" +
            $" classification={Token(classification)}" +
            $" action={Token(action)}";
        WriteLine(editor, line);
    }

    public static void WriteGeneratedAnnotationEraseRepair(
        Editor? editor,
        string? owner,
        int erasedAnnotationCount,
        int restoredAnnotationCount,
        bool sameObjectId,
        bool sameHandle,
        string? result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_GENERATED_ANNOTATION_ERASE_REPAIR" +
            $" owner={Token(owner)}" +
            $" erasedAnnotationCount={erasedAnnotationCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" restoredAnnotationCount={restoredAnnotationCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" sameObjectId={(sameObjectId ? "1" : "0")}" +
            $" sameHandle={(sameHandle ? "1" : "0")}" +
            $" result={Token(result)}";
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
