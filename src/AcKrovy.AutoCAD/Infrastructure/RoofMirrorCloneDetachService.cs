using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// MIRROR lifecycle for roof timber.
/// <list type="bullet">
/// <item>MIRROR No (Generated source): detach clone from Generated metadata, promote to
/// AttachedManual Origin.Copy, immediate annotation + group sync.</item>
/// <item>MIRROR No (AttachedManual source): re-initialize the clone from its final WCS.</item>
/// <item>MIRROR Yes (Generated source): ALSO suppress the erased source's Generated slot
/// so a later rebuild never resurrects it.</item>
/// </list>
/// </summary>
internal static class RoofMirrorCloneDetachService
{
    public static void Process(
        Document document,
        string? globalCommandName,
        IReadOnlyCollection<ObjectId> appendedTimberIds,
        IReadOnlyCollection<string> erasedSourceHandles,
        IReadOnlyCollection<ObjectId> mirrorModifiedTimberIds,
        IReadOnlyCollection<ObjectId> appendedAnnotationIds)
    {
        if (LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName) ||
            !RoofGeneratedMemberEditCommandRules.IsMirrorCommand(globalCommandName) ||
            (appendedTimberIds.Count == 0 &&
             erasedSourceHandles.Count == 0 &&
             mirrorModifiedTimberIds.Count == 0))
        {
            return;
        }

