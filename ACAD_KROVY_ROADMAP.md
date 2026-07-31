# ACAD KROVY – ROADMAP

**Aktualizované:** 31. 7. 2026
**Predchádzajúci stabilný commit v0.18.0:** `46ad0cfe555f9f3177de2d47d13bdda33d9a91a0`
**Predchádzajúci stabilný commit v0.19.0:** `41373d235a357dee05033872a1df8fed8b3286d3`
**Predchádzajúci stabilný commit v0.20.0:** `6564fc930d98e3eca591bafcccef709af07dc9a5`
**Aktuálny míľnik:** v0.21.0 „Annotation Scale Engine + Settings UI“, dokončený, automaticky otestovaný a manuálne overený

Tento dokument určuje odporúčané poradie ďalšieho vývoja. Úplný zásobník nápadov je v `ACAD_KROVY_BACKLOG.md`.

# FÁZA A – UPRATANIE A PRODUKTIVITA

## 1. Dokumentácia + centralizovaná verzia — DOKONČENÉ
- README, ROADMAP, BACKLOG a project context sú zosúladené,
- verzia má autoritatívny zdroj v `Directory.Build.props`,
- `AK_HELP` a startup ju získavajú z assembly metadata,
- Compatibility Gate kontroluje zhodu s `.bundle` manifestom.

Pri budúcom About okne použiť existujúci version provider, nie nový literal.

## 2. `AK_RENUMBER` — DOKONČENÉ
- explicitne prečísluje celý aktuálny DWG; čiastočný výber sa zámerne nepodporuje,
- každému typu prideľuje vlastnú súvislú sériu od 1,
- primárne radí unikátne výrobné signatúry podľa `CuttingLengthMm`,
- pri zhode používa deterministicky prierez a materiál,
- aktualizuje XData položkové označenie a existujúce labely v jednej transakcii,
- bežné automatické číslovanie zostáva stabilné a medzery nekompaktuje.

## 3. Select Similar / filtre — DOKONČENÉ VO v0.19.0
- `AK_SELECTSIMILAR` filtruje typ, neotáčaný prierez, canonical materiál,
  ElementId, CuttingLength s nezápornou toleranciou a Custom ID,
- CAD-neutrálny filter bezpečne odmieta chýbajúce alebo neplatné metadata,
- AutoCAD adapter iba read-only skenuje model space a nastaví implied selection.

## 4. CSV export — DOKONČENÉ VO v0.19.0
- `AK_EXPORTCSV` podporuje PickFirst, ručný výber a celý model space,
- Individual a Summarized používajú typované dáta a `TimberReportBuilder`,
- UTF-8 BOM, `;`, CRLF, CSV escaping, lokalizované hlavičky a aplikačná kultúra,
- bezpečný dočasný zápis a štandardné potvrdenie prepísania; bez zmeny DWG.

## 5. Diagnostika a servis — DOKONČENÉ VO v0.19.0
- logy v `%LOCALAPPDATA%\ACAD_KROVY\Logs`, denne, najviac 5 MB na súbor,
  retencia 14 dní a bezpečné čistenie,
- `AK_DIAGNOSTICS` zobrazuje verzie, hostiteľa/runtime, jazyk, settings stavy
  a posledné udalosti,
- všetkých päť lokálnych JSON stores používa `.corrupt` recovery; ak záloha
  zlyhá, originál sa neprepíše a defaults zostanú iba v pamäti.

# FÁZA B – PREZENTAČNÉ A VÝKRESOVÉ NASTAVENIA

## 6. Linetype podľa typu prvku — DOKONČENÉ VO v0.17.0
- `AK_SETTINGS` ponúka per-type linetype vedľa názvu a farby vrstvy,
- Krokva používa default `DASHDOT` a entity scale `0.5`; ostatné typy
  a `KROV_CUSTOM` používajú `Continuous` a scale `1.0`,
- podporované štandardné definície sa načítajú z metrického `acadiso.lin`
  cez AutoCAD support paths, s bezpečným fallbackom `Continuous`,
