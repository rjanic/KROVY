# Hip / Valley native edit lifecycle — architectural audit and evidence checkpoint

Date: 2026-10-01. Branch: `main`. Published baseline:
`21a92093e4e99edbf5fd3e3b4f9e1495ab1eabd5`.

## Status and scope

The requested complete structural lifecycle is **NOT IMPLEMENTED**. This change
provides a read-only architectural audit and DEBUG HOST instrumentation needed
before changing structural persistence, ownership, undo or recovery. No product
version, persisted schema, physical builder, production routing, ordinary
lifecycle, or localization resources were changed. Per-member Z remains out of scope.

The repository's Roof Timber Lifecycle workflow explicitly requires:

> When the real sequence cannot be proven statically, add narrow temporary DEBUG
> diagnostics and request one HOST run before changing architecture.

It also says:

> Do not change persistence/schema/undo architecture to compensate for an event
> sequence that has not been proven in AutoCAD.

These requirements are reinforced by the root `AGENTS.md`. Unit and source-contract
tests cannot provide the missing native evidence. The requested implementation
must continue after that checkpoint; a diagnostic PASS does not mean lifecycle PASS.

## 1. Architectural audit

Current source code is the implementation reference. The normative physical
contract is `docs/geometry/roof-elevation-contract.md`, especially H1.7/H1.8.
Older project-context and audit documents are historical context, not proof of
the current native command sequence.

| Concern | Published ordinary implementation / reusable component | Structural equivalent and required change |
| --- | --- | --- |
| Supported-command routing | `LiveGeometrySynchronizationService.RefreshTimberElements`, `RoofGeneratedMemberEditCommandRules` | Whole-roof ownership must retain first priority; a structural member router must claim supported native results before generic restoration. |
| COPY/MIRROR semantic claim | `ProcessNativeMemberClones` runs before `RoofLiveResizeService.Process` | It currently accepts ordinary Generated/AttachedManual plans and restores Hip/Valley. Generalize narrow role dispatch after HOST mapping evidence. |
| Command snapshots | `RoofUnsupportedStretchRecoverySnapshotService` captures structural Lines in the assembly; `RoofNativeCloneSnapshot` captures native mappings | Native member snapshots currently omit structural Lines from `Members` and classify them as derived plans. Add typed structural state; do not make structural plans disposable collateral. |
| Clone/source matching | Native `IdMapping` and pre-existing semantic snapshots; ambiguous maps are rejected | Preserve exact native pairs. Never use ElementId, handle, position, or iteration order as structural semantic identity. Handles remain session evidence/bindings only. |
| Identity | Generated `RoofGeneratedMemberKey`; AttachedManual v4 semantic UUID distinct from ChildIdentity | Existing structural identity is owner + `RoofStructuralLogicalKey` (role and canonical boundary pair). Preserve it for surviving/replacement members. Independent copies/fragments require new UUIDs. |
| Suppression | `RoofGeneratedMemberOverride` in owner definition | It is keyed by ordinary member kind/face/station and cannot represent a structural key. Structural suppression must survive destruction of the Plan2D entity. |
| Accepted 2D edits | `RoofGeneratedMemberManualEditService.ProcessOwner` / `TryAcceptUnlockedEdits` | Before ordinary acceptance, `TryRestoreStructuralHipValleyMembersOnly` restores structural geometry, then structural members are skipped. This is the first semantic divergence for structural edits. |
| BREAK fragments | Existing role-aware Generated/AttachedManual split reconciliation | `TryEraseUnsnapshotStructuralDuplicates` deletes inherited structural fragments before an unlocked structural semantic accept path exists. Preserve locked cleanup while excluding semantically claimed unlocked fragments. |
| Physical reconcile | `RoofOrdinaryRafterSolidMaterializationService.TryReconcileSemanticMembersInTransaction`, `RoofOrdinaryPhysicalReconciliationRules` | Reuse transaction/affected-key/collateral patterns. Structural materialization currently requires the exact complete canonical structural set. |
| Physical builder | Existing ordinary builder from semantic state | Reuse `RoofStructuralRafterPolyhedronService` and `RoofStructuralRafterPhysicalBuilder`. Extend explicit Plan2D input/context; do not create command-specific builders. |
| GROUP | `RoofAssemblyGroupMemberCollector` + `RoofAssemblyGroupSyncService` | Collect structural manual children as well as generated/physical/annotation members. Expected membership must derive from semantic state, never fixture counts or extents. |
| U/REDO | `LiveGeometryCommandRules.IsUndoRedoCommand` prevents DB maintenance around undo/redo | Keep zero DB access at boundaries. Persist semantic/physical/group changes in the native command undo scope; do not mint UUIDs or repair at REDO boundaries. |
| Direct 3D protection | `RoofLiveResizeService` derived MOVE/ERASE/STRETCH paths; `RoofPhysical3DLifecycleService.TryRestoreStretchPhysicalInTransaction` | Structural stretch rebuild already invokes the shared canonical builder; ERASE includes structural solids. These paths must consume edited structural semantics after implementation, with immediate recovery proven in HOST. |
| Roof maintenance | `RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction` called on live resize | Currently generates canonical members and physical set. Replay structural overrides, suppressions and independent children before materialization, preserving exact anchors through topology changes. |

