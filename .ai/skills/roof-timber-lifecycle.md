# KROVY Skill: Roof Timber Lifecycle

## Purpose

Use this skill for AutoCAD roof-timber lifecycle work: COPY, MIRROR, ERASE, BREAK, TRIM,
STRETCH/GRIP, Generated ownership, AttachedManual children, dormancy/reactivation,
Generated suppression, group sync, and U/UNDO/REDO behavior.

## When to use

Use this skill before designing or changing persistence, undo/redo, ownership, or
recovery for any roof-generated-member or AttachedManual lifecycle operation.

## HOST event sequence is authoritative

For AutoCAD lifecycle-sensitive commands such as COPY, MIRROR, ERASE, BREAK, TRIM,
STRETCH and U/REDO:

- Source-contract/unit tests prove code contracts, not actual AutoCAD callback
  ordering.
- Never assume MIRROR/COPY/etc. means a particular sequence such as
  ObjectAppended -> ObjectErased -> CommandEnded.
- Compare the real HOST path from CommandWillStart through ObjectAppended,
  ObjectModified, ObjectErased and CommandEnded.
- Find the first control-flow divergence between working and failing command
  variants.
- When the real sequence cannot be proven statically, add narrow temporary DEBUG
  diagnostics and request one HOST run before changing architecture.
- Absence of an expected success diagnostic in HOST is evidence that the intended
  production path did not execute.
- Do not change persistence/schema/undo architecture to compensate for an event
  sequence that has not been proven in AutoCAD.
- Preserve the zero-DB U/UNDO/REDO boundary rule.

## Known lifecycle invariants

These invariants are HOST-proven and must not be regressed:

- Generated creation order: `AppendEntity` -> `WriteAtomic` -> `AddNewlyCreatedDBObject` (T2).
- ASCII-only canonical Generated identity; no `1005`.
- One Generated XData setter.
- `DECORAIR_ACADKROVY_ROOF_TIMBER` Generated / `DECORAIR_ACADKROVY_ROOF_ATTACHED_MANUAL` / generic timber sections.
- AttachedManual `Origin` distinguishes Copy (follows anchor) from Split (keep-in-place).
- Dormancy uses `Entity.Visible = false`; Generated suppression uses `ManualOverrides`.
- Resize never performs nearest-anchor remapping; anchor selection is creation/reposition-time only.
- Zero DB access around U/UNDO/REDO/MREDO.

## Split / BREAK resize lifecycle

Reusable rule:

> AttachedManual Origin.Split is a persistent roof child, not disposable resize
> geometry. Temporary loss of its exact Generated anchor should use dormancy and
> exact-key reactivation, not permanent deletion.

- On SupportedResize, both Origin.Copy and Origin.Split children anchor-replay against
  their rebuilt exact Generated anchor (`ReplayAnchoredChildrenForOwner`) — never a
  nearest-station remap.
- If the exact anchor station disappears (footprint shrinks), the Split fragment goes
  dormant (`Visible=false` + annotation removed), retaining Origin.Split, ChildIdentity,
  anchor key and RelativeSegment. When the exact anchor returns it reactivates and replays.
- A Split fragment must never be permanently deleted by a temporary footprint shrink, and
  must never be routed through the legacy keep-in-place/delete-outside policy.
- Multiple Split fragments may share the same Generated anchor K; each keeps an independent
  ChildIdentity + RelativeSegment and replays independently without overlap.
- `ReplayAnchoredChildrenForOwner` preserves Origin/ChildIdentity/anchor/RelativeSegment
  (it replays WCS from the persisted RelativeSegment; it does not call CreateAnchoredData).

## Generated manual-edit identity reservation

Reusable rule:

> When a manual edit changes a Generated member's item signature and identity, any
> persisted `ReservedElementId`/identity reservation used by future rebuilds must be
> synchronized to the final reconciled ElementId. A rebuild must never restore an item
> number belonging to a different signature.

- `RoofGeneratedMemberOverride.ReservedElementId` forces a rebuilt Generated member's
  ElementId on SupportedResize (`RoofGeneratedRafterSetService` assigns it to the recreated
  member). If it is stale it collides with another signature's item group.
