ACAD KROVY – STRUČNÝ SLOVENSKÝ QUICK-START

Aktuálny a úplný popis projektu je v README.md.
Architektúra, roadmap a backlog sú v:
- ACAD_KROVY_PROJECT_CONTEXT.md
- ACAD_KROVY_ROADMAP.md
- ACAD_KROVY_BACKLOG.md

SPUSTENIE V AUTOCAD 2027
1. Zostav riešenie pre Debug | x64.
2. V AutoCADe spusti NETLOAD.
3. Vyber:
   src\AcKrovy.AutoCAD\bin\x64\Debug\net10.0-windows\AcKrovy.AutoCAD.dll
4. Zadaj AK_HELP pre aktuálny lokalizovaný zoznam príkazov.

ZÁKLADNÝ WORKFLOW
- AK_ASSIGN alebo rýchly príkaz typu priradí inteligentné údaje prvku.
- AK_CUSTOM vytvorí alebo znovu použije vlastný lineárny typ s názvom a prefixom.
- AK_EDIT upraví jeden alebo viac prvkov.
- AK_INSPECT zobrazí technické údaje jedného prvku.
- AK_RENUMBER po potvrdení vedome prečísluje všetky výrobné položky podľa
  reznej dĺžky od najkratšej po najdlhšiu.
- AK_REPORT / AK_REPORTALL vloží výrobný výkaz.
- AK_SELECTSIMILAR vyberie podobné inteligentné prvky bez zmeny DWG.
- AK_EXPORTCSV uloží jednotlivý alebo súhrnný výrobný CSV výkaz.
- AK_DIAGNOSTICS zobrazí technický stav, logy a lokálne settings.
- AK_SETTINGS nastaví jazyk, hladiny, farby, typy čiar, výrobné defaulty a režim anotácie.
- AK_LABELS obnoví automatické popisy.

SETTINGS FASHION LOOK v0.18.0
AK_SETTINGS používa modernú ľavú navigáciu, svetlú/tmavú tému, vektorové ikony,
karty a sticky spodnú lištu. Predvolený rozmer je 1500 × 900, minimum
1250 × 720. Okno je resizable, pamätá si lokálne tému, sekciu,
veľkosť, polohu a maximalizovaný stav a zalamuje dlhé lokalizované texty.
Tieto UI údaje nie sú súčasťou DWG ani layer profilu.

Farba vrstvy sa vyberá z celej ACI palety 1–255. Picker podporuje myš,
klávesnicu, priamy index, Enter a bezpečný Esc. Ukladá sa iba ACI index;
RGB slúži len na náhľad. Anotačné režimy a rámčekové štýly majú vizuálne
karty, ale naďalej používajú pôvodné stabilné enumy. Téma, navigácia, preview
a zrušený picker nemenia DBMOD ani nespúšťajú Apply. Automatické sledovanie
AutoCAD témy je odložené, pretože nebol potvrdený stabilný verejný API kontrakt.
Popisy používajú presne 10 PNG presetov v mriežke 5 × 2 vrátane Bez popisov,
samostatných framed leaderov a kombinovaných framed item + dimensions režimov.
Sekčné footery zachovávajú samostatné Apply workflowy. Výber existujúcej
hladiny iba načíta jej vlastnosti; suffix sa rieši idempotentne až pri Apply.
Pravidlá pre AI vývojových agentov sú v AGENTS.md a v šiestich súboroch .ai/.

ČÍSLOVANIE
Bežné automatické číslovanie je stabilné a zachováva medzery. Iba explicitný
AK_RENUMBER vytvorí v každom type súvislé poradie od 1 podľa CuttingLengthMm.
Geometria, SourceHandle a ostatné výrobné údaje zostávajú bez zmeny.

VRSTVY A TYPY ČIAR
Každý timber typ a spoločný profil KROV_CUSTOM má vlastný názov vrstvy, farbu
a typ čiary. Krokva má default DASHDOT a entity scale 0,5, ostatné typy vrátane
Custom používajú Continuous a scale 1,0. Timber entity majú farbu aj typ čiary
ByLayer. Iba nové prvky nikdy neprepíšu existujúcu konfliktnú vrstvu; použijú
jej aktuálny vzhľad a zobrazí sa upozornenie. Odlišný vzhľad iba nových prvkov
vyžaduje nový názov vrstvy. Existujúce prvky sa zmenia iba cez explicitné Apply
na výber alebo celý výkres; tým sa môžu zmeniť aj iné CAD entity na rovnakej
vrstve. AK_SETTINGS zostáva po Použiť otvorené a režimy výber/všetky možno
opakovať bez zmeny formulára; zapisujú iba skutočné rozdiely. Samotné otvorenie
AK_SETTINGS nemení DWG. Chýbajúci DASHDOT sa načíta z metrického acadiso.lin
cez AutoCAD support paths, pri chybe sa použije Continuous. Definícia zostáva
uložená v DWG. Globálne scale premenné sa nemenia a annotation vrstvy zostávajú nezávislé.
Runtime zmena jazyka obnoví dynamické zoznamy bez straty rozpracovaných hodnôt.
Výsledok Použiť sa zobrazí vo veľkom neblokujúcom 2-sekundovom banneri.

