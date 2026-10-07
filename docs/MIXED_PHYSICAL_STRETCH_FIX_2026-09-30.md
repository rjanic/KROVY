# Mixed Plan2D / Physical3D STRETCH — 2026-09-30

## Výsledok a rozsah

Implementovaný fallback cez existujúci `CommandEnded` inspection, manual-edit
transakciu a shared Physical3D generátory. Plan2D je edit authority; zo Solid3d
sa číta iba owner/role/StructuralId, nikdy geometria na vytvorenie override.
HOST retest zostáva neoverený. Bez commit/push/tag. Predchádzajúce WIP zachované.

Branch: `main`. HEAD: `40ddce742d8ba6ed4c3eda7f52fe5cc413840930`.
Lokálny `origin/main`: ahead/behind `0/0`; bez fetch. Working tree bol už dirty,
zmeny sú unstaged a tento zásah nezmenil HEAD ani index.

## Audit selection filtering

Adaptér nemá existujúci roof STRETCH `Editor.SelectionAdded` filter ani
predtransformačný STRETCH veto/overrule. Explicitná ordinary derived recovery
v inspection bola obmedzená na `IsMoveCommand` a `OrdinaryRafterSolid`.
Samotná úspešná Plan2D vetva z používateľovho HOST dôkazu zostáva používaná.

Autodesk dokumentuje `SelectionAddedEventArgs.AddedObjects` a `Selection` ako
práve pridané a aktuálne vybrané objekty:
[SelectionAddedEventArgs properties](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-__MEMBERTYPE_Properties_Autodesk_AutoCAD_EditorInput_SelectionAddedEventArgs.html).

Tieto údaje ešte nepredstavujú konečný používateľský výber. Odstránenie 3D
v prvom crossing kroku by mohlo zmeniť neskorší výber celého krovu, keď používateľ
pridá zdrojovú Polyline, alebo ďalšie add/remove kroky. Bez overeného HOST
predtransformačného filtra s touto garanciou používame explicitne povolený
reconciliation fallback. Natívny selection set sa nemení; nevznikol nový reactor,
selection framework, command replacement, schema ani undo mechanizmus.

## Správanie

| Situácia | Výsledná cesta |
| --- | --- |
| Ordinary Plan2D + matching 3D | Pôvodné prijatie override. Union fyzických kľúčov rebuildne matching telo iba raz. |
| Ordinary Plan2D + unrelated ordinary 3D | Logická zmena iba z Plan2D. Vybrané fyzické kľúče sa obnovia z replay/modelu. |
| Ordinary Plan2D + structural/surface 3D | Po obnove structural references sa odvodené role obnovia shared generátormi v rovnakej owner transakcii. |
| Kolaterálne 3D iného ownera | Po Plan2D accept sa obnovia owner-scoped; úspešná recovery nehlási direct-3D rejection. |
| Iba Physical3D | Recovery z existujúceho Plan2D/logical state/topology/settings a existujúce lokalizované odmietnutie priamej 3D editácie. |
| Source Polyline / whole-roof STRETCH | Source lifecycle má prioritu; nová member-level recovery sa vynechá. |
| GRIP_STRETCH | Pôvodná cesta, bez novej collateral klasifikácie. |
| MOVE/TRIM/ERASE/MIRROR/UNDO/REDO | Nový STRETCH recovery gate ich neprijíma. |

Matching a kolaterálne ordinary keys sa zjednocujú cez HashSet. Existujúci
builder pred zápisom overuje úplnosť a jedinečnosť ordinary physical setu.
Odpoveď "collateral" vyžaduje skutočnú zmenu Generated ordinary Plan2D membera
a úspešný commit; samotné annotation/AttachedManual/structural zmeny nestačia.

Accepted owner transakcia obsahuje persist override, ordinary physical rebuild,
obnovu structural references, obnovu zvyšných kolaterálnych physical rolí a
canonical GROUP/unique-key kontrolu. Existujúca výnimka pre physical failure
abortuje owner transakciu a obnoví natívne Plan2D zo snapshotu.
Zvyšná priama/cross-owner recovery využíva pôvodné group finalize a post-commit
kontroly. Failure je viditeľný; nehlási sa úspešné prijatie Solid3d geometrie.

