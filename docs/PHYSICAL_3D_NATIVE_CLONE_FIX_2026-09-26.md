# Physical 3D native clone ownership correction

Status: automated validation PASS; AutoCAD 2027 HOST acceptance pending.
Branch `main`, HEAD `d4d4fcff0cdfb3c548af40f17aac6ae00c6029f9`, version 0.23.0.
Existing WIP is preserved. No commit, push, stash, reset or clean.

## Confirmed cause and evidence boundary

The user's COPY HOST diagnostic establishes original owner `2912`, new owner `294D`,
native physical clones `294E–295A` with inherited owner `2912`, and a second canonical
physical set `2961–296D` owned by `294D`. Native display clones `2948–294C` also still
refer to `2912`. The stale set includes physical Faces and native 3D edge Lines plus
2D display entities, rather than only hidden face edges.

Physical child metadata stores the roof owner as an ASCII value; native cloning does
not translate it. EDIT's exact-owner cleanup correctly finds only the new owner's
canonical set and therefore cannot erase the inherited old-owner native clones.
The old whole-roof rebind did not consume native physical children. Its display and
physical rebuild also depended on generated timber being present, so a roof without
rafters could bypass that branch. Owner/elevation schemas are not the cause.

This COPY evidence does not establish MIRROR's native event sequence. The same exact
mapping policy handles a complete MIRROR clone if AutoCAD supplies it; MIRROR No and
Yes remain separate HOST acceptance cases. No geometry-based MIRROR inference is used.

## Single ownership policy

1. The existing document tracker captures a read-only pre-command source/owned-child
   snapshot for genuine COPY/MIRROR. It observes `BeginDeepCloneTranslation` and copies
   only `IsCloned` source/destination IDs; the callback opens no transaction and writes
   no DWG metadata. IDs live only in this document's command scope.
2. Full-clone eligibility requires the exact mapped owner plus the complete current
   display, generated timber, structural timber and AttachedManual set. It requires
   fresh, distinct destinations outside the pre-command source set. Source-only and
   partial member copies retain their existing lifecycle. Physical children omitted
   by native GROUP COPY are regenerated; any physical children actually mapped are
   consumed, including Faces and eave/hip/ridge Lines. Physical ownership remains
   independent of native group membership; the group collector is unchanged.
3. Maps are scoped to each roof before pairing. Identical definitions and repeated
   destinations do not require payload guessing. The old definition-equality pairing
   and the old-owner model-space display deletion scan were removed.
4. The existing whole-roof transaction erases only disposable display/physical child
   destinations proven by that mapping and snapshot. It validates their current
   metadata against the old/new owner and forbids the cloned owner itself as a target.
   Original IDs are never targets. The same transaction calls the existing display,
   physical and timber materializers, then the existing final group sync.
5. Display/physical generation runs for every accepted full clone, including roofs
   without rafters. AttachedManual and timber recipe/identity behavior is retained.
6. Any rebind failure returns without Commit. Mapped clone IDs are excluded from the
   subsequent resize/tamper pass, both on success (avoid a second generation) and on
   rollback (avoid independently writing a partially rebound clone). Consumed timber
   clones remain excluded from single-member COPY/MIRROR detach.
7. On success the canonical children refer to the new owner. Subsequent EDIT uses the
   existing exact-owner erase/recreate transaction, so no inherited physical set remains
   outside cleanup. No parallel physical generator or signature-based deletion exists.

Definition schema 5, physical elevation schema 2, physical child schema 1, decisions
1B+2A, native eave Lines, flattened plan, source grips, per-entity visibility and all
AK_ROOF_PURLINS WIP are unchanged. No shared layer visibility is changed. Native
U/UNDO/REDO/MREDO boundaries clear memory and bypass the clone observer/DB maintenance.

## What committed=false means