- Override composition reuses the EXISTING reservation (`existing.ReservedElementId ??
  reservedElementId`), so a repeated length-changing edit (e.g. repeated BREAK) keeps the
  pre-recalc number in the override while recalc moves the live element to a new number.
- Fix: after an accepted targeted recalc changes a member's signature, re-upsert the
  override with `ReservedElementId` set to the FINAL reconciled ElementId (read from the
  reconciled timber metadata). This is the shared accepted-edit path
  (BREAK, split-TRIM, TRIM, EXTEND, endpoint GRIP_STRETCH), not BREAK-only.
- Same signature must still share one item number; different signatures must not collide.
  The sync only changes the reservation to the number the identity system already assigned.
- All identity/override synchronization happens in the normal accepted-edit transaction,
  never at a U/UNDO/REDO/MREDO boundary.

## Repeated splitting is role-sensitive

Reusable rule:

> Repeated splitting is role-sensitive. BREAK of a Generated member produces
> Generated + AttachedManual Origin.Split. BREAK of an existing AttachedManual
> Origin.Split produces AttachedManual Origin.Split + AttachedManual Origin.Split.
> Never assume the pre-command source handle is Generated.

- BREAK of a Generated member (HOST PASS, unchanged): Generated surviving fragment
  remains Generated; the new fragment becomes AttachedManual Origin.Split anchored to the
  surviving Generated key.
- BREAK of an AttachedManual Origin.Split child: NO new Generated member. The source
  fragment keeps its own handle/ChildIdentity, stays Origin.Split, and recomputes its
  RelativeSegment from its post-BREAK geometry; the appended fragment gets a new
  ChildIdentity, Origin.Split, the SAME exact persisted anchor key, and an independently
  captured RelativeSegment. Neither fragment enters the Generated recipe.
- BREAK is not MOVE: never nearest-reanchor the split fragments; they keep the source's
  exact persisted anchor key.
- The split promotion path (`TryAttachManualSplitFragment`) is role-aware: it reads the
  source's metadata — Generated data first, else AttachedManual Origin.Split data — and
  uses the source's exact anchor in both cases. Emit `ROOF_GENERATED_SPLIT` only when the
  source is Generated, and `ROOF_ATTACHED_MANUAL_SPLIT` when it is an AttachedManual Split.
- Middle split-TRIM on an Origin.Split child shares the same role-aware path (both BREAK
  and split-TRIM are split commands); ordinary one-sided shortening TRIM (no second
  fragment) is unaffected.

## Origin.Copy replay metadata integrity

Reusable rule:

> A COPY clone must never become a malformed AttachedManual child (missing
> AnchorGeneratedMemberKey or RelativeSegment), because resize replay would silently
> skip it and it would stay stale outside the roof. Replay must never silently skip a
> recognized-origin child — it should go dormant (hidden) instead.

- A valid Origin.Copy child must carry: Role=AttachedManual, Origin=Copy, ChildIdentity,
  RoofOwnerReference, AnchorGeneratedMemberKey, RelativeSegment, and a stable Visible flag.
- At COPY creation, if the source Generated anchor cannot be resolved via the association
  plan, resolve a compatible live Generated anchor (reusing `SelectNearestAnchor`); only if
  none exists, leave the clone detached as plain generic timber — never a v1 child with no
  anchor/relative.
- `ReplayAnchoredChildrenForOwner` (Copy or Split filter): a child matching the origin
  filter but missing anchor/relative is made DORMANT (Visible=false, annotation removed,
  metadata/identity retained) and counted — not silently skipped.
- On resize: anchor survives → replay RelativeSegment against the exact anchor; anchor
  missing → dormant; exact anchor returns → reactivate. No nearest-remap during resize
  (MOVE may re-anchor at edit time; resize uses the exact persisted anchor only).
- MOVE re-anchor preserves Origin.Copy, ChildIdentity, owner, and writes new anchor +
  matching new RelativeSegment atomically. Later TRIM/EXTEND/GRIP/ROTATE edits must
  preserve Origin.Copy/anchor and recompute RelativeSegment — never write a v1 record.

## Proven HOST: MIRROR with Erase Source = Yes

