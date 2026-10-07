# Ordinary override continuity and roof grip investigation — 2026-10-02

**CODE-SIDE: PASS ✅** for OrdinaryCutNotOnStructuralSide cut contract + SupportedResize HardFailure atomic rollback/liveness (2026-10-03). Prior A/B override-continuity work remains. Prior root-cause C (intermittent first grip miss) remains evidence-only / HOST-not-proven. HOST retest for this structural-cut + rollback fix: NOT RUN.

## Baseline and WIP preservation

- Branch: `main`.
- Baseline HEAD: `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`.
- Local upstream comparison: ahead 0 / behind 0; no fetch performed.
- Initial inventory: 72 dirty/untracked files, including expanded untracked directories. Before edits, `git status --short`, binary working diff and SHA-256 inventory were saved in `%TEMP%/krovy-override-continuity-20261002/`.
- 69 initial files remain byte-for-byte identical by SHA-256. The three necessary shared extensions are `RoofOrdinaryRafterSolidMaterializationService.cs`, `RoofAutomaticRafterPhysicalBuilder.cs` and `RoofGeneratedMemberReplayPlanner.cs`. Every original added WIP line remains present; original pure-MOVE replay/body/clip blocks are retained. This is preservation of the existing WIP with additive shared fixes, not a claim that these three files are byte-identical.
- AttachedManual anchor/frame, ManualStructural COPY, Structural lifecycle/LOCK, SimpleGable WIP, published compatibility documentation and existing tests were not edited by this task.
- No reset, revert, stash, clean, checkout, commit, push, tag or release performed.
- AutoCAD was checked outside the sandbox and was not running before CAD-linked builds.
- No persisted schema, application version, topology or identity mapping change. `CarriesAcceptedTranslation` is a transient Core replay-item property; no metadata store or codec persists it.

## Root cause A — confirmed and fixed

`RoofGeneratedMemberManualEditService` already composes accepted endpoint edits into the previous override and writes it before Physical3D reconciliation. The persisted translation is not lost there.

`RoofOrdinaryRafterSolidMaterializationService.TryBuildExistingModelInTransaction` calls `RoofGeneratedMemberReplayPlanner.CreateForExistingPhysicalMembers`. That existing WIP path revives an outside-domain edit only when rotation and both endpoint offsets are zero. After MOVE → STRETCH/GRIP_STRETCH, an endpoint offset is nonzero, so the exception fails. Generic `Create` supplies canonical geometry with `DormantInvalidDomain`, and the physical builder receives the canonical member instead of the accepted final Plan. This loses both translation and changed length.

A second necessary physical seam is the general edited-member branch of `RoofAutomaticRafterPhysicalBuilder.TryBuild`: building a displaced axis directly on the original absolute roof plane changes elevation. For accepted ordinary replay, the new branch applies the same complete override with translation zero to derive the local shape, resolves its endpoint/cut semantics through the existing builder, then carries the prior translation on the body and every existing clip/construction input using the existing `TranslateMember` implementation. The final Plan axis is the complete override axis. Pure MOVE retains its original WIP branch. Legacy replay, AttachedManual and Structural builders retain their existing interpretation.

## Root cause B — confirmed and fixed

`RoofGeneratedRafterSetService.TryReplaceForOwnerInTransaction` used generic `RoofGeneratedMemberReplayPlanner.Create`. Exact-key resolution succeeds, but `RoofGeneratedMemberDomainRules.OverlapsBoundedPlane` rejects a wholly outside-footprint axis. The planner substitutes canonical geometry and increments `DormantInvalidDomainCount`. Thus three resolved accepted edits can yield `geometryReplayed=0`, `dormantInvalidDomain=3`.

`RoofAcceptedOrdinaryOverrideReplayRules.Apply` is the shared Unlocked acceptance policy over the base replay plan. It uses `RoofGeneratedMemberOverrideMath.TryApply` for all Along/Lateral/Rotation/StartOffset/EndOffset values and revives resolved, nonsuppressed accepted overrides regardless of footprint overlap. Resize applies the persisted values against the newly solved canonical key. Missing keys remain stored and dormant without nearest rebind; suppression remains suppressed; invalid plans remain invalid. Locked policy returns the original plan unchanged.

Existing-member physical replay additionally requires the exact-key live Plan to match the complete persisted geometry within the existing 0.01 mm tolerance. Missing, mismatching or nonfinite live geometry fails the build with `accepted-override-live-plan-mismatch` instead of materializing a stale canonical fallback for an outside-domain accepted edit.

The policy is wired into ordinary resize replacement, ordinary materialization, existing-model physical reconcile, existing-owner physical rebuild and dependent ordinary-model preparation. Canonical annotation creation and GROUP finalization remain owned by the existing services and transactions.

## Root cause C — not established from available HOST evidence

The current HOST log `%LOCALAPPDATA%/ACAD_KROVY/Logs/ACAD_KROVY-20261002.1.log` was inspected. The 17:25:27 grip affects member Line `2942`; it records manual edit acceptance and an ordinary physical reconcile. Its `hasWork=False modified=0` timing is emitted after roof/member processing and is not proof of a missed source resize. The 17:25:33, 17:25:37 and 17:25:39 grips record maintenance/replacement work. No identified failing source drag includes sufficient before/after source-polyline evidence to establish the first divergence.

The old native diagnostic counter counts every DBObject event but detailed `NATIVE_EVENT` lines log member Lines. For example a member line can be `seq=2` without detail for `seq=1`. The missing detail cannot be treated as an absent source event.

Traced production path:

1. `CommandWillStart` normalizes command gates, starts stretch scope, captures grip/group baseline and pre-command roof assembly snapshots.
2. `ObjectModified` queues object IDs unless ignore/suppression guards apply.
3. `CommandEnded` drains queues; Structural claims precede `RoofLiveResizeService.Process` and later annotation/group work.
4. `Process` has an empty-modified-ID early exit; `Inspect` resolves source candidates from modified source/member/display/physical IDs. It does not independently enumerate changed snapshot owners for GRIP.
5. `HasSourceGeometryChanged` compares actual source geometry with the command snapshot using existing tolerances. Owner classification then chooses SupportedResize/Unsupported/other state; resize owners are deduplicated in a HashSet and passed to existing `ApplyResizes` transactions.
6. Command finalization clears transient snapshots/scopes. There is no new timer, retry, rebuild owner or changed undo boundary in this task.

Missing candidates, suppression, snapshot timing or unchanged actual source remain hypotheses, not the reported root cause. No C behavior change was designed around an inferred event order.

Added DEBUG evidence:

