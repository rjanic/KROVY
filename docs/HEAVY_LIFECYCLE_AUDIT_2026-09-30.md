# Heavy Lifecycle Package — audit a príprava HOST dôkazu

Dátum: 2026-09-30. Baseline: `40ddce742d8ba6ed4c3eda7f52fe5cc413840930`, branch `main`.

## 1. STATUS

**PARTIAL / BLOCKED pre clone/split/replacement identity architektúru.** Po pokyne
„pokračuj“ sa dokončilo úzke code-side rozšírenie existujúcej accepted-edit cesty:
Generated ordinary STRETCH a GRIP_STRETCH používajú rovnaký targeted Physical3D
reconcile a GROUP failure rollback ako MOVE/TRIM. Implementované sú aj DEBUG
diagnostické rozšírenia. Celý Heavy Lifecycle Package nie je dokončený ani
označený PASS. HOST nebol spustený.

Pravidlo AGENTS.md vyžaduje reálny HOST event/control-flow dôkaz pred návrhom
persistence, ownership, recovery alebo Undo/Redo. Workflow
`.agents/skills/roof-timber-lifecycle/SKILL.md` navyše stanovuje:
„When the real sequence cannot be proven statically, add narrow temporary DEBUG
diagnostics and request one HOST run before changing architecture.“

Dodané zadanie obsahuje skrátený historický GRIP_STRETCH accept/fallback, bez
konkrétneho handle, detailu zlyhania a úplnej command sequence. Vyhľadanie v
repozitári, ErrorReports a dostupných lokálnych ACAD_KROVY logoch tento výpis
nenašlo. Existujúce uložené whole-roof COPY/MIRROR audity a LiveGeometryTiming
nie sú dôkazom member-level sekvencie všetkých požadovaných variantov.

## 2. Audit pôvodnej architektúry

| Oblasť | Existujúca implementácia a význam |
| --- | --- |
| Native hooks | `LiveGeometrySynchronizationService`: CommandWillStart, CommandEnded, CommandCancelled, CommandFailed; ObjectModified, ObjectAppended, ObjectErased; BeginDeepCloneTranslation. |
| Event discipline | Deduplikované pending sets; callbacky zbierajú kandidátov, completion spúšťa maintenance. Scoped `Suppress()` chráni interné zápisy a erase pred reentrancy. |
| Snapshot | `RoofUnsupportedStretchRecoverySnapshotService`: pred-command source, timber Lines, AttachedManual a anotácie. Capture gate je `IsAssemblySnapshotCommand`, ktorý teraz nezahŕňa COPY/MIRROR. |
| Owner resolution | `RoofLiveResizeService.Inspect` rozlišuje source/display/ordinary/structural/attached/derived physical a vracia spracované IDs na odfiltrovanie z generic refresh. |
| Ordinary edit | `RoofGeneratedMemberManualEditService.ProcessOwners` → owner transaction → TryAcceptUnlockedEdits → relatívne overrides → targeted recalc → synchronizácia ReservedElementId → GROUP. |
| Semantic SSOT | `RoofGeneratedMemberKey` (kind, face, station), recipe/layout, `RoofGeneratedMemberOverride` a replay planner. Typ `LogicalMemberModel` sa v repozitári nenachádza. |
| ManualOverride | Relatívny Along/Lateral/Rotation/StartOffset/EndOffset; kľúč zostáva pôvodný Generated key. ReservedElementId je rezerva item/numbering identity; nemá sa zamieňať s unique physical/member identity. |
| Suppression | `Suppressed=true` na Generated override. Recept zostáva; reset edits môže slot obnoviť. Nie je to permanentné zmazanie AttachedManual. |
| AttachedManual | Persistovaný owner, ChildIdentity, exact Generated anchor, RelativeSegment, Origin.Copy alebo Origin.Split. Dormancy = Visible=false. Zdrojový resize replay vyžaduje exact anchor a footprint containment. |
| Physical model | `RoofOrdinaryRafterSolidMaterializationService` → replay → `RoofAutomaticRafterPhysicalBuilder`, topology/elevation/settings/structural sources. Solid nie je vstup SSOT. |
| Physical keys | Ordinary StructuralId je kind:face:station; Physical3D XData má owner/role/StructuralId/signature. AttachedManual zatiaľ nie je súčasťou tohto modelu. |
| GROUP | `RoofAssemblyGroupMemberCollector` zhromažďuje source, display, Generated, StructuralGenerated, purlins, AttachedManual, timber solids a source-bound anotácie. Collector organizuje entity; nepreukazuje bijekciu logical member ↔ solid. |
| Whole-roof clone | `RoofNativeCloneSnapshot` zachytáva native mapping a `GetCompleteClones` rozpoznáva celé assembly; `RoofWholeRoofCopyRebindService` beží pred per-member maintenance. |
| COPY | Generic copy init → AttachedManual copy reinitialization → Generated copy ownership rehydration/detach/promotion. Viac samostatných transaction boundaries. |
| MIRROR | `RoofMirrorCloneDetachService`: role-sensitive append a in-place vetvy; whole-roof vetva sa spracuje skôr. |
| Undo/Redo | Grouped undo mark pre native edit; U/UNDO/REDO/MREDO clearing bez DB refresh. Nie je to runtime dôkaz celého požadovaného Undo výsledku. |
| Cancel/fail | Pending state a snapshot scopes sa vyčistia; plugin completion maintenance sa nevykoná. Skutočný native rollback ostáva HOST otázka. |
| Persistence | Owner definition overrides a AttachedManual codec/XData sú existujúce formáty. Codec roundtrip testy nepreukazujú uloženie/reopen celej DWG ani GROUP. |