- timber entity používajú Color aj Linetype `ByLayer`,
- nové prvky neprepisujú existujúcu konfliktnú vrstvu; vybrané/všetky prvky
  ju môžu zmeniť iba explicitným Apply,
- settings zostáva po Apply otvorený a zachová kontext; Selection a All sa dajú
  opakovať bez zmeny profilu, pričom zapisujú iba skutočné rozdiely,
- existujúci DWG sa nemení otvorením settings ani lokálnym uložením profilu,
- typ čiary zostáva portable v DWG; `LTSCALE` sa nemení,
- metadata timber prvku zostáva schema v4 a Core bez Autodesk dependencies.

## 7. Mierka anotácií — DOKONČENÉ VO v0.21.0
- priorita Drawing > UserDefault > fallback 1:50,
- drawing persistence `ACAD_KROVY / DRAWING_SETTINGS` schema 1,
- `AK_SETTINGS` pre aktuálny DWG a default nových výkresov,
- predvoľby 1:25, 1:50, 1:75, 1:100 a custom 10–200,
- živý Core-based typography/BlockScale preview a odstránenie override,
- automatický spoločný refresh existujúcich anotácií bez duplicít,
- FullLabel, leadery, framed/combined, Post footprint, slope a 0°/90° symboly,
- immutable context, scale presne raz, Core bez Autodesk a bez native
  annotative contexts,
- 421 lokalizačných kľúčov v každom zo 6 jazykov.

## 8. Písmo a text styles
Nastavenia pre:
- kóty,
- labely/popisy,
- reportovú tabuľku.

Preferovať CAD text styles.

Poznámka:
V používateľskom PDF je pri bode o písme uvedené „default pre všetko 1:50“. Roadmap to interpretuje ako default mierky 1:50. Potrebné neskôr potvrdiť.

## 9. v0.18.0 – Settings Fashion Look — IMPLEMENTOVANÉ
- centralizované Light/Dark ResourceDictionary, dizajnové tokeny a reusable styles,
- moderná ľavá navigácia so stabilnými section ID a vektorovými ikonami,
- resizable 1500 × 900 okno, minimum 1250 × 720, wrapping, DPI a keyboard focus,
- plná ACI paleta 1–255 s popup pickerom, priamym indexom a preview,
- moderná layer tabuľka s read-only hydration a idempotentným suffix reuse/create,
- presne 10 embedded PNG annotation presetov (5 × 2), NoAnnotations a oddelené
  standalone/combined framed workflowy,
- sekčné footery, formulárový stav a zjednotený dvojsekundový overlay banner,
- lokálna persistence Light/Dark, sekcie a window bounds mimo DWG,
- zachovaný Apply callback, DBMOD pravidlá, profile v3 a metadata schema v4.
- AI vývojárska dokumentácia v `AGENTS.md` a `.ai/`.

Automatický Follow AutoCAD režim zostáva budúce rozšírenie, kým nebude
potvrdený stabilný verejný hostiteľský theme kontrakt.

## 10. Režimy kótovania a popisov — DOKONČENÉ VO v0.16.0
- `FullLabel`: pôvodný automatický MText,
- `ItemNumberLeader`: natívny MLeader iba s položkou, napr. `K1`,
- `ItemNumberLeader` podporuje `Plain`, `Circle`, `Slot` a `Rectangle`,
- Circle/Slot/Rectangle používajú natívny Spline MLeader s BlockContent,
  atribútom `ITEM_NO`, insertion-point attachmentom a prvým segmentom 60°,
- hotové: `NoAnnotations` a combined framed `DimensionsWithItemNumber`
  (`Circle`, `Rectangle`, `Slot`) s `LandingDistance = 350 mm`,
- ručne posunutá rámčeková anotácia zachováva lokálny offset pri refreshi,
  AK_RENUMBER a zmenách source geometrie,
- `DimensionsLeader`: rovnaká natívna MLeader reprezentácia iba s prierezom `80x160`,
- default pre nové prvky a explicitné použitie na výber/všetky cez `AK_SETTINGS`,
- per-element schema v4 persistence, COPY/COPYCLIP/WBLOCK a live reconcile,
- footprint-aware Post placement a nezávislé slope/Post anotácie.

