using System.Globalization;
using System.Text;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Dedicated secondary XData ownership store for roof-generated timber sources.
///
/// Canonical identity is stored as REDO-stable ASCII XData ONLY — schema/version, owner
/// handle as ASCII, role, member kind, face, station — under <see cref="RegAppName"/>.
/// No 1005 soft pointer is written: native U/REDO replays Entity.XData as a single
/// property assignment, so a failed 1005 replay would drop the entire canonical identity.
///
/// Backward compatibility: <see cref="Read"/> still accepts the legacy combined
/// ASCII+1005 payload and the experimental ROOF_TIMBER_LINK RegApp, but new writes use
/// the ASCII-only canonical format.
/// </summary>
internal static class RoofGeneratedTimberStore
{
    internal const string RegAppName = "DECORAIR_ACADKROVY_ROOF_TIMBER";
    internal const string LinkRegAppName = "DECORAIR_ACADKROVY_ROOF_TIMBER_LINK";
    private const int DxfRegAppNameCode = (int)DxfCode.ExtendedDataRegAppName;
    private const int DxfAsciiStringCode = (int)DxfCode.ExtendedDataAsciiString;
    private const int DxfOwnerHandleCode = (int)DxfCode.ExtendedDataHandle;
    private const int MaxTextChunkLength = 240;

