using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Restores the exact pre-command roof assembly on the same ObjectIds after an
/// Unsupported STRETCH / source GRIP_STRETCH: roof source, owned generated timber
/// Lines, and annotations bound to those timber SourceHandles. Then rebuilds
/// canonical roof display/GROUP. Does not write RoofDefinition. Does not regenerate
/// timber through the supported-resize replacement path.
/// </summary>
internal static class RoofUnsupportedStretchRecoveryService
{
    public static RoofUnsupportedStretchRecoveryOutcome TryRecoverOwner(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        Autodesk.AutoCAD.EditorInput.Editor? editor = null)
    {
        if (ownerId.IsNull)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor, "owner-restore", "roof-source-objectid-missing");
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        if (ownerId.IsErased)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor, "owner-restore", "roof-source-erased", owner: ownerId.Handle.ToString());
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        if (!RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var entry))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                RoofUnsupportedStretchRecoverySnapshotService.SnapshotCount == 0
                    ? "no-command-snapshot"
                    : "owner-snapshot-missing",
                owner: ownerId.Handle.ToString());
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                ownerId,
                OpenMode.ForWrite,
                out var entity,
                database) ||
            entity is null)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "roof-source-missing",
                handle: entry.Assembly.RoofSource.OwnerHandle);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        if (entity is not Polyline owner)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "roof-source-type-mismatch",
                handle: entity.Handle.ToString(),
                kind: entity.GetType().Name);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        var liveHandle = owner.Handle.ToString();
        if (!string.Equals(
                liveHandle,
                entry.Assembly.RoofSource.OwnerHandle,
                StringComparison.OrdinalIgnoreCase))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "ambiguous-owner-match",
                owner: liveHandle,
                handle: entry.Assembly.RoofSource.OwnerHandle);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        var stored = RoofDefinitionStore.Read(owner);
        if (stored.Data is null)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "roof-source-metadata-mismatch",
                owner: liveHandle);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        var liveClassification = Classify(owner);
        if (liveClassification.Kind != RoofSourceChangeKind.Unsupported)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "not-unsupported",
                owner: liveHandle,
                kind: liveClassification.Kind.ToString());
#endif
            return RoofUnsupportedStretchRecoveryOutcome.NotApplicable;
        }

        if (!TryProbeAssemblyMembers(
                database,
                transaction,
                entry.Assembly,
                editor,
                liveHandle))
        {
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        try
        {
            RestorePolylineGeometry(owner, entry.Assembly.RoofSource);
            if (!TryRestoreTimberLines(
                    database,
                    transaction,
                    entry.Assembly.TimberLines,
                    editor,
                    liveHandle) ||
                !TryRestoreAnnotations(
                    database,
                    transaction,
                    entry.Assembly.Annotations,
                    editor,
                    liveHandle))
            {
                return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
            }
        }
#if DEBUG
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "restore-write-failure",
                owner: liveHandle,
                detail: ex.ErrorStatus.ToString());
            return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }
#else
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }
#endif


        var restoredClassification = Classify(owner);
        if (!RoofUnsupportedStretchRecoveryRules.IsAcceptableRestoredClassification(
                restoredClassification.Kind) ||
            restoredClassification.Geometry is null)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "post-restore-rigid-equivalent-failure",
                owner: liveHandle,
                kind: restoredClassification.Kind.ToString());