## 3. Potvrdené medzery a nepreukázaná príčina

- **V baseline** `RoofGeneratedMemberManualEditService.TryAcceptUnlockedEdits` volá targeted
  Physical3D reconcile iba pri MOVE/TRIM. BREAK, STRETCH a GRIP_STRETCH túto vetvu
  nepoužijú. STRETCH/GRIP_STRETCH medzera je teraz opravená spoločným command policy
  extension pointom; BREAK zatiaľ nie.
- Physical model aj `MatchesGeneratedMemberKeys` zahŕňajú Generated Lines, nie
  AttachedManual Copy/Split; samotné 2D promotion nestačí na canonical solid.
- AttachedManual ChildIdentity je v dnešných creation/reinitialization cestách
  odvodené z handle. Požadovaný nezávislý domain member identity sa tým nedá
  deklarovať ako hotový. Zmena musí rešpektovať staré payloady a clone correlation.
- MIRROR Yes Generated dnes clearne Generated metadata, suppressne starý slot a
  prevedie výsledok na AttachedManual Copy. Nie je to zachovanie Generated key ako
  unified permanent identity podľa nového zadania.
- Atomic rollback COPY/MIRROR nemožno len zapnúť použitím edit snapshotu, keďže
  snapshot command gate tieto príkazy teraz vylučuje.
- GROUP sync výsledky sa na viacerých clone vetvách ignorujú; organization set
  nezaručuje jedinečný physical key ani absenciu orphan solids.
- Po accepted ordinary edit sa bez rozlíšenia native zmeny volá structural
  Hip/Valley restore. Návratový bool je ignorovaný. To je potvrdená riziková cesta,
  **nie preukázaná príčina konkrétneho GRIP fallbacku**.
- Restore failure môže znamenať open/type failure alebo ElementId mismatch.
  ElementId je numbering identita; fallback sám nepreukazuje native Hip edit.

## 4. Použitý spoločný extension point a ďalší kandidát

`RoofGeneratedMemberEditCommandRules.RequiresOrdinaryPhysicalReconcile` združuje
MOVE/TRIM/STRETCH/GRIP_STRETCH. Accepted-edit transaction volá existujúci
`TryReconcileModifiedMembersInTransaction`, ktorý deduplikuje dirty Generated keys
a materializuje iba ich solids. Rovnaká policy chráni výsledok GROUP sync; zlyhanie
vyhodí existujúcu OrdinaryPhysicalReconcileException a použije pôvodnú recovery
cestu. Žiadny nový renderer, geometry formula, persistence formát alebo Undo handler.

Core test s dvoma endpoint overrides preukazuje rovnaké keys, Z=0 Plan2D, nové
physical geometry, nezmenené ostatné bodies a top vertices na roof plane.

Existujúci command completion koordinátor je vhodné miesto: ponechať native
capture, whole-roof classification, role-sensitive 2D semantic acceptance a
rozšíriť existujúci ordinary physical materializer na výsledné semantic členy.
Physical reconciliation a canonical GROUP verification musia patriť tej istej
transaction ako semantic zápisy. ObjectModified nesmie permanentne materializovať.

Toto je výsledok auditu, **nie dokončený návrh ownership/rollback architektúry**.
Konkrétne clone/replacement pairing, persistent identity a failure rollback sa
musia určiť z preukázanej native sekvencie. Nový paralelný lifecycle framework
ani fiktívne Generated station keys neboli zavedené.

## 5–9. Semantics jednotlivých operácií

