# ACAD KROVY – ROADMAP

**Aktualizované:** 14. 8. 2026
**Predchádzajúci stabilný commit v0.18.0:** `46ad0cfe555f9f3177de2d47d13bdda33d9a91a0`
**Predchádzajúci stabilný commit v0.19.0:** `41373d235a357dee05033872a1df8fed8b3286d3`
**Predchádzajúci stabilný commit v0.20.0:** `6564fc930d98e3eca591bafcccef709af07dc9a5`
**Predchádzajúci stabilný commit v0.21.0:** `f98900c1bd257a8e5357f6e77eb6f118bd4930d3`
**Aktuálny míľnik:** post-Stage-5 „Roof Validation WPF Notifications Polish“, implementovaný lokálne; manuálny AutoCAD HOST W1–W4 checkpoint je otvorený

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

## 7. Per-element mierka anotácií — ROZŠÍRENÉ VO v0.22.0
- priorita platný Element override > Drawing scale > fixný fallback 1:50,
- drawing persistence `ACAD_KROVY / DRAWING_SETTINGS` schema 1,
- metadata schema 5 s nullable per-element override a read-only kompatibilitou schema 4,
- `AK_SETTINGS` Save New / Apply Selection / Apply All pre celú Annotation sekciu,
- predvoľby 1:25, 1:50, 1:75, 1:100 a custom 5–250,
- živý Core-based typography/BlockScale preview a odstránenie override,
- Apply All nastaví drawing scale, odstráni override a vykoná najviac jeden batch,
- FullLabel, leadery, framed/combined, Post footprint, slope a 0°/90° symboly,
- immutable context, scale presne raz, Core bez Autodesk a bez native
  annotative contexts,
- 440 lokalizačných kľúčov v každom zo 6 jazykov.

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

## 14. Roof Domain Foundation – IMPLEMENTOVANÉ V S1
- `AK_ROOF` vyberá closed lightweight Polyline a nemení DWG,
- AutoCAD extractor mapuje closed state, vrcholy, bulge a XY planarity flag do
  `RoofFootprintInput`; žiadny Autodesk typ nevstupuje do Core,
- Core obsahuje body/smer, kanonický obrys, indexované hrany, bounds, centroid,
  plochu, deterministický signature, budúci parameter boundary a roof definition,
- normalizácia: bez opakovaného closing bodu, CCW, lexikograficky najmenší prvý
  vrchol; zdrojová CW/CCW orientácia sa zachytí vo validačnom výsledku,
- `EffectiveClosed` zachováva presnú toleranciu `1e-9 mm`: prijíma natívne closed
  alebo otvorené PLINE so zhodným prvým/posledným bodom, bez opravy zdroja,
- validácia pokrýva closure, minimálny počet unikátnych bodov, konečné
  súradnice, duplicitné/krátke hrany, samopriesek, plochu, kolinearitu, krivky
  a nepodporovanú rovinu,
- výsledok sa zobrazí lokalizovane v SK/CS/EN/DE/PL/FR a prijatý zdroj zostane
  implied-selected ako nedeštruktívny preview,
- iba `OpenLoop` používa centrované Fashion WPF upozornenie s približne 2500 ms
  auto-close, bez mouse-dismiss a s Esc aktivovaným až po `ApplicationIdle`;
  následne pokračuje pôvodný selection retry loop,
- bez XData/Xrecord persistence, settings dialógu, strešných rovín, členov,
  anotácií alebo zmeny timber metadata schema,
- AutoCAD 2027 host test potvrdil CW/CCW kanonizáciu, Endpoint+Enter
  `EffectiveClosed` vstup a DBMOD 5 → 5 po opakovaných `OpenLoop` výberoch.

## 15. S2 – jednoduchá sedlová strecha MVP – STAGE 1 IMPLEMENTOVANÝ
### Stage 1 – CAD-neutrálna geometria
- `RoofDefinition` + existujúci sklon/smer → `SimpleGableRoofGeometry`,
- presná podpora štvorvrcholového obdĺžnika v jeho vlastnej otočenej XY báze,
- samostatná rectangle validácia bez opravy vstupu a centralizované distance,
  relative-length, angular, dimension a slope tolerancie,
- explicitný smer sa musí zhodovať s jednou osou obdĺžnika; `+d`/`-d`, CW/CCW
  a zdrojový prvý vrchol nemenia kanonický ridge ani dve bounded 3D faces,
- centered ridge medzi odkvapmi, `run = width / 2`, `rise = run * tan(slope)`,
  lokálny eave datum `Z = 0`, ridge `Z = rise`, bez zaokrúhľovania,
