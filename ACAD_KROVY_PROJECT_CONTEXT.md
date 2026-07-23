# ACAD KROVY – PROJECT CONTEXT

**Aktualizované:** 23. 7. 2026

**Stabilný základ pred v0.15.0:** `2ad55a49086ee8f656effb3e636e82937ed02bd4`

**Branch:** `main`

**Verzia aplikácie:** autoritatívne v `Directory.Build.props`

**Stav baseline:** pracovný strom čistý, `HEAD == origin/main`

**Overovanie:** Debug/Release build, kompletné automatické testy a Portable/Full Compatibility Gate

## Vízia
ACAD KROVY je CAD systém pre návrh, označovanie, výpočty, výkazy a postupne aj automatické kreslenie drevených konštrukcií krovu.

Dlhodobý cieľ:
- spoločné doménové a výpočtové jadro nezávislé od konkrétneho CAD API,
- AutoCAD ako prvý plnohodnotný adaptér,
- kompatibilita AutoCAD 2021–2027,
- BricsCAD ako prvý alternatívny CAD adaptér,
- neskôr ZWCAD.

> Všetko, čo sa dá vypočítať alebo rozhodnúť bez CAD API, patrí do Core. Konkrétny CAD má byť iba vrstva, ktorá vyberá, číta, zapisuje a kreslí.

## Architektúra

### Core / Domain
CAD-neutrálna logika:
- modely prvkov,
- výpočty dĺžok a objemov,
- výrobné prídavky a zaokrúhľovanie,
- numbering/signatures,
- patching,
- geometrické validačné pravidlá,
- rectangle foundation pre Post footprint,
- plánovanie refreshu a testovateľné pravidlá.

Core nesmie obsahovať `Autodesk.AutoCAD.*`.

### CAD abstractions
Rozhrania medzi doménou a konkrétnym CAD prostredím.

### AutoCAD adapter
Obsahuje:
- `Document`, `Database`, `Transaction`, `Editor`,
- `Entity`, `Line`, `Polyline`,
- XData,
- selection workflow,
- Ribbon,
- Classic Toolbar,
- WPF UI,
- kreslenie labelov, anotácií a reportov.

### Localization
Samostatná vrstva bez závislosti na AutoCAD API.

Aktívne jazyky:
- SK
- CS
- EN
- DE
- PL
- FR

Runtime používateľské texty majú používať explicitnú aplikačnú kultúru z `AppLanguageService`, nie implicitnú `CurrentUICulture` AutoCAD command threadu.

## Stabilné technické pravidlá

### Identita a metadata
- Primárne metadata: XData.
- Legacy Xrecord: iba spätné čítanie.
- `SourceHandle`: identita konkrétnej CAD entity/anotácie.
- `ElementId`: identita výrobnej položky.
- Metadata schema je verzovaná.
- Post rectangular footprint zostáva spätne čitateľný zo schema v2.
- Custom element používa schema v3 a self-contained `CustomElementTypeId`, názov a prefix.
- Existujúce DWG sa nesmú deštruktívne migrovať bez potreby.

### Item signature
Výrobná identita položky:
- ElementType,
- Material,
- WidthMm,
- HeightMm,
- CuttingLengthMm.

Pre `ElementType.Custom` je súčasťou signatúry aj stabilný `CustomElementTypeId`;
používateľský názov ani prefix signatúru nemenia.

### Výrobná dĺžka
- `Raw = Actual + Max(0, Allowance)`
- finálna CuttingLength sa zaokrúhľuje nahor podľa konfigurovateľného kroku,
- default krok 100 mm.

### Labely
- pri live refreshi sa label vracia na vypočítanú pozíciu,
- ručný posun automatického labelu sa nezachováva,
- preferovaný rozmerový formát je `80x160`,
- budúca voľba aj `80/160`.

### Jazyk a technické dáta
Nikdy nelokalizovať/prepisovať pri zmene jazyka:
- ElementId,
- SourceHandle,
- XData schema,
- enum/internal identifiers,
- `AK_...` command names,
- item signatures,
- Material/Note/RoofPlaneId ako používateľské dáta,
- existujúce názvy vrstiev.

## Dokončené veľké míľniky

### Architecture & Compatibility Foundation
- Core `netstandard2.0`
- CAD abstractions
- metadata abstraction
- schema versioning
- layer abstraction
- Compatibility Gate

### Manufacturing Length & Allowances
- default prídavky podľa typu
- zaokrúhľovanie výrobných dĺžok
- per-element override

### Live Geometry Synchronization
- refresh po geometrických úpravách
- COPY cleanup
- stabilné ElementId pravidlá

### Cutting Rules
- centralizovaný calculator
- sorting reportov
- Inspect vylepšenia

### Slope Direction Annotation
- šípka smeru spádu
- angle text
- collision-aware placement
- flip direction

### Multilingual system
- localization foundation
- 6 jazykových balíkov
- runtime prepínanie jazyka
- Ribbon a Classic Toolbar localization
- explicitná aplikačná kultúra pre runtime texty

### Post Rectangular Footprint
Stabilný commit: `a97b3d9c98e3cb2f3923d44bca86f6f28f8d9253`

- Post ako rectangular footprint
- one-click výber strany
- closed/geometrically closed Polyline
- Width/Height z footprint geometrie
- ManualLength default 2500 mm
- dedicated 3-line label
- `⊥ 90°` anotácia
- geometry-aware reading
- perimeter sa nepoužíva ako PlanLength
- COPY/live refresh/cleanup
- schema v2