### Existing physical builder constraints

`RoofStructuralRafterPolyhedronService.TryBuild` requires the resolved segment to
match its topology edge, a compatible real Hip/Valley fold, exactly two incident
faces and a resolved eave boundary anchor. `RoofStructuralRafterPhysicalBuilder`
requires the two roof planes to agree on the canonical axis and uses actual
ordinary physical contacts for automatic height and lower-end trimming.

`RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction`
requires the live structural key set to equal the canonical eligible edge set.
Consequently enabling native routing alone cannot support suppression, clones or
fragments: later maintenance would restore/delete them or fail set validation.

Manual geometry needs an explicit Core extension that retains canonical eave,
top-profile, lower-end and upper-node behavior for unedited members. A shortened
fragment must not retain a cut at a removed original endpoint. Whether an
off-fold translated/rotated segment admits the required rectangular Hip or
existing Valley profile must be validated from retained roof-face context; an
undefined or incompatible construction must fail atomically and recover.
No geometry definition is invented in this checkpoint.

### Actual available HOST evidence

Read-only inspection covered every `.log` under
`%LOCALAPPDATA%\ACAD_KROVY\Logs`. Four native structural member records were found,
all Hip, in `ACAD_KROVY-20261001.log`:

| Local time (Europe/Bratislava) | Command | Proven native result | First observed plugin divergence |
| --- | --- | --- | --- |
| 09:46:41 | BREAK | Source Line `29F8` modified; appended Line `2A83` inherits StructuralRole=Hip. Native summary: ObjectModified=4, ObjectAppended=1. | `structural-unsnapshot-erase reason=break-fragment owner=2912 handle=2A83 kind=Hip` at 09:46:41.9269315. |
| 09:47:12 | BREAKATPOINT | Source Line `29FB` modified; appended Line `2A95` inherits StructuralRole=Hip. Native summary: ObjectModified=4, ObjectAppended=1. | Same fallback deletes `2A95` at 09:47:12.4655966. |

These are historical HOST observations of the baseline path, not a new HOST
retest or proof of the complete requested family. No Valley native member record
was found. Ordinary MIRROR in-place evidence must not be assumed to prove
structural MIRROR behavior. Existing first-event-per-Line reporting does not prove
later erase/unerase transitions or the structural physical event sequence.

## 2. Structural semantic model candidate

No new persisted model has been implemented or finalized before HOST evidence.
The compatible candidate is a separate, versioned structural-manual model shared
by Hip and Valley, rather than overloading ordinary AttachedManual's face/station
anchor semantics.

It needs owner-scoped generated overrides/suppressions and independent structural
members, with minimal fields:

- Schema version and RoofOwnerReference.
- Canonical Structural LogicalKey for an override/suppression, or semantic UUID
  for an independent structural manual member.
- TimberType/structural role and exact source/anchor Structural LogicalKey.
- Relative Plan2D endpoints in the canonical structural anchor's planar basis;
  structural Plan2D Z remains zero.
- Required stable face/boundary provenance to resolve the real roof topology
  after SAVE/reopen or roof regeneration; no persisted transient topology index.
- Suppressed flag where applicable and origin (copy, mirror, split) if needed.

Suppression cannot live only on an erased child. Owner-scoped structural state
must survive its native deletion. Plan/physical bindings must reference the same
semantic identity. Existing ordinary override math and UUID validation may be
generalized narrowly, without changing public ordinary behavior.

## 3. Files changed

- `src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs`:
  automatic full-family member checkpoints; structural Plan2D and Physical3D
  snapshots; structural native mapping; complete structural native event sequence;
  owner definition/boundary/elevation context; explicit early undo/redo guard.
- `src/AcKrovy.Core.Tests/RoofPhysical3DHostDiagnosticsSourceContractTests.cs`:
  diagnostic isolation/routing/identity/late-ownership/event-sequence guards.
