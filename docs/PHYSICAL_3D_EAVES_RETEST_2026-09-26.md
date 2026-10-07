# Physical 3D hip eaves — implementation and acceptance record

Date: 2026-09-26. Branch: `main`. HEAD: `d4d4fcff0cdfb3c548af40f17aac6ae00c6029f9`.
Build version: 0.23.0, AutoCAD 2027 / .NET 10, x64.
The working tree was already dirty, including the physical-3D prototype and AK_ROOF_PURLINS WIP. No commit, push, release tag, reset or checkout was performed.

## Root cause and fix

The owner/grip WIP suppressed the eave edge on each native Face through `RoofPhysical3DPlanDisplayRules.FaceEdgeVisibility`. `RoofPhysical3DMaterializationService.CreateEntities` also deliberately omitted `model.Eaves`. Consequently neither a visible Face edge nor a dedicated Line represented the physical outer eave. The Core builder still produced the correct four physical eaves; the loss occurred in native entity materialization, not elevation calculation.

The materializer now appends all four Core eaves as native 3D Lines on `KROV_STRECHA_3D_HRANY`, using the same owned edge pipeline as hips/ridge. Each receives `RoofPhysical3DGeneratedRole.EaveEdge`, source owner reference, structural ID and generation signature. Existing role values remain unchanged; `EaveEdge = 3` is additive. The existing Face eave edge suppression remains, so the dedicated Line supplies that physical edge. No flattened perimeter Lines are introduced.

Existing per-entity visibility and owner-scoped erase/recreate paths cover the new role without layer toggling. Both and Model3D show physical entities; Plan2D hides them. The original source polyline is neither rewritten nor replaced by the new selection action.

`AK_ROOF_SELECT_SOURCE` prompts for the source or any recognized roof child, resolves its source with the existing owner resolver, verifies roof definition metadata, closes a read-only transaction, then sets the implied selection to that source ObjectId alone. It does not write XData, change layer visibility, alter group selectability, or add a grip overrule. Its prompt exists in all six languages. This is an explicit command-line action; no ribbon redesign is included.

## Recovery assessment

The current WIP already preserves `Physical3DEnabled` when an otherwise supported 2D quadrilateral loses rectangular/symmetric 3D eligibility. It erases owned 3D, rebuilds flattened supported 2D, and reports suspension once at the command boundary. On return to a rectangle, current display drift promotes the compact-descriptor `RigidEquivalent` case to supported resize and the same reconcile path rebuilds physical geometry.

The new regression performs valid → irregular → valid against successively updated definitions, keeps the preference, rejects the irregular physical build, checks flattened plan Z and absence of perimeter roles, then verifies 4 faces / 4 hips / 4 unique eaves / 1 ridge. Restored eaves have WCS Z=3000; ridge Z=6000, length=4000 for 10000×6000 at 45°. It also validates the EaveEdge ownership payload round trip.

No separate automatic-recovery defect was demonstrated in the portable model. Recovery of eaves had the same materialization omission as initial creation and is covered by the fix. Actual command dispatch, transaction acceptance, one warning and native grip recovery remain HOST tests, not proven by source contracts.

Decisions 1B + 2A are retained. No source-elevation addition or elevation formula change was made. RoofDefinitionData schema remains 5; elevation-store schema remains 2. No arbitrary quadrilateral physical 3D was implemented. AK_ROOF_PURLINS implementation/WIP was not edited.

## Exact files changed by this task

These are task-local edits; other dirty files shown by Git predate this task.

