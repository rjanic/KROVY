# Hip / Valley structural native-edit foundation — 2026-10-01

Baseline: `21a92093e4e99edbf5fd3e3b4f9e1495ab1eabd5` (HEAD unchanged).
Scope: Plan2D MOVE / ERASE, direct StructuralRafterSolid MOVE / ERASE / STRETCH /
GRIP_STRETCH, structural first claim, and canonical GROUP maintenance. Full
COPY / MIRROR / BREAK semantics remain deferred pending HOST A+B+C.

## Evidence and common defect

The user supplied actual HOST evidence after the diagnostic audit:

- Unlocked `Hip|1|4` Plan2D MOVE produced native geometry changes, followed by
  `structural-hip-valley-before-unlocked-accept` restoration and ordinary
  `changed=0`. Structural mutations lacked a semantic claim before legacy recovery.
- BREAK fragments entered `break-fragment` fallback before semantic ownership.
  This foundation gives the router first opportunity but deliberately leaves
  fragment semantics unclaimed.
- Direct StructuralRafterSolid MOVE retained the same moved body after maintenance.
  The existing direct-MOVE helper restores OrdinaryRafterSolid roles only.
- Structural erase/recovery left one surplus ObjectId slot in the canonical GROUP.
  EnsureGroup previously removed duplicate slots by ObjectId rather than exact
  membership index, and provisional membership alone could miss native reattachment.

Raw ObjectModified / ObjectErased collection remains in the existing tracker.
Erased entities use RoofDisplayErasePreCommandMapService captured while entities
were live; its new optional StructuralData field retains the exact semantic key.
The router does not rely on reading XData from an already erased entity.

## Production ordering

1. Existing whole-roof COPY / MIRROR ownership retains first priority.
2. RoofStructuralNativeEditService receives native structural candidates under
   the existing collector suppression scopes.
3. It verifies that the source footprint, elevation and normal match the native
   pre-command snapshot, and reads existing boundary identity / roof definition.
   Source transforms and source resize remain owned by their existing workflow.
4. Plan2D MOVE / ERASE resolves owner + LogicalKey and the exact pre-command
   source binding. Appended clones cannot be mistaken for their original source.
5. Unlocked pure XY MOVE composes placement; ERASE persists suppression on the
   owner. Locked MOVE / ERASE restores the same reference entity and geometry.
6. The shared structural physical materializer rebuilds from canonical Core
   geometry plus accepted semantic placement, then synchronizes GROUP.
7. Only committed claims are removed from generic candidates and marked in the
   command snapshot. Mixed ordinary recovery skips these Plan2D sources and
   their annotations, while continuing to protect unclaimed mutations.
8. A closed-transaction GROUP verification performs at most two synchronous
   correction passes within the existing native command scope.

All ten requested command families reach the first-claim router. Accepted
structural Plan2D semantics in this slice are MOVE / ERASE only. Other structural
Plan2D mutations remain explicitly unclaimed and can use legacy fallback.

## Semantic persistence and geometry limits

`RoofStructuralEditState`, schema 1, is stored as a dedicated owner extension-
dictionary Xrecord `AK_ROOF_STRUCTURAL_EDITS`. It contains LogicalKey, OffsetXmm,
OffsetYmm and Suppressed. It contains no Handle, ObjectId or ElementId. Missing
state means canonical; malformed state fails closed rather than being reset.
Reads do not create a dictionary, repair provenance or modify the drawing.
The state and derived entities use ordinary DWG transactions and native undo
records; no sidecar file or deferred command writes are introduced.

MOVE is explicitly planar placement of the retained canonical source-fold member:
fresh canonical profile, construction prisms, faces and cut planes receive the
same XY translation. Z, dimensions, source face indices, slopes, height mode,
clip normals and lower-end mode remain unchanged. This is not a refit to different
roof faces and not Solid3d readback. Per-member Z, rotation and endpoint edits are
not accepted by the foundation. The canonical Hip physical builder and normative
roof elevation contract are unchanged.