- `ROOF_GRIP_NATIVE_SOURCE`: actual source-polyline native event sequence, handle and geometry.
- `ROOF_GRIP_COMPLETION phase=before-inspection`: snapshot, current source, queued flag, modified count, EditState and authoritative source-change result, before the empty-queue return.
- `ROOF_GRIP_COMPLETION phase=inspection`: related count and resize/unsupported owner handles.

These diagnostics read geometry only. The zero-DB Undo/Redo guard runs before them. They are omitted from Release. They must be removed after the actual C divergence is proven and fixed.

The repository workflow [Roof Timber Lifecycle](../.agents/skills/roof-timber-lifecycle/SKILL.md) requires: “When the real sequence cannot be proven statically, add narrow temporary DEBUG diagnostics and request one HOST run before changing architecture.” The supplied AGENTS instructions also require proof of the real HOST event/control-flow sequence. Consequently C and requested single/repeated-grip rebuild tests 7/8 remain outstanding. No synthetic decision test is presented as proof of the unobserved HOST failure.

## Files changed by this task

| File | Purpose |
| --- | --- |
| `src/AcKrovy.Core/Services/Roofs/RoofAcceptedOrdinaryOverrideReplayRules.cs` | Shared exact-key Unlocked acceptance replay policy |
| `src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberReplayPlanner.cs` | Transient policy marker; domain documentation; existing WIP retained |
| `src/AcKrovy.Core/Services/Roofs/RoofAutomaticRafterPhysicalBuilder.cs` | Accepted cumulative shape plus carried translation using existing builder/cut translation |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryRafterSolidMaterializationService.cs` | Existing physical and owner rebuild wiring |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedRafterSetService.cs` | Resize/materialization replay wiring |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofRafterCommandWorkflow.cs` | Consistent dependent ordinary physical-model preparation |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs` | DEBUG native source-grip evidence |
| `src/AcKrovy.AutoCAD/Infrastructure/RoofLiveResizeService.cs` | DEBUG completion evidence; resize decisions unchanged |
| `src/AcKrovy.Core.Tests/RoofOrdinaryOverrideContinuityTests.cs` | 21 new behavioral cases |
| This report | Findings, evidence limits and handoff |

## Tests and validation

Tests were added before production edits: 11 cases, 10 failed / 1 passed. Failures exposed full endpoint replay and resize dormancy; pure MOVE already passed.

Final new behavioral coverage: MOVE; MOVE → STRETCH; MOVE → GRIP_STRETCH; TRIM/EXTEND; repeated endpoint composition through actual classify/compose methods; resolved outside override resize including rotation/start/end offsets; missing exact-key dormancy and exact-key reactivation; mismatching/missing/nonfinite live Plan rejection; Locked policy; suppression; canonical annotation planning, placement and idempotent source ownership. Physical assertions include final Plan, XY/Z vertex relationships, section, pitch, changed physical length, lower/structural cut geometry, unaffected other keys, removal of the old body, one physical identity and canonical Core reconciliation/GROUP membership.

Annotation and GROUP tests exercise Core planning/ownership/reconciliation, not a live AutoCAD DB entity count. The existing canonical CAD annotation/GROUP paths were preserved; final DWG counts remain a HOST check.

During development the first full Core exposed a legacy MIRROR behavior regression and the first Debug build exposed a missing diagnostics namespace. Both were fixed and the focused/full validations rerun. A proposed 11 × 7 m resize test fixture exposed an unrelated ambiguous Hip trim target for station 23; the final regression uses the existing valid 12 × 6 m resize fixture without changing Structural trim semantics. No failure is concealed or fixed by weakening the physical assertions.

| Check | Final result |
| --- | --- |
| New behavioral cases | 21 PASS |
| Focused new/MOVE/LOCK/AttachedManual/ManualStructural/annotation suite | 171 PASS, 0 failed/skipped |
| Full Core | 7270 PASS, 0 failed/skipped |
| Full WPF | 826 PASS, 0 failed/skipped |
| Debug x64, warnings as errors | PASS, 0 warnings / 0 errors |
| Release x64, warnings as errors | PASS, 0 warnings / 0 errors |
| Portable Compatibility Gate | PASS; 7270 Core tests; no CAD API leakage |
| Full Compatibility Gate | PASS; 7270 Core + 826 WPF = 8096 tests; adapter build 0 warnings / 0 errors |
| `git diff --check` | PASS; no whitespace errors |
| HOST | NOT RUN |

Commands run (logs/TRX retained under `%TEMP%/krovy-override-continuity-20261002/`):

```powershell
Get-Process -Name acad -ErrorAction SilentlyContinue
git rev-parse HEAD
git branch --show-current
git rev-list --left-right --count '@{upstream}...HEAD'
git status --short
git diff --binary
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj -m:1 -warnaserror --filter FullyQualifiedName~RoofOrdinaryOverrideContinuityTests
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj -m:1 -warnaserror --filter 'FullyQualifiedName~RoofOrdinaryOverrideContinuityTests|FullyQualifiedName~RoofOrdinaryGeneratedMovePhysicalTests|FullyQualifiedName~RoofAttachedManualAnchorStabilityTests|FullyQualifiedName~RoofAttachedManualPhysicalLifecycleTests|FullyQualifiedName~RoofStructuralManualCopyPlacementTests|FullyQualifiedName~RoofStructuralLocked|FullyQualifiedName~RoofLocked|FullyQualifiedName~RoofAutomaticRafterAnnotation'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj -m:1 -warnaserror
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj -m:1 -warnaserror
dotnet build AcKrovy.sln -c Debug -p:Platform=x64 -m:1 -warnaserror
dotnet build AcKrovy.sln -c Release -p:Platform=x64 -m:1 -warnaserror
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
```

## Minimal HOST handoff — two scenarios

Use the Debug build in the existing drawing with Physical3D enabled and the roof Unlocked. `AK_ROOF_3D_TRACE` should report `enabled=True` so the native source evidence is captured. These are A/B acceptance checks plus C evidence collection; deterministic C success is not yet claimed.

1. **A:** Select one Generated ordinary rafter, record its `Rafter:FaceX:N` identity, MOVE by `(+5000,-2700)` outside the footprint, then extend one endpoint by approximately 600 mm using STRETCH or GRIP_STRETCH. Run `AK_ROOF_3D_AUDIT`. The Plan retains both effects at Z=0; exactly one matching ordinary body remains at the displaced XY with changed length and correct section/slope/cuts. Annotation ownership follows that Plan; final GROUP expected=actual, duplicates/missing/foreign=0, canonical=True.
2. **B / C evidence:** In the same drawing complete exactly one valid source-roof GRIP_STRETCH resize, then `AK_ROOF_3D_AUDIT`. For the still-resolved key, accepted translation/rotation/endpoint offsets replay against the new canonical member; outside-footprint alone produces no DormantInvalidDomain, and Physical3D/annotations follow. Record whether the first completed grip rebuilds once, plus its time/owner, `ROOF_GRIP_NATIVE_SOURCE`, both `ROOF_GRIP_COMPLETION` phases and resize transaction completion. Final GROUP and unique physical keys must be canonical. If the first drag fails, return that first sequence rather than retrying it; it identifies the seam required to finish C.

