# Physical3D-only ERASE warning/GROUP completion — 2026-10-06

Physical-only Ordinary ERASE now commits and verifies the existing package restoration before its first GROUP synchronization. Completion runs in a fresh transaction at the terminal handler, within the same ERASE/undo scope. Warning ON/OFF follows the same model path. A post-warning final check precedes the lifecycle PASS diagnostic. No commit, push or tag was performed.

## HOST evidence and diagnosis

The user's A/B reproduction identifies OFF as PASS and ON as FAIL. The existing log `%LOCALAPPDATA%\ACAD_KROVY\Logs\ACAD_KROVY-20261006.log` confirms the membership race for the same Solid `2B44`, owner `2912`, Plan line `2AC4`:

- 07:52:38: native ERASE has one ObjectErased event; restored physical inventory is complete but GROUP is 160/161. There is one explicit KROVY append of `2B44`; pre-commit GROUP becomes 161/161, while final maintenance reports 162 entries / 161 unique members.
- 07:53:47–52: GROUP already contains 161 members before EnsureGroup; no missing-slot append is necessary; final membership stays canonical 161/161.

These raw log records do not encode the warning preference value; the ON/OFF labels come from the user's HOST evidence. The warning callback contains UI/preference handling only, with no GROUP append or package restore. The former race is between GROUP repair inside the unerase transaction and native slot reattachment when that transaction completes. Existing `RoofLiveResizeService` locked-source repair and `RoofAssemblyGroupSyncService.TryFinalizeRestoredSources` already document/protect the same post-commit native reattachment class of issue, with GROUP writes kept inside the current ERASE undo scope rather than Idle/UNDO.

## Changes

- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryEraseLifecycleService.cs`: only a command whose affected Ordinary members have live Plan lines takes the pure Physical3D completion route. Existing source geometry, XData, identity, BRep and annotation snapshot verification remains immediate; GROUP verification/sync waits for the restore transaction to close. Pending completion contains copied ObjectIds/messages, is consumed once and cleared/disposed with the command context. Plan2D AUTO deletion, Independent Plan2D deletion and mixed Line/Solid precedence retain their existing route.
- `RoofOrdinaryGripLifecycleService.cs`: optional `deferGroupVerification=false` parameter; only pure physical ERASE opts in. All other callers retain their existing verification behavior.
- `LiveGeometrySynchronizationService.cs`: consumes physical completion after ordinary maintenance and before `EndStretchUndoMark`, snapshot disposal and final GROUP audit. No Idle handler or Undo/Redo database work was added.
- `RoofOrdinaryPhysicalEraseCompletionService.cs` (new): rereads canonical membership after restore commit; when it is already current, performs no append or repair. Missing membership uses the existing authoritative `TrySyncForOwner` / `EnsureGroup` mechanism. Warning remains the existing shared UX service. The final check after warning is unconditional; a late duplicate may be normalized only if the unique membership equals the expected snapshot and every duplicated ID belongs to the restored Solid set. Foreign/missing final membership or duplicates of unrelated members fail rather than trigger broad cleanup. Lifecycle PASS is emitted after completion with `phase=post-warning`, `restoreTransactionClosed=True`, and `groupSyncCount`.
- `src/AcKrovy.Core/Services/Roofs/RoofPhysicalEraseCompletion.cs` (new): CAD-neutral once-only ordering guard; completion requires a closed restore transaction, runs model completion before UX, and executes final verification even if UX throws. No warning preference is an input to the model path.
- `src/AcKrovy.Core.Tests/RoofPhysicalEraseWarningGroupTests.cs` (new): ON/OFF equivalence including a simulated late native slot, no unnecessary plugin append, exact 161/161 membership, once-only completion, no completion before restore commit, UX-error finalization, and adapter/undo-scope/source guards.
- This report.

No warning preference, dialog style, opt-out/persistence semantics, physical builder, designation, recipe or DWG metadata schema was changed. No parallel GROUP canonicalizer was introduced.

## Verification

Repository `C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy`, branch `main`, HEAD `cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`; existing dirty WIP preserved. Elevated process checks confirmed AutoCAD OFF before every build/gate. No save/force-close window was necessary because acad.exe was not running.

Commands:

```powershell
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -p:Platform=x64 -warnaserror
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror --filter 'FullyQualifiedName~RoofPhysicalEraseWarningGroupTests|FullyQualifiedName~RoofOrdinaryEraseRebuildTests|FullyQualifiedName~WarningPreferenceTests|FullyQualifiedName~RoofOrdinaryGripLifecycle'
.\scripts\compatibility-gate.ps1 -Full
git diff --check
```

Automated verdict **PASS**: targeted **53/53**; Full Compatibility Gate including Portable, Core **7577/7577**, WPF **831/831**, no failures/skips, zero warnings/errors. Dependency checks and restore passed. Gate log `.ai/handoffs/physical-erase-warning-group-2026-10-06/full-gate.log`. Standard Debug x64 output `src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll` only; no alternate build directory or manual runtime/hash/timestamp probe.

The automated A/B regression simulates late reattachment and exercises production completion ordering plus the established Core multiset rules. It does not prove native AutoCAD Group reactor timing or modal behavior. New HOST A/B result remains **UNCONFIRMED**.

## Required manual HOST retest

AutoCAD Architecture 2027, `C:\Users\Roman\Documents\3d.dwg`, current standard Debug build. Start from canonical membership and use the same unchanged AUTO Ordinary Solid for both runs.

1. Warning OFF: erase only the Physical3D Solid; finish; capture the fresh final audit.
2. Warning ON: erase the same restored Solid; acknowledge the approved warning without opting out; finish; capture the fresh final audit.
3. Both final audits must have expected=actual, duplicates=missing=foreign=0, canonical=True. Check the post-warning lifecycle diagnostic retains physicalOnlyRejected=True, suppressionWritten=False, independentCreated=False, attachedManualWritten=False, legacyGeometryOverrideWritten=False and annotationsRemoved=0.
4. Confirm one warning only when ON, none when OFF; identical restored geometry, pairing, identity and annotations. If a second GROUP sync occurs, its operations must remove only late duplicate restored-solid slots; no repeated append of the Solid.
5. Check native UNDO/REDO and a following normal AUTO/Independent Plan2D ERASE retain their established behavior. Do not describe these HOST checks as PASS until actually performed.