Budúce rozšírenie: voliteľný formát rozmerov `80/160` a reset manuálneho
rámčekového offsetu.

## 11. Vlastný používateľský prvok / Custom Element — DOKONČENÉ
- `AK_CUSTOM` pre lineárne LINE/LWPOLYLINE,
- stabilné Custom ID, persistentný používateľský názov a validovaný prefix,
- self-contained schema v3 metadata plus opakovane použiteľný AppData katalóg,
- samostatné signature/numbering série podľa Custom ID,
- explicitné premenovanie názvu definície cez AK_EDIT pre celé aktuálne DWG,
- integrácia labelov, inspectu, reportov, AK_EDIT, COPY/COPYCLIP/WBLOCK a live sync,
- slope-aware automatický výpočet dĺžky zdieľaný s Krokvou,
- spoločný profil vrstvy `KROV_CUSTOM`.

Budúce rozšírenia: samostatný správca definícií a per-definition layer/linetype.

# FÁZA C – KOMPATIBILITNÝ CHECKPOINT

## 12. AutoCAD 2021–2027 Compatibility Milestone
- build/package stratégia,
- API rozdiely,
- smoke test na podporovaných verziách,
- autoloader manifesty,
- rozšírenie Compatibility Gate.

## 13. BricsCAD Proof of Concept
Minimálne:
- načítanie pluginu,
- XData metadata,
- assignment,
- AK_INSPECT,
- AK_REPORT,
- základné labely,
- selection a transactions.

Cieľ:
overiť abstractions pred veľkým roof automation modulom.

# FÁZA D – AUTOMATICKÁ GEOMETRIA STRECHY

## 14. Roof Domain Foundation
Pracovný príkaz:
`AK_ROOF`

CAD-neutrálne modely:
- obrys,
- strešná rovina,
- hrana,
- hrebeň,
- nárožie,
- úžľabie,
- štítová hrana,
- sklon,
- `RoofPlaneId`.

## 15. Automatické vytvorenie strechy zadaním bodov
Používateľ:
- zadá body obrysu,
- zvolí typ strechy cez názov a ikonku.

Typy podľa PDF:

### Pultová
- doplnkový výber hrebeňa/vysokej hrany pultu.

### Sedlová
- výber štítu/štítovej steny.

### Valbová
- automatické odvodenie rovín a nároží.

### Polovalbová
- výber štítu,
- zadanie dĺžky polvalby.

UI:
ikonky podobné referencii v PDF.

## 16. Strešné roviny
- výber konkrétnej strešnej roviny,
- stabilný `RoofPlaneId`,
- sklon,
- použitie ako vstup pre automatické krokvy.

## 17. Automatické kreslenie krokiev
Tri režimy:

### A. Začiatok prvej krokvy + rozostup

### B. Symetricky podľa počtu krokiev

### C. Symetricky podľa rozostupu krokiev

Výsledné krokvy majú byť okamžite inteligentné prvky.

## 18. Nárožné a údolnicové krokvy
- automatické alebo asistované vytvorenie,
- skutočná dĺžka podľa strešných rovín,
- report a numbering.

## 19. True-width zobrazenie prvkov
- obdĺžnik/obrys okolo centerline podľa šírky prvku,
- oddeliť nosnú centerline geometriu od prezentačného obrysu.

## 20. Automatický trim / vizuálne prekrytie
- vizuálne orezanie prezentačných obrysov,
- hlavne pri prvkoch pod krokvami a pri krížení.

Implementovať až po stabilnom true-width systéme.

# FÁZA E – VÝKAZY A PRODUKČNÉ VÝSTUPY

## 21. XLSX export

## 22. PDF výrobný výkaz

## 23. Prepojenie reportu s výkresom
- riadok reportu → zvýrazniť prvky,
- prvok → nájsť reportovú položku.

## 24. Používateľské šablóny výstupov
- voliteľné stĺpce,
- poradie,
- firemné šablóny.

