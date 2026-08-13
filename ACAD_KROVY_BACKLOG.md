# ACAD KROVY – BACKLOG

**Aktualizované:** 13. 8. 2026
**Stabilný commit v0.18.0:** `46ad0cfe555f9f3177de2d47d13bdda33d9a91a0`
**Stabilný commit v0.19.0:** `41373d235a357dee05033872a1df8fed8b3286d3`
**Stabilný commit v0.20.0:** `6564fc930d98e3eca591bafcccef709af07dc9a5`
**Stabilný commit v0.21.0:** `f98900c1bd257a8e5357f6e77eb6f118bd4930d3`

> Tento súbor je úplný zásobník nápadov. Poradie realizácie určuje `ACAD_KROVY_ROADMAP.md`.

## A. Produktivita

### Select Similar / filtre — dokončené vo v0.19.0
- `AK_SELECTSIMILAR`: typ, neotáčaný prierez, canonical materiál, ElementId,
  CuttingLength s toleranciou a CustomElementTypeId,
- read-only model-space scan, implied selection a bezpečné missing/invalid metadata.

### Prepojenie report ↔ DWG
- riadok reportu zvýrazní prvky,
- prvok nájde reportovú položku.

## B. Exporty a výkazy

### CSV — dokončené vo v0.19.0
- PickFirst, ručný výber alebo celý model space,
- individual/summarized rows, UTF-8 BOM, `;`, CRLF, lokalizované hlavičky,
  kultúrne desatinné čísla a bezpečný zápis.

### XLSX

### PDF

### Používateľské report templates
- stĺpce,
- poradie,
- firemné šablóny.

## C. Labely, kóty a grafika

### Formát rozmerov
- `80x160`
- `80/160`

### Režimy popisu podľa PDF — dokončené vo v0.16.0
- hotové: `FullLabel`, `ItemNumberLeader` a `DimensionsLeader`,
- hotové: `Plain`, `Circle`, `Slot` a `Rectangle`,
- hotové: rámčekové natívne Spline MLeadery s BlockContent/ITEM_NO,
  insertion-point attachmentom a prvým segmentom 60°,
- hotové: NoAnnotations a combined framed Circle/Rectangle/Slot s
  `LandingDistance = 350 mm`,
- hotové: persistentný lokálny framed offset po STRETCH, default pre nové prvky,
  explicitné použitie na výber/všetky, Post/Custom, COPY/COPYCLIP/WBLOCK a live refresh,
- budúce: reset manuálneho offsetu a výber `80x160`/`80/160`.

### Per-element mierka anotácií — rozšírené vo v0.22.0
- platný Element override > Drawing scale > fixný fallback 1:50,
- 1:25, 1:50, 1:75, 1:100 a custom 5–250 z jedného Core kontraktu,
- metadata schema 5, schema 4 read-only kompatibilita a COPY preservation,
- Save New / Apply Selection / Apply All pre celú Annotation sekciu,
- škálovanie hlavných, leader, framed/combined, Post a slope anotácií.

### Písmo/text styles
- kóty,
- labely,
- reportová tabuľka.

### Linetype podľa typu prvku — dokončené vo v0.17.0
- per-type voľba vedľa layer setting,
- Krokva default `DASHDOT`/`0.5`, ostatné typy `Continuous`/`1.0`,
- načítanie z `acadiso.lin`, fallback `Continuous`, ByLayer entity a DWG portability,
- bez zmeny globálnych linetype scale premenných,
- budúce: voliteľné nastavenia linetype scale podľa mierky výkresu.

### True-width element display
- obdĺžnik/obrys okolo centerline podľa šírky prvku.

### Auto trim / visual overlap
- prvky pod krokvami,
- vizuálne orezanie prezentačných obrysov.

## D. Vlastné prvky a materiály

### Custom element — základ dokončený vo v0.15.0
- hotové: lineárny workflow, stabilné ID, názov, prefix, signature, numbering,
  slope-aware dĺžka, stručný label, report, inspect, editácia výrobných údajov,
  premenovanie definície v aktuálnom DWG a portable metadata,
- budúce: samostatný správca definícií,
- budúce: layer a linetype pre každú definíciu namiesto spoločného profilu,
- budúce: import/export katalógu definícií.

## E. Automatická strecha

### `AK_ROOF` S1 – foundation implementovaný
- closed lightweight Polyline ako prvý produkčný vstup,
- CAD-neutrálny `RoofFootprintInput` → validácia → kanonický `RoofFootprint`,
- deterministické CCW poradie, prvý vrchol, indexy hrán, plocha, bounds,
  centroid a signature,
- `EffectiveClosed` pre native closed alebo zhodný prvý/posledný bod s presnou
  toleranciou `1e-9 mm`, bez zmeny zdrojovej Polyline,
