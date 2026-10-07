# Locked source ERASE: physical 3D lifecycle correction — 2026-09-26

Implementation and automated validation are complete. AutoCAD 2027 HOST acceptance is pending.
No commit, push, reset, stash or clean was performed. Existing WIP, including AK_ROOF_PURLINS,
was retained. This report describes this task's changes, not the entire existing working diff.

## Evidence and cause

Supplied HOST evidence establishes that source owner 293C was restored after a rejected
locked-source ERASE, with the same ObjectId/handle and five plan children, but its final
physical count was zero. Original owner 2912 retained counts 4/4/4/1. The evidence does
not contain intermediate physical counts, so it cannot establish the first native event
at which the physical children disappeared. COPY/MIRROR and repeated EDIT passed in the
preceding HOST test; the new protected-ERASE paths have not been retested with this build.

The current source contains a concrete destructive path: `ApplySourceEraseTampers` restores
the source and rebuilds its display, then `RefreshTimberElements` processes the historical
`erasedSourceHandles` and unconditionally calls `EraseOwned`. That later cleanup does not
check whether repair has already made the source live again. Thus a successful source
repair can be followed by physical deletion for the same owner. The prior repair also
does not verify or restore the physical model. These are confirmed code defects consistent
with the supplied HOST result; exact native event timing still requires the trace below.

## Correction

- Cleanup resolves the exact source handle and confirms roof provenance and current
  erased state. A live restored source is preserved; unresolved/non-roof handles fail
  closed. An intentional ERASE of a source that remains erased still removes its owned 3D.
- Locked repair uses one transaction for un-erasing the original source, rebuilding its
  supported plan, preserving/restoring its physical set, and synchronizing/validating the
  existing assembly group. Failure returns without committing the repair transaction.
- Complete physical metadata and native entity types preserve existing physical handles.
  Missing/incomplete/stale sets use the existing shared materialization/reconcile service.
  No parallel generator, layer-wide operation, geometric adoption or geometric deletion
  was added. Repair is scoped by the exact owner handle.
- Completeness checks expected role/structural-ID keys, owner, schema, generation signature
  and exact child count from the actual Core model. For the 10000×6000 hip fixture the
  required counts are 4 Faces, 4 EaveEdges, 4 HipEdges, 1 RidgeEdge. Square roofs continue
  using their actual model's expected set rather than a hardcoded 13-child requirement.
- Existing per-owner visibility is applied to rebuilt display children even when complete
  physical children are preserved. Definition schema 5, elevation-store schema 2, physical
  metadata schema 1 and the 1B+2A WCS elevation contract are unchanged.
- Existing UNDO/REDO command guards remain in place. Added automatic diagnostics do not
  open database transactions during UNDO/REDO. Opt-in diagnostics read only, never commit
  or mutate the drawing; explicit manual audit is separate from native undo processing.

## Separate diagnostics

`ROOF_COPY_INVARIANT result=fail` with no generated rafters had a separate diagnostic cause:
the generic uniqueness helper returns false for an empty set. The DEBUG invariant now
accepts uniqueness when both expected and actual rafter sets are empty. Rebind success,
counts, missing keys and duplicates remain required. Nonempty mismatches still fail.
This change does not generate rafters or establish physical-3D correctness.

The old duplicate group listing alone cannot prove whether it represents repeated audit
output, dictionary aliases, or multiple distinct groups. New `OWNER_GROUP` records include
phase, dictionary name, group handle, `firstListing`, total/unique members and source slots.
Distinct group handles in one phase establish distinct groups; repeated rows across phases
do not. Repair validates the canonical existing display/structural assembly membership;
no speculative group deletion or new physical-group policy was introduced.

## Exact files changed in this task

