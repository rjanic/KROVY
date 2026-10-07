# Ordinary Rafter ROTATE — final lifecycle checkpoint

**Date:** 2026-10-06
**CODE:** PASS
**HOST:** PASS
**Lifecycle defect:** CLOSED

This checkpoint records the completed Ordinary rafter ROTATE lifecycle. The
HOST matrix below is the user-provided manual AutoCAD validation result. No
additional AutoCAD operations were performed for this documentation update.

## 1. Purpose

Record the final product contract, implementation route, executed CODE
validation, and accepted AutoCAD HOST results for native ROTATE of Ordinary
rafters. This checkpoint supersedes the earlier OPEN HOST status in
[`ORDINARY_ROTATE_LIFECYCLE_2026-10-06.md`](ORDINARY_ROTATE_LIFECYCLE_2026-10-06.md).

## 2. Product contract

Plan2D Line geometry is authoritative; Physical3D is derived. AUTO Ordinary
ROTATE uses the shared detach confirmation: YES accepts the edit and detaches
the member, while NO restores the exact pre-command state. Existing Independent
members rotate without a detach prompt and retain their `IndependentMemberId`.
Edits to Physical3D alone are rejected and restored.

For accepted Independent geometry, Plan2D remains at Z=0, the section is
preserved exactly, the width axis remains horizontal, handedness is valid, and
Physical3D is rebuilt through the shared builder. Mixed Plan2D and Solid
selection gives authority to Plan2D and processes the logical member once.
One native ROTATE command uses one logical confirmation for all changed AUTO
members.

## 3. Architecture and shared lifecycle

Native ROTATE is routed through the existing Ordinary command snapshot and
first-claim lifecycle. The command-start snapshot captures Plan2D, paired
Physical3D, annotations, member metadata, physical build state, and the GROUP
baseline. At command completion the Ordinary ROTATE handler consumes final
native geometry before legacy roof/manual recovery paths. It groups changed
entities by logical Plan member and deduplicates Plan2D plus paired Solid.

Accepted members use the shared Ordinary acceptance and physical rebuild path,
then designation recalculation and package/GROUP verification before commit.
NO restores all affected packages and the exact GROUP baseline atomically. A
failed acceptance also aborts and restores the affected snapshots. The shared
warning preference controls confirmation UX only; it does not change model
semantics.

## 4. Physical Build State v2 integration

ROTATE uses the existing Independent Ordinary Physical Build State v2. Existing
Independent members use `persistent_member_xrecord` when available. Legacy
members without that state use the existing lazy migration to
`migrated_member_package`, which persists the migrated state for future edits.
No ROTATE-specific persistence format was added, and `TryRebase` semantics were
not weakened.

## 5. AUTO YES

The normal detach confirmation is shown. On YES, AUTO becomes Independent, gets
a new `IndependentMemberId`, clears Generated ownership, rebuilds Physical3D,
and is removed from AUTO GROUP membership. No suppression, geometry
ManualOverride, or AttachedManual fallback is written.

## 6. AUTO NO

On NO, the handler restores the exact captured lifecycle state. It does not
detach or create an Independent identity; the original `ElementId` and
canonical GROUP are preserved. The user visually confirmed the geometry
rollback. The accepted HOST result treats the existing deferred graphics
refresh diagnostic as non-blocking; see section 16.

## 7. Existing Independent members

Independent members do not show a detach confirmation. Their
`IndependentMemberId` remains stable, Physical3D is rebuilt from final Plan2D
using v2 build state, and the member remains outside AUTO GROUP. `ElementId`
continues to follow manufacturing-signature grouping and may change when the
signature rules require it.

## 8. Physical3D-only authority

A ROTATE affecting only a derived Solid is rejected. The Solid is restored,
Plan2D remains unchanged, and no detach or ownership change occurs. With the
modal warning disabled, AutoCAD may show the command-line notice:

> 3D prvok je odvodený z 2D. Úprava 3D bola zrušená.

Disabling the warning does not enable direct Physical3D editing.

## 9. Mixed selection

When a selection contains the Plan2D Line and its paired Physical3D Solid, the
final Plan2D geometry wins. The logical member is processed once, the Solid is
not duplicated, and Physical3D is rebuilt from the final Plan2D.

## 10. Multi-member ROTATE

The accepted mixed HOST case recorded `candidateCount=3`, `processedCount=3`,
`autoCount=1`, `independentCount=2`, and `mixedCount=3`. The command showed one
confirmation. The two existing Independent IDs remained stable; the AUTO
member detached with a new ID and cleared Generated ownership. All three
rebuilt successfully with `rollback=False`, and GROUP remained canonical.

## 11. Identity rules

- `IndependentMemberId` is the stable identity of one detached Independent
  instance.