#endif
            return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }

        var input = RoofPolylineExtractor.Extract(owner);
        if (!RoofUnsupportedStretchRecoveryRules.RestoredMatchesSnapshot(
                input.Vertices,
                input.IsClosed,
                entry.Assembly.RoofSource))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "post-restore-geometry-mismatch",
                owner: liveHandle);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }

        var edges = SimpleGableRoofWireframe.Create(
            restoredClassification.Geometry,
            RoofPolylineExtractor.GetSourceElevation(owner));
        var signature = SimpleGableRoofWireframe.BuildGenerationSignature(edges);
        if (!RoofDisplayService.Rebuild(
                database,
                transaction,
                owner.ObjectId,
                owner.Handle.ToString(),
                edges,
                signature))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "roof-display-rebuild-failure",
                owner: liveHandle);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }

        return RoofUnsupportedStretchRecoveryOutcome.Recovered;
    }

    /// <summary>
    /// Scenario 2: roof source remains <see cref="RoofSourceChangeKind.RigidEquivalent"/>;
    /// restore only owned generated timber Lines + annotations in place. Does not write
    /// the roof Polyline, RoofDefinition, or regenerate timber.
    /// </summary>
    public static RoofUnsupportedStretchRecoveryOutcome TryRecoverGeneratedMembersOnly(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        Autodesk.AutoCAD.EditorInput.Editor? editor = null)
    {
        if (ownerId.IsNull ||
            !RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var entry))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "generated-only",
                ownerId.IsNull ? "roof-source-objectid-missing" : "owner-snapshot-missing");
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                database) ||
            owner is null)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "generated-only",
                "roof-source-missing",
                handle: entry.Assembly.RoofSource.OwnerHandle);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        var liveHandle = owner.Handle.ToString();
        if (!string.Equals(
                liveHandle,
                entry.Assembly.RoofSource.OwnerHandle,
                StringComparison.OrdinalIgnoreCase))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "generated-only",
                "ambiguous-owner-match",
                owner: liveHandle,
                handle: entry.Assembly.RoofSource.OwnerHandle);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        var classification = Classify(owner);
        if (classification.Kind != RoofSourceChangeKind.RigidEquivalent)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "generated-only",
                "source-not-rigid-equivalent",
                owner: liveHandle,
                kind: classification.Kind.ToString());
#endif
            return RoofUnsupportedStretchRecoveryOutcome.NotApplicable;
        }

        if (!TryProbeAssemblyMembers(
                database,
                transaction,
                entry.Assembly,
                editor,
                liveHandle))
        {
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        try
        {
            if (!TryRestoreTimberLines(
                    database,
                    transaction,
                    entry.Assembly.TimberLines,
                    editor,
                    liveHandle) ||
                !TryRestoreAnnotations(
                    database,
                    transaction,
                    entry.Assembly.Annotations,
                    editor,
                    liveHandle) ||
                !TryEraseUnsnapshotGeneratedDuplicates(
                    database,
                    transaction,
                    ownerId,
                    entry.Assembly.TimberLines))
            {
                return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
            }
        }
#if DEBUG
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "generated-only",
                "restore-write-failure",
                owner: liveHandle,
                detail: ex.ErrorStatus.ToString());
            return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }
#else
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }
#endif

#if DEBUG
        RoofUnsupportedStretchRecoveryDiag.WriteProbe(
            editor,
            liveHandle,
            roof: 0,
            timber: entry.Assembly.TimberLines.Count,
            annotations: entry.Assembly.Annotations.Count,
            result: "generated-only-ok",
            kindCounts: RoofUnsupportedStretchRecoverySnapshotService.FormatAnnotationKindCounts(
                entry.Assembly));
#endif
        return RoofUnsupportedStretchRecoveryOutcome.Recovered;
    }

    public static bool TryUnEraseAndRestore(
        Database database,
        Transaction transaction,
        RoofUnsupportedStretchRecoverySnapshotService.SnapshotEntry entry,
        Autodesk.AutoCAD.EditorInput.Editor? editor)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            if (!TryRestoreTimberLines(
                    database,
                    transaction,
                    entry.Assembly.TimberLines,
                    editor,
                    entry.Assembly.RoofSource.OwnerHandle,
                    allowErased: true) ||
                !TryRestoreAnnotations(
                    database,
                    transaction,
                    entry.Assembly.Annotations,
                    editor,
                    entry.Assembly.RoofSource.OwnerHandle,
                    allowErased: true))
            {
                return false;
            }

            return true;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static bool TryProbeAssemblyMembers(
        Database database,
        Transaction transaction,
        RoofUnsupportedStretchAssemblySnapshotData assembly,
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string ownerHandle)
    {
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        foreach (var timber in assembly.TimberLines)
        {
            if (!TryResolveEntityByHandle(
                    database,
                    timber.EntityHandle,
                    role: "generated-timber",
                    out var timberId,
                    out var timberResolveReason))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    timberResolveReason,
                    owner: ownerHandle,
                    handle: timber.EntityHandle,
                    kind: "timber");
#endif
                return false;
            }

            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    timberId,
                    OpenMode.ForRead,
                    out var timberEntity,
                    database) ||
                timberEntity is null)
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    "generated-timber-missing",
                    owner: ownerHandle,
                    handle: timber.EntityHandle);