Suppression removes the corresponding structural physical body and source
annotations. The suppression survives deletion of the reference and future
structural Plan regeneration. An absent topology key retains its exact override;
there is no nearest-edge remapping. Ordinary member geometry and canonical
ordinary trim context remain unchanged by this slice.

Native member reconciliation preserves existing structural Plan2D metadata and
item numbers. It does not invoke whole-set Plan materialization, which could
renumber untouched signature groups before mixed ordinary snapshot recovery.
Only affected live Plan references get annotation upserts.

Direct StructuralRafterSolid MOVE / ERASE / STRETCH / GRIP_STRETCH always takes the
derived recovery action in Locked and Unlocked states. Old structural bodies are
detached from GROUP before replacement. Geometry of edited host bodies is never
an authority. The committed verifier checks unique Plan keys, Plan2D Z=0, unique
physical keys, exact live Plan/physical key equivalence, and absence of orphan
structural bodies (or zero bodies when Physical3D is disabled).

## GROUP and undo boundaries

EnsureGroup removes surplus / foreign slots with `RemoveAt(index)` in descending
order, preserving the first expected occurrence. It re-reads membership before
append, appends missing members once and enforces the canonical multiset in
Release as well as Debug. It never clears the canonical group.

Structural completion verifies membership after the owning transaction is closed,
so a native unerase reattachment cannot be hidden by provisional counts. Expected
membership is collected from the drawing; no HOST count is hardcoded. The existing
ordinary source and physical finalization workflows remain intact.

U / UNDO / REDO / MREDO return before document locks or DB access in the new router
and finalizer. Command claims remain in the existing in-memory command snapshot
and are cleared at its existing lifecycle boundaries. Core tests prove semantic
snapshot roundtrips and replay; real native DWG undo ordering remains HOST work.

## Changed files

New production files:

- `src/AcKrovy.Core/Models/Roofs/RoofStructuralEditState.cs`
- `src/AcKrovy.Core/Services/Roofs/RoofStructuralEditRules.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralEditStateStore.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs`

Production integration:

- `LiveGeometrySynchronizationService.cs`: first-claim ordering and candidate filtering.
- `RoofDisplayErasePreCommandMapService.cs`: pre-erase structural semantic metadata.
- `RoofUnsupportedStretchRecoverySnapshotService.cs`: command-local committed claims.
- `RoofUnsupportedStretchRecoveryService.cs`: skip claimed sources / annotations in
  snapshot restoration and probes; preserve all snapshot handles for duplicate checks.
- `RoofGeneratedMemberManualEditService.cs`: explicit unclaimed-only fallback.
- `RoofAutomaticStructuralRafterMaterializationService.cs`: semantic Plan replay.
- `RoofStructuralRafterSolidMaterializationService.cs`: suppression, semantic placement,
  canonical fresh construction and detach-before-replacement.
- `RoofDisplayGroupService.cs`: exact-slot multiset synchronization and verification.
- `RoofAssemblyGroupSyncService.cs`: synchronous closed-transaction structural finalization.

Tests:

- New `RoofStructuralFoundationTests.cs` and
  `RoofStructuralFoundationSourceContractTests.cs`.
- `RoofStructuralRafterPolyhedronServiceTests.cs`: real Hip / Valley canonical builder
  placement regressions for both lower-end modes and repeated rebuild.
- `RoofStructuralGeneratedLockParityTests.cs`, `RoofMixedPhysicalStretchTests.cs`:
  updated adapter wiring expectations for unclaimed structural restoration.
- `RoofCanonicalGroupPersistenceSourceContractTests.cs`,
  `RoofDisplayGroupSourceContractTests.cs`, `RoofGroupCopyOwnershipSourceContractTests.cs`,
  `RoofGroupRehydrationSourceContractTests.cs`: exact-slot GROUP synchronization guards.

Retained preceding audit WIP: `RoofPhysical3DHostDiagnostics.cs`,
`RoofPhysical3DHostDiagnosticsSourceContractTests.cs` and
`HIP_VALLEY_NATIVE_LIFECYCLE_AUDIT_2026-10-01.md`. Diagnostics are DEBUG-only and
read-only. Unrelated WIP is preserved. No commit, push, tag or release.

