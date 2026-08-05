#if DEBUG
using System.Globalization;
using System.IO;
using System.Text;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG-only BEFORE/AFTER dump for LEFT Combined BlockContent knee STRETCH drift.
/// No automatic fix. Pair second run against the same MLeader handle to compute deltas.
/// </summary>
internal static class AutoCadFramedBlockContentLeftStretchDiagService
{
    private const string CommandBanner = "AK_DEV_FBC_LEFT_STRETCH_DIAG";
    private const string SnapshotFileName = "ak_dev_fbc_left_stretch_diag_last.txt";

    private static readonly object CacheGate = new();
    private static string? _cachedHandle;
    private static Snapshot? _cachedSnapshot;

    public static void Run()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        editor.WriteMessage($"\n=== {CommandBanner} ===");
        editor.WriteMessage(
            "\nDiagnostic only — no create-path mutation, no AttrRef rewrite.");

        using var documentLock = document.LockDocument();
        var database = document.Database;
        using var transaction = database.TransactionManager.StartTransaction();

        if (!TryResolveLeader(editor, transaction, out var leader, out var resolveNote))
        {
            editor.WriteMessage($"\n{resolveNote}");
            transaction.Commit();
            return;
        }

        var handle = leader.ObjectId.Handle.ToString();
        editor.WriteMessage($"\nSelected MLeader handle={handle}");
        if (!string.IsNullOrWhiteSpace(resolveNote))
        {
            editor.WriteMessage($"\n{resolveNote}");
        }

        var current = CaptureSnapshot(transaction, leader);
        PrintSnapshot(editor, "CURRENT", current);

        Snapshot? previous = null;
        lock (CacheGate)
        {
            if (_cachedSnapshot is not null &&
                string.Equals(_cachedHandle, handle, StringComparison.OrdinalIgnoreCase))
            {
                previous = _cachedSnapshot;
            }
        }

        if (previous is null &&
            TryLoadSnapshotFromFile(handle, out var fileSnapshot))
        {
            previous = fileSnapshot;
            editor.WriteMessage(
                $"\nLoaded previous snapshot for handle={handle} from side-file.");
        }

        if (previous is null)
        {
            editor.WriteMessage(
                "\n--- BEFORE snapshot stored ---");
            editor.WriteMessage(
                "\nNext: grip STRETCH the knee on this LEFT Combined MLeader, " +
                "then re-run AK_DEV_FBC_LEFT_STRETCH_DIAG on the SAME entity.");
            StoreSnapshot(handle, current);
            WriteSnapshotFile(handle, current, phase: "BEFORE");
            PrintCodeEvidenceBrief(editor);
            transaction.Commit();
            return;
        }