No additional roof types or topology duplication requested.

## Structural cut failure and rollback-liveness investigation

Date: 2026-10-03. Existing WIP preserved (no reset/revert/stash/clean/checkout/commit/push).

### Offending ordinary / structural pair class

Exact HOST pair is drawing-specific; Core proof identifies the class that produces `structural-physical-OrdinaryCutNotOnStructuralSide` after outside-footprint MOVE + source GRIP_STRETCH:

- Ordinary: accepted overridden Generated member (identity preserved, e.g. `Rafter:FaceX:N`) with translated `StructuralCut` still carrying the original `TopologyEdgeIndex`.
- Structural: Hip/Valley edge selected by that index.
- Canonical ordinary geometry: cut lies on structural side (`±width/2`).
- Current ordinary geometry after MOVE `(+5000,-2700)` (and cumulative endpoint edit): cut plane/face vertices are rigidly translated with the body; signed side offset is thousands of mm, not `±width/2`.
- Intersection classification: **Case B / NonContact** — Plan endpoints no longer touch the Hip edge; logical membership alone must not force a cut.
- Classifier previously returned `OrdinaryCutNotOnStructuralSide` because contact gather was index-only, then fail-closed on side geometry.

DEBUG marker `ROOF_STRUCT_CUT_CLASSIFY` now emits ordinary key, structural edge/role, override Along/Lateral/Start/End, plan endpoints, signed offset detail, and `contact|non-contact|corrupt`.

### Cut root cause

`TranslateMember` intentionally shifts ordinary Physical3D cut vertices with the accepted MOVE (continuity tests require this). Structural builders treated any matching `TopologyEdgeIndex` as a mandatory contact. That couples logical membership to physical intersection.

Fix: [`RoofStructuralOrdinaryContactRules`](../src/AcKrovy.Core/Services/Roofs/RoofStructuralOrdinaryContactRules.cs)

- Contact = still on structural side (existing tolerances).
- NonContact = off-side and Plan does not touch the structural edge → skip (not an error).
- Corrupt = off-side but Plan still touches the edge → keep HardFailure.
- Wired into polyhedron + physical builder contact selection / lower-end contact inputs.

### Rollback / dead automatic editing root cause

Native GRIP_STRETCH commits the stretched source polyline before `CommandEnded`. `ApplyResizes` HardFailure aborted only the plugin rebuild transaction (definition/display/generated/structural writes), leaving **new source + old definition/children**. That aggregate poison made subsequent automatic edits appear dead. Runtime stretch-owner sets were secondary; they already cleared in `CommandEnded` finally, but HardFailure was also treated as quiet success for the rest of `Process`.

Dirty runtime state addressed explicitly on HardFailure:

- `SourceHandledOwnersThisCommand` / `SourceSupportedResizeOwnersThisCommand` cleared in `FinalizeSupportedResizeHardFailure`
- dedicated restore transaction before snapshot finally-clear
- HardFailure short-circuits further Process mutation for that command

### Atomic rollback fix

`FinalizeSupportedResizeHardFailure` + `TryRestoreSupportedResizeFailureAggregate`:

- abort rebuild txn
- restore source Polyline + snapshotted RoofDefinition + Plan2D timber + annotations
- erase unsnapshot duplicates
- rebuild display + GROUP sync + unlock indicator
- verify Physical3D inventory against snapshot handles (do **not** re-reconcile structural Physical3D after abort)
- emit `ROOF_RESIZE_FAILURE_RECOVERY` with full equality flags; `result=ok` only when groupCanonical and all restore flags are true
- emit `ROOF_RESIZE_HARDFAILURE_DETAIL` via `HardFailureAt` on every HardFailure stage
- DEBUG inject seam `InjectStructuralPhysicalFailureOnce`

Core phase machine [`RoofSupportedResizeFailureRecoveryRules`](../src/AcKrovy.Core/Services/Roofs/RoofSupportedResizeFailureRecoveryRules.cs) covers forced failure then immediate Ordinary MOVE / Ordinary GRIP_STRETCH / valid source GRIP_STRETCH / repeated fail-success without lifecycle degradation.

### Files changed (this investigation)

| File | Purpose |
| --- | --- |
| `RoofStructuralOrdinaryContactRules.cs` | Geometric contact eligibility |
| `RoofSupportedResizeFailureRecoveryRules.cs` | Failure-injection + post-failure liveness phase machine |
| `RoofStructuralRafterPolyhedronService.cs` | Use contact rules |
| `RoofStructuralRafterPhysicalBuilder.cs` | Use contact rules for height/lower-end |
| `RoofLiveResizeService.cs` | HardFailure finalization, injection, DEBUG classify/recovery markers |
| `RoofUnsupportedStretchRecoveryService.cs` | SupportedResize failure aggregate restore |
| `RoofStructuralCutAndResizeFailureRecoveryTests.cs` | Tests A–H + corrupt Case C/D |
| `RoofSupportedResizeFailureRecoverySourceContractTests.cs` | AutoCAD/Core wiring contracts |
| Source-contract updates for `ResizeBatchResult ApplyResizes` | Keep existing resize contracts aligned |
| This report | Findings |

### Validation (2026-10-03)

| Check | Result |
| --- | --- |
| New cut/recovery behavioral + source-contract cases | PASS (included in Core 7280) |
| Focused ordinary/structural/resize suites | PASS |
| Full Core | 7280 PASS, 0 failed/skipped |
| Full WPF | 826 PASS, 0 failed/skipped |
| Debug x64 WAE | PASS, 0 warnings / 0 errors |
| Release x64 WAE | PASS, 0 warnings / 0 errors |
| Portable Compatibility Gate | PASS; 7280 Core |
| Full Compatibility Gate | PASS; 7280 Core + 826 WPF = 8106 |
| `git diff --check` | PASS (LF/CRLF notices only) |
| HOST | NOT RUN — request one fresh-roof sequence below |

### Minimal HOST retest (after CODE-SIDE PASS)

Fresh Unlocked Hip, Physical3D on, `AK_ROOF_3D_TRACE` enabled:

1. MOVE one Generated Ordinary outside footprint.
2. STRETCH its endpoint; confirm 3D follows.
3. One source-roof GRIP_STRETCH — must rebuild on first completed grip; no `OrdinaryCutNotOnStructuralSide`; override survives; structural rebuilds; `AK_ROOF_3D_AUDIT`.
4. Without reload: MOVE another automatic ordinary; GRIP_STRETCH another; another valid source GRIP_STRETCH; `AK_ROOF_3D_AUDIT`.
5. If any failure occurs, preserve the first failure log; do not repeat the grip.

### FINAL VERDICT (superseded by HOST follow-up below)

- CUT ROOT CAUSE: FOUND ✅
- CUT FIX: PASS ✅ (code-side)
- ROLLBACK ATOMICITY: PASS ✅ (code-side) — **HOST disproved; see follow-up**
- POST-FAILURE LIFECYCLE LIVENESS: PASS ✅ (code-side; Core injection + AutoCAD finalization seam)
- CODE-SIDE OVERALL: PASS ✅ — **HOST follow-up required true rollback**
- HOST: NOT RUN (then ran; false recovery)

## HOST failure follow-up — true atomic rollback + HardFailure detail + grip orientation

Date: 2026-10-03 (same WIP tree; no commit/push/tag). HOST disproved the prior rollback verdict:

- `ROOF_RESIZE_FAILURE_RECOVERY` reported `result=ok` with `groupCanonical=False`
- `RigidFootprint.Edge12LengthMm` 6000 → 7138.038… after “recovery”
- GROUP members 186 → 182 (four structural physical members dropped)

### Root defects fixed (code-side)

1. **False-positive recovery verdict** — `IsRecoveryVerdictOk` now requires all of: `dbRollback`, `runtimeReset`, `pendingResize=false`, `suppressionDepth=0`, `activeOwner` empty, `groupCanonical=true`, `sourceGeometryRestored`, `roofDefinitionRestored`, `physicalInventoryRestored`, `annotationsRestored`, `nextCommandReady`.
2. **Incomplete aggregate restore** — `TryRestoreSupportedResizeFailureAggregate` writes snapshotted `RoofDefinition` (RigidFootprint/overrides), verifies PRE/POST source geometry + definition + Physical3D inventory + annotations + GROUP canonical, and **does not** run destructive structural Physical3D reconcile (that previously erased four Hip solids after abort already restored them).
3. **HardFailure diagnostic gap** — every HardFailure path goes through `HardFailureAt` and emits `ROOF_RESIZE_HARDFAILURE_DETAIL` (`owner`, `stage`, `member`, `result`, `exception`, `transactionState`). Stages cover generated Plan rebuild, ordinary/structural Physical3D, purlins, GROUP, boundary identity, display, etc. UI key may still be generic `Command_RoofRafters_GenerationFailed`; the marker identifies the seam.
4. **Grip Plan-vs-Physical3D orientation** — Core proof via `RoofOrdinaryPhysicalPlanFrameRules`: after MOVE + lateral endpoint GRIP, Plan acquires `RotationRadians` and Physical3D longitudinal/section axes follow Plan (**hypothesis A**); longitudinal GRIP keeps rotation ≈ 0 and faithful frame. No extra Physical3D yaw relative to Plan.

### Tests A–N (Core)

A–H exact PRE/POST restore tokens; I recovery fail if `groupCanonical=false`; J–L post-failure Ordinary MOVE / Ordinary GRIP / next source GRIP; M lateral grip Plan↔3D; N longitudinal grip no unexpected rotation. Plus source-contract updates for `HardFailureAt`, definition write only on SupportedResize hard-failure restore, and recovery equality fields.

### Validation (follow-up)

| Check | Result |
| --- | --- |
| Focused rollback / resize / ordinary / structural suites | PASS (226 focused) |
| Full Core | 7284 PASS, 0 failed/skipped |
| Full WPF | 826 PASS, 0 failed/skipped |
| Debug x64 WAE | PASS, 0 warnings / 0 errors |
| Release x64 WAE | PASS, 0 warnings / 0 errors |
| Portable Compatibility Gate | PASS; 7284 Core |
| Full Compatibility Gate | PASS; 7284 Core + 826 WPF = 8110 |
| `git diff --check` | PASS (LF/CRLF notices only) |

### Minimal HOST sequence (one only — after this CODE-SIDE readiness)

Fresh Unlocked Hip, Physical3D on, `AK_ROOF_3D_TRACE` enabled. Do not retry a failed grip; preserve the first failure log.

1. MOVE one Generated Ordinary outside footprint; confirm Plan vertical then GRIP_STRETCH (lateral and/or longitudinal as needed). Confirm `ROOF_RESIZE_HARDFAILURE_DETAIL` is absent on success; Plan axis XY matches Physical3D longitudinal XY (no extra yaw).
2. One valid source-roof GRIP_STRETCH. On any HardFailure: require `ROOF_RESIZE_HARDFAILURE_DETAIL` with non-empty `stage`; `ROOF_RESIZE_FAILURE_RECOVERY` must have `result=fail` unless `groupCanonical=true` and Edge12 restored to pre-command (e.g. 6000) and GROUP member count restored.
3. Immediately without reload: Ordinary MOVE, Ordinary GRIP_STRETCH, next valid source GRIP_STRETCH, `AK_ROOF_3D_AUDIT`.

### FOLLOW-UP FINAL VERDICT (superseded by Grok forensic narrow fix below)

- CURRENT HARDFAILURE ROOT CAUSE: OPEN 🟡 → see forensic section
- GRIP PLAN-vs-3D ORIENTATION: PASS ✅ (code-side; Plan rotation followed by Physical3D, no extra yaw)
- ROLLBACK PRE/POST EQUALITY: PASS ✅ (code-side; definition + source + inventory + GROUP)
- GROUP RECOVERY: PASS ✅ (code-side; no destructive structural physical reconcile; canonical required for `ok`)
- POST-FAILURE LIVENESS: PASS ✅ (code-side)
- CODE-SIDE OVERALL: PARTIAL 🟡 → see forensic section
- HOST: READY TO REQUEST ONE SEQUENCE (instrumentation + rollback equality in place)

## Grok forensic narrow fix — generated-plan-rebuild + ElementId recovery

Date: 2026-10-03. Source: `KROVY_GRIP_RESIZE_FORENSIC_AUDIT_2026-10-03.md`. No commit/push/tag. Ordinary grip frame and pure MOVE roof-plane semantics unchanged.

### Exact generated-plan-rebuild failure

FOUND class/method (forensic):