#endif
                return false;
            }

            if (timberEntity is not Line line)
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    "generated-timber-type-mismatch",
                    owner: ownerHandle,
                    handle: timber.EntityHandle,
                    kind: timberEntity.GetType().Name);
#endif
                return false;
            }

            if (!metadataStore.TryRead(line, out var data) || data is null)
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    "generated-timber-metadata-mismatch",
                    owner: ownerHandle,
                    handle: timber.EntityHandle);
#endif
                return false;
            }

            if (!string.Equals(data.ElementId, timber.ElementId, StringComparison.Ordinal))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    "generated-timber-elementid-mismatch",
                    owner: ownerHandle,
                    handle: timber.EntityHandle,
                    detail: $"expected={timber.ElementId};actual={data.ElementId}");
#endif
                return false;
            }

            if (!string.Equals(
                    line.Handle.ToString(),
                    timber.SourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    "generated-timber-sourcehandle-mismatch",
                    owner: ownerHandle,
                    handle: timber.EntityHandle,
                    detail: timber.SourceHandle);
#endif
                return false;
            }
        }

        foreach (var annotation in assembly.Annotations)
        {
            if (!TryResolveEntityByHandle(
                    database,
                    annotation.EntityHandle,
                    role: "annotation",
                    out var annotationId,
                    out var annotationResolveReason))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    annotationResolveReason,
                    owner: ownerHandle,
                    handle: annotation.EntityHandle,
                    kind: annotation.Kind.ToString());
#endif
                return false;
            }

            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    annotationId,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    "annotation-missing",
                    owner: ownerHandle,
                    handle: annotation.EntityHandle,
                    kind: annotation.Kind.ToString());
#endif
                return false;
            }

            if (!MatchesAnnotationKind(entity, annotation.Kind))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    annotation.Kind == RoofUnsupportedStretchAnnotationKind.Unknown
                        ? "unsupported-annotation-entity-type"
                        : "annotation-type-kind-mismatch",
                    owner: ownerHandle,
                    handle: annotation.EntityHandle,
                    kind: $"{annotation.Kind}/{entity.GetType().Name}");