## Validation

Final CODE-side verdict: **PASS**.

| Check | Final result |
| --- | --- |
| Focused structural foundation + ordinary lifecycle regressions | 708 PASS, 0 failed, 0 skipped |
| Full Core | 7,038 PASS, 0 failed, 0 skipped |
| Full WPF | 806 PASS, 0 failed, 0 skipped |
| Debug x64 solution, warnings as errors | PASS, 0 warnings, 0 errors |
| Release x64 solution, warnings as errors | PASS, 0 warnings, 0 errors |
| Portable Compatibility Gate | PASS |
| Full Compatibility Gate | PASS |
| `git diff --check` | PASS |

Detailed command logs are in ignored `artifacts/hip-valley-audit-validation/`.
The existing HOST workflow closed AutoCAD before assembly builds.

Commands:

```powershell
pwsh -NoProfile -File scripts/acad-host-workflow.ps1 -Configuration Debug -SkipTests -NoLaunch
pwsh -NoProfile -File artifacts/hip-valley-audit-validation/validate.ps1
```

The validation helper runs these commands sequentially (plus the focused filter):

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj --no-restore -p:Platform=x64 -warnaserror -m:1 -nr:false
dotnet build AcKrovy.sln --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
dotnet build AcKrovy.sln --no-restore -c Release -p:Platform=x64 -warnaserror -m:1 -nr:false
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
```

Development failures corrected before final validation: netstandard2.0 finite-number
API compatibility; source-contract marker renamed with the unclaimed guard; removal
of a redundant boundary-identity ensure from native member routing. The failed runs
are not counted as PASS.

An automatic approval review rejected a proposed broad line-ending rewrite based
on `git diff --name-only`, because it could affect unrelated WIP. That rewrite
was omitted. Validation ran separately and completed successfully; no approval
request remains pending.

## HOST closure and remaining risks

Existing workflow launch completed:

```powershell
pwsh -NoProfile -File scripts/acad-host-workflow.ps1 -Configuration Debug -SkipTests
```

The launch build passed with 0 warnings / 0 errors. Read-only process inspection
confirmed PID `119488`, title `AutoCAD Architecture 2027 - [3d.dwg]`, command line
opening `C:\Users\Roman\Documents\3d.dwg`, and loaded adapter from
`src\AcKrovy.AutoCAD\bin\x64\Debug\net10.0-windows\AcKrovy.AutoCAD.dll`.
Built DLL SHA256: `97738754CF852B8522DA4853E2665A998C433F2842FE0460D3AA3BEB9C1EE4C1`.
Startup/autoload remains authoritative for AK_RUNTIME_BUILD and AK_ROOF_3D_TRACE;
no competing loader or TRACE command was added. New source / report files also
passed whitespace inspection using `git diff --no-index --check`.

HOST acceptance is complete for this checkpoint:

A. PASS — Locked roof: MOVE one Hip Physical3D body was restored/rebuilt to the
canonical state before later commands.

B. PASS — Unlocked roof: MOVE one Hip Plan2D reference retained its new persistent
XY position with the same owner/key, and the corresponding Structural Physical3D
was rebuilt from it.

C. PASS — Unlocked roof: ERASE one Hip Plan2D reference left the Plan2D member
suppressed/removed and its Structural Physical3D removed; structural count changed
4 → 3. GROUP after B and C reported `duplicates=0`, `missing=0`, `foreign=0`,
`canonical=True`.

Therefore: **Structural Foundation HOST accepted**. Native U/REDO behavior, real
erased-object reattachment, geometry
and immediate visual recovery require actual AutoCAD evidence. Full structural
COPY / MIRROR / BREAK, endpoint editing and whole-roof mirror transformation of
accepted placement are not accepted by this report and are not expanded here.
The structural physical materializer rebuilds the owner structural body set, so
physical ObjectIds can change; semantic owner + LogicalKey remains stable.