## Hip metadata-diff — diagnóza, bez zmeny write policy

Podmienka zapisovania je `geometryChanged || metadataChanged`.
`metadataChanged` porovnáva celý `TimberElementData` a `RoofStructuralGeneratedData`
record. Aj pri rovnakých koncových bodoch teda môže byť odlišná typografia,
annotation scale/mode, rozmery, sklon, ElementId, schema alebo iné metadata.
Táto vetva potom nastaví endpoints a zapíše obe metadata sekcie; diagnostika ju
označuje `update-geometry`, aj keď dôvod je len `metadata-diff`.

Poskytnutý HOST výpis neobsahuje before/desired metadata. Nie je možné z neho
preukázať konkrétne rozdielne pole, zbytočnosť zápisu ani kauzálnu súvislosť
s historickým restore-write-failure. Výpis rovnakých endpoints nestačí.
Write policy a snapshot restore sa preto nemenia.

DEBUG `ROOF_STRUCT_METADATA_DIFF` pred `UpgradeOpen` vypíše/persistuje oba
Timber aj Structural records ako JSON s plnou numerickou presnosťou.
Diagnostika je read-only, best-effort a Release ju neobsahuje.
Test overuje DEBUG izoláciu a zachovanú write podmienku. Nebola zavedená
nepreukázaná optimalizácia ani test predstierajúci HOST ForWrite pozorovanie.

## Zmenené súbory tohto zásahu

- `src/AcKrovy.Core/Services/Roofs/RoofPhysicalStretchRules.cs` — nové CAD-neutral routing/rebuild key pravidlá.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofLiveResizeService.cs` — owned physical STRETCH inspection, direct/collateral recovery a GROUP/unique-key kontroly.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedMemberManualEditService.cs` — collateral reconcile v accepted transakcii a explicitné sledovanie commitnutej ordinary zmeny; rozšírenie predchádzajúceho WIP.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryRafterSolidMaterializationService.cs` — union ordinary Plan2D/collateral kľúčov, jediný rebuild každého kľúča.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DLifecycleService.cs` — skladanie existujúcich ordinary/structural/surface generátorov pre recovery.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofAutomaticStructuralRafterMaterializationService.cs` — iba DEBUG metadata diagnosis call.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofAutomaticStructuralRafterTrace.cs` — iba DEBUG before/desired metadata výpis.
- `src/AcKrovy.Core.Tests/RoofMixedPhysicalStretchTests.cs` — 17 nových semantic/model/source-contract testov.
- `src/AcKrovy.Core.Tests/RoofOrdinaryRafterSolidSourceContractTests.cs` — assertion pre shared rebuild-key planner; rozšírenie predchádzajúceho WIP.
- `src/AcKrovy.Core.Tests/RoofGeneratedChildEraseProtectionTests.cs` — opravené koncové hranice ERASE source-contract úsekov po pridaní susednej metódy.
- `docs/MIXED_PHYSICAL_STRETCH_FIX_2026-09-30.md` — tento report.

## Validácia

AutoCAD process bol pred každým adapter buildom, WPF run a Full Gate skontrolovaný
cez `Get-Process -Name 'acad*'`; nebol aktívny. .NET SDK 10.0.401,
AutoCAD 2027 reference assemblies. HOST AutoCAD sa na testy nespúšťal.