    public static RoofGeneratedTimberStoreReadResult Read(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        try
        {
            // 1. Canonical ASCII identity (RegApp A). Accepts both the new ASCII-only
            //    format and the legacy combined ASCII+1005 format for backward compat.
            using var canonical = entity.GetXDataForApplication(RegAppName);
            if (canonical is null)
            {
                return RoofGeneratedTimberStoreReadResult.Missing;
            }

            var values = canonical.AsArray();
            if (values.Length < 2 ||
                values[0].TypeCode != DxfRegAppNameCode ||
                !string.Equals(
                    Convert.ToString(values[0].Value, CultureInfo.InvariantCulture),
                    RegAppName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return RoofGeneratedTimberStoreReadResult.Invalid(
                    RoofGeneratedTimberDataDecodeError.MalformedPayload);
            }

            var payload = new StringBuilder();
            string? legacyOwnerReference = null;
            for (var index = 1; index < values.Length; index++)
            {
                if (values[index].TypeCode == DxfAsciiStringCode)
                {
                    payload.Append(Convert.ToString(
                        values[index].Value,
                        CultureInfo.InvariantCulture));
                    continue;
                }

                if (values[index].TypeCode == DxfOwnerHandleCode)
                {
                    // Legacy combined payload carried the 1005 inside RegApp A.
                    if (legacyOwnerReference is null &&
                        TryNormalizeOwnerReference(
                            Convert.ToString(values[index].Value, CultureInfo.InvariantCulture),
                            out var legacyRemapped))
                    {
                        legacyOwnerReference = legacyRemapped;
                    }

                    continue;
                }

                return RoofGeneratedTimberStoreReadResult.Invalid(
                    RoofGeneratedTimberDataDecodeError.MalformedPayload);
            }

            var text = payload.ToString();
            if (!RoofGeneratedTimberDataCodec.TryDecode(
                    text,
                    out var data,
                    out var error) ||
                data is null)
            {
                return RoofGeneratedTimberStoreReadResult.Invalid(error);
            }

            // 2. Clone LINK section (RegApp B) takes precedence over any legacy 1005.
            var cloneSafeOwnerReference = ReadLinkOwner(entity) ?? legacyOwnerReference;
            if (cloneSafeOwnerReference is not null)
            {
                data = data with { RoofOwnerReference = cloneSafeOwnerReference };
            }

            return RoofGeneratedTimberStoreReadResult.Valid(
                data,
                ownerReferenceFromCloneHandle: cloneSafeOwnerReference is not null);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofGeneratedTimberStoreReadResult.Invalid(
                RoofGeneratedTimberDataDecodeError.MalformedPayload);
        }
    }

    /// <summary>Reads the 1005 soft pointer from the dedicated LINK RegApp (or null).</summary>
    public static string? ReadLinkOwner(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        try
        {
            using var link = entity.GetXDataForApplication(LinkRegAppName);
            if (link is null)
            {
                return null;
            }

            foreach (var value in link.AsArray())
            {
                if (value.TypeCode == DxfOwnerHandleCode &&
                    TryNormalizeOwnerReference(
                        Convert.ToString(value.Value, CultureInfo.InvariantCulture),
                        out var ownerReference))
                {
                    return ownerReference;
                }
            }

            return null;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return null;
        }
    }

    public static void Write(
        Entity entity,
        Transaction transaction,
        RoofGeneratedTimberData data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        if (!entity.IsWriteEnabled)
        {
            throw new InvalidOperationException(
                "Roof-generated timber source must be opened ForWrite.");
        }

        var retained = ReadForeignXData(entity);
        retained.AddRange(BuildSection(entity, transaction, data));
        using var buffer = new ResultBuffer(retained.ToArray());
        entity.XData = buffer;
    }

    /// <summary>
    /// Builds the canonical Generated ASCII section (RegApp + ASCII chunks) WITHOUT
    /// assigning Entity.XData, so a caller can merge it with the generic Timber section
    /// and perform a single atomic Entity.XData assignment. No 1005 is ever added.
    /// </summary>
    public static IReadOnlyList<TypedValue> BuildSection(
        Entity entity,
        Transaction transaction,
        RoofGeneratedTimberData data)
    {
        if (!TryNormalizeOwnerReference(data.RoofOwnerReference, out var ownerReference))
        {
            throw new ArgumentException(
                "Roof-generated timber owner reference must be a positive hexadecimal handle.",
                nameof(data));
        }

        data = data with { RoofOwnerReference = ownerReference };
        var payload = RoofGeneratedTimberDataCodec.Encode(data);
        EnsureRegAppRegistered(entity.Database, transaction, RegAppName);

        var values = new List<TypedValue>
        {
            new TypedValue(DxfRegAppNameCode, RegAppName),
        };
        values.AddRange(SplitIntoChunks(payload)
            .Select(chunk => new TypedValue(DxfAsciiStringCode, chunk)));
        return values;
    }

    /// <summary>
    /// Atomic single Entity.XData assignment for a newly created Generated entity:
    /// generic Timber section + canonical Generated ASCII section in ONE ResultBuffer.
    /// This avoids the REDO asymmetry where a later Generated setter assignment is
    /// dropped for a subset of entities while the generic Timber assignment survives.
    /// </summary>
    public static void WriteAtomic(
        Entity entity,
        Transaction transaction,
        TimberElementData genericData,
        IReadOnlyList<TypedValue> generatedSection)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(genericData);
        ArgumentNullException.ThrowIfNull(generatedSection);
        if (!entity.IsWriteEnabled)
        {
            throw new InvalidOperationException(
                "Roof-generated timber source must be opened ForWrite.");
        }

        var retained = ReadForeignXData(entity);
        retained.AddRange(ElementDataStore.BuildSection(entity, transaction, genericData));
        retained.AddRange(generatedSection);
        using var buffer = new ResultBuffer(retained.ToArray());
        entity.XData = buffer;
    }