#endif
                return false;
            }

            if (!TryResolveAnnotationSourceHandle(entity, out var liveSource) ||
                !string.Equals(
                    liveSource,
                    annotation.SourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    "annotation-sourcehandle-mismatch",
                    owner: ownerHandle,
                    handle: annotation.EntityHandle,
                    detail: $"expected={annotation.SourceHandle};actual={liveSource}");
#endif
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveEntityByHandle(
        Database database,
        string handleText,
        string role,
        out ObjectId id,
        out string reason)
    {
        id = ObjectId.Null;
        var missing = role == "annotation" ? "annotation-missing" : "generated-timber-missing";
        var erased = role == "annotation" ? "annotation-erased" : "generated-timber-erased";
        reason = missing;
        if (!long.TryParse(
                handleText,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var handleValue))
        {
            return false;
        }

        try
        {
            id = database.GetObjectId(false, new Handle(handleValue), 0);
            if (id.IsNull)
            {
                return false;
            }

            if (id.IsErased)
            {
                reason = erased;
                return false;
            }

            return true;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static bool TryRestoreTimberLines(
        Database database,
        Transaction transaction,
        IReadOnlyList<RoofUnsupportedStretchTimberLineSnapshotData> timberLines,
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string ownerHandle,
        bool allowErased = false)
    {
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        foreach (var timber in timberLines)
        {
            if (!TryGetEntityByHandle<Line>(
                    database,
                    transaction,
                    timber.EntityHandle,
                    OpenMode.ForWrite,
                    out var line,
                    allowErased) ||
                line is null)
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-restore",
                    "restore-write-failure",
                    owner: ownerHandle,
                    handle: timber.EntityHandle,
                    kind: "timber");
#endif
                return false;
            }

            if (line.IsErased)
            {
                line.Erase(false);
            }

            if (!metadataStore.TryRead(line, out var data) ||
                data is null ||
                !string.Equals(data.ElementId, timber.ElementId, StringComparison.Ordinal))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-restore",
                    "restore-write-failure",
                    owner: ownerHandle,
                    handle: timber.EntityHandle,
                    kind: "timber");
#endif
                return false;
            }

            line.StartPoint = ToAcad(timber.Start);
            line.EndPoint = ToAcad(timber.End);
        }

        return true;
    }

    private static bool TryEraseUnsnapshotGeneratedDuplicates(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyList<RoofUnsupportedStretchTimberLineSnapshotData> timberLines)
    {
        var snapshotHandles = timberLines
            .Select(item => item.EntityHandle)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var generatedIds = RoofGeneratedTimberStore.FindByOwner(
            database,
            transaction,
            ownerId.Handle.ToString());
        foreach (var id in generatedIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var line,
                    database) ||
                line is null ||
                line.IsErased)
            {
                continue;
            }

            if (snapshotHandles.Contains(line.Handle.ToString()))
            {
                continue;
            }

            var sourceHandle = line.Handle.ToString();
            ElementLabelService.DeleteForSourceHandle(database, transaction, sourceHandle);
            SlopeAnnotationService.DeleteForSourceHandle(database, transaction, sourceHandle);
            PostFootprintPerpendicularAnnotationService.DeleteForSourceHandle(
                database,
                transaction,
                sourceHandle);
            line.Erase(true);
        }

        return true;
    }

    private static bool TryRestoreAnnotations(
        Database database,
        Transaction transaction,
        IReadOnlyList<RoofUnsupportedStretchAnnotationSnapshotData> annotations,
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string ownerHandle,
        bool allowErased = false)
    {
        foreach (var annotation in annotations)
        {
            if (!TryGetEntityByHandle<Entity>(
                    database,
                    transaction,
                    annotation.EntityHandle,
                    OpenMode.ForWrite,
                    out var entity,
                    allowErased) ||
                entity is null)
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-restore",
                    "restore-write-failure",
                    owner: ownerHandle,
                    handle: annotation.EntityHandle,
                    kind: annotation.Kind.ToString());
#endif
                return false;
            }

            if (entity.IsErased)
            {
                entity.Erase(false);
            }

            if (!TryRestoreAnnotationEntity(entity, annotation, editor))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-restore",
                    "restore-write-failure",
                    owner: ownerHandle,
                    handle: annotation.EntityHandle,
                    kind: annotation.Kind.ToString());
