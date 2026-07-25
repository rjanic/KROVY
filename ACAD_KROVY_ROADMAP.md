# ACAD KROVY – ROADMAP

**Aktualizované:** 25. 7. 2026
**Stabilný základ pred v0.17.0:** `b4833746dc0b66a2c22ecdebb4f6f048877c75fe`

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

## 3. Select Similar / filtre
Výber podľa:
- typu,
- prierezu,
- materiálu,
- ElementId / výrobnej položky,
- označenia,
- dĺžky,
- chybných alebo neúplných dát.

Neskôr prepojiť s reportom.

## 4. CSV export
- výber alebo celý DWG,
- položka,
- typ,
- materiál,
- rozmery,
- actual length,
- cutting length,
- množstvo,
- celková dĺžka,
- kubatúra.

Report/export model držať v Core.

## 5. Diagnostika a servis
- logy do `%APPDATA%\ACAD_KROVY\logs`,
- čas, verzia, príkaz, stack trace,
- bezpečné spracovanie poškodených settings,
- `.corrupt` záloha,
- `AK_DIAGNOSTICS`.

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

## 7. Mierka anotácií
Podporovať:
- 1:25
- 1:50
- 1:75
- 1:100
- vlastná hodnota

Odporúčaný default:
- 1:50

Mierka má ovplyvňovať:
- kóty,
- leader popisy,
- automatické labely,
- reportovú tabuľku.

## 8. Písmo a text styles
Nastavenia pre:
- kóty,
- labely/popisy,
- reportovú tabuľku.

Preferovať CAD text styles.

Poznámka:
V používateľskom PDF je pri bode o písme uvedené „default pre všetko 1:50“. Roadmap to interpretuje ako default mierky 1:50. Potrebné neskôr potvrdiť.

## v0.18.0 – Settings Fashion Look
- moderný vizuálny systém WPF okien,
- ľavá navigácia alebo modernizované sekcie,
- lepšia typografia, spacing a DPI,
- malé vektorové ikony,
- ACI výber farieb 1–255,
- vizuálne náhľady anotácií,
- príprava svetlej/tmavej témy.

Tento vizuálny míľnik nie je súčasťou v0.17.0.

## 9. Režimy kótovania a popisov — DOKONČENÉ VO v0.16.0
- `FullLabel`: pôvodný automatický MText,
- `ItemNumberLeader`: natívny MLeader iba s položkou, napr. `K1`,
- `ItemNumberLeader` podporuje `Plain`, `Circle`, `Slot` a `Rectangle`,
- Circle/Slot/Rectangle používajú natívny Spline MLeader s BlockContent,
  atribútom `ITEM_NO`, insertion-point attachmentom, uhlom 40° a offsetom 350 mm,
- ručne posunutá rámčeková anotácia zachováva lokálny offset pri refreshi,
  AK_RENUMBER a zmenách source geometrie,
- `DimensionsLeader`: rovnaká natívna MLeader reprezentácia iba s prierezom `80x160`,
- default pre nové prvky a explicitné použitie na výber/všetky cez `AK_SETTINGS`,
- per-element schema v4 persistence, COPY/COPYCLIP/WBLOCK a live reconcile,
- footprint-aware Post placement a nezávislé slope/Post anotácie.

Budúce rozšírenie: voliteľný formát rozmerov `80/160`, reset manuálneho
rámčekového offsetu a mierka anotácií.

## 10. Vlastný používateľský prvok / Custom Element — DOKONČENÉ
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

## 11. AutoCAD 2021–2027 Compatibility Milestone
- build/package stratégia,
- API rozdiely,
- smoke test na podporovaných verziách,
- autoloader manifesty,
- rozšírenie Compatibility Gate.

## 12. BricsCAD Proof of Concept
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

## 13. Roof Domain Foundation
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

## 14. Automatické vytvorenie strechy zadaním bodov
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

## 15. Strešné roviny
- výber konkrétnej strešnej roviny,
- stabilný `RoofPlaneId`,
- sklon,
- použitie ako vstup pre automatické krokvy.

