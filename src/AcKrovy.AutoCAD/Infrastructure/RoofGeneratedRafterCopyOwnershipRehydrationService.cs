using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;

using AcKrovy.Core.Services.Roofs;

using Autodesk.AutoCAD.ApplicationServices;

using Autodesk.AutoCAD.DatabaseServices;

using Autodesk.AutoCAD.Geometry;



namespace AcKrovy.AutoCAD.Infrastructure;



/// <summary>

/// Same-DWG native COPY ownership rehydration for roof-generated rafters.

/// AutoCAD does not remap generated-timber 1005 soft pointers; geometry matching

/// rebinds copied members to the copied roof. Writes join the COPY undo group via

/// the existing StartUndoMark / EndUndoMark lifecycle.

/// </summary>

internal static class RoofGeneratedRafterCopyOwnershipRehydrationService

{

    public static void Process(

        Document document,

        string? globalCommandName,

        IReadOnlyCollection<ObjectId>? appendedTimberIds = null)

    {

        ArgumentNullException.ThrowIfNull(document);

        if (LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName) ||

            !LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(globalCommandName))

        {

            return;

        }



        var appendedKeys = CollectAppendedMemberKeys(appendedTimberIds);

#if DEBUG

        TraceAppendedCandidates(document, appendedTimberIds, appendedKeys);

#endif



        try

        {

            using (document.LockDocument())

            using (var transaction = document.Database.TransactionManager.StartTransaction())

            {

                var owners = CollectOwners(document.Database, transaction);

                var observations = CollectObservations(document.Database, transaction);

                if (owners.Count == 0 || observations.Count == 0)

                {

#if DEBUG

                    RoofGeneratedCopyLifecycleDiag.WriteProcessSummary(

                        document.Editor,

                        globalCommandName,

                        appendedKeys.Count,

                        0,

                        committed: false);

#endif

                    return;

                }



                var plan = RoofGeneratedRafterCopyAssociationRules.BuildPlan(owners, observations);

                var claimedKeys = plan.Associations

                    .SelectMany(association => association.Members)

                    .Select(member => member.MemberKey)

                    .ToHashSet(StringComparer.Ordinal);

                var rewriteHandles = plan.Associations

                    .Where(association => association.RequiresMetadataRewrite)

                    .SelectMany(association => association.Members)

                    .Select(member => member.MemberKey)

                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var appendedLines = CollectAppendedGeneratedLines(

                    document.Database,

                    transaction,

                    appendedTimberIds);

                var appendedDetach = RoofGeneratedRafterCopyDetachRules.FindAppendedCloneDetachHandles(

                    RoofGeneratedCopyPreCommandSnapshotService.GetPreCommandLogicalKeysByOwner(),

                    RoofGeneratedCopyPreCommandSnapshotService.GetPreCommandGeneratedHandles(),

                    appendedLines,

                    rewriteHandles);

                var orphansFromRules = RoofGeneratedRafterCopyOrphanRules.FindAllStandaloneDetachMemberKeys(

                    plan,

                    owners,

                    observations,

                    appendedKeys);

                var orphans = appendedDetach

                    .Concat(orphansFromRules)

                    .Distinct(StringComparer.OrdinalIgnoreCase)

                    .Where(memberKey => !IsConsumedWholeRoofKey(memberKey, observations))

                    .ToArray();

#if DEBUG

                RoofGeneratedCopyLifecycleDiag.WriteCopyClassifyStage(

                    document.Editor,

                    RoofGeneratedCopyPreCommandSnapshotService.GetPreCommandGeneratedHandles(),

                    appendedKeys,

                    claimedKeys,

                    appendedDetach);

                foreach (var memberKey in orphans)

                {

                    var observation = observations.FirstOrDefault(item =>

                        string.Equals(item.MemberKey, memberKey, StringComparison.Ordinal));

                    RoofGeneratedCopyLifecycleDiag.WriteClassify(

                        document.Editor,

                        memberKey,

                        claimedKeys.Contains(memberKey),

                        observation?.EffectiveOwnerReference,

                        observation is null

                            ? "-"

                            : $"{observation.Face}:s{observation.StationIndex}");

                }

#endif



                var wrote = false;

                foreach (var association in plan.Associations)

                {

                    if (!association.RequiresMetadataRewrite)

                    {

                        continue;

                    }



                    foreach (var member in association.Members)

                    {

                        if (!TryRewriteMember(

                                document.Database,

                                transaction,

                                member,

                                association.OwnerReference,

                                association.ExpectedLayout.Signature))

                        {

                            // Abort the whole write rather than leave a mixed owner set.

#if DEBUG

                            RoofGeneratedCopyLifecycleDiag.WriteProcessSummary(

                                document.Editor,

                                globalCommandName,

                                appendedKeys.Count,

                                orphans.Length,

                                committed: false);

#endif

                            return;

                        }



                        wrote = true;

                    }

                }



                var lockedCopyErased = 0;

                foreach (var memberKey in orphans)

                {

                    if (!TryProcessCopiedClone(

                            document,

                            transaction,

                            memberKey,

                            plan,

                            observations,

                            out var erasedLockedCopy))

                    {

#if DEBUG

                        RoofGeneratedCopyLifecycleDiag.WriteProcessSummary(

                            document.Editor,

                            globalCommandName,

                            appendedKeys.Count,

                            orphans.Length,

                            committed: false);

#endif

                        return;

                    }



                    if (erasedLockedCopy)

                    {

                        lockedCopyErased++;

                    }



                    wrote = true;

                }



                if (lockedCopyErased > 0)

                {

                    TransientNotificationService.Show(

                        "Command_Roof_LockedNotificationTitle",

                        "Command_Roof_LockedNotificationBody");

                }



                if (wrote)

                {

#if DEBUG

                    VerifyPostCopyInvariants(document, transaction, owners, orphans);

#endif

                    transaction.Commit();

#if DEBUG

                    RoofGeneratedCopyLifecycleDiag.WriteProcessSummary(

                        document.Editor,

                        globalCommandName,

                        appendedKeys.Count,

                        orphans.Length,

                        committed: true);

#endif

                }

#if DEBUG

                else

                {

                    RoofGeneratedCopyLifecycleDiag.WriteProcessSummary(

                        document.Editor,

                        globalCommandName,

                        appendedKeys.Count,

                        orphans.Length,

                        committed: false);

                }

#endif

            }

        }

#if DEBUG