        editor.WriteMessage("\n--- AFTER vs stored BEFORE (same handle) ---");
        PrintDeltas(editor, previous, current);
        StoreSnapshot(handle, current);
        WriteSnapshotFile(handle, current, phase: "AFTER");
        PrintCodeEvidenceBrief(editor);
        editor.WriteMessage(
            "\nTip: re-run once more without stretch to replace BEFORE, " +
            "or erase the side-file under _scratch / %TEMP%\\AcKrovy.");
        transaction.Commit();
    }

    private static bool TryResolveLeader(
        Editor editor,
        Transaction transaction,
        out MLeader leader,
        out string note)
    {
        leader = null!;
        note = string.Empty;

        var implied = editor.SelectImplied();
        if (implied.Status == PromptStatus.OK &&
            implied.Value is not null &&
            implied.Value.Count == 1)
        {
            var id = implied.Value[0].ObjectId;
            if (transaction.GetObject(id, OpenMode.ForRead, true) is MLeader selected &&
                !selected.IsErased &&
                selected.ContentType == ContentType.BlockContent)
            {
                note = "Using implied selection (1 BlockContent MLeader).";
                leader = selected;
                return true;
            }

            note =
                "Implied selection is not a single BlockContent MLeader; prompting.";
        }

        var options = new PromptEntityOptions(
            "\nSelect one Combined BlockContent MLeader (LEFT preferred): ");
        options.SetRejectMessage("\nMust be an MLeader.");
        options.AddAllowedClass(typeof(MLeader), exactMatch: true);
        var prompt = editor.GetEntity(options);
        if (prompt.Status != PromptStatus.OK)
        {
            note = "Selection cancelled.";
            return false;
        }

        if (transaction.GetObject(prompt.ObjectId, OpenMode.ForRead, true) is not
                MLeader prompted ||
            prompted.IsErased)
        {
            note = "Selected entity is not a readable MLeader.";
            return false;
        }

        if (prompted.ContentType != ContentType.BlockContent)
        {
            note =
                $"Selected MLeader ContentType={prompted.ContentType}; need BlockContent.";
            return false;
        }

        leader = prompted;
        note = "Using prompted selection.";
        return true;
    }

    private static Snapshot CaptureSnapshot(Transaction transaction, MLeader leader)
    {
        var vertices = new List<Point3d>();
        foreach (int leaderIndex in leader.GetLeaderIndexes())
        {
            foreach (int lineIndex in leader.GetLeaderLineIndexes(leaderIndex))
            {
                var count = leader.VerticesCount(lineIndex);
                if (count <= 0)
                {
                    continue;
                }

                // MLeader exposes first/last accessors; dump both ends per line.
                vertices.Add(leader.GetFirstVertex(lineIndex));
                if (count > 1)
                {
                    vertices.Add(leader.GetLastVertex(lineIndex));
                }
            }
        }

        Vector3d? doglegDirection = null;
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (leaderIndexes.Length > 0)
        {
            try
            {
                doglegDirection = leader.GetDogleg(leaderIndexes[0]);
            }
            catch
            {
                doglegDirection = null;
            }
        }

        var blockId = leader.BlockContentId;
        string blockName = "(null)";
        var attrDefs = new List<AttrDefSnapshot>();
        var attrRefs = new List<AttrRefSnapshot>();
        if (!blockId.IsNull)
        {
            var block = (BlockTableRecord)transaction.GetObject(
                blockId,
                OpenMode.ForRead);
            blockName = block.Name;
            foreach (ObjectId id in block)
            {
                if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                        AttributeDefinition definition ||
                    definition.IsErased)
                {
                    continue;
                }

                var tag = definition.Tag.ToUpperInvariant();
                if (!IsTrackedTag(tag))
                {
                    continue;
                }

                attrDefs.Add(CaptureAttrDef(definition, tag));
                using var attribute = leader.GetBlockAttribute(definition.ObjectId);
                if (attribute is null)
                {
                    continue;
                }

                attrRefs.Add(
                    CaptureAttrRef(
                        attribute,
                        tag,
                        leader.BlockPosition,
                        leader.BlockScale,
                        leader.BlockRotation,
                        leader.Normal));
            }
        }

        return new Snapshot(
            DateTime.UtcNow,
            leader.ObjectId.Handle.ToString(),
            blockName,
            blockId.IsNull ? string.Empty : blockId.Handle.ToString(),
            FormatPoint(leader.BlockPosition),
            FormatScale(leader.BlockScale),
            leader.BlockRotation,
            leader.ContentType.ToString(),
            leader.BlockConnectionType.ToString(),
            doglegDirection is Vector3d d ? FormatVector(d) : "(unavailable)",
            leader.DoglegLength,
            vertices.Select(FormatPoint).ToArray(),
            FormatVector(leader.Normal),
            attrDefs.ToArray(),
            attrRefs.ToArray());
    }

    private static AttrDefSnapshot CaptureAttrDef(
        AttributeDefinition definition,
        string tag)
    {
        string justify;
        try
        {
            justify = definition.Justify.ToString();
        }
        catch
        {
            justify = "(eNotApplicable)";
        }

        return new AttrDefSnapshot(
            tag,
            FormatPoint(definition.Position),
            FormatPoint(definition.AlignmentPoint),
            definition.Height,
            definition.Rotation,
            definition.HorizontalMode.ToString(),
            definition.VerticalMode.ToString(),
            justify,
            definition.LockPositionInBlock,
            definition.Constant,
            definition.Invisible,
            TryIsMText(definition));
    }

    private static AttrRefSnapshot CaptureAttrRef(
        AttributeReference attribute,
        string tag,
        Point3d blockPosition,
        Scale3d blockScale,
        double blockRotation,
        Vector3d normal)
    {
        var worldPos = attribute.Position;
        var worldAlign = SafeAlignmentPoint(attribute);
        var localPos = WorldToBlockLocal(
            worldPos,
            blockPosition,
            blockScale,
            blockRotation,
            normal);
        var localAlign = WorldToBlockLocal(
            worldAlign,
            blockPosition,
            blockScale,
            blockRotation,
            normal);

        string isDefaultAlignment;
        try
        {
            isDefaultAlignment = attribute.IsDefaultAlignment.ToString();
        }
        catch
        {
            isDefaultAlignment = "(unavailable)";
        }

        string justify;
        try
        {
            justify = attribute.Justify.ToString();
        }
        catch
        {
            justify = "(eNotApplicable)";
        }

        return new AttrRefSnapshot(
            tag,
            FormatPoint(worldPos),
            FormatPoint(worldAlign),
            FormatPoint(localPos),
            FormatPoint(localAlign),
            attribute.Height,
            attribute.Rotation,
            isDefaultAlignment,
            attribute.HorizontalMode.ToString(),
            attribute.VerticalMode.ToString(),
            justify,
            attribute.TextString);
    }

    private static Point3d SafeAlignmentPoint(AttributeReference attribute)
    {
        try
        {
            return attribute.AlignmentPoint;
        }
        catch
        {
            return attribute.Position;
        }
    }

    private static Point3d WorldToBlockLocal(
        Point3d world,
        Point3d blockPosition,
        Scale3d blockScale,
        double blockRotation,
        Vector3d normal)
    {
        var axis = normal.Length > 1e-12d ? normal.GetNormal() : Vector3d.ZAxis;
        var delta = world - blockPosition;
        var unrotated = delta.TransformBy(
            Matrix3d.Rotation(-blockRotation, axis, Point3d.Origin));
        var sx = Math.Abs(blockScale.X) > 1e-12d ? blockScale.X : 1d;
        var sy = Math.Abs(blockScale.Y) > 1e-12d ? blockScale.Y : 1d;
        var sz = Math.Abs(blockScale.Z) > 1e-12d ? blockScale.Z : 1d;
        return new Point3d(unrotated.X / sx, unrotated.Y / sy, unrotated.Z / sz);
    }

    private static void PrintSnapshot(Editor editor, string label, Snapshot snapshot)
    {
        editor.WriteMessage($"\n--- {label} ---");
        editor.WriteMessage($"\nutc={snapshot.Utc:O}");
        editor.WriteMessage($"\nhandle={snapshot.Handle}");
        editor.WriteMessage(
            $"\nBlockContentId handle={snapshot.BlockContentHandle} name={snapshot.BlockName}");
        editor.WriteMessage($"\nBlockPosition={snapshot.BlockPosition}");
        editor.WriteMessage($"\nBlockScale={snapshot.BlockScale}");
        editor.WriteMessage(
            $"\nBlockRotation={snapshot.BlockRotation.ToString("R", CultureInfo.InvariantCulture)}");
        editor.WriteMessage($"\nContentType={snapshot.ContentType}");
        editor.WriteMessage($"\nBlockConnectionType={snapshot.BlockConnectionType}");
        editor.WriteMessage($"\nDoglegDirection={snapshot.DoglegDirection}");
        editor.WriteMessage(
            $"\nDoglegLength={snapshot.DoglegLength.ToString("R", CultureInfo.InvariantCulture)}");
        editor.WriteMessage($"\nNormal={snapshot.Normal}");
        editor.WriteMessage(
            $"\nLeaderVertices({snapshot.Vertices.Length})=[" +
            string.Join("; ", snapshot.Vertices) +
            "]");

        editor.WriteMessage("\nAttrDefs (BTR local):");
        foreach (var def in snapshot.AttrDefs.OrderBy(d => d.Tag, StringComparer.Ordinal))
        {
            editor.WriteMessage(
                $"\n  [{def.Tag}] Pos={def.Position} Align={def.AlignmentPoint} " +
                $"H={def.Height.ToString("R", CultureInfo.InvariantCulture)} " +
                $"Rot={def.Rotation.ToString("R", CultureInfo.InvariantCulture)} " +
                $"HMode={def.HorizontalMode} VMode={def.VerticalMode} " +
                $"Justify={def.Justify} Lock={def.LockPositionInBlock} " +
                $"Const={def.Constant} Inv={def.Invisible} MText={def.IsMText}");
        }

        editor.WriteMessage("\nAttrRefs (WORLD + BLOCK-LOCAL):");
        foreach (var attr in snapshot.AttrRefs.OrderBy(a => a.Tag, StringComparer.Ordinal))
        {
            editor.WriteMessage(
                $"\n  [{attr.Tag}] text=\"{attr.TextString}\" " +
                $"WPos={attr.WorldPosition} WAlign={attr.WorldAlignment} " +
                $"LPos={attr.LocalPosition} LAlign={attr.LocalAlignment} " +
                $"H={attr.Height.ToString("R", CultureInfo.InvariantCulture)} " +
                $"Rot={attr.Rotation.ToString("R", CultureInfo.InvariantCulture)} " +
                $"IsDefaultAlignment={attr.IsDefaultAlignment} " +
                $"HMode={attr.HorizontalMode} VMode={attr.VerticalMode} " +
                $"Justify={attr.Justify}");
        }

        editor.WriteMessage("\nAttrRef local vs AttrDef (AlignmentPoint preferred):");
        foreach (var tag in TrackedTags)
        {
            var def = snapshot.AttrDefs.FirstOrDefault(
                d => string.Equals(d.Tag, tag, StringComparison.Ordinal));
            var attr = snapshot.AttrRefs.FirstOrDefault(
                a => string.Equals(a.Tag, tag, StringComparison.Ordinal));
            if (def is null || attr is null)
            {
                editor.WriteMessage($"\n  [{tag}] missing def and/or ref");
                continue;
            }

            var defAlign = ParsePoint(def.AlignmentPoint);
            var refLocal = ParsePoint(attr.LocalAlignment);
            var delta = refLocal - defAlign;
            editor.WriteMessage(
                $"\n  [{tag}] AttrRef.LAlign - AttrDef.Align = {FormatVector(delta)} " +
                $"|len|={delta.Length.ToString("E3", CultureInfo.InvariantCulture)}");
        }
    }

    private static void PrintDeltas(Editor editor, Snapshot before, Snapshot after)
    {
        editor.WriteMessage(
            $"\nBlockPosition Δ = {FormatVector(
                ParsePoint(after.BlockPosition) - ParsePoint(before.BlockPosition))}");
        editor.WriteMessage(
            $"\nBlockScale before={before.BlockScale} after={after.BlockScale}");
        editor.WriteMessage(
            $"\nBlockRotation Δ = " +
            $"{(after.BlockRotation - before.BlockRotation).ToString("R", CultureInfo.InvariantCulture)}");
        editor.WriteMessage(
            $"\nBlockConnectionType before={before.BlockConnectionType} " +
            $"after={after.BlockConnectionType}");
        editor.WriteMessage(
            $"\nDoglegLength Δ = " +
            $"{(after.DoglegLength - before.DoglegLength).ToString("R", CultureInfo.InvariantCulture)}");
        editor.WriteMessage(
            $"\nDoglegDirection before={before.DoglegDirection} after={after.DoglegDirection}");
        editor.WriteMessage(
            $"\nVertices before=[{string.Join("; ", before.Vertices)}] " +
            $"after=[{string.Join("; ", after.Vertices)}]");

        editor.WriteMessage("\nAttrRef WORLD drift (after - before):");
        foreach (var tag in TrackedTags)
        {
            var b = before.AttrRefs.FirstOrDefault(
                a => string.Equals(a.Tag, tag, StringComparison.Ordinal));
            var a = after.AttrRefs.FirstOrDefault(
                x => string.Equals(x.Tag, tag, StringComparison.Ordinal));
            if (b is null || a is null)
            {
                editor.WriteMessage($"\n  [{tag}] missing before and/or after");
                continue;
            }

            var dPos = ParsePoint(a.WorldPosition) - ParsePoint(b.WorldPosition);
            var dAlign = ParsePoint(a.WorldAlignment) - ParsePoint(b.WorldAlignment);
            editor.WriteMessage(
                $"\n  [{tag}] ΔWPos={FormatVector(dPos)} |len|={dPos.Length:E3} " +
                $"ΔWAlign={FormatVector(dAlign)} |len|={dAlign.Length:E3} " +
                $"ΔH={(a.Height - b.Height).ToString("R", CultureInfo.InvariantCulture)} " +
                $"ΔRot={(a.Rotation - b.Rotation).ToString("R", CultureInfo.InvariantCulture)}");
        }

        editor.WriteMessage("\nAttrRef BLOCK-LOCAL drift (after - before):");
        foreach (var tag in TrackedTags)
        {
            var b = before.AttrRefs.FirstOrDefault(
                a => string.Equals(a.Tag, tag, StringComparison.Ordinal));
            var a = after.AttrRefs.FirstOrDefault(
                x => string.Equals(x.Tag, tag, StringComparison.Ordinal));
            if (b is null || a is null)
            {
                editor.WriteMessage($"\n  [{tag}] missing before and/or after");
                continue;
            }

            var dPos = ParsePoint(a.LocalPosition) - ParsePoint(b.LocalPosition);
            var dAlign = ParsePoint(a.LocalAlignment) - ParsePoint(b.LocalAlignment);
            editor.WriteMessage(
                $"\n  [{tag}] ΔLPos={FormatVector(dPos)} |len|={dPos.Length:E3} " +
                $"ΔLAlign={FormatVector(dAlign)} |len|={dAlign.Length:E3}");
        }

        editor.WriteMessage(
            "\nAttrRef vs AttrDef local delta change (AlignmentPoint; after - before):");
        foreach (var tag in TrackedTags)
        {
            var bDef = before.AttrDefs.FirstOrDefault(
                d => string.Equals(d.Tag, tag, StringComparison.Ordinal));
            var aDef = after.AttrDefs.FirstOrDefault(
                d => string.Equals(d.Tag, tag, StringComparison.Ordinal));
            var bRef = before.AttrRefs.FirstOrDefault(
                a => string.Equals(a.Tag, tag, StringComparison.Ordinal));
            var aRef = after.AttrRefs.FirstOrDefault(
                a => string.Equals(a.Tag, tag, StringComparison.Ordinal));
            if (bDef is null || aDef is null || bRef is null || aRef is null)
            {
                editor.WriteMessage($"\n  [{tag}] incomplete pair");
                continue;
            }

            var beforeDelta =
                ParsePoint(bRef.LocalAlignment) - ParsePoint(bDef.AlignmentPoint);
            var afterDelta =
                ParsePoint(aRef.LocalAlignment) - ParsePoint(aDef.AlignmentPoint);
            var change = afterDelta - beforeDelta;
            editor.WriteMessage(
                $"\n  [{tag}] before(Ref-Def)={FormatVector(beforeDelta)} " +
                $"after(Ref-Def)={FormatVector(afterDelta)} " +
                $"Δ={FormatVector(change)} |len|={change.Length:E3}");
        }

        // Relative rigidity: WIDTH/HEIGHT local offset vs ITEM_NO must stay fixed.
        if (TryGetLocalAlign(before, "ITEM_NO", out var bItem) &&
            TryGetLocalAlign(after, "ITEM_NO", out var aItem) &&
            TryGetLocalAlign(before, "WIDTH", out var bWidth) &&
            TryGetLocalAlign(after, "WIDTH", out var aWidth) &&
            TryGetLocalAlign(before, "HEIGHT", out var bHeight) &&
            TryGetLocalAlign(after, "HEIGHT", out var aHeight))
        {
            var beforeW = bWidth - bItem;
            var afterW = aWidth - aItem;
            var beforeH = bHeight - bItem;
            var afterH = aHeight - aItem;
            editor.WriteMessage(
                "\nRelative local offsets vs ITEM_NO (AlignmentPoint):");
            editor.WriteMessage(
                $"\n  WIDTH-ITEM before={FormatVector(beforeW)} after={FormatVector(afterW)} " +
                $"Δ={FormatVector(afterW - beforeW)} |len|={(afterW - beforeW).Length:E3}");
            editor.WriteMessage(
                $"\n  HEIGHT-ITEM before={FormatVector(beforeH)} after={FormatVector(afterH)} " +
                $"Δ={FormatVector(afterH - beforeH)} |len|={(afterH - beforeH).Length:E3}");
        }
    }

    private static bool TryGetLocalAlign(
        Snapshot snapshot,
        string tag,
        out Point3d point)
    {
        var attr = snapshot.AttrRefs.FirstOrDefault(
            a => string.Equals(a.Tag, tag, StringComparison.Ordinal));
        if (attr is null)
        {
            point = Point3d.Origin;
            return false;
        }

        point = ParsePoint(attr.LocalAlignment);
        return true;
    }

    private static void PrintCodeEvidenceBrief(Editor editor)
    {
        editor.WriteMessage("\n--- CODE EVIDENCE BRIEF (G5C vs P3) ---");
        editor.WriteMessage(
            "\nP3 ApplyAttributeValues: SetAttributeFromBlock + TextString + Height only " +
            "(no Position/AlignmentPoint). Same as G5C matrix ApplyAttributeValues.");
        editor.WriteMessage(
            "\nP3 + G5C AttrDefs: MidCenter (TextCenter+TextVerticalMid) + " +
            "AlignmentPoint=local for ITEM_NO/WIDTH/HEIGHT.");
        editor.WriteMessage(
            "\nNeither path calls AdjustAlignment on AttrRefs. " +
            "Neither writes world Position after TransformBy.");
        editor.WriteMessage(
            "\nConcrete diffs: P3 BTR side-agnostic + baseline 1:50 + BlockScale; " +
            "G5C BTR name includes L/R + per-denom heights baked. " +
            "P3 sets Preset/Verifiable=false; G5C omits. " +
            "P3 ValidateAttributeContract checks AlignmentPoint only (not Position).");
        editor.WriteMessage(
            "\nFix deferred until this diag shows which AttrRef local/world field drifts.");
    }

    private static void StoreSnapshot(string handle, Snapshot snapshot)
    {
        lock (CacheGate)
        {
            _cachedHandle = handle;
            _cachedSnapshot = snapshot;
        }
    }

    private static void WriteSnapshotFile(
        string handle,
        Snapshot snapshot,
        string phase)
    {
        try
        {
            var path = ResolveSnapshotPath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"phase={phase}");
            sb.AppendLine($"utc={snapshot.Utc:O}");
            sb.AppendLine($"handle={snapshot.Handle}");
            sb.AppendLine($"blockName={snapshot.BlockName}");
            sb.AppendLine($"blockContentHandle={snapshot.BlockContentHandle}");
            sb.AppendLine($"BlockPosition={snapshot.BlockPosition}");
            sb.AppendLine($"BlockScale={snapshot.BlockScale}");
            sb.AppendLine(
                $"BlockRotation={snapshot.BlockRotation.ToString("R", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"ContentType={snapshot.ContentType}");
            sb.AppendLine($"BlockConnectionType={snapshot.BlockConnectionType}");
            sb.AppendLine($"DoglegDirection={snapshot.DoglegDirection}");
            sb.AppendLine(
                $"DoglegLength={snapshot.DoglegLength.ToString("R", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Normal={snapshot.Normal}");
            sb.AppendLine($"Vertices={string.Join("|", snapshot.Vertices)}");
            foreach (var def in snapshot.AttrDefs)
            {
                sb.AppendLine(
                    $"AttrDef|{def.Tag}|Pos={def.Position}|Align={def.AlignmentPoint}|" +
                    $"H={def.Height.ToString("R", CultureInfo.InvariantCulture)}|" +
                    $"Rot={def.Rotation.ToString("R", CultureInfo.InvariantCulture)}|" +
                    $"HMode={def.HorizontalMode}|VMode={def.VerticalMode}|" +
                    $"Justify={def.Justify}");
            }

            foreach (var attr in snapshot.AttrRefs)
            {
                sb.AppendLine(
                    $"AttrRef|{attr.Tag}|WPos={attr.WorldPosition}|WAlign={attr.WorldAlignment}|" +
                    $"LPos={attr.LocalPosition}|LAlign={attr.LocalAlignment}|" +
                    $"H={attr.Height.ToString("R", CultureInfo.InvariantCulture)}|" +
                    $"Rot={attr.Rotation.ToString("R", CultureInfo.InvariantCulture)}|" +
                    $"IsDefaultAlignment={attr.IsDefaultAlignment}|" +
                    $"text={attr.TextString}");
            }

            File.WriteAllText(path, sb.ToString());
            var editor = AcApplication.DocumentManager.MdiActiveDocument?.Editor;
            editor?.WriteMessage($"\nSnapshot side-file: {path}");
        }
        catch (Exception exception)
        {
            var editor = AcApplication.DocumentManager.MdiActiveDocument?.Editor;
            editor?.WriteMessage(
                $"\nSnapshot side-file write failed: {exception.Message}");
        }
    }

    private static bool TryLoadSnapshotFromFile(string handle, out Snapshot snapshot)
    {
        snapshot = null!;
        try
        {
            var path = ResolveSnapshotPath();
            if (!File.Exists(path))
            {
                return false;
            }

            var lines = File.ReadAllLines(path);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var defs = new List<AttrDefSnapshot>();
            var refs = new List<AttrRefSnapshot>();
            string[] vertices = [];
            foreach (var line in lines)
            {
                if (line.StartsWith("AttrDef|", StringComparison.Ordinal))
                {
                    if (TryParseAttrDefLine(line, out var def))
                    {
                        defs.Add(def);
                    }

                    continue;
                }

                if (line.StartsWith("AttrRef|", StringComparison.Ordinal))
                {
                    if (TryParseAttrRefLine(line, out var attr))
                    {
                        refs.Add(attr);
                    }

                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq > 0)
                {
                    map[line[..eq]] = line[(eq + 1)..];
                }
            }

            if (!map.TryGetValue("handle", out var fileHandle) ||
                !string.Equals(fileHandle, handle, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (map.TryGetValue("Vertices", out var vertexLine) &&
                !string.IsNullOrWhiteSpace(vertexLine))
            {
                vertices = vertexLine.Split('|', StringSplitOptions.RemoveEmptyEntries);
            }

            _ = DateTime.TryParse(
                map.GetValueOrDefault("utc"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var utc);
            _ = double.TryParse(
                map.GetValueOrDefault("BlockRotation"),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var blockRotation);
            _ = double.TryParse(
                map.GetValueOrDefault("DoglegLength"),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var doglegLength);

            snapshot = new Snapshot(
                utc,
                fileHandle,
                map.GetValueOrDefault("blockName") ?? string.Empty,
                map.GetValueOrDefault("blockContentHandle") ?? string.Empty,
                map.GetValueOrDefault("BlockPosition") ?? string.Empty,
                map.GetValueOrDefault("BlockScale") ?? string.Empty,
                blockRotation,
                map.GetValueOrDefault("ContentType") ?? string.Empty,
                map.GetValueOrDefault("BlockConnectionType") ?? string.Empty,
                map.GetValueOrDefault("DoglegDirection") ?? string.Empty,
                doglegLength,
                vertices,
                map.GetValueOrDefault("Normal") ?? string.Empty,
                defs.ToArray(),
                refs.ToArray());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseAttrDefLine(string line, out AttrDefSnapshot snapshot)
    {
        snapshot = null!;
        var parts = line.Split('|');
        if (parts.Length < 3)
        {
            return false;
        }

        var tag = parts[1];
        var fields = ParseFields(parts.Skip(2));
        _ = double.TryParse(
            fields.GetValueOrDefault("H"),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var height);
        _ = double.TryParse(
            fields.GetValueOrDefault("Rot"),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var rotation);
        snapshot = new AttrDefSnapshot(
            tag,
            fields.GetValueOrDefault("Pos") ?? string.Empty,
            fields.GetValueOrDefault("Align") ?? string.Empty,
            height,
            rotation,
            fields.GetValueOrDefault("HMode") ?? string.Empty,
            fields.GetValueOrDefault("VMode") ?? string.Empty,
            fields.GetValueOrDefault("Justify") ?? string.Empty,
            LockPositionInBlock: true,
            Constant: false,
            Invisible: false,
            IsMText: false);
        return true;
    }

    private static bool TryParseAttrRefLine(string line, out AttrRefSnapshot snapshot)
    {
        snapshot = null!;
        var parts = line.Split('|');
        if (parts.Length < 3)
        {
            return false;
        }

        var tag = parts[1];
        var fields = ParseFields(parts.Skip(2));
        _ = double.TryParse(
            fields.GetValueOrDefault("H"),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var height);
        _ = double.TryParse(
            fields.GetValueOrDefault("Rot"),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var rotation);
        snapshot = new AttrRefSnapshot(
            tag,
            fields.GetValueOrDefault("WPos") ?? string.Empty,
            fields.GetValueOrDefault("WAlign") ?? string.Empty,
            fields.GetValueOrDefault("LPos") ?? string.Empty,
            fields.GetValueOrDefault("LAlign") ?? string.Empty,
            height,
            rotation,
            fields.GetValueOrDefault("IsDefaultAlignment") ?? string.Empty,
            HorizontalMode: string.Empty,
            VerticalMode: string.Empty,
            Justify: string.Empty,
            fields.GetValueOrDefault("text") ?? string.Empty);
        return true;
    }

    private static Dictionary<string, string> ParseFields(IEnumerable<string> parts)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts)
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                map[part[..eq]] = part[(eq + 1)..];
            }
        }

        return map;
    }

    private static string ResolveSnapshotPath()
    {
        var scratch = TryFindScratchDirectory();
        if (!string.IsNullOrEmpty(scratch))
        {
            return Path.Combine(scratch, SnapshotFileName);
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AcKrovy",
            SnapshotFileName);
        return fallback;
    }

    private static string? TryFindScratchDirectory()
    {
        try
        {
            var dir = new DirectoryInfo(
                Path.GetDirectoryName(typeof(AutoCadFramedBlockContentLeftStretchDiagService)
                    .Assembly.Location) ??
                AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AcKrovy.sln")))
                {
                    var scratch = Path.Combine(dir.FullName, "_scratch");
                    Directory.CreateDirectory(scratch);
                    return scratch;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // Fall back to LocalAppData.
        }

        return null;
    }

    private static bool IsTrackedTag(string tag) =>
        string.Equals(tag, TimberFramedBlockContentDefinitionRules.ItemNoTag, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tag, TimberFramedBlockContentDefinitionRules.WidthTag, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tag, TimberFramedBlockContentDefinitionRules.HeightTag, StringComparison.OrdinalIgnoreCase);

    private static readonly string[] TrackedTags =
    [
        TimberFramedBlockContentDefinitionRules.ItemNoTag,
        TimberFramedBlockContentDefinitionRules.WidthTag,
        TimberFramedBlockContentDefinitionRules.HeightTag,
    ];

    private static bool TryIsMText(AttributeDefinition definition)
    {
        try
        {
            return definition.IsMTextAttributeDefinition;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatPoint(Point3d point) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"({point.X:R},{point.Y:R},{point.Z:R})");

    private static string FormatVector(Vector3d vector) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"({vector.X:R},{vector.Y:R},{vector.Z:R})");

    private static string FormatScale(Scale3d scale) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"({scale.X:R},{scale.Y:R},{scale.Z:R})");

    private static Point3d ParsePoint(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Point3d.Origin;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
        {
            trimmed = trimmed[1..^1];
        }

        var parts = trimmed.Split(',');
        if (parts.Length < 2)
        {
            return Point3d.Origin;
        }

        _ = double.TryParse(
            parts[0],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var x);
        _ = double.TryParse(
            parts[1],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var y);
        var z = 0d;
        if (parts.Length >= 3)
        {
            _ = double.TryParse(
                parts[2],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out z);
        }

        return new Point3d(x, y, z);
    }

    private sealed record Snapshot(
        DateTime Utc,
        string Handle,
        string BlockName,
        string BlockContentHandle,
        string BlockPosition,
        string BlockScale,
        double BlockRotation,
        string ContentType,
        string BlockConnectionType,
        string DoglegDirection,
        double DoglegLength,
        string[] Vertices,
        string Normal,
        AttrDefSnapshot[] AttrDefs,
        AttrRefSnapshot[] AttrRefs);

    private sealed record AttrDefSnapshot(
        string Tag,
        string Position,
        string AlignmentPoint,
        double Height,
        double Rotation,
        string HorizontalMode,
        string VerticalMode,
        string Justify,
        bool LockPositionInBlock,
        bool Constant,
        bool Invisible,
        bool IsMText);

    private sealed record AttrRefSnapshot(
        string Tag,
        string WorldPosition,
        string WorldAlignment,
        string LocalPosition,
        string LocalAlignment,
        double Height,
        double Rotation,
        string IsDefaultAlignment,
        string HorizontalMode,
        string VerticalMode,
        string Justify,
        string TextString);
}
#endif
