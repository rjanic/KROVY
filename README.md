# ACAD KROVY – štartovací projekt

Základ pre AutoCAD doplnok na **2D výkaz krovu**. Čiara alebo polyline predstavuje jeden drevený prvok. K objektu sa do DWG uloží typ, prierez, sklon, prídavok na rezanie, materiál a označenie. Z vybraných alebo všetkých označených prvkov doplnok vytvorí výkaz s počtom, dĺžkami a kubatúrou.

> Stav: **0.1.0 – vývojová kostra**. Pripravené pre AutoCAD 2025 alebo 2026 na Windows. Neobsahuje statické posúdenie a v tejto fáze podporuje len základný prepočet dĺžky podľa jedného zadaného sklonu.

## Čo už kostra obsahuje

- C# / .NET 8 riešenie rozdelené na výpočtové jadro a AutoCAD doplnok.
- Údaje uložené priamo pri čiare/polyline v jej **Extension Dictionary** ako `Xrecord`.
- Príkaz `AK_ASSIGN` na priradenie dát viacerým čiaram naraz.
- Príkaz `AK_EDIT` na hromadnú úpravu šírky, výšky, sklonu, prídavku, materiálu a typu.
- Príkaz `AK_REPORT` na výkaz z aktuálneho výberu.
- Príkaz `AK_REPORTALL` na výkaz všetkých prvkov v modelovom priestore.
- Príkaz `AK_INSPECT` na vypísanie údajov pri jednom prvku.
- Príkaz `AK_RECALC` na kontrolný prepočet všetkých prvkov.
- Výsledný AutoCAD `Table` so stĺpcami: typ, materiál, šírka, výška, dĺžka kusu, počet, celková dĺžka, kubatúra.

## Dôležité pravidlo výpočtu v 0.1.0

Pre krokvy a vzpery je čiara chápaná ako **vodorovná projekcia v smere spádu strechy**:

```text
skutočná dĺžka = pôdorysná dĺžka / cos(sklon)
```

Pre pomúrnice, väznice, klieštiny a väzné trámy sa používa pôdorysná dĺžka. Stĺpik zatiaľ používa ručne zadanú dĺžku, ak ju nastavíš; inak dĺžku čiary.

Nárožné krokvy, úžľabia, rôzne strešné roviny a skutočné 3D smerové výpočty patria do modulu **Strešné roviny 2D/3D**, ktorý sa doplní neskôr. Tento základ ich architektúru už predpokladá cez pole `RoofPlaneId`.

## Predpoklady

- Windows 10 alebo 11, 64-bit.
- Plný **AutoCAD**, nie AutoCAD LT. AutoCAD LT nepodporuje vlastné .NET plug-iny.
- Visual Studio 2022 s pracovným zaťažením **.NET desktop development**.
- .NET SDK 8.
- AutoCAD 2025 alebo 2026 nainštalovaný na počítači, kde sa bude doplnok kompilovať a skúšať.

AutoCAD 2025 aj 2026 podporujú Managed .NET API na .NET 8. API DLL sa berú z lokálnej inštalácie AutoCADu, preto nie sú vložené v projekte ani v repozitári.

## Otvorenie vo Visual Studiu

1. Otvor `AcKrovy.sln`.
2. Uprav cestu `AutoCadInstallDir` v `src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj` podľa nainštalovanej verzie:

```xml
<AutoCadInstallDir>C:\Program Files\Autodesk\AutoCAD 2026</AutoCadInstallDir>
```

3. Nastav konfiguráciu `Debug | x64`.
4. Zostav riešenie.
5. V AutoCADe spusti:

```text
NETLOAD
```

6. Vyber vytvorený súbor:

```text
src\AcKrovy.AutoCAD\bin\x64\Debug\net8.0-windows\AcKrovy.AutoCAD.dll
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