        catch (System.Exception ex)

        {

            try

            {

                RoofGeneratedCopyLifecycleDiag.WriteError(

                    document.Editor,

                    "RoofGeneratedRafterCopyOwnershipRehydrationService.Process",

                    ex);

            }

            catch

            {

            }

        }

#else

        catch (System.Exception)

        {

            // Silent internal maintenance — do not break native COPY UX.

        }

#endif

    }



    private static IReadOnlyList<RoofGeneratedRafterCopyOwnerTarget> CollectOwners(

        Database database,

        Transaction transaction)

    {

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);

        var modelSpace = (BlockTableRecord)transaction.GetObject(

            blockTable[BlockTableRecord.ModelSpace],

            OpenMode.ForRead);

        var owners = new List<RoofGeneratedRafterCopyOwnerTarget>();

        foreach (ObjectId id in modelSpace)

        {

            if (id.IsErased ||

                transaction.GetObject(id, OpenMode.ForRead, false) is not Polyline polyline ||

                polyline.IsErased)

            {

                continue;

            }



            var stored = RoofDefinitionStore.Read(polyline);

            if (stored.Data is null)

            {

                continue;

            }



            var input = RoofPolylineExtractor.Extract(polyline);

            var validation = RoofFootprintValidator.Validate(input);

            if (!validation.IsValid || validation.Footprint is null)

            {

                continue;

            }



            var restored = RoofDefinitionPersistence.Restore(

                input,

                validation.Footprint,

                stored.Data);

            if (!restored.IsValid || restored.Geometry is null)

            {

                continue;

            }



            owners.Add(new RoofGeneratedRafterCopyOwnerTarget(

                polyline.Handle.ToString(),

                restored.Geometry));

        }



        return owners;

    }



    private static IReadOnlyList<RoofGeneratedRafterGeometryObservation> CollectObservations(

        Database database,

        Transaction transaction)

    {

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);

        var modelSpace = (BlockTableRecord)transaction.GetObject(

            blockTable[BlockTableRecord.ModelSpace],

            OpenMode.ForRead);

        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);

        var observations = new List<RoofGeneratedRafterGeometryObservation>();

        foreach (ObjectId id in modelSpace)

        {

            if (id.IsErased ||

                transaction.GetObject(id, OpenMode.ForRead, false) is not Line line ||

                line.IsErased)

            {

                continue;

            }



            var generated = RoofGeneratedTimberStore.Read(line);

            if (generated.Data is null ||

                generated.Data.MemberKind != RoofGeneratedTimberKind.Rafter ||

                !metadataStore.TryRead(line, out var timber) ||

                timber is null ||

                timber.ElementType != TimberElementType.Rafter)

            {

                continue;

            }



            observations.Add(new RoofGeneratedRafterGeometryObservation(

                line.Handle.ToString(),

                generated.Data.RoofOwnerReference,

                new RoofRafterGenerationRecipe(

                    timber.WidthMm,

                    timber.HeightMm,

                    generated.Data.RequestedMaximumSpacingMm,

                    timber.Material),

                generated.Data.RoofFace,

                generated.Data.StationIndex,

                generated.Data.StationCount,

                ToPlan(line.StartPoint),

                ToPlan(line.EndPoint),

                generated.Data.LayoutSignature));

        }



        return observations;

    }



    private static IReadOnlyCollection<string> CollectAppendedMemberKeys(

        IReadOnlyCollection<ObjectId>? appendedTimberIds)

    {

        if (appendedTimberIds is null || appendedTimberIds.Count == 0)

        {

            return [];

        }



        var keys = new List<string>(appendedTimberIds.Count);

        foreach (var id in appendedTimberIds)

        {

            if (id.IsNull || !id.IsValid)

            {

                continue;

            }



            keys.Add(id.Handle.ToString());

        }



        return keys;

    }

    private static IReadOnlyList<RoofGeneratedRafterCopyDetachRules.AppendedGeneratedLine> CollectAppendedGeneratedLines(

        Database database,

        Transaction transaction,

        IReadOnlyCollection<ObjectId>? appendedTimberIds)

    {

        if (appendedTimberIds is null || appendedTimberIds.Count == 0)

        {

            return [];

        }

        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);

        var lines = new List<RoofGeneratedRafterCopyDetachRules.AppendedGeneratedLine>(appendedTimberIds.Count);

        foreach (var id in appendedTimberIds)

        {

            if (!AutoCadObjectIdAccess.TryGetObject<Line>(

                    transaction,

                    id,

                    OpenMode.ForRead,

                    out var line,

                    database) ||

                line is null)

            {

                continue;

            }

            var generated = RoofGeneratedTimberStore.Read(line);

            if (generated.Data is null ||

                generated.Data.MemberKind != RoofGeneratedTimberKind.Rafter ||

                !metadataStore.TryRead(line, out var timber) ||

                timber is null ||

                timber.ElementType != TimberElementType.Rafter)

            {

                continue;

            }

            if (RoofGeneratedCopyPreCommandSnapshotService.IsConsumedWholeRoofClone(

                    line.Handle.ToString()))

            {

                // Clone already consumed by the whole-roof COPY branch (erased on

                // success, or excluded on failure). It must never enter the ordinary

                // per-rafter detach path under the inherited old owner.

                continue;

            }

            lines.Add(new RoofGeneratedRafterCopyDetachRules.AppendedGeneratedLine(

                line.Handle.ToString(),

                generated.Data.RoofOwnerReference,

                generated.Data.RoofFace,

                generated.Data.StationIndex));

        }

        return lines;

    }

    private static bool IsConsumedWholeRoofKey(

        string memberKey,

        IReadOnlyList<RoofGeneratedRafterGeometryObservation> observations)

    {

        foreach (var observation in observations)

        {

            if (string.Equals(observation.MemberKey, memberKey, StringComparison.Ordinal) &&

                RoofGeneratedCopyPreCommandSnapshotService.IsConsumedWholeRoofClone(

                    observation.MemberKey))

            {

                return true;

            }

        }

        return false;

    }