HOST-proven on the KROVY target AutoCAD, NOT hypothetical:

- AutoCAD MIRROR with `Erase source objects? Yes` may transform the selected Generated
  member IN PLACE: the same ObjectId/handle survives with mirrored WCS geometry, and
  NO ObjectAppended clone and NO ObjectErased source are emitted.
- The SAME in-place event shape (`appendedCount=0`, `erasedCount=0`, `modifiedCount=1`)
  also occurs for an AttachedManual source (Origin.Copy OR Origin.Split): AutoCAD
  transforms the same AttachedManual child in place. Never infer AttachedManual MIRROR
  Yes from MIRROR No, and never assume MIRROR Yes always means a Generated source.
- Proven KROVY HOST event shape: `appendedCount=0`, `erasedCount=0`, `modifiedCount=1`.
- There are therefore FOUR distinct MIRROR lifecycles:
  - MIRROR No Generated: appended clone -> detach + promote to AttachedManual Origin.Copy.
  - MIRROR No AttachedManual: appended clone -> reinitialize from final WCS.
  - MIRROR Yes Generated: SAME entity modified in place -> convert the same entity to
    AttachedManual Origin.Copy (ChildIdentity = same handle) and persist a Suppress
    override for its original slot K.
  - MIRROR Yes AttachedManual (Origin.Copy OR Origin.Split): SAME entity modified in
    place -> re-anchor the same child from its FINAL mirrored WCS geometry (same
    handle/ChildIdentity/owner/Role preserved). Origin.Copy stays Origin.Copy; Origin.Split
    is PROMOTED to Origin.Copy (MIRROR Yes = "erase original, keep mirrored copy", so the
    surviving result must NOT retain Split exact-anchor semantics). Anchor + RelativeSegment
    are recomputed and the canonical annotation set is refreshed. NO Generated suppression
    and NO extra child (AttachedManual count unchanged).
- Never infer MIRROR Yes semantics from MIRROR No callback behavior.
- When command-specific lifecycle processing needs the raw modified ids, preserve them
  BEFORE generic roof-related candidate filtering (RoofLiveResizeService) removes them,
  and route them to the MIRROR service as the in-place candidate set.
- For an in-place conversion, clear the entity's Generated XData BEFORE anchor discovery
  so the entity cannot select itself as its own anchor; capture owner/key/elementId before
  clearing.