#endif
                return false;
            }
        }

        return true;
    }

    private static bool TryRestoreAnnotationEntity(
        Entity entity,
        RoofUnsupportedStretchAnnotationSnapshotData annotation,
        Autodesk.AutoCAD.EditorInput.Editor? editor)
    {
        try
        {
            switch (annotation.Kind)
            {
                case RoofUnsupportedStretchAnnotationKind.Line when entity is Line line:
                    if (annotation.SecondaryPoint is null || annotation.TertiaryPoint is null)
                    {
                        return false;
                    }

                    line.StartPoint = ToAcad(annotation.SecondaryPoint.Value);
                    line.EndPoint = ToAcad(annotation.TertiaryPoint.Value);
                    return true;

                case RoofUnsupportedStretchAnnotationKind.Polyline when entity is Polyline polyline:
                    return TryRestorePolyline(polyline, annotation);

                case RoofUnsupportedStretchAnnotationKind.MText when entity is MText mtext:
                    if (annotation.Position is null || annotation.Rotation is null)
                    {
                        return false;
                    }

                    mtext.Location = ToAcad(annotation.Position.Value);
                    mtext.Rotation = annotation.Rotation.Value;
                    return true;

                case RoofUnsupportedStretchAnnotationKind.DBText when entity is DBText dbText:
                    if (annotation.Position is null ||
                        annotation.Rotation is null ||
                        annotation.SecondaryPoint is null)
                    {
                        return false;
                    }

                    dbText.Position = ToAcad(annotation.Position.Value);
                    dbText.AlignmentPoint = ToAcad(annotation.SecondaryPoint.Value);
                    dbText.Rotation = annotation.Rotation.Value;
                    return true;

                case RoofUnsupportedStretchAnnotationKind.MLeader when entity is MLeader leader:
                    return TryRestoreMLeader(leader, annotation, editor);

                case RoofUnsupportedStretchAnnotationKind.BlockReference when entity is BlockReference block:
                    if (annotation.Position is null || annotation.Rotation is null)
                    {
                        return false;
                    }

                    block.Position = ToAcad(annotation.Position.Value);
                    block.Rotation = annotation.Rotation.Value;
                    return true;

                case RoofUnsupportedStretchAnnotationKind.Circle when entity is Circle circle:
                    if (annotation.Position is null || annotation.SecondaryScalar is null)
                    {
                        return false;
                    }

                    circle.Center = ToAcad(annotation.Position.Value);
                    circle.Radius = annotation.SecondaryScalar.Value;
                    return true;

                default:
                    return false;
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static bool TryRestorePolyline(
        Polyline polyline,
        RoofUnsupportedStretchAnnotationSnapshotData annotation)
    {
        if (annotation.PolylineVertices is null ||
            annotation.PolylineBulges is null ||
            annotation.PolylineClosed is null ||
            annotation.ElevationMm is null ||
            annotation.PolylineVertices.Count == 0 ||
            annotation.PolylineVertices.Count != annotation.PolylineBulges.Count)
        {
            return false;
        }

        if (polyline.NumberOfVertices == annotation.PolylineVertices.Count)
        {
            for (var i = 0; i < annotation.PolylineVertices.Count; i++)
            {
                var v = annotation.PolylineVertices[i];
                polyline.SetPointAt(i, new Point2d(v.X, v.Y));
                polyline.SetBulgeAt(i, annotation.PolylineBulges[i]);
            }
        }
        else
        {
            while (polyline.NumberOfVertices > 0)
            {
                polyline.RemoveVertexAt(0);
            }

            for (var i = 0; i < annotation.PolylineVertices.Count; i++)
            {
                var v = annotation.PolylineVertices[i];
                polyline.AddVertexAt(i, new Point2d(v.X, v.Y), annotation.PolylineBulges[i], 0d, 0d);
            }
        }

        polyline.Closed = annotation.PolylineClosed.Value;
        polyline.Elevation = annotation.ElevationMm.Value;
        return true;
    }

    private static bool TryRestoreMLeader(
        MLeader leader,
        RoofUnsupportedStretchAnnotationSnapshotData annotation,
        Autodesk.AutoCAD.EditorInput.Editor? editor)
    {
        if (annotation.SecondaryPoint is null || annotation.TertiaryPoint is null)
        {
            return false;
        }

        if (!TryPrepareLiveMLeaderTopology(
                leader,
                annotation,
                editor,
                out var leaderIndex,
                out var lineIndex))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "member-restore",
                "restore-write-failure",
                handle: annotation.EntityHandle,
                kind: "MLeader",
                detail: "topology-mismatch");
#endif
            return false;
        }

        var attachment = ToAcad(annotation.SecondaryPoint.Value);
        var knee = ToAcad(annotation.TertiaryPoint.Value);
#if DEBUG
        var step = "init";
#endif
        try
        {
            // If native STRETCH inserted bend vertices, rebuild the single leader line
            // in place (same MLeader ObjectId) to the canonical two-point KROVY form.
            if (leader.VerticesCount(lineIndex) != 2)
            {
#if DEBUG
                step = "RebuildLeaderLine";
#endif
                leader.RemoveLeaderLine(lineIndex);
                lineIndex = leader.AddLeaderLine(leaderIndex);
                leader.AddFirstVertex(lineIndex, attachment);
                leader.AddLastVertex(lineIndex, knee);
            }

            var unitX = 0d;
            var unitY = 0d;
            var applyDogleg =
                annotation.MLeaderEnableDogleg == true &&
                annotation.Position is { } doglegVector &&
                annotation.SecondaryScalar is { } doglegLength &&
                TimberNativeMLeaderDoglegInputRules.ShouldCallSetDogleg(
                    doglegLength,
                    doglegVector.X,
                    doglegVector.Y,
                    out unitX,
                    out unitY);

            if (applyDogleg)
            {
#if DEBUG
                step = "DoglegLength";
#endif
                leader.DoglegLength = annotation.SecondaryScalar!.Value;
#if DEBUG
                step = "EnableDogleg";
#endif
                leader.EnableDogleg = true;
#if DEBUG
                step = "SetDogleg";
#endif
                leader.SetDogleg(leaderIndex, new Vector3d(unitX, unitY, 0d));
            }
            else if (annotation.MLeaderEnableDogleg == false)
            {
#if DEBUG
                step = "EnableDogleg-false";
#endif
                leader.EnableDogleg = false;
            }

            if (annotation.QuaternaryPoint is { } landing)
            {
                if (leader.ContentType == ContentType.BlockContent)
                {
#if DEBUG
                    step = "BlockPosition";
#endif
                    leader.BlockPosition = ToAcad(landing);
                }
                else if (leader.ContentType == ContentType.MTextContent)
                {
#if DEBUG
                    step = "TextLocation";
#endif
                    leader.TextLocation = ToAcad(landing);
                }
            }

#if DEBUG
            step = "SetLastVertex";
#endif
            leader.SetLastVertex(lineIndex, knee);
#if DEBUG
            step = "SetFirstVertex";
#endif
            leader.SetFirstVertex(lineIndex, attachment);

            if (applyDogleg)
            {
#if DEBUG
                step = "SetDogleg-reassert";
#endif
                leader.SetDogleg(leaderIndex, new Vector3d(unitX, unitY, 0d));
#if DEBUG
                step = "DoglegLength-reassert";
#endif
                leader.DoglegLength = annotation.SecondaryScalar!.Value;
            }

            if (annotation.Rotation is { } blockRotation &&
                leader.ContentType == ContentType.BlockContent)
            {
#if DEBUG
                step = "BlockRotation";
#endif
                leader.BlockRotation = blockRotation;
            }

            if (annotation.QuaternaryPoint is { } landingFinal &&
                leader.ContentType == ContentType.BlockContent)
            {
#if DEBUG
                step = "BlockPosition-reassert";
#endif
                leader.BlockPosition = ToAcad(landingFinal);
#if DEBUG
                step = "SetLastVertex-reassert";
#endif
                leader.SetLastVertex(lineIndex, knee);
#if DEBUG
                step = "SetFirstVertex-reassert";
#endif
                leader.SetFirstVertex(lineIndex, attachment);
            }

            return true;
        }
#if DEBUG
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            RoofUnsupportedStretchRecoveryDiag.WriteMLeaderWriteFail(
                editor,
                annotation.EntityHandle,
                step,
                leaderIndex,
                lineIndex,
                ex);
            return false;
        }
#else
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
#endif
    }

    private static bool TryPrepareLiveMLeaderTopology(
        MLeader leader,
        RoofUnsupportedStretchAnnotationSnapshotData annotation,
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        out int leaderIndex,
        out int lineIndex)
    {
        leaderIndex = -1;
        lineIndex = -1;
        var snapshotSummary = FormatMLeaderSnapshotTopology(annotation);
        var liveBefore = FormatMLeaderLiveTopology(leader);

        // Same ObjectId: collapse extras to KROVY's one-leader/one-line form
        // (mirrors AutoCadStandaloneFramedItemOnlyAnnotationService.EnsureSingleLeaderLine).
        try
        {
            var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            if (leaderIndexes.Length == 0)
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteMLeaderTopology(
                    editor,
                    annotation.EntityHandle,
                    snapshotSummary,
                    liveBefore + ";reason=no-leaders");
#endif
                return false;
            }

            leaderIndex = leaderIndexes[0];
            for (var i = 1; i < leaderIndexes.Length; i++)
            {
                leader.RemoveLeader(leaderIndexes[i]);
            }

            var lineIndexes = leader.GetLeaderLineIndexes(leaderIndex).Cast<int>().ToArray();
            if (lineIndexes.Length == 0)
            {
                lineIndex = leader.AddLeaderLine(leaderIndex);
            }
            else
            {
                lineIndex = lineIndexes[0];
                for (var i = 1; i < lineIndexes.Length; i++)
                {
                    leader.RemoveLeaderLine(lineIndexes[i]);
                }
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteMLeaderTopology(
                editor,
                annotation.EntityHandle,
                snapshotSummary,
                liveBefore + ";reason=normalize-exception");
#endif
            return false;
        }

        var liveAfter = FormatMLeaderLiveTopology(leader);
        var liveContent = MapContentKind(leader.ContentType);
        var leadersAfter = leader.GetLeaderIndexes().Cast<int>().ToArray();
        var linesAfter = leadersAfter.Length == 1
            ? leader.GetLeaderLineIndexes(leadersAfter[0]).Cast<int>().ToArray()
            : Array.Empty<int>();

        if (!RoofUnsupportedStretchRecoveryRules.IsRecoverableMLeaderTopology(
                leadersAfter.Length,
                linesAfter.Length,
                annotation.MLeaderContentKind,
                liveContent))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteMLeaderTopology(
                editor,
                annotation.EntityHandle,
                snapshotSummary,
                liveAfter + ";reason=incompatible");
#endif
            return false;
        }

        leaderIndex = leadersAfter[0];
        lineIndex = linesAfter[0];
