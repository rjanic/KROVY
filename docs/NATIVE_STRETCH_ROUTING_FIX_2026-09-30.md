# Native STRETCH routing — 2026-09-30

## Root cause and routing

The supplied HOST evidence proves that an ordinary Generated Plan2D endpoint edit
entered `ROOF_HIP_LIVE_RESIZE` / `apply-resizes` instead of semantic member acceptance.
It does not prove the exact raw `ObjectModified` notification set. The source audit
identifies the incorrect promotion: `RoofLiveResizeService.Inspect` classified every
notified persisted source Polyline with `ClassifyOwner(...,
treatHipDisplayDriftAsResize: true)`. An otherwise `RigidEquivalent` Hip source is
promoted to `SupportedResize` when its display or generated member coverage differs.
An edited child endpoint changes that coverage even when the source is unchanged.
`Process` runs `ApplyResizes` before `RoofGeneratedMemberManualEditService.ProcessOwners`
and excludes resize owners from the member branch, so the edit disappears before
an override can be persisted.

Before: a source notification plus child/display mismatch could trigger full resize.
After: for STRETCH and GRIP_STRETCH, source lifecycle eligibility first compares the
live source with the existing command snapshot (raw vertices, closed flag, elevation,
normal, curves and planarity). An unchanged source notification cannot promote
child/display drift to resize. A changed source retains the existing classification,
resize/recovery, regeneration and whole-roof grip handling.

Generated, display and physical candidates also expose their owner for this source
comparison. The manual service and collateral physical exclusion use the same
`HasSourceGeometryChanged` decision, preventing a later raw notification check from
discarding an otherwise valid member edit. Other commands and unavailable snapshots
retain their previous notification policy; no new snapshot, reactor, selection filter,
schema, persistence framework or undo mechanism was introduced.

The accepted member path still uses `TryClassifyAcceptedMemberEdit`,
`TryClassifyCollinearEndpointEdit` and `ComposeEndpointOffsets`. Existing targeted
recalculation synchronizes final `ReservedElementId`; the logical MemberKey is stable.
The accepted Plan2D lines are normalized to Z=0 and drive semantic overrides.
`TryReconcileModifiedMembersInTransaction` now receives actual accepted Plan2D IDs,
rather than rebuilding bodies for unchanged selected lines. Only those keys and
collateral ordinary Physical3D keys are replaced. Solid geometry is never edit authority.
Pure no-op native STRETCH notifications are filtered using the existing line snapshot
before manual processing; unchanged lines do not drive overrides or physical rebuilds.

The preceding mixed-selection reconciliation remains in place: matching and unrelated
derived bodies are rebuilt from semantic state; Physical3D-only STRETCH retains recovery
and direct-edit rejection. Source changes take priority, as before.

## Files changed by this routing follow-up

Existing WIP from the earlier heavy/mixed-selection fixes remains unstaged and intact.
This follow-up edits exactly these existing files:

- `src/AcKrovy.AutoCAD/Infrastructure/RoofLiveResizeService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedMemberManualEditService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryRafterSolidMaterializationService.cs`
- `src/AcKrovy.Core/Services/Roofs/RoofUnsupportedStretchRecoveryRules.cs`
- `src/AcKrovy.Core.Tests/HipRoofLiveResizeSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofGeneratedMemberLockedTamperSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofGeneratedSnapshotCaptureSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofLockedShapeChangeRejectionSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofMixedPhysicalStretchTests.cs` (already untracked WIP)

New files:

- `src/AcKrovy.Core.Tests/RoofNativeStretchRoutingTests.cs`
- `docs/NATIVE_STRETCH_ROUTING_FIX_2026-09-30.md`

No UI, localization, gate script or physical geometry formula changes.

## Tests and commands

The new class adds 13 cases covering source geometry authority, actual source resize,
persisted endpoint composition/codec/replay, same key and Plan2D Z=0, target-only physical
replacement, unchanged unrelated model identity, unique keys/canonical membership,
no-op, cancellation guards, and semantic before/after payload replay for undo/redo.
Adapter source contracts guard routing and accepted-ID boundaries. They do not execute
native crossing selection, AutoCAD transactions or native undo/redo.

Commands executed from repository root, in order of relevant validation:

