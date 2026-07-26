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
- `AcKrovy.Cad.Abstractions`: portable CAD-facing contracts and values. It references Core.
- `AcKrovy.Localization`: resource lookup, culture switching and display-name providers. It references Core but no CAD adapter.
- `AcKrovy.AutoCAD`: AutoCAD commands, transactions, XData stores, geometry/annotation services, settings persistence and WPF UI. It references Core, CAD abstractions and localization.
- `AcKrovy.Core.Tests`: portable unit, localization, architecture and source-contract tests.
- `AcKrovy.Wpf.Tests`: Windows/AutoCAD-linked XAML/BAML runtime smoke tests.

Allowed direction:

`AcKrovy.AutoCAD -> AcKrovy.Localization / AcKrovy.Cad.Abstractions -> AcKrovy.Core`

Localization also depends directly on Core. Core never depends back on any of these projects.

### Domain and persisted identity

- `ElementId` is a logical timber identity used by numbering/reporting. `SourceHandle` binds annotations to one physical DWG source entity.
- `TimberElementSignature` derives the stable manufacturing grouping key from measurements. Stable item numbering reuses compatible existing assignments.
- `TimberElementDataSchema.CurrentVersion` governs the timber XData JSON payload. Older supported payloads are normalized and upgraded only when written (`PrepareForWrite`); reads do not rewrite the DWG.
- Preserve backward-compatible defaults when adding optional metadata. Bump the schema only when the payload contract structurally changes.

### Annotation architecture

- `TimberAnnotationMode` and `TimberAnnotationModeRules` map modes to `FullLabel`, `ItemNumberLeader`, `DimensionsLeader`, `NoAnnotations`, or a combined representation.
- `NoAnnotations` removes source-bound annotation families without deleting timber metadata, identity, numbering or report data.
- Standalone `ItemCircle`, `ItemRectangle` and `ItemSlot` use the native block-content MLeader workflow.
- Combined dimensions-plus-framed-item modes use a separate composite workflow: framed MLeader plus a dimensions MText component. Do not collapse these branches.
- Annotation stores bind helpers through `SourceHandle`. `ElementLabelService`, `SlopeAnnotationService` and `PostFootprintPerpendicularAnnotationService` own creation, update and cleanup of their entity families.
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
- Reusing the standalone framed implementation for combined annotations and losing the dimensions MText lifecycle.
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
- `src/AcKrovy.AutoCAD/Infrastructure/ElementLabelService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs`
- `src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs`
- `src/AcKrovy.AutoCAD/UI/LayerSettingsWindow.xaml`
- `src/AcKrovy.AutoCAD/UI/LayerSettingsWindow.xaml.cs`
- `src/AcKrovy.Core.Tests/TimberElementDataVersioningTests.cs`
- `src/AcKrovy.Core.Tests/TimberAnnotationModeTests.cs`
- `src/AcKrovy.Core.Tests/SettingsTargetedFeatureTests.cs`

See also [cad-abstractions.md](cad-abstractions.md), [localization.md](localization.md), and [testing.md](testing.md).
