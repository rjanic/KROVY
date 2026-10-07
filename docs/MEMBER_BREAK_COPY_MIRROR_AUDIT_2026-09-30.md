# BREAK / COPY / MIRROR member lifecycle audit — 2026-09-30

## 1. STATUS

**PARTIAL — audit and DEBUG evidence checkpoint; requested Physical3D lifecycle package is NOT implemented.**

Baseline: `4f03788f7afb6f26e95dceff52e6e960e088706d`, branch `main`. No commit, push, tag, release, version/schema bump, or production lifecycle change. Existing untracked WIP was preserved.

Automated checks validate the diagnostic change and existing regressions. They do not prove native command ordering, new split/clone physical behavior, or AutoCAD undo/save/reopen. HOST validation: **NOT RUN**. Release verdict: **NOT READY FOR HOST-AFFECTING CHANGE**.

## 2. Native command audit

`LiveGeometrySynchronizationService.Tracker` subscribes to document command start/end/cancel/failure and database append/modify/erase/deep-clone events. Native callbacks collect candidates; successful command completion runs maintenance. Undo/redo boundaries clear pending state without opening the database for maintenance.

`RoofNativeCloneSnapshot` captures roof-owned entities and records `IdMapping` pairs. `GetCompleteClones` recognizes complete roof assemblies for `RoofWholeRoofCopyRebindService`; it is not a partial-member identity resolver. Multiple mapping batches exist for whole-roof COPY. A native map is evidence only when AutoCAD actually emits it; missing mappings must not be replaced by callback-order or nearest-geometry guesses.

Repository workflow documents older **2D** HOST observations:

| Command | Existing documented behavior | Still required for this package |
| --- | --- | --- |
| BREAK | Surviving Generated fragment retains its identity; extra fragment becomes AttachedManual Split. | Single-point vs two-point event sequence with Physical3D enabled; retained object, appended fragment and final geometry. |
| COPY | Generated clone can be promoted to AttachedManual Copy. | Exact source/clone pairs, repeated placements, collateral solid/annotation clones, group membership. |
| MIRROR No | Appended Generated/Attached clone is promoted/reinitialized as AttachedManual Copy. | Current member-level native mapping and mixed selection with derived solids. |
| MIRROR Yes | May modify the same Line in place: no appended clone or erased source. Existing 2D implementation then promotes/suppresses. | Current physical-enabled in-place or clone+erase variant and unambiguous replacement pairing. |

These are repository-documented observations, not a new HOST run by this agent. Baseline STRETCH/MOVE/GRIP HOST evidence does not prove this new package. No usable new member-level BREAK/COPY/MIRROR Physical3D trace was found in the supplied evidence.

## 3. Shared lifecycle mechanism

Reuse the existing tracker, command snapshots, generated semantic acceptance, physical materialization, transaction recovery and canonical group sync. Do not add another event or undo framework.

Current completion flow includes complete-roof clone rebinding, `RoofLiveResizeService`, generic timber refresh, `RoofAttachedManualCopyCloneReinitializeService`, `RoofGeneratedRafterCopyOwnershipRehydrationService`, and `RoofMirrorCloneDetachService`. Several clone services have their own transactions. A future generalized member operation must reconcile semantic identity and materialization atomically rather than append a physical pass after independently committed promotions.

`RoofUnsupportedStretchRecoverySnapshotService` covers generated edit commands including BREAK; COPY/MIRROR use their existing copy/native-clone captures instead. Current ordinary physical reconciliation excludes the cardinality-changing BREAK/COPY/MIRROR paths. Merely enabling BREAK in the command predicate would leave its new AttachedManual fragment without a physical representation.

## 4. Identity policy

| Operation | Required policy | Current implementation |
| --- | --- | --- |
| MOVE/TRIM/STRETCH/GRIP | Same logical identity. | Baseline semantic geometry override path; unchanged. |
| ERASE | Existing suppression/removal policy. | Baseline path; unchanged. |
| BREAK | One retained identity plus one independent persistent identity. | Generated retained fragment plus AttachedManual Split in 2D. |
| COPY | Source retained, independent clone identity. | AttachedManual Copy promotion in 2D. |
| MIRROR No | Source retained, independent mirrored clone identity. | AttachedManual Copy promotion/reinitialization in 2D. |
| MIRROR Yes | Same logical Generated MemberKey for a clear replacement. | Existing 2D conversion to AttachedManual plus original-slot suppression differs from this request. |

