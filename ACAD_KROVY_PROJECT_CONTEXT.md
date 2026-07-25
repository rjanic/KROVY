# ACAD KROVY – PROJECT CONTEXT

**Aktualizované:** 25. 7. 2026

**Stabilný základ pred v0.17.0:** `b4833746dc0b66a2c22ecdebb4f6f048877c75fe`

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
- Custom element používa od schema v3 self-contained `CustomElementTypeId`, názov a prefix.
- Schema v4 pridáva jazykovo neutrálne per-element `AnnotationMode` a
  `ItemNumberLeaderStyle`; chýbajúce hodnoty znamenajú `FullLabel` a `Plain`.
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
- `FullLabel` sa pri live refreshi vracia na vypočítanú pozíciu,
- ručný lokálny offset rámčekového `ItemNumberLeader` sa ukladá v XData a
  zachováva pri refreshi, MOVE/ROTATE, AK_RENUMBER, COPY/COPYCLIP a SAVE/REOPEN,
- preferovaný rozmerový formát je `80x160`,
- budúca voľba aj `80/160`.
- hlavná anotácia je presne jeden MText (`FullLabel`) alebo jeden natívny MLeader
  (`ItemNumberLeader`/`DimensionsLeader`) na `KROV_POPIS`,
- `ItemNumberLeader` má `Plain`, `Circle`, `Slot` a `Rectangle`; rámčekové
  varianty sú natívny Spline MLeader s BlockContent/ITEM_NO a insertion-point
  attachmentom, kým Plain a DimensionsLeader zostávajú Straight,
- MText aj MLeader nesú portable XData väzbu na `SourceHandle`; slope a Post 90°
  anotácie zostávajú samostatné,
- default režimu je používateľské nastavenie, konkrétny režim je uložený na prvku.

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

### Annotation Modes / Režimy popisu a kótovania
- `FullLabel` zachováva pôvodný viacriadkový MText,
- `ItemNumberLeader` zobrazuje cez jeden natívny MLeader iba položkové označenie,
- `Plain` používa priamy MLeader a rámčekové `Circle`, `Slot`, `Rectangle`
  používajú self-contained BlockContent s atribútom `ITEM_NO`,
- rámčekové MLeadery používajú Spline, insertion-point attachment, uhol 40°,
  dodatočný offset 350 mm a persistentný lokálny manuálny offset,
- Circle používa jednu definíciu s priemerom 520 mm a BlockScale 1 bez
  textovo alebo typovo závislých veľkostí; staré 760/1800 mm varianty sa pri
  reconcile cielene normalizujú so zachovaním manuálneho offsetu,
- `DimensionsLeader` zobrazuje cez rovnaký natívny MLeader iba prierez vo formáte `80x160`,
- `AK_SETTINGS` ukladá default pre nové prvky a explicitne ho vie aplikovať na
  výber alebo všetky prvky,
- centrálny `TimberAnnotationService` a `ElementLabelService` konvertujú
  reprezentácie bez duplicít a rešpektujú režim pri AK_LABELS, live refreshi,
  COPY/COPYCLIP, WBLOCK a AK_RENUMBER,
- Post používa footprint-aware anchor hlavnej anotácie; jeho `⊥ 90°` sa nemení,
- Custom stručné režimy nezobrazujú používateľský názov definície.

### Layer Linetypes / Typy čiar timber vrstiev
- CAD-neutrálny `ElementLayerProfile` v3 persistuje stabilný názov vrstvy,
  ACI farbu, textový `LinetypeName` a číselný per-entity `LinetypeScale`;
  timber metadata schema zostáva v4,
- Krokva má default `DASHDOT`, ostatné built-in typy a spoločný `KROV_CUSTOM`
  používajú `Continuous`; scale je `0.5` pre Krokvu a `1.0` pre ostatné,
- AutoCAD adapter rešpektuje už načítaný `LinetypeTableRecord`, chýbajúci
  podporovaný typ načíta cez `Database.LoadLineTypeFile` z `acadiso.lin`
  vyhľadaného AutoCAD support paths a pri chybe použije `Continuous`,
- timber geometria používa Color aj Linetype `ByLayer`; annotation, slope
  a Post `⊥ 90°` entity zostávajú mimo layer-profile workflowu,
- otvorenie settings je read-only; existujúce vrstvy a prvky sa menia iba
  explicitným Apply na výber alebo všetky prvky,
- nové priradenie existujúcu konfliktnú vrstvu zachová bez explicitného entity
  override; odlišný vzhľad iba nových prvkov vyžaduje nový názov vrstvy,
- modal `AK_SETTINGS` používa validovaný callback a `Editor.StartUserInteraction`
  pre výber; po Apply zostáva otvorený, profilový fingerprint riadi iba
  persistenciu profilu a neblokuje opakovaný Selection/All dispatch,
- otvorené `AK_SETTINGS` regeneruje lokalizované annotation/style/apply/color
  zdroje atómovou výmenou kompletnej kolekcie po `LanguageChanged`; výber držia
  stabilné enumy a vždy sa obnoví až po dostupnosti novej ItemsSource,
- neblokujúce výsledky používa ako overlay banner so stabilným resource key,
  severity a argumentmi; jeden restartovaný `DispatcherTimer` ho skryje po 2 s,
- rovnaký fyzický layer môže zdieľať viac profilov iba s identickou farbou
  a linetype; konflikt sa odmietne pred uložením,
- vizuálny míľnik `v0.18.0 – Settings Fashion Look` zostáva iba v backlogu;
  v0.17.0 nemení vizuálny systém WPF okien,
- definícia linetype a entity scale sú súčasť DWG; globálne scale premenné sa nemenia.

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