1. `ROOF_GENERATED_POST_ATOMIC_SUMMARY result=ok` is emitted at the end of `TimberSourceLineCreationService.Create`, still inside `ReplacePreparedSetWithRecipe`'s try.
2. The next throwing path is `RoofOrdinaryRafterSolidMaterializationService.ReconcileInTransaction` (`InvalidOperationException` from TryBuild / GeneratedMemberKeyMismatch / `CreateSolid` Region failure).
3. The catch collapsed that to bare `ReplacementOutcome.Failed`, so `HardFailureAt` logged `member=- exception=-`.

Host-specific CreateSolid failure mode fixed: when AutoCAD `Region.CreateFromCurves` rejects the source-prism side profile after Plan2D already succeeded, `CreateSolid` now falls back to Core's already-clipped `SolidVertices` without re-slicing. CreateSolid failures include `member=` / plan endpoints in the exception text.

### Changed methods / types

| Location | Change |
| --- | --- |
| `RoofGeneratedPlanRebuildFailureRules` | Formats `ROOF_GENERATED_PLAN_REBUILD_FAILURE` detail (owner/substage/member/exception) |
| `RoofGeneratedRafterSetService.ReplacePreparedSetWithRecipe` / `MaterializeCore` | Preserve exception via `LastFailureDetail`; wrap Reconcile as `ordinary-physical-reconcile`; DEBUG inject after post-atomic |
| `RoofLiveResizeService.HardFailureAt` / Failed branch | Propagate substage + exception token from `LastFailureDetail` |
| `RoofOrdinaryRafterSolidMaterializationService.CreateSolid` | Source-prism Region failure → SolidVertices fallback; member-keyed errors |
| `RoofAutomaticRafterPhysicalBuilder.TryBuild` | Specific `failureReason` tokens; LogicalKey equality instead of rafter reference |
| `RoofUnsupportedStretchRecoveryRules.IsRecoverableGeneratedElementIdSeriesMismatch` | NK4/NK5 same-handle series drift is recoverable |
| `TryProbeAssemblyMembers` / `TryRestoreTimberLines` | Do not abort aggregate restore on recoverable ElementId drift; rewrite snapshotted ElementId on restore |

### ElementId recovery root cause

Snapshot at CommandWillStart stores `NK5`. Failed resize txn renumbers live XData to `NK4`, then aborts without Commit. Probe compared series ElementId and returned false **before** `RestorePolylineGeometry`, leaving the native stretch. Fix: same-handle Line + metadata mismatch is recoverable; restore writes the snapshotted ElementId back.

### OPEN — pure MOVE roof-plane (do not fix here)

`TranslateMember` shifts XY without changing Z. On a sloped roof the physical upper face can leave the roof plane. Lateral GRIP path A rebuilds from Plan+normal and is not a roll accumulator. Authoritative MOVE contract deferred.

### Validation (forensic narrow fix)

| Check | Result |
| --- | --- |
| Focused propagation / ElementId / rollback / ordinary / structural | PASS |
| Full Core | 7288 PASS |
| Full WPF | 826 PASS |
| Debug / Release x64 WAE | PASS, 0 warnings / 0 errors |
| Portable / Full Compatibility Gate | PASS; 7288 + 826 = 8114 |
| `git diff --check` | PASS (LF/CRLF notices only) |

### FORENSIC FINAL VERDICT

- GENERATED PLAN REBUILD EXACT FAILURE: FOUND ✅ (post-Create `ReconcileInTransaction` / `CreateSolid`; exception no longer swallowed)
- GENERATED PLAN REBUILD FIX: PASS ✅ (CreateSolid SolidVertices fallback + failure propagation)
- ELEMENTID RECOVERY FIX: PASS ✅
- ROLLBACK PRE/POST: PASS ✅
- POST-FAILURE LIVENESS: PASS ✅
- PURE MOVE ROOF-PLANE ISSUE: OPEN 🟡
- CODE-SIDE OVERALL: PASS ✅
- HOST: one sequence ready — expect `ROOF_GENERATED_PLAN_REBUILD_FAILURE` / `ROOF_RESIZE_HARDFAILURE_DETAIL` with `substage=` on any rebuild failure; recovery must not stop on NK4/NK5 alone

## Shared-node structural target ambiguity (narrow fix)

Date: 2026-10-03. Source: `KROVY_STRUCTURAL_TARGET_AMBIGUOUS_AUDIT_2026-10-03.md`. No commit/push/tag.

### Root cause

HOST `StructuralCut:StructuralTargetAmbiguous` during `ordinary-physical-reconcile` for apex ordinary rafters whose plan end equals the common topology node of two Hip edges of the same face. `RoofStructuralSidePlaneResolver.TryResolve` treated every closed-segment hit (including endpoints) as a candidate and required exactly one. Shared-node endpoint contacts are not a unique side plane and must not fail closed.

### New resolver contract

| Case | Rule |
| --- | --- |
| Exactly one **Interior** contact | Resolve that Hip/Valley side plane |
| Multiple Interior contacts | `StructuralTargetAmbiguous` |
| Exactly one Endpoint contact | Existing side-plane resolve |
| Multiple Endpoint contacts at the **same topology node** | Success with `StructuralCut = null`, reason `StructuralSharedNodeNoSideCut` |
| Multiple Endpoint contacts at different nodes | `StructuralTargetAmbiguous` |

Interior contact has priority over endpoint-only candidates. No nearest-edge / enumeration-order / logical-key tie-break. Ridge semantics unchanged.

### Changed code / tests

- `RoofStructuralSidePlaneResolver.TryResolve` — Interior vs Endpoint classification + shared-node result
- `AsymmetricHip_Non45StructuralEnd_UsesPlaneNotLengthSubtraction` — full TryBuild must succeed; apex leaves cut unset; non-45 plane assertions retained
- Added rectangular Hip apex build, resize-layout reconcile, and resolver tests A/B/C/F/G

### Validation

| Check | Result |
| --- | --- |
| Focused resolver + physical builder + rollback/ordinary | PASS — 120 / 0 fail |
| Full Core | PASS — 7295 / 0 fail / 0 warn |
| Full WPF | PASS — 826 / 0 fail / 0 warn |
| Debug x64 WAE | PASS — 0 Warning(s), 0 Error(s) |
| Release x64 WAE | PASS — 0 Warning(s), 0 Error(s) |
| Portable Gate | PASSED |
| Full Gate | PASSED (Core 7295 + WPF 826) |
| `git diff --check` | PASS (CRLF notices only; no whitespace errors) |

### SHARED-NODE FINAL VERDICT