`ROOF_COPY_TRACE stage=summary ... committed=false` is emitted by the subsequent
`RoofGeneratedRafterCopyOwnershipRehydrationService`, including its no-write branch.
It is not the commit state of `RoofWholeRoofCopyRebindService`. A bare physical roof
can have no timber work for that service. The raw user trace line was not supplied
in this checkout, so its particular branch cannot be asserted from the boolean alone.

The corrected whole-roof path emits a separate DEBUG line:

```text
ROOF_NATIVE_CLONE_REBIND command=COPY oldOwner=<O> newOwner=<C> disposableClones=18 committed=true
```

This line is emitted only after its `transaction.Commit()`. For the fixture selected
with all physical and plan children, 18 means 13 native physical plus five plan clones.
A full GROUP copy that omitted physical children may report five disposable clones.
The final canonical physical role counts must still be 4/4/4/1. On a checked rebind
failure a separate line reports `committed=false rollback=true stage=<failure>`.
An `ROOF_WHOLE_COPY_REBIND ... result=ok` / MIRROR equivalent is a provisional per-pair
result before the enclosing transaction commits; use the new commit marker and final
manual audit for acceptance. Missing commit marker is a failure/inconclusive result,
never proof of success. The source-contract tests check this placement; HOST must
verify execution and native undo grouping.

## Changed files in this correction (prior WIP excluded)

- `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofWholeRoofCopyRebindService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofNativeCloneSnapshot.cs` (new)
- `src/AcKrovy.Core/Services/Roofs/RoofNativeCloneOwnershipRules.cs` (new)
- `src/AcKrovy.Core.Tests/RoofNativeCloneOwnershipRulesTests.cs` (new)
- `src/AcKrovy.Core.Tests/RoofNativeCloneOwnershipSourceContractTests.cs` (new)
- `src/AcKrovy.Core.Tests/RoofWholeRoofCopySourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofWholeRoofMirrorSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofGeneratedRafterCopyRehydrationSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofGeneratedCopySplitStretchSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofGripStretchSourcePrecedenceSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofRigidGroupTransformSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofGroupGripGeometrySnapshotSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofOriginalCopyResizeParitySourceContractTests.cs`
- `docs/PHYSICAL_3D_NATIVE_CLONE_FIX_2026-09-26.md` (this report)

Old tests banning any IdMapping hook in the tracker now keep that prohibition within
grip/resize/member services. New integration guards require the read-only, undo-filtered
whole-roof observer. Core tests exercise exact 13+5 disposable mapping, original/owner
target rejection, duplicate destinations, partial copies, independent COPY batches and
group copies without native physical children. These are not AutoCAD HOST execution tests.

## Automated validation

| Check | Result |
| --- | --- |
| Focused Core ownership/physical/purlin | PASS 594/594 |
| Focused WPF hip/purlin | PASS 463/463 |
| Full Core | PASS 6640/6640 |
| Full WPF | PASS 803/803 |
| Debug x64 warnings-as-errors | PASS, 0 warnings / 0 errors |
| Release x64 warnings-as-errors | PASS, 0 warnings / 0 errors |
| Portable Compatibility Gate | PASS |
| Full Compatibility Gate | PASS (includes full Core/WPF suites) |
| git diff --check | PASS |
| AutoCAD 2027 corrected HOST | NOT RUN — acceptance pending |