- This audit/report and the HOST evidence plan below.

All pre-existing unrelated untracked WIP was preserved. No commit/push/tag/release.

The Solid3d centroid/volume are diagnostic measurements only, taken at
checkpoints. Native solid event callbacks do not measure bodies. They are never
semantic state, roof footprint authority, or physical builder input. The
diagnostic observer opens objects ForRead, never commits a transaction, and has
no entity/XData/visibility writes. It exists only in DEBUG builds.

## 4–6. Commands implemented and Hip/Valley coverage

**No new accepted structural edit command is implemented by this checkpoint.**
Diagnostics cover the ten normalized native command names MOVE, TRIM, EXTEND,
STRETCH, GRIP_STRETCH, ERASE, COPY, MIRROR, BREAK, BREAKATPOINT automatically,
without manually toggling TRACE. U/UNDO/REDO/MREDO are excluded from diagnostic DB
access. The same instrumentation recognizes real Hip and Valley members by metadata.

The future structural semantic and HOST matrix remains pending for BOTH roles:

| Required case | Hip implementation/retest | Valley implementation/retest | Required semantic assertion |
| --- | --- | --- | --- |
| MOVE | PENDING | PENDING | Preserve logical identity; persist segment; replace derived body once. |
| TRIM | PENDING | PENDING | Accept final endpoints and preserve identity. |
| EXTEND | PENDING | PENDING | Same extent reconciliation as TRIM. |
| STRETCH | PENDING | PENDING | Accept Plan2D; affected Physical3D rebuilt. |
| GRIP_STRETCH | PENDING | PENDING | Same semantic path, including endpoint edits. |
| ERASE | PENDING | PENDING | Generated suppression survives maintenance; manual child deletion stays deleted. |
| COPY single | PENDING | PENDING | Source unchanged; one new persistent UUID and derived body. |
| COPY multiple | PENDING | PENDING | N distinct UUIDs and one body per new member. |
| MIRROR No | PENDING | PENDING | Source unchanged; clone gets UUID; native solid is collateral. |
| MIRROR Yes | PENDING | PENDING | Preserve identity for unambiguous replacement; ambiguous mapping recovers safely. |
| BREAK | PENDING | PENDING | Native removed interval; original retains key; new fragments get UUIDs. |
| BREAKATPOINT | PENDING | PENDING | Zero-gap split; same identity allocation as BREAK. |
| U | PENDING | PENDING | Native transaction undo restores semantic/physical/group state together. |
| REDO | PENDING | PENDING | Replay persisted UUIDs; no duplicated identities. |
| SAVE/reopen | PENDING | PENDING | Hydrate persisted state without read-time identity creation or DB writes. |
| direct 3D MOVE | PENDING | PENDING | Immediate recovery from semantic state. |
| direct 3D ERASE | PENDING | PENDING | Immediate recovery; no semantic suppression. |
| direct 3D STRETCH | PENDING | PENDING | Immediate recovery, without a second user command. |
| direct 3D GRIP_STRETCH | PENDING | PENDING | Same immediate recovery requirement. |
| Mixed Plan2D + Physical3D | PENDING | PENDING | Plan2D wins; native physical collateral removed. |
| Locked protection | Existing protection retained; new matrix PENDING | Existing protection retained; new matrix PENDING | Structural Plan2D recovered while locked; 3D always read-only. |
| Unlocked acceptance | PENDING | PENDING | Accept defined semantic geometry; invalid builder context fails safely. |

For every case, derive expected members from canonical semantic state plus
overrides/manual children minus suppressions. Assert one body per semantic key,
unique generated keys/manual UUIDs, no orphan solids, canonical GROUP, zero
foreign/missing/duplicate members and structural Plan2D Z=0. Do not hardcode counts.

Existing structural break-leak/lock tests encode recovery even for Unlocked roofs.
During implementation update only the intentionally superseded unlocked
expectations, retaining locked/unsupported guards and all ordinary regressions.
Passing those baseline tests now is not acceptance of the requested new behavior.

## 7. Persistence / Undo / Redo design boundary

The implementation must use the published native command transaction/undo scope.
Compose semantic edits, plan metadata/annotation updates, physical replacement,
collateral cleanup and GROUP reconciliation atomically. A failed physical build
rolls back semantic writes and restores native result geometry from the pre-command
snapshot. UUIDs are created only on new independent member creation and restored
by native redo, never regenerated at an undo/redo boundary. Roof maintenance must
replay exact structural anchors, retaining valid edits/copies/splits/suppressions.