#if DEBUG
        if (RoofUnsupportedStretchRecoveryRules.IsIndexOnlyTopologyDrift(
                annotation.MLeaderLeaderIndex,
                annotation.MLeaderLeaderLineIndex,
                leaderIndex,
                lineIndex) ||
            !string.Equals(liveBefore, liveAfter, StringComparison.Ordinal))
        {
            RoofUnsupportedStretchRecoveryDiag.WriteMLeaderTopology(
                editor,
                annotation.EntityHandle,
                snapshotSummary,
                liveAfter + ";recoverable=1");
        }
#endif
        return true;
    }

    private static string FormatMLeaderSnapshotTopology(
        RoofUnsupportedStretchAnnotationSnapshotData annotation) =>
        $"content={annotation.MLeaderContentKind};" +
        $"leaderIdx={annotation.MLeaderLeaderIndex?.ToString() ?? "-"};" +
        $"lineIdx={annotation.MLeaderLeaderLineIndex?.ToString() ?? "-"};" +
        $"dogleg={(annotation.MLeaderEnableDogleg == true ? 1 : 0)}";

    private static string FormatMLeaderLiveTopology(MLeader leader)
    {
        try
        {
            var leaders = leader.GetLeaderIndexes().Cast<int>().ToArray();
            var leaderText = leaders.Length == 0
                ? "-"
                : string.Join(",", leaders);
            var lineParts = new List<string>();
            foreach (var leaderIdx in leaders)
            {
                var lines = leader.GetLeaderLineIndexes(leaderIdx).Cast<int>().ToArray();
                foreach (var lineIdx in lines)
                {
                    lineParts.Add($"{lineIdx}:v{leader.VerticesCount(lineIdx)}");
                }
            }

            return $"content={MapContentKind(leader.ContentType)};" +
                   $"leaders={leaders.Length}[{leaderText}];" +
                   $"lines={lineParts.Count}[{string.Join(",", lineParts)}];" +
                   $"dogleg={(leader.EnableDogleg ? 1 : 0)};" +
                   $"connect={leader.BlockConnectionType}";
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return "unreadable";
        }
    }

    private static RoofUnsupportedStretchMLeaderContentKind MapContentKind(ContentType contentType) =>
        contentType switch
        {
            ContentType.BlockContent => RoofUnsupportedStretchMLeaderContentKind.BlockContent,
            ContentType.MTextContent => RoofUnsupportedStretchMLeaderContentKind.MTextContent,
            ContentType.NoneContent => RoofUnsupportedStretchMLeaderContentKind.NoneContent,
            _ => RoofUnsupportedStretchMLeaderContentKind.Unknown,
        };

    private static void RestorePolylineGeometry(
        Polyline owner,
        RoofUnsupportedStretchSourceSnapshotData snapshot)
    {
        var vertices = snapshot.Vertices;
        if (vertices.Count != 4)
        {
            throw new InvalidOperationException("Recovery snapshot requires four vertices.");
        }

        if (owner.NumberOfVertices == 4)
        {
            for (var i = 0; i < 4; i++)
            {
                owner.SetPointAt(i, new Point2d(vertices[i].X, vertices[i].Y));
                owner.SetBulgeAt(i, 0d);
            }
        }
        else
        {
            while (owner.NumberOfVertices > 0)
            {
                owner.RemoveVertexAt(0);
            }

            for (var i = 0; i < 4; i++)
            {
                owner.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), 0d, 0d, 0d);
            }
        }

        owner.Closed = snapshot.IsClosed;
        owner.Elevation = snapshot.ElevationMm;
        var normal = new Vector3d(snapshot.NormalX, snapshot.NormalY, snapshot.NormalZ);
        if (normal.Length > RoofUnsupportedStretchRecoveryRules.NormalTolerance)
        {
            owner.Normal = normal.GetNormal();
        }
    }

    private static bool TryGetEntityByHandle<T>(
        Database database,
        Transaction transaction,
        string handleText,
        out T? entity)
        where T : Entity =>
        TryGetEntityByHandle(database, transaction, handleText, OpenMode.ForRead, out entity);

    private static bool TryGetEntityByHandle<T>(
        Database database,
        Transaction transaction,
        string handleText,
        OpenMode mode,
        out T? entity,
        bool allowErased = false)
        where T : Entity
    {
        entity = null;
        if (!long.TryParse(
                handleText,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var handleValue))
        {
            return false;
        }

        try
        {
            var id = database.GetObjectId(false, new Handle(handleValue), 0);
            if (id.IsNull)
            {
                return false;
            }

            if (id.IsErased && !allowErased)
            {
                return false;
            }

            var resolved = allowErased
                ? AutoCadObjectIdAccess.TryGetObjectAllowErased(
                    transaction,
                    id,
                    mode,
                    out entity,
                    database)
                : AutoCadObjectIdAccess.TryGetObject(
                    transaction,
                    id,
                    mode,
                    out entity,
                    database);
            return resolved && entity is not null;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static bool MatchesAnnotationKind(Entity entity, RoofUnsupportedStretchAnnotationKind kind) =>
        kind switch
        {
            RoofUnsupportedStretchAnnotationKind.Line => entity is Line,
            RoofUnsupportedStretchAnnotationKind.Polyline => entity is Polyline,
            RoofUnsupportedStretchAnnotationKind.MText => entity is MText,
            RoofUnsupportedStretchAnnotationKind.DBText => entity is DBText,
            RoofUnsupportedStretchAnnotationKind.MLeader => entity is MLeader,
            RoofUnsupportedStretchAnnotationKind.BlockReference => entity is BlockReference,
            RoofUnsupportedStretchAnnotationKind.Circle => entity is Circle,
            _ => false,
        };

    private static bool TryResolveAnnotationSourceHandle(Entity entity, out string sourceHandle)
    {
        sourceHandle = string.Empty;
        if (ElementLabelStore.TryRead(entity, out var label) &&
            label is not null &&
            !string.IsNullOrWhiteSpace(label.SourceHandle))
        {
            sourceHandle = label.SourceHandle;
            return true;
        }

        if (SlopeArrowStore.TryRead(entity, out var arrow) &&
            arrow is not null &&
            !string.IsNullOrWhiteSpace(arrow.SourceHandle))
        {
            sourceHandle = arrow.SourceHandle;
            return true;
        }

        if (SlopeAngleTextStore.TryRead(entity, out var angle) &&
            angle is not null &&
            !string.IsNullOrWhiteSpace(angle.SourceHandle))
        {
            sourceHandle = angle.SourceHandle;
            return true;
        }

        if (PostFootprintPerpendicularAnnotationStore.TryRead(entity, out var post) &&
            post is not null &&
            !string.IsNullOrWhiteSpace(post.SourceHandle))
        {
            sourceHandle = post.SourceHandle;
            return true;
        }

        return false;
    }

    private static RoofSourceChangeClassification Classify(Polyline polyline)
    {
        var stored = RoofDefinitionStore.Read(polyline);
        if (stored.Data is null)
        {
            return new RoofSourceChangeClassification(
                RoofSourceChangeKind.None,
                null,
                RoofDefinitionRestoreError.InvalidDefinition);
        }

        var input = RoofPolylineExtractor.Extract(polyline);
        var validation = RoofFootprintValidator.Validate(input);
        if (!validation.IsValid || validation.Footprint is null)
        {
            return new RoofSourceChangeClassification(
                RoofSourceChangeKind.Unsupported,
                null,
                RoofDefinitionRestoreError.StaleFootprint);
        }

        return RoofDefinitionPersistence.Classify(
            input,
            validation.Footprint,
            stored.Data);
    }

    private static Point3d ToAcad(RoofPoint3D point) => new(point.X, point.Y, point.Z);
}