- bez AutoCAD entity/preview, persistence, XData/Xrecord, UI a timber generation.

### Stage 2 – AutoCAD transient preview – IMPLEMENTOVANÝ
- `AK_ROOF` získava explicitný sklon a dvojbodový WCS smer a používa Stage 1 solver,
- dočasný wireframe obsahuje jeden ridge, dva eaves a štyri gable-slope edges;
  local eave `Z = 0` sa mapuje na WCS eleváciu zdrojovej Polyline,
- `TransientManager` drawables nie sú DB entities; scoped session zabezpečuje
  erase/dispose pri Enter, Esc/cancel, výnimke a document destruction,
- bez write transaction, ModelSpace/PaperSpace appendu, vrstiev, XData/Xrecord,
  persistence, permanentných roof entities a timber generation,
- OpenLoop Fashion WPF retry zostáva bez zmeny; ostatné Stage 2 chyby sú CLI-only,
- manuálny AutoCAD 2027 visual/DBMOD preview test zostáva otvorený.

### Stage 3 – persistent roof definition – IMPLEMENTOVANÝ
- source footprint Polyline vlastní dedikované `DECORAIR_ACADKROVY_ROOF` XData,
- roof schema `1` perzistuje iba `SimpleGable`, slope, kanonický ridge direction
  a Stage 1 footprint signature; geometriu po načítaní vždy znovu rieši Core solver,
- čítanie je bez RegApp registrácie a DB write; zápis nastane iba po explicitnom
  lokalizovanom Yes a v krátkom atomic transaction/DocumentLock scope,
- writer nahrádza iba vlastnú roof sekciu a zachováva cudzie/timber XData,
- malformed, future, unsupported a stale dáta sa neopravia ani neprepíšu,
- bez permanentnej roof geometrie a timber generation; SAVE/CLOSE/REOPEN host proof
  a MOVE/ROTATE/STRETCH/COPY/WBLOCK lifecycle zostávajú otvorené.

### Stage 4 – rigid-transform roof lifecycle foundation – IMPLEMENTOVANÝ LOKÁLNE
- nové roof schema `2` ukladá sklon, natívnu source edge-family `0→1`/`1→2`
  a descriptor `4 vertices + CW/CCW + adjacent edge lengths`, nie WCS umiestnenie,
- read-only lazy restore z aktuálnej source topológie podporuje MOVE, ROTATE a ich
  opakovanie bez reactorov alebo metadata rewrite; štvorce zachovávajú zvolenú os,
- STRETCH a zmena rozmerov ostávajú stale; schema `1` sa číta bez migrácie a po
  MOVE/ROTATE zachováva pôvodné Stage 3 stale správanie,
- COPY má neutrálnu owner-local podporu, ale bude označená za produkčne podporenú
  až po reálnom HOST potvrdení kopírovania `DECORAIR_ACADKROVY_ROOF` XData,
- bez permanentnej roof geometrie a timber generation; HOST lifecycle/DBMOD
  acceptance je otvorená a celý S2 týmto nie je dokončený.

### Stage 5 – permanent simple-gable roof display – PUBLIKOVANÝ
- solver geometry sa mapuje na presne sedem natívnych `Line` cez neutrálny stabilný
  role kontrakt, bez druhej strešnej matematiky v AutoCAD adaptéri,
- `KROV_STRECHA` + `DECORAIR_ACADKROVY_ROOF_DISPLAY` schema `1` tvoria oddelenú,
  odstrániteľnú a regenerovateľnú display cache vlastnenú source Polyline handle,
- nový roof zapisuje definition + display jedným potvrdeným transaction commitom;
  existujúci missing/stale display sa obnovuje iba po Yes, current/No/Esc sú read-only,
- bez reactorov a timber generation; roof schema `2`, timber schema `7`, drawing schema `1`
  a package version zostávajú nezmenené; S2 nie je dokončené,
- manuálny AutoCAD 2027 visual/lifecycle/DBMOD/Undo HOST checkpoint prešiel.
- lokálne UX rozšírenie pridáva opraviteľnú 8-member GROUP `AK_ROOF_<handle>`,
  výber ownera cez každé tagged display dieťa a samostatnú červenú ridge vrstvu;
  GROUP nie je semantic truth a PICKSTYLE sa nemení,
- spoločný rigidný MOVE/ROTATE ownera a displaya sa validuje podľa skutočných hrán,
  takže geometricky current cache nevyžaduje rebuild iba pre starú generation signature.