MATERIÁLY A REPORTY
AK_EDIT ponúka lokalizovaný katalóg šiestich materiálov, ale do DWG vždy
ukladá stabilnú canonical hodnotu. Neznámy materiál zo starého DWG sa zachová.
AK_REPORT a AK_REPORTALL zobrazia katalógový materiál v dvoch riadkoch a
šírku stĺpcov Typ/Materiál prispôsobia iba skutočnému obsahu daného reportu.

POST / STĹPIK
Nový Stĺpik používa jednu rectangular Polyline. AK_STLPIK vie spracovať aj
validný obdĺžnik zo štyroch samostatných LINE a bezpečne ho skonvertuje na
jednu uzavretú Polyline. Manuálna dĺžka je nezávislá od obvodu footprintu.

VLASTNÝ PRVOK
AK_CUSTOM pracuje s LINE/LWPOLYLINE. Definícia má stabilné technické ID,
používateľský názov a prefix (napr. Konzola / KO). Každý prvok nesie kompletnú
definíciu vo svojich XData, takže COPY, COPYCLIP a WBLOCK nie sú závislé od
lokálneho katalógu. Používateľský názov sa pri zmene jazyka neprekladá a každá
definícia má vlastnú numbering sériu. V AK_EDIT možno názov definície výslovne
zmeniť pre všetky jej prvky v aktuálnom DWG bez zmeny ID, prefixu a položiek.
Automatický label obsahuje iba položku, prierez a výrobnú dĺžku. Custom prvok
je v automatickom režime slope-aware rovnako ako Krokva.

POPISY A KÓTOVANIE
AK_SETTINGS ponúka FullLabel, ItemNumberLeader, DimensionsLeader,
NoAnnotations a DimensionsWithItemNumber.
ItemNumberLeader má varianty Plain, Circle, Slot a Rectangle. Plain a
DimensionsLeader používajú priamy natívny MLeader; rámčekové varianty používajú
Spline MLeader s prenosným BlockContent a atribútom ITEM_NO. Prvý leader
segment standalone aj kombinovaných rámčekových režimov má 60°. Kombinované
Circle/Rectangle/Slot majú LandingDistance 350 mm, horizontálny landing a
rozmerový MText v strede landing čiary. Ručne posunutý
rámček zostane po STRETCH, refreshi aj AK_RENUMBER na zvolenom mieste.
Default platí pre nové prvky; na existujúce prvky sa použije iba výslovne pre
výber alebo celý výkres. Režim aj štýl sú uložené v každom prvku, preto ich
zachovajú COPY, COPYCLIP, WBLOCK aj SAVE/REOPEN. Staré prvky bez uloženého
režimu používajú FullLabel; chýbajúci štýl čísla znamená Plain.
Slope anotácie a samostatné označenie Stĺpika ⊥ 90° zostávajú nezávislé.

MIERKA ANOTÁCIÍ v0.21.0
AK_SETTINGS nastavuje samostatne mierku aktuálneho DWG a predvolenú mierku
nových výkresov. Priorita je Drawing > UserDefault > fallback 1:50.
Predvoľby sú 1:25, 1:50, 1:75, 1:100 a vlastný menovateľ 10–200. Náhľad
zobrazuje výšku textov a BlockScale. Drawing override možno bezpečne odstrániť;
reálna zmena mierky automaticky obnoví existujúce anotácie bez duplicít.

Pri 1:50 má dimension/FullLabel text 125 mm, item-number text 135 mm, slope
text 80 mm, slope offset 100 mm a Circle priemer 400 mm. Škálovanie pokrýva
hlavné, leader, framed, combined, Post footprint a slope anotácie vrátane
symbolov 0° a 90°.

PRODUCTIVITY & RELIABILITY v0.19.0
AK_SELECTSIMILAR porovnáva typ, neotáčaný prierez, canonical materiál a podľa
voľby položku, výrobnú dĺžku s toleranciou alebo Custom ID. Skenuje iba model
space read-only a výsledok nastaví ako implied selection.

AK_EXPORTCSV podporuje PickFirst, ručný výber alebo celý model space a režimy
Individual/Summarized. CSV používa UTF-8 BOM, bodkočiarku, CRLF, lokalizované
hlavičky s jednotkami a desatinný formát aktívneho jazyka ACAD KROVY.

AK_DIAGNOSTICS používa Light/Dark Fashion Look a všetkých šesť jazykov.
Logy sú v %LOCALAPPDATA%\ACAD_KROVY\Logs, max. 5 MB na súbor a 14 dní.
Poškodené lokálne JSON nastavenie sa zálohuje ako
<name>.corrupt.<yyyyMMdd-HHmmss>.json. Ak záloha zlyhá, originál sa neprepíše
a bezpečné defaulty sa použijú iba v pamäti.

OVERENIE
.\scripts\compatibility-gate.ps1 -Portable
.\scripts\compatibility-gate.ps1 -Full

Tento TXT súbor zostáva iba ako jednoduchý vstupný bod pre existujúce
používateľské balíky. Pri budúcom zavedení generovaného inštalačného návodu
ho možno odstrániť; dovtedy nemá duplikovať release notes ani celý README.