Validation commands:

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj -c Debug --no-restore -m:1 --filter "FullyQualifiedName~RoofNativeClone|FullyQualifiedName~RoofWholeRoof|FullyQualifiedName~Physical3D|FullyQualifiedName~RoofPhysical|FullyQualifiedName~RoofAbsolute|FullyQualifiedName~Purlin" --logger "trx;LogFileName=clone-focused-core.trx"
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj -c Debug -p:Platform=x64 --no-restore -m:1 --filter "FullyQualifiedName~HipRoof|FullyQualifiedName~Purlin" --logger "trx;LogFileName=clone-focused-wpf.trx"
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj -c Debug --no-restore -m:1 --logger "trx;LogFileName=clone-full-core.trx"
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj -c Debug -p:Platform=x64 --no-restore -m:1 --logger "trx;LogFileName=clone-full-wpf.trx"
dotnet build AcKrovy.sln -c Debug -p:Platform=x64 -warnaserror -m:1 --no-restore
dotnet build AcKrovy.sln -c Release -p:Platform=x64 -warnaserror -m:1 --no-restore
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
git branch --show-current
git rev-parse HEAD
git status --short --branch
```

The final focused WPF and full Core runs also used `--no-build --no-restore` against
the final compiled assemblies. The Full gate supplies the final full WPF execution.
TRX records are under the respective test projects' `TestResults` directories. Initial
build/test failures were fixed: netstandard2.0 collection compatibility, obsolete broad
IdMapping bans and assertions for the removed payload-pairing path. Final runs have
zero failed/skipped tests. Git reported line-ending conversion notices; a new trailing
whitespace at an existing unusual file terminator was restored before the final PASS.
AutoCAD and Core Console processes were absent before builds. No HOST probe was rerun.

Release verdict: automated PASS, **NOT READY FOR HOST-AFFECTING ACCEPTANCE** until the
HOST matrix below is observed in AutoCAD. The correction adds 18 automated cases;
native event timing, actual undo grouping, save/reopen and visual/grip results remain
explicit HOST checks.

## Existing drawings created with the broken build

No automatic geometric recovery is performed on load. After reload, inherited owner
metadata alone cannot distinguish a legitimate original child from a stale native
clone. Deleting everything referring to `2912` would destroy the original roof.

Proposed explicit recovery: work on a saved copy of the affected DWG, obtain the retained
native source→clone mapping/audit from that exact drawing, and review a manifest of
individual stale destination handles with source handle, old/new roof handles, native
type, role/structural ID and generation signature. Re-read every handle and validate
all metadata and drawing provenance before any mutation; reject missing/changed handles,
original or source-owner targets and ambiguous provenance. Erase only those proven
stale destinations in one explicit transaction, rebuild the affected new owner with
the existing generators, and audit both roofs afterward. This recovery command is a
proposal, not implemented or silently executed by this correction.

For the supplied COPY fixture the review candidates are `2948–294C` and `294E–295A`,
only if they still exist in the exact same DWG and the full retained diagnostic proves
each mapping. Never apply these handles to a different drawing, erase canonical
`2961–296D` by assumption, or infer stale objects from geometry/signature alone. Without
retained provenance, recreate the affected copy from the verified original in a fresh
test drawing rather than attempt cross-owner cleanup.

## AutoCAD 2027 HOST acceptance procedure

Environment: AutoCAD 2027 x64, main plugin Debug DLL
`src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll`, version 0.23.0,
HEAD above plus this uncommitted WIP. Use a fresh disposable DWG; do not load the failed
Core Console probe. Capture the DLL timestamp/hash, AutoCAD version, DWG, command log,
DBMOD and before/after audits. Restart AutoCAD to ensure the intended DLL is loaded.

Final Debug DLL SHA256:
`CE4A0B30B5D6716882E7888EBA7E3B3F472935669B6C2FCFE674616E9B0CD0B6`.
Host-local file timestamp: 2026-09-26 20:53:53; length 6698496 bytes.

1. Create a closed 10000×6000 mm source rectangle at drawing Z=0. Create a supported
   symmetric physical hip roof at 30°, eave WCS Z=3000, visibility Both. Run
   `AK_ROOF_3D_TRACE` once to enable and `AK_ROOF_3D_AUDIT`. Record original owner O and
   every original child handle/signature. Expect four Faces, four EaveEdges, four
   HipEdges, one RidgeEdge, five flattened plan Lines (four hips plus ridge), and one
   original source polyline as the sole 2D outer boundary. Ridge Z≈4732.050808, length4000.
2. COPY the complete roof with a non-overlapping displacement. Include source, plan and
   all physical children (window selection); also repeat using the existing assembly
   GROUP selection. Run manual audit after COPY finishes. Expect one new source owner C,
   one canonical set with role counts 4/4/4/1 owned by C, five plan children owned by C,
   exactly one signature per physical roof, and the post-Commit marker. Each disposable
   native destination in the MAP trace must be erased. Original O must still have its
   exact original handles, owner XData, coordinates, visibility and 30° signature.
3. `AK_ROOF_EDIT` only C to 45°, audit. Expect eaves Z3000, ridge Z6000, length4000,
   counts4/4/4/1, no old 30° native or canonical child remaining for C, no changes to O.
   Repeat C 45→30→45, auditing each step. Child handles may change; all must reference C,
   keys must be unique and there must be one current physical signature. Audit's
   after-create snapshot is provisional; use the final manual audit for counts.
4. Repeat steps2–3 with MIRROR Erase source=No across a displaced oblique axis. Capture
   MIRROR's actual MAP/native CommandEnded sequence; expect mirrored owner M and independent
   canonical data, original O unchanged. Separately exercise Erase source=Yes on a fresh
   duplicate fixture; confirm the actual mapped-clone vs in-place HOST path and absence
   of orphan original children. Do not infer this result from COPY or from test names.
5. On C/M exercise unlocked source STRETCH and direct GRIP_STRETCH. For a valid rectangle
   expect rebuilt physical model and flat plan without duplicates. Move one corner to an
   unsupported quadrilateral: supported 2D remains, physical is suspended/removed,
   Physical3DEnabled remains true and exactly one localized warning appears. Restore a
   rectangle: physical faces/eaves/hips/ridge recover automatically with correct counts.
   Also check locked whole-roof rigid STRETCH/group grip translation as previously accepted.
6. For O/C/M separately set Plan2D, Both and Model3D. Plan2D permits direct source native
   grips and has no flattened duplicate outer perimeter. Both/Model3D show all native
   eaves at Z3000. `AK_ROOF_SELECT_SOURCE` resolves each owner explicitly in overlapping
   Both geometry. Changing one roof's mode must not alter other roofs/shared layers.
7. On fresh unlocked fixtures ERASE only the cloned owner. Audit: all its physical/display
   children removed and original untouched. Separately confirm existing locked-owner ERASE
   recovery behavior. U/UNDO/REDO/MREDO after COPY/MIRROR, EDIT, stretch and ERASE must replay
   native history consistently; no plugin reconcile/clone maintenance writes at those
   boundaries. Run manual audit only after the boundary finishes; that explicit read-only
   command is separate from undo processing. Record handle/count/state/DBMOD results.
8. Save, close and reopen. Audit ownership, counts, signatures, visibility and source grips;
   then EDIT a clone once more. Repeat with two identical originals copied in the same
   command, repeated COPY destinations, partial source/member copies, and a fixture with
   existing AK_ROOF_PURLINS WIP. Partial timber copies retain prior semantics; purlin
   geometry/metadata/annotations and existing purlin regressions must remain unchanged.

At every accepted clone/EDIT boundary collect `COUNT ownerRole=<owner>:Face count=4`,
`EaveEdge count=4`, `HipEdge count=4`, `RidgeEdge count=1`; inspect all five DISPLAY
records for that owner. Compare source-owner records and original child handle set,
not only visual appearance. Record owner-scoped structural IDs, signatures, raw child
XData, per-entity visibility and native group membership; group membership does not
substitute for roof-owner metadata. No duplicate keys, old mapped destinations,
cross-owner mutations, stale 30° geometry or orphan physical/display objects are allowed.

HOST result: NOT RUN for this correction. COPY supplied defect evidence: FAIL.
MIRROR correction/reload/undo/visual acceptance: NOT RUN. STOP for HOST acceptance.