- odmietnutie open/curved/non-planar/degenerate/self-intersecting geometrie,
- lokalizovaný výsledok, implied-selection preview a 2500 ms Fashion WPF
  `OpenLoop` upozornenie s následným retry, všetko bez zmeny DWG,
- bez persistence a bez zmeny timber metadata schema,
- AutoCAD 2027 host potvrdený: CW/CCW, Endpoint+Enter a DBMOD 5 → 5 po
  opakovaných neplatných `OpenLoop` výberoch.

### S2 Stage 1 – neutral simple-gable geometry implementovaný
- `RoofDefinition` + sklon + explicitný smer rieši obdĺžnikovú sedlovú strechu,
- axis-aligned aj otočené obdĺžniky, centered ridge, dve bounded 3D faces,
- `+d`/`-d`, CW/CCW a zdrojový start nemenia deterministický výsledok,
- eave datum `Z = 0`, `run = width / 2`, `rise = run * tan(slope)`,
- bez AutoCAD outputu, persistence, timber generation, UI a zmeny schémy/verzie.

### Ďalšie S2 stages – jednoduchá sedlová strecha MVP
- host preview, potom presah, rozostup a prierez krokiev,
- pomúrnice, hrebeňová väznica a common rafters,
- stabilný source/roof-plane/member lifecycle a až potom persistence schema,
- položky, anotácie a report/BOM integrácia v rozsahu simple-gable MVP.

### Budúci vstup bodmi a komplexné obrysy
- point-by-point vstup cez rovnaký neutrálny Core kontrakt,
- L/T/komplexné footprinty, viaceré hrebene, nárožia a úžľabia,
- stabilný `RoofPlaneId`.

### Typy strechy podľa PDF
- pultová,
- sedlová,
- valbová,
- polovalbová.

### Pultová
- výber hrebeňa/vysokej hrany.

### Sedlová
- výber štítu/štítovej steny.

### Polovalbová
- výber štítu,
- dĺžka polvalby.

### Ikony typov striech
- vizuálne podobné referencii z PDF.

### Automatické krokvy
1. začiatok prvej krokvy + rozostup,
2. symetricky podľa počtu,
3. symetricky podľa rozostupu.

### Nárožné/údolnicové krokvy
- automaticky alebo asistovane.

## F. Nastavenia a internacionalizácia

### v0.18.0 – Settings Fashion Look — dokončené
- centralizovaný moderný WPF dizajnový systém Light/Dark,
- ľavá navigácia, vektorové ikony, karty, sticky action bar a DPI-safe layout,
- ACI picker 1–255 s deterministickým preview RGB a iba indexovou persistenciou,
- editable layer hydration bez zápisu a idempotentné suffix reuse/create pri Apply,
- 10 PNG annotation presetov (5 × 2), NoAnnotations a oddelené standalone/
  combined framed workflowy,
- sekčné footery a AI vývojárska dokumentácia `AGENTS.md` + `.ai/`,
- zachovaný opakovaný Apply, runtime lokalizácia, DBMOD a schema hranice.

Budúce: Follow AutoCAD theme až po potvrdení stabilného verejného API kontraktu.

### Default layer names
- pre nové čisté inštalácie neutrálne/EN,
- nikdy automaticky nepremenovávať existujúce DWG.

### Units
- Metric,
- US,
- GB.

### Jazyk a jednotky oddeliť
- UI language ≠ unit system.

## G. Multi-CAD

### AutoCAD 2021–2027

### BricsCAD PoC

### BricsCAD adapter

### ZWCAD PoC

### ZWCAD adapter

Pravidlo:
Core zostáva bez konkrétneho CAD API.

## H. Diagnostika — dokončené vo v0.19.0

### Logging
- `%LOCALAPPDATA%\ACAD_KROVY\Logs`,
- denný log, 5 MB, 14 dní, stack trace, verzia, príkaz, čas a sanitizácia.

### `AK_DIAGNOSTICS` — dokončené

### Corrupt settings handling — dokončené
- `.corrupt.<yyyyMMdd-HHmmss>.json` backup pre všetkých päť lokálnych JSON stores,
- safe defaults; pri zlyhaní zálohy iba v pamäti a bez prepísania originálu.

## I. Inštalácia a distribúcia

### `.bundle` autoloader

### Installer

### Update/upgrade strategy

## J. Help a onboarding

### `AK_HELP` refinement

### Video návody priamo v UI
- video ku každej hlavnej funkcii,
- help/video button alebo catalog.

## K. Branding

### Medzinárodný názov produktu
Pracovný návrh:
- `RoofCAD`

