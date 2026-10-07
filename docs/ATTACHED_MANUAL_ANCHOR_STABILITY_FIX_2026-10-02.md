# AttachedManual anchor/frame stability — 2026-10-02

Branch: `main`. HEAD/origin/main: `1ce75359c5449184f96a2d282156a010278d6ff4`; ahead/behind `0/0`.

## Exact root cause

`RoofAttachedManualCopyCloneReinitializeService.TryReinitializeClone` discarded the inherited anchor key and called `SelectNearestMirrorAnchor`. That MIRROR policy rejects candidates with `U1 <= U0`. The HOST source's s12 frame has constant U, so it was rejected even though the anchor was valid. The perpendicular s2 frame gives a positive U span and wins the remaining nearest-frame selection. Native COPY had not rotated the line.

`RoofAttachedManualLifecycleService.RefreshModifiedAttachedManualRelatives` then tried `TrySelectNearestCopyAnchor` before resolving the existing anchor. Its minimum absolute midpoint-V policy could select s12 again on MOVE. Preserving world Plan endpoints while re-capturing relative coordinates did not preserve physical meaning.

Generated-source COPY already used the exact source Generated key when its source line was resolved. Its association fallback nevertheless used the nearest selector instead of the observed source key.

`RoofAttachedManualPhysicalBuilder.TryAppend` validated RelativeSegment but built physical geometry by lifting the displaced absolute Plan onto a roof face. An XY displacement along the roof slope changed physical Z; a displaced segment could also resolve against another face. The initial regression reproduced a top vertex changing from Z=3000 to Z=10000 for an XY-only COPY.

## Production change and authoritative seams

Production changes were required. Eight production files changed:

- `src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualCopyCloneReinitializeService.cs`: `TryReinitializeClone` selects only the exact inherited key, creates a fresh binding/UUID, and inherits the source physical reference. `Process` rejects an appended clone whose source frame is unavailable instead of leaving duplicate inherited identity.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofAttachedManualLifecycleService.cs`: `RefreshModifiedAttachedManualRelatives` retains the pre-rigid-edit physical reference on MOVE. The existing private `TrySelectNearestCopyAnchor` now delegates to the retained-key policy; its historical method name remains to keep the surrounding source-contract boundaries stable.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedRafterCopyOwnershipRehydrationService.cs`: Generated COPY captures the source reference segment; `TryResolveCopyCloneAnchor` resolves only the observed exact source key. Its existing generic-timber detach fallback remains.
- `src/AcKrovy.Core/Services/Roofs/RoofAttachedManualReanchorRules.cs`: new `SelectRetainedAnchor`. Exact key + valid finite frame wins regardless of candidate order, distance, child orientation or station number. Missing/degenerate exact key returns null. Existing nearest/MIRROR policies remain unchanged for their other callers.
- `src/AcKrovy.Core/Models/Roofs/RoofAttachedManualTimberData.cs`: optional `PhysicalReferenceSegment`.
- `src/AcKrovy.Core/Models/Roofs/RoofAttachedManualTimberDataSchema.cs`: AttachedManual schema v5.
- `src/AcKrovy.Core/Services/Roofs/RoofAttachedManualTimberDataCodec.cs`: backward-compatible reads of v1–v4; v5 stores six optional reference coordinates, validates complete/finite reference data and preserves it through serialization.
- `src/AcKrovy.Core/Services/Roofs/RoofAttachedManualPhysicalBuilder.cs`: `TryAppend` resolves the reference body and accepts only a pure XY translation from that reference to current Plan. `TranslateCopy` translates every solid vertex, source prism, cut polygon, cut-plane point and contact edge together. Z, normals, section, slope and resolved shape remain unchanged. Physical identity still comes from the child's semantic UUID.

The authoritative sequence remains native final Plan → existing COPY/MOVE metadata capture → existing ordinary physical reconcile → existing annotation/GROUP finalization. No second annotation mechanism or restore owner was added. Structural, LOCK, ManualStructural COPY and the existing Generated MOVE implementation were not edited.

## Reference and recovery semantics

`RelativeSegment` is the current Plan pose in the retained anchor frame. `PhysicalReferenceSegment` is the pose before the sequence of rigid COPY/MOVE displacements. They must be separate: the current Plan alone cannot retain the old physical elevation.

- Generated → AttachedManual: reference is the Generated source axis; existing generated physical geometry is reused when it matches the reference and section. This also respects the accepted Generated MOVE WIP body.
- AttachedManual A → B: B gets a fresh semantic identity; owner/anchor/reference are inherited. A is not rewritten. A legacy source uses its persisted pre-copy RelativeSegment as the reference.
- MOVE: same identity and anchor; current RelativeSegment changes, physical reference stays fixed. Repeated MOVE therefore does not accumulate Z drift.
- Existing non-rigid/in-place MIRROR metadata factory paths leave the optional physical reference absent and retain their existing geometry solver.
- Missing anchor: no nearest replacement is selected. MOVE can recover the same old basis through the existing pre-command snapshot. Physical reconstruction can recover that same basis from persisted relative/current world geometry and the reference when topology resolves it; invalid reference/unsolvable geometry fails closed. New COPY with an unavailable exact live anchor rejects the appended clone. No new cross-key recovery is introduced.

There is no bulk migration or repair of already corrupted legacy source solids. Copying a legacy source preserves its existing reference geometry; the requested fresh Generated → A → B → MOVE chain is the acceptance case for correct initial Z.