This is a required design boundary, not a claim that new structural persistence
or native undo/save/reopen currently works.

## 8. Validation

**Diagnostic package CODE-SIDE PASS. Requested complete lifecycle: NOT IMPLEMENTED.**

| Check | Result |
| --- | --- |
| Final focused structural/ordinary/diagnostic regressions | PASS — 633/633, 0 skipped |
| Complete Core | PASS — 7002/7002, 0 skipped |
| Complete WPF | PASS — 806/806, 0 skipped |
| Solution Debug x64 | PASS — 0 warnings, 0 errors |
| Solution Release x64 | PASS — 0 warnings, 0 errors |
| Portable Compatibility Gate | PASS — restores/builds/7002 tests, architecture and version checks |
| Full Compatibility Gate | PASS — portable checks, solution restore/build, 7002 Core + 806 WPF tests |
| git diff --check | PASS |
| New report whitespace check | PASS — git diff --no-index --check against /dev/null |
| New structural semantic lifecycle tests | NOT IMPLEMENTED — table above is a planned matrix, not runnable new acceptance tests |
| Current HOST acceptance | NOT RUN / NOT CLAIMED |

The initial full Core run detected prohibited `GeometricExtents` usage even in
diagnostics. It was removed; the unchanged footprint-authority test now passes.
The existing `SafeFileWriterTests` also failed on atomic `File.Replace` under the
execution sandbox, including with TEMP/TMP relocated inside the workspace. The
same test passed outside the sandbox, confirming the environment restriction.
The entire final matrix was then rerun outside the sandbox and passed; no
SafeFileWriter or other unrelated production code/test was modified. A temporary
validation-runner result-file sharing failure was avoided by separate per-step
result files. Neither earlier failure is hidden by filtering or skipped tests.

Exact final commands (all from the repository root; AutoCAD remained closed):

```powershell
$focusedFilter = 'FullyQualifiedName~RoofPhysical3DHostDiagnosticsSourceContractTests|FullyQualifiedName~RoofStructuralGenerated|FullyQualifiedName~RoofStructuralRestoreNumberingOrder|FullyQualifiedName~RoofSupportedNativeMemberRouting|FullyQualifiedName~RoofNativeCopyRouting|FullyQualifiedName~RoofAttachedManual|FullyQualifiedName~RoofBreakAtPointRouting|FullyQualifiedName~RoofMixedPhysicalStretch|FullyQualifiedName~RoofOrdinaryPhysicalStretch|FullyQualifiedName~RoofPhysicalGripStretchRecovery|FullyQualifiedName~RoofStructuralRafter|FullyQualifiedName~RoofGeneratedMember|FullyQualifiedName~RoofMirror|FullyQualifiedName~RoofOrdinaryExtendPhysical'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter $focusedFilter --logger 'console;verbosity=minimal'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --logger 'console;verbosity=minimal'
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj --no-restore -p:Platform=x64 -warnaserror -m:1 -nr:false --logger 'console;verbosity=minimal'
dotnet build AcKrovy.sln --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
dotnet build AcKrovy.sln --no-restore -c Release -p:Platform=x64 -warnaserror -m:1 -nr:false
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
git diff --no-index --check -- /dev/null docs/HIP_VALLEY_NATIVE_LIFECYCLE_AUDIT_2026-10-01.md
```

Raw logs and per-step exit codes are retained in the ignored local folder
`artifacts/hip-valley-audit-validation/`. A preliminary focused run passed 238/238;
the final expanded selection above supersedes it. The diagnostic test class has
12 source-contract checks, including five new checks. These guard diagnostic
isolation and evidence capture, not actual AutoCAD native lifecycle behavior.

### HOST preparation result

After the diagnostic package passed all checks, the existing workflow was run:

```powershell
pwsh -NoProfile -File scripts/acad-host-workflow.ps1 -Configuration Debug -SkipTests
```

It verified that no acad.exe was running, rebuilt the adapter with 0 warnings /
0 errors and launched AutoCAD with the saved fixture. Tests were skipped in this
last workflow invocation because the complete final matrix had just passed.
Read-only process inspection then confirmed:

- AutoCAD PID `65200`, responding.
- Window title `AutoCAD Architecture 2027 - [3d.dwg]`.
- Command line targets `C:\Users\Roman\Documents\3d.dwg`.
- Loaded module is the current repository
  `src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll`.
- Built DLL SHA256:
  `90C6FAC0BB28C96C357B559352D82F4F7E0BB9DFF517B508055953A72A296FEA`.