| Kontrola | Výsledok |
| --- | --- |
| Focused STRETCH / derived MOVE / ERASE / ordinary physical contracts | PASS — 62/62 |
| Core | PASS — 6845/6845 |
| WPF | PASS — 806/806 |
| Debug x64 adapter | PASS — 0 warnings, 0 errors |
| Release x64 adapter | PASS — 0 warnings, 0 errors |
| Portable Gate | PASS — restore/build/tests/architecture/version |
| Full Gate | PASS — solution restore/build a Core 6845/6845 + WPF 806/806 |
| git diff --check | PASS — exit 0; iba Git LF→CRLF upozornenia |
| CAD-neutral dependencies | PASS — Portable Gate |
| Lokalizácia | Bez zmeny resources; existujúce správy majú všeobecný 3D derived význam vo všetkých 6 jazykoch. |
| HOST | NOT RUN — HOST PASS sa netvrdí |

Spustené príkazy (build/test s `--no-restore -warnaserror -m:1 -nr:false`):

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --filter 'FullyQualifiedName~RoofMixedPhysicalStretch|FullyQualifiedName~RoofOrdinaryPhysicalStretch|FullyQualifiedName~RoofDerivedPhysicalMove|FullyQualifiedName~RoofGeneratedChildEraseProtection|FullyQualifiedName~RoofOrdinaryRafterSolidSourceContract' --logger 'console;verbosity=minimal'
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --logger 'console;verbosity=minimal'
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj -p:Platform=x64 --logger 'console;verbosity=minimal'
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj -c Debug -p:Platform=x64
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj -c Release -p:Platform=x64
./scripts/compatibility-gate.ps1 -Portable
./scripts/compatibility-gate.ps1 -Full
git diff --check
git status --short
git diff --stat
git diff --cached --name-only
```

Gate prostredie: `DOTNET_PROCESSOR_COUNT=2`, `MSBUILDDISABLENODEREUSE=1`.
Logy: `%TEMP%/acad-mixed-stretch-portable-20260930.log` a
`%TEMP%/acad-mixed-stretch-full-20260930.log`.
Prvé focused/full Core pokusy našli iba zastarané textové assertions/hranicu
metódy a newline assertion; po opravách boli príslušné sady opakované.

## HOST retest — krátky hlavný scenár

Prostredie: AutoCAD 2027, nový Debug/Release x64 build; zaznamenať načítanú DLL,
DWG, HEAD uvedený vyššie a DBMOD pred/po. Obyčajné krokvy Unlocked,
Physical3D enabled, režim Both, bežný pôdorys. Nedávať iba axis-only výber.

1. Natívny `STRETCH`: bežným crossing window sprava doľava zachytiť koniec jednej
   2D obyčajnej krokvy vrátane 3D objektov, ktoré okno prirodzene zachytí.
   Posunúť koniec napr. o 125 mm; Enter dokončiť príkaz.
2. Overiť prijatý Plan2D override, rovnaký MemberKey, jedno matching 3D telo,
   žiadne neúmyselné logické editácie okolitých členov, canonical GROUP a unique keys.
   Platný 2D edit nesmie vyvolať direct-3D rejection. V DEBUG zaznamenať
   `ROOF_PHYSICAL_STRETCH` a prípadné `ROOF_STRUCT_METADATA_DIFF` before/desired JSON.
3. Spustiť `AUDIT`, potvrdiť opravu chýb a zaznamenať výsledok.

Doplňujúce HOST regresie: Physical3D-only STRETCH; rovnaký crossing s viacerými
unrelated 3D; source roof STRETCH; GRIP_STRETCH; 2D MOVE/TRIM/ERASE;
direct Physical3D MOVE/ERASE; whole-roof MIRROR; Hip/Valley; U/REDO a SAVE/REOPEN.

## Verdikt a zostávajúce riziká

Automated validation: PASS. Focused/Core/WPF, Debug/Release x64,
Portable/Full Gate a diff check prešli. Build warnings/errors: 0/0.
HOST: INCONCLUSIVE / NOT RUN. Unit/model/source-contract testy dokazujú Core
pravidlá a väzby adaptéra, nie skutočný native crossing/event/control-flow priebeh.
Do dokončenia HOST retestu zostávajú neoverené native selection/callback ordering,
transient physical tamper, AUDIT a U/REDO správanie. Hip redundant-write hypotéza
zostáva otvorená do získania konkrétnych before/desired metadata.