#if DEBUG

    private static void TraceAppendedCandidates(

        Document document,

        IReadOnlyCollection<ObjectId>? appendedTimberIds,

        IReadOnlyCollection<string> appendedKeys)

    {

        if (appendedTimberIds is null || appendedTimberIds.Count == 0)

        {

            return;

        }



        try

        {

            using var transaction = document.Database.TransactionManager.StartTransaction();

            foreach (var id in appendedTimberIds)

            {

                if (!AutoCadObjectIdAccess.TryGetObject<Line>(

                        transaction,

                        id,

                        OpenMode.ForRead,

                        out var line,

                        document.Database) ||

                    line is null)

                {

                    continue;

                }



                var generated = RoofGeneratedTimberStore.Read(line);

                var owner = generated.Data?.RoofOwnerReference ?? "-";

                var source = "-";

                if (generated.Data is not null)

                {

                    foreach (ObjectId otherId in ((BlockTableRecord)transaction.GetObject(

                                 ((BlockTable)transaction.GetObject(

                                     document.Database.BlockTableId,

                                     OpenMode.ForRead))[BlockTableRecord.ModelSpace],

                                 OpenMode.ForRead)))

                    {

                        if (otherId == id ||

                            !AutoCadObjectIdAccess.TryGetObject<Line>(

                                transaction,

                                otherId,

                                OpenMode.ForRead,

                                out var other,

                                document.Database) ||

                            other is null)

                        {

                            continue;

                        }



                        var otherGenerated = RoofGeneratedTimberStore.Read(other);

                        if (otherGenerated.Data is null ||

                            !string.Equals(

                                otherGenerated.Data.RoofOwnerReference,

                                generated.Data.RoofOwnerReference,

                                StringComparison.OrdinalIgnoreCase) ||

                            otherGenerated.Data.RoofFace != generated.Data.RoofFace ||

                            otherGenerated.Data.StationIndex != generated.Data.StationIndex)

                        {

                            continue;

                        }



                        source = other.Handle.ToString();

                        break;

                    }

                }



                RoofGeneratedCopyLifecycleDiag.WriteAppended(

                    document.Editor,

                    source,

                    line.Handle.ToString(),

                    owner);

            }



            transaction.Commit();

        }

        catch (System.Exception)

        {

        }



        _ = appendedKeys;

    }



    private static void VerifyPostCopyInvariants(

        Document document,

        Transaction transaction,

        IReadOnlyList<RoofGeneratedRafterCopyOwnerTarget> owners,

        IReadOnlyList<string> detachedKeys)

    {

        var detached = detachedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var owner in owners)

        {

            var matches = RoofGeneratedTimberStore.FindByOwner(

                document.Database,

                transaction,

                owner.OwnerReference);

            var members = new List<RoofGeneratedTimberData>();

            foreach (var id in matches)

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



                var stored = RoofGeneratedTimberStore.Read(entity);

                if (stored.Data is not null)

                {

                    members.Add(stored.Data);

                }

            }



            var unique = RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(members);
            var preKeysByOwner = RoofGeneratedCopyPreCommandSnapshotService.GetPreCommandLogicalKeysByOwner();
            preKeysByOwner.TryGetValue(owner.OwnerReference, out var expectedKeys);
            expectedKeys ??= Array.Empty<string>();
            var actualKeys = members
                .Select(member => RoofGeneratedRafterCopyDetachRules.FormatLogicalKey(
                    member.RoofFace,
                    member.StationIndex))
                .ToList();
            var missing = expectedKeys
                .Where(key => actualKeys.Count(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase)) == 0)
                .ToArray();
            var duplicate = actualKeys
                .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            RoofGeneratedCopyLifecycleDiag.WriteCopyInvariant(
                document.Editor,
                owner.OwnerReference,
                expectedKeys.Count,
                actualKeys.Count,
                unique,
                string.Join("|", RoofAttachedManualTimberStore.FindByOwner(
                        document.Database,
                        transaction,
                        owner.OwnerReference)
                    .Select(id => id.Handle.ToString())),
                missing.Length == 0 ? "none" : string.Join("|", missing),
                duplicate.Length == 0 ? "none" : string.Join("|", duplicate),
                unique && missing.Length == 0 && duplicate.Length == 0 ? "ok" : "fail");
        }



        foreach (var detachedKey in detached)

        {

            if (!TryOpenGeneratedLine(

                    document.Database,

                    transaction,

                    detachedKey,

                    out var entity) ||

                entity is null)

            {

                continue;

            }



            var stillGenerated = RoofGeneratedTimberStore.Read(entity).Data;

            RoofGeneratedCopyLifecycleDiag.WriteDetach(

                document.Editor,

                detachedKey,

                stillGenerated is null ? "ok" : "generated-xdata-remains",

                stillGenerated?.RoofOwnerReference,

                RoofGeneratedCopyLifecycleDiag.FormatGeneratedKey(stillGenerated));

        }

    }

