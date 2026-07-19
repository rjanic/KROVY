# ACAD KROVY – štartovací projekt

Základ pre AutoCAD doplnok na **2D výkaz krovu**. Čiara alebo polyline predstavuje jeden drevený prvok. K objektu sa do DWG uloží typ, prierez, sklon, prídavok na rezanie, materiál a označenie. Z vybraných alebo všetkých označených prvkov doplnok vytvorí výkaz s počtom, dĺžkami a kubatúrou.

> Stav: **0.9.0 – Live Geometry Synchronization**. Hlavná vývojová platforma je AutoCAD 2027 na Windows. Verzia obsahuje prenosné XData metadáta, automatické popisy, používateľské nastavenia hladín, nastaviteľné výrobné prídavky podľa typu, individuálne výrobné prídavky cez `AK_EDIT` a automatickú synchronizáciu po zmene geometrie.

## Čo už kostra obsahuje

- C# riešenie rozdelené na CAD-nezávislé výpočtové jadro, CAD abstrakcie a AutoCAD doplnok.
- Údaje uložené priamo pri čiare/polyline v prenosnom **XData** formáte; legacy `Xrecord` údaje sa stále vedia spätne načítať.
- Príkaz `AK_ASSIGN` na priradenie dát viacerým čiaram naraz.
- Príkaz `AK_EDIT` na hromadnú úpravu šírky, výšky, sklonu, individuálneho výrobného prídavku, materiálu a typu.
- Príkaz `AK_REPORT` na výkaz z aktuálneho výberu.
- Príkaz `AK_REPORTALL` na výkaz všetkých prvkov v modelovom priestore.
- Príkaz `AK_INSPECT` na vypísanie údajov a read-only informačné okno pri jednom prvku.
- Príkaz `AK_RECALC` na kontrolný prepočet všetkých prvkov.
- Príkaz `AK_SETTINGS` na používateľské nastavenia hladín, farieb a predvolených výrobných prídavkov.
- Automatické MText popisy prvkov s väzbou cez `SourceHandle`.
- Automatický refresh po STRETCH, TRIM, EXTEND, grip edit a MOVE bez nutnosti ručne spúšťať `AK_RECALC`.
- Výsledný AutoCAD `Table` so stĺpcami: typ, materiál, šírka, výška, dĺžka kusu, počet, celková dĺžka, kubatúra.

## Dôležité pravidlo výpočtu v 0.9.0

Pre krokvy a vzpery je čiara chápaná ako **vodorovná projekcia v smere spádu strechy**:

```text
skutočná dĺžka = pôdorysná dĺžka / cos(sklon)
```

Pre pomúrnice, väznice, klieštiny a väzné trámy sa používa pôdorysná dĺžka. Stĺpik zatiaľ používa ručne zadanú dĺžku, ak ju nastavíš; inak dĺžku čiary.

Rezná dĺžka sa počíta centrálne v Core:

```text
CuttingLengthMm = RoundUp(ActualLengthMm + Max(0, CuttingAllowanceMm), 100)
```

Výsledok sa vždy zaokrúhľuje nahor na 100 mm. Predvolené výrobné prídavky sú nastaviteľné podľa typu prvku v `AK_SETTINGS` a ukladajú sa pre aktuálny účet Windows do `%APPDATA%\ACAD_KROVY\timber-element-default-profile.json`.

Nárožné krokvy, úžľabia, rôzne strešné roviny a skutočné 3D smerové výpočty patria do modulu **Strešné roviny 2D/3D**, ktorý sa doplní neskôr. Tento základ ich architektúru už predpokladá cez pole `RoofPlaneId`.

## Predpoklady

- Windows 10 alebo 11, 64-bit.
- Plný **AutoCAD**, nie AutoCAD LT. AutoCAD LT nepodporuje vlastné .NET plug-iny.
- Visual Studio 2022 s pracovným zaťažením **.NET desktop development**.
- .NET SDK kompatibilné s riešením.
- AutoCAD 2027 nainštalovaný na počítači, kde sa bude doplnok kompilovať a skúšať.

AutoCAD API DLL sa berú z lokálnej inštalácie AutoCADu, preto nie sú vložené v projekte ani v repozitári.

## Otvorenie vo Visual Studiu

1. Otvor `AcKrovy.sln`.
2. Uprav cestu `AutoCadInstallDir` v `src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj` podľa nainštalovanej verzie:

```xml
<AutoCadInstallDir>C:\Program Files\Autodesk\AutoCAD 2027</AutoCadInstallDir>
```

3. Nastav konfiguráciu `Debug | x64`.
4. Zostav riešenie.
5. V AutoCADe spusti:

```text
NETLOAD
```

6. Vyber vytvorený súbor:

```text
src\AcKrovy.AutoCAD\bin\x64\Debug\net10.0-windows\AcKrovy.AutoCAD.dll
```

7. Zadaj príkaz:

```text
AK_HELP
```

## Prvé testovanie

1. Nakresli 3 až 5 čiar príkazom `LINE`.
2. Spusti `AK_ASSIGN` a označ ich oknom alebo kliknutím.
3. V dialógu ponechaj napríklad:
   - Typ: Krokva
   - Šírka: 80 mm
   - Výška: 160 mm
   - Sklon: 35°
   - Prídavok na rez: 100 mm
4. Označ tie isté čiary.
5. Spusti `AK_REPORT`.
6. Klikni do výkresu na miesto vloženia tabuľky.

## Príkazy

| Príkaz | Účel |
|---|---|
| `AK_HELP` | Zobrazí stručnú pomoc. |
| `AK_ASSIGN` | Priradí údaje jedným alebo viacerým čiaram/polyline naraz. |
| `AK_EDIT` | Hromadne zmení iba polia zaškrtnuté v dialógu. |
| `AK_INSPECT` | Vypíše údaje a prepočet vybraného prvku. |
| `AK_REPORT` | Vloží výkaz z aktuálne vybraných inteligentných prvkov. |
| `AK_REPORTALL` | Vloží výkaz zo všetkých inteligentných prvkov v modelovom priestore. |
| `AK_RECALC` | Overí a vypíše prepočet všetkých prvkov. |
| `AK_SETTINGS` | Nastaví hladiny, farby a predvolené výrobné prídavky podľa typu prvku. |

## v0.7.0 Manufacturing Length & Allowance Foundation

- Nastaviteľné predvolené výrobné prídavky podľa typu prvku.
- Rezná dĺžka zaokrúhlená vždy nahor na 100 mm.
- Tri režimy v `AK_SETTINGS`: aplikovať na všetky, aplikovať na výber, uložiť iba pre nové prvky.
- Bezpečné aplikovanie defaultov na existujúce vybrané alebo všetky inteligentné prvky.
- COPY/COPYCLIP kópia sa pri synchronizačnom flow inicializuje ako nový fyzický prvok.
- WBLOCK/import workflow je chránený pred nechceným hromadným prepisom prídavkov.
- Výpočet a aplikovanie výrobných prídavkov sú centralizované v Core bez AutoCAD závislostí.

## v0.8.0 Per-Element Manufacturing Overrides

- Individuálna editácia `CuttingAllowanceMm` cez `AK_EDIT`.
- Hromadná editácia s bezpečným mixed-value správaním.
- Možnosť obnoviť aktuálny predvolený prídavok podľa typu prvku.
- Bezpečný prepočet reznej dĺžky a synchronizácia `ElementId` cez existujúcu výrobnú identitu.
- Zachované správanie COPY/COPYCLIP z v0.7.0 a ochrana WBLOCK/import workflow.

## v0.9.0 Live Geometry Synchronization

- Automatická synchronizácia inteligentných timber prvkov po STRETCH, TRIM, EXTEND, grip edit a MOVE.
- Zmenené entity sa zbierajú počas AutoCAD príkazu a spracujú až po jeho úspešnom ukončení.
- Refresh používa existujúce Core meranie, výrobnú signatúru, synchronizáciu `ElementId` a `ElementLabelService`.
- Reentrancy guard bráni zacykleniu pri internom zápise metadát a aktualizácii labelov.
- Multi-document lifecycle je riešený per-dokumentovými subscription bez zdieľania kandidátov medzi DWG.
- Stabilné číslovanie výrobných položiek nekompaktuje existujúce `ElementId`; nové položky môžu použiť prvú voľnú medzeru.
- Automatické popisy sa aktualizujú okamžite, čistia orphan/clone labely a rozmery zapisujú vo formáte `80x160`.
- `AK_INSPECT` zobrazuje kompaktné read-only informačné okno.
- COPY/COPYCLIP a WBLOCK/import kompatibilita z v0.7.0 zostáva zachovaná.

## Štruktúra

```text
AcKrovyStarter/
├── AcKrovy.sln
├── src/
│   ├── AcKrovy.Core/              # výpočet, dátové modely, výkaz
│   └── AcKrovy.AutoCAD/           # príkazy, AutoCAD Xrecord, WPF dialóg
├── deploy/
│   └── AcKrovy.bundle/            # základ pre budúce automatické načítanie
└── docs/
    ├── ROADMAP.md
    └── TEST_SCENARIO_001.md
```

## Ďalšie plánované kroky

1. Ribbon karta **ACAD KROVY** s ikonami jednotlivých prvkov.
2. Zobrazenie menoviek `K1`, `K2`, `P1` vo výkrese.
3. Strešné roviny, hrebeň, smer spádu a automatický výpočet nárožných krokiev.
4. Export Excel/CSV/PDF.
5. Prednastavenia bežných prierezov a materiálov.
6. Výkaz reziva podľa obchodných dĺžok a optimalizácia odpadu.
7. Licencovanie, inštalátor a aktualizácie.