Startup/autoload and runtime/TRACE configuration were left to the existing
workflow. No competing loader was added and no manual TRACE setup was requested.
No native member edit command was executed during this task. This launch is
preparation for the required evidence checkpoint, not structural HOST acceptance.

## 9. Remaining limitations and next HOST phase

The entire requested production implementation and semantic test matrix remain
pending. Missing native structural COPY/MIRROR/grip/physical sequences block
persistence/recovery architecture changes under the repository rule. The new
instrumentation has no current HOST execution result. It cannot establish
immediate recovery or SAVE/reopen/Undo/Redo by static tests.
Equal centroid/volume is not proof of identical solid shape; deformation recovery
needs real HOST observation or a complete physical geometry comparison.

### HOST Test Plan — baseline native evidence, first Hip

- AutoCAD version: AutoCAD Architecture 2027.
- Build: Debug x64 diagnostic checkpoint; `AK_RUNTIME_BUILD` must identify the
  loaded current DLL through the existing autoload/runtime workflow.
- DWG: `%USERPROFILE%\Documents\3d.dwg`.
- Branch: `main`; HEAD: `21a92093e4e99edbf5fd3e3b4f9e1495ab1eabd5` plus the three
  uncommitted diagnostic/audit files listed above.
- This phase observes existing baseline behavior; acceptance of structural edits
  is not expected until the production implementation exists.

Preconditions: a real generated Hip Plan2D Line and its owned
StructuralRafterSolid; Physical3D enabled; owner and logical key confirmed by
metadata; the source footprint itself stays unselected. Use isolated member
selection with GROUP selection disabled as appropriate. Restore a clean baseline
between scenarios. Protect the saved fixture; do not save experimental edits
over it. Existing TRACE/autoload infrastructure remains authoritative; the new
checkpoints are automatic and require no manual TRACE command.

Steps:

1. On an Unlocked Hip owner run Plan2D COPY once and multi-copy; MIRROR No and Yes.
   Include a separate mixed Plan2D + corresponding Physical3D COPY/MIRROR case.
2. Run endpoint GRIP_STRETCH and classic endpoint STRETCH on Plan2D; repeat with
   mixed Plan2D + Physical3D selection. Run MOVE, TRIM, EXTEND and ERASE on Plan2D.
3. Run BREAK and BREAKATPOINT to capture structural baseline and native fragment
   checkpoints with the new observer (historical evidence already exists).
4. Select Physical3D alone and run MOVE, ERASE, STRETCH and GRIP_STRETCH. Observe
   whether recovery is immediate and whether a body was actually modified; a
   command that affects no body does not prove deformation recovery.
5. Repeat a representative Plan2D MOVE/grip/ERASE attempt on a Locked owner.
6. Record U/REDO observations and command line output. No plugin diagnostic DB
   read or maintenance may run at U/REDO boundaries. SAVE/reopen validation of
   the new structural model is deferred until that model is implemented.
7. After Hip evidence, repeat on a clean supported roof containing a genuine
   Valley structural member. Do not synthesize a Valley from a Hip or assume
   rectangle geometry contains one.

Expected evidence: command start, `STRUCTURAL_NATIVE_SEQUENCE` ordered callbacks,
`MEMBER_MAP` exact pairs/ambiguity, `MEMBER_CHECKPOINT` at native-ended and
after-maintenance, `MEMBER_OWNER_CONTEXT` lock/topology/elevation, physical counts
and GROUP summary. Capture DBMOD before/after separately through the existing HOST
workflow, visible recovery behavior and native U/REDO results. If inherited
ownership is unreadable at ObjectAppended, the observer retains the original
Line/Solid3d event sequence and resolves structural relevance at native completion.

Compare the first semantic/router divergence with working ordinary commands.
Only then implement the full Hip/Valley family coherently and run the requested
semantic/Core/WPF/build/gate matrix, followed by acceptance HOST phases.

### HOST Regression Test Result

| Check | Result |
| --- | --- |
| Current diagnostic HOST execution | NOT RUN |
| Loaded intended build | Current repository Debug DLL path confirmed in fresh AutoCAD process; no native acceptance test |
| DBMOD / entity lifecycle / visual behavior | NOT CHECKED |
| New structural persistence / native U/REDO | NOT IMPLEMENTED / NOT CHECKED |
| New diagnostics in AutoCAD | NOT CHECKED |
| Historical Hip BREAK/BREAKATPOINT | Available evidence described above; not current retest PASS |
| Valley native evidence | MISSING |

Verdict: **INCONCLUSIVE / HOST PASS NOT CLAIMED**.