`RoofGeneratedMemberKey` is kind/face/station. Ordinary physical `StructuralId` uses the existing key serialization. AttachedManual has `ChildIdentity`, exact Generated anchor and relative endpoints. Current creation commonly initializes ChildIdentity from the native child handle; this must not silently be presented as the requested handle-independent domain identity policy. No new identity system or handle-based production pairing was added.

`ElementId`/`ReservedElementId` follow manufacturing signature and stable numbering rules. Compatible members can share an item number; a new clone does not imply globally unique manufacturing numbering. Do not use ElementId to correlate ambiguous clones or force a global renumber.

## 5. BREAK implementation

Existing `RoofGeneratedMemberManualEditService.TryAcceptUnlockedEdits`, split rules and `TryAttachManualSplitFragment` already implement the 2D Generated/Attached split. Their retention and role-sensitive split logic was audited and regression-tested, not changed.

**New two-member physical materialization, atomic failure recovery, deterministic redo and physical persistence are not implemented.** The builder has no AttachedManual physical input/identity. New interior fragment endpoints also need an explicit audit of how existing endpoint/cut rules apply; no new physical cut policy was invented.

## 6. COPY implementation

Existing Generated copy ownership rehydration and AttachedManual clone reinitialization remain unchanged. They persist a new child with final relative geometry and refresh numbering/presentation.

**New canonical clone Physical3D and collateral solid cleanup for partial member COPY are not implemented.** Exact source/clone correlation for multiple native placements and an atomic semantic/physical transaction remain required.

## 7. MIRROR No implementation

Existing role-sensitive promotion, compatible anchor discovery and cloned-annotation cleanup remain unchanged. Canonical annotations are rebuilt from final timber geometry using existing readability rules.

**New mirrored AttachedManual Physical3D is not implemented.** Endpoint orientation and actual roof face/elevation must follow existing geometry authority, not a reflected solid or a guessed source face.

## 8. MIRROR Yes implementation

No identity policy change was made. Existing in-place and appended-clone handling remains as audited. The new request takes precedence over the workflow's older promotion policy for eventual implementation.

**Requested same-Generated-key replacement, old-physical removal and canonical physical rebuild are not implemented.** Do not assume that “Delete source = Yes” necessarily erases the native Line; logical replacement can be in place. Current HOST pairing must distinguish this from clone+erase before persistence/undo behavior changes.

## 9. AttachedManual / ManualOverride decision

ManualOverride expresses geometry/suppression for one generated slot; it cannot give two children the same key after BREAK. Existing AttachedManual is the relevant secondary semantic model, with Copy/Split origin and relative geometry; a parallel model is unnecessary.

`RoofOrdinaryRafterSolidMaterializationService.TryBuildExistingModelInTransaction` and `RoofAutomaticRafterPhysicalBuilder` currently consume generated layout, semantic replay, topology and physical settings. Physical role metadata has OrdinaryRafterSolid and StructuralRafterSolid, but no AttachedManual identity/builder path. Extending this contract is necessary for independent split/copy members, after native behavior is proven. No schema migration was attempted.

AttachedManual resize replay uses its exact stored anchor; missing anchor/outside footprint causes dormancy. Creation/reposition may select a compatible anchor; resize does not nearest-reanchor. Copy/Split remain distinct. Direct derived recovery, visibility and canonical physical keys must cover any future AttachedManual physical extension.

## 10. Undo/redo behavior

Existing grouped undo marks and zero-database undo/redo hooks remain unchanged. The new diagnostic command scope only activates for BREAK/COPY/MIRROR, clears on cancel/failure/disposal and cannot repair state during U/UNDO/REDO/MREDO.

New physical split/clone/replacement undo and redo are **NOT VERIFIED**. Independent current clone-service commits cannot simply be declared an atomic generalized operation.

## 11. Persistence behavior

Existing roof definition ManualOverrides and AttachedManual XData codec persist the baseline 2D semantic state. Existing codec/relative replay tests were run.

No new physical lifecycle save/reopen roundtrip tests were added or claimed. DWG save/reopen of the requested four operations remains **NOT VERIFIED**. Codec tests are not a substitute for canonical derived materialization after reopen.

## 12. GROUP / Physical3D invariants