- SHARED-NODE TARGET RULE: PASS ✅
- TRUE AMBIGUITY FAIL-CLOSED: PASS ✅
- ORDINARY PHYSICAL BUILD: PASS ✅
- ROOF RESIZE REGRESSION: PASS ✅ (code-side)
- ROLLBACK REGRESSION: PASS ✅ (focused)
- PURE MOVE ROOF-PLANE ISSUE: OPEN 🟡 (out of scope)
- CODE-SIDE OVERALL: PASS ✅

## Ordinary Generated GRIP_STRETCH axis constraint (narrow fix)

Date: 2026-10-03. No commit/push/tag. Preserves WIP.

### Root cause

Unlocked Generated Ordinary endpoint `GRIP_STRETCH` accepted raw native Line geometry. When the user dragged an endpoint diagonally, `TryClassifyCollinearEndpointEdit` rejected the lateral component and the classifier fell through to rigid / `TryClassify`, which persisted non-zero `RotationRadians` and rebuilt Physical3D with yaw.

### Product contract

Endpoint GRIP_STRETCH is axis-constrained to the PRE-COMMAND longitudinal Plan axis:

- accept only the longitudinal projection of the native endpoint delta
- discard lateral / perpendicular cursor motion
- Start grip → only `StartOffsetMm`; End grip → only `EndOffsetMm`
- never introduce or recompute `RotationRadians` from GRIP_STRETCH
- preserve prior MOVE translation (`AlongMm` / `LateralMm`) and any pre-existing rotation from a separate accepted rotate mechanism

### Implementation

- Core: `RoofGeneratedMemberOverrideMath.TryClassifyAxisConstrainedEndpointGrip`
- Host seam: `TryClassifyAcceptedMemberEdit` grip branch uses axis constraint (+ pure-translation fallback); no rigid / freeform rotation fallback for `isGrip`
- Classic STRETCH freeform path unchanged
- DEBUG: `ROOF_ORDINARY_GRIP_CONSTRAINT` via `WriteOrdinaryGripConstraint`

### Tests

`RoofOrdinaryGripAxisConstraintTests` A–I (along, diagonal, start, MOVE→grip, repeated grip, large lateral, pure lateral no-op, source contract, continuity) plus preserve-existing-rotation case.

### Validation

| Check | Result |
| --- | --- |
| Focused grip/override/physical/recovery | PASS — 87 / 0 fail |
| Full Core | PASS — 7305 / 0 fail / 0 warn |
| Full WPF | PASS — 826 / 0 fail / 0 warn |
| Debug x64 WAE | PASS — 0 Warning(s), 0 Error(s) |
| Release x64 WAE | PASS — 0 Warning(s), 0 Error(s) |
| Portable Gate | PASSED |
| Full Gate | PASSED (Core 7305 + WPF 826) |
| `git diff --check` | PASS (CRLF notices only; no whitespace errors) |

### ORDINARY GRIP FINAL VERDICT

- ORDINARY GRIP AXIS CONSTRAINT: PASS ✅
- ROTATIONRADIANS PRESERVED: PASS ✅
- MOVE -> GRIP CONTINUITY: PASS ✅
- PHYSICAL3D NO-YAW: PASS ✅
- REPEATED GRIP DRIFT: PASS ✅
- TWO-HIP ORDINARY CUT AT GABLE/JUNCTION: OPEN 🟡
- PURE MOVE ROOF-PLANE ISSUE: OPEN 🟡
- CODE-SIDE OVERALL: PASS ✅

## Initial dirty inventory and final git status

The following snapshot sections are appended from actual inventory and `git status --short` output.

### Initial expanded files

- .ai/handoffs/ak-roof-purlins-final-ui-2026-09-19/A-CURRENT-STATE-before.png
- .ai/handoffs/ak-roof-purlins-final-ui-2026-09-19/B-TARGET-UI-after.jpg
- .ai/handoffs/ak-roof-purlins-final-ui-2026-09-19/C-krov-master-scene-alt.png
- .ai/handoffs/ak-roof-purlins-final-ui-2026-09-19/C-krov-master-scene.png
- .ai/handoffs/ak-roof-purlins-final-ui-2026-09-19/krov.svg
- .ai/handoffs/ak-roof-purlins-final-ui-2026-09-19/PROMPT.txt
- .ai/handoffs/ak-roof-purlins-final-ui-2026-09-19/prvky_0.png
- .ai/handoffs/ak-roof-purlins-final-ui-2026-09-19/prvky_100.png
- .ai/handoffs/ak-roof-purlins-final-ui-2026-09-19/README.md
- .cursor/rules/acad-build-lock.mdc
- docs/ACAD_HOST_WORKFLOW.md
- docs/ATTACHED_MANUAL_ANCHOR_STABILITY_FIX_2026-10-02.md
- docs/ATTACHEDMANUAL_PHYSICAL_LIFECYCLE_2026-09-30.md
- docs/HEAVY_LIFECYCLE_AUDIT_2026-09-30.md
- docs/MANUAL_STRUCTURAL_COPY_PLACEMENT_FIX_2026-10-02.md
- docs/MEMBER_BREAK_COPY_MIRROR_AUDIT_2026-09-30.md
- docs/MIXED_PHYSICAL_STRETCH_FIX_2026-09-30.md
- docs/NATIVE_COPY_LIFECYCLE_FIX_2026-09-30.md
- docs/NATIVE_MEMBER_REGRESSION_FIX_2026-10-01.md
- docs/NATIVE_STRETCH_ROUTING_FIX_2026-09-30.md
- docs/ORDINARY_GENERATED_MOVE_PHYSICAL3D_FIX_2026-10-02.md
- docs/PHYSICAL_3D_CLONE_OWNERSHIP_DIAGNOSIS_2026-09-26.md
- docs/PHYSICAL_3D_EAVES_RETEST_2026-09-26.md
- docs/PHYSICAL_3D_LOCKED_ERASE_FIX_2026-09-26.md
- docs/PHYSICAL_3D_NATIVE_CLONE_FIX_2026-09-26.md
- ErrorReports/9d6767a77b9d2cce90e03082e1302957689eeac5/cer.log
- ErrorReports/GroupUneraseProbe/ErrorReports/9d6767a77b9d2cce90e03082e1302957689eeac5/cer.log
- ErrorReports/Physical3DCloneProbe/copy-before-core.log
- ErrorReports/Physical3DCloneProbe/copy-before-db.log
- ErrorReports/Physical3DCloneProbe/copy-before-input.log
- ErrorReports/Physical3DCloneProbe/copy-before-native.log
- ErrorReports/Physical3DCloneProbe/copy-before.log
- ErrorReports/Physical3DCloneProbe/copy-before.scr
- ErrorReports/Physical3DCloneProbe/ErrorReports/9d6767a77b9d2cce90e03082e1302957689eeac5/cer.log
- ErrorReports/Physical3DCloneProbe/PLAN.md
- ErrorReports/Physical3DCloneProbe/probe-steps.txt
- ErrorReports/Physical3DCloneProbe/Probe.cs
- ErrorReports/Physical3DCloneProbe/Probe.csproj
- ChatGPT Image 23. 8. 2026, 11_10_39.png
- ChatGPT Image 23. 8. 2026, 11_17_14.png
- KROVY_roof_icons_v1_preview.png
- scripts/acad-host-workflow.ps1
- src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualCopyCloneReinitializeService.cs
- src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualLifecycleService.cs
- src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedRafterCopyOwnershipRehydrationService.cs
- src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryRafterSolidMaterializationService.cs
- src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs
- src/AcKrovy.Core.Tests/AsymmetricGableRoofFoundationTests.cs
- src/AcKrovy.Core.Tests/MonopitchRafterStage2D1SourceContractTests.cs
- src/AcKrovy.Core.Tests/MonopitchRafterStage2D3Tests.cs
- src/AcKrovy.Core.Tests/MonopitchRafterStage2D4AClipboardTests.cs
- src/AcKrovy.Core.Tests/MonopitchRafterStage2D4BClipboardTests.cs
- src/AcKrovy.Core.Tests/MonopitchRoofStage1SourceContractTests.cs
- src/AcKrovy.Core.Tests/RoofAttachedManualAnchorStabilityTests.cs
- src/AcKrovy.Core.Tests/RoofAttachedManualCopyCloneSourceContractTests.cs
- src/AcKrovy.Core.Tests/RoofAttachedManualPhysicalLifecycleTests.cs
- src/AcKrovy.Core.Tests/RoofCopyReplayMetadataSourceContractTests.cs
- src/AcKrovy.Core.Tests/RoofGeometryDialogSourceContractTests.cs
- src/AcKrovy.Core.Tests/RoofOrdinaryGeneratedMovePhysicalTests.cs
- src/AcKrovy.Core.Tests/RoofSplitAttachedManualSourceContractTests.cs
- src/AcKrovy.Core.Tests/RoofStructuralManualCopyPlacementTests.cs
- src/AcKrovy.Core.Tests/SimpleGableOrdinaryPhysical3DTests.cs
- src/AcKrovy.Core/Models/Roofs/RoofAttachedManualTimberData.cs
- src/AcKrovy.Core/Models/Roofs/RoofAttachedManualTimberDataSchema.cs
- src/AcKrovy.Core/Services/Roofs/RoofAttachedManualPhysicalBuilder.cs
- src/AcKrovy.Core/Services/Roofs/RoofAttachedManualReanchorRules.cs
- src/AcKrovy.Core/Services/Roofs/RoofAttachedManualTimberDataCodec.cs
- src/AcKrovy.Core/Services/Roofs/RoofAutomaticRafterPhysicalBuilder.cs
- src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberReplayPlanner.cs
- src/AcKrovy.Core/Services/Roofs/RoofStructuralAttachedManualDataRules.cs
- src/AcKrovy.Core/Services/Roofs/SimpleGableOrdinaryRafterPhysicalAdapter.cs
- src/AcKrovy.Core/Services/Roofs/SimpleGableRoofTopologyAdapter.cs

