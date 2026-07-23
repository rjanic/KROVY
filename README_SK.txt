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
- AK_SETTINGS nastaví jazyk, hladiny, farby a výrobné defaulty.
- AK_LABELS obnoví automatické popisy.

ČÍSLOVANIE
Bežné automatické číslovanie je stabilné a zachováva medzery. Iba explicitný
AK_RENUMBER vytvorí v každom type súvislé poradie od 1 podľa CuttingLengthMm.
Geometria, SourceHandle a ostatné výrobné údaje zostávajú bez zmeny.

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

OVERENIE
.\scripts\compatibility-gate.ps1 -Portable
.\scripts\compatibility-gate.ps1 -Full

Tento TXT súbor zostáva iba ako jednoduchý vstupný bod pre existujúce
používateľské balíky. Pri budúcom zavedení generovaného inštalačného návodu
ho možno odstrániť; dovtedy nemá duplikovať release notes ani celý README.