## Tests

New `src/AcKrovy.Core.Tests/RoofAttachedManualAnchorStabilityTests.cs`: **9 behavioral cases**.

- Cross-frame source s12 / perpendicular alternate s2: demonstrates that the unchanged MIRROR selector chooses s2 while COPY's retained selector keeps s12; MOVE and reversed candidate order keep the same source frame. Relative capture/replay preserves world endpoints.
- Six lower-cut/ridge combinations: Generated COPY, AttachedManual COPY, repeated MOVE, source-body invariance, UUID independence, unique physical keys, codec round-trip, complete body/cut translation and unchanged Z/section/slope.
- Those six cases also cover the HOST-style perpendicular legacy child: COPY and MOVE preserve its reference body rather than replacing it with the anchor's orientation.
- Same-frame physical recovery without a live Generated member or layout slot reproduces the complete body for all six combinations.
- Missing/degenerate exact anchor returns null even with a valid perpendicular candidate (two cases).

Before production changes, the initial seven cases failed: one rejected the source frame and six exposed the Z jump. Final new cases pass. Existing behavioral geometry/lifecycle suites remain active. Wiring assertions were updated for retained selection; schema-freeze assertions were updated only for AttachedManual v5.

Additional changed test files (11): `AsymmetricGableRoofFoundationTests.cs`, `MonopitchRafterStage2D1SourceContractTests.cs`, `MonopitchRafterStage2D3Tests.cs`, `MonopitchRafterStage2D4AClipboardTests.cs`, `MonopitchRafterStage2D4BClipboardTests.cs`, `MonopitchRoofStage1SourceContractTests.cs`, `RoofAttachedManualCopyCloneSourceContractTests.cs`, `RoofAttachedManualPhysicalLifecycleTests.cs`, `RoofCopyReplayMetadataSourceContractTests.cs`, `RoofGeometryDialogSourceContractTests.cs`, `RoofSplitAttachedManualSourceContractTests.cs` (all under `src/AcKrovy.Core.Tests/`). Other schema/version assertions were preserved.

## Validation

AutoCAD process check before linked builds: no running `acad` process. No HOST test was performed.

| Check | Result |
| --- | --- |
| Focused AttachedManual / ordinary COPY, MOVE, physical / Structural / MIRROR / split | PASS, 871/871 |
| Full Core | PASS, 7249/7249 |
| Full WPF | PASS, 826/826 |
| Debug x64, warnings as errors | PASS, 0 warnings / 0 errors |
| Release x64, warnings as errors | PASS, 0 warnings / 0 errors |
| Portable Compatibility Gate | PASS, 7249 tests; 0 warnings / 0 errors |
| Full Compatibility Gate | PASS, Core 7249 + WPF 826 = 8075 tests; 0 warnings / 0 errors |
| CAD-neutral dependency scan | PASS through compatibility gates |
| `git diff --check` | PASS |
| Initial dirty/untracked files | SHA-256 preserved, 51/51 |
| HOST validation | NOT RUN |

Commands executed (final validation):

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj -c Debug -m:1 --logger 'console;verbosity=minimal'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj -c Debug -m:1 --no-build --filter 'FullyQualifiedName~RoofAttachedManual|FullyQualifiedName~RoofCopy|FullyQualifiedName~RoofOrdinary|FullyQualifiedName~RoofAutomaticRafterPhysical|FullyQualifiedName~RoofStructural|FullyQualifiedName~RoofMirror|FullyQualifiedName~RoofSplit' --logger 'console;verbosity=minimal'
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj -c Debug -p:Platform=x64 -m:1 --no-build --logger 'console;verbosity=minimal'
dotnet build AcKrovy.sln -c Debug -p:Platform=x64 -warnaserror -m:1
dotnet build AcKrovy.sln -c Release -p:Platform=x64 -warnaserror -m:1
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
git diff --check
git status --short
```

Detailed command logs: `%TEMP%/krovy-attached-anchor-20261002/`.

**MANUAL_STRUCTURAL_COPY_PLACEMENT WIP preserved exactly.**

**ORDINARY_GENERATED_MOVE_PHYSICAL3D WIP preserved exactly.**

All nine code/test/report files belonging to those two fixes were independently SHA-256 checked. All 51 initial dirty/untracked files also retain their original bytes. No commit, push, tag, reset, revert, stash or clean was performed.

Schema impact: v5 metadata requires this updated reader; v1–v4 remain readable. Localization/resources/UI were not changed. Release verdict remains NOT READY FOR HOST-AFFECTING CHANGE until the single acceptance retest succeeds.

## Exactly one HOST acceptance retest

1. On one Unlocked roof, select one ordinary Generated rafter.
2. Native COPY once → AttachedManual A; include an XY displacement along the slope direction.
3. Native COPY A again → AttachedManual B.
4. MOVE B by another XY displacement.
5. Run `AK_ROOF_3D_AUDIT`.

Acceptance: A/B remain parallel; no 90° turn; B inherits A's anchor and MOVE keeps it; source Generated/A remain unchanged; distinct semantic/physical keys; Physical3D follows the XY displacement with unchanged slope/section/Z; no huge Z jump; source/clone annotations canonical with no orphans; GROUP `duplicates=0`, `missing=0`, `foreign=0`, `canonical=True`. No additional topology or SAVE/REOPEN retest is requested.

**CODE-SIDE VERDICT: PASS ✅**

## Final `git status --short`

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