| File | Change |
| --- | --- |
| `src/AcKrovy.Core/Services/Roofs/RoofPhysical3DSetRules.cs` | New CAD-neutral cleanup-state/completeness rules |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DLifecycleService.cs` | Current source-state cleanup; complete-set preservation/shared repair |
| `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs` | Guarded cleanup call; final opt-in maintenance snapshot |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofLiveResizeService.cs` | Atomic source/display/physical/group repair and committed snapshot |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs` | ERASE observation, owner counts including zero, group identity diagnostics |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofWholeRoofCopyRebindService.cs` | Post-commit COPY/MIRROR owner counts only |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedRafterCopyOwnershipRehydrationService.cs` | Empty expected/actual rafter invariant diagnostic correction |
| `src/AcKrovy.Core.Tests/RoofPhysical3DLockedSourceEraseTests.cs` | New model/state regression cases |
| `src/AcKrovy.Core.Tests/RoofPhysical3DLockedSourceEraseSourceContractTests.cs` | New adapter source-contract regressions |
| `src/AcKrovy.Core.Tests/RoofPhysical3DHostDiagnosticsSourceContractTests.cs` | ERASE trace contract update |
| `src/AcKrovy.Core.Tests/RoofPhysical3DHostSourceContractTests.cs` | Guarded cleanup contract update |
| `docs/PHYSICAL_3D_LOCKED_ERASE_FIX_2026-09-26.md` | This handoff and HOST protocol |

## Automated validation

| Check | Result |
| --- | --- |
| Branch / HEAD | main / d4d4fcff0cdfb3c548af40f17aac6ae00c6029f9 |
| Upstream | origin/main; no ahead/behind shown |
| Working tree before/after | Dirty existing WIP; retained, no commit |
| Focused Core roof/physical/clone/erase/purlin regressions | PASS 638/638 |
| New locked-source model and source-contract cases | PASS 16/16 |
| Focused WPF hip/purlin regressions | PASS 463/463 |
| Full Core | PASS 6656/6656 |
| Full WPF | PASS 803/803 |
| Debug x64 / Release x64 | PASS; 0 warnings, 0 errors |
| Portable Compatibility Gate | PASS; restore/build/tests/architecture checks |
| Full Compatibility Gate | PASS; Core6656/WPF803 and solution build, 0 warnings/errors |
| CAD API leakage | Portable gate architecture rules PASS |
| Localization | Existing suites PASS; no localization edits in this task |
| Schema/version impact | NONE |
| Git diff check | PASS; Git line-ending conversion notices only |
| AutoCAD HOST validation | NOT RUN for this correction |

Commands executed for final verification (AutoCAD/acCoreConsole were not running):

```powershell
dotnet build AcKrovy.sln -c Debug -p:Platform=x64 -warnaserror -m:1 --no-restore
dotnet build AcKrovy.sln -c Release -p:Platform=x64 -warnaserror -m:1 --no-restore
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj -c Debug --no-build --no-restore --logger "trx;LogFileName=locked-erase-full-core.trx"
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj -c Debug -p:Platform=x64 --no-build --no-restore --logger "trx;LogFileName=locked-erase-full-wpf.trx"
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj -c Debug --no-build --no-restore --filter "FullyQualifiedName~RoofPhysical3DLockedSourceErase" --logger "trx;LogFileName=locked-erase-new-regressions.trx"
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj -c Debug -p:Platform=x64 --no-build --no-restore --filter "FullyQualifiedName~AutomaticPurlin|FullyQualifiedName~HipRoof" --logger "trx;LogFileName=locked-erase-focused-wpf.trx"
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
```

The broader focused Core run is recorded in `src/AcKrovy.Core.Tests/TestResults/locked-erase-focused-core.trx`.
Core COPY/MIRROR model cases exercise shared ownership/count/elevation policy; they do not
execute native AutoCAD commands. Source-contract tests are not HOST ordering evidence.

## AutoCAD 2027 HOST acceptance procedure

Restart AutoCAD and NETLOAD the final Debug x64 DLL:
`src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll`.
Use Debug because the audit/trace commands are DEBUG-only. Enable command logging and
`AK_ROOF_3D_TRACE` once (check `TRACE enabled=True`). Do not load an older DLL in the same
process. Final DLL hash is recorded after the gates below.

Final Debug DLL SHA256: `F05B8F26096B0B11305F1E8ACFD1B8157223A5667043BE0D25ABF2AAF5A0DB05`.
Host-local timestamp: 2026-09-26 23:33:30; length6706688 bytes.

1. In a fresh drawing create a 10000×6000 mm source at drawing Z=0, physical symmetric
   hip 30°, eave WCS Z=3000, Both mode. Audit with `AK_ROOF_3D_AUDIT`. Record original
   owner O, source ObjectId/handle and all physical child handles/signatures. Expect
   4/4/4/1 physical children and five flat plan Lines, with source as sole outer boundary.
   Ridge WCS Z≈4732.050808 and length4000.
2. Whole-roof COPY to a separate location, including source, plan and physical children.
   After command completion audit copied owner C. Require 4/4/4/1, total13, unique keys,
   one physical signature, five display children owned by C. Record
   `native-clone-rebind-committed:COPY` and `CommandEnded-after-maintenance:COPY` counts.
   O must retain its original handles, coordinates, signature and visibility.
3. Lock C using `AK_ROOF_LOCK`. Run `AK_ROOF_SELECT_SOURCE` on C and ERASE only its source polyline. Audit
   after command completion. The source must be restored with the same ObjectId/handle;
   source/display/group/physical must be complete. Require 4/4/4/1 and five plan Lines.
   Compare the phase sequence below to establish the actual loss/recovery stage.
4. EDIT only C 30→45→30 and audit after each command. Require one current set13, no old
   signatures/orphans and no changes to O. Eaves stay Z3000; ridge Z6000 at45° and
   ≈4732.050808 at30°, length4000. If repair preserved a complete set, its pre-EDIT
   physical handles should match the pre-ERASE handles; replacement EDIT may change them.
5. Repeat steps2–4 using MIRROR with Erase source=No on a fresh fixture. Capture MIRROR's
   actual native/maintenance sequence independently. The COPY evidence is not proof of
   MIRROR ERASE recovery. Repeat using the existing group-selection workflow as well.
6. Repeat protected source ERASE in Plan2D, Both and Model3D. Counts remain13 in every mode;
   visibility follows only that roof's mode (physical hidden in Plan2D, visible in Both/
   Model3D). Both/Model3D include all four native 3D eave Lines. Verify direct source native
   grips in Plan2D and explicit source selection in overlapping Both geometry. No shared
   layer toggles, duplicate flat perimeter, or unrelated-roof visibility change is allowed.
7. On separate fixtures exercise U/UNDO/REDO/MREDO after COPY/MIRROR, protected source
   ERASE and EDIT. Check counts, original identity, mode and DBMOD/REDO availability at
   the completed boundaries. No plugin reconcile/repair writes may occur during native
   undo/redo. Run the explicit read-only audit after each boundary has finished.
8. After recovery, exercise valid rectangular STRETCH/GRIP_STRETCH on C/M and audit the
   independent regenerated set and flat plan. Exercise rectangle→unsupported quadrilateral
   →rectangle: suspend physical geometry with one warning, retain Physical3DEnabled, then
   restore the complete supported model. Verify intentional unlocked source ERASE still removes only that owner's generated
   children. Repeat with an existing AK_ROOF_PURLINS fixture; preserve timber metadata,
   annotations and group members. Save/reopen and audit ownership, counts, visibility
   and source grips; repeat EDIT. Do not repair legacy entities through geometric guesses.

Trace phases to compare for C/M:

```text
CommandWillStart:ERASE
CommandEnded-before-maintenance:ERASE
locked-source-repair-before-unerase
locked-source-repair-after-unerase
locked-source-physical-ensure-before
locked-source-physical-ensure-preserved OR locked-source-physical-ensure-restored:provisional
locked-source-repair-after-display-physical-group:provisional
locked-source-repair-committed
source-cleanup-before:resolved=True:isRoof=True:sourceErased=False:erase=False
source-cleanup-after:preserved
CommandEnded-after-maintenance:ERASE
manual
```

Each `OWNER_COUNTS` includes owner, sourceLive, role counts, total, display, uniqueKeys,
signatures and physical handles; zero is explicit. If native end already lacks physical
children, shared repair must restore the set. If native end retains13, repair must
preserve them and subsequent cleanup must not erase them. If COPY completion already
shows zero, that is an earlier failure and must not be attributed to ERASE.

Use committed/final/manual snapshots as acceptance evidence; provisional snapshots alone
are insufficient. Inspect `OWNER_GROUP` by group ID within one phase, expected unique
membership and exactly one source slot. On the no-rafter/no-purlin fixture the canonical
display group has six members (source+five plan Lines); do not mistake physical children
outside that group for missing physical ownership. A distinct unexpected group is a
separate HOST finding, not proof of the physical cleanup cause.

Release verdict: NOT READY FOR HOST-AFFECTING CHANGE until HOST acceptance. STOP for HOST.