#endif



    private static bool TryRewriteMember(

        Database database,

        Transaction transaction,

        RoofGeneratedRafterGeometryObservation member,

        string ownerReference,

        string layoutSignature)

    {

        if (!TryOpenGeneratedLine(database, transaction, member.MemberKey, out var entity) ||

            entity is null)

        {

            return false;

        }



        var current = RoofGeneratedTimberStore.Read(entity);

        if (current.Data is null ||

            current.Data.MemberKind != RoofGeneratedTimberKind.Rafter ||

            current.Data.RoofFace != member.Face ||

            current.Data.StationIndex != member.StationIndex ||

            current.Data.StationCount != member.StationCount)

        {

            return false;

        }



        RoofGeneratedTimberStore.Write(

            entity,

            transaction,

            current.Data with

            {

                RoofOwnerReference = ownerReference,

                LayoutSignature = layoutSignature,

            });

        return true;

    }



    private static bool TryProcessCopiedClone(

        Document document,

        Transaction transaction,

        string memberKey,

        RoofGeneratedRafterCopyAssociationPlan plan,

        IReadOnlyList<RoofGeneratedRafterGeometryObservation> observations,

        out bool erasedLockedCopy)

    {

        erasedLockedCopy = false;

        var observation = observations.FirstOrDefault(item =>

            string.Equals(item.MemberKey, memberKey, StringComparison.Ordinal));

        if (observation is null)

        {

#if DEBUG

            RoofGeneratedCopyLifecycleDiag.WriteDetach(

                document.Editor,

                memberKey,

                "observation-missing",

                "-",

                "-");

#endif

            return false;

        }



        if (!IsOwnerUnlocked(document.Database, transaction, observation.EffectiveOwnerReference))

        {

            if (!TryEraseLockedCopyClone(document, transaction, memberKey))

            {

                return false;

            }



            erasedLockedCopy = true;

            return true;

        }



        if (!TryPromoteAttachedManualClone(

            document,

            transaction,

            memberKey,

            plan,

            observations))

        {

            if (!RoofCopiedChildRollbackService.TryRollbackCopiedRoofChild(

                    document,

                    transaction,

                    memberKey,

                    out _))

            {

                return false;

            }



#if DEBUG

            RoofGeneratedMemberManualEditDiag.WriteGeneratedCopy(

                document.Editor,

                "-",

                memberKey,

                observation.EffectiveOwnerReference,

                "attached-manual-rollback",

                "ok");

#endif

            return true;

        }



        return true;

    }



    private static bool IsOwnerUnlocked(

        Database database,

        Transaction transaction,

        string ownerReference)

    {

        if (!TryResolveOwnerPolyline(database, transaction, ownerReference, out var owner))

        {

            return false;

        }



        if (owner is null)

        {

            return false;

        }



        var stored = RoofDefinitionStore.Read(owner);

        return stored.Data?.EditState == RoofEditState.Unlocked;

    }



    private static bool TryResolveOwnerPolyline(

        Database database,

        Transaction transaction,

        string ownerReference,

        out Polyline? owner)

    {

        owner = null;

        if (!TryParseEntityHandle(database, ownerReference, out var ownerId))

        {

            return false;

        }



        return AutoCadObjectIdAccess.TryGetObject<Polyline>(

            transaction,

            ownerId,

            OpenMode.ForRead,

            out owner,

            database) &&

            owner is not null &&

            RoofDefinitionStore.Read(owner).Data is not null;

    }



    private static bool TryEraseLockedCopyClone(

        Document document,

        Transaction transaction,

        string memberKey)

    {

        if (!RoofCopiedChildRollbackService.TryRollbackCopiedRoofChild(

                document,

                transaction,

                memberKey,

                out _))

        {

            return false;

        }



#if DEBUG

        RoofGeneratedMemberManualEditDiag.WriteGeneratedCopy(

            document.Editor,

            "-",

            memberKey,

            "-",

            "locked-copy-erased",

            "ok");

#endif

        return true;

    }



    private static bool TryPromoteAttachedManualClone(

        Document document,

        Transaction transaction,

        string memberKey,

        RoofGeneratedRafterCopyAssociationPlan plan,

        IReadOnlyList<RoofGeneratedRafterGeometryObservation> observations)

    {

        var observation = observations.FirstOrDefault(item =>

            string.Equals(item.MemberKey, memberKey, StringComparison.Ordinal));

        if (observation is null)

        {

#if DEBUG

            RoofGeneratedCopyLifecycleDiag.WriteDetach(

                document.Editor,

                memberKey,

                "observation-missing",

                "-",

                "-");

#endif

            return false;

        }



        if (!TryOpenGeneratedLine(

                document.Database,

                transaction,

                memberKey,

                out var entity) ||

            entity is null)

        {

#if DEBUG

            RoofGeneratedCopyLifecycleDiag.WriteDetach(

                document.Editor,

                memberKey,

                "open-failed",

                observation.EffectiveOwnerReference,

                $"{observation.Face}:s{observation.StationIndex}");

#endif

            return false;

        }



        var before = RoofGeneratedTimberStore.Read(entity).Data;

#if DEBUG

        var keyBefore = RoofGeneratedCopyLifecycleDiag.FormatGeneratedKey(before);

#endif

        try

        {

            if (!RoofGeneratedTimberStore.TryClear(entity, transaction, out var clearReason))

            {

#if DEBUG

                RoofGeneratedCopyLifecycleDiag.WriteDetach(

                    document.Editor,

                    memberKey,

                    clearReason,

                    before?.RoofOwnerReference,

                    keyBefore);

#endif

                WriteCopyFail(document, observation.EffectiveOwnerReference, memberKey, clearReason);

                return false;

            }

        }

        catch (System.Exception)

        {

#if DEBUG

            RoofGeneratedCopyLifecycleDiag.WriteDetach(

                document.Editor,

                memberKey,

                "clear-exception",

                before?.RoofOwnerReference,

                keyBefore);

#endif

            WriteCopyFail(document, observation.EffectiveOwnerReference, memberKey, "clear-generated-xdata");

            return false;

        }



        var after = RoofGeneratedTimberStore.Read(entity).Data;

        if (after is not null)

        {

#if DEBUG

            RoofGeneratedCopyLifecycleDiag.WriteDetach(

                document.Editor,

                memberKey,

                "generated-xdata-remains",

                after.RoofOwnerReference,

                RoofGeneratedCopyLifecycleDiag.FormatGeneratedKey(after));

#endif

            WriteCopyFail(document, observation.EffectiveOwnerReference, memberKey, "generated-xdata-remains");

            return false;

        }



        if (entity is not Line cloneLine)
        {
            WriteCopyFail(document, observation.EffectiveOwnerReference, memberKey, "not-line");
            return false;
        }

        var sourceMemberKey = plan.Associations

            .FirstOrDefault(association =>

                string.Equals(

                    association.OwnerReference,

                    observation.EffectiveOwnerReference,

                    StringComparison.OrdinalIgnoreCase))

            ?.Members

            .FirstOrDefault(member =>

                member.Face == observation.Face &&

                member.StationIndex == observation.StationIndex)

            ?.MemberKey;

        RoofAttachedManualTimberData attachedData;
        if (!string.IsNullOrWhiteSpace(sourceMemberKey) &&
            TryOpenGeneratedLine(
                document.Database,
                transaction,
                sourceMemberKey,
                out var sourceEntity) &&
            sourceEntity is Line sourceLine &&
            RoofGeneratedTimberStore.Read(sourceLine).Data is { } sourceGenerated)
        {
            var anchorKey = RoofGeneratedMemberKey.From(sourceGenerated);
            attachedData = RoofAttachedManualLifecycleService.CreateAnchoredData(
                observation.EffectiveOwnerReference,
                memberKey,
                anchorKey,
                sourceLine.StartPoint,
                sourceLine.EndPoint,
                cloneLine.StartPoint,
                cloneLine.EndPoint,
                RoofAttachedManualOrigin.Copy);
        }
        else
        {
            // No source Generated anchor resolved via the association plan. A COPY clone
            // must NEVER become a malformed AttachedManual child (missing anchor /
            // RelativeSegment): such a child is silently skipped by resize replay and
            // stays stale outside the roof. Resolve a compatible live Generated anchor in
            // the same owner (reusing the proven re-anchor rule); only if none exists,
            // leave the clone detached as plain generic timber — never a non-replaying
            // roof child.
            if (TryResolveCopyCloneAnchor(
                    document.Database,
                    transaction,
                    observation,
                    cloneLine,
                    out var fallbackAnchor) &&
                fallbackAnchor is not null)
            {
                attachedData = RoofAttachedManualLifecycleService.CreateAnchoredData(
                    observation.EffectiveOwnerReference,
                    memberKey,
                    fallbackAnchor.Key,
                    ToAcad(fallbackAnchor.Start),
                    ToAcad(fallbackAnchor.End),
                    cloneLine.StartPoint,
                    cloneLine.EndPoint,
                    RoofAttachedManualOrigin.Copy);
            }
            else
            {
                _ = RoofAttachedManualTimberStore.TryClear(cloneLine, transaction, out _);
#if DEBUG
                RoofGeneratedCopyLifecycleDiag.WriteDetach(
                    document.Editor,
                    memberKey,
                    "no-compatible-anchor",
                    observation.EffectiveOwnerReference,
                    "generic-timber");
#endif
                WriteCopyOk(document, "-", memberKey, observation.EffectiveOwnerReference);
                return true;
            }
        }

        RoofAttachedManualLifecycleService.WriteAnchored(cloneLine, transaction, attachedData);

#if DEBUG
        RoofAttachedManualLifecycleService.WriteAnchorDiag(
            document,
            cloneLine.Handle.ToString(),
            attachedData,
            "ok");
#endif

        if (!EnsureAttachedManualPresentation(document, transaction, cloneLine))

        {

            WriteCopyFail(

                document,

                observation.EffectiveOwnerReference,

                memberKey,

                "attached-manual-annotation-failed");

            return false;

        }



#if DEBUG

        RoofGeneratedCopyLifecycleDiag.WriteDetach(

            document.Editor,

            memberKey,

            "ok",

            observation.EffectiveOwnerReference,

            "AttachedManual");

#endif

        var source = plan.Associations

            .FirstOrDefault(association =>

                string.Equals(

                    association.OwnerReference,

                    observation.EffectiveOwnerReference,

                    StringComparison.OrdinalIgnoreCase))

            ?.Members

            .FirstOrDefault(member =>

                member.Face == observation.Face &&

                member.StationIndex == observation.StationIndex)

            ?.MemberKey ?? "-";

        WriteCopyOk(document, source, memberKey, observation.EffectiveOwnerReference);

        _ = RoofAssemblyGroupSyncService.TrySyncForOwnerReference(
            document,
            transaction,
            observation.EffectiveOwnerReference);

        return true;

    }



    private static bool EnsureAttachedManualPresentation(

        Document document,

        Transaction transaction,

        Entity entity)

    {

        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);

        if (!metadataStore.TryRead(entity, out var timberData) || timberData is null)

        {

            return false;

        }



        var defaultProfile = TimberElementDefaultProfileStore.Load();

        var batch = AutoCadAnnotationPresentationBatchContext.Create(

            document.Database,

            transaction,

            defaultProfile);

        try

        {

            // EnsureForElement returns true only when a new main label is created; false means updated in place.

            _ = TimberAnnotationService.EnsureForElement(

                document.Database,

                transaction,

                entity,

                timberData,

                batch,

                roundingStepMm: defaultProfile.GetCuttingLengthRoundingStepMm());

            return true;

        }

        catch (System.Exception)

        {

            return false;

        }

    }



    // Fallback anchor resolution for a COPY clone whose source Generated member could not
    // be resolved via the association plan. Prefers the live Generated member matching the
    // clone's logical key; else the nearest compatible station. Returns null when no
    // compatible anchor exists so the caller can leave the clone detached.
    private static bool TryResolveCopyCloneAnchor(
        Database database,
        Transaction transaction,
        RoofGeneratedRafterGeometryObservation observation,
        Line cloneLine,
        out RoofReanchorCandidate? anchor)
    {
        anchor = null;
        var candidateKey = new RoofGeneratedMemberKey(
            RoofGeneratedTimberKind.Rafter,
            observation.Face,
            observation.StationIndex);
        var candidates = new List<RoofReanchorCandidate>();
        foreach (var id in RoofGeneratedTimberStore.FindByOwner(
                     database,
                     transaction,
                     observation.EffectiveOwnerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var line,
                    database) ||
                line is null)
            {
                continue;
            }

            var generated = RoofGeneratedTimberStore.Read(line).Data;
            if (generated is null)
            {
                continue;
            }

            var key = RoofGeneratedMemberKey.From(generated);
            candidates.Add(new RoofReanchorCandidate(
                key,
                ToRoof(line.StartPoint),
                ToRoof(line.EndPoint)));
        }

        anchor = RoofAttachedManualReanchorRules.SelectNearestAnchor(
            candidateKey,
            candidates,
            ToRoof(cloneLine.StartPoint),
            ToRoof(cloneLine.EndPoint));
        return anchor is not null;
    }

    private static RoofPoint3D ToRoof(Point3d point) => new(point.X, point.Y, point.Z);

    private static Point3d ToAcad(RoofPoint3D point) => new(point.X, point.Y, point.Z);

    private static bool TryOpenGeneratedLine(

        Database database,

        Transaction transaction,

        string memberKey,

        out Entity? entity)

    {

        entity = null;

        if (!TryParseEntityHandle(database, memberKey, out var objectId))

        {

            return false;

        }



        return AutoCadObjectIdAccess.TryGetObject<Entity>(

            transaction,

            objectId,

            OpenMode.ForWrite,

            out entity,

            database) &&

            entity is not null;

    }



    private static bool TryParseEntityHandle(Database database, string memberKey, out ObjectId objectId)

    {

        objectId = ObjectId.Null;

        if (string.IsNullOrWhiteSpace(memberKey))

        {

            return false;

        }



        try

        {

            var hex = memberKey.Trim();

            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))

            {

                hex = hex[2..];

            }



            if (!long.TryParse(

                    hex,

                    System.Globalization.NumberStyles.HexNumber,

                    System.Globalization.CultureInfo.InvariantCulture,

                    out var handleValue))

            {

                return false;

            }



            objectId = database.GetObjectId(false, new Handle(handleValue), 0);

            return !objectId.IsNull;

        }

        catch (Autodesk.AutoCAD.Runtime.Exception)

        {

            return false;

        }

    }



    private static void WriteCopyOk(

        Document document,

        string source,

        string clone,

        string owner)

    {

#if DEBUG

        RoofGeneratedMemberManualEditDiag.WriteGeneratedCopy(

            document.Editor,

            source,

            clone,

            owner,

            "detach-to-attached-manual",

            "ok");

#else

        _ = document;

        _ = source;

        _ = clone;

        _ = owner;

#endif

    }



    private static void WriteCopyFail(

        Document document,

        string owner,

        string clone,

        string reason)

    {

#if DEBUG

        RoofGeneratedMemberManualEditDiag.WriteGeneratedCopy(

            document.Editor,

            "-",

            clone,

            owner,

            "detach-to-attached-manual",

            reason);

#else

        _ = document;

        _ = owner;

        _ = clone;

        _ = reason;

#endif

    }



    private static RoofPoint2D ToPlan(Point3d point) => new(point.X, point.Y);

}
