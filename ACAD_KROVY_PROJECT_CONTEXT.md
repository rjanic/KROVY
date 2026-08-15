# ACAD KROVY – PROJECT CONTEXT

**Aktualizované:** 14. 8. 2026

**Predchádzajúci stabilný commit v0.21.0:** `f98900c1bd257a8e5357f6e77eb6f118bd4930d3`

**Branch:** `main`

**Verzia aplikácie:** autoritatívne v `Directory.Build.props`

**Aktuálny míľnik:** post-Stage-5 „Roof Validation WPF Notifications Polish“,
implementovaný lokálne a pripravený na manuálny AutoCAD HOST W1–W4 checkpoint

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
- kanonický polygonálny roof footprint a jeho validácia,
- plánovanie refreshu a testovateľné pravidlá.

Core nesmie obsahovať `Autodesk.AutoCAD.*`.

### CAD abstractions
Rozhrania medzi doménou a konkrétnym CAD prostredím.

### Infrastructure
Prenosná, hostiteľsky neutrálna implementácia diagnostického logovania,
bezpečného zápisu súborov a obnovy poškodených lokálnych JSON nastavení.
Nemá Autodesk ani WPF závislosti a je testovateľná mimo AutoCADu.

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
- `NoAnnotations` odstráni rodinu anotácií bez odstránenia timber metadata,
- `DimensionsWithItemNumber` používa samostatný composite lifecycle: framed
  položku a rozmerový MText pod sebou v strede horizontálnej landing čiary,
- `Plain` používa priamy MLeader a rámčekové `Circle`, `Slot`, `Rectangle`
  používajú self-contained BlockContent s atribútom `ITEM_NO`,
- standalone aj combined framed MLeadery používajú prvý segment 60°;
  combined `Circle`/`Rectangle`/`Slot` majú centralizovanú
  `LandingDistance = 350 mm`, horizontálny landing a persistentný manuálny offset,
- Circle používa jednu legacy definíciu a pri 1:50 výsledný priemer 400 mm
  cez BlockScale 1; staré 760/1800 mm varianty sa pri
  reconcile cielene normalizujú so zachovaním manuálneho offsetu,
- `DimensionsLeader` zobrazuje cez rovnaký natívny MLeader iba prierez vo formáte `80x160`,
- `AK_SETTINGS` ukladá default pre nové prvky a explicitne ho vie aplikovať na
  výber alebo všetky prvky,
- centrálny `TimberAnnotationService` a `ElementLabelService` konvertujú
  reprezentácie bez duplicít a rešpektujú režim pri AK_LABELS, live refreshi,
  COPY/COPYCLIP, WBLOCK a AK_RENUMBER,
- Post používa footprint-aware anchor hlavnej anotácie; jeho `⊥ 90°` sa nemení,
- Custom stručné režimy nezobrazujú používateľský názov definície.

### Per-Element Annotation Scale v0.22.0
- každý prvok dostáva immutable scale context s prioritou platný Element
  override > Drawing scale > fixný fallback 1:50,
- drawing settings schema 1 je uložená idempotentne v NOD
  `ACAD_KROVY / DRAWING_SETTINGS`; override má bezpečné idempotentné Remove,
- metadata schema 5 persistuje nullable `AnnotationScaleDenominatorOverride`;
  schema 4 sa číta bez zápisu a migruje sa iba pri reálnom write,
- jeden Core kontrakt povoľuje 5–250 bez orezania; neplatný element override
  prepadne na drawing context a neplatná drawing scale na fallback 1:50,
- `AK_SETTINGS` používa spoločné Save New / Apply Selection / Apply All akcie
  pre celú Annotation sekciu; Apply All nastaví drawing scale, odstráni všetky
  elementové override a vykoná najviac jeden refresh batch,
- predvoľby 1:25, 1:50, 1:75 a 1:100 zostávajú nezmenené,
- pri 1:50 sú dimension/FullLabel 125 mm, item number 135 mm, slope text
  80 mm, slope offset 100 mm a Circle Ø400 mm,
