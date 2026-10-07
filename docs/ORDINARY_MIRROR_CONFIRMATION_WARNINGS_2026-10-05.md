# Ordinary MIRROR confirmation and shared warning preferences — 2026-10-05

The later [Ordinary ERASE / MIRROR rebuild contract](ORDINARY_ERASE_MIRROR_REBUILD_2026-10-05.md) supersedes this report's source-slot suppression behavior. Accepted AUTO MIRROR erase-source YES deletes the current source package without writing suppression; explicit roof regeneration recreates its AUTO slot. Shared warning preferences and exact KROVY NO rollback remain applicable.

## Implemented behavior

AUTO Ordinary MIRROR now makes one operation-wide detach decision before creating identities, rebuilding Physical3D, suppressing the source slot or recalculating designations. With confirmation enabled it reuses the existing KROVY wood-style Áno/Nie dialog. Áno continues the previously accepted package lifecycle. Nie erases the mapped Ordinary native clones and restores source entity snapshots, roof definitions and exact GROUP membership in one transaction, including erased-source and in-place variants. Verification precedes commit; NO diagnostics follow commit. Independent-only MIRROR does not prompt. Physical-only rejection retains its existing restoration behavior.

AK_SETTINGS has a new localized **Upozornenia a potvrdenia** navigation section. One CAD-neutral `WarningPreferences` contract contains `ConfirmAutomaticMemberDetach` and `WarnDerived3DEdit`, both default ON. These preferences are stored in the existing `SettingsUiPreferencesStore` / `settings-ui.json`, through its existing recoverable settings infrastructure. No extra file or persistence system was introduced. Old settings without warning fields default ON; explicit OFF values survive serialization/reload. Settings controls save immediately as application preferences; they never dispatch a DWG Apply. Window-close persistence preserves the current warning and other preference values.

The shared `MemberWarningPreferenceService` replaces the two existing detach-dialog entry points (Ordinary MOVE/ROTATE path and shared GRIP_STRETCH/STRETCH/TRIM/EXTEND/BREAK handler) and serves MIRROR. The existing derived-3D warning service delegates to it for all its current consumers. No new ROTATE lifecycle or geometry path was added. Future equivalent commands must use this same service.

Both dialogs include the wood-style “Upozornenie nabudúce nezobrazovať” checkbox. Detach opt-out is saved only after Áno; Nie and closing the confirmation do not change the preference. With confirmation OFF the decision is YES with `automaticConfirm=True`. With the 3D warning OFF the edit is still rejected/restored, and a short localized command-line notification replaces the modal warning. Reset warnings enables both preferences. Preference-save failures report an error without changing the geometry decision or allowing a derived-3D edit.

MIRROR diagnostics include `decision`, `detached`, `rollback`, `confirmationShown`, `automaticConfirm` and `warningPreferenceChanged`. NO explicitly reports no rebuild, designation change, manual override or new Independent ID. Existing confirmation handlers emit one shared `ROOF_MEMBER_CONFIRMATION` record rather than adding per-entity noise.

## Files changed in this task

Paths below are relative to `C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy`. Pre-existing unrelated WIP was preserved.

- Core: `src/AcKrovy.Core/Models/WarningPreferences.cs`, `src/AcKrovy.Core/Services/WarningPreferenceRules.cs`.
- AutoCAD settings: `src/AcKrovy.AutoCAD/Settings/SettingsUiPreferencesStore.cs`.
- AutoCAD lifecycle: `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryCopyLifecycleService.cs`, `RoofOrdinaryGripLifecycleService.cs`, `RoofGeneratedMemberManualEditService.cs` in that same directory.
- AutoCAD UI: `src/AcKrovy.AutoCAD/UI/MemberWarningPreferenceService.cs`, `LayerSettingsWindow.Warnings.cs`, `LayerSettingsWindow.xaml`, `LayerSettingsWindow.xaml.cs`, `SettingsVisualStateViewModel.cs`, `RoofIndependentOrdinaryDetachWindow.cs`, `RoofPhysical3DWarningWindow.cs`, `RoofPhysical3DWarningService.cs`.
- Localization: `src/AcKrovy.Localization/SettingsFashionLookRules.cs`, `SettingsWindowActionRules.cs`, and all six `Resources/UiStrings*.resx` language packs (eight new keys each).
- Core tests: `src/AcKrovy.Core.Tests/WarningPreferenceTests.cs`, `RoofOrdinaryMirrorLifecycleTests.cs`, `RoofOrdinaryCopyLifecycleTests.cs`, `RoofPhysical3DWarningSourceContractTests.cs`, `SettingsFashionLookTests.cs`, `LocalizationLanguagePackTests.cs`.
- WPF tests: `src/AcKrovy.Wpf.Tests/WarningSettingsPersistenceTests.cs`, `RoofIndependentOrdinaryDetachWindowTests.cs`, `RoofPhysical3DWarningWindowTests.cs`, `SettingsXamlRuntimeSmokeTests.cs`.
- Documentation: `docs/geometry/roof-elevation-contract.md`, the supersession note in `docs/ORDINARY_MIRROR_INDEPENDENT_2026-10-05.md`, and this report.

## Verification and commands