## 16. Automatické kreslenie krokiev
Tri režimy:

### A. Začiatok prvej krokvy + rozostup

### B. Symetricky podľa počtu krokiev

### C. Symetricky podľa rozostupu krokiev

Výsledné krokvy majú byť okamžite inteligentné prvky.

## 17. Nárožné a údolnicové krokvy
- automatické alebo asistované vytvorenie,
- skutočná dĺžka podľa strešných rovín,
- report a numbering.

## 18. True-width zobrazenie prvkov
- obdĺžnik/obrys okolo centerline podľa šírky prvku,
- oddeliť nosnú centerline geometriu od prezentačného obrysu.

## 19. Automatický trim / vizuálne prekrytie
- vizuálne orezanie prezentačných obrysov,
- hlavne pri prvkoch pod krokvami a pri krížení.

Implementovať až po stabilnom true-width systéme.

# FÁZA E – VÝKAZY A PRODUKČNÉ VÝSTUPY

## 20. XLSX export

## 21. PDF výrobný výkaz

## 22. Prepojenie reportu s výkresom
- riadok reportu → zvýrazniť prvky,
- prvok → nájsť reportovú položku.

## 23. Používateľské šablóny výstupov
- voliteľné stĺpce,
- poradie,
- firemné šablóny.

# FÁZA F – INTERNACIONALIZÁCIA A PRODUKTIZÁCIA

## 24. Material catalog — DOKONČENÉ
- šesť stabilných canonical material hodnôt,
- lokalizované display názvy pre SK/CS/EN/DE/PL/FR,
- výber cez `AK_EDIT` bez lokalizovaných textov v metadata alebo signatúre,
- neznáme legacy hodnoty sa zachovávajú bez migrácie,
- lokalizované `AK_INSPECT` a adaptívne dvojriadkové reporty.

## 25. Default jazyk pri prvom spustení
Odporúčanie:
1. zistiť Windows UI language,
2. ak je podporovaný, použiť ho,
3. inak fallback EN.

## 26. Default vrstvy pre nové inštalácie
- neutrálne alebo EN názvy,
- existujúce DWG nikdy automaticky nepremenovávať.

## 27. Jednotkové systémy
- Metric
- US
- GB

Interná kanonická geometria zostáva jednoznačná. Jazyk UI a jednotkový systém sú oddelené.

## 28. Medzinárodný názov produktu
Pracovný návrh:
`RoofCAD`

Pred rozhodnutím preveriť:
- ochranné známky,
- domény,
- existujúce produkty,
- Autodesk branding pravidlá.

Rebranding nesmie meniť historické metadata ani `AK_...` commands.

# FÁZA G – DISTRIBÚCIA A PODPORA

## 29. AutoCAD Autoloader `.bundle`
- odstrániť ručný NETLOAD,
- PackageContents,
- verzovanie,
- podporované AutoCAD verzie.

## 30. Inštalátor
- install,
- upgrade,
- uninstall,
- zachovanie settings.

## 31. Video návody priamo v programe
Požiadavka z PDF:
- video pre každú hlavnú funkciu,
- Help/Video action priamo v UI.

Realizovať až keď sa hlavné workflow prestanú výrazne meniť.

# FÁZA H – ĎALŠIE CAD PLATFORMY

## 32. BricsCAD plný adapter

## 33. ZWCAD Proof of Concept + adapter

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

1. Select Similar / filtre
2. CSV export
3. Diagnostika/logovanie
4. Annotation scale
5. Fonts/text styles
6. AutoCAD 2021–2027 compatibility checkpoint
7. BricsCAD PoC
8. Roof Domain Foundation
9. Automatic roof from points + roof types
10. Roof planes
11. Automatic rafters
12. Hip/valley rafters
13. True-width element outlines
17. Automatic visual trim
18. XLSX/PDF/report linking
19. Internationalization/productization
20. Autoloader + installer
21. Video tutorials
22. BricsCAD full adapter
23. ZWCAD adapter
