# Testing guidance for AI agents

## Purpose

Choose the smallest useful feedback loop during development and the complete mandatory gate before committing a release milestone.

## Hard rules

- A failing test, build, warnings-as-errors check or compatibility gate blocks commit and push.
- Do not claim host behavior from portable unit tests alone.
- Keep warnings as errors in final builds/gates.
- Run `git diff --check` before every release commit.
- Record exact test counts in release reports, not as durable architecture rules.

## Current architecture or workflow

### Test layers

- Core unit tests: domain calculations, metadata, numbering, annotation planning/lifecycle, layer rules and profile migration in `AcKrovy.Core.Tests`.
- Localization tests: key/placeholder parity, six language packs, display providers and runtime switching.
- Architecture/compatibility tests: portable dependency rules plus version/manifest checks in `scripts/compatibility-gate.ps1`.
- Productivity/reliability tests: CAD-neutral similarity filters and CSV formatting, settings recovery, logger concurrency/rotation/privacy, and AutoCAD source contracts.
- XAML/BAML runtime smoke tests: instantiate compiled Settings and ACI picker resources in `AcKrovy.Wpf.Tests`; these catch runtime parsing and embedded-image failures.
- AutoCAD Core Console/runtime probes: use when host APIs and DWG persistence can be exercised non-interactively.
- Manual AutoCAD host tests: visual and command lifecycle cases that require a real document/editor.

### When a unit test is insufficient

Use a runtime or host test for WPF `XamlParseException`, actual MLeader API behavior (including `SetDogleg`), annotative entities, `ObjectModified`/`CommandEnded` STRETCH lifecycle, DBMOD, COPYCLIP/WBLOCK database cloning and AutoCAD support-path/resource loading. Source-contract tests are useful guards but are not proof of host geometry.

### Mandatory regressions

- COPY, COPYCLIP/PASTECLIP, SAVE/REOPEN and WBLOCK.
- MOVE, ROTATE and STRETCH/manual offset.
- `AK_RENUMBER`.
- No duplicate or orphan annotations.
- Runtime localization switching and all six language packs.
- Open/close Settings without Apply.
- Repeated Selection and All Apply.
- Layer-profile migration and repeated layer Apply.

### Efficient development cycle

During iteration:

1. Run tests filtered to the changed feature/classes.
2. Build Debug x64.
3. Run `git diff --check`.

Before a version commit:

1. Run all targeted Settings, layer, localization and annotation groups.
2. Run the complete solution test suite.
3. Build Debug x64 with warnings as errors.
4. Build Release x64 with warnings as errors.
5. Run `scripts/compatibility-gate.ps1 -Portable`.
6. Run `scripts/compatibility-gate.ps1 -Full`.
7. Run `git diff --check`, `git status --short` and `git diff --stat`.

## Common failure modes

- Using `--no-build` after changing sources and testing stale binaries.
- Counting source-text assertions as AutoCAD runtime verification.
- Running only Core tests after changing compiled XAML.
- Treating manual COPY/WBLOCK/STRETCH scenarios as automated when no host probe ran.
- Ignoring warnings because ordinary build succeeds without `-warnaserror`.

## Checklist before changing this area

- Map the change to the appropriate test layer.
- Add a regression that fails for the original defect.
- Include WPF runtime tests for XAML/resources.
- Identify host-only checks explicitly.
- Run targeted tests while iterating and the full matrix before commit.
- Report failures honestly and do not commit around them.

## Relevant source files and tests

- `src/AcKrovy.Core.Tests/SettingsTargetedFeatureTests.cs`
- `src/AcKrovy.Core.Tests/SettingsFashionLookTests.cs`
- `src/AcKrovy.Core.Tests/SettingsRuntimeLocalizationTests.cs`
- `src/AcKrovy.Core.Tests/TimberAnnotationModeTests.cs`
- `src/AcKrovy.Core.Tests/TimberFramedLeaderPlacementTests.cs`
- `src/AcKrovy.Core.Tests/TimberElementSimilarityFilterTests.cs`
- `src/AcKrovy.Core.Tests/TimberCsvFormatterTests.cs`
- `src/AcKrovy.Core.Tests/RecoverableSettingsStoreTests.cs`
- `src/AcKrovy.Core.Tests/FileDiagnosticLoggerTests.cs`
- `src/AcKrovy.Core.Tests/SafeFileWriterTests.cs`
- `src/AcKrovy.Core.Tests/ProductivityCommandSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/DiagnosticsWindowSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/EditCommandSourceContractTests.cs`
- `src/AcKrovy.Core.Tests/TimberElementEditRulesTests.cs`
- `src/AcKrovy.Core.Tests/TimberEditSelectionRulesTests.cs`
- `src/AcKrovy.Core.Tests/ApplicationLanguagePersistenceSourceContractTests.cs`
- `src/AcKrovy.Wpf.Tests/SettingsXamlRuntimeSmokeTests.cs`
- `src/AcKrovy.Wpf.Tests/ApplicationLanguageWorkflowTests.cs`
- `src/AcKrovy.Wpf.Tests/ProductivityWindowsSmokeTests.cs`
- `src/AcKrovy.Wpf.Tests/LayerSettingsRowHydrationTests.cs`
- `scripts/compatibility-gate.ps1`
- `docs/COMPATIBILITY_GATE.md`
- `docs/TEST_SCENARIO_008_PRODUCTIVITY_RELIABILITY.md`