- In-place MIRROR Yes classification is PER-ENTITY from LIVE metadata after native MIRROR:
  Generated -> existing Generated branch; AttachedManual Origin.Copy OR Origin.Split ->
  re-anchor in place (reuse `TryPromoteFromMirroredGeometry` so ChildIdentity == the
  entity's own unchanged handle AND Origin is written as Copy); unknown/malformed -> leave
  untouched. The AttachedManual in-place branch emits one `ROOF_MIRROR_YES_ATTACHED` trace
  (`mode=in-place sourceRole=AttachedManual originBefore=<Copy|Split> originAfter=Copy
  oldAnchor=… newAnchor=… childIdentityPreserved=true annotationRefresh=true result=ok`).

## Source footprint is the only spatial authority

Reusable rule:

> The roof source closed Polyline is the ONLY authoritative geometric boundary of
> the roof. AutoCAD GROUP, annotation, generated-timber, and display-entity extents
> must never substitute for footprint containment/boundary decisions. GROUP is
> organizational (membership + selectability) only. Source-resize spatial validity
> applies to persistent AttachedManual children of BOTH Origin.Copy and Origin.Split:
> exact anchor existence is necessary but not sufficient — after replay the final child
> segment must be contained by the current source footprint; outside-footprint children
> go dormant and reactivate when a later footprint contains them.

- Inside/outside/containment decisions come from `RoofFootprintContainmentRules`
  (`IsPointInsideOrOnBoundary` / `IsSegmentInsideOrOnBoundary`) against the footprint
  vertices extracted by `RoofPolylineExtractor.Extract(owner)` — even-odd ray cast +
  segment crossing, polygon-safe (concave U/L shapes, not rectangle-only).
- Source-resize classification (`RoofDefinitionPersistence.Classify`) compares the
  extracted footprint descriptor (vertex count/orientation/edge lengths) against the
  persisted descriptor — polyline-pure, never group/annotation/selection extents.
- Persistent AttachedManual source-resize replay (Origin.Copy AND Origin.Split) requires
  BOTH an exact persisted Generated anchor key (`TryFindGeneratedAnchorLine`) AND final
  replayed geometry contained by the current source roof footprint. Anchor existence
  alone is not sufficient.
- Decision order for either origin: read valid metadata → resolve exact anchor → missing
  → dormant (`anchor-missing`) → else replay RelativeSegment → test final replayed
  segment with `RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary` against the
  current source footprint (from `RoofPolylineExtractor` + `RoofFootprintValidator`) →
  contained → visible/replayed → outside → dormant (`outside-footprint`, SAME
  `MakeCopyChildDormant` mechanism, entity NOT erased).
- The current source closed Polyline is the ONLY spatial authority. Group and
  annotation extents never participate; no "near roof" proximity tolerance, no
  group-bounding-box enclosure, no annotation-overlap check. Near vs far behave
  identically (containment only, not distance).
- A child (Copy OR Split) dormant because it is outside the footprint is NOT permanently
  dead: on a later source resize the same exact anchor + same RelativeSegment are
  retested against the NEW footprint; if contained, the same entity/handle/ChildIdentity
  reactivates and the annotation is restored exactly once. Source resize never
  nearest-reanchors.
- This footprint-containment rule applies to ANY persistent AttachedManual child
  (Origin.Copy AND Origin.Split) during SOURCE RESIZE only. Explicit MOVE may still
  nearest-reanchor Origin.Copy per the MOVE rules (Split never nearest-reanchors) and may
  leave the child visible; dormancy-from-containment happens on the next source resize.
- The shared containment decision does NOT merge Copy and Split semantics: Copy keeps
  explicit-MOVE nearest-reanchor, MIRROR/COPY and permanent ERASE; Split keeps
  BREAK/split-TRIM, its exact original Split anchor, MOVE-does-not-nearest-reanchor,
  permanent ERASE, and independent sibling evaluation. Neither origin is converted to the
  other, and dormancy is evaluated per-child (never anchor-wide).
- GROUP is maintained by `RoofDisplayGroupService` / `RoofAssemblyGroupMemberCollector`
  using member ObjectIds (`GetAllEntityIds`) and `Selectable` only. Never `GeometricExtents`
  / `GetBoundingBox` / `Extents3d` as a roof boundary.
- Keep annotations in the assembly group; do not change group membership merely to shrink
  the visual selection rectangle.

## Mirrored annotation orientation

Reusable rule:

> Mirrored timber annotation orientation must always be recomputed from the final
> canonical timber geometry. Never preserve a mirrored/cloned annotation rotation as
> authoritative. Repeated MIRROR must not alternate readable and upside-down label
> orientation.

- AutoCAD MIRROR clones the source timber annotation along with its Line; the clone
  annotation deep-copies the source XData (SOURCE handle) and carries a mirrored
  text/block rotation residue. For MIRROR No of an AttachedManual Origin.Copy child, the
  clone annotation is upside-down and is matched/preserved as if canonical.
- Fix: in `RoofMirrorCloneDetachService`, the AttachedManual clone path removes ONLY the
  annotation clones that native MIRROR APPENDED in THIS command and that are bound to the
  SOURCE identity, BEFORE the canonical `RefreshClonePresentation`, so the new child's
  annotation set is regenerated from its FINAL mirrored geometry — never a geometric mirror
  transform. The appended annotation ids come from the command lifecycle
  (`LiveGeometrySynchronizationService` union of appended labels/slope arrows/slope angle
  text). Pre-existing source annotations are NOT in the appended set and are never touched.
- Annotation identity is command-lifecycle based, NEVER geometry proximity. Do not keep or
  delete annotations by midpoint distance, bounding-box proximity, nearest entity, or
  visual location — a timber legitimately owns MULTIPLE annotations (item label, dimension,
  slope/auxiliary), and the source must retain its complete annotation set (Erase source =
  No).
- The annotation layer always reuses the existing canonical orientation rules
  (`TimberStandaloneNativeLeaderOrientationRules.ResolveTextPresentationRadians` /
  `ResolveTransformRadians`, `TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians`).
  Never add an unconditional +π or a mirror-specific angle algorithm.
- Erase source = No: the source child and its own annotation are preserved; only the
  redundant mirrored clone annotation is removed.
- This applies to any Origin.Copy child (Generated->COPY, COPY->COPY, MIRROR No/Yes
  promoted as Origin.Copy). Generated -> MIRROR No is HOST PASS and unchanged.

## MIRROR No is role-sensitive (Origin.Split source)

Reusable rule:

> MIRROR is role-sensitive. MIRROR No of an AttachedManual Origin.Split source must
> preserve the source as Split but reinitialize the newly mirrored clone as an independent
> AttachedManual Origin.Copy child. A native mirrored clone must never retain the source
> Split ChildIdentity or remain lifecycle-unclassified.

- A BREAK pair is lifecycle-heterogeneous: one surviving Generated fragment + one
  AttachedManual Origin.Split fragment. Never infer source role from length, position, or
  iteration order — read it from the clone's inherited XData (Generated vs AttachedManual
  Origin).