- faktor sa aplikuje presne raz na FullLabel, leadery, framed/combined
  anotácie, Post footprint, slope arrow/text a symboly 0°/90°,
- `AK_LABELS`, `AK_LABELSELECTED`, `AK_EDIT`, live refresh a COPY zachovávajú
  elementové override a zmiešané mierky,
- Core zostáva bez Autodesk typov, native annotative contexts sa nepoužívajú.

### Layer Linetypes / Typy čiar timber vrstiev
- CAD-neutrálny `ElementLayerProfile` v3 persistuje stabilný názov vrstvy,
  ACI farbu, textový `LinetypeName` a číselný per-entity `LinetypeScale`;
  timber metadata schema je aktuálne v5; layer profile zostáva v3,
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
- definícia linetype a entity scale sú súčasť DWG; globálne scale premenné sa nemenia.

### Settings Fashion Look v0.18.0
- `AK_SETTINGS` používa centralizované Light/Dark ResourceDictionary,
  dizajnové tokeny a reusable control styles bez externého theme frameworku,
- stabilná ľavá navigácia používa `SettingsWindowTabKind`; runtime preklad
  obnoví názvy bez zmeny sekcie, rozpracovaných hodnôt alebo Apply,
- ACI picker obsahuje presne indexy 1–255, drží dočasný výber do potvrdenia
  a do profilu zapisuje iba pôvodný číselný `ColorIndex`,
- CAD-neutrálna `AciColorPalette` poskytuje deterministický preview RGB;
  Autodesk farba sa naďalej vytvára iba v AutoCAD adaptéri z ACI indexu,
- annotation mode a frame style karty naďalej bindujú stabilné enumy;
  framed leader geometria, BlockContent a STRETCH sa nemenia,
- presne 10 annotation presetov používa embedded PNG 501 × 321 v mriežke 5 × 2;
  `NoAnnotations`, standalone framed a combined framed + dimensions zostávajú
  samostatné produkčné workflowy,
- sekčné footery oddeľujú layer/manufacturing Apply, annotation NewOnly,
  Selection/All a language Close; repeated Apply zostáva podporovaný,
- editovateľný layer ComboBox hydratuje lokálnu hladinu bez DWG zápisu;
  kanonický suffix sa reuse/create rieši idempotentne až v Apply transakcii,
- samostatný lokálny `settings-ui.json` uchová tému, poslednú sekciu a bounds;
  nikdy nie je súčasť DWG, layer profile fingerprintu alebo Apply requestu,
- zmena témy/navigácie/preview a otvorenie či zrušenie ACI pickeru nemajú
  drawing callback ani DBMOD code path,
- automatické sledovanie AutoCAD témy je odložené: audit nepotvrdil stabilný
  verejný AutoCAD 2027 theme event/property kontrakt.
- koreňový `AGENTS.md` a šesť dokumentov v `.ai/` zachytávajú architektúru,
  CAD hranice, lokalizáciu, testovanie, release proces a AI roadmap.

### Productivity & Reliability v0.19.0
- `AK_SELECTSIMILAR` používa CAD-neutrálny Core filter pre typ, neotáčaný
  prierez, canonical materiál, položku, výrobnú dĺžku s toleranciou a Custom ID;
  AutoCAD adapter iba read-only skenuje model space a nastaví implied selection,
- `AK_EXPORTCSV` exportuje PickFirst, ručný výber alebo celý model space
  v režime Individual/Summarized; Core formatter používa `;`, CRLF, UTF-8 BOM,
  lokalizované hlavičky, aplikačnú kultúru a zoskupovanie `TimberReportBuilder`,
- `AK_DIAGNOSTICS` zobrazuje verzie, hostiteľa/runtime, jazyk, stav lokálnych
  nastavení, log path a posledné udalosti v Light/Dark Fashion Look okne,
- logy sú v `%LOCALAPPDATA%\ACAD_KROVY\Logs`, rotujú denne a pri 5 MB,
  uchovávajú sa 14 dní a neobsahujú obsah/geometriu výkresu ani plnú DWG cestu,
