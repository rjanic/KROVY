# Architecture guidance for AI agents

## Purpose

Preserve the CAD-neutral domain model, stable persisted data, and the separation between reusable logic and the AutoCAD host while changing ACAD KROVY.

## Hard rules

- `AcKrovy.Core` must not reference Autodesk, the AutoCAD adapter, or localization.
- Manufacturing calculations and domain rules use only CAD-neutral types.
- Dependencies point toward `AcKrovy.Core`; never add a reverse dependency from Core to an adapter or UI.
- Persist stable enum values and identifiers, never localized display text.
- Never persist AutoCAD `ObjectId` in a local profile. It is valid only inside the current database session.
- Keep application versions in `Directory.Build.props`; do not scatter hardcoded product versions.
- A read-only UI operation, including selecting or hydrating a layer, must not lock or modify the DWG.
- Keep metadata schema version independent from the layer-profile version and UI-preference storage.

## Current architecture or workflow

### Project dependencies

- `AcKrovy.Core`: CAD-neutral models, calculations, metadata versioning, numbering, annotation planning and lifecycle rules.
- `AcKrovy.Infrastructure`: host-neutral diagnostics, recoverable local-settings loading and safe file replacement. It references no Autodesk project.
- `AcKrovy.Cad.Abstractions`: portable CAD-facing contracts and values. It references Core.
- `AcKrovy.Localization`: resource lookup, culture switching and display-name providers. It references Core but no CAD adapter.
- `AcKrovy.AutoCAD`: AutoCAD commands, transactions, XData stores, geometry/annotation services, settings persistence and WPF UI. It references Core, CAD abstractions and localization.
- `AcKrovy.Core.Tests`: portable unit, localization, architecture and source-contract tests.
- `AcKrovy.Wpf.Tests`: Windows/AutoCAD-linked XAML/BAML runtime smoke tests.

Allowed direction:

`AcKrovy.AutoCAD -> AcKrovy.Infrastructure / AcKrovy.Localization / AcKrovy.Cad.Abstractions -> AcKrovy.Core`

Localization also depends directly on Core. Core never depends back on any of these projects.

### Productivity and reliability

- `TimberElementSimilarityFilter` and `TimberCsvFormatter` are CAD-neutral Core services. Host selection, scanning and save dialogs remain in the AutoCAD adapter.
- `RecoverableSettingsStore` protects every local JSON store. A corrupt or unreadable original must be preserved before a later save may replace it; a failed backup blocks disk writes for that file while defaults remain usable in memory.
- `FileDiagnosticLogger` writes sanitized daily logs under `%LOCALAPPDATA%\ACAD_KROVY\Logs`, rotates at 5 MB, retains 14 days and must never throw into plug-in code.
- `AK_SELECTSIMILAR` and `AK_EXPORTCSV` are read-only with respect to the DWG. They may read model-space entities and change the editor implied selection, but must not create/repair metadata or open entities for write.

### Domain and persisted identity

- `ElementId` is a logical timber identity used by numbering/reporting. `SourceHandle` binds annotations to one physical DWG source entity.
- `TimberElementSignature` derives the stable manufacturing grouping key from measurements. Stable item numbering reuses compatible existing assignments.
- `TimberElementDataSchema.CurrentVersion` governs the timber XData JSON payload. Older supported payloads are normalized and upgraded only when written (`PrepareForWrite`); reads do not rewrite the DWG.
- Preserve backward-compatible defaults when adding optional metadata. Bump the schema only when the payload contract structurally changes.

### Annotation architecture

