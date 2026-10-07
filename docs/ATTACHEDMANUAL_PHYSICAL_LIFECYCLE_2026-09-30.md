# AttachedManual Physical3D + BREAK/COPY/MIRROR lifecycle — 2026-09-30

1. **STATUS**: IMPLEMENTED; automated validation PASS, HOST NOT RUN. Baseline remains `4f03788f7afb6f26e95dceff52e6e960e088706d` on `main`. No commit, push, tag or release. The supplied HOST proof establishes the missing AttachedManual physical materialization, the Generated-to-AttachedManual identity loss during in-place MIRROR Yes, and surviving duplicated native physical keys. Those proven variants guided this implementation. Earlier diagnostic WIP is preserved.

2. **AttachedManual identity**: existing `RoofAttachedManualTimberData` gains nullable `SemanticIdentity`; schema 4 persists a canonical UUID in the existing XData codec. `ChildIdentity` remains the historical CAD/annotation binding. Physical keys use `AttachedManual:<UUID>`, disjoint from Generated `Rafter:Face0:<station>`. New children get a UUID once during the accepted transaction. Reading old metadata does not mint random UUIDs. Legacy anchored records derive a deterministic migration UUID from their original owner/binding; owned writes/materialization persist it, then rebinding preserves that explicit UUID. UUID case is normalized.

3. **Builder/materialization**: `RoofAttachedManualPhysicalBuilder` invokes the shared ordinary prism/cut solver extracted from `RoofAutomaticRafterPhysicalBuilder`. Input is valid semantic metadata, final accepted Plan2D at Z=0, section, roof topology/elevation, and existing end-cut/structural/ridge policies. Final endpoints resolve their actual roof face and boundary roles; interior split endpoints have perpendicular free cuts. Generated canonical geometry is covered by existing golden regressions. No Solid3d geometry, extents, mass properties, cloning or native transform is a physical geometry source. Existing physical eligibility remains unchanged.

4. **BREAK**: the retained Generated segment keeps its key through the existing geometry override; the added segment receives its own AttachedManual identity. Shared reconciliation replaces the old full body and builds the two retained bodies. Two-point BREAK leaves no separate body for the removed middle interval. Breaking Copy/Split AttachedManual sources preserves the surviving child's UUID; appended siblings get new UUIDs. Failure recovery removes newly appended owned fragments, restores the existing snapshot and reconciles affected physical keys.

5. **COPY**: the existing ownership rehydration creates new AttachedManual children, including multiple placements and copies of either supported AttachedManual origin. A valid unique source body binding is preserved. Each clone receives a fresh UUID and physical body. The command reconciler checks that a supported ordinary result really has semantic ownership before committing; falling back to generic timber cannot masquerade as success.

6. **MIRROR No**: the source retains its semantic identity and valid unique body binding. The result is an independent AttachedManual member derived from final mirrored Plan2D, with a new UUID/body. Source annotations are preserved; native mirrored annotation clones use the existing cleanup/presentation services.

7. **MIRROR Yes**: unambiguous in-place ordinary Generated edits and exact mapped clone+erase replacements use `TryRebindGeneratedReplacement`. It classifies mirrored final geometry into the existing `RoofGeneratedMemberOverride`, persists it under the original key/reservation, writes Generated metadata to the replacement and rebuilds that key. It does not suppress or permanently promote the ordinary member. AttachedManual replacements retain their source UUID and force rebuilding that key even when the old body still exists. Unchanged in-place geometry is skipped. Whole-roof source changes keep the existing resize/mirror lifecycle; complete roof clones are consumed before member reconciliation.

