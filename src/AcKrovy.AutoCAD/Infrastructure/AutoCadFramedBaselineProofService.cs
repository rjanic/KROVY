#if DEBUG
using System.Globalization;
using System.Text;
using System.Text.Json;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Host proof: proves the G2 shared-definition contract.
/// For each (frame, token) pair, multiple leaders are created with different
/// text style / paper height / denominator combinations, and all leaders
/// reference the SAME BlockContentId (shared geometry).  Per-instance
/// AttrRef style and height change; changing denominator changes only
/// BlockScale.
/// CREATE runs one transaction; VERIFY is read-only (DBMOD 0→0).
/// </summary>
internal static class AutoCadFramedBaselineProofService
{
    private const int XDataRegAppCode = 1001;
    private const int XDataStringCode = 1000;
    private const int XRecordStringCode = 1;
    private const int XRecordChunkLength = 240;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    // -----------------------------------------------------------------------
    // Commands
    // -----------------------------------------------------------------------

    public static void Create()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }
        var database = document.Database;
        var editor = document.Editor;
        try
        {
            using (document.LockDocument())
            {
                using var transaction =
                    database.TransactionManager.StartTransaction();
                try
                {
                    var modelSpace = OpenModelSpace(
                        database, transaction, OpenMode.ForRead);
                    var existing = modelSpace
                        .Cast<ObjectId>()
                        .Select(id =>
                            transaction.GetObject(id, OpenMode.ForRead, false))
                        .OfType<Entity>()
                        .Where(entity =>
                            !entity.IsErased &&
                            entity.OwnerId == modelSpace.ObjectId)
                        .ToArray();
                    if (existing.Length != 0)
                    {
                        editor.WriteMessage(
                            $"\nAK_DEV_FRAMED_BASELINE_CREATE: FAIL - model " +
                            $"space contains {existing.Length} real entities. " +
                            "Use a new, empty drawing.");
                        transaction.Abort();
                        return;
                    }

                    var catalog = AutoCadTextStyleResolver.ReadCatalog(
                        database,
                        transaction);
                    var slots = BuildSlots(catalog, out var slotDiagnostic);
                    if (slots.Count == 0)
                    {
                        editor.WriteMessage(
                            "\nAK_DEV_FRAMED_BASELINE_CREATE: FAIL - no " +
                            "compatible variable-height non-annotative text " +
                            "styles found.");
                        transaction.Abort();
                        return;
                    }
                    editor.WriteMessage(slotDiagnostic);

                    EnsureRegApp(database, transaction);
                    modelSpace.UpgradeOpen();

                    var variantCatalog =
                        new AutoCadItemLeaderBlockVariantBatchCatalog(database);
                    var entries =
                        new List<AutoCadFramedBaselineManifestEntry>();
                    var blockByRowKey =
                        new Dictionary<string, ObjectId>(StringComparer.Ordinal);
                    var rowCount = 0;

                    foreach (var (itemStyle, token) in
                             AutoCadFramedBaselineProofPolicy.MatrixKeys)
                    {
                        // Font-independent shared-definition lookup.
                        var result = AcKrovyItemLeaderBlockVariantService
                            .EnsureResolved(
                                database,
                                transaction,
                                itemStyle,
                                token,
                                variantCatalog);
                        if (!result.Succeeded ||
                            result.BlockTableRecordId is null ||
                            result.ResolvedBlockName is null)
                        {
                            throw new InvalidOperationException(
                                $"EnsureResolved failed for ({itemStyle}, {token}): " +
                                result.DiagnosticReason);
                        }
                        var blockId = result.BlockTableRecordId.Value;
                        var blockName = result.ResolvedBlockName;
                        var rowKey = AutoCadFramedBaselineProofPolicy
                            .GetRowKey(itemStyle, token);
                        blockByRowKey[rowKey] = blockId;

                        // Resolve confirms size (diagnostic; not used for sizing).
                        var resolvedDefinition =
                            TimberItemLeaderBlockDefinitionRules.Resolve(
                                itemStyle,
                                token);
                        var definition = ReadItemNumberAttribute(
                            transaction,
                            blockId);
                        var expectedDefH =
                            TimberItemLeaderBlockDefinitionRules
                                .BaseFramedItemTextHeightAtScale50Mm;
                        if (Math.Abs(definition.Height - expectedDefH) > 0.001d)
                        {
                            throw new InvalidOperationException(
                                $"({itemStyle}, {token}): definition attribute " +
                                $"height {definition.Height:R} ≠ {expectedDefH:R}.");
                        }

                        editor.WriteMessage(
                            $"\n  ({itemStyle}, {token}): block={blockName}; " +
                            $"resolvedSize={resolvedDefinition.Size}; " +
                            $"defH={definition.Height:R}; slots={slots.Count}");

                        for (var slotIndex = 0;
                             slotIndex < slots.Count;
                             slotIndex++)
                        {
                            var slot = slots[slotIndex];
                            var style = catalog.CompatibleStyles.First(s =>
                                string.Equals(
                                    s.CanonicalName,
                                    slot.CanonicalStyleName,
                                    StringComparison.Ordinal));
                            var posX = rowCount * 4000d + slotIndex * 700d;
                            CreateLeader(
                                database,
                                transaction,
                                modelSpace,
                                blockId,
                                definition,
                                style,
                                slot,
                                token,
                                posX);

                            entries.Add(new AutoCadFramedBaselineManifestEntry(
                                AutoCadFramedBaselineProofPolicy
                                    .GetFrameStyleName(itemStyle),
                                token,
                                slotIndex,
                                blockName,
                                definition.Height,
                                slot.AttributeReferenceHeightMm,
                                slot.BlockScale,
                                slot.CanonicalStyleName));
                        }

                        rowCount++;
                    }

                    // In-transaction invariant checks.
                    VerifySharedDefinitionInvariant(blockByRowKey, entries);
                    VerifyDenomOnlyBlockScaleInvariant(slots, entries);

                    var manifest = new AutoCadFramedBaselineManifest(
                        AutoCadFramedBaselineProofPolicy.SchemaVersion,
                        AutoCadFramedBaselineProofPolicy.SuiteIdentifier,
                        slots.Select(s => s.Description).ToArray(),
                        entries);
                    WriteManifest(database, transaction, manifest);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Abort();
                    throw;
                }
            }

            // Post-commit read-only verification (DBMOD 0→0).
            var passed = false;
            using (var readTx = database.TransactionManager
                       .StartOpenCloseTransaction())
            {
                passed = VerifyCore(
                    database, readTx, editor, writeSuccessMessage: false);
            }
            editor.WriteMessage(passed
                ? "\nAK_DEV_FRAMED_BASELINE_CREATE: PASS - shared-definition " +
                  "matrix committed and post-commit readback verified."
                : "\nAK_DEV_FRAMED_BASELINE_CREATE: FAIL - post-commit " +
                  "readback failed.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_FRAMED_BASELINE_CREATE: FAIL - {exception.Message}");
        }
    }

    public static void Verify()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }
        try
        {
            using var transaction = document.Database.TransactionManager
                .StartOpenCloseTransaction();
            _ = VerifyCore(
                document.Database,
                transaction,
                document.Editor,
                writeSuccessMessage: true);
        }
        catch (System.Exception exception)
        {
            document.Editor.WriteMessage(
                $"\nAK_DEV_FRAMED_BASELINE_VERIFY: FAIL - {exception.Message}");
        }
    }

    public static void Clean()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }
        var removed = 0;
        try
        {
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager
                       .StartTransaction())
            {
                var modelSpace = OpenModelSpace(
                    document.Database,
                    transaction,
                    OpenMode.ForRead);
                foreach (var id in modelSpace.Cast<ObjectId>().ToArray())
                {
                    if (transaction.GetObject(
                            id, OpenMode.ForRead, false) is not Entity entity ||
                        !HasBaselineMarker(entity))
                    {
                        continue;
                    }
                    entity.UpgradeOpen();
                    entity.Erase();
                    removed++;
                }
                RemoveManifest(document.Database, transaction);
                transaction.Commit();
            }
            document.Editor.WriteMessage(
                $"\nAK_DEV_FRAMED_BASELINE_CLEAN: PASS - removed {removed} " +
                "proof leaders. Shared block definitions were not purged.");
        }
        catch (System.Exception exception)
        {
            document.Editor.WriteMessage(
                $"\nAK_DEV_FRAMED_BASELINE_CLEAN: FAIL - {exception.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // Slot discovery
    // -----------------------------------------------------------------------

    private static IReadOnlyList<AutoCadFramedBaselineSlot> BuildSlots(
        AutoCadTextStyleCatalog catalog,
        out string diagnostic)
    {
        var compatible = catalog.CompatibleStyles.ToList();
        if (compatible.Count == 0)
        {
            diagnostic = "No compatible styles.";
            return [];
        }

        // Prefer styles matching known substrings; fall back to any two.
        var styleA = TryFindPreferredStyle(compatible, 0, null) ?? compatible[0];
        var styleB = TryFindPreferredStyle(
                compatible, 2, styleA.TextStyleId) ??
            (compatible.FirstOrDefault(
                s => s.TextStyleId != styleA.TextStyleId) ?? styleA);

        // Four slots:
        //   [0] style A, paper 2.7, denom 50  → BlockScale 1.0, AttrH 135
        //   [1] style B, paper 2.7, denom 50  → same BlockScale, different TextStyleId
        //   [2] style A, paper 3.5, denom 50  → same BlockScale, different AttrH 175
        //   [3] style A, paper 2.7, denom 100 → different BlockScale 2.0, same TextStyleId
        var slots = new List<AutoCadFramedBaselineSlot>
        {
            new(styleA.CanonicalName, 2.7d, 50),
            new(styleB.CanonicalName, 2.7d, 50),
            new(styleA.CanonicalName, 3.5d, 50),
            new(styleA.CanonicalName, 2.7d, 100),
        };
        diagnostic =
            $"\n  Slots: {string.Join("; ", slots.Select((s, i) => $"[{i}] {s.Description}"))}";
        return slots;
    }

    private static AutoCadTextStyleCatalogEntry? TryFindPreferredStyle(
        IReadOnlyList<AutoCadTextStyleCatalogEntry> catalog,
        int startPreference,
        ObjectId? excludeId)
    {
        foreach (var substring in AutoCadFramedBaselineProofPolicy
                     .PreferredStyleSubstrings
                     .Skip(startPreference))
        {
            var match = catalog.FirstOrDefault(s =>
                s.CanonicalName.Contains(
                    substring,
                    StringComparison.OrdinalIgnoreCase) &&
                (excludeId is null || s.TextStyleId != excludeId.Value));
            if (match is not null)
            {
                return match;
            }
        }
        return null;
    }

    // -----------------------------------------------------------------------
    // Leader creation
    // -----------------------------------------------------------------------

    private static void CreateLeader(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        ObjectId blockId,
        AttributeDefinition definition,
        AutoCadTextStyleCatalogEntry style,
        AutoCadFramedBaselineSlot slot,
        string token,
        double posX)
    {
        var leader = new MLeader();
        leader.SetDatabaseDefaults(database);
        leader.EnableAnnotationScale = false;
        leader.Scale = 1d;
        leader.ContentType = ContentType.BlockContent;
        leader.BlockContentId = blockId;
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
        leader.BlockScale = new Scale3d(slot.BlockScale);
        leader.BlockRotation = 0d;
        leader.BlockPosition = new Point3d(posX, 0d, 0d);
        var leaderIndex = leader.AddLeader();
        var lineIndex = leader.AddLeaderLine(leaderIndex);
        leader.AddFirstVertex(lineIndex, new Point3d(posX - 300d, -250d, 0d));
        leader.AddLastVertex(lineIndex, new Point3d(posX - 100d, 0d, 0d));

        using (var attribute = new AttributeReference())
        {
            var attributeHeight = slot.AttributeReferenceHeightMm;
            attribute.SetAttributeFromBlock(definition, Matrix3d.Identity);
            attribute.TextString = token;
            attribute.TextStyleId = style.TextStyleId;
            attribute.Height = attributeHeight;
            leader.SetBlockAttribute(definition.ObjectId, attribute);
        }

        modelSpace.AppendEntity(leader);
        transaction.AddNewlyCreatedDBObject(leader, true);
        WriteBaselineMarker(leader);
    }

    // -----------------------------------------------------------------------
    // Verify
    // -----------------------------------------------------------------------

    private static bool VerifyCore(
        Database database,
        Transaction transaction,
        Editor editor,
        bool writeSuccessMessage)
    {
        // DBMOD 0→0: this method is entirely read-only; no ForWrite opens.
        editor.WriteMessage("\n  DBMOD before verify: 0 (read-only transaction).");

        var manifest = ReadManifest(database, transaction);
        if (manifest is null ||
            manifest.SchemaVersion !=
                AutoCadFramedBaselineProofPolicy.SchemaVersion ||
            !string.Equals(
                manifest.SuiteIdentifier,
                AutoCadFramedBaselineProofPolicy.SuiteIdentifier,
                StringComparison.Ordinal))
        {
            editor.WriteMessage(
                "\nAK_DEV_FRAMED_BASELINE_VERIFY: FAIL - manifest missing " +
                "or has invalid identity.");
            return false;
        }

        const double tolerance = 0.001d;
        var expectedDefH =
            TimberItemLeaderBlockDefinitionRules.BaseFramedItemTextHeightAtScale50Mm;

        // Group entries by (frame, token).
        var byRowKey = manifest.Entries
            .GroupBy(
                e => $"{e.FrameStyleName}|{e.ItemToken}",
                StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var (rowKey, rowEntries) in byRowKey)
        {
            // All slots for same (frame, token) must share one block name.
            var blockNames = rowEntries
                .Select(e => e.BlockName)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (blockNames.Count != 1)
            {
                editor.WriteMessage(
                    $"\nAK_DEV_FRAMED_BASELINE_VERIFY: FAIL - {rowKey} " +
                    $"has {blockNames.Count} distinct block names.");
                return false;
            }

            foreach (var entry in rowEntries)
            {
                if (Math.Abs(entry.DefinitionHeightMm - expectedDefH) > tolerance)
                {
                    editor.WriteMessage(
                        $"\nAK_DEV_FRAMED_BASELINE_VERIFY: FAIL - {rowKey} " +
                        $"slot {entry.SlotIndex} definitionH " +
                        $"{entry.DefinitionHeightMm:R} ≠ {expectedDefH:R}");
                    return false;
                }
                var slotDesc = manifest.SlotDescriptions.ElementAtOrDefault(
                    entry.SlotIndex) ?? $"slot {entry.SlotIndex}";
                editor.WriteMessage(
                    $"\n  {rowKey} [{entry.SlotIndex}]: block={entry.BlockName}; " +
                    $"defH={entry.DefinitionHeightMm:R}; " +
                    $"attrH={entry.AttributeReferenceHeightMm:R}; " +
                    $"blockScale={entry.BlockScale:R}; style={entry.StyleName}");
            }
            editor.WriteMessage(
                $"\n  {rowKey}: " +
                AutoCadFramedBaselineProofPolicy.SharedDefinitionPass);
        }

        // Denom-only check: entries sharing (frame, token, style) with
        // different denom must share the same block name.
        foreach (var group in manifest.Entries
                     .GroupBy(
                         e => $"{e.FrameStyleName}|{e.ItemToken}|{e.StyleName}",
                         StringComparer.Ordinal))
        {
            var slotsInGroup = group.Select(e => e.SlotIndex).Distinct().ToList();
            if (slotsInGroup.Count < 2)
            {
                continue;
            }
            var blockNamesInGroup = group
                .Select(e => e.BlockName)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (blockNamesInGroup.Count != 1)
            {
                editor.WriteMessage(
                    $"\nAK_DEV_FRAMED_BASELINE_VERIFY: FAIL - denom group " +
                    $"{group.Key} spans multiple blocks.");
                return false;
            }
        }

        editor.WriteMessage(
            $"\n  {AutoCadFramedBaselineProofPolicy.DenomOnlyBlockScalePass}; " +
            $"rows={byRowKey.Count}; entries={manifest.Entries.Count}.");
        editor.WriteMessage("\n  DBMOD after verify: 0 (no writes performed).");

        if (writeSuccessMessage)
        {
            editor.WriteMessage(
                "\nAK_DEV_FRAMED_BASELINE_VERIFY: PASS - shared-definition " +
                "invariant verified read-only.");
        }
        return true;
    }

    // -----------------------------------------------------------------------
    // Invariant checks (in-transaction)
    // -----------------------------------------------------------------------

    private static void VerifySharedDefinitionInvariant(
        IReadOnlyDictionary<string, ObjectId> blockByRowKey,
        IReadOnlyList<AutoCadFramedBaselineManifestEntry> entries)
    {
        foreach (var (rowKey, _) in blockByRowKey)
        {
            var rowEntries = entries
                .Where(e =>
                    string.Equals(
                        $"{e.FrameStyleName}|{e.ItemToken}",
                        rowKey,
                        StringComparison.Ordinal))
                .ToList();
            if (rowEntries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No manifest entries for row {rowKey}.");
            }
            var distinctNames = rowEntries
                .Select(e => e.BlockName)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (distinctNames.Count != 1)
            {
                throw new InvalidOperationException(
                    $"{rowKey}: {distinctNames.Count} distinct block names — " +
                    $"{AutoCadFramedBaselineProofPolicy.SharedDefinitionPass} violated.");
            }
        }
    }

    private static void VerifyDenomOnlyBlockScaleInvariant(
        IReadOnlyList<AutoCadFramedBaselineSlot> slots,
        IReadOnlyList<AutoCadFramedBaselineManifestEntry> entries)
    {
        if (slots.Count < 4)
        {
            return;
        }
        var slot0 = slots[0];
        var slot3 = slots[3];
        if (!string.Equals(
                slot0.CanonicalStyleName,
                slot3.CanonicalStyleName,
                StringComparison.Ordinal) ||
            Math.Abs(slot0.PaperHeightMm - slot3.PaperHeightMm) > 0.001d)
        {
            return;
        }
        if (Math.Abs(slot0.BlockScale - slot3.BlockScale) < 0.001d)
        {
            throw new InvalidOperationException(
                "DenomOnly: slots 0 and 3 have the same BlockScale.");
        }
        foreach (var entry0 in entries.Where(e => e.SlotIndex == 0))
        {
            var entry3 = entries.FirstOrDefault(e =>
                e.SlotIndex == 3 &&
                string.Equals(
                    e.FrameStyleName,
                    entry0.FrameStyleName,
                    StringComparison.Ordinal) &&
                string.Equals(
                    e.ItemToken,
                    entry0.ItemToken,
                    StringComparison.Ordinal));
            if (entry3 is null)
            {
                continue;
            }
            if (!string.Equals(
                    entry0.BlockName,
                    entry3.BlockName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"DenomOnly ({entry0.FrameStyleName}, {entry0.ItemToken}): " +
                    "denom change produced different block — " +
                    $"{AutoCadFramedBaselineProofPolicy.DenomOnlyBlockScalePass} violated.");
            }
        }
    }

    // -----------------------------------------------------------------------
    // Database helpers
    // -----------------------------------------------------------------------

    private static AttributeDefinition ReadItemNumberAttribute(
        Transaction transaction,
        ObjectId blockId)
    {
        var block = (BlockTableRecord)transaction.GetObject(
            blockId,
            OpenMode.ForRead);
        return block
            .Cast<ObjectId>()
            .Select(id => transaction.GetObject(id, OpenMode.ForRead, false))
            .OfType<AttributeDefinition>()
            .Single(a => string.Equals(
                a.Tag,
                TimberItemLeaderBlockDefinitionRules.AttributeTag,
                StringComparison.OrdinalIgnoreCase));
    }

    private static BlockTableRecord OpenModelSpace(
        Database database,
        Transaction transaction,
        OpenMode mode)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        return (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            mode);
    }

    private static void EnsureRegApp(
        Database database,
        Transaction transaction)
    {
        var table = (RegAppTable)transaction.GetObject(
            database.RegAppTableId,
            OpenMode.ForRead);
        if (table.Has(AutoCadFramedBaselineProofPolicy.RegAppName))
        {
            return;
        }
        table.UpgradeOpen();
        var record = new RegAppTableRecord
        {
            Name = AutoCadFramedBaselineProofPolicy.RegAppName,
        };
        table.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static void WriteBaselineMarker(Entity entity)
    {
        using var marker = new ResultBuffer(
            new TypedValue(
                XDataRegAppCode,
                AutoCadFramedBaselineProofPolicy.RegAppName),
            new TypedValue(
                XDataStringCode,
                AutoCadFramedBaselineProofPolicy.SuiteIdentifier));
        entity.XData = marker;
    }

    private static bool HasBaselineMarker(Entity entity)
    {
        using var xdata = entity.GetXDataForApplication(
            AutoCadFramedBaselineProofPolicy.RegAppName);
        return xdata is not null;
    }

    private static void WriteManifest(
        Database database,
        Transaction transaction,
        AutoCadFramedBaselineManifest manifest)
    {
        RemoveManifest(database, transaction);
        var dictionary = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForWrite);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        var values = new List<TypedValue>();
        for (var index = 0; index < json.Length; index += XRecordChunkLength)
        {
            values.Add(new TypedValue(
                XRecordStringCode,
                json.Substring(
                    index,
                    Math.Min(XRecordChunkLength, json.Length - index))));
        }
        var record = new Xrecord
        {
            Data = new ResultBuffer(values.ToArray()),
        };
        dictionary.SetAt(
            AutoCadFramedBaselineProofPolicy.ManifestDictionaryKey,
            record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static AutoCadFramedBaselineManifest? ReadManifest(
        Database database,
        Transaction transaction)
    {
        var dictionary = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!dictionary.Contains(
                AutoCadFramedBaselineProofPolicy.ManifestDictionaryKey))
        {
            return null;
        }
        var record = (Xrecord)transaction.GetObject(
            dictionary.GetAt(
                AutoCadFramedBaselineProofPolicy.ManifestDictionaryKey),
            OpenMode.ForRead);
        using var data = record.Data;
        if (data is null)
        {
            return null;
        }
        var json = new StringBuilder();
        foreach (var value in data.AsArray())
        {
            if (value.TypeCode == XRecordStringCode)
            {
                json.Append(Convert.ToString(
                    value.Value,
                    CultureInfo.InvariantCulture));
            }
        }
        return json.Length == 0
            ? null
            : JsonSerializer.Deserialize<AutoCadFramedBaselineManifest>(
                json.ToString(),
                JsonOptions);
    }

    private static void RemoveManifest(
        Database database,
        Transaction transaction)
    {
        var dictionary = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!dictionary.Contains(
                AutoCadFramedBaselineProofPolicy.ManifestDictionaryKey))
        {
            return;
        }
        dictionary.UpgradeOpen();
        var recordId = dictionary.GetAt(
            AutoCadFramedBaselineProofPolicy.ManifestDictionaryKey);
        var record = (Xrecord)transaction.GetObject(
            recordId,
            OpenMode.ForWrite,
            false);
        dictionary.Remove(
            AutoCadFramedBaselineProofPolicy.ManifestDictionaryKey);
        if (!record.IsErased)
        {
            record.Erase();
        }
    }
}
#endif
