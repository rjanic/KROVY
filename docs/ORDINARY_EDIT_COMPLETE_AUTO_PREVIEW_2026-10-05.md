# Complete AUTO preview in AK_ROOF_EDIT — 2026-10-05

AK_ROOF_EDIT now overlays the complete Ordinary generator plan in its transient preview, including AUTO stations absent after native ERASE. It uses the same layout and replay computation as Apply. Normal ERASE, Independent ownership, physical builders, GROUP and designation lifecycle were not changed. No suppression write was introduced. No commit, push or tag was performed.

## Cause and implementation

The previous edit preview called `RoofTransientPreviewSession.Show(document, geometry, sourceElevation)` and added only the roof wireframe. Rafters visible behind it were existing drawing entities; no generator rafter plan was drawn. Apply separately solved and regenerated the full set, so an erased station appeared only after Apply.

Preview now reads the selected owner definition in a read transaction, derives the candidate definition from the current geometry draft with the same semantic-mirror handling as Apply, resolves the current recipe and solves the generator layout. The stored `OrdinaryRafterRecipe` takes precedence. A supported legacy definition without that field may recover width/height/spacing/material from generated metadata, but live handles never determine preview station membership. A bare roof without a generation recipe remains a roof-wireframe preview.

Both preview and Apply use `RoofGeneratedRafterSetService.CreateGeneratorLayout` (the existing Core solver and current drawing minimum-length settings) and `RoofOrdinaryRebuildRules.CreateReplayPlan` (prepared definition, legacy Ordinary suppression ignored, existing compatible replay rules retained). Transient rendering maps that plan's actual member endpoints, including compatible replay geometry, at authoritative Plan2D Z=0. The existing roof geometry/topology preview is retained. No transaction/lock is held while the user views the transient session. Preview writes no definition, recipe, entity, annotation or GROUP data and reads no Independent member store.

## Changed files

- `src/AcKrovy.AutoCAD/Infrastructure/RoofEditCommandWorkflow.cs`: pass selected owner/current geometry to both gable and Hip previews; prepare the complete read-only generator plan before showing transients.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedRafterSetService.cs`: shared recipe resolution and layout creation; authoritative replacement uses the same Core replay plan.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofTransientPreviewSession.cs`: optional generator replay plan, complete transient rafter overlay and neutral endpoint mapping for tests.
- `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryRebuildRules.cs`: common prepared replay-plan computation.
- `src/AcKrovy.Core.Tests/RoofOrdinaryEditPreviewTests.cs` (new): missing/all-absent AUTO inventory, gable/Hip/current pitch, read-only definition preservation, legacy suppression preparation and adapter routing guards.
- `src/AcKrovy.Core.Tests/RoofEditCommandSourceContractTests.cs`, `RoofRafterEditModeSourceContractTests.cs`, `RoofSuppressedLogicalAnchorSourceContractTests.cs`, `RoofGeneratedOverrideReplayDomainSourceContractTests.cs`: updated guards for the shared layout/replay computation and owner-aware preview entry.
- `src/AcKrovy.Wpf.Tests/RoofRafterTransientPreviewTests.cs`: production endpoint mapping compared against Apply plan for gable and Monopitch, including the erased station and Z=0.
- `docs/geometry/roof-elevation-contract.md`: normative complete-generator preview rule; this report.

## Verification

Repository: `C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy`, existing branch `main`, existing uncommitted WIP preserved.

Commands run:

```powershell
# Elevated process check before every build/test gate: acad.exe = NOT RUNNING
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror --filter 'FullyQualifiedName~RoofOrdinaryEditPreviewTests|FullyQualifiedName~RoofEditCommandSourceContractTests|FullyQualifiedName~RoofGeometryDialogSourceContractTests|FullyQualifiedName~RoofSuppressedLogicalAnchorSourceContractTests|FullyQualifiedName~RoofRafterEditModeSourceContractTests|FullyQualifiedName~RoofOrdinaryEraseRebuildTests'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --configuration Debug --no-restore -p:Platform=x64 -warnaserror
.\scripts\compatibility-gate.ps1 -Full
git diff --check
```

Final verdict: **PASS**. Full Compatibility Gate, including Portable: Core **7573/7573**, WPF **831/831**, zero failed/skipped tests and zero warnings/errors. Dependency checks and restore passed. Standard AutoCAD Debug x64 output: `src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll`. Gate log: `.ai/handoffs/ordinary-edit-preview-2026-10-05/full-gate.log`. No alternate output or manual runtime/hash/timestamp probe.

During iteration an old source guard still searched the former inline planner call; it now checks the shared planner remains before erasure/materialization. The new WPF mapping test initially exposed an ambiguous geometry conditional type and a Point3d test dependency; it now explicitly uses IRoofGeometry and verifies neutral RoofPoint3D mapping. Final gate passed after these corrections.

## Remaining HOST check

New automated tests simulate removal from the current live AUTO inventory and exercise the production generator and adapter mapping. They do not execute native ERASE, the modal editor, transient display or final DWG materialization in AutoCAD. Actual visual behavior and Independent package preservation remain **HOST UNCONFIRMED**.

Manual test: create/use a canonical roof with an AUTO recipe; ERASE one AUTO Ordinary package and verify its absence; open AK_ROOF_EDIT and Preview; verify the missing station appears in yellow without modifying DWG inventory; Apply; compare the rebuilt set to the preview. A known Independent member must retain its identity, geometry, Physical3D and exclusion from roof GROUP. Also check Cancel leaves the erased member absent, deleting all AUTO instances still previews from the recipe, current spacing/minimum-length settings are honored, and no Suppressed record is written.

AutoCAD startup uses exactly `C:\Users\Roman\Documents\3d.dwg`; the agent stops before this geometry test.
