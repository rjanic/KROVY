#if DEBUG
using System.Globalization;
using System.Text;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG-only same-DWG COPY ownership probe for roof-generated rafters.
/// Observe-only: no DWG writes. Not for production use.
/// </summary>
internal static class RoofGeneratedTimberCopyOwnershipDiagService
{
    private const string Banner = "AK_DEV_ROOF_GENERATED_OWNER_DIAG";
    private const string ReplaceBanner = "AK_DEV_ROOF_RAFTER_REPLACE_DIAG";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;
    private const int DxfOwnerHandleCode = (int)DxfCode.ExtendedDataHandle;

    private static readonly object Gate = new();
    private static bool _replaceDiagEnabled;

    public static bool IsReplaceDiagEnabled
    {
        get
        {
            lock (Gate)
            {
                return _replaceDiagEnabled;
            }
        }
    }

    public static void EnableReplaceDiag()
    {
        lock (Gate)
        {
            _replaceDiagEnabled = true;
        }

        Write(AcApplication.DocumentManager.MdiActiveDocument?.Editor, $"{ReplaceBanner}: ON");
    }

    public static void DisableReplaceDiag()
    {
        lock (Gate)
        {
            _replaceDiagEnabled = false;
        }

        Write(AcApplication.DocumentManager.MdiActiveDocument?.Editor, $"{ReplaceBanner}: OFF");
    }

    public static void WriteReplaceDiag(Editor? editor, string message)
    {
        if (!IsReplaceDiagEnabled || editor is null)
        {
            return;
        }

        Write(editor, $"{ReplaceBanner}: {message}");
    }

    public static void RunOwnerDiag()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        Write(editor, $"{Banner}: DEVELOPMENT-ONLY ownership probe (read-only)");

        if (!TryPick(editor, "Select ORIGINAL roof source Polyline", typeof(Polyline), out var originalSourceId) ||
            !TryPick(editor, "Select COPIED roof source Polyline", typeof(Polyline), out var copiedSourceId) ||
            !TryPick(editor, "Select ONE ORIGINAL generated rafter Line", typeof(Line), out var originalRafterId) ||
            !TryPick(editor, "Select ONE COPIED generated rafter Line", typeof(Line), out var copiedRafterId))
        {
            Write(editor, $"{Banner}: cancelled.");
            return;
        }

        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            DumpSource(editor, transaction, "ORIGINAL_SOURCE", originalSourceId);
            DumpSource(editor, transaction, "COPIED_SOURCE", copiedSourceId);

            var originalHandle = ReadHandle(transaction, originalSourceId);
            var copiedHandle = ReadHandle(transaction, copiedSourceId);
            var originalMatches = RoofGeneratedTimberStore.FindByOwner(
                document.Database,
                transaction,
                originalHandle);
            var copiedMatches = RoofGeneratedTimberStore.FindByOwner(
                document.Database,
                transaction,
                copiedHandle);

            Write(
                editor,
                $"FIND_BY_OWNER originalHandle={originalHandle} count={originalMatches.Count}");
            Write(
                editor,
                $"FIND_BY_OWNER copiedHandle={copiedHandle} count={copiedMatches.Count}");

            DumpRafter(
                editor,
                transaction,
                "ORIGINAL_RAFTER",
                originalRafterId,
                originalHandle,
                copiedHandle,
                originalMatches,
                copiedMatches);
            DumpRafter(
                editor,
                transaction,
                "COPIED_RAFTER",
                copiedRafterId,
                originalHandle,
                copiedHandle,
                originalMatches,
                copiedMatches);

            DumpDisplayOwnerSample(
                editor,
                document.Database,
                transaction,
                "ORIGINAL_SOURCE_DISPLAY",
                originalHandle);
            DumpDisplayOwnerSample(
                editor,
                document.Database,
                transaction,
                "COPIED_SOURCE_DISPLAY",
                copiedHandle);