        try
        {
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                var wrote = false;
                var affectedOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // MIRROR Erase Source = Yes: the native MIRROR erased the source. For a
                // Generated source, persist a Suppress override so the canonical slot is
                // NOT rebuilt by a later SupportedResize. AttachedManual/generic sources
                // are already permanently erased (no override, no resurrection).
                foreach (var sourceHandle in erasedSourceHandles)
                {
                    if (!TrySuppressErasedGeneratedSource(
                            document,
                            transaction,
                            sourceHandle,
                            out var suppressedOwner,
                            out var suppressedKey))
                    {
                        continue;
                    }

                    wrote = true;
                    affectedOwners.Add(suppressedOwner);
#if DEBUG
                    WriteMirrorYesTrace(
                        document,
                        sourceHandle,
                        suppressedOwner,
                        suppressedKey,
                        "ok");
#endif
                }

                foreach (var id in appendedTimberIds)
                {
                    if (id.IsNull ||
                        id.IsErased ||
                        !AutoCadObjectIdAccess.TryGetObject<Line>(
                            transaction,
                            id,
                            OpenMode.ForWrite,
                            out var cloneLine,
                            document.Database) ||
                        cloneLine is null)
                    {
                        continue;
                    }

                    var generated = RoofGeneratedTimberStore.Read(cloneLine).Data;
                    if (generated is not null)
                    {
                        var keyBefore = RoofGeneratedMemberKey.From(generated);
                        var ownerReference = generated.RoofOwnerReference;
                        affectedOwners.Add(ownerReference);

                        if (!TryDetachAndPromote(
                                document,
                                transaction,
                                cloneLine,
                                ownerReference,
                                keyBefore,
                                out var roleAfter,
                                out var result))
                        {
#if DEBUG
                            WriteTrace(
                                document,
                                ownerReference,
                                cloneLine.Handle.ToString(),
                                keyBefore,
                                roleAfter,
                                result,
                                false);
#endif
                            continue;
                        }

                        wrote = true;
                        if (roleAfter == "attached-manual")
                        {
                            RefreshClonePresentation(document, transaction, id);
                        }
#if DEBUG
                        WriteTrace(
                            document,
                            ownerReference,
                            cloneLine.Handle.ToString(),
                            keyBefore,
                            roleAfter,
                            "ok",
                            roleAfter == "attached-manual");
#endif
                        _ = RoofAssemblyGroupSyncService.TrySyncForOwnerReference(
                            document,
                            transaction,
                            ownerReference);
                        continue;
                    }

                    // MIRROR of an existing AttachedManual child: the clone inherits the
                    // source AttachedManual section byte-for-byte. Re-initialize it from
                    // its final mirrored WCS geometry so it never keeps the source
                    // ChildIdentity / stale RelativeSegment. This is role-sensitive: BOTH
                    // Origin.Copy and Origin.Split sources are re-initialized here as an
                    // independent AttachedManual Origin.Copy child. A Split clone must never
                    // retain the source's Split ChildIdentity / anchor / RelativeSegment, and
                    // must never remain lifecycle-unclassified (annotation-less, un-erasable).
                    var attached = RoofAttachedManualTimberStore.Read(cloneLine);
                    if (attached.Data is null ||
                        attached.Data.AnchorGeneratedMemberKey is null)
                    {
                        continue;
                    }

                    var sourceOrigin = attached.Data.Origin;
                    if (sourceOrigin != RoofAttachedManualOrigin.Copy &&
                        sourceOrigin != RoofAttachedManualOrigin.Split)
                    {
                        continue;
                    }

                    var isSplitSource = sourceOrigin == RoofAttachedManualOrigin.Split;

                    var sourceIdentity = attached.Data.ChildIdentity;
                    var attachedOwner = attached.Data.RoofOwnerReference;
                    var inheritedKey = attached.Data.AnchorGeneratedMemberKey.Value;
                    affectedOwners.Add(attachedOwner);

                    if (!TryReinitializeAttachedManualClone(
                            document,
                            transaction,
                            cloneLine,
                            attachedOwner,
                            inheritedKey,
                            out var attachedRoleAfter,
                            out var attachedResult))
                    {
#if DEBUG
                        WriteAttachedManualTrace(
                            document,
                            sourceIdentity,
                            cloneLine.Handle.ToString(),
                            attachedOwner,
                            attachedRoleAfter,
                            attachedResult,
                            false);
#endif
                        continue;
                    }

                    wrote = true;
                    if (attachedRoleAfter == "attached-manual")
                    {
                        // AutoCAD MIRROR appends cloned annotations alongside the mirrored
                        // Line; each clone inherits the SOURCE handle and carries a mirrored
                        // text/block rotation residue (upside-down label). Remove ONLY those
                        // annotation clones APPENDED by this MIRROR command and bound to the
                        // source identity — never any pre-existing source annotation (a source
                        // timber may own several legitimate annotations: item label,
                        // dimension, slope/auxiliary). Deterministic command-lifecycle identity.
                        // The canonical refresh that follows recreates the child's complete
                        // annotation set from its FINAL mirrored geometry.
                        DeleteMirroredCloneAnnotations(
                            document,
                            transaction,
                            appendedAnnotationIds,
                            sourceIdentity);
                        RefreshClonePresentation(document, transaction, id);
                    }
#if DEBUG
                    WriteAttachedManualTrace(
                        document,
                        sourceIdentity,
                        cloneLine.Handle.ToString(),
                        attachedOwner,
                        attachedRoleAfter,
                        "ok",
                        attachedRoleAfter == "attached-manual");
                    if (isSplitSource)
                    {
                        WriteSplitCloneTrace(
                            document,
                            sourceIdentity,
                            cloneLine.Handle.ToString(),
                            attachedOwner,
                            attachedRoleAfter,
                            inheritedKey,
                            attachedRoleAfter == "attached-manual");
                    }
#endif
                    _ = RoofAssemblyGroupSyncService.TrySyncForOwnerReference(
                        document,
                        transaction,
                        attachedOwner);
                }

                // MIRROR Yes (Generated): HOST-proven lifecycle. AutoCAD transforms the
                // selected Generated member IN PLACE — same ObjectId/handle survives, no
                // ObjectAppended clone, no ObjectErased source. Convert that SAME entity
                // from Generated to AttachedManual Origin.Copy and persist a Suppress
                // override for its original slot K. No clone is created by AutoCAD.
                foreach (var id in mirrorModifiedTimberIds)
                {
                    if (id.IsNull ||
                        id.IsErased ||
                        // A MIRROR No clone is also recorded in _modifiedIds; the clone
                        // branch above already handled it. Never double-process it.
                        appendedTimberIds.Contains(id) ||
                        !AutoCadObjectIdAccess.TryGetObject<Line>(
                            transaction,
                            id,
                            OpenMode.ForWrite,
                            out var inPlaceLine,
                            document.Database) ||
                        inPlaceLine is null)
                    {
                        continue;
                    }

                    var inPlaceGenerated = RoofGeneratedTimberStore.Read(inPlaceLine).Data;
                    if (inPlaceGenerated is null)
                    {
                        // Role-aware in-place MIRROR Yes: a modified id that is NOT
                        // Generated may still be an AttachedManual child (Origin.Copy OR
                        // Origin.Split) transformed IN PLACE (appended=0 / erased=0 /
                        // modified=1). Re-anchor it from FINAL mirrored WCS geometry,
                        // preserving its identity. Origin.Split is promoted to Origin.Copy;
                        // unknown/malformed members are left untouched.
                        if (!TryReanchorInPlaceAttachedManual(
                                document,
                                transaction,
                                inPlaceLine,
                                out var attachedOwner,
                                out var attachedOldAnchor,
                                out var attachedNewAnchor,
                                out var attachedOriginBefore,
                                out var attachedOriginAfter,
                                out var attachedRoleAfter,
                                out var attachedResult))
                        {
                            continue;
                        }

                        wrote = true;
                        affectedOwners.Add(attachedOwner);
                        if (attachedRoleAfter == "attached-manual")
                        {
                            RefreshClonePresentation(document, transaction, id);
                        }
#if DEBUG
                        WriteInPlaceMirrorYesAttachedTrace(
                            document,
                            inPlaceLine.Handle.ToString(),
                            attachedOwner,
                            attachedOldAnchor,
                            attachedNewAnchor,
                            attachedOriginBefore,
                            attachedOriginAfter,
                            attachedRoleAfter,
                            attachedResult);
#endif
                        _ = RoofAssemblyGroupSyncService.TrySyncForOwnerReference(
                            document,
                            transaction,
                            attachedOwner);
                        continue;
                    }

                    var inPlaceKey = RoofGeneratedMemberKey.From(inPlaceGenerated);
                    var inPlaceOwner = inPlaceGenerated.RoofOwnerReference;
                    affectedOwners.Add(inPlaceOwner);

                    // Capture current ElementId for Suppress semantics before any metadata
                    // is cleared.
                    string? inPlaceElementId = null;
                    var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
                    if (metadataStore.TryRead(inPlaceLine, out var inPlaceTimberData) &&
                        inPlaceTimberData is not null)
                    {
                        inPlaceElementId = inPlaceTimberData.ElementId;
                    }

                    var suppressed = TryWriteSuppressOverride(
                        document,
                        transaction,
                        inPlaceOwner,
                        inPlaceKey,
                        inPlaceElementId);

                    if (!TryConvertInPlaceToAttachedManual(
                            document,
                            transaction,
                            inPlaceLine,
                            inPlaceOwner,
                            inPlaceKey.MemberKind,
                            out var inPlaceRoleAfter,
                            out var inPlaceResult))
                    {
#if DEBUG
                        WriteInPlaceMirrorYesTrace(
                            document,
                            inPlaceLine.Handle.ToString(),
                            inPlaceOwner,
                            inPlaceKey,
                            inPlaceRoleAfter,
                            suppressed,
                            false,
                            inPlaceResult);
#endif
                        continue;
                    }

                    wrote = true;
                    if (inPlaceRoleAfter == "attached-manual")
                    {
                        RefreshClonePresentation(document, transaction, id);
                    }
#if DEBUG
                    WriteInPlaceMirrorYesTrace(
                        document,
                        inPlaceLine.Handle.ToString(),
                        inPlaceOwner,
                        inPlaceKey,
                        inPlaceRoleAfter,
                        suppressed,
                        inPlaceRoleAfter == "attached-manual",
                        inPlaceResult);
#endif
                    _ = RoofAssemblyGroupSyncService.TrySyncForOwnerReference(
                        document,
                        transaction,
                        inPlaceOwner);
                }

#if DEBUG
                if (wrote || affectedOwners.Count > 0)
                {
                    WriteInvariant(document, transaction, affectedOwners);
                }
#endif

                if (wrote)
                {
                    transaction.Commit();
                }
            }
        }
        catch (System.Exception)
        {
            // Silent internal maintenance — do not break native MIRROR UX.
        }
    }

    private static bool TrySuppressErasedGeneratedSource(
        Document document,
        Transaction transaction,
        string sourceHandle,
        out string ownerReference,
        out RoofGeneratedMemberKey key)
    {
        ownerReference = string.Empty;
        key = default;

        if (!TryParseHandle(sourceHandle, out var handleValue))
        {
            return false;
        }

        ObjectId id;
        try
        {
            id = document.Database.GetObjectId(false, new Handle(handleValue), 0);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }

        if (id.IsNull || !id.IsValid ||
            !AutoCadObjectIdAccess.TryGetObjectAllowErased<Entity>(
                transaction,
                id,
                OpenMode.ForRead,
                out var entity,
                document.Database) ||
            entity is null)
        {
            return false;
        }

        var generated = RoofGeneratedTimberStore.Read(entity).Data;
        if (generated is null)
        {
            // Not a Generated source (generic timber / AttachedManual child): no
            // suppression. Its permanent erase is already native + annotation cleanup.
            return false;
        }

        key = RoofGeneratedMemberKey.From(generated);
        ownerReference = generated.RoofOwnerReference;

        string? elementId = null;
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        if (metadataStore.TryRead(entity, out var timberData) && timberData is not null)
        {
            elementId = timberData.ElementId;
        }

        return TryWriteSuppressOverride(
            document,
            transaction,
            ownerReference,
            key,
            elementId);
    }

    // Shared suppression write: persists RoofGeneratedMemberOverride.Suppress(key,
    // elementId) into the owner RoofDefinitionData.ManualOverrides so a later
    // SupportedResize never regenerates slot K. Reuses the proven Generated ERASE
    // suppression architecture (EditState preserved, not forced).
    private static bool TryWriteSuppressOverride(
        Document document,
        Transaction transaction,
        string ownerReference,
        RoofGeneratedMemberKey key,
        string? elementId)
    {
        if (!TryResolveOwnerPolyline(
                document.Database,
                transaction,
                ownerReference,
                out var owner) ||
            owner is null)
        {
            return false;
        }

        var stored = RoofDefinitionStore.Read(owner);
        if (stored.Data is null)
        {
            return false;
        }

        var overrides = new RoofManualOverrideSet(stored.Data.Overrides);
        overrides = overrides.Upsert(RoofGeneratedMemberOverride.Suppress(key, elementId));
        var updated = RoofGeneratedMemberOverrideRules.WithEditState(
            stored.Data,
            stored.Data.EditState,
            overrides.Items);

        owner.UpgradeOpen();
        RoofDefinitionStore.Write(owner, transaction, updated);
        return true;
    }

    private static bool TryDetachAndPromote(
        Document document,
        Transaction transaction,
        Line cloneLine,
        string ownerReference,
        RoofGeneratedMemberKey keyBefore,
        out string roleAfter,
        out string result)
    {
        roleAfter = "detached";
        result = "-";

        // 1. Detach Generated XData (removes the duplicate key + ownership).
        try
        {
            if (!RoofGeneratedTimberStore.TryClear(cloneLine, transaction, out var clearReason))
            {
                result = clearReason;
                return false;
            }
        }
        catch (System.Exception)
        {
            result = "clear-exception";
            return false;
        }

        if (RoofGeneratedTimberStore.Read(cloneLine).Data is not null)
        {
            result = "generated-xdata-remains";
            return false;
        }

        if (!TryPromoteFromMirroredGeometry(
                document,
                transaction,
                cloneLine,
                ownerReference,
                keyBefore.MemberKind,
                out result))
        {
            // Mirrored geometry has no compatible Generated anchor (e.g. mirrored
            // outside the footprint / unclassifiable orientation). Leave it detached
            // as plain generic timber — never fabricate roof ownership.
            roleAfter = "generic-timber";
            return true;
        }

        roleAfter = "attached-manual";
        return true;
    }

    // MIRROR Yes in-place conversion: the SAME Generated entity H must cease being a
    // Generated member and become an AttachedManual Origin.Copy child (ChildIdentity = H)
    // anchored to a compatible LIVE Generated neighbor, using H's final mirrored WCS
    // geometry. Clearing H's Generated XData first both removes its duplicate key AND
    // self-excludes H from anchor discovery (FindByOwner only returns live Generated
    // members), so H can never be its own anchor.
    private static bool TryConvertInPlaceToAttachedManual(
        Document document,
        Transaction transaction,
        Line line,
        string ownerReference,
        RoofGeneratedTimberKind memberKind,
        out string roleAfter,
        out string result)
    {
        roleAfter = "detached";
        result = "-";

        // 1. Detach Generated XData (captured owner/key in the caller before this).
        if (!RoofGeneratedTimberStore.TryClear(line, transaction, out var clearReason))
        {
            result = "clear-failed:" + clearReason;
            return false;
        }

        if (RoofGeneratedTimberStore.Read(line).Data is not null)
        {
            result = "generated-xdata-remains";
            return false;
        }

        // 2. Re-anchor the same entity from its final mirrored WCS. H is no longer in
        // FindByOwner, so it cannot select itself as its own anchor.
        if (!TryPromoteFromMirroredGeometry(
                document,
                transaction,
                line,
                ownerReference,
                memberKind,
                out result))
        {
            // No compatible LIVE Generated anchor (mirrored outside footprint /
            // unclassifiable orientation). H is now detached plain generic timber —
            // never fabricate roof ownership, never keep a stale Generated key.
            roleAfter = "generic-timber";
            return true;
        }

        roleAfter = "attached-manual";
        return true;
    }

    private static bool TryReinitializeAttachedManualClone(
        Document document,
        Transaction transaction,
        Line cloneLine,
        string ownerReference,
        RoofGeneratedMemberKey inheritedAnchorKey,
        out string roleAfter,
        out string result)
    {
        roleAfter = "detached";
        result = "-";

        if (!TryPromoteFromMirroredGeometry(
                document,
                transaction,
                cloneLine,
                ownerReference,
                inheritedAnchorKey.MemberKind,
                out result))
        {
            // No compatible anchor: drop the inherited stale AttachedManual ownership
            // so the clone becomes plain generic timber (never a stale roof child).
            if (RoofAttachedManualTimberStore.TryClear(cloneLine, transaction, out var clearReason))
            {
                roleAfter = "generic-timber";
                return true;
            }

            result = "attached-clear-failed:" + clearReason;
            return false;
        }

        roleAfter = "attached-manual";
        return true;
    }

    private static bool TryPromoteFromMirroredGeometry(
        Document document,
        Transaction transaction,
        Line cloneLine,
        string ownerReference,
        RoofGeneratedTimberKind memberKind,
        out string result)
    {
        result = "-";

        var candidates = new List<RoofReanchorCandidate>();
        foreach (var genId in RoofGeneratedTimberStore.FindByOwner(
                     document.Database,
                     transaction,
                     ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    genId,
                    OpenMode.ForRead,
                    out var genLine,
                    document.Database) ||
                genLine is null)
            {
                continue;
            }

            var genData = RoofGeneratedTimberStore.Read(genLine).Data;
            if (genData is null)
            {
                continue;
            }

            candidates.Add(new RoofReanchorCandidate(
                RoofGeneratedMemberKey.From(genData),
                ToRoof(genLine.StartPoint),
                ToRoof(genLine.EndPoint)));
        }

        var selected = RoofAttachedManualReanchorRules.SelectNearestMirrorAnchor(
            memberKind,
            candidates,
            ToRoof(cloneLine.StartPoint),
            ToRoof(cloneLine.EndPoint));

        if (selected is null)
        {
            result = "no-compatible-anchor";
            return false;
        }

        var attachedData = RoofAttachedManualLifecycleService.CreateAnchoredData(
            ownerReference,
            cloneLine.Handle.ToString(),
            selected.Key,
            ToAcad(selected.Start),
            ToAcad(selected.End),
            cloneLine.StartPoint,
            cloneLine.EndPoint,
            RoofAttachedManualOrigin.Copy);
        RoofAttachedManualLifecycleService.WriteAnchored(cloneLine, transaction, attachedData);
        result = "ok";
        return true;
    }

    // MIRROR Yes in-place for an AttachedManual child: the SAME entity H was transformed
    // IN PLACE (appended=0 / erased=0 / modified=1). Preserve H's identity (same
    // handle/ChildIdentity/owner/Role=AttachedManual) and re-anchor from its FINAL
    // mirrored WCS geometry, recomputing RelativeSegment against a compatible LIVE
    // Generated neighbor. Origin.Copy stays Origin.Copy; Origin.Split is promoted to
    // Origin.Copy (MIRROR Yes = "erase original, keep mirrored copy", so the surviving
    // result must NOT retain Split exact-anchor semantics). Unknown/malformed members are
    // left untouched.
    private static bool TryReanchorInPlaceAttachedManual(
        Document document,
        Transaction transaction,
        Line inPlaceLine,
        out string ownerReference,
        out RoofGeneratedMemberKey oldAnchorKey,
        out RoofGeneratedMemberKey newAnchorKey,
        out RoofAttachedManualOrigin originBefore,
        out RoofAttachedManualOrigin originAfter,
        out string roleAfter,
        out string result)
    {
        ownerReference = string.Empty;
        oldAnchorKey = default;
        newAnchorKey = default;
        originBefore = RoofAttachedManualOrigin.Copy;
        originAfter = RoofAttachedManualOrigin.Copy;
        roleAfter = "unchanged";
        result = "-";

        var attached = RoofAttachedManualTimberStore.Read(inPlaceLine);
        if (attached.Data is null ||
            (attached.Data.Origin != RoofAttachedManualOrigin.Copy &&
             attached.Data.Origin != RoofAttachedManualOrigin.Split))
        {
            // Not a valid AttachedManual Origin.Copy/Origin.Split child; unknown /
            // malformed members must not be mutated blindly.
            return false;
        }

        originBefore = attached.Data.Origin;
        ownerReference = attached.Data.RoofOwnerReference;
        oldAnchorKey = attached.Data.AnchorGeneratedMemberKey ?? default;
        var memberKind = attached.Data.AnchorGeneratedMemberKey?.MemberKind
            ?? RoofGeneratedTimberKind.Rafter;

        // Re-anchor from FINAL mirrored WCS geometry. TryPromoteFromMirroredGeometry
        // writes CreateAnchoredData(owner, sameHandle, selectedKey, ..., Origin.Copy), so
        // ChildIdentity stays equal to the entity's own (unchanged) handle AND Origin
        // becomes Copy. For an Origin.Copy source this is a no-op origin change; for an
        // Origin.Split source this is the required Split -> Copy promotion.
        if (!TryPromoteFromMirroredGeometry(
                document,
                transaction,
                inPlaceLine,
                ownerReference,
                memberKind,
                out result))
        {
            // No compatible Generated anchor after the mirror (mirrored outside the
            // footprint / unclassifiable orientation). Keep the existing AttachedManual
            // metadata untouched (do NOT clear ownership) and still refresh the annotation
            // so the label follows the geometry.
            roleAfter = "attached-manual";
            newAnchorKey = oldAnchorKey;
            originAfter = originBefore;
            return true;
        }

        var rewritten = RoofAttachedManualTimberStore.Read(inPlaceLine);
        newAnchorKey = rewritten.Data?.AnchorGeneratedMemberKey ?? oldAnchorKey;
        originAfter = RoofAttachedManualOrigin.Copy;
        roleAfter = "attached-manual";
        result = "ok";
        return true;
    }

    private static bool TryParseHandle(string handleText, out long handleValue) =>
        long.TryParse(
            handleText,
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out handleValue);

    private static bool TryResolveOwnerPolyline(
        Database database,
        Transaction transaction,
        string ownerReference,
        out Polyline? owner)
    {
        owner = null;
        if (!TryParseHandle(ownerReference, out var handleValue))
        {
            return false;
        }

        try
        {
            var id = database.GetObjectId(false, new Handle(handleValue), 0);
            return AutoCadObjectIdAccess.TryGetObject<Polyline>(
                       transaction,
                       id,
                       OpenMode.ForRead,
                       out owner,
                       database) &&
                   owner is not null;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static RoofPoint3D ToRoof(Point3d point) => new(point.X, point.Y, point.Z);

    private static Point3d ToAcad(RoofPoint3D point) => new(point.X, point.Y, point.Z);

    /// <summary>
    /// Removes ONLY the annotation clones that native MIRROR APPENDED for the mirrored
    /// child. A KROVY timber legitimately owns MULTIPLE annotation entities (item label,
    /// dimension, slope / auxiliary), so identity is command-lifecycle based, never
    /// geometry proximity. Each appended annotation bound to the surviving SOURCE identity
    /// is necessarily a clone of this MIRROR command (the source's own annotations already
    /// existed and were NOT appended). Pre-existing source annotations are therefore never
    /// touched, and no midpoint/nearest heuristic is used.
    /// </summary>
    private static void DeleteMirroredCloneAnnotations(
        Document document,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> appendedAnnotationIds,
        string sourceIdentity)
    {
        if (appendedAnnotationIds is null ||
            appendedAnnotationIds.Count == 0 ||
            string.IsNullOrWhiteSpace(sourceIdentity))
        {
            return;
        }

        foreach (var id in appendedAnnotationIds)
        {
            if (id.IsNull ||
                id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    document.Database) ||
                entity is null)
            {
                continue;
            }

            // The clone annotation deep-copies the source's XData, so it resolves to the
            // SOURCE handle. Being in the appended set AND bound to the source identity is
            // the deterministic marker of a MIRROR clone for this child.
            if (!RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(entity, out var handle) ||
                !string.Equals(handle, sourceIdentity, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var writable,
                    document.Database) &&
                writable is not null)
            {
                writable.Erase();
            }
        }
    }

    private static void RefreshClonePresentation(
        Document document,
        Transaction transaction,
        ObjectId cloneId)
    {
        // Same AK_RECALC-equivalent pipeline used for accepted AttachedManual edits:
        // InitializeLocalCopies → SynchronizeElementIdsDetailed → EnsureForElement →
        // DeleteDuplicatesForExistingSourceHandles. Produces current measurements,
        // a current ElementId/signature, and exactly one clone-handle-bound annotation.
        _ = ElementLabelService.UpdateInCurrentTransaction(
            document.Database,
            transaction,
            document.Editor,
            new[] { cloneId },
            new[] { cloneId });
    }

#if DEBUG
    private static void WriteTrace(
        Document document,
        string ownerReference,
        string cloneHandle,
        RoofGeneratedMemberKey keyBefore,
        string roleAfter,
        string result,
        bool annotationRefresh)
    {
        try
        {
            document.Editor?.WriteMessage(
                "\nROOF_MIRROR_TRACE" +
                $" clone={cloneHandle}" +
                $" owner={ownerReference}" +
                $" generatedKeyBefore={RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(keyBefore)}" +
                $" roleAfter={roleAfter}" +
                $" annotationRefresh={(annotationRefresh ? "true" : "false")}" +
                $" result={result}");
        }
        catch
        {
        }
    }

    private static void WriteAttachedManualTrace(
        Document document,
        string source,
        string clone,
        string owner,
        string roleAfter,
        string result,
        bool annotationRefresh)
    {
        try
        {
            document.Editor?.WriteMessage(
                "\nROOF_MIRROR_TRACE" +
                $" source={source}" +
                $" clone={clone}" +
                $" owner={owner}" +
                $" sourceRole=AttachedManual" +
                $" roleAfter={roleAfter}" +
                $" annotationRefresh={(annotationRefresh ? "true" : "false")}" +
                $" result={result}");
        }
        catch
        {
        }
    }

    private static void WriteSplitCloneTrace(
        Document document,
        string source,
        string clone,
        string owner,
        string roleAfter,
        RoofGeneratedMemberKey inheritedKey,
        bool annotationRefresh)
    {
        try
        {
            document.Editor?.WriteMessage(
                "\nROOF_MIRROR_SPLIT_CLONE" +
                $" source={source}" +
                $" clone={clone}" +
                $" sourceOrigin=Split" +
                $" roleAfter={roleAfter}" +
                $" originAfter={(roleAfter == "attached-manual" ? "Copy" : "none")}" +
                $" childIdentity={clone}" +
                $" anchor={RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(inheritedKey)}" +
                $" annotationRefresh={(annotationRefresh ? "true" : "false")}" +
                $" result=ok");
        }
        catch
        {
        }
    }

    private static void WriteMirrorYesTrace(
        Document document,
        string source,
        string owner,
        RoofGeneratedMemberKey generatedKey,
        string result)
    {
        try
        {
            document.Editor?.WriteMessage(
                "\nROOF_MIRROR_YES" +
                $" source={source}" +
                $" owner={owner}" +
                $" sourceRole=Generated" +
                $" generatedKey={RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(generatedKey)}" +
                $" suppression=true" +
                $" result={result}");
        }
        catch
        {
        }
    }

    private static void WriteInPlaceMirrorYesTrace(
        Document document,
        string handle,
        string owner,
        RoofGeneratedMemberKey generatedKey,
        string roleAfter,
        bool suppression,
        bool annotationRefresh,
        string result)
    {
        try
        {
            document.Editor?.WriteMessage(
                "\nROOF_MIRROR_YES" +
                $" handle={handle}" +
                $" owner={owner}" +
                $" mode=in-place" +
                $" sourceRole=Generated" +
                $" generatedKey={RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(generatedKey)}" +
                $" suppression={(suppression ? "true" : "false")}" +
                $" roleAfter={roleAfter}" +
                $" annotationRefresh={(annotationRefresh ? "true" : "false")}" +
                $" result={result}");
        }
        catch
        {
        }
    }

    private static void WriteInPlaceMirrorYesAttachedTrace(
        Document document,
        string handle,
        string owner,
        RoofGeneratedMemberKey oldAnchor,
        RoofGeneratedMemberKey newAnchor,
        RoofAttachedManualOrigin originBefore,
        RoofAttachedManualOrigin originAfter,
        string roleAfter,
        string result)
    {
        try
        {
            document.Editor?.WriteMessage(
                "\nROOF_MIRROR_YES_ATTACHED" +
                $" handle={handle}" +
                $" owner={owner}" +
                $" mode=in-place" +
                $" sourceRole=AttachedManual" +
                $" originBefore={originBefore}" +
                $" originAfter={originAfter}" +
                $" oldAnchor={RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(oldAnchor)}" +
                $" newAnchor={RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(newAnchor)}" +
                $" childIdentityPreserved=true" +
                $" annotationRefresh={(roleAfter == "attached-manual" ? "true" : "false")}" +
                $" result={result}");
        }
        catch
        {
        }
    }

    private static void WriteInvariant(
        Document document,
        Transaction transaction,
        IReadOnlyCollection<string> affectedOwners)
    {
        foreach (var ownerReference in affectedOwners)
        {
            var generatedIds = RoofGeneratedTimberStore.FindByOwner(
                document.Database,
                transaction,
                ownerReference);
            var attachedManual = RoofAttachedManualTimberStore.FindByOwner(
                document.Database,
                transaction,
                ownerReference).Count;

            var suppressed = 0;
            if (TryResolveOwnerPolyline(
                    document.Database,
                    transaction,
                    ownerReference,
                    out var owner) &&
                owner is not null &&
                RoofDefinitionStore.Read(owner).Data is { } definition)
            {
                suppressed = new RoofManualOverrideSet(definition.Overrides).SuppressedCount;
            }

            var seen = new HashSet<(RoofGeneratedTimberKind, RafterRoofFace, int)>();
            var duplicateKeys = 0;
            foreach (var id in generatedIds)
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        document.Database) ||
                    entity is null)
                {
                    continue;
                }

                var data = RoofGeneratedTimberStore.Read(entity).Data;
                if (data is null)
                {
                    continue;
                }

                if (!seen.Add((data.MemberKind, data.RoofFace, data.StationIndex)))
                {
                    duplicateKeys++;
                }
            }

            try
            {
                document.Editor?.WriteMessage(
                    "\nROOF_MIRROR_INVARIANT" +
                    $" owner={ownerReference}" +
                    $" generatedActual={generatedIds.Count}" +
                    $" attachedManual={attachedManual}" +
                    $" suppressed={suppressed}" +
                    $" duplicateKeyCount={duplicateKeys}" +
                    $" uniqueStations={(duplicateKeys == 0 ? "true" : "false")}" +
                    $" annotationReady=true" +
                    $" result=ok");
            }
            catch
            {
            }
        }
    }
#endif
}