- `RoofMirrorCloneDetachService` processes each appended clone by its OWN inherited
  metadata: Generated XData -> Generated promote branch; AttachedManual XData (Origin.Copy
  OR Origin.Split) -> re-initialize as independent Origin.Copy from FINAL mirrored WCS.
- A Split clone re-initialized as Origin.Copy gets: Origin.Copy, new ChildIdentity = clone
  handle, same RoofOwnerReference, a compatible Generated anchor via
  `SelectNearestMirrorAnchor`, a fresh RelativeSegment, no Generated metadata, and the full
  canonical annotation set. It then inherits the HOST-PASS Copy lifecycle (exact-anchor
  replay, footprint containment, dormancy/reactivation, permanent Copy ERASE).
- The surviving Split source is never mutated by MIRROR No: same handle, ChildIdentity,
  anchor, RelativeSegment, complete annotation set.
- Do not invent a Split-specific ERASE rule: the Split-derived clone is Origin.Copy, so the
  existing permanent Copy ERASE applies (no suppression, no resurrection).

## ERASE is role-sensitive (Origin.Split permanent delete)

Reusable rule:

> ERASE is role-sensitive. Generated ERASE is suppression. AttachedManual Origin.Copy and
> AttachedManual Origin.Split ERASE are permanent child deletion. A valid AttachedManual
> child must be classified by its AttachedManual metadata before any Generated logical-key
> requirement or generated-erasure recovery.

- Generated ERASE = suppression override (recipe remains; AK_ROOF_RESET_EDITS restores it).
- AttachedManual Origin.Copy ERASE = permanent native deletion (annotation cleanup, no
  suppression, no resurrection). HOST PASS, unchanged.
- AttachedManual Origin.Split ERASE = the SAME permanent native deletion: only the selected
  Split fragment is deleted; its annotation set is removed; sibling Split fragments and the
  surviving Generated member are untouched; no suppression, no resurrection.
- Origin.Split does NOT require a Generated logical key. A valid Split child is classified
  by its AttachedManual XData (Origin=Split, owner-scoped by the command snapshot) BEFORE
  `TryResolveErasedMemberKey` — it must never fall through to `logical-key-missing`.
- `TryClassifyErasedCopyAttachedManual` accepts BOTH Origin.Copy and Origin.Split; the
  permanent-delete branch reports `copy-delete` vs `split-delete` and emits
  `ROOF_ATTACHED_MANUAL_ERASE origin=Split action=permanent-delete annotationCleanup=true
  recoverySuppressed=true`.
- Because a valid Split/Copy erase is ACCEPTED, `ProcessOwner` returns Accepted and the
  unsupported/generated erase recovery (`generated-timber-erased` member-probe) is never
  entered. An accepted permanent AttachedManual erase is "handled" by the accept path and
  must never be fed into generated-erasure recovery.

## Human approval

Do not commit or push unless explicitly approved.

Do not claim HOST PASS without actual AutoCAD execution.
