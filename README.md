# ACAD KROVY

ACAD KROVY je .NET doplnok pre AutoCAD na evidenciu, označovanie a výrobný výkaz drevených prvkov krovu v 2D výkrese. Inteligentné prvky nesú prenosné XData metadata, reagujú na zmeny geometrie a vytvárajú lokalizované labely, anotácie a reportové tabuľky.

Aktuálne číslo aplikácie je definované výhradne v [`Directory.Build.props`](Directory.Build.props). Startup hláška a `AK_HELP` ho čítajú z assembly metadata; `.bundle` manifest je povinný literal kontrolovaný Compatibility Gate.

## Aktuálne možnosti

- priradenie typov Krokva, Pomúrnica, Väznica, Stĺpik, Klieština, Vzpera a Väzný trám,
- individuálna aj bezpečná batch editácia rozmerov, materiálu, režimu dĺžky, sklonu a výrobného prídavku,
- stabilné položkové číslovanie, XData metadata a väzby cez `ElementId`/`SourceHandle`,
- centrálne výpočty skutočnej a reznej dĺžky, prídavkov, zaokrúhľovania a kubatúry,
- automatický refresh po MOVE, ROTATE, STRETCH, TRIM, EXTEND a grip edit,
- labely prvkov, collision-aware anotácia smeru sklonu a `AK_FLIPSLOPE`,
- report z výberu alebo celého výkresu s prirodzeným radením položiek,
- rectangular footprint pre Stĺpik z jednej rectangular Polyline,
- konverzia validného obdĺžnika zo štyroch samostatných LINE na jeden Post footprint,
- bezpečné správanie pri COPY, COPYCLIP/PASTECLIP, WBLOCK a SAVE/REOPEN,
- Ribbon aj klasický dokovateľný panel,
- runtime lokalizácia SK, CS, EN, DE, PL a FR bez zmeny technických DWG dát.

## Post / Stĺpik

Nový Stĺpik je reprezentovaný jednou uzavretou rectangular Polyline. Kliknutá strana určuje orientáciu `Width`, susedná strana `Height`; skutočná dĺžka pochádza z manuálnej dĺžky, nie z obvodu footprintu. Doplnok vytvára samostatný trojriadkový label a anotáciu `⊥ 90°`.

Alternatívny vstup zo štyroch LINE najprv overí jednoznačný uzavretý obdĺžnik. Až po úspešnom vytvorení a priradení jednej Polyline odstráni pôvodné čiary. Pri chybe vstupnú geometriu nemení.

Legacy line-based Post prvky zostávajú čitateľné a kompatibilné.

## Architektúra

```text
AcKrovy.Core              CAD-neutrálne modely, výpočty a geometrické pravidlá
AcKrovy.Cad.Abstractions  rozhrania medzi doménou a CAD adaptérom
AcKrovy.Localization      resources, jazyková služba a prezentačné názvy
AcKrovy.AutoCAD           AutoCAD API, XData, výber, kreslenie, WPF a príkazy
AcKrovy.Core.Tests        automatické regresné a architektonické testy
```

`AcKrovy.Core` cieli na `netstandard2.0` a nesmie závisieť od Autodesk API. AutoCAD 2027 je hlavná vývojová a manuálne testovaná platforma. Roadmap počíta so samostatnými adaptérmi pre AutoCAD 2021–2027, BricsCAD a neskôr ZWCAD; spoločná doména a technické metadata preto zostávajú CAD- aj jazykovo neutrálne.

## Výpočet dĺžky

Režim dĺžky určuje, či sa použije pôdorysná, sklonovo prepočítaná alebo manuálna dĺžka. Výrobná dĺžka sa vždy počíta v Core:

```text
RawCuttingLengthMm = ActualLengthMm + Max(0, CuttingAllowanceMm)
CuttingLengthMm    = RoundUp(RawCuttingLengthMm, configured step)
```

Predvolený krok je 100 mm. Defaultné prídavky podľa typu sú používateľské nastavenie; existujúce prvky si uchovávajú vlastnú uloženú hodnotu, kým používateľ výslovne neaplikuje nové defaulty.

## Príkazy

| Oblasť | Príkazy |
|---|---|
| Pomoc a UI | `AK_HELP`, `AK_RIBBON`, `AK_TOOLBAR`, `AK_TOOLBARSHOW`, `AK_TOOLBARHIDE` |
| Priradenie | `AK_ASSIGN`, `AK_KROKVA`, `AK_POMURNICA`, `AK_VAZNICA`, `AK_STLPIK`, `AK_KLIESTINA`, `AK_VZPERA`, `AK_VAZNYTRAM` |
| Údaje | `AK_EDIT`, `AK_INSPECT`, `AK_RECALC`, `AK_FLIPSLOPE` |
| Reporty | `AK_REPORT`, `AK_REPORTALL` |
| Labely | `AK_LABELS`, `AK_LABELSELECTED`, `AK_LABELSHOW`, `AK_LABELHIDE` |
| Nastavenia | `AK_SETTINGS`, `AK_APPLYLAYERS` |

Úplný lokalizovaný prehľad zobrazí `AK_HELP` priamo v AutoCADe.

## Požiadavky a build

- Windows x64,
- plný AutoCAD 2027 (AutoCAD LT nepodporuje vlastné .NET plug-iny),
- .NET 10 SDK,
- Visual Studio 2022 alebo `dotnet` CLI,
- AutoCAD .NET assemblies v štandardnom priečinku alebo cesta zadaná cez `AutoCadInstallDir`.

```powershell
dotnet restore AcKrovy.sln
dotnet build AcKrovy.sln --no-restore
dotnet test AcKrovy.sln --no-build
```

Pre inú inštalačnú cestu AutoCADu:

```powershell
dotnet build AcKrovy.sln -p:AutoCadInstallDir="D:\Autodesk\AutoCAD 2027"
```

Debug DLL pre `NETLOAD` vznikne v:

```text
src\AcKrovy.AutoCAD\bin\x64\Debug\net10.0-windows\AcKrovy.AutoCAD.dll
```

## Compatibility Gate

```powershell
.\scripts\compatibility-gate.ps1 -Portable
.\scripts\compatibility-gate.ps1 -Full
```

Portable režim overuje CAD-neutrálne projekty, testy, zakázané závislosti a konzistenciu verzie. Full režim navyše zostaví AutoCAD adaptér proti lokálnej inštalácii AutoCADu. Podrobnosti sú v [`docs/COMPATIBILITY_GATE.md`](docs/COMPATIBILITY_GATE.md).

## Dokumentácia projektu

- [`ACAD_KROVY_PROJECT_CONTEXT.md`](ACAD_KROVY_PROJECT_CONTEXT.md) – stabilné architektonické pravidlá a aktuálny kontext,
- [`ACAD_KROVY_ROADMAP.md`](ACAD_KROVY_ROADMAP.md) – odporúčané poradie ďalšieho vývoja,
- [`ACAD_KROVY_BACKLOG.md`](ACAD_KROVY_BACKLOG.md) – úplný zásobník otvorených nápadov,
- [`README_SK.txt`](README_SK.txt) – stručný slovenský quick-start pre používateľa.

Najbližšou plánovanou funkciou je `AK_RENUMBER`. Dokumentácia a centralizácia verzie sú už dokončený základ, nie otvorená feature položka.