```powershell
$focused = 'FullyQualifiedName~RoofNativeStretchRoutingTests|FullyQualifiedName~RoofMixedPhysicalStretchTests|FullyQualifiedName~HipRoofLiveResizeSourceContractTests|FullyQualifiedName~RoofGeneratedMemberLockedTamper|FullyQualifiedName~RoofOrdinaryPhysicalStretchTests|FullyQualifiedName~RoofDerivedMove|FullyQualifiedName~RoofLockedShapeChangeRejectionSourceContractTests|FullyQualifiedName~RoofGeneratedSnapshotCaptureSourceContractTests'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter $focused --logger 'console;verbosity=minimal'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --logger 'console;verbosity=minimal'
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj -p:Platform=x64 --no-restore -warnaserror -m:1 -nr:false --logger 'console;verbosity=minimal'
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj -c Debug -p:Platform=x64 --no-restore -warnaserror -m:1 -nr:false
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj -c Release -p:Platform=x64 --no-restore -warnaserror -m:1 -nr:false
$env:DOTNET_PROCESSOR_COUNT='2'
$env:MSBUILDDISABLENODEREUSE='1'
./scripts/compatibility-gate.ps1 -Portable
./scripts/compatibility-gate.ps1 -Full
git -c core.safecrlf=false diff --check
```

`Get-Process -Name 'acad*'` verified AutoCAD was closed before adapter/WPF/full builds.
Gate output is retained in `%TEMP%/acad-stretch-routing-{portable,full}-20260930.log`.
The scripts additionally perform restore, architecture/manifest checks, and full
solution build/tests. A separate `rg` scan found no CAD vendor dependencies in portable
source/project files. No separate AutoCAD test project exists; adapter source contracts
are part of Core and adapter/WPF compilation is verified separately.

Initial test compilation exposed two nullable fixture warnings, corrected immediately.
The first complete Core run found two obsolete text assertions for raw notification
routing; both were updated to require source geometry authority. Final results supersede
these development failures.

| Check | Result |
| --- | --- |
| Focused | PASS — 89/89 |
| Core | PASS — 6858/6858 |
| WPF | PASS — 806/806 |
| Debug x64 | PASS — 0 warnings/errors |
| Release x64 | PASS — 0 warnings/errors |
| Portable restore/build/tests/architecture | PASS |
| Full Gate | PASS — solution build; Core 6858/6858 and WPF 806/806 |
| CAD dependency leakage | PASS |
| Localization/resource changes | Not applicable |
| Diff check | PASS — final check, including new file whitespace scan |
| HOST | NOT RUN |

Branch `main`, HEAD `40ddce742d8ba6ed4c3eda7f52fe5cc413840930`;
local origin/main ahead/behind 0/0, no fetch. Working tree was already dirty;
index remains empty. No commit, push or tag.

## Structural metadata-diff

The earlier read-only DEBUG `ROOF_STRUCT_METADATA_DIFF` diagnostic remains available.
Equal endpoints do not establish equality of the full `TimberElementData` and
`RoofStructuralGeneratedData` records. `geometryChanged || metadataChanged` intentionally
opens the reference for write on either difference; `update-geometry` is also emitted for
a metadata-only update. The supplied evidence lacks before/desired metadata values,
so redundancy or causation of historic restore-write failure is not established.
No speculative write-policy optimization was made; see the preceding mixed-selection
report for the exact diagnostic fields.

## HOST retest and remaining risks

HOST verdict remains **NOT RUN**. Use the final Debug x64 DLL in AutoCAD and the same DWG
that reproduced the failure; record AutoCAD version, drawing, build, DBMOD, logs and IDs.

1. On an unlocked generated Hip roof with Physical3D, use normal native STRETCH with a
   right-to-left crossing window through the endpoint of one ordinary Plan2D rafter.
   Allow the natural overlapping derived/group selection; do not manually isolate its axis.
2. Verify the source vertices are unchanged, the target endpoint stays edited at Z=0,
   a semantic ManualOverride exists under the same MemberKey, and its matching physical
   key has exactly one rebuilt body. Unchanged selected Plan2D lines and unrelated rafter
   identities/geometry must persist. No whole-roof resize commit or direct-3D rejection
   should occur for this case; capture member acceptance/reconcile logs.
3. Run AUDIT immediately. Capture canonical GROUP membership, unique Physical3D keys,
   absence of missing/duplicate/foreign entities, and any errors.
4. Test U/REDO, cancel and no-op; semantic Plan2D and physical state must move together,
   with no maintenance writes at undo/redo boundaries. Portable payload replay does not
   replace this native check.
5. Verify source Polyline STRETCH and whole-roof GRIP_STRETCH still use source lifecycle,
   ordinary endpoint GRIP_STRETCH still accepts, Physical3D-only STRETCH/MOVE/ERASE still
   recover, 2D MOVE/TRIM/ERASE still synchronize, and whole-roof MIRROR remains coherent.
   Keep Hip/Valley and cross-owner collateral cases in the HOST regression matrix.
6. If structural metadata-diff recurs, compare both JSON records from
   `ROOF_STRUCT_METADATA_DIFF` before deciding whether a write is redundant.

Snapshot unavailability retains the previous fallback policy. Exact native callback
ordering, crossing-selection behavior, grouped undo and DB persistence still require
this HOST run. There are no hardcoded entity handles or rafter/group counts.