`RoofAssemblyGroupService` and the existing owned-entity collector already include Generated, AttachedManual, annotations and owned physical entities. Reuse their canonical sync. The baseline direct-3D recovery and whole-roof clone lifecycle are unchanged.

The diagnostic now reports source roof geometry/definition, member keys and ElementIds, physical handles with role/StructuralId metadata, unique physical keys, group dictionary identity, source slots and raw/unique members before native execution, at native completion and after maintenance. It does not assert a canonical expected count from an unimplemented semantic model, and does not repair anything. Counts are derived from actual entities; no fixture counts are hardcoded.

## 13. Exact changed files

- `src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs`: DEBUG-only automatic BREAK/COPY/MIRROR member snapshots; exact mapping records; unresolved/ambiguous pairing labels; native/final comparison and owner/physical/group evidence. Full trace stays opt-in. All entity access is read-only.
- `src/AcKrovy.Core.Tests/RoofPhysical3DHostDiagnosticsSourceContractTests.cs`: two new diagnostic isolation/correlation contracts; existing diagnostic assertions updated.
- `docs/MEMBER_BREAK_COPY_MIRROR_AUDIT_2026-09-30.md`: this audit, validation and HOST request.

No production semantic action labels such as split-new/mirror-rebind were invented for diagnostics: the new trace reports actual before/after state, not success of an unimplemented operation. Existing semantic diagnostics still report existing maintenance results.

## 14. Focused tests

**232/232 PASS**, including 6 diagnostic source contracts (2 new), existing split/AttachedManual codec, Generated split identity, mirror, native clone ownership, generated copy, mixed physical STRETCH and native STRETCH routing classes.

New tests prove diagnostic scope, cancel clearing, read-only isolation, exact mapping use and explicit unresolved/ambiguous evidence. They do not prove AutoCAD runtime mapping or the requested new physical lifecycle behavior. The requested new four-operation physical, cross-command, undo/redo and persistence test matrix is outstanding alongside implementation.

## 15–18. Automated validation

| Check | Final result |
| --- | --- |
| 15. Core | PASS — 6860/6860, 0 skipped; repeated in final Portable and Full gates. |
| 15. WPF | PASS — 806/806, 0 skipped; standalone and Full gate. |
| 16. Debug x64 | PASS — final Full gate rebuild, 0 warnings / 0 errors. Explicit Debug x64 build also passed before the final metadata logging addition. |
| 16. Release x64 | PASS — explicit final build after all source edits, 0 warnings / 0 errors. |
| 17. Portable Gate | PASS — final standalone run after all source edits; restore, builds, tests and leakage checks pass. |
| 17. Full Gate | PASS — after all source edits; restore, portable checks, AutoCAD-linked build and solution tests pass. |
| 18. git diff --check | PASS. |
| Branch / HEAD | main / 4f03788f7afb6f26e95dceff52e6e960e088706d. |
| Upstream | Local HEAD/main/origin/main unchanged; ahead/behind 0/0. No fetch/push performed. |
| Working tree before | Dirty from pre-existing untracked WIP, no tracked edits. |
| Working tree after | Two tracked diagnostic/test edits, this untracked report, and preserved prior untracked WIP. Nothing staged. |
| CAD API leakage | PASS — gate checks plus source search of four CAD-neutral projects; none changed. |
| Localization/resource impact | NONE — existing localization tests included; no resources edited. |
| Version/schema impact | NONE. |
| HOST validation | NOT RUN; new physical lifecycle package NOT READY. |

Gate logs: TEMP `acad-member-lifecycle-portable-final-20260930.log` and `acad-member-lifecycle-full-20260930.log`. Tracked diff: 2 files, +174/-9; this report is additional and untracked. Automated diagnostic/regression validation verdict: PASS. Requested feature completion verdict: PARTIAL / HOST EVIDENCE REQUIRED.

Initial Core sandbox run: 6859 passed / 1 failed, `SafeFileWriterTests.WriteAllBytes_CreatesAndAtomicallyReplacesDestinationWithoutTemporaryFile` raised UnauthorizedAccessException at `File.Replace`. Running the same suite outside that sandbox passed 6860/6860 without a code change. No unrelated test/source was altered.

Commands used:

```powershell
# Focused filter; final run is after all diagnostic source edits.
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter 'FullyQualifiedName~RoofPhysical3DHostDiagnosticsSourceContractTests|FullyQualifiedName~RoofSplitAttachedManual|FullyQualifiedName~RoofGeneratedMemberSplit|FullyQualifiedName~RoofAttachedManualTimberDataCodec|FullyQualifiedName~RoofMirror|FullyQualifiedName~RoofNativeClone|FullyQualifiedName~RoofGeneratedCopy|FullyQualifiedName~RoofMixedPhysicalStretch|FullyQualifiedName~RoofNativeStretchRouting' --logger 'console;verbosity=minimal'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-build --no-restore --logger 'console;verbosity=minimal'
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj --no-build --no-restore -p:Platform=x64 --logger 'console;verbosity=minimal'
dotnet build AcKrovy.sln --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
dotnet build AcKrovy.sln --no-restore -c Release -p:Platform=x64 -warnaserror -m:1 -nr:false
./scripts/compatibility-gate.ps1 -Portable
./scripts/compatibility-gate.ps1 -Full
git -c core.safecrlf=false diff --check
```

AutoCAD process absence checked before adapter/WPF/full builds. Gates use DOTNET_PROCESSOR_COUNT=2 and MSBUILDDISABLENODEREUSE=1. Local gate logs are in TEMP; no diagnostic build output was added to the repository. CAD-neutral projects and localization/schema/version sources were not changed.

## 19. Known limitations / retained WIP

This is an evidence checkpoint, not the requested implementation. All four new physical lifecycles, handle-independent child identity reconciliation, atomic clone/split failure recovery and the new behavior tests remain outstanding. Automatic trace is temporary DEBUG evidence; it scans read-only member/owner state and may produce substantial output for large roofs. Release contains none of this diagnostic class. Missing/ambiguous maps and read failures are logged, not resolved heuristically. A failed capture cannot be treated as complete proof.

Previously present WIP remains untouched: `.ai/handoffs/`, `.cursor/rules/acad-build-lock.mdc`, the two August 23 ChatGPT images, `KROVY_roof_icons_v1_preview.png`, `ErrorReports/9d6767a77b9d2cce90e03082e1302957689eeac5/`, `ErrorReports/GroupUneraseProbe/ErrorReports/`, `ErrorReports/Physical3DCloneProbe/`, and seven prior lifecycle/Physical3D audit documents. Nothing was staged or committed.

## 20. Required HOST evidence

The [Roof Timber Lifecycle workflow](../.agents/skills/roof-timber-lifecycle/SKILL.md) requires: “When the real sequence cannot be proven statically, add narrow temporary DEBUG diagnostics and request one HOST run before changing architecture.” It also forbids persistence/schema/undo architecture changes to compensate for an unproven sequence. The user explicitly permits this evidence checkpoint. Older documented 2D paths are retained; the physical-enabled native variants and same-key replacement are the pending work.

1. **Na čistej uloženej streche sprav BREAK jednej 2D bežnej krokvy v jednom bode, potom AUDIT.** Zopakuj z čistého stavu BREAK s dvoma bodmi a AUDIT.
2. Z čistého stavu sprav COPY jednej krokvy na dve rôzne XY umiestnenia v jednom príkaze; zahrň prirodzene zachytený Physical3D. Potom AUDIT.
3. Z čistého stavu sprav member MIRROR s No, potom samostatne s Yes; pri oboch zahrň Physical3D a sprav AUDIT.
4. Pri každom výsledku skús U → REDO → AUDIT, následne save/reopen → AUDIT. Samostatne zruš COPY/MIRROR pomocou Esc a sprav AUDIT.

Pošli celý trace od začiatku po maintenance vrátane `NATIVE_EVENT`, `MAP`/`MEMBER_MAP`, `MEMBER_CHECKPOINT`, `MEMBER_ROOF_SOURCE`, `MEMBER_PHYSICAL`, `OWNER_COUNTS`, `OWNER_GROUP` a existujúcich semantic maintenance hlášok. Uveď variantu BREAK a odpoveď No/Yes, lebo samotné meno MIRROR ich nerozlišuje. Počty sa hodnotia voči konkrétnemu semantic výsledku, nie voči fixnému počtu prvkov.

Toto je zber dôkazu o súčasnom správaní; nové physical výsledky nemusia ešte spĺňať požadovaný kontrakt. HOST PASS sa z tohto reportu netvrdí.
