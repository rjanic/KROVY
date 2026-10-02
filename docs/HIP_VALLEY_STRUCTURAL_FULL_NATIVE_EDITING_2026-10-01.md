# Structural Hip/Valley full native editing — Priority Pack A WIP

Date: 2026-10-01  
Published baseline remains: `3e0e133` (Finalize structural hip and valley native edit lifecycle)  
Status: **CODE WIP — HOST partially accepted. NOT FINALIZED. NO COMMIT.**

## HOST accepted (ACA 2027, Unlocked Hip 30°)

| Case | Result |
| --- | --- |
| Hip eave-side TRIM | PASS — elevated Physical3D, identity stable, GROUP canonical |
| Hip ridge-side TRIM | PASS — no NRE, shortened upper end, miter cleared correctly |
| Hip ridge EXTEND back to canonical | PASS — automatic ridge treatment restored |

Do **not** re-solve these frozen baselines unless their logic changes.

## Semantic edit classes

Relative to automatic fold Plan XY (Z=0):

| Class | Meaning |
| --- | --- |
| `Automatic` | No Offset, no absolute Plan (or exact canonical restore normalized away) |
| `OffsetRigid` | Pure XY Offset, or absolute Plan parallel to fold with common lateral |
| `OnFoldSubsegment` | Absolute Plan on fold, parameters inside [0,1] |
| `OnFoldExtendedSegment` | Absolute Plan on fold, parameters beyond [0,1] |
| `ArbitraryPlanLine` | Skew / rotated off-fold — **rejected** (not a valid Hip/Valley fold member) |
| `Suppressed` | ERASE |

Materialization keys off geometry class, not command name. TRIM / STRETCH / GRIP_STRETCH with the same final Plan share one Place path.

## Plan → Physical

- Plan2D Z always 0.
- Physical Z from structural `UpperAxis` (eave→ridge builder invariant).
- Endpoint roles from fold parameter `t`, not native Line Start/End order.
- On-fold inward edit: extract end stations; ridge inward clears obsolete `UpperNodeMiterPlane` + envelope.
- Parallel offset absolute Plan (MOVE then TRIM): extract on-fold, then XY-translate.
- Exact canonical Plan restore: normalize to `Automatic` (no persistent Plan equal to automatic).

## Override state machine

| From | Command / event | To |
| --- | --- | --- |
| Automatic | MOVE | OffsetRigid |
| OffsetRigid | MOVE | OffsetRigid (composed) |
| OffsetRigid | TRIM/STRETCH/EXTEND on-fold/parallel | absolute Plan (Offset cleared) |
| Absolute Plan | MOVE | absolute Plan updated |
| Absolute Plan | TRIM/STRETCH/EXTEND | absolute Plan replaced |
| Absolute Plan | exact canonical endpoints | Automatic (cleared) |
| Any | ERASE | Suppressed |
| Suppressed | geometry accept | rejected |

Precedence: absolute Plan XOR Offset — never both. Absolute clears Offset on accept.

## Command matrix (product)

| Command | Class |
| --- | --- |
| MOVE | PERSISTENT TRUE EDIT (Offset or absolute update) |
| ERASE | PERSISTENT TRUE EDIT (Suppressed) |
| STRETCH | PERSISTENT TRUE EDIT (on-fold / parallel OffsetRigid) |
| GRIP_STRETCH | PERSISTENT TRUE EDIT (same as STRETCH) |
| TRIM | PERSISTENT TRUE EDIT (HOST Hip eave/ridge accepted) |
| EXTEND | PERSISTENT TRUE EDIT (HOST ridge restore accepted) |
| ROTATE | REJECTED / RESTORED — arbitrary rotation ≠ Hip/Valley fold member |
| SCALE | REJECTED / RESTORED — section must not scale; path-only ambiguous |
| BREAK | REJECTED / RESTORED — dual fragment vs one StructuralId |
| FILLET / CHAMFER / JOIN | REJECTED / RESTORED — not a single straight N/U axis |
| OFFSET / COPY / MIRROR / ARRAY* | CLONE REJECTED |
| EXPLODE | REJECTED / RESTORED — no detach model |
| Physical3D native edit | DERIVED PHYSICAL REBUILD |
| Locked Plan geometry | LOCKED RESTORE (Unclaimed → locked guard) |
| U / UNDO / REDO | Supported (native DWG; no first-claim writes) |
| SAVE/CLOSE/REOPEN | Supported (XRecord schema 1) |

## Domain conclusions

### ROTATE — REJECT
A Structural Hip/Valley is a timber on an actual roof fold. Arbitrary Plan rotation away from the fold is not a valid Structural Hip/Valley member even if a Solid3d can be extruded. Restore Plan2D.

### SCALE — REJECT
Cross-section width/height must not scale. Path-only SCALE without a clear on-fold product rule remains RestorePlan.

### BREAK — DEFERRED PRODUCT MODEL
Would need one StructuralId → manual child member(s) with new identities. Do not invent duplicate StructuralId. AttachedManual-style foundation only if complete (identity, provenance, group, undo, reopen).

### COPY/MIRROR/ARRAY/OFFSET — DEFERRED PRODUCT MODEL
Keep clone reject. Future: Generated Structural → AttachedManual Structural with new identity, same owner, independent Plan, derived Physical. Reuse ordinary AttachedManual patterns only when schema-complete.

### EXPLODE — DEFERRED
Needs explicit detach semantics (Plan only / Solid / both / annotations / group / ownership loss). Keep RestorePlan until specified.

## Diagnostics

DEBUG audit `STRUCTURAL_SOLID` parses generation signatures:

`StructuralPhysical1|width|height|mode|Automatic|dx|dy`  
`StructuralPhysical1|width|height|mode|Plan|x1|y1|x2|y2`

Exposes `placementMode`, `placementPayload`, `metadataValid=True` when valid. Does not change production XData.

## Performance

`TryReconcileInTransaction` rebuilds **all** non-suppressed Structural Physical3D solids for the owner after any accepted claim. Intentional for set consistency; per-member narrowing deferred (correctness first).

## Remaining HOST matrix (do not retest A–C unless logic changes)

4. STRETCH eave inward on-fold  
5. STRETCH ridge inward on-fold  
6. GRIP eave inward  
7. GRIP ridge inward  
8. MOVE + TRIM composition  
9. Undo/Redo of accepted Plan edit  
10. SAVE/CLOSE/REOPEN mixed overrides  
11. Locked STRETCH/TRIM/EXTEND/ROTATE  
12. Valley boundary + inner TRIM + STRETCH  
13. ROTATE — expect restore (if probing)  
14. SCALE — expect restore (if probing)

## Deferred / not finalized

Independent Structural clone → AttachedManual; BREAK multi-fragment; FILLET/CHAMFER/JOIN; EXPLODE detach; Full Compatibility Gate before HOST completion of remaining matrix.