            // No Commit — read-only probe.
        }

        Write(editor, $"{Banner}: done. Paste CLI output for diagnosis.");
    }

    private static void DumpSource(
        Editor editor,
        Transaction transaction,
        string label,
        ObjectId id)
    {
        if (transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity)
        {
            Write(editor, $"{label}: unresolved ObjectId={id}");
            return;
        }

        Write(editor, $"{label}: ObjectId={id} Handle={entity.Handle} Type={entity.GetType().Name}");
        var definition = RoofDefinitionStore.Read(entity);
        Write(
            editor,
            $"{label}: RoofDefinitionExists={definition.Data is not null} Schema={definition.Data?.SchemaVersion.ToString(CultureInfo.InvariantCulture) ?? "-"}");
    }

    private static void DumpRafter(
        Editor editor,
        Transaction transaction,
        string label,
        ObjectId id,
        string originalOwnerHandle,
        string copiedOwnerHandle,
        IReadOnlyList<ObjectId> originalMatches,
        IReadOnlyList<ObjectId> copiedMatches)
    {
        if (transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity)
        {
            Write(editor, $"{label}: unresolved ObjectId={id}");
            return;
        }

        Write(editor, $"{label}: ObjectId={id} Handle={entity.Handle} Type={entity.GetType().Name}");

        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        if (metadataStore.TryRead(entity, out var timber) && timber is not null)
        {
            Write(
                editor,
                $"{label}: TimberElementId={timber.ElementId} Type={timber.ElementType} W={timber.WidthMm} H={timber.HeightMm} Material={timber.Material}");
        }
        else
        {
            Write(editor, $"{label}: TimberElementId=<missing>");
        }

        DumpRawGeneratedTimberXData(editor, entity, label);

        var stored = RoofGeneratedTimberStore.Read(entity);
        if (stored.Data is null)
        {
            Write(
                editor,
                $"{label}: RoofGeneratedTimberStore.Read Exists={stored.Exists} Error={stored.Error} EffectiveOwner=<none>");
        }
        else
        {
            Write(
                editor,
                $"{label}: EffectiveOwner={stored.Data.RoofOwnerReference} From1005={stored.OwnerReferenceFromCloneHandle} Kind={stored.Data.MemberKind} Face={stored.Data.RoofFace} Station={stored.Data.StationIndex}/{stored.Data.StationCount} Spacing={stored.Data.RequestedMaximumSpacingMm} LayoutSig={stored.Data.LayoutSignature}");
            Write(
                editor,
                $"{label}: JsonOwnerField(rawPayload)={TryReadJsonOwnerOnly(entity) ?? "<unreadable>"}");
        }

        Write(
            editor,
            $"{label}: IncludedIn FindByOwner(original={originalOwnerHandle})={originalMatches.Contains(id)}");
        Write(
            editor,
            $"{label}: IncludedIn FindByOwner(copied={copiedOwnerHandle})={copiedMatches.Contains(id)}");
    }

    private static void DumpRawGeneratedTimberXData(Editor editor, Entity entity, string label)
    {
        try
        {
            using var xdata = entity.GetXDataForApplication(RoofGeneratedTimberStore.RegAppName);
            if (xdata is null)
            {
                Write(editor, $"{label}: RawXData[{RoofGeneratedTimberStore.RegAppName}]=<missing>");
                return;
            }

            var values = xdata.AsArray();
            Write(editor, $"{label}: RawXDataCount={values.Length}");
            string? raw1005 = null;
            var payload = new StringBuilder();
            for (var index = 0; index < values.Length; index++)
            {
                var value = values[index];
                var printed = Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "<null>";
                var runtimeType = value.Value?.GetType().FullName ?? "<null>";
                Write(
                    editor,
                    $"{label}: Raw[{index}] Code={(int)value.TypeCode}({DescribeDxf((int)value.TypeCode)}) Value={printed} ClrType={runtimeType}");

                if ((int)value.TypeCode == DxfOwnerHandleCode && raw1005 is null)
                {
                    raw1005 = printed;
                }

                if ((int)value.TypeCode == DxfAsciiStringCode)
                {
                    payload.Append(printed);
                }
            }

            Write(editor, $"{label}: Raw1005={raw1005 ?? "<absent>"}");
            Write(editor, $"{label}: RawAsciiPayload={payload}");
        }
        catch (System.Exception ex)
        {
            Write(editor, $"{label}: RawXDataError={ex.GetType().Name}:{ex.Message}");
        }
    }

    private static string? TryReadJsonOwnerOnly(Entity entity)
    {
        try
        {
            using var xdata = entity.GetXDataForApplication(RoofGeneratedTimberStore.RegAppName);
            if (xdata is null)
            {
                return null;
            }

            var payload = new StringBuilder();
            foreach (var value in xdata.AsArray())
            {
                if ((int)value.TypeCode == DxfAsciiStringCode)
                {
                    payload.Append(Convert.ToString(value.Value, CultureInfo.InvariantCulture));
                }
            }

            if (!RoofGeneratedTimberDataCodec.TryDecode(payload.ToString(), out var data, out _) ||
                data is null)
            {
                return null;
            }

            return data.RoofOwnerReference;
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    private static void DumpDisplayOwnerSample(
        Editor editor,
        Database database,
        Transaction transaction,
        string label,
        string ownerHandle)
    {
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity ||
                entity.IsErased)
            {
                continue;
            }

            var display = RoofDisplayStore.Read(entity);
            if (display.Data is null ||
                !string.Equals(
                    display.Data.OwnerReference,
                    ownerHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Write(
                editor,
                $"{label}: sampleDisplay ObjectId={id} Handle={entity.Handle} EffectiveOwner={display.Data.OwnerReference} From1005={display.OwnerReferenceFromCloneHandle} Role={display.Data.Role}");
            try
            {
                using var xdata = entity.GetXDataForApplication(RoofDisplayStore.RegAppName);
                if (xdata is null)
                {
                    Write(editor, $"{label}: display RawXData=<missing>");
                    return;
                }

                foreach (var value in xdata.AsArray())
                {
                    if ((int)value.TypeCode != DxfOwnerHandleCode)
                    {
                        continue;
                    }

                    Write(
                        editor,
                        $"{label}: display Raw1005={Convert.ToString(value.Value, CultureInfo.InvariantCulture)} ClrType={value.Value?.GetType().FullName ?? "<null>"}");
                    return;
                }

                Write(editor, $"{label}: display Raw1005=<absent>");
            }
            catch (System.Exception ex)
            {
                Write(editor, $"{label}: display RawXDataError={ex.GetType().Name}:{ex.Message}");
            }

            return;
        }

        Write(editor, $"{label}: no display child found for owner={ownerHandle}");
    }

    private static string ReadHandle(Transaction transaction, ObjectId id) =>
        transaction.GetObject(id, OpenMode.ForRead, false) is Entity entity
            ? entity.Handle.ToString()
            : string.Empty;

    private static bool TryPick(
        Editor editor,
        string message,
        Type allowedType,
        out ObjectId id)
    {
        id = ObjectId.Null;
        var options = new PromptEntityOptions($"\n{Banner}: {message}");
        options.SetRejectMessage($"\n{Banner}: wrong entity type.");
        options.AddAllowedClass(allowedType, exactMatch: false);
        var result = editor.GetEntity(options);
        if (result.Status != PromptStatus.OK)
        {
            return false;
        }

        id = result.ObjectId;
        return true;
    }

    private static string DescribeDxf(int code) => code switch
    {
        DxfRegAppNameCode => "1001-RegApp",
        DxfAsciiStringCode => "1000-Ascii",
        DxfOwnerHandleCode => "1005-Handle",
        _ => "other",
    };

    private static void Write(Editor? editor, string message) =>
        editor?.WriteMessage("\n" + message);
}
#endif