Pred rozhodnutím:
- trademark search,
- domain availability,
- existing product conflicts,
- Autodesk branding rules.

## L. Technický dlh

Priebežne:
- delenie veľkých command classes,
- delenie label/live sync logiky,
- testovateľnosť,
- Compatibility Gate,
- runtime localization audit.

## M. Dokončené položky, ktoré už nie sú otvorený backlog

### Productivity & Reliability v0.19.0
- `AK_SELECTSIMILAR`, CAD-neutrálny filter a read-only model-space selection,
- `AK_EXPORTCSV`, individual/summarized Core CSV formatter a bezpečný zápis,
- thread-safe diagnostické logy, `AK_DIAGNOSTICS` a corrupt-settings recovery,
- všetkých šesť jazykov a Light/Dark WPF smoke testy,
- produkčný adapter zostáva AutoCAD 2027-only; metadata schema 4 a layer profile 3.

### First-run Language Onboarding v0.20.0
- `AppLanguageService.ResolveFirstRunLanguageCode` detekuje Windows UI jazyk,
- iba pri `SettingsFileState.Missing` používa `CultureInfo.InstalledUICulture`,
- podporované jazyky SK, CS, EN, DE, PL, FR, fallback EN,
- loaded a corrupt recovery stavy vracajú `result.Value`,
- `Load` automaticky nevolá `Save`,
- ProductVersion 0.20.0, metadata schema 4, layer profile 3.

### Annotation Scale Engine + Settings UI v0.21.0
- ProductVersion 0.21.0, AssemblyVersion/FileVersion 0.21.0.0,
- drawing override schema 1 v `ACAD_KROVY / DRAWING_SETTINGS`,
- samostatný UserDefault a aktuálny DWG, custom 10–200 a Core preview,
- automatický refresh bez duplicít pre všetky produkčné anotácie,
- metadata schema 4 a layer profile schema 3 zostávajú nezmenené.

### Per-Element Annotation Scale v0.22.0
- ProductVersion 0.22.0, AssemblyVersion/FileVersion 0.22.0.0,
- metadata schema 5, layer profile schema 3 a drawing settings schema 1,
- nullable element override s prioritou Element > Drawing > fallback 1:50,
- centralizovaný rozsah 5–250 bez orezania a bezpečný fallback invalid hodnôt,
- spoločné Save New / Apply Selection / Apply All workflow a jeden refresh batch,
- per-element context pre všetky produkčné anotácie vrátane slope a Post.

### Linetype settings v0.17.0
- per-type `LinetypeName` a `LinetypeScale` v layer profile v3,
- `DASHDOT`/`0.5` pre Krokvu a `Continuous`/`1.0` pre ostatné typy,
- AutoCAD načítanie z `acadiso.lin`, fallback, ByLayer a DWG portability,
- preserve-existing pravidlo pre nové prvky a opakovateľný Apply bez zatvorenia settings,
- explicitné Apply režimy bez zmeny metadata schema v4 alebo globálnych scale premenných.
- runtime lokalizácia otvoreného settings regeneruje dynamické ItemsSource
  atómovou výmenou podľa stabilných enumov bez prázdneho validačného medzistavu
  a Apply používa 2-sekundový overlay banner.

### Multilingual foundation
- SK/CS/EN/DE/PL/FR,
- runtime language switching,
- Ribbon/Classic Toolbar localization.

### Post rectangular footprint
- one-click rectangular Polyline,
- PL geometric closure,
- dedicated label,
- `⊥ 90°`,
- schema v2.

### Post from 4 separate LINE
- discovery,
- validation,
- conversion to one closed Polyline,
- one SourceHandle.

### Documentation and centralized version
- spoločné build metadata,
- runtime version provider pre startup a `AK_HELP`,
- version guard pre `.bundle` manifest,
- zosúladený README, project context, roadmap a backlog.

### `AK_RENUMBER`
- explicitné prečíslovanie všetkých položiek v aktuálnom DWG,
- samostatné súvislé série podľa typu,
- poradie podľa `CuttingLengthMm` s deterministickými tie-breakermi,
- atomická aktualizácia metadata a labelov,
- bez zmeny stabilnej automatickej numbering logiky.

### Lokalizovaný katalóg materiálov a adaptívne reporty
- šesť canonical material hodnôt s display názvami v SK/CS/EN/DE/PL/FR,
- bezpečné zachovanie neznámych legacy materiálov,
- canonical `Material` zostáva súčasťou metadata a `TimberElementSignature`,
- dvojriadkový reportový materiál bez oddeľovacej pomlčky,
- dynamické šírky Typ/Materiál podľa skutočného obsahu reportu.

Tieto funkcie zostávajú v histórii projektu a regresných testoch.