# FÁZA F – INTERNACIONALIZÁCIA A PRODUKTIZÁCIA

## 25. Material catalog — DOKONČENÉ
- šesť stabilných canonical material hodnôt,
- lokalizované display názvy pre SK/CS/EN/DE/PL/FR,
- výber cez `AK_EDIT` bez lokalizovaných textov v metadata alebo signatúre,
- neznáme legacy hodnoty sa zachovávajú bez migrácie,
- lokalizované `AK_INSPECT` a adaptívne dvojriadkové reporty.

## 26. Default jazyk pri prvom spustení — DOKONČENÉ VO v0.20.0
- Platný existujúci `application-settings.json` má prednosť,
- iba `SettingsFileState.Missing` používa `CultureInfo.InstalledUICulture`,
- podporované Windows UI jazyky: SK, CS, EN, DE, PL, FR,
- nepodporovaný jazyk používa fallback EN,
- poškodený JSON používa existujúci recovery/default mechanizmus,
- poškodený JSON sa nepovažuje za prvé spustenie,
- `Load` automaticky nevolá `Save`,
- nový JSON vznikne až po vedomej zmene jazyka používateľom,
- manuálne overené: chýbajúci JSON → SK, ručný výber DE → vytvorenie JSON, reštart zachoval DE, corrupt recovery fungovalo, DBMOD = 0,
- ProductVersion 0.20.0, AssemblyVersion a FileVersion 0.20.0.0,
- metadata schema zostáva 4, layer profile schema zostáva 3,
- zostáva 406 lokalizačných kľúčov v každom zo 6 jazykov.

## 27. Default vrstvy pre nové inštalácie
- neutrálne alebo EN názvy,
- existujúce DWG nikdy automaticky nepremenovávať.

## 28. Jednotkové systémy
- Metric
- US
- GB

Interná kanonická geometria zostáva jednoznačná. Jazyk UI a jednotkový systém sú oddelené.

## 29. Medzinárodný názov produktu
Pracovný návrh:
`RoofCAD`

Pred rozhodnutím preveriť:
- ochranné známky,
- domény,
- existujúce produkty,
- Autodesk branding pravidlá.

Rebranding nesmie meniť historické metadata ani `AK_...` commands.

# FÁZA G – DISTRIBÚCIA A PODPORA

## 30. AutoCAD Autoloader `.bundle`
- odstrániť ručný NETLOAD,
- PackageContents,
- verzovanie,
- podporované AutoCAD verzie.

## 31. Inštalátor
- install,
- upgrade,
- uninstall,
- zachovanie settings.

## 32. Video návody priamo v programe
Požiadavka z PDF:
- video pre každú hlavnú funkciu,
- Help/Video action priamo v UI.

Realizovať až keď sa hlavné workflow prestanú výrazne meniť.

# FÁZA H – ĎALŠIE CAD PLATFORMY

## 33. BricsCAD plný adapter

## 34. ZWCAD Proof of Concept + adapter

# FÁZA I – PRIEBEŽNÝ TECHNICKÝ DLH

Priebežne pri dotyku s danou oblasťou:
- deliť veľký `AcKrovyCommands.cs`,
- zmenšovať `ElementLabelService`,
- izolovať live sync rozhodovanie od AutoCAD runtime,
- rozširovať testovanie adaptérovateľnej logiky,
- aktualizovať dokumentáciu po veľkých míľnikoch,
- udržiavať Compatibility Gate,
- auditovať runtime localization proti implicitnej thread culture.

# ODPORÚČANÉ NAJBLIŽŠIE PORADIE

1. AutoCAD 2021–2027 compatibility checkpoint.
2. BricsCAD PoC.
3. Samostatne rozhodnúť o konfigurovateľných CAD text styles.
4. Roof Domain Foundation.
5. Automatická strecha z bodov a strešné roviny.
6. Automatické, nárožné a údolnicové krokvy.
7. True-width a automatický vizuálny trim.
8. XLSX/PDF/report linking.
9. Internacionalizácia, distribúcia a ďalšie CAD adaptéry.
