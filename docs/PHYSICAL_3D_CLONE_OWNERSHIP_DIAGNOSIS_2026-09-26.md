# Stale physical 3D after COPY/MIRROR → EDIT: Phase 1 evidence request

Status: **defect remains open; HOST 6/7 not accepted**. This is a diagnostic checkpoint, not a correction or a claim of successful reproduction.

Environment: branch `main`, HEAD `d4d4fcff0cdfb3c548af40f17aac6ae00c6029f9`, version 0.23.0, AutoCAD 2027 x64 / .NET 10. The working tree was already dirty. Existing geometry, source-selection, purlin WIP and prior eave fix remain in place. No commit, push, reset, clean or stash was performed.

## Evidence and limitations

User HOST evidence establishes that COPY and MIRROR initially produce physical roofs, and changing the cloned roof from 30° to 45° leaves the old geometry underneath the new model. New ridge Z=6000 and length=4000 are measured; the remaining old objects' native types and ownership payloads have not yet been supplied.

Confirmed source facts:

- `RoofPhysical3DGeneratedStore.BuildSection` writes a six-value schema-1 child section. The owner is a DXF 1000 ASCII string. There is no translated 1005 owner handle or native IdMapping-based rebinding in this store.
- `FindByOwner` scans live model-space entities and accepts only a matching decoded `RoofOwnerReference`. It does not erase by generation signature or spatial proximity.
- `RoofPhysical3DMaterializationService.ReconcileInTransaction` builds the new Core model, erases `FindByOwner` results, creates the new physical set, then writes elevation metadata and owned visibility in the caller's transaction.
- `RoofEditCommandWorkflow.TryApply` invokes that reconcile in its existing Apply transaction. A physical child referring to another owner is outside that erase set regardless of where it is located or which signature it carries.
- `RoofWholeRoofCopyRebindService` collects/rebinds generated timber, structural timber and AttachedManual clones, then regenerates physical 3D under the new owner. Its physical materialization call does not explicitly consume/rebind native physical Face/Line clones.
- `RoofAssemblyGroupMemberCollector` does not include the physical-3D store. Group ownership and physical child ownership therefore need separate inspection.
- `RoofPhysicalElevationStore` is independent schema-2 metadata on the definition-bearing source polyline. It contains the elevation preference/values and visibility, not a list or mapping of child handles.
- The 2D display store has an existing translated-handle path. Its behavior must be observed separately; a remaining 3D object must not be mistaken for a flattened display child.

The leading hypothesis is that native physical clones still reference the original owner, while reconcile creates a second canonical set under the new owner. EDIT would then erase only the canonical new-owner set. **The exact native IDs, callback sequence, stale entity family and cross-owner payloads are not yet confirmed.** No ownership, persistence, signature or undo architecture was changed on that hypothesis.

## Attempted runtime reproduction

A disposable probe was compiled in `ErrorReports/Physical3DCloneProbe/`, outside the solution. It uses the current source stores/materializers and reflects the exact production `RoofEditCommandWorkflow.TryApply` method, bypassing only modal input. The intended script runs native COPY ALL on a stock drawing opened without saving, then dumps child data and applies 45°.

The first Core Console invocation without `/i` failed at startup with ErrorStatus=53. Subsequent invocations opened `Express/brkline.dwg` read-only and NETLOADed the probe, but crashed during setup. Narrowing from the UI tracker to database-only setup did not produce a successful reproduction. Step logs reached model-space acquisition in the last attempt. The precise Core Console crash cause was not established. **No successful native COPY → EDIT or MIRROR → EDIT run was obtained; no mapping or stale-child classification can be claimed from these runs.** Further console experiments were stopped because this environment is unstable for this probe. Existing stock input files were not saved or modified.

Evidence retained: `copy-before.log`, `copy-before-input.log`, `copy-before-core.log`, `copy-before-native.log`, `copy-before-db.log`, `probe-steps.txt`, the source/csproj, `copy-before.scr`, `PLAN.md`, and automatically created crash artifacts beneath `ErrorReports/Physical3DCloneProbe/`. Do not load that probe for the acceptance procedure below; use the main Debug DLL and its read-only commands.

## Diagnostic changes