1. `src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DMaterializationService.cs` — append owned physical eave Lines.
2. `src/AcKrovy.AutoCAD/Infrastructure/RoofSourceSelectionWorkflow.cs` — new read-only source selection action.
3. `src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs` — register the action using the existing command boundary.
4. `src/AcKrovy.Core/Models/Roofs/RoofPhysical3DModel.cs` — additive EaveEdge role.
5. `src/AcKrovy.Core/Services/Roofs/RoofPhysical3DPlanDisplayRules.cs` — clarify 2D perimeter versus dedicated physical 3D eaves in comments.
6. `src/AcKrovy.Core.Tests/HipPhysical3DSingleCornerGripTests.cs` — valid/invalid/valid geometry and ownership regression.
7. `src/AcKrovy.Core.Tests/RoofPhysical3DHostSourceContractTests.cs` — require eave materialization and read-only source selection wiring.
8. `src/AcKrovy.Core.Tests/LocalizationFoundationTests.cs` — include the new stable technical command.
9. `src/AcKrovy.Core.Tests/LocalizationLanguagePackTests.cs` — resource count for the additional prompt.
10. `src/AcKrovy.Localization/CommandUiCatalog.cs` — stable command name/catalog entry.
11. `src/AcKrovy.Localization/Resources/UiStrings.resx` — Slovak prompt.
12. `src/AcKrovy.Localization/Resources/UiStrings.cs.resx` — Czech prompt.
13. `src/AcKrovy.Localization/Resources/UiStrings.en.resx` — English prompt.
14. `src/AcKrovy.Localization/Resources/UiStrings.de.resx` — German prompt.
15. `src/AcKrovy.Localization/Resources/UiStrings.pl.resx` — Polish prompt.
16. `src/AcKrovy.Localization/Resources/UiStrings.fr.resx` — French prompt.
17. `docs/PHYSICAL_3D_EAVES_RETEST_2026-09-26.md` — this validation/acceptance record.

## Automated verification

| Check | Result |
| --- | --- |
| Focused Core physical 3D / grip / redo / localization | PASS — 99/99 |
| Focused WPF physical elevation/visibility | PASS — 6/6 |
| Full Core | PASS — 6620/6620 |
| Full WPF | PASS — 803/803 |
| Debug x64 solution build, warnings as errors | PASS — 0 warnings, 0 errors |
| Release x64 solution build, warnings as errors | PASS — 0 warnings, 0 errors |
| Portable Compatibility Gate | PASS — restore, portable builds, tests, architecture and version checks |
| Full Compatibility Gate | PASS — restore, builds with 0 warnings/errors, Core 6620/6620 and WPF 803/803 |
| Localization key/placeholder parity | PASS in full Core |
| `git diff --check` | PASS; Git emitted only existing LF/CRLF conversion notices |
| AutoCAD running during build | No `acad` process found before builds |
| HOST visual/native lifecycle | NOT RUN — acceptance pending |