- všetkých päť lokálnych JSON stores používa deterministickú `.corrupt`
  zálohu; pri zlyhaní zálohy zostáva originál nedotknutý a defaults iba v pamäti,
- produkčný adapter zostáva AutoCAD 2027 / .NET 10; metadata schema je 7
  a layer profile schema zostáva 3.

### STRECHY S1 – Roof Domain Foundation + Input Geometry
- `RoofPoint2D`, `RoofDirection2D`, `RoofEdge`, `RoofBoundingBox2D`,
  `RoofFootprint`, `RoofParameters`, `RoofDefinition`, `RoofFootprintInput` a
  typované validačné výsledky tvoria samostatný CAD-neutrálny Core základ,
- platný uzavretý polygon sa normalizuje proti smeru hodinových ručičiek,
  opakovaný koncový vrchol sa odstráni a prvý vrchol sa deterministicky volí
  ako lexikograficky najmenší bod `(X, Y)`; hrany majú stabilné indexy od nuly,
- `EffectiveClosed` akceptuje natívne `Polyline.Closed == true` alebo otvorenú
  Polyline, ktorej posledný bod sa zhoduje s prvým v presnej tolerancii
  `ClosingPointToleranceMm = 1e-9 mm`; väčší rozdiel sa neopravuje ani nesnapuje,
- validátor odmieta otvorený obrys, menej než tri jedinečné vrcholy, neplatné
  súradnice, duplicitné alebo príliš krátke hrany, samopriesek, degenerovanú
  plochu, nadbytočný kolineárny vrchol, krivky a nepodporovanú rovinu,
- `AK_ROOF` read-only vyberie closed lightweight Polyline, AutoCAD adapter
  prevedie iba neutrálne hodnoty, Core obrys overí a úspešný zdroj zostane
  zvýraznený cez implied selection ako ľahký nedeštruktívny S1 preview,
- iba `OpenLoop` používa centrované Fashion WPF upozornenie s auto-close približne
  2500 ms; klik myšou ho nezatvára, Esc sa aktivuje až po `ApplicationIdle` a po
  zatvorení pokračuje existujúci výberový retry loop,
- host test potvrdil zhodné CW/CCW kanonické výsledky, Endpoint+Enter
  `EffectiveClosed` vstup a read-only správanie: DBMOD zostal 5 pred aj po
  opakovaných neplatných `OpenLoop` výberoch,
- S1 nezapisuje XData/Xrecord, nemení timber metadata schema 7 ani drawing
  settings schema 1 a nepridáva roof nastavenia; persistence sa odkladá do S2,
  keď bude známy stabilný lifecycle zdroja, strešných rovín a členov,
- strešné roviny, hrebeňový solver, automatické krokvy, hip/valley geometria,
  item numbering, anotácie a BOM sú zámerne odložené.

### STRECHY S2 Stage 1 – neutral simple-gable geometry
- `SimpleGableRoofGeometrySolver` používa existujúci `RoofDefinition`, sklon a
  `RoofDirection2D` a bez CAD hosta rieši jednoduchú sedlovú strechu nad
  štvorvrcholovým obdĺžnikovým footprintom,
- podporuje world-XY aj ľubovoľne otočené obdĺžniky; samostatne overuje dĺžky,
  kolmosť, rovnobežnosť, zhodu protiľahlých strán a konvexnú orientáciu bez
  snapovania alebo opravy vstupu,
- explicitný smer hrebeňa musí byť rovnobežný s jednou osou obdĺžnika; opačný
  vektor predstavuje rovnakú fyzickú strechu a vedie k rovnakému kanonickému
  výsledku, takže aj štvorec zostáva deterministický,
- hrebeň spája stredy štítových strán, `run = transverse width / 2` a
  `rise = run * tan(slope)`; lokálna odkvapová rovina má `Z = 0`,
- výsledok obsahuje kanonický 3D ridge segment a dve deterministicky zoradené
  štvorbodové roof faces s actual footprint eave hranami a finite signature,
