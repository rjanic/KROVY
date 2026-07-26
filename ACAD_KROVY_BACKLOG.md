# ACAD KROVY – BACKLOG

**Aktualizované:** 25. 7. 2026
**Stabilný základ pred v0.17.0:** `b4833746dc0b66a2c22ecdebb4f6f048877c75fe`

> Tento súbor je úplný zásobník nápadov. Poradie realizácie určuje `ACAD_KROVY_ROADMAP.md`.

## A. Produktivita

### Select Similar / filtre
- typ,
- prierez,
- materiál,
- ElementId,
- item number,
- dĺžka,
- chyby,
- chýbajúce metadata.

### Prepojenie report ↔ DWG
- riadok reportu zvýrazní prvky,
- prvok nájde reportovú položku.

## B. Exporty a výkazy

### CSV
- výber alebo celý DWG,
- individual/summarized rows.

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
  insertion-point attachmentom, uhlom 40° a offsetom 350 mm,
- hotové: persistentný lokálny framed offset po STRETCH, default pre nové prvky,
  explicitné použitie na výber/všetky, Post/Custom, COPY/COPYCLIP/WBLOCK a live refresh,
- budúce: reset manuálneho offsetu, výber `80x160`/`80/160` a annotation scale.

### Mierka anotácií
- 1:25
- 1:50
- 1:75
- 1:100
- custom
- odporúčaný default 1:50.

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

### `AK_ROOF`
- body obrysu,
- strešné roviny,
- stabilný RoofPlaneId.

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

### Default language onboarding
- Windows UI language,
- podporovaný jazyk automaticky,
- fallback EN.

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

## H. Diagnostika

### Logging
- `%APPDATA%\ACAD_KROVY\logs`,
- stack trace,
- verzia,
- príkaz,
- čas.

### `AK_DIAGNOSTICS`

### Corrupt settings handling
- `.corrupt` backup,
- safe defaults,
- informovanie používateľa.

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
