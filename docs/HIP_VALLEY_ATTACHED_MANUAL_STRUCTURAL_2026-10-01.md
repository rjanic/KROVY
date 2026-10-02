# Structural Hip/Valley — Attached Manual Structural foundation

Date: 2026-10-01  
Published baseline: `3e0e133`  
Status: **CODE WIP — TIER 1 (COPY + MOVE/ERASE + whole-roof rebind). NOT FINALIZED. NO COMMIT.**

Companion to `HIP_VALLEY_STRUCTURAL_FULL_NATIVE_EDITING_2026-10-01.md`.

## Product contract

| Class | Identity | Regenerates from topology? | Plan authority |
| --- | --- | --- | --- |
| Generated Structural | `Hip\|a\|b` / `Valley\|a\|b` | Yes + edit overrides | Owner XRecord + Line |
| Attached Manual Structural | `ManualStructural:<uuid>` | No | Line Plan Z=0 + provenance fold |

COPY (Unlocked) of Generated Structural Plan2D:
- Source stays Generated
- Clone drops Generated XData, receives AttachedManualStructural metadata
- Physical via fold-constrained Place (Model A): provenance `SourceLogicalKey` polyhedron + absolute Plan

COPY of existing Manual Structural:
- Source stays Manual (same ManualIdentity)
- Clone remints a NEW ManualIdentity; keeps SourceLogicalKey + section snapshot

## Placement model (decision)

**Model C — snapshotted rigid frame** (COPY):
- Model A fold Place is not the manual authority. A sideways copy leaves the fold.
- Capture source prism frame after the source member's own Place, translate by the Plan XY vector, persist schema 2.
- Rebuild with `TryBuildPrism`. Ordinary solids are not the manual model.
- Generated reconcile still builds the ordinary model for generated cuts, with `structuralReconcilePending: true`, so a transient duplicate `Hip|a|b` cannot fail as `OrdinaryPhysicalModelUnavailable`.
- MIRROR stays rejected. OFFSET stays RestorePlan.

## Schema

- RegApp: `DECORAIR_ACADKROVY_ROOF_STRUCTURAL_ATTACHED_MANUAL`
- Schema 1: identity without a frame
- Schema 2: identity plus rigid placement (13 reals)
- ProductVersion unchanged (0.23.0)

## Provenance

- `SourceLogicalKey` = original Generated Hip/Valley key (Hip\|a\|b)
- Manual→Manual COPY keeps the same SourceLogicalKey (no ParentManual id in schema 1)
- Identity never ObjectId/Handle

## Command matrix (this milestone)

### Generated Structural
| Cmd | Status |
| --- | --- |
| COPY | CREATES MANUAL STRUCTURAL (Unlocked); CLONE REJECTED Locked |
| MIRROR / ARRAY* | CLONE REJECTED |
| OFFSET | REJECTED / RESTORED |
| MOVE/ERASE/TRIM/EXTEND/STRETCH/GRIP | unchanged Pack A |
| ROTATE/SCALE/BREAK/… | unchanged reject |

### Attached Manual Structural
| Cmd | Status |
| --- | --- |
| created by COPY | schema 2 rigid frame |
| COPY of Manual | new ManualIdentity, frame translated on next reconcile if placement exists |
| MOVE | Plan Z=0; persisted frame translated in XY |
| ERASE | manual only; no Generated suppression |
| TRIM/EXTEND/STRETCH | Plan accepted; frame update not yet proven — do not HOST-test until M1 COPY passes |
| MIRROR/OFFSET/ARRAY | not creators |

## Whole-roof COPY/MIRROR

Order unchanged: whole-roof rebind → Structural native claim.
- Generated Structural clones: existing erase + rematerialize
- Structural AttachedManual clones: owner rebind **before** structural Materialize so Physical includes them
- Consumed whole-roof handles skipped by AcceptManualClone

## TIER 1 implemented surface

- Core: `RoofStructuralAttachedManualData`, identity + validation rules
- Host: `RoofStructuralAttachedManualStore`
- `RoofStructuralEditRules.AcceptManualClone` for COPY
- `RoofStructuralNativeEditService` Generated→Manual + Manual→Manual + MOVE/ERASE
- Physical reconcile builds Manual solids with `ManualStructural:<uuid>`
- GROUP collector includes Manual Plan lines
- Erase pre-command map + assembly snapshot include Manual Plan
- Whole-roof Structural AttachedManual rebind
- Core foundation + source-contract tests

## HOST pending (after gates)

M1 COPY Generated Hip → Manual child — retest after:
- atomic Generated→Manual XData write (`WriteReplacingGenerated`)
- convert failure no longer reports `result=committed`
- ordinary native-member disposable erase skips ManualStructural Plan/solid
- RigidCopy matches live Generated Plan Line
- MEMBER_CHECKPOINT enumerates StructuralAttachedManual

M2 MOVE Manual  
M3 TRIM Manual (Model A)  
M4 ERASE Manual  
M5–M7 MIRROR/OFFSET/ARRAY deferred  
M8 Valley COPY  
M9 SAVE/CLOSE/OPEN  
M10 U/UNDO/REDO  
M11 Locked COPY reject  
M12 Whole-roof COPY with Manual children  
M13 Roof regen — Manual Plan survives; solid dormancy if fold missing

## Deferred

MIRROR handedness, OFFSET elevation rule, ARRAY associative ownership, Manual ROTATE (Model A rejects skew), BREAK→two manuals, reporting ElementId renumber, AK_ROOF_3D_AUDIT MANUAL_* rows.