`AK_ROOF_3D_AUDIT` is a DEBUG-only read-only command. It records every live physical and display child, original source definition and elevation metadata, native type, handle, database owner, per-entity visibility, group membership, raw physical XData codes/values, generation signatures and Line/Face/source coordinates. It summarizes physical counts by owner and role. It performs no repair, erase, transform, visibility or XData write and does not commit a transaction.

`AK_ROOF_3D_TRACE` toggles an opt-in observer for the active document. It is initially disabled and registers before production lifecycle maintenance. Only COPY, MIRROR and AK_ROOF_EDIT are observed. It captures source/clone `IdPair` mappings during native `BeginDeepCloneTranslation`, command-start snapshots, and native CommandEnded snapshots before production maintenance. Manual AUDIT gives the final post-maintenance state. Active tracing also snapshots the production physical reconcile before cleanup and after creating the new set. No database access occurs for U/UNDO/REDO/MREDO callbacks. Errors in diagnostic reads are contained and reported rather than altering the production result.

Changed production/test files in this checkpoint:

1. `src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs` — new opt-in read-only evidence collector.
2. `src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs` — two DEBUG diagnostic commands. Existing source-selection command retained.
3. `src/AcKrovy.AutoCAD/PluginEntry.cs` — DEBUG observer registration before the lifecycle tracker and disposal on unload.
4. `src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DMaterializationService.cs` — DEBUG snapshots around the existing reconcile. Geometry and erase behavior unchanged.
5. `src/AcKrovy.Core.Tests/RoofPhysical3DHostDiagnosticsSourceContractTests.cs` — guards read-only behavior, DEBUG isolation, command filtering and registration order. These are not HOST tests.
6. `docs/PHYSICAL_3D_CLONE_OWNERSHIP_DIAGNOSIS_2026-09-26.md` — this record.

New probe source/setup files: `ErrorReports/Physical3DCloneProbe/Probe.cs`, `Probe.csproj`, `copy-before.scr`, `PLAN.md`. Their generated logs/crash artifacts are listed above. Probe output is not part of the release build.

No Core geometry/model, physical/elevation store schema, localization, purlin implementation, group collector, COPY/MIRROR rebind or production EDIT logic was modified. Decisions 1B + 2A, definition schema 5, elevation schema 2, flattened 2D plan, native source grips and 3D eave Lines remain unchanged.

## Automated validation of the diagnostic checkpoint

| Check | Result |
| --- | --- |
| Focused Core: physical 3D, copy/mirror/edit, purlins, diagnostic isolation | PASS — 653/653 |
| Focused WPF: physical elevation and automatic purlins | PASS — 452/452 |
| Full Core | PASS — 6622/6622 |
| Full WPF | PASS — 803/803 |
| Debug x64, warnings as errors | PASS — 0 warnings, 0 errors |
| Release x64, warnings as errors | PASS — 0 warnings, 0 errors; diagnostics excluded |
| Portable gate | PASS — restore/build/test, architecture and version checks |
| Full gate | PASS — architecture, restore/build, Core 6622/6622, WPF 803/803 |
| `git diff --check` | PASS; only LF/CRLF conversion notices |
| CAD-neutral dependencies | Checked by compatibility gate |
| Core Console reproduction | INCONCLUSIVE — crashes before native clone scenario |
| HOST A–H / HOST 6–7 acceptance | NOT ACCEPTED / correction pending |

Commands run:

```powershell
dotnet build ErrorReports/Physical3DCloneProbe/Probe.csproj -p:Platform=x64 -m:1 -v:minimal
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --filter 'FullyQualifiedName~RoofPhysical3D|FullyQualifiedName~HipPhysical3D|FullyQualifiedName~RoofWholeRoofCopy|FullyQualifiedName~RoofMirror|FullyQualifiedName~RoofEditCommand|FullyQualifiedName~RoofAutomaticPurlin' -p:Platform=x64 -m:1 -warnaserror -v:minimal
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj --filter 'FullyQualifiedName~HipRoofPhysicalElevation|FullyQualifiedName~AutomaticPurlin' -p:Platform=x64 -m:1 -warnaserror -v:minimal
dotnet test AcKrovy.sln -c Debug -p:Platform=x64 -m:1 -warnaserror -v:minimal
dotnet build AcKrovy.sln -c Debug -p:Platform=x64 -warnaserror -m:1 -v:minimal
dotnet build AcKrovy.sln -c Release -p:Platform=x64 -warnaserror -m:1 -v:minimal
./scripts/compatibility-gate.ps1 -Portable
./scripts/compatibility-gate.ps1 -Full
git status --short
git diff --numstat
git diff --check
Get-Process acad,accoreconsole -ErrorAction SilentlyContinue
```