    public static IReadOnlyList<ObjectId> FindByOwner(
        Database database,
        Transaction transaction,
        string ownerReference)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!TryNormalizeOwnerReference(ownerReference, out var normalizedOwner))
        {
            return [];
        }

        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        var matches = new List<ObjectId>();
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity ||
                entity.IsErased)
            {
                continue;
            }

            var stored = Read(entity);
            if (stored.Data is not null &&
                string.Equals(
                    stored.Data.RoofOwnerReference,
                    normalizedOwner,
                    StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(id);
            }
        }

        return matches;
    }

    public static void Clear(Entity entity, Transaction transaction)
    {
        if (!TryClear(entity, transaction, out var failureReason))
        {
            throw new InvalidOperationException(
                $"Roof-generated timber clear failed: {failureReason}");
        }
    }

    /// <summary>
    /// Removes both the canonical identity section and the clone LINK section using the
    /// AutoCAD RegApp-only sentinel (merge-safe). Other applications such as generic
    /// timber metadata remain on the entity.
    /// </summary>
    public static bool TryClear(
        Entity entity,
        Transaction transaction,
        out string failureReason)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transaction);
        failureReason = string.Empty;
        if (!entity.IsWriteEnabled)
        {
            failureReason = "entity-not-write-enabled";
            return false;
        }

        try
        {
            // The LINK RegApp is legacy-only and is no longer registered by current
            // Generated writes (ASCII-only canonical identity). Assigning a RegApp-only
            // erase sentinel for a section that is absent throws
            // RegisteredApplicationIdNotFound when that RegApp is missing from the
            // RegAppTable. Erase a section only when it is actually present.
            if (HasApplicationSection(entity, LinkRegAppName))
            {
                using var eraseLink = new ResultBuffer(
                    new TypedValue(DxfRegAppNameCode, LinkRegAppName));
                entity.XData = eraseLink;
            }

            if (HasApplicationSection(entity, RegAppName))
            {
                using var eraseCanonical = new ResultBuffer(
                    new TypedValue(DxfRegAppNameCode, RegAppName));
                entity.XData = eraseCanonical;
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            failureReason = "xdata-assign-failed:" + ex.ErrorStatus;
#if DEBUG
            WriteClearXDataFail(entity, transaction, ex.ErrorStatus.ToString());
#endif
            return false;
        }

        var read = Read(entity);
        if (read.Data is not null)
        {
            failureReason = "generated-xdata-remains";
#if DEBUG
            WriteGeneratedClearFail(entity, failureReason);
#endif
            return false;
        }

        if (read.Exists && read.Error != RoofGeneratedTimberDataDecodeError.None)
        {
            failureReason = "generated-xdata-remains:" + read.Error;
#if DEBUG
            WriteGeneratedClearFail(entity, failureReason);
#endif
            return false;
        }

        if (ReadLinkOwner(entity) is not null)
        {
            failureReason = "generated-link-xdata-remains";
#if DEBUG
            WriteGeneratedClearFail(entity, failureReason);
#endif
            return false;
        }

        using (var residual = entity.GetXDataForApplication(RegAppName))
        {
            if (residual is not null)
            {
                failureReason = "generated-xdata-remains:regapp-buffer-present";
#if DEBUG
                WriteGeneratedClearFail(entity, failureReason);
#endif
                return false;
            }
        }

        if (HasApplicationSection(entity, LinkRegAppName))
        {
            failureReason = "generated-link-xdata-remains:regapp-buffer-present";
#if DEBUG
            WriteGeneratedClearFail(entity, failureReason);
#endif
            return false;
        }

        return true;
    }

#if DEBUG
    private static void WriteGeneratedClearFail(Entity entity, string reason)
    {
        try
        {
            var editor = Autodesk.AutoCAD.ApplicationServices.Application
                .DocumentManager
                .MdiActiveDocument?
                .Editor;
            if (editor is null)
            {
                return;
            }

            var raw = DescribeRegAppXData(entity);
            editor.WriteMessage(
                "\nROOF_GENERATED_CLEAR_FAIL" +
                " handle=" + (entity.Handle.ToString() ?? "-") +
                " regapp=" + RegAppName +
                " raw=" + raw +
                " reason=" + reason);
        }
        catch
        {
        }
    }

    private static string DescribeRegAppXData(Entity entity)
    {
        try
        {
            using var xdata = entity.GetXDataForApplication(RegAppName);
            if (xdata is null)
            {
                return "<missing>";
            }

            var parts = new List<string>();
            foreach (var value in xdata.AsArray())
            {
                var text = Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "<null>";
                text = text.Replace(' ', '_');
                parts.Add(((int)value.TypeCode).ToString(CultureInfo.InvariantCulture) + ":" + text);
            }

            return parts.Count == 0 ? "<empty>" : string.Join(",", parts);
        }
        catch (System.Exception ex)
        {
            return "error:" + ex.GetType().Name;
        }
    }

    private static void WriteClearXDataFail(
        Entity entity,
        Transaction transaction,
        string errorStatus)
    {
        try
        {
            var editor = Autodesk.AutoCAD.ApplicationServices.Application
                .DocumentManager
                .MdiActiveDocument?
                .Editor;
            if (editor is null)
            {
                return;
            }

            var missing = MissingRegApps(entity, transaction);
            editor.WriteMessage(
                "\nROOF_COPY_XDATA_CLEAR" +
                " clone=" + (entity.Handle.ToString() ?? "-") +
                " existingApps=" + DescribePresentRegApps(entity) +
                " removedApp=" + (missing.Count == 0 ? "-" : missing[0]) +
                " missingApps=" + (missing.Count == 0 ? "-" : string.Join(",", missing)) +
                " result=failure");
        }
        catch
        {
        }
    }

    private static string DescribePresentRegApps(Entity entity)
    {
        var names = new List<string>();
        using var xdata = entity.XData;
        if (xdata is not null)
        {
            foreach (var value in xdata.AsArray())
            {
                if (value.TypeCode == DxfRegAppNameCode)
                {
                    names.Add(Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "-");
                }
            }
        }

        return names.Count == 0 ? "-" : string.Join(",", names);
    }

    private static List<string> MissingRegApps(Entity entity, Transaction transaction)
    {
        var missing = new List<string>();
        foreach (var name in new[] { RegAppName, LinkRegAppName })
        {
            try
            {
                var table = (RegAppTable)transaction.GetObject(
                    entity.Database.RegAppTableId,
                    OpenMode.ForRead);
                if (!table.Has(name))
                {
                    missing.Add(name);
                }
            }
            catch
            {
                missing.Add(name);
            }
        }

        return missing;
    }