### Initial git status --short

```text
 M src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualCopyCloneReinitializeService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualLifecycleService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedRafterCopyOwnershipRehydrationService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryRafterSolidMaterializationService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs
 M src/AcKrovy.Core.Tests/AsymmetricGableRoofFoundationTests.cs
 M src/AcKrovy.Core.Tests/MonopitchRafterStage2D1SourceContractTests.cs
 M src/AcKrovy.Core.Tests/MonopitchRafterStage2D3Tests.cs
 M src/AcKrovy.Core.Tests/MonopitchRafterStage2D4AClipboardTests.cs
 M src/AcKrovy.Core.Tests/MonopitchRafterStage2D4BClipboardTests.cs
 M src/AcKrovy.Core.Tests/MonopitchRoofStage1SourceContractTests.cs
 M src/AcKrovy.Core.Tests/RoofAttachedManualCopyCloneSourceContractTests.cs
 M src/AcKrovy.Core.Tests/RoofAttachedManualPhysicalLifecycleTests.cs
 M src/AcKrovy.Core.Tests/RoofCopyReplayMetadataSourceContractTests.cs
 M src/AcKrovy.Core.Tests/RoofGeometryDialogSourceContractTests.cs
 M src/AcKrovy.Core.Tests/RoofSplitAttachedManualSourceContractTests.cs
 M src/AcKrovy.Core/Models/Roofs/RoofAttachedManualTimberData.cs
 M src/AcKrovy.Core/Models/Roofs/RoofAttachedManualTimberDataSchema.cs
 M src/AcKrovy.Core/Services/Roofs/RoofAttachedManualPhysicalBuilder.cs
 M src/AcKrovy.Core/Services/Roofs/RoofAttachedManualReanchorRules.cs
 M src/AcKrovy.Core/Services/Roofs/RoofAttachedManualTimberDataCodec.cs
 M src/AcKrovy.Core/Services/Roofs/RoofAutomaticRafterPhysicalBuilder.cs
 M src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberReplayPlanner.cs
 M src/AcKrovy.Core/Services/Roofs/RoofStructuralAttachedManualDataRules.cs
?? .ai/handoffs/
?? .cursor/rules/acad-build-lock.mdc
?? "ChatGPT Image 23. 8. 2026, 11_10_39.png"
?? "ChatGPT Image 23. 8. 2026, 11_17_14.png"
?? ErrorReports/9d6767a77b9d2cce90e03082e1302957689eeac5/
?? ErrorReports/GroupUneraseProbe/ErrorReports/
?? ErrorReports/Physical3DCloneProbe/
?? KROVY_roof_icons_v1_preview.png
?? docs/ACAD_HOST_WORKFLOW.md
?? docs/ATTACHEDMANUAL_PHYSICAL_LIFECYCLE_2026-09-30.md
?? docs/ATTACHED_MANUAL_ANCHOR_STABILITY_FIX_2026-10-02.md
?? docs/HEAVY_LIFECYCLE_AUDIT_2026-09-30.md
?? docs/MANUAL_STRUCTURAL_COPY_PLACEMENT_FIX_2026-10-02.md
?? docs/MEMBER_BREAK_COPY_MIRROR_AUDIT_2026-09-30.md
?? docs/MIXED_PHYSICAL_STRETCH_FIX_2026-09-30.md
?? docs/NATIVE_COPY_LIFECYCLE_FIX_2026-09-30.md
?? docs/NATIVE_MEMBER_REGRESSION_FIX_2026-10-01.md
?? docs/NATIVE_STRETCH_ROUTING_FIX_2026-09-30.md
?? docs/ORDINARY_GENERATED_MOVE_PHYSICAL3D_FIX_2026-10-02.md
?? docs/PHYSICAL_3D_CLONE_OWNERSHIP_DIAGNOSIS_2026-09-26.md
?? docs/PHYSICAL_3D_EAVES_RETEST_2026-09-26.md
?? docs/PHYSICAL_3D_LOCKED_ERASE_FIX_2026-09-26.md
?? docs/PHYSICAL_3D_NATIVE_CLONE_FIX_2026-09-26.md
?? scripts/acad-host-workflow.ps1
?? src/AcKrovy.Core.Tests/RoofAttachedManualAnchorStabilityTests.cs
?? src/AcKrovy.Core.Tests/RoofOrdinaryGeneratedMovePhysicalTests.cs
?? src/AcKrovy.Core.Tests/RoofStructuralManualCopyPlacementTests.cs
?? src/AcKrovy.Core.Tests/SimpleGableOrdinaryPhysical3DTests.cs
?? src/AcKrovy.Core/Services/Roofs/SimpleGableOrdinaryRafterPhysicalAdapter.cs
?? src/AcKrovy.Core/Services/Roofs/SimpleGableRoofTopologyAdapter.cs
```