Repository root confirmed by `git rev-parse --show-toplevel`; branch `main`, HEAD `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`. Working tree was already dirty. No commit, push or tag.

```powershell
Get-Process acad -ErrorAction SilentlyContinue
git status --short
git rev-parse --show-toplevel
git branch --show-current
git rev-parse HEAD
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror --filter 'FullyQualifiedName~WarningPreferenceTests|FullyQualifiedName~RoofOrdinary|FullyQualifiedName~RoofIndependentOrdinary|FullyQualifiedName~SettingsFashionLookTests|FullyQualifiedName~LocalizationLanguagePackTests|FullyQualifiedName~RoofPhysical3DWarningSourceContractTests'
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror --filter 'FullyQualifiedName~RoofIndependentOrdinaryDetachWindowTests|FullyQualifiedName~RoofPhysical3DWarningWindowTests|FullyQualifiedName~SettingsXamlRuntimeSmokeTests|FullyQualifiedName~WarningSettingsPersistenceTests'
.\scripts\compatibility-gate.ps1 -Full
git diff --check
```

Targeted Core: PASS 449/449. Targeted WPF: PASS 25/25. Final Full Compatibility Gate, including Portable Gate: **PASS**, Core 7550/7550 and WPF 829/829, no skipped or failed tests, zero build warnings/errors, dependency leakage checks PASS. Final whitespace check PASS. The initial WPF compilation caught an ambiguous WPF/WinForms `CheckBox`; it was qualified. A later source guard used a fixed-width text slice and failed after diagnostic wiring; it now checks the enclosing MIRROR block. Both issues were corrected before the final passing gate.

Tests cover default/missing preference migration, persisted OFF values, YES/NO, YES opt-out and subsequent automatic acceptance, NO with checked opt-out preserving ON, Independent/no prompt, derived edit always rejected before and after warning suppression, restoring both defaults, AUTO MIRROR retain/erase source decision combinations and one multi-member decision. Source guards verify NO routing before identity/rebuild/suppression and use of the existing snapshot, definition and GROUP restoration. Existing Ordinary geometry, COPY, MOVE, GRIP, STRETCH, TRIM, EXTEND and BREAK regressions remain green. WPF runtime tests load the new Settings section and both styled checkbox dialogs. Localization tests cover resource parsing, identical key sets/placeholders and runtime cultures sk/cs/en/de/pl/fr.

These automated/source checks do **not** prove native MIRROR event ordering, exact HOST rollback, real drawing annotations, undo/redo or visual behavior. Those require the manual HOST test.

## Simplified build/startup protocol

External process checks confirmed AutoCAD OFF before every compiling test/gate. There was no save prompt, so no 15-second save window or forced close was needed. Final AutoCAD output is standard Debug x64:

`C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy\src\AcKrovy.AutoCAD\bin\x64\Debug\net10.0-windows\AcKrovy.AutoCAD.dll`

The explicit simplified protocol supersedes the earlier runtime-metadata protocol. The agent did not invoke `AK_RUNTIME_BUILD`, calculate DLL hashes/timestamps/size or compare runtime metadata. AutoCAD was launched with exactly `C:\Users\Roman\Documents\3d.dwg`; startup verification is recorded below. The user's existing automatic startup diagnostics were not changed.

Startup verification: **PASS**. Launched the pre-existing executable with:

```powershell
Start-Process -FilePath 'C:\Program Files\Autodesk\AutoCAD 2027\acad.exe' -ArgumentList '"C:\Users\Roman\Documents\3d.dwg"' -WorkingDirectory 'C:\Program Files\Autodesk\AutoCAD 2027' -WindowStyle Normal -PassThru
```

Computer Use returned the unique `AutoCAD Architecture 2027 - [3d.dwg]` window. After bringing that window to the foreground, the screenshot confirmed the roof drawing was loaded with command input available and no modal error dialog. No command, selection or geometry input was sent, and no DWG save was performed. Existing automatic startup emitted its usual diagnostics; the agent did not invoke or compare runtime metadata. STOP for the user test.

## Manual HOST plan

AutoCAD Architecture 2027, `Documents\3d.dwg`, standard Debug x64 output, branch/HEAD above. First test: one untouched AUTO Ordinary Plan2D rafter; MIRROR; define axis; native erase-source NO; verify KROVY confirmation; choose Áno; STOP and send the fresh log. Expect an unchanged AUTO source in canonical GROUP and one valid Independent mirrored package outside GROUP, with `decision=YES detached=True rollback=False result=pass` and cleared command context.

Then separately test Nie with retain/erase source (including in-place source transformation), Independent MIRROR, multiple AUTO members with one dialog, physical-only rejection with warning ON/OFF, accepted detach opt-out followed by an automatic acceptance, rejected detach opt-out keeping confirmation ON, settings reset and settings reload. Capture source/clone IDs, GROUP and duplicate diagnostics, annotations/designations, physical visuals and command cleanup. Native UNDO/REDO and SAVE/REOPEN remain HOST checks.

HOST lifecycle/visual verdict: **NOT RUN / INCONCLUSIVE**. The agent performs no MIRROR or other geometry edit and does not save the DWG. Existing reflection ambiguity handling is unchanged; the known coincident-midpoint ambiguity remains fail-closed. No broader geometry support is claimed.