#endif

    private static List<TypedValue> ReadForeignXData(Entity entity)
    {
        var retained = new List<TypedValue>();
        using var xdata = entity.XData;
        if (xdata is null)
        {
            return retained;
        }

        var skipSection = false;
        foreach (var value in xdata.AsArray())
        {
            if (value.TypeCode == DxfRegAppNameCode)
            {
                var name = Convert.ToString(value.Value, CultureInfo.InvariantCulture);
                skipSection =
                    string.Equals(name, RegAppName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, LinkRegAppName, StringComparison.OrdinalIgnoreCase);
            }

            if (!skipSection)
            {
                retained.Add(value);
            }
        }

        return retained;
    }

    /// <summary>
    /// True when the entity's raw XData contains a 1001 RegApp section with the given
    /// name. Reading Entity.XData does not require the RegApp to be registered, so this
    /// is safe to call for legacy RegApps that may be absent from the RegAppTable.
    /// </summary>
    private static bool HasApplicationSection(Entity entity, string regAppName)
    {
        using var xdata = entity.XData;
        if (xdata is null)
        {
            return false;
        }

        foreach (var value in xdata.AsArray())
        {
            if (value.TypeCode == DxfRegAppNameCode &&
                string.Equals(
                    Convert.ToString(value.Value, CultureInfo.InvariantCulture),
                    regAppName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureRegAppRegistered(Database database, Transaction transaction, string regAppName)
    {
        var table = (RegAppTable)transaction.GetObject(database.RegAppTableId, OpenMode.ForRead);
        if (table.Has(regAppName))
        {
            return;
        }

        table.UpgradeOpen();
        var record = new RegAppTableRecord { Name = regAppName };
        table.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static IEnumerable<string> SplitIntoChunks(string value)
    {
        for (var index = 0; index < value.Length; index += MaxTextChunkLength)
        {
            yield return value.Substring(index, Math.Min(MaxTextChunkLength, value.Length - index));
        }
    }

    private static bool TryNormalizeOwnerReference(
        string? value,
        out string normalized)
    {
        normalized = string.Empty;
        if (!long.TryParse(
                value,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var handleValue) || handleValue <= 0)
        {
            return false;
        }

        normalized = handleValue.ToString("X", CultureInfo.InvariantCulture);
        return true;
    }
}

internal sealed record RoofGeneratedTimberStoreReadResult(
    bool Exists,
    RoofGeneratedTimberData? Data,
    RoofGeneratedTimberDataDecodeError Error,
    bool OwnerReferenceFromCloneHandle = false)
{
    public static RoofGeneratedTimberStoreReadResult Missing { get; } =
        new(false, null, RoofGeneratedTimberDataDecodeError.None);

    public static RoofGeneratedTimberStoreReadResult Valid(
        RoofGeneratedTimberData data,
        bool ownerReferenceFromCloneHandle = false) =>
        new(true, data, RoofGeneratedTimberDataDecodeError.None, ownerReferenceFromCloneHandle);

    public static RoofGeneratedTimberStoreReadResult Invalid(
        RoofGeneratedTimberDataDecodeError error) =>
        new(true, null, error);
}