- `TimberAnnotationMode` and `TimberAnnotationModeRules` map modes to `FullLabel`, `ItemNumberLeader`, `DimensionsLeader`, `NoAnnotations`, or a combined representation.
- `NoAnnotations` removes source-bound annotation families without deleting timber metadata, identity, numbering or report data.
- Standalone `ItemCircle`, `ItemRectangle` and `ItemSlot` use the native block-content MLeader workflow.
- Combined dimensions-plus-framed-item modes use one native G5 BlockContent MLeader (`ITEM_NO` / `WIDTH` / `HEIGHT` AttrDefs in immutable R3_RIGHT / R3_LEFT BTR variants). WIDTH/HEIGHT must lie on the knee side of the frame (`dot(D−F, K−F) > 0`). R3_RIGHT = −local X (PASS when knee is on −local X of frame); R3_LEFT = +local X (when knee is on +local X). Variant selection uses final knee→frame landing projected onto effective block-local +X — not world/source Left/Right alone. Text is never mirrored; leader Left/Right is ModelSpace geometry. R3 Combined separates (A) leader geometry `TransformBy(readable)` + CREATE 60° / straight landing, (B) toward-knee R3_RIGHT/LEFT, (C) content presentation via `TimberFramedBlockContentReadableOrientationRules.Decide` → `R3ContentOrientationDecision` (`PhysicalAxisAngle`, `PresentationAngle`, `ReadableFlip`, `IncomingLandingSide`). Presentation half-plane is [−90°, +90°], with one R3 construction-drawing boundary rule: exact 90° and 270° both present at −90°; 89°/91° and 269°/271° retain normal folding. After CREATE geometry and the provisional R3 variant are final, exact physical 90° and 180° references alone measure the live frame world +X and receive `desiredWorld=currentWorld+180°` through a relative `BlockRotation` delta. Because that half-turn reverses block-local ±X, the existing final-geometry R3 resolver then selects the opposite RIGHT/LEFT variant and the exact relative BlockRotation target is reasserted; this preserves `dot(D−F,K−F)>0` without touching leader geometry. CREATE otherwise keeps `BlockRotation = 0` after TransformBy (G5C contract) — never post-hoc `BlockRotation = NormalizeReadableAngle` after R3. The managed MLeader API exposes no `BlockTransform`; its effective BTR-to-world basis is implicit in the transformed MLeader and must be measured from final AttrRef world geometry. Source-stretch / live in-place refresh measures the live world content axis before and after BTR/AttrRef content updates. With no source-axis edit it keeps the exact pre-refresh world presentation and applies only `desiredWorld−measuredWorldAfter` to the current relative `BlockRotation`; on a true readable-axis change it adopts `Decide(new)` through the separate source-rotation lifecycle. Do not re-`Decide()`/`NormalizeReadable` from knee/landing on that path. Refresh/rotation paths that rebuild points without TransformBy may set presentation `BlockRotation`, then sync R3. AttrDef rotations stay 0. Readable correction must not move attachment, knee, landing, or BlockPosition. After annotation knee STRETCH, production GripOverrule runs native `MoveGripPointsAt` (leader geometry authority — no vertex rewrite), measures the current final world content +X from ITEM_NO/WIDTH/HEIGHT AttrRef geometry, resolves `Decide(final landing)`, and applies only `delta = desiredWorld − currentWorld` to the existing BlockRotation. This avoids composing a desired absolute world angle with the pre-existing TransformBy basis. R3_RIGHT↔R3_LEFT is selected on that same desired final world axis and the swap restores only content scale/rotation/BlockPosition — never leader vertices or dogleg. Source-axis length-only refresh remains a separate preserve lifecycle and must not be conflated with knee grip. CREATE applies a one-shot finalization so the FINAL world first-segment angle is 60° ±0.01° and reseats landing/BlockPosition along readable +T from the corrected knee (second segment stays straight; WIDTH/HEIGHT remain on that landing axis). After that CREATE geometry is final, the same content-variant ensure as grip (`EnsureCorrectR3ContentVariantFromFinalGeometry` → `TrySwapR3ContentVariantIfSideChanged`) re-resolves R3_RIGHT/LEFT from final knee/frame in AttrDef space — early DesiredWorldSide pick is only a provisional insert default. Combined Plain keeps the native MText MLeader + dimensions MText composite. ItemNumberLeader framed (Iba popis) stays on the G4 composite path.
- Annotation stores bind helpers through `SourceHandle`. `ElementLabelService`, `SlopeAnnotationService` and `PostFootprintPerpendicularAnnotationService` own creation, update and cleanup of their entity families.
- Production R3 Combined treats a persisted-to-current physical Start→End source-direction change above tolerance as a rebuild boundary: the exact owned `SourceHandle + CombinedRole` MLeader is erased and recreated through the canonical CREATE service with clean default placement. G5 metadata uses the existing `RotationRadians` field for the physical source direction and keeps `PlacementRotationRadians` exclusively for the readable layout angle; detector inputs must never mix those semantics. The comparison uses the normalized directed physical delta modulo 2π, including sensible ±180° wrap-around, while legacy payloads that wrote readable rotation into both fields are adopted through the nearest π-equivalent physical line axis without a refresh-only false rebuild. Exact +90°/−90° keep their distinct vertical CREATE-family semantics. Length-only source edits, annotation grip moves, ordinary refresh, COPY and WBLOCK remain on their existing non-rebuild lifecycles. No old→new knee/landing transform, presentation compensation, BTR repair or whole-MLeader rotation is used as a source-rotation repair.
- G5 annotation metadata schema 5 persists `R3ReferencePresentationRevision`. Existing content-presentation adoption remains at revision 2. The host-proven whole-annotation correction uses internal revision 3 to mean that one rigid `MLeader.TransformBy(Matrix3d.Rotation(π, ZAxis, attachment))` is present. Exact source +90°/-90° require revision 3; every other source angle requires the untransformed state. CREATE applies the transition only after 60°, BTR, AttrRef and presentation finalization. Refresh/source rotation applies a half-turn only when required and stored states differ, so repeated refresh is idempotent, vertical→nonvertical removes the correction once, and +90°→-90° performs no second whole transform. The attachment and rigid K→D→I geometry are verified before the state revision is written; no metadata schema field or schema-version bump is introduced.
- `TimberAnnotationRefreshPlanner`, matching/cleanup rules and composite lifecycle rules protect COPY/COPYCLIP/WBLOCK/SAVE-REOPEN portability and prevent duplicates or orphans.
- `LiveGeometrySynchronizationService` handles user geometry changes and keeps STRETCH/manual offsets coherent while suppressing programmatic modifications.

