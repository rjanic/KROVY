# Native COPY routing and compact HOST trace — 2026-09-30

Code-side validation report. HOST retest of this fix has not been run.
Branch: `main`; HEAD: `4f03788f7afb6f26e95dceff52e6e960e088706d`.
Existing unrelated WIP was preserved. No commit, push, tag or release.

1. **Root cause.** The supplied HOST evidence proves native COPY mapping from a
   Generated source to a newly appended Line with inherited Generated metadata.
   `LiveGeometrySynchronizationService.RefreshCandidates` previously called
   `RoofLiveResizeService.Process` before `ProcessNativeMemberClones`.
   Inspect classified the duplicate Generated Line as member tamper. COPY is an
   assembly-snapshot/ownership command, deliberately not a direct supported
   Generated geometry-edit command. `ProcessOwner` therefore recovered it through
   `TryRecoverGeneratedMembersOnly` before clone promotion could run.
2. **Why `command-misclassified`.** For an unlocked owner, the unsupported-command
   branch emits this reason after successful generated recovery. Adding COPY to
   the MOVE/TRIM/STRETCH override vocabulary would conflate source geometry edits
   with semantic cloning and is not the fix.
3. **Ordering fix.** Whole-roof native rebind retains first priority. Member COPY
   now invokes the existing clone transaction before LiveResize/tamper inspection.
   Handled clone IDs and proven unchanged mapped source IDs are removed from the
   remaining generic maintenance inputs. MIRROR keeps its later routing slot.
4. **Transient metadata.** Inherited Generated/AttachedManual metadata is permitted
   within this command's ownership transaction. Its owner must match a pre-command
   roof with unchanged source geometry. Genuine failures still use existing
   rollback and snapshot recovery. No general tamper gate is weakened.
5. **Generated source.** Source geometry and Generated metadata are checked against
   the native snapshot. COPY never requests a source override or suppression.
   The physical planner preserves each existing canonical source body.
6. **Clone identity.** The existing Generated rehydration/promotion service clears
   Generated XData and creates AttachedManual Origin.Copy schema v4 data through
   `CreateAnchoredData`. This uses a fresh persistent UUID; CAD handle remains a
   binding, not semantic identity. No new schema or persistence framework.
7. **Physical creation.** The existing semantic model, AttachedManual physical
   builder and reconciliation planner build missing clone bodies from accepted
   Plan2D, measurements, topology and settings. Plan2D remains at Z=0. Solid
   geometry is not read as authority. Compact diagnostics report rebuilt/removed
   body counts and affected semantic keys.
8. **Collateral clones.** Native copied physical entities are identified using
   pre-command membership. They are passed as collateral to the shared planner,
   detached from GROUP and erased. Native appended annotations bound to original
   sources are removed before canonical clone presentation is refreshed. Original
   annotations are excluded by the command-appended ID set.
9. **Multiple COPY/idempotence.** Appended IDs and mapping-derived clone IDs are
   united and deduplicated. Mapping is indexed by clone, not source; one source
   can have many clones. A command-local consumed-clone set prevents repeated
   processing. It clears with the existing snapshot. Each final clone receives
   its own UUID and missing physical body.
10. **AttachedManual COPY.** Existing Copy and Split children use the established
    reinitialize service to create a fresh Origin.Copy clone. Source UUID remains
    stable. Common copy-preserving numbering/annotation refresh now runs inside
    the clone transaction, including COPY-of-COPY, with failures propagated to
    its existing rollback path.
11. **Undo/Redo.** All operations remain inside the existing native command undo
    scope and outer transaction. U/UNDO/REDO/MREDO still clear queues without DB
    maintenance. UUID metadata and physical/group changes participate in native
    undo. Serialization tests prove stable replay identities; they do not prove
    AutoCAD's native Undo/Redo execution.
12. **GROUP.** Existing GROUP sync and physical/GROUP verification must succeed
    before the clone transaction commits. Automatic summaries show expected,
    actual, duplicate, missing and foreign member counts plus canonical status.
13. **Physical keys.** Existing uniqueness/cardinality validation remains enabled.
    Duplicate-key diagnostics retain offending handles. Planner tests prove
    collateral removal and exactly one desired body per semantic key.
14. **TRACE reduction.** Automatic command boundaries and physical reconciliation
    no longer call exhaustive Audit. Owner summaries omit inventory handles,
    signatures, roof-definition payloads and individual unaffected physical items.
    Repeated native events are counted by kind; member output is bounded to one
    native record per handle plus affected before/after checkpoints. Unchanged
    member notifications are omitted from those checkpoints.
15. **Deep audit.** `AK_ROOF_3D_AUDIT` remains the explicit exhaustive read-only
    path. It retains Plan2D/Physical3D/display/owner inventory, geometry, metadata,
    raw physical data, full GROUP membership and physical handle/signature lists.
    Recovery/fallback/error diagnostics remain intact.
16. **Files changed in this task.** Four AutoCAD source files:
    `LiveGeometrySynchronizationService.cs`, `RoofNativeCloneSnapshot.cs`,
    `RoofOrdinaryRafterSolidMaterializationService.cs`,
    `RoofPhysical3DHostDiagnostics.cs`; four test files:
    `RoofNativeCopyRoutingTests.cs` (new),
    `RoofAttachedManualPhysicalLifecycleTests.cs`,
    `RoofPhysical3DHostDiagnosticsSourceContractTests.cs`,
    `RoofPhysical3DLockedSourceEraseSourceContractTests.cs`; this report.
    No Core production, geometry, UI or localization change in this task.