The native executable attempts used `C:/Program Files/Autodesk/AutoCAD 2027/accoreconsole.exe`, `/s .../copy-before.scr`, `/l en-US`; retries additionally used `/i C:/Program Files/Autodesk/AutoCAD 2027/Express/brkline.dwg`. All those processes exited before subsequent builds. Builds/test/gate processes ran with normal .NET cache access outside the sandbox.

## Required desktop AutoCAD 2027 evidence run

Automated verdict for the diagnostic checkpoint: **PASS**. Overall defect verdict: **OPEN / INCONCLUSIVE pending desktop HOST evidence**. Passing gates do not establish a correction or acceptance of HOST 6/7.

Use a disposable DWG. Close AutoCAD before any build. Load the verified main **Debug** DLL: `src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll`. Ensure another copy of KROVY has not already been loaded. Keep the original source and selection method identical to the observed failing scenario.

1. Run `AK_ROOF_3D_TRACE` once; verify `enabled=True`. Create the 10000×6000, 30° physical hip roof with eave 3000 mm. Run `AK_ROOF_3D_AUDIT`; retain the OWNER handle, definition/elevation, five flattened display children and 13 physical children (4 Face / 4 HipEdge / 4 EaveEdge / 1 RidgeEdge).
2. Native COPY the whole roof using the same selection method as the failure. Run `AK_ROOF_3D_AUDIT` immediately. Record which source→clone owner and child pairs are logged, the original/clone owner handles and all child ownership payloads. Do not erase any leftover object manually.
3. Run `AK_ROOF_EDIT` on the copy, change 30° → 45°, Apply. Run `AK_ROOF_3D_AUDIT` immediately. Retain a screenshot showing the stale object(s), preferably with one stale object selected so its native handle/type is known. The trace now contains reconcile-before and reconcile-after-create snapshots, including the stale entity's role, owner and signature if tagged.
4. Repeat on a fresh disposable roof with native MIRROR (Erase source No), then EDIT only the mirror to 45°. Audit before MIRROR, after MIRROR and after EDIT. Include the exact selected entities, source owner, clone owner and native mapping. If a stale object is not listed as PHYSICAL/DISPLAY, LIST/Properties its handle, native type, layer and XData so it can be distinguished from unrelated/untagged geometry.
5. Send the `ROOF_3D_AUDIT` output and existing COPY/MIRROR trace from command history, or the current KROVY log under `%LOCALAPPDATA%/ACAD_KROVY/Logs`. Provide the screenshots and the selection method. Run `AK_ROOF_3D_TRACE` again to verify `enabled=False` after capture.

This is the minimum input needed to confirm the first ownership/control-flow divergence and implement Phase 2 without guessing. Do not treat this diagnostic build as a fix.

After correction, required acceptance still includes: COPY and MIRROR No/Yes with original unchanged; repeated EDIT 45°→30°→45°; one current physical/plan set per owner; source selection/XData; STRETCH/GRIP_STRETCH, ERASE and U/REDO; no cross-owner deletion or orphans; independent visibility with native eaves; existing purlin regressions. Expected 45° ridge is Z=6000, length=4000; the retained 30° original ridge is Z=3000+3000·tan(30°). No obsolete signature should remain among the edited owner's live generated children.

## Why work stops before correction

The repository's [Roof Timber Lifecycle workflow](../.agents/skills/roof-timber-lifecycle/SKILL.md) explicitly requires: “When the real sequence cannot be proven statically, add narrow temporary DEBUG diagnostics and request one HOST run before changing architecture.” It also says not to change persistence/schema/undo architecture to compensate for an unproven AutoCAD event sequence.

The [HOST Regression Test workflow](../.agents/skills/host-regression-test/SKILL.md) requires stopping with INCONCLUSIVE when AutoCAD crashes or the test environment is unstable. That occurred in the console probe. The independent automated validation was completed; Phase 2 and actual HOST A–H acceptance await the desktop evidence run above.