- `ElementId` is a manufacturing designation derived from the current timber
  signature and follows the existing grouping/reassignment rules.
- A newly detached AUTO member receives a new Independent identity; existing
  Independent identities are preserved.

## 12. GROUP rules

Canonical AUTO GROUP contains roof-owned generated members and excludes
Independent packages. ROTATE acceptance and rollback both finish with canonical
membership. An Independent member's retained roof provenance is informational
or recovery context; it does not confer AUTO ownership.

## 13. Explicit rebuild coexistence

After accepted AUTO-to-Independent ROTATE, `AK_ROOF_EDIT` → Apply regenerated
the complete AUTO roof, including the original AUTO slots. The rotated
Independent members stayed at their edited world positions and remained
outside AUTO automation. No suppression was used and GROUP was canonical. The
user confirmed: “AK_ROOF_EDIT → Použiť OK, odpojené zostali na mieste”.

## 14. CODE validation

These are the exact executed implementation results recorded by the ROTATE
work; this documentation checkpoint did not rerun builds or tests.

| Validation | Result |
|---|---|
| Focused `RoofOrdinaryRotateLifecycleTests` | 19/19 PASS |
| Affected Ordinary / Independent / Generated-member tests | 541/541 PASS |
| Final full Core suite | 7624/7624 PASS; 0 failures, 0 skipped |
| Final full WPF suite | 831/831 PASS; 0 failures, 0 skipped |
| Full Compatibility Gate, including Portable Gate | PASS |
| Standard AutoCAD 2027 Debug x64 build | PASS; 0 warnings, 0 errors |
| `git diff --check` during implementation | PASS |

The final build was produced at
`src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll`.
The implementation report records AutoCAD not running before the host build
gate and AutoCAD launched afterward with `C:\Users\Roman\Documents\3d.dwg`.
No build, runtime hash check, or test was run for this documentation-only
checkpoint. Product version and metadata schema were unchanged.

## 15. HOST validation matrix

Results in this table are the user's accepted manual AutoCAD Architecture 2027
HOST observations.

| Scenario | Result | Accepted observation |
|---|---|---|
| AUTO Plan2D ROTATE → YES | PASS | Standard detach confirmation; new Independent ID; Generated ownership cleared; Physical3D rebuilt; removed from AUTO GROUP; no forbidden legacy ownership state. Tested at 35°, 45°, and 90°. |
| AUTO Plan2D ROTATE → NO | PASS | Confirmation shown; NO selected; exact rollback visually confirmed; no detach or Independent identity; ElementId preserved; GROUP canonical. |
| Existing Independent ROTATE | PASS | No detach prompt; same Independent ID; v2 build state used; Physical3D rebuilt; outside AUTO GROUP. |
| Mixed Plan2D + paired Solid | PASS | Plan2D authority; one logical processing; no duplicate Solid; Physical3D rebuilt. |
| Physical3D-only ROTATE | PASS | Solid rejected and restored; Plan2D unchanged; no detach or ownership change. |
| Arbitrary angle / physical frame | PASS | At 35°, 45°, and 90°: Plan2D Z=0, exact section, horizontal width axis (`W.Z ≈ 0`), valid handedness, no arbitrary roll or scaling. |
| Multi-member ROTATE | PASS | Three logical members processed with one confirmation; two Independent IDs preserved; one AUTO detached with new ID; rebuilt bodies; canonical GROUP. |
| Physical Build State v2 | PASS | Persistent state used when present; legacy missing state migrated and persisted through common lazy migration; no ROTATE-specific persistence. |
| AUTO rebuild coexistence | PASS | Apply regenerated the full AUTO roof and original slots; rotated Independents stayed in place and outside AUTO automation; no suppression; canonical GROUP. |

## 16. Non-blocking diagnostics

For AUTO NO, the deferred refresh diagnostic can report
`deferredRegenExecuted=False / result=failed`. The user visually confirmed the
rollback as correct and accepted this diagnostic as non-blocking for ROTATE.
It is recorded transparently and does not change the accepted HOST PASS result.

## 17. Remaining separate scope

The following items were explicitly kept outside this ROTATE closure: general
warning-dialog styling, Settings UI polish, broader UNDO/REDO lifecycle work,
generic annotation-owner diagnostics for AttributeDefinition, and unrelated
N/Ú lifecycle parity. The native ROTATE Copy option was not part of the
in-place lifecycle HOST matrix. These items do not reopen the accepted ROTATE
lifecycle result.

## 18. Final status

**ORDINARY RAFTER ROTATE**
**CODE: PASS** — focused ROTATE 19/19, affected group 541/541, Core 7624/7624,
WPF 831/831, Full Gate PASS, Debug x64 build PASS.
**HOST: PASS ✅**
**Lifecycle defect: CLOSED ✅**

No commit, push, or tag was created.