- source-only COPY môže vytvoriť vlastný display/group; lokálny full 8-member GROUP
  deep-clone fix persistuje display owner aj ako AutoCAD-remapovaný XData `1005`
  soft-pointer a pre staršie schema-1 display dáta používa prísny read-only fallback
  podľa kompletnej group topológie. Finálny HOST test 24 original + 24 copied = 48
  je PASS; skorších 72 vzniklo používateľským COPY Multiple s dvoma kópiami,
- post-Stage-5 polish používa existujúce transient WPF upozornenie iba pre blokujúce
  selection/footprint validácie; normálne prompty, stale semantic/display workflow a
  technické zlyhania zostávajú CLI, bez DB write alebo input bleed.

### Stage 6 – automatic SimpleGable rafters – DOKONČENÝ A HOST OVERENÝ
- nový explicitný `AK_ROOF_RAFTERS` vytvára iba intelligent `Krokva` source Lines,
  bez automatických labels a bez pridania do 8-member roof GROUP,
- kompaktný Fashion WPF dialóg načíta posledné úspešné Width/Height/Smax/Material z
  existujúceho per-user UI store (first-use 80/160/900/Smrek C24); slope je vždy
  read-only hodnota aktuálne vybranej persisted strechy a nevstupuje do preferences,
- neutral layout používa zadanú pôdorysnú šírku `B=Krokva.WidthMm`; center span
  `U=L-B` sa rozdelí na `max(1,ceil(U/Smax))` rovnakých intervalov, takže krajné
  centerlines ležia `B/2` od štítových rovín a vonkajšie plochy krokiev lícujú štíty,
- oba roof faces používajú eave→ridge source direction, uložený roof slope a canonical
  `IsSlopeDirectionReversed=true`, aby bežná KROVY šípka smerovala downhill
  ridge→eave pred aj po MOVE/ROTATE refresh; renderer ani refresh nemá roof vetvu,
  rotated
  rectangle aj square retained ridge-family ostávajú bez world-axis predpokladov,
- štandardné timber metadata zostáva schema `7`; samostatné generated ownership XData
  `DECORAIR_ACADKROVY_ROOF_TIMBER` používa schema `1`,
- Ribbon má native `Strecha` dropdown (Sedlová enabled → `AK_ROOF`, Valbová,
  Polvalbová a Pultová visible disabled) a samostatné `Krokvy` → `AK_ROOF_RAFTERS`;
  16/32 PNG assets používajú existujúci loader a technický roof/PDF vizuálny jazyk,
- dialóg live summary je zámerne bez modal-live transient preview; Cancel je read-only
  a WPF Vytvoriť je jediné potvrdenie. Existujúci generated set sa
  bezpečne deteguje, ale replacement je odložený, kým nebude jednotná cleanup služba
  pre source aj neskôr pridané annotations,
- bez reactorov, live synchronizácie, generated-rafter COPY ownership remap, iných
  timber typov alebo Stage 7; display GROUP COPY ownership patrí do Stage 5 kontraktu.
- HOST R1–R10, downhill MOVE/ROTATE, Ribbon/WPF localization, Width 100 → 50 mm
  edge offset, full 8-member GROUP COPY independent generation a AK_ROOF successful
  selection clear sú PASS.

### Ďalšie S2 stages
- nasledujúci samostatný checkpoint je SimpleGable Roof STRETCH / Resize Lifecycle;
  podporovaný obdĺžnik má zachovať roof type/slope/ridge family a prepočítať display,
- potom doplniť explicitný stale/regeneration lifecycle pre generated rafters,
- až v samostatných etapách vytvoriť pomúrnice, hrebeňovú väznicu a ďalšie timber typy,
- definovať stabilnú DWG persistence a `RoofPlaneId` až spolu s reálnym S2
  source/member lifecycle, bez zmeny timber schema, ak to kontrakt nevyžaduje,
- pridať item numbering, anotácie a report/BOM integráciu iba v rozsahu
  potrebnom pre produkčný simple-gable workflow.

## 16. Neskoršie typy strechy a vstup bodmi
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
- jeden budúci `HipRoof` solver musí pokrývať obdĺžnikový prípad s kladnou dĺžkou
  hrebeňa aj štvorcový stanový/pyramídový prípad. Ak sa pozdĺžny a priečny rozmer
  footprintu rovnajú v rámci geometrickej tolerancie, hrebeň sa deterministicky
  zrúti na jeden vrchol (`ridgeLength -> 0`),