17. **New tests.** Five adapter routing/source-contract cases and four real Core
    single/multiple COPY planner/geometry/codec cases, with and without collateral
    copied bodies. Cover promotion order, exact map consumption, repeated-event
    deduplication, unchanged sources, unique UUIDs, idempotent physical planning,
    stable persisted identities and compact automatic trace/full explicit audit.
    Existing AttachedManual Copy/Split source tests and serialized COPY undo/redo
    model tests remain included. Adapter assertions are not native HOST tests.
18. **Tests.** COPY/diagnostic focused run: 64/64 PASS. Expanded lifecycle
    regression run: 724/724 PASS. Core: 6953/6953 PASS outside sandbox. Initial
    sandbox Core run failed only existing SafeFileWriter's atomic File.Replace
    with UnauthorizedAccessException; its implementation was not modified.
19. **Builds/gates.** Debug x64 and Release x64 PASS, both with zero warnings and
    errors. Full Compatibility Gate PASS, including its Portable Gate and
    architecture/dependency checks. Core 6953/6953 and WPF 806/806 in Full Gate.
    git diff --check PASS; new test/report files separately checked for whitespace.
    The gate ran outside the sandbox for the existing atomic temporary-file test.
20. **Minimal HOST retest.** In AutoCAD Architecture 2027, open the saved clean
    `C:\Users\Roman\Documents\3d.dwg` with the existing startup/autoload. COPY
    one 2D ordinary rafter to one new location. Visually verify both its new 2D
    member and new 3D body; capture the automatic compact TRACE. Expect a stable
    Generated source, new AttachedManual UUID, one additional ordinary physical
    body, uniqueKeys=True and canonical GROUP, without command-misclassified
    recovery. Manual AK_ROOF_3D_AUDIT is needed only if automatic final summaries
    are missing or inconsistent. Multiple COPY, AttachedManual COPY and native
    UNDO/REDO remain additional HOST risks, not claimed as validated.

## Validation commands

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoofNativeCopyRouting|FullyQualifiedName~RoofAttachedManualPhysicalLifecycle|FullyQualifiedName~RoofPhysical3DHostDiagnostics' -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~RoofNativeCopyRouting|FullyQualifiedName~RoofStructuralRestoreNumberingOrder|FullyQualifiedName~RoofBreakAtPoint|FullyQualifiedName~RoofAttachedManual|FullyQualifiedName~RoofGeneratedMember|FullyQualifiedName~RoofMixedPhysicalStretch|FullyQualifiedName~RoofOrdinaryPhysicalStretch|FullyQualifiedName~RoofPhysical3D|FullyQualifiedName~RoofStructuralGenerated|FullyQualifiedName~RoofMirror|FullyQualifiedName~RoofWholeRoofMirror|FullyQualifiedName~RoofHipLiveResize|FullyQualifiedName~RoofWholeRoofCopy|FullyQualifiedName~RoofGeneratedRafterCopy|FullyQualifiedName~RoofAssemblyGroup|FullyQualifiedName~RoofUndo|FullyQualifiedName~LiveGeometry|FullyQualifiedName~TimberElementItemNumbering' -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-build -m:1 -nr:false
pwsh -NoProfile -File scripts/acad-host-workflow.ps1 -SkipTests -NoLaunch
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Release -p:Platform=x64 -warnaserror -m:1 -nr:false
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
git diff --no-index --check -- /dev/null src/AcKrovy.Core.Tests/RoofNativeCopyRoutingTests.cs
git diff --no-index --check -- /dev/null docs/NATIVE_COPY_LIFECYCLE_FIX_2026-09-30.md
pwsh -NoProfile -File scripts/acad-host-workflow.ps1 -SkipTests
```

The repository architecture checklist calls for both compatibility gates. The
existing `-Full` script invokes the Portable Gate before full adapter/solution
validation. No separate redundant Portable run is necessary. AutoCAD is closed
for every adapter build. HOST validation: NOT RUN. Release verdict: not ready
for publication pending HOST retest; publication was not requested.

| Final check | Result |
| --- | --- |
| Branch / HEAD | main / 4f03788f7afb6f26e95dceff52e6e960e088706d |
| Upstream comparison | origin/main, ahead/behind 0/0 (local refs) |
| Working tree | Dirty before and after; unrelated WIP retained; nothing staged |
| Focused COPY/diagnostics | 64/64 PASS |
| Expanded lifecycle regressions | 724/724 PASS |
| Core | 6953/6953 PASS |
| WPF (Full Gate) | 806/806 PASS |
| Debug / Release x64 | PASS, zero warnings/errors |
| Portable / Full compatibility | PASS, Portable executed by Full |
| Restore / CAD API leakage checks | PASS |
| Localization / new schema change | Not applicable / none in this task |
| Diff whitespace | PASS |
| Actual HOST COPY / native Undo/Redo | NOT RUN |

The saved HOST DWG is launched through the existing helper after validation.
Per the user's latest instruction, the PC is put to sleep only after this work
and launch preparation are complete; open applications are preserved.