| Command | Dnešný 2D lifecycle | Stav požadovaného Physical3D rozšírenia |
| --- | --- | --- |
| BREAK | Generated survivor + AttachedManual Split; repeated Split ostáva role-sensitive. | Nie je hotový druhý solid ani unified unique identity/boundary-role riešenie. Nutný bodový aj dvojbodový HOST variant. |
| COPY | Source preserve; clone reinit/promotion ako AttachedManual Copy. | Nie je hotový vlastný derived solid, multi-copy atomicity ani odstránenie native solid klonov pre partial member selection. |
| MIRROR No | Source preserve; Generated/AttachedManual clone sa prevedie na samostatný Copy child. | Unified clone identity a physical materialization nie sú implementované. |
| MIRROR Yes | Skill zaznamenáva HOST in-place variant, vrátane AttachedManual. Generated sa suppressne a konvertuje na Copy. | Nové logical identity retention a clone+erase variant nie sú preukázané ani implementované. |
| STRETCH | Unlocked representable ordinary override; Locked shape tamper recovery; geometric whole-roof rigid translation má vlastnú cestu. | Generated ordinary targeted reconcile teraz používa existujúcu owner transaction; dirty keys sa deduplikujú a GROUP sync failure spustí recovery. AttachedManual physical output ešte chýba. |
| GRIP_STRETCH | Zdieľa ordinary override/recalc, group-grip/source precedence a rigid transform cesty. | Rovnaký Generated ordinary physical reconcile ako STRETCH; konkrétny restore failure root cause je stále nepreukázaný a recovery heuristika sa nemenila. |

## 10. Undo/Redo + Cancel

Existing grouped undo a zero-DB Undo boundaries zostali bez zmien. Žiadna nová
Undo/Redo/Cancel záruka pre package sa netvrdí. Native partial results pri cancel
multi-copy treba pozorovať, nie odvodzovať z callback názvu.

## 11. GROUP / identity / orphan guarantees

Nové clone/split/replace guarantees neboli implementované. Existujúce MOVE/TRIM/direct physical
ERASE/MOVE recovery, whole-roof MIRROR, ridge Overlap, Hip/Valley a Faces/Edges
produkčné vetvy zostávajú nezmenené. Shared STRETCH/GRIP využíva ich existing
transaction/recovery mechanizmus. Žiadny schema/version bump ani per-member Z.

## 12. Presný zoznam zmenených súborov

1. `src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs`
   — DEBUG PLAN_MEMBER/native member evidence, member clone mapping, after-maintenance
   audit požadovaných commands a read-only failure probe.
2. `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedMemberManualEditDiag.cs`
   — existujúce DEBUG accept/reject trace sa zapisujú aj do file loggera.
3. `src/AcKrovy.AutoCAD/Infrastructure/RoofUnsupportedStretchRecoveryDiag.cs`
   — existujúci DEBUG fallback sa zapisuje aj do file loggera.
4. `src/AcKrovy.AutoCAD/Infrastructure/RoofUnsupportedStretchRecoveryService.cs`
   — iba dva DEBUG failure probe callsites; recovery rozhodnutia a zápisy bez zmeny.
5. `src/AcKrovy.Core.Tests/RoofPhysical3DHostDiagnosticsSourceContractTests.cs`
   — diagnostická izolácia a provenance/persistence source-contract assertions.
6. `src/AcKrovy.Core.Tests/RoofPhysical3DLockedSourceEraseSourceContractTests.cs`
   — DEBUG post-maintenance whitelist test, vrátane vylúčenia Undo/Redo.
7. `src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberEditCommandRules.cs`
   — spoločná physical reconcile policy.
8. `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedMemberManualEditService.cs`
   — existujúci physical reconcile a GROUP failure guard používajú spoločnú policy.
9. `src/AcKrovy.Core.Tests/RoofOrdinaryRafterSolidSourceContractTests.cs`
   — shared physical reconcile integration guard.
10. `src/AcKrovy.Core.Tests/RoofOrdinaryPhysicalStretchTests.cs`
    — command policy, multi-member replay, identity/Z=0/roof-plane/unchanged-body tests.
11. `docs/HEAVY_LIFECYCLE_AUDIT_2026-09-30.md` — tento audit a handoff.

Nesúvisiace pôvodné untracked WIP sa zachovávajú. Žiadny commit/push/tag/release.

Git Diff Summary: `main`, HEAD `40ddce7`, upstream `origin/main`, local ahead/behind
`0/0` (bez fetch). Všetky zmeny sú unstaged: 9 tracked modifications + 2 nové
súbory tohto tasku. Core: 1; AutoCAD host: 5; testy: 4; dokumentácia: 1.
UI/WPF, localization, scripts/CI a generated assets sa nemenili.

## 13–14. Validácia

Finálne výsledky:

| Kontrola | Výsledok |
| --- | --- |
| Pôvodný focused regression filter | PASS, 104/104 (diagnostická etapa) |
| Finálny focused STRETCH/diagnostic/integration filter | PASS, 19/19 |
| Všetky Core testy | PASS, 6 828/6 828, 0 failed, 0 skipped |
| Všetky WPF testy | PASS, 806/806, 0 failed, 0 skipped |
| AutoCAD Debug x64 | PASS, 0 warnings / 0 errors |
| AutoCAD Release x64 | PASS, 0 warnings / 0 errors |
| Portable Compatibility Gate | PASS; restores/builds/tests/architecture/version checks |
| Full Compatibility Gate | PASS; solution restore/build/tests, 0 build warnings/errors |
| git diff --check | PASS, exit 0 |
| HOST | NOT RUN; žiadny HOST PASS |
| Celý Heavy Lifecycle Package | INCOMPLETE; nie PASS |

Prvý Portable run zlyhal na source-text assertion starého DEBUG audit whitelistu;
po aktualizácii zachovávajúcej Undo/Redo exclusion prešli finálne gates. Prvé dva
sandbox dotnet test pokusy neposkytli výsledok; dôveryhodné počty vyššie sú z
úspešných následných behov mimo sandboxu s obmedzeným MSBuild paralelizmom.
Git vypisuje upozornenie na LF→CRLF pri niektorých upravených zdrojoch; nejde o
build warning ani whitespace failure. Pôvodný WIP nebol upravený, HEAD je stále baseline.

Spustené validačné príkazy:

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -warnaserror -m:1 -nr:false --filter 'FullyQualifiedName~RoofOrdinaryPhysicalStretchTests|FullyQualifiedName~RoofOrdinaryRafterSolidSourceContractTests|FullyQualifiedName~RoofPhysical3DHostDiagnosticsSourceContractTests' --logger 'console;verbosity=minimal'
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj -c Debug -p:Platform=x64 --no-restore -warnaserror -m:1 -nr:false
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj -c Release -p:Platform=x64 --no-restore -warnaserror -m:1 -nr:false
./scripts/compatibility-gate.ps1 -Portable
./scripts/compatibility-gate.ps1 -Full
git diff --check
git diff --stat
git status --short
git rev-parse HEAD
```

Pred adapter buildmi/Full gate sa overilo, že nebeží `acad*`. Gates bežali s
process-local `DOTNET_PROCESSOR_COUNT=2` a `MSBUILDDISABLENODEREUSE=1`; skripty
repozitára sa nemenili. Kompletné výstupy gates sú v `%TEMP%`:
`acad-heavy-lifecycle-portable-20260930.log`, `acad-heavy-lifecycle-full-20260930.log`.

Testy tejto zmeny overujú diagnostiku, shared Generated ordinary STRETCH/GRIP
physical reconcile a existujúce regresie;
**nie celý Heavy Lifecycle Package**. Package clone/split/replacement Undo/Redo a
save-reopen testy nevznikli, pretože príslušná architektúra nie je implementovaná.

## 15–16. Zostávajúci HOST dôkaz a krátky plán

Najprv treba command/control-flow dôkaz, až potom finálny acceptance retest balíka.
Na uloženej čistej testovacej streche s existujúcimi krokvami, v DEBUG builde:

1. Zapnúť `AK_ROOF_3D_TRACE`; vykonať `AK_ROOF_3D_AUDIT` pred každým variantom.
2. Unlocked ordinary Line: BREAK v bode a medzi dvoma bodmi; COPY dve umiestnenia;
   member MIRROR No a Yes; endpoint STRETCH a endpoint GRIP_STRETCH.
3. Po každom variante AUDIT, U, AUDIT, REDO, AUDIT; samostatne ESC cancel.
4. Zachytiť najmä celý GRIP interval s accept/fallback, jeho `detail`, handle a
   `ROOF_TIMBER_RESTORE_FAILURE_PROBE`. Potrebné sú snapshot/live ElementId,
   geometryMatchesSnapshot a NATIVE_MEMBER structural role toho istého handle.
5. Odovzdať file log + command line výpis. Oddeliť native begin/events/end,
   pred-maintenance stav a after-maintenance stav. Counts 32/186 nie sú acceptance SSOT.

Po implementácii sa rozšíri finálny HOST acceptance o Locked policy, direct derived
3D recovery, whole-roof MIRROR a save/reopen COPY/BREAK/MIRROR Yes. Práve pripravený
DEBUG build obsahuje aj shared Generated ordinary STRETCH/GRIP reconcile; ostatné
clone/split/replacement cesty sú pôvodné. Nie je to hotový lifecycle package.