- Stage 1 nepridáva AutoCAD preview/entity creation, persistence, XData/Xrecord,
  timber generation, UI ani zmenu verzie alebo schémy; tieto časti S2 zostávajú
  budúcimi etapami.

### STRECHY S2 Stage 2 – AutoCAD transient wireframe preview
- `AK_ROOF` po S1 validácii explicitne vyžiada sklon a dva WCS body smeru
  hrebeňa, zavolá existujúci Stage 1 solver a host adapter už geometriu nerieši,
- `RoofTransientPreviewSession` mapuje local roof `Z` na konštantnú WCS eleváciu
  vybranej Polyline a cez `TransientManager` zobrazuje sedem unikátnych `Line`
  drawables: ridge, dva odkvapy a štyri štítové šikmé hrany,
- preview je viditeľné počas jednoduchého Enter/Esc inspection promptu; scoped
  `IDisposable` ho odstráni pri dokončení, cancel/Escape, výnimke a zrušení dokumentu,
- source Polyline zostáva `ForRead`; adapter neotvára write transaction, nepridáva
  DB entity, vrstvu, XData/Xrecord ani settings a nemení `Polyline.Closed`,
- ide iba o dočasnú vývojovú vizualizáciu geometrie, nie o roof object alebo
  timber generation; manuálne visual/DBMOD host overenie zostáva otvorené.

### STRECHY S2 Stage 3 – persistent roof-definition foundation
- vlastníkom definície je vybraná footprint Polyline; samostatný RegApp
  `DECORAIR_ACADKROVY_ROOF` uchováva iba roof schema `1`, typ `SimpleGable`,
  plnú hodnotu sklonu, kanonický smer hrebeňa a kanonický footprint signature,
- nový roof codec a restore politika sú CAD-neutral v Core; host XData reader je
  read-only a ne registruje RegApp, kým používateľ po preview výslovne nepotvrdí zápis,
- krátky zamknutý write scope zachová všetky cudzie XData sekcie a atomicky zapíše
  najviac jednu roof sekciu; uložená definícia sa znovu rieši Stage 1 solverom,
- neplatná, future-schema, unsupported alebo stale definícia sa neprepisuje ani
  neopravuje; transient preview zostáva jedinou roof vizualizáciou,
- SAVE/CLOSE/REOPEN a DBMOD host proof je povinný otvorený test; MOVE/ROTATE/
  STRETCH/COPY/WBLOCK lifecycle a roof edit/regeneration zostávajú budúcou etapou,
- bez permanentnej roof geometrie, Xrecordov, drawing-settings storage a timber generation.

### STRECHY S2 Stage 4 – rigid-transform roof lifecycle foundation
- nové definície používajú roof schema `2`: plný sklon, ridge edge-family viazanú
  na natívnu topológiu Polyline `0→1`/`1→2` a rigidne invariantný descriptor
  `vertex count + CW/CCW + dve susedné dĺžky`; neukladajú absolútny WCS smer ani súradnice,
- lazy read extrahuje aktuálnu Polyline `ForRead`, overí S1 a descriptor, odvodí
  aktuálny WCS ridge smer zo zvolenej source edge-family a znovu použije Stage 1 solver,
- MOVE, ROTATE a opakované rigidné transformácie sú podporené Core kontraktom bez
  prepisovania XData; os štvorca zostáva viazaná na fyzickú source edge-family,
- STRETCH/zmena rozmerov zostáva stale bez preview, opravy alebo zápisu; schema `1`
  sa číta pôvodnou absolútnou Stage 3 cestou a automaticky sa nemigruje,
- COPY je automaticky pokrytá rovnakou owner-local semantikou, ale produkčná podpora
  sa potvrdí až HOST dôkazom, že AutoCAD kopíruje dedikované XData,
- bez reactorov, write-on-read, permanentnej roof geometrie a timber generation;
  MOVE/ROTATE/COPY/SAVE-REOPEN a DBMOD HOST acceptance zostáva otvorená.