- Stanová/Pyramídová preto nebude samostatný sémantický roof type, Ribbon položka,
  command, ikona ani persistence schema; v menu zostáva iba Valbová,
- ide o budúcu solver architektúru, nie implementáciu v Stage 6.

### Polovalbová
- výber štítu,
- zadanie dĺžky polvalby.

UI:
ikonky podobné referencii v PDF.

## 17. Rozšírené strešné roviny
- výber konkrétnej strešnej roviny,
- stabilný `RoofPlaneId`,
- sklon,
- použitie ako vstup pre automatické krokvy.

## 18. Rozšírené automatické kreslenie krokiev
Tri režimy:

### A. Začiatok prvej krokvy + rozostup

### B. Symetricky podľa počtu krokiev

### C. Symetricky podľa rozostupu krokiev

Výsledné krokvy majú byť okamžite inteligentné prvky.

## 19. Nárožné a údolnicové krokvy
- automatické alebo asistované vytvorenie,
- skutočná dĺžka podľa strešných rovín,
- report a numbering.

## 20. True-width zobrazenie prvkov
- obdĺžnik/obrys okolo centerline podľa šírky prvku,
- oddeliť nosnú centerline geometriu od prezentačného obrysu.

## 21. Automatický trim / vizuálne prekrytie
- vizuálne orezanie prezentačných obrysov,
- hlavne pri prvkoch pod krokvami a pri krížení.

Implementovať až po stabilnom true-width systéme.

# FÁZA E – VÝKAZY A PRODUKČNÉ VÝSTUPY

## 22. XLSX export

## 23. PDF výrobný výkaz

## 24. Prepojenie reportu s výkresom
- riadok reportu → zvýrazniť prvky,
- prvok → nájsť reportovú položku.

## 25. Používateľské šablóny výstupov
- voliteľné stĺpce,
- poradie,
- firemné šablóny.

# FÁZA F – INTERNACIONALIZÁCIA A PRODUKTIZÁCIA

## 26. Material catalog — DOKONČENÉ
- šesť stabilných canonical material hodnôt,
- lokalizované display názvy pre SK/CS/EN/DE/PL/FR,
- výber cez `AK_EDIT` bez lokalizovaných textov v metadata alebo signatúre,
- neznáme legacy hodnoty sa zachovávajú bez migrácie,
- lokalizované `AK_INSPECT` a adaptívne dvojriadkové reporty.

## 27. Default jazyk pri prvom spustení — DOKONČENÉ VO v0.20.0
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

## 28. Default vrstvy pre nové inštalácie
- neutrálne alebo EN názvy,
- existujúce DWG nikdy automaticky nepremenovávať.

## 29. Jednotkové systémy
- Metric
- US
- GB

Interná kanonická geometria zostáva jednoznačná. Jazyk UI a jednotkový systém sú oddelené.

## 30. Medzinárodný názov produktu
Pracovný návrh:
`RoofCAD`

Pred rozhodnutím preveriť:
- ochranné známky,
- domény,
- existujúce produkty,
- Autodesk branding pravidlá.

Rebranding nesmie meniť historické metadata ani `AK_...` commands.

# FÁZA G – DISTRIBÚCIA A PODPORA

## 31. AutoCAD Autoloader `.bundle`
- odstrániť ručný NETLOAD,
- PackageContents,
- verzovanie,
- podporované AutoCAD verzie.

## 32. Inštalátor
- install,
- upgrade,
- uninstall,
- zachovanie settings.

## 33. Video návody priamo v programe
Požiadavka z PDF:
- video pre každú hlavnú funkciu,
- Help/Video action priamo v UI.

Realizovať až keď sa hlavné workflow prestanú výrazne meniť.

# FÁZA H – ĎALŠIE CAD PLATFORMY

## 34. BricsCAD plný adapter

## 35. ZWCAD Proof of Concept + adapter

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

1. Manuálny AutoCAD 2027 Stage 4 lifecycle/DBMOD test: new v2, MOVE, ROTATE,
   opakované transformácie, square 90°, STRETCH stale, COPY a OpenLoop regresia.
2. Po HOST acceptance pokračovať roof editingom a ďalšími simple-gable členmi;
   SCALE/MIRROR/REVERSE/WBLOCK zostávajú mimo Stage 4 kontraktu.
3. AutoCAD 2021–2027 compatibility checkpoint a BricsCAD PoC.
4. Komplexné footprints, hip/valley a ďalšie typy strechy.
5. True-width a automatický vizuálny trim.
6. XLSX/PDF/report linking.
7. Internacionalizácia, distribúcia a ďalšie CAD adaptéry.
