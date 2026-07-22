# ACAD KROVY – BACKLOG

**Aktualizované:** 22. 7. 2026  
**Aktuálny stabilný commit:** `4a951041e2deef40a127ac9560cf6fb2ba4b6a5b`

> Tento súbor je úplný zásobník nápadov. Poradie realizácie určuje `ACAD_KROVY_ROADMAP.md`.

## A. Produktivita

### `AK_RENUMBER`
- explicitné prečíslovanie,
- podľa typu a dĺžky,
- výber alebo celý DWG,
- aktualizácia labelov a reportov.

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

### Režimy popisu podľa PDF
- súčasný label,
- iba číslo položky s leader/kótovacou čiarou,
- iba rozmer s leader/kótovacou čiarou.

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

### Linetype podľa typu prvku
- voľba vedľa layer setting,
- Krokva default bodko-čiarkovaná podľa používateľskej požiadavky.

### True-width element display
- obdĺžnik/obrys okolo centerline podľa šírky prvku.

### Auto trim / visual overlap
- prvky pod krokvami,
- vizuálne orezanie prezentačných obrysov.

## D. Vlastné prvky a materiály

### Custom element
- vlastný používateľský názov,
- vzor podľa trámového prvku,
- stabilná neutral technical identity,
- prefix,
- layer,
- linetype,
- length mode,
- allowance,
- report,
- label.

### Material presets
- oddeliť interný preset code od lokalizovaného display názvu,
- staré voľné materiály neprepisovať.

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
- centralizácia verzie,
- README,
- ROADMAP,
- BACKLOG,
- delenie veľkých command classes,
- delenie label/live sync logiky,
- testovateľnosť,
- Compatibility Gate,
- runtime localization audit.

## M. Dokončené položky, ktoré už nie sú otvorený backlog

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

Tieto funkcie zostávajú v histórii projektu a regresných testoch.