### Post footprint zo 4 samostatných LINE
Stabilný commit: `4a951041e2deef40a127ac9560cf6fb2ba4b6a5b`

- klik na jednu LINE
- automatické nájdenie 4-line rectangle
- validácia
- konverzia na jednu closed lightweight Polyline
- jeden SourceHandle
- rollback pri chybe
- následne celý existujúci Post footprint lifecycle

### Documentation & Centralized Version
- spoločná assembly/package verzia vychádza z `Directory.Build.props`,
- startup a `AK_HELP` používajú runtime version provider,
- `.bundle` manifest je kontrolovaný Compatibility Gate,
- README, project context, roadmap a backlog odrážajú Post workflow aj runtime lokalizáciu,
- počet testov sa v dokumentácii nefixuje; zdrojom pravdy je aktuálny test run.

### Explicit Item Renumbering
- `AK_RENUMBER` pracuje so všetkými platnými timber prvkami aktuálneho DWG,
- unikátne `TimberElementSignature` radí v každom type podľa `CuttingLengthMm`, prierezu a materiálu,
- prideľuje súvislé používateľské označenia od 1 a rovnaké signatúry zdieľajú číslo,
- `ElementId` je v existujúcej architektúre používateľské položkové označenie, nie nemenná entitná identita,
- stabilná identita CAD entity a väzieb anotácií zostáva `SourceHandle`,
- bežná automatická logika naďalej zachováva existujúce čísla a medzery,
- labely sa obnovia v rovnakej transakcii; reporty čítajú nové čísla z metadata.

### Localized Material Catalog & Adaptive Reports
- katalóg obsahuje šesť stabilných canonical material hodnôt; lokalizované názvy sú iba prezentačná vrstva,
- `AK_EDIT` zapisuje vybranú canonical hodnotu a bezpečný batch patch vzniká iba pri aktívnej zmene,
- neznámy legacy materiál sa pridá ako zachovateľná raw voľba a automaticky sa nemigruje,
- `TimberElementSignature`, metadata, COPY/COPYCLIP, live sync a `AK_RENUMBER` používajú raw/canonical `Material`,
- `AK_INSPECT`, `AK_REPORT` a `AK_REPORTALL` zobrazujú lokalizovaný názov podľa explicitnej aplikačnej UI kultúry,
- reportový materiál sa delí na hlavný názov a popis bez zobrazenej oddeľovacej pomlčky,
- Typ a Materiál majú dynamickú šírku podľa skutočných riadkov reportu, maximálne dva textové riadky a ochranu pred delením slov.

### Custom Element / Vlastný definovaný prvok
- jeden stabilný enum `Custom`, nie dynamické runtime enum hodnoty,
- schema v3 ukladá na každom prvku stabilné ID definície, používateľský názov a prefix,
- `AK_CUSTOM` vyberá alebo vytvára opakovane použiteľnú lineárnu definíciu,
- voliteľný používateľský katalóg je iba pomôcka; DWG ostáva samostatne prenosný,
- každé Custom ID má samostatnú signatúru a numbering sériu,
- explicitné premenovanie v `AK_EDIT` aktualizuje všetky prvky rovnakého Custom ID
  v aktuálnom DWG a lokálny katalóg bez zmeny ID, prefixu alebo položiek,
- `AK_INSPECT`, `AK_EDIT`, `AK_REPORT` a `AK_REPORTALL` zobrazujú persistentný
  používateľský názov; automatický label obsahuje iba položku, prierez a výrobnú dĺžku,
- automatický režim dĺžky je slope-aware cez rovnaký Core pipeline ako Krokva,
- spoločná vrstva `KROV_CUSTOM` používa existujúce ByLayer pravidlá,
- built-in typy, Post footprint, COPY/COPYCLIP/WBLOCK a live synchronization ostávajú v pôvodnom pipeline.

## Povinné kompatibilitné pravidlá

1. Výpočty a geometrické rozhodovanie preferovať v Core.
2. AutoCAD API držať v adaptéri/UI vrstve.
3. Nepoužívať AutoCAD command automation, ak sa dá bezpečne použiť Managed API.
4. Myslieť na SAVE/REOPEN, WBLOCK, COPYCLIP/PASTECLIP.
5. Metadata držať prenositeľné a verzované.
6. Nezavádzať AutoCAD-2027-only riešenia bez skutočnej potreby.
7. Každá väčšia zmena musí prejsť Portable a Full Compatibility Gate.
8. Pri runtime lokalizácii používať explicitnú aplikačnú kultúru.
9. Pri grafických prvkoch oddeliť CAD-neutrálnu geometriu od CAD entity creation.
10. Veľké nové moduly navrhovať tak, aby neskôr dostali BricsCAD/ZWCAD adapter.

## Cieľová kompatibilita

Poradie:
1. AutoCAD 2027 ako hlavná vývojová verzia.
2. AutoCAD 2021–2027 kompatibilitný míľnik.
3. BricsCAD Proof of Concept.
4. BricsCAD adapter.
5. ZWCAD adapter.

Multi-CAD kompatibilita sa má overiť ešte pred tým, než projekt prerastie do príliš veľkého AutoCAD-špecifického roof automation modulu.

## Najbližšia priorita
1. Select Similar / filtre,
2. CSV export,
3. diagnostika a servis,
4. prezentačné a škálovacie nastavenia,
5. Custom element,
6. kompatibilitný checkpoint,
7. potom veľký modul automatickej geometrie strechy.

Presné poradie je v `ACAD_KROVY_ROADMAP.md`, úplný zásobník nápadov v `ACAD_KROVY_BACKLOG.md`.