Commands executed from the repository (each test/build rerun used normal .NET cache access outside the sandbox):

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --filter 'FullyQualifiedName~Physical3D|FullyQualifiedName~HipPhysical3D|FullyQualifiedName~HipRoofFlattened|FullyQualifiedName~RoofGripStretch|FullyQualifiedName~RoofResizeRedo|FullyQualifiedName~LocalizationLanguagePack' -p:Platform=x64 -m:1 -v:minimal -warnaserror
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj --filter 'FullyQualifiedName~HipRoofPhysicalElevation' -p:Platform=x64 -m:1 -warnaserror -v:minimal
dotnet test AcKrovy.sln -c Debug -p:Platform=x64 -m:1 -warnaserror -v:minimal
dotnet build AcKrovy.sln -c Debug -p:Platform=x64 -warnaserror -m:1 -v:minimal
dotnet build AcKrovy.sln -c Release -p:Platform=x64 -warnaserror -m:1 -v:minimal
./scripts/compatibility-gate.ps1 -Portable
./scripts/compatibility-gate.ps1 -Full
git diff --check
git status --short
git diff --numstat
git branch --show-current
git rev-parse HEAD
Get-Process acad -ErrorAction SilentlyContinue
```

Initial sandboxed restore/test attempts stalled and were cancelled; their identified orphan MSBuild workers were stopped. One subsequent run emitted a resource-cache lock warning from the overlapping cancelled run; the clean rerun passed with warnings as errors. Iteration also caught a new command wrapper typo and fixed-count resource/command expectations; all were corrected before final verification. The failed runs are not counted as final PASS evidence.

Automated verdict: **PASS**. Overall acceptance verdict: **INCONCLUSIVE — HOST pending**.

## Remaining HOST risks and acceptance boundary

Visual style, pick selection, source grips, event ordering, native clone ownership and native UNDO/REDO are not portable-test results. Existing physical-3D WIP is not included in `RoofAssemblyGroupMemberCollector`; whole-roof COPY/MIRROR rebind regenerates physical 3D for an accepted new owner but does not explicitly collect/rebind/consume physical-3D native clones. It also has an appended-timber prerequisite. This is a concrete integration gap to inspect in HOST, particularly a window selection including faces/edges or a roof with no rafters. This task does not silently redesign clone/event architecture from source inspection.

The [Roof Timber Lifecycle workflow](../.agents/skills/roof-timber-lifecycle/SKILL.md) requires: “Do not change persistence/schema/undo architecture to compensate for an event sequence that has not been proven in AutoCAD.” The [HOST Regression Test workflow](../.agents/skills/host-regression-test/SKILL.md) requires: “Do not claim HOST PASS without actual AutoCAD execution.” No HOST PASS or complete lifecycle acceptance is claimed.

## Minimal AutoCAD 2027 HOST retest

1. With AutoCAD closed, use the verified Debug x64 DLL at `src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll`. Start AutoCAD 2027, load that build, and create a disposable DWG with a closed 10000×6000 rectangular source at drawing elevation 0. Create a uniform 45° hip roof with Physical3D enabled, eave input 3000 mm, Both mode. Retain source/owner handle and command/log output. Also test an existing affected roof: `AK_ROOF_EDIT`, Apply once should materialize the missing eaves.
2. Inspect in an oblique 2D Wireframe view, then a shaded-with-edges view. Expected physical set per owner: 4 native Faces, 4 EaveEdge Lines, 4 HipEdge Lines, 1 RidgeEdge Line. LIST/Properties must show all eave endpoints Z=3000, ridge endpoints Z=6000 and length=4000. Check child owner references and distinct structural IDs. At drawing elevation 0 there must be only the original perimeter polyline plus five flattened hip/ridge plan Lines, with no generated flattened eaves.
3. Cycle `AK_ROOF_EDIT` → Plan2D → Model3D → Both, applying each. Plan2D: source/native grips selectable directly, physical set invisible. Model3D: all four physical eaves visible with hips/ridge/faces; flattened plan children invisible. Both: both sets visible. Confirm a second roof on the same layers retains its chosen visibility. Repeat Apply; counts must stay constant.
4. Unlock source editing with the existing roof unlock action. In Plan2D select the original source directly and grip V3 from (0,6000) to (1500,4500), keeping the other vertices unchanged. Supported 2D must update; all owned physical 3D must disappear; stored Physical3DEnabled must remain true; exactly one localized suspension warning should appear after completion. Return V3 to (0,6000): automatically restore the 13 physical entities and correct five flattened plan Lines, with no duplicate/orphan entities. Repeat this cycle once.
5. In Both mode run `AK_ROOF_SELECT_SOURCE` and click an eave Line, hip Line, Face, plan child and source in separate runs. Each must select the original polyline with native grips. Check DBMOD immediately before/after this selection-only action; it must not change. Perform the same valid/invalid/valid grip cycle through this action. Capture whether native overlap selection works and whether the explicit action exposes only source grips.
6. Exercise classic STRETCH and `AK_ROOF_EDIT` on a valid rectangle. Confirm all rebuilt elevations, counts and ownership. Exercise existing locked/unlocked semantics separately; do not interpret a deliberately rejected locked shape edit as a recovery failure.
7. COPY the whole roof, first through normal assembly selection and then through a window containing source + plan + physical 3D; repeat on a roof without generated rafters. MIRROR whole roof with Erase source No and Yes. Every retained owner must have exactly one correctly located physical set and plan set; original owners must remain intact when retained. Any cloned physical entity still referring to the original owner, duplicate physical set, stale physical set, or missing new set is FAIL. Capture command-scoped appended/modified/erased IDs and existing COPY/MIRROR diagnostics before designing a lifecycle fix.
8. UNDO/REDO each grip transition, EDIT, STRETCH, COPY and MIRROR; repeat U/REDO once. Geometry, preference, visibility, counts and ownership must return exactly, with no maintenance writes at undo/redo boundaries. SAVE/reopen the disposable DWG and recheck entity ownership, visibility and native source selection.

Capture screenshots for all modes and transitions, measured coordinates, owner/child handles and entity counts, localized warnings, DBMOD for selection, and command/event diagnostics for any failing lifecycle path. Overall HOST verdict is **INCONCLUSIVE / NOT RUN** until these observations are supplied. Stop for HOST acceptance; no commit or push.
