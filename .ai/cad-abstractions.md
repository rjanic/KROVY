# CAD abstraction guidance for AI agents

## Purpose

Keep portable domain and layer decisions independent of AutoCAD runtime objects so a future host adapter can implement the same contracts.

## Hard rules

- Autodesk namespaces, `Database`, `Transaction`, `ObjectId`, `Point3d`, `LayerTable` and `LinetypeTable` belong only in `AcKrovy.AutoCAD`.
- `ICadLayerService<TEntity>` and CAD-neutral layer values belong in `AcKrovy.Cad.Abstractions`.
- Persist an ACI index (1-255), not an Autodesk color object.
- Timber entities use `ByLayer`; `LinetypeScale` is an entity property, not a layer property.
- Read-only queries must not acquire a write transaction, create records, write XData or change DBMOD.
- Mutating adapter workflows require the appropriate `DocumentLock` and transaction.

## Current architecture or workflow

`ICadLayerService<TEntity>` accepts a CAD-neutral `ElementLayerProfile` and assigns a host entity without exposing host identifiers. `AciColorPalette`, picker rules, layer-name rules, equivalence/conflict rules and suffix rules remain portable.

`TimberLayerService` is the AutoCAD adapter. It reads `LayerTable` and `LinetypeTable`, normalizes physical appearance, resolves/reuses matching canonical-family layers, creates a suffix only for a real property override, and assigns entity color/linetype as ByLayer while applying `LinetypeScale` to the entity. Selecting a layer in Settings uses a read-only catalog/hydration workflow; Apply owns the write path.

Timber and annotation stores serialize CAD-neutral JSON into XData. The host `ObjectId` is used only to open an object inside the current database/transaction. Persisted ownership uses `SourceHandle`, and metadata is prepared for the current schema only on write.

COPY/COPYCLIP/WBLOCK portability relies on data traveling with entities and on lifecycle cleanup/reconciliation by source handle. A local profile must never be needed to interpret copied timber metadata.

`LiveGeometrySynchronizationService` subscribes to AutoCAD `ObjectModified` and `CommandEnded`. It records relevant user changes, then reconciles after the command. Programmatic annotation work is suppressed so it is not interpreted as STRETCH/manual offset.

`AK_SELECTSIMILAR` and `AK_EXPORTCSV` use the existing model-space scanner and open candidate entities only for read. Setting an implied selection or writing an external CSV is not a DWG mutation; neither workflow may hydrate missing metadata or commit a database transaction.

For a future BricsCAD or ZWCAD adapter, retain Core and CAD abstractions unchanged. Implement host locking, transactions, tables, event mapping, XData and geometry conversions behind a new adapter; do not add vendor conditionals to Core.

## Never do this

- Put `Autodesk.AutoCAD.Geometry.Point3d` in a Core model or calculator.
- Store an `ObjectId` in JSON, settings, metadata or a report.
- hardcode an absolute path to `acadiso.lin`.
- Assign an explicit linetype to a timber entity instead of ByLayer.
- Treat layer `LinetypeScale` as if it were a `LayerTableRecord` property.
- Change an incompatible MLeader `ContentType` in place; recreate the owned annotation safely.
- Interpret a programmatic `ObjectModified` callback as a user STRETCH.
- Create a missing layer or linetype while merely opening Settings or selecting a ComboBox item.
- Rewrite old metadata merely because it was read.

## Common failure modes

- Silent DBMOD changes from “read” helpers that open objects for write.
- Chained suffixes such as `KROV_CUSTOM_01_01` because the canonical family was not recognized.
- Persisting adapter-only values and making clipboard/WBLOCK data machine-specific.
- Starting a transaction without the document lock in a modeless/UI callback.
- Mixing event suppression scope with user-command tracking and losing manual offsets.

## Checklist before changing this area

- Decide whether the code is portable policy or host implementation.
- Check references and source namespaces with the Portable Compatibility Gate.
- Verify read paths stay read-only and repeated Apply is idempotent.
- Verify ACI range, ByLayer and entity `LinetypeScale`.
- Test COPY/COPYCLIP/WBLOCK/SAVE-REOPEN and event suppression when ownership changes.
- Plan the equivalent contract for another CAD host before widening an abstraction.

## Relevant source files and tests

- `src/AcKrovy.Cad.Abstractions/Layers/ICadLayerService.cs`
- `src/AcKrovy.Cad.Abstractions/Layers/ElementLayerProfile.cs`
- `src/AcKrovy.Cad.Abstractions/Layers/AciColorPalette.cs`
- `src/AcKrovy.Cad.Abstractions/Layers/CadLayerNameRules.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/TimberLayerService.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/TimberElementStore.cs`
- `src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs`
- `src/AcKrovy.Core.Tests/ElementLayerProfileTests.cs`
- `src/AcKrovy.Core.Tests/SettingsTargetedFeatureTests.cs`
- `scripts/compatibility-gate.ps1`

See [architecture.md](architecture.md) for ownership and dependency rules.