8. **Collateral solids**: command-local IDs/native mapping identify new derived clones. Shared `RoofOrdinaryPhysicalReconciliationRules` removes collateral/orphan bodies and builds missing/changed canonical bodies from semantic state. Ambiguous pre-existing duplicate ordinary bodies are both replaced; a solid is never selected as authoritative by iteration order or shape. Other collateral physical/display/structural roles are discarded/recovered through existing generators. Native COPY/MIRROR semantic, physical and GROUP maintenance runs in an outer transaction. [Autodesk documents that aborting an outer transaction rolls back committed nested transactions](https://help.autodesk.com/cloudhelp/2027/CSY/OARX-DevGuide-Managed/files/GUID-8D8B9EE8-9D85-4C29-93A0-0BDC90F66EA7.htm). That API contract does not prove a completed native command HOST retest.

9. **AttachedManual edits**: MOVE/TRIM/STRETCH/GRIP_STRETCH update real changed Plan2D relatives while keeping the UUID and reconcile the matching body. When the native anchor is missing, matching pre-command Plan2D plus persisted relative geometry can recover its unchanged basis without inventing a Generated slot. ERASE removes the orphaned matching physical identity and GROUP entries. Mixed Generated+AttachedManual ERASE retains strict prestate validation. Direct physical MOVE/STRETCH recovery recognizes AttachedManual identity; native ERASE recovery keeps the existing exact recovery mechanism.

10. **Undo/Redo**: existing grouped command marks and zero-database-write Undo/Redo guards remain. UUIDs are persisted in normal native transaction state, so replay does not rerun factories. Core tests cover restoring/reloading the serialized semantic snapshots and deriving the same keys/geometries; source contracts cover guards and rollback wiring. Actual AutoCAD Undo/Redo transactions and callback ordering remain HOST tests.

11. **Persistence/migration**: schema 1 remains readable without inventing an anchor. Valid schema 2/3 anchored records migrate deterministically to schema 4; schema 4 rejects malformed UUID payloads. Codec round trips cover BREAK/COPY/MIRROR No/Yes overrides and AttachedManual metadata; restored child endpoints are replayed from the decoded RelativeSegment against the restored semantic anchor. Plan2D is still stored in native Line geometry; actual DWG save/reopen is not claimed by these codec tests. Dormant children retain semantic metadata/UUID and have no physical body; physical reconciliation follows source child replay before structural validation.

12. **Physical invariant**: each active physical-enabled ordinary semantic identity must have exactly one owned Solid3d key, with no orphan/duplicate keys. This check is independent of GROUP canonicality. Diagnostics emit `DUPLICATE_PHYSICAL_KEY` with role, key and concrete handles. Attached children are excluded from the automatic Hip/Valley profile recommendation, preserving its existing Generated authority. No HOST fixture counts or handles were hardcoded.

13. **Exact changed files** (baseline diff + new source/tests/report; 48 files):

- `docs/ATTACHEDMANUAL_PHYSICAL_LIFECYCLE_2026-09-30.md`
- `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualCopyCloneReinitializeService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualLifecycleService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualTimberStore.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofEditCommandWorkflow.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedMemberManualEditService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedRafterCopyOwnershipRehydrationService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedRafterSetService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofLiveResizeService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofMirrorCloneDetachService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofNativeCloneSnapshot.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryRafterSolidMaterializationService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/RoofStructuralRafterSolidMaterializationService.cs`
- `src/AcKrovy.Core.Tests/AsymmetricGableRoofFoundationTests.cs`
- `src/AcKrovy.Core.Tests/MonopitchRafterStage2D1SourceContractTests.cs`
- `src/AcKrovy.Core.Tests/MonopitchRafterStage2D3Tests.cs`
- `src/AcKrovy.Core.Tests/MonopitchRafterStage2D4AClipboardTests.cs`
- `src/AcKrovy.Core.Tests/MonopitchRafterStage2D4BClipboardTests.cs`
- `src/AcKrovy.Core.Tests/MonopitchRoofStage1SourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofAttachedManualCopyCloneSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofAttachedManualCopySplitSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofAttachedManualPhysicalAdapterSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofAttachedManualPhysicalLifecycleTests.cs`
- `src/AcKrovy.Core.Tests/RoofGeometryDialogSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofMirrorCloneAnnotationSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofMirrorCloneDetachSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofMirrorYesSuppressionSemanticsTests.cs`
- `src/AcKrovy.Core.Tests/RoofMixedPhysicalStretchTests.cs`
- `src/AcKrovy.Core.Tests/RoofOrdinaryPhysicalStretchTests.cs`
- `src/AcKrovy.Core.Tests/RoofOrdinaryRafterSolidSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofPhysical3DHostDiagnosticsSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofSplitAttachedManualSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofVirtualSuppressedAttachedManualCaptureSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/RoofWholeRoofMirrorSourceContractTests.cs`
- `src/AcKrovy.Core/Models/Roofs/RoofAttachedManualTimberData.cs`
- `src/AcKrovy.Core/Models/Roofs/RoofAttachedManualTimberDataSchema.cs`
- `src/AcKrovy.Core/Models/Roofs/RoofAutomaticRafterPhysicalModel.cs`
- `src/AcKrovy.Core/Models/Roofs/RoofFaceRafterLayout.cs`
- `src/AcKrovy.Core/Services/Roofs/RoofAttachedManualIdentityRules.cs`
- `src/AcKrovy.Core/Services/Roofs/RoofAttachedManualPhysicalBuilder.cs`
- `src/AcKrovy.Core/Services/Roofs/RoofAttachedManualRelativeGeometryRules.cs`
- `src/AcKrovy.Core/Services/Roofs/RoofAttachedManualTimberDataCodec.cs`
- `src/AcKrovy.Core/Services/Roofs/RoofAutomaticRafterPhysicalBuilder.cs`
- `src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberEditCommandRules.cs`
- `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryPhysicalReconciliationRules.cs`
- `src/AcKrovy.Core/Services/Roofs/RoofOrdinaryRafterSemanticGeometryRules.cs`

The diagnostic source/test files already contained this task's earlier WIP. Unrelated `.ai/handoffs`, `.cursor/rules/acad-build-lock.mdc`, root preview/images, `ErrorReports` trees and the prior audit/report documents were preserved. No UI, localization, project dependencies, product version, build script or roof definition schema was changed. AttachedManual schema freeze assertions were explicitly advanced to v4; all other schema assertions remain.

14. **Focused tests**: PASS **546/546**, including **58 new** Core geometry/identity/planner/codec and adapter wiring cases. Existing Generated edits, mixed STRETCH, native clone/mirror, whole-roof, Hip/Valley and ridge overlap regressions are included. Historical MIRROR Yes suppression assertions now enforce same-key geometry override/replay. Source-contract tests prove wiring, not actual HOST event sequence.

15. **Core/WPF**: PASS **6918/6918** Core; PASS **806/806** WPF. Full Gate also reran both suites successfully. After the final MIRROR test-only assertion updates, the full Core suite and focused suite passed again; production sources were unchanged.

16. **Debug/Release x64**: both PASS, 0 warnings/errors. AutoCAD process checks preceded adapter builds.

17. **Portable/Full Gate**: both PASS. The standalone Portable Gate passed; final Full Gate repeated the portable dependency/build/test checks against all 6918 Core tests, then built the adapter and passed solution tests. CAD-neutral projects remain vendor-free.

18. **git diff --check / repository**: PASS. Branch `main`, HEAD `4f03788f7afb6f26e95dceff52e6e960e088706d`, local `main...origin/main` 0/0; nothing staged. Working tree intentionally contains implementation plus preserved unrelated WIP. No remote fetch or publication was performed.

19. **Known limits**: physical creation stays within the existing supported/enabled roof scope. Schema-1 unanchored legacy children remain readable but cannot get invented semantic anchors/physical provenance. Exact unambiguous replacement provenance is required; unknown/ambiguous native variants fail closed rather than choosing a source heuristically. HOST materialization, native Undo/Redo and DWG reopen need the short retest below. The previous Hip metadata-diff diagnostic/write policy is unchanged; no speculative redundant-write removal was made. Cross-DWG/clipboard adoption is outside this native COPY/MIRROR change and keeps its prior routing policy.

20. **Minimal HOST retest**: start directly on a clean saved, unlocked Hip roof with Physical3D enabled.

- BREAK one ordinary 2D rafter at one point, then another with two points. Check retained Generated key, new AttachedManual UUID, one body per retained segment and no old full body.
- COPY an ordinary member with natural mixed 2D/3D selection, including multiple placements. Check unchanged source, distinct clone UUIDs/bodies and no duplicate source physical key.
- MIRROR No, then MIRROR Yes on individual members. MIRROR Yes must report the same Generated key, semantic geometry override and one mirrored body, with no permanent AttachedManual promotion/suppression.
- On one new AttachedManual child, exercise MOVE, endpoint STRETCH/GRIP_STRETCH, TRIM and ERASE. Check stable UUID and matching body change/removal; briefly check direct 3D MOVE/ERASE recovery.
- UNDO/REDO representative BREAK/COPY/MIRROR and AttachedManual edits; save/reopen. Check keys, Plan2D, physical bodies, metadata and GROUP each time.
- Finish with roof-source STRETCH / whole-roof MIRROR regression and AUDIT. Physical unique keys and GROUP must both be canonical; no new architecture trace is requested for the already proven variants.

## Commands executed

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore --filter '<focused lifecycle filter>' -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj --no-restore -p:Platform=x64 -warnaserror -m:1 -nr:false
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj --no-build --no-restore -p:Platform=x64
dotnet build AcKrovy.sln --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
dotnet build AcKrovy.sln --no-restore -c Release -p:Platform=x64 -warnaserror -m:1 -nr:false
./scripts/compatibility-gate.ps1 -Portable
./scripts/compatibility-gate.ps1
git diff --check
git status --short
git branch --show-current
git rev-parse HEAD
git rev-list --left-right --count main...origin/main
git diff --cached --stat
```

Focused filter:

```text
FullyQualifiedName~RoofAutomaticRafterPhysicalBuilder|FullyQualifiedName~RoofGeneratedMember|FullyQualifiedName~RoofAttachedManual|FullyQualifiedName~RoofPhysical3D|FullyQualifiedName~RoofMirror|FullyQualifiedName~RoofNativeClone|FullyQualifiedName~RoofWholeRoofMirror|FullyQualifiedName~RoofOrdinaryRafterPhysical|FullyQualifiedName~RoofStructuralRafterPhysical|FullyQualifiedName~RoofHipLiveResize
```

Gate logs are temporary diagnostic output under `%TEMP%`; they are not repository artifacts. Gate runs used `DOTNET_PROCESSOR_COUNT=2` and `MSBUILDDISABLENODEREUSE=1`.