### STRECHY S2 Stage 5 – permanent simple-gable roof display
- CAD-neutrálny `SimpleGableRoofWireframe` enumeruje výhradne zo solver výsledku
  presne sedem rolí: hrebeň, dva odkvapy a štyri štítové sklonové hrany,
- potvrdené uloženie novej strechy atomicky zapíše semantic roof definition aj sedem
  natívnych `Line` na `KROV_STRECHA`; display je regenerovateľná cache, nie zdroj pravdy,
- deti používajú samostatný RegApp `DECORAIR_ACADKROVY_ROOF_DISPLAY`, display schema `1`,
  owner handle, stabilnú rolu a generation signature; roof schema zostáva `2`,
- existujúci aktuálny display je read-only; missing/stale/damaged display sa obnoví iba
  po explicitnom Yes a maže iba deti daného ownera, bez zápisu do source Polyline,
- MOVE/ROTATE sa prejavia až explicitnou regeneráciou cez `AK_ROOF`; STRETCH stale
  definícia sa naďalej odmieta a žiadne reactors ani timber generation neboli pridané,
- Stage 5 SAVE/REOPEN, lifecycle, DBMOD, Undo/Redo a vizuálny HOST checkpoint prešli;
  S2 tým nie je dokončené,
- UX rozšírenie udržiava deterministickú AutoCAD GROUP `AK_ROOF_<OWNER_HANDLE>`
  s presne ôsmimi členmi (owner + 7 display Lines); skupina je iba opraviteľná
  interakčná cache a jej absencia nemení platnosť semantic roof definition,
- `AK_ROOF` prijíma source Polyline aj ľubovoľné display dieťa a owner rieši výhradne
  cez jeho display XData/handle, bez geometrických alebo nearest-object heuristík,
- ridge používa samostatnú ByLayer vrstvu `KROV_STRECHA_HREBEN` ACI 1; ostatných
  šesť hrán zostáva na oddelenej `KROV_STRECHA`, tiež ACI 1,
- PICKSTYLE zostáva používateľským nastavením; pri zapnutom group selection môže
  spoločný MOVE/ROTATE všetkých ôsmich členov ponechať display geometricky current
  bez rebuildu, aj keď pôvodná diagnostická generation signature ostala nezmenená,
- žiadny BlockReference, custom entity, reactor ani timber generation nebol pridaný.
- source-only COPY zostáva podporovaný cez nový owner handle; COPY celej GROUP sa bez
  úplného deep-clone HOST dôkazu zámerne nevyhlasuje za podporovaný kontrakt.
- úzky post-Stage-5 UX checkpoint smeruje blokujúce selection/footprint validácie cez
  existujúce 2500 ms WPF upozornenie bez mouse dismiss/input bleed a bez DWG zápisu;
  stale semantic/display stavy, prompty a úspešný workflow zostávajú CLI.

### STRECHY S2 Stage 6 – automatic SimpleGable rafters (dokončený HOST checkpoint)
- explicitný `AK_ROOF_RAFTERS` obnoví platnú persisted SimpleGable definíciu cez
  existujúci owner resolver a solver; display cache nie je autoritatívny vstup,
- kompaktný Fashion WPF dialóg nastavuje Width/Height/Smax/Material, zobrazuje live
  summary a read-only slope z vybranej strechy; prvé hodnoty sú 80/160/900/Smrek C24
  a posledná úspešná štvorica sa ukladá do existujúceho per-user `settings-ui.json`,
- Autodesk-free layout používa zadanú `Krokva.WidthMm` aj v production tvorbe: pre
  `L`, pôdorysnú šírku `B` a maximum `Smax` platí
  `U=L-B`, `intervals=max(1,ceil(U/Smax))`; osi sú rovnomerne od `B/2` po `L-B/2`,
- každá stanica vytvorí dve source `Line` v smere odkvap→hrebeň s normálnym KROVY
  `IsSlopeDirectionReversed=true`, takže slope arrow smeruje hrebeň→odkvap aj po
  MOVE/ROTATE refresh a `AK_FLIPSLOPE` ho môže štandardne obrátiť; vznikajú ako bežné
  inteligentné `Krokva` cez timber schema `7`, existujúce vrstvy/defaults a item identity,
  bez automatických anotácií,