### Settings architecture

- `ElementLayerProfile` is the local versioned layer profile (`CurrentVersion` is its own contract). `ElementLayerProfileStore` persists it outside the DWG.
- Timber metadata schema is unrelated to the settings profile version.
- `SettingsUiPreferencesStore` persists UI-only choices such as Light/Dark theme separately; these never bump metadata schema.
- `LayerSettingsWindow.xaml` contains the Fashion Look layout and resource-based design tokens. `SettingsVisualStateViewModel` exposes visual state; code-behind coordinates host workflows and runtime localization.
- Section footers intentionally have distinct actions: layers and manufacturing save/apply; annotation supports new-only, selection and all; language closes after using the existing runtime culture workflow.
- Layer Apply resolves physical layers through `TimberLayerService`, then returns accepted names so the profile and row baselines match reused or generated suffixes.

## Common failure modes

- Treating `ElementId` as a unique physical DWG owner and stealing annotations after COPY.
- Updating metadata during a read and unexpectedly changing DBMOD.
- Reusing the standalone framed ItemOnly implementation for Combined annotations and losing WIDTH/HEIGHT AttrRefs.
- Letting framed Combined refresh fall back to the G4 multi-entity composite.
- Rebuilding a block-content MLeader in place with an incompatible content type.
- Letting programmatic `ObjectModified` events look like user STRETCH.
- Coupling theme/runtime localization state to the metadata or layer-profile schema.

## Checklist before changing this area

- Confirm the dependency direction and run both compatibility gates.
- Identify whether a change affects domain data, local profile data, UI preferences, or only presentation.
- Preserve `ElementId`, `SourceHandle`, signature and update-on-write semantics.
- Exercise COPY/COPYCLIP/WBLOCK/SAVE-REOPEN and STRETCH lifecycle rules when annotations change.
- Check repeated Apply for idempotency and duplicate/orphan cleanup.
- Update relevant tests and documentation without recording transient test counts.

## Relevant source files and tests

- `src/AcKrovy.Core/Models/TimberElementData.cs`
- `src/AcKrovy.Core/Models/TimberElementDataSchema.cs`
- `src/AcKrovy.Core/Models/TimberElementSignature.cs`
- `src/AcKrovy.Core/Services/TimberElementDataVersioning.cs`
- `src/AcKrovy.Core/Services/TimberAnnotationModeRules.cs`
- `src/AcKrovy.Core/Services/TimberAnnotationRefreshPlanner.cs`
- `src/AcKrovy.Core/Services/TimberElementSimilarityFilter.cs`
- `src/AcKrovy.Core/Services/TimberCsvFormatter.cs`
- `src/AcKrovy.Infrastructure/Diagnostics/FileDiagnosticLogger.cs`
- `src/AcKrovy.Infrastructure/Settings/RecoverableSettingsStore.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/ElementLabelService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs`
- `src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs`
- `src/AcKrovy.AutoCAD/UI/LayerSettingsWindow.xaml`
- `src/AcKrovy.AutoCAD/UI/LayerSettingsWindow.xaml.cs`
- `src/AcKrovy.Core.Tests/TimberElementDataVersioningTests.cs`
- `src/AcKrovy.Core.Tests/TimberAnnotationModeTests.cs`
- `src/AcKrovy.Core.Tests/SettingsTargetedFeatureTests.cs`

See also [cad-abstractions.md](cad-abstractions.md), [localization.md](localization.md), and [testing.md](testing.md).