### Final git status --short

```text
 M src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualCopyCloneReinitializeService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualLifecycleService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedRafterCopyOwnershipRehydrationService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedRafterSetService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofLiveResizeService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryRafterSolidMaterializationService.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofRafterCommandWorkflow.cs
 M src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralNativeEditService.cs
 M src/AcKrovy.Core.Tests/AsymmetricGableRoofFoundationTests.cs
 M src/AcKrovy.Core.Tests/MonopitchRafterStage2D1SourceContractTests.cs
 M src/AcKrovy.Core.Tests/MonopitchRafterStage2D3Tests.cs
 M src/AcKrovy.Core.Tests/MonopitchRafterStage2D4AClipboardTests.cs
 M src/AcKrovy.Core.Tests/MonopitchRafterStage2D4BClipboardTests.cs
 M src/AcKrovy.Core.Tests/MonopitchRoofStage1SourceContractTests.cs
 M src/AcKrovy.Core.Tests/RoofAttachedManualCopyCloneSourceContractTests.cs
 M src/AcKrovy.Core.Tests/RoofAttachedManualPhysicalLifecycleTests.cs
 M src/AcKrovy.Core.Tests/RoofCopyReplayMetadataSourceContractTests.cs
 M src/AcKrovy.Core.Tests/RoofGeometryDialogSourceContractTests.cs
 M src/AcKrovy.Core.Tests/RoofSplitAttachedManualSourceContractTests.cs
 M src/AcKrovy.Core/Models/Roofs/RoofAttachedManualTimberData.cs
 M src/AcKrovy.Core/Models/Roofs/RoofAttachedManualTimberDataSchema.cs
 M src/AcKrovy.Core/Services/Roofs/RoofAttachedManualPhysicalBuilder.cs
 M src/AcKrovy.Core/Services/Roofs/RoofAttachedManualReanchorRules.cs
 M src/AcKrovy.Core/Services/Roofs/RoofAttachedManualTimberDataCodec.cs
 M src/AcKrovy.Core/Services/Roofs/RoofAutomaticRafterPhysicalBuilder.cs
 M src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberReplayPlanner.cs
 M src/AcKrovy.Core/Services/Roofs/RoofStructuralAttachedManualDataRules.cs
?? .ai/handoffs/
?? .cursor/rules/acad-build-lock.mdc
?? "ChatGPT Image 23. 8. 2026, 11_10_39.png"
?? "ChatGPT Image 23. 8. 2026, 11_17_14.png"
?? ErrorReports/9d6767a77b9d2cce90e03082e1302957689eeac5/
?? ErrorReports/GroupUneraseProbe/ErrorReports/
?? ErrorReports/Physical3DCloneProbe/
?? KROVY_roof_icons_v1_preview.png
?? docs/ACAD_HOST_WORKFLOW.md
?? docs/ATTACHEDMANUAL_PHYSICAL_LIFECYCLE_2026-09-30.md
?? docs/ATTACHED_MANUAL_ANCHOR_STABILITY_FIX_2026-10-02.md
?? docs/HEAVY_LIFECYCLE_AUDIT_2026-09-30.md
?? docs/MANUAL_STRUCTURAL_COPY_PLACEMENT_FIX_2026-10-02.md
?? docs/MEMBER_BREAK_COPY_MIRROR_AUDIT_2026-09-30.md
?? docs/MIXED_PHYSICAL_STRETCH_FIX_2026-09-30.md
?? docs/NATIVE_COPY_LIFECYCLE_FIX_2026-09-30.md
?? docs/NATIVE_MEMBER_REGRESSION_FIX_2026-10-01.md
?? docs/NATIVE_STRETCH_ROUTING_FIX_2026-09-30.md
?? docs/ORDINARY_GENERATED_MOVE_PHYSICAL3D_FIX_2026-10-02.md
?? docs/ORDINARY_OVERRIDE_CONTINUITY_AND_ROOF_GRIP_FIX_2026-10-02.md
?? docs/PHYSICAL_3D_CLONE_OWNERSHIP_DIAGNOSIS_2026-09-26.md
?? docs/PHYSICAL_3D_EAVES_RETEST_2026-09-26.md
?? docs/PHYSICAL_3D_LOCKED_ERASE_FIX_2026-09-26.md
?? docs/PHYSICAL_3D_NATIVE_CLONE_FIX_2026-09-26.md
?? scripts/acad-host-workflow.ps1
?? src/AcKrovy.Core.Tests/RoofAttachedManualAnchorStabilityTests.cs
?? src/AcKrovy.Core.Tests/RoofOrdinaryGeneratedMovePhysicalTests.cs
?? src/AcKrovy.Core.Tests/RoofOrdinaryOverrideContinuityTests.cs
?? src/AcKrovy.Core.Tests/RoofStructuralManualCopyPlacementTests.cs
?? src/AcKrovy.Core.Tests/SimpleGableOrdinaryPhysical3DTests.cs
?? src/AcKrovy.Core/Services/Roofs/RoofAcceptedOrdinaryOverrideReplayRules.cs
?? src/AcKrovy.Core/Services/Roofs/SimpleGableOrdinaryRafterPhysicalAdapter.cs
?? src/AcKrovy.Core/Services/Roofs/SimpleGableRoofTopologyAdapter.cs
```