- sekundárny RegApp `DECORAIR_ACADKROVY_ROOF_TIMBER` schema `1` nesie owner handle,
  member/face rolu, index a počet staníc, requested spacing a layout signature,
- krokvy nie sú členmi `AK_ROOF_<handle>`; roof GROUP zostáva presne owner + 7 display
  Lines. Bezpečná replacement služba zatiaľ neexistuje, preto existujúci generated set
  command iba deteguje a odmietne bez erase/write,
- full 8-member roof GROUP COPY používa na nových display Lines hostový XData `1005`
  soft-pointer owner handle, ktorý AutoCAD pri deep clone preloží na skopírovanú source
  Polyline. Staršie schema-1 display dáta bez `1005` majú prísny read-only fallback iba
  pre jednoznačnú topológiu 1 platná RoofDefinition Polyline + 7 Line rolí; originálna
  strecha ani jej generated rafters sa neprepisujú a copied roof začína bez krokiev,
- Ribbon panel Strechy používa native `Strecha` dropdown: Sedlová spúšťa existujúci
  `AK_ROOF`, Valbová/Polvalbová/Pultová sú viditeľné disabled a samostatné `Krokvy`
  spúšťajú `AK_ROOF_RAFTERS`; persistentné 16/32 PNG ikony sledujú technický roof/PDF
  vizuálny jazyk a existujúcu resource pipeline,
- budúca Stanová/Pyramídová strecha nebude samostatný sémantický typ, command, ikona
  ani schéma. Je to štvorcový špeciálny prípad budúcej `HipRoof`: pri obdĺžnikovom
  pôdoryse má vyriešený hrebeň kladnú dĺžku, pri rovnosti pozdĺžneho a priečneho
  rozmeru v rámci budúcej geometrickej tolerancie sa deterministicky zrúti na jeden
  vrchol (`ridgeLength -> 0`). Ribbon preto zachová iba typ Valbová; Stage 6 túto
  geometriu neimplementuje,
- modal WPF summary nahrádza riskantný live transient preview a je jediným potvrdením;
  Cancel nevstúpi do write scope. Stage 6 nepridáva reactors, live sync,
  generated-rafter COPY/WBLOCK remapping, iné timber typy ani Stage 7 funkcionalitu,
- HOST R1–R10 je PASS: Width/2 offset a equalized spacing, normálny AK_INSPECT/report,
  SAVE/CLOSE/REOPEN s DBMOD 0, rotated aj square retained-ridge prípady, Cancel 0→0,
  bezpečné odmietnutie existujúceho setu, stale STRETCH rejection bez dodatočného
  zápisu a coherent Undo/Redo batch. Downhill šípky Face0/Face1 ostávajú ridge→eave
  po MOVE/ROTATE; Width 100 dáva 50 mm edge offset,
- finálny full-GROUP COPY HOST test vytvoril 24 pôvodných + 24 nezávislých copied
  krokiev = presne 48. Skorších 72 bolo potvrdené ako používateľský COPY Multiple,
  ktorý vytvoril dve kópie, nie chyba ownership implementácie. AK_ROOF po úspechu
  čistí iba implied selection a HOST potvrdil, že GROUP už nezostáva zvýraznený.

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
1. samostatný SimpleGable Roof STRETCH / Resize Lifecycle checkpoint; platný obdĺžnik
   má zachovať typ, slope a ridge family a prepočítať roof geometry/display,
2. explicitný stale/regeneration lifecycle pre už generated rafters; pomúrnice,
   väznice a ostatné roof timber typy zostávajú mimo aktuálneho checkpointu,
3. persistence navrhnúť až spolu so stabilnou S2 identitou zdroja a rovín,
4. compatibility checkpoint a alternatívne CAD adaptéry naďalej riešiť bez
   prenikania vendor typov do Core.

Presné poradie je v `ACAD_KROVY_ROADMAP.md`, úplný zásobník nápadov v `ACAD_KROVY_BACKLOG.md`.
