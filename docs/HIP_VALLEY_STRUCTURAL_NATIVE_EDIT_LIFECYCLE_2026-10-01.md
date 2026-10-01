# Structural Hip/Valley native edit lifecycle — final closure

Date: 2026-10-01  
Milestone: STRUCTURAL HIP/VALLEY NATIVE EDIT LIFECYCLE COMPLETE  
Foundation: `c98cfb29a01b23c7f38f610a483b6b5122523aad` (`c98cfb2` — Finalize structural hip and valley edit foundation)

## Scope

Complete native-edit claim routing for generated Structural Hip/Valley Plan2D and
StructuralRafterSolid Physical3D on top of the HOST-accepted foundation
(MOVE/ERASE + GROUP). Ordinary automatic-rafter lifecycle is a frozen baseline.

## Final product semantics

- One logical StructuralId under one roof owner has at most one canonical Plan2D
  reference (unless suppressed) and at most one Structural Physical3D solid.
- Unlocked Plan2D MOVE persists planar XY placement; Physical3D rebuilds from it.
- Unlocked Plan2D ERASE persists suppression; Physical3D disappears and stays gone.
- Individual COPY/MIRROR/ARRAY clones inheriting owner + StructuralId are rejected
  (erased), not promoted to independent structural members.
- Whole-roof COPY/MIRROR remains owned by existing `RoofWholeRoofCopyRebindService`.
- Unsupported geometry edits restore Plan2D (Z=0) and rebuild Physical3D/display/GROUP.
- Locked Plan2D restore-family edits are owned exclusively by the existing Locked
  generated-member guard (structural first-claim leaves them Unclaimed).

## Command classification matrix

### SUPPORTED / PERSISTENT

| Command | Behavior |
| --- | --- |
| Unlocked Plan2D MOVE | AcceptPlan — persistent XY offset |
| Unlocked Plan2D ERASE | AcceptPlan — persistent suppression |
| Physical3D MOVE/ERASE/STRETCH/GRIP_STRETCH | RebuildPhysical |
| Whole-roof COPY/MIRROR | Existing rebind (before structural claim) |
| U / UNDO / REDO / MREDO | Native DWG undo; zero DB at undo boundary |
| SAVE / CLOSE / REOPEN | Owner XRecord `AK_ROOF_STRUCTURAL_EDITS` |

### REJECTED / RESTORED

| Command | Behavior |
| --- | --- |
| Individual COPY / MIRROR | RejectClone — erase clone, restore source |
| ARRAY / ARRAYRECT / ARRAYPOLAR / ARRAYPATH | RejectClone |
| Unlocked ROTATE / SCALE | RestorePlan |
| Unlocked Plan2D STRETCH / GRIP_STRETCH | RestorePlan + display rebuild + GROUP |
| BREAK / BREAKATPOINT / TRIM / EXTEND | RestorePlan (erase fragments) |
| FILLET / CHAMFER / JOIN / OFFSET / EXPLODE | RestorePlan |

### LOCKED

| Command | Behavior |
| --- | --- |
| Plan2D MOVE / ERASE | Structural RestorePlan (foundation) |
| Plan2D STRETCH / ROTATE / BREAK / … | Unclaimed → Locked generated-member guard |
| Physical3D edits | RebuildPhysical |

### DEFERRED

Independent Structural clone becoming AttachedManual/manual timber — no approved
product model in this milestone. Individual clones remain RejectClone.

## Key implementation architecture

1. Whole-roof rebind first.
2. `RoofStructuralNativeEditService` first-claim (Hip/Valley Plan2D or
   StructuralRafterSolid only).
3. Ordinary `ProcessNativeMemberClones` for Generated/AttachedManual.
4. Generic live-resize / Locked guard for remaining candidates.
5. Semantic state: owner extension dictionary XRecord, no Handle/ObjectId/ElementId.
6. Physical3D always from canonical Core builder + semantic placement (never host
   Solid3d readback).
7. Exact-slot GROUP sync; committed finalize verifies uniqueness and Plan2D Z=0.

## HOST bug fixes during acceptance

1. **Locked Plan2D STRETCH** — structural RestorePlan raced the Locked guard, failed
   GROUP sync, and emitted a false “3D prvok…” error. Fix: Locked + Plan2D +
   restore-family → Unclaimed so the Locked guard is sole owner.
2. **Unlocked Plan2D STRETCH** — classic STRETCH mutates display children; claim
   restored Plan2D/Physical then GROUP sync failed on stale display. Fix: after
   Physical reconcile, `RoofDisplayService.Rebuild(..., syncAssemblyGroup: false)`,
   then `TrySyncForOwner`.

## Plan2D Z=0 contract

RestorePlan/RejectClone write snapshot XY with Z forced to 0. Committed verification
rejects any live structural Plan2D with non-zero endpoint Z. HOST persistence proved
moved Hip Plan2D remains Z=0 after SAVE/CLOSE/REOPEN.

## Ordinary-rafter non-regression

Structural candidates never read Generated/AttachedManual stores.
`IsAssemblySnapshotCommand` is not widened for ARRAY/structural-only commands.
Ordinary STRETCH/GRIP still route through existing ProcessOwners path.

## HOST acceptance matrix (ACA 2027)

| # | Case | Result |
| --- | --- | --- |
| 1 | Individual COPY Plan2D | PASS |
| 2 | Individual MIRROR Plan2D | PASS |
| 3 | ARRAYRECT Plan2D | PASS |
| 4 | ROTATE Plan2D | PASS |
| 5 | STRETCH Locked/Unlocked + Physical/GRIP | PASS |
| 6 | BREAK Plan2D | PASS |
| 7 | Locked Plan MOVE + ERASE | PASS |
| 8 | U/UNDO/REDO after rejected COPY | PASS |
| 9 | SAVE/CLOSE/REOPEN after MOVE −500 mm + ERASE | PASS |

Persistence proof (item 9): Unlocked; erased Hip absent; moved Hip −500 mm;
Plan2D Z=0; Physical generation signature contains `Automatic|-500|0`;
StructuralRafterSolid=3; OrdinaryRafterSolid=32; GROUP expected=181 actual=181
duplicates=0 missing=0 foreign=0 canonical=True.

Foundation A/B/C (Locked Physical MOVE, Unlocked Plan MOVE, Unlocked Plan ERASE)
remains accepted and was not re-run for this closure.

## Automated validation

| Check | Result |
| --- | --- |
| Structural focused filter | 135 PASS, 0 failed, 0 skipped |
| Ordinary-rafter lifecycle regression | 430 PASS, 0 failed, 0 skipped |
| Full Core | 7,068 PASS, 0 failed, 0 skipped |
| Full WPF | 806 PASS, 0 failed, 0 skipped |
| Debug x64 (warnaserror) | PASS, 0 warnings, 0 errors |
| Release x64 (warnaserror) | PASS, 0 warnings, 0 errors |
| Portable Compatibility Gate | PASS |
| Full Compatibility Gate | PASS |
| `git diff --check` | PASS |

## Known deferred product decisions

- Independent Structural COPY/ARRAY as AttachedManual (or other manual child) is
  out of scope until an explicit product model is approved.
- Endpoint-edit semantics for Structural Plan2D (true TRIM/EXTEND accept) remain
  rejected/restored.

## Final closure status

HOST matrix 1–9 accepted. All required CODE closure gates PASS.
Milestone published after commit/push to `origin/main`.
