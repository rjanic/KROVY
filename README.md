# ACAD KROVY

ACAD KROVY je .NET doplnok pre AutoCAD na evidenciu, označovanie a výrobný výkaz drevených prvkov krovu v 2D výkrese. Inteligentné prvky nesú prenosné XData metadata, reagujú na zmeny geometrie a vytvárajú lokalizované labely, anotácie a reportové tabuľky.

Aktuálne číslo aplikácie je definované výhradne v [`Directory.Build.props`](Directory.Build.props). Startup hláška a `AK_HELP` ho čítajú z assembly metadata; `.bundle` manifest je povinný literal kontrolovaný Compatibility Gate.

## Aktuálne možnosti

- priradenie typov Krokva, Pomúrnica, Väznica, Stĺpik, Klieština, Vzpera a Väzný trám,
- opakovane použiteľné vlastné lineárne typy cez `AK_CUSTOM` s vlastným názvom a prefixom,
- individuálna aj bezpečná batch editácia rozmerov, materiálu, režimu dĺžky, sklonu a výrobného prídavku,
- lokalizovaný katalóg šiestich materiálov so stabilnými canonical hodnotami v DWG,
- stabilné položkové číslovanie, XData metadata a väzby cez `ElementId`/`SourceHandle`,
- explicitný `AK_RENUMBER` na vedomé kompaktné prečíslovanie podľa finálnej reznej dĺžky,
- centrálne výpočty skutočnej a reznej dĺžky, prídavkov, zaokrúhľovania a kubatúry,
- automatický refresh po MOVE, ROTATE, STRETCH, TRIM, EXTEND a grip edit,
- labely prvkov, collision-aware anotácia smeru sklonu a `AK_FLIPSLOPE`,
- report z výberu alebo celého výkresu s prirodzeným radením položiek a adaptívnymi stĺpcami,
- rectangular footprint pre Stĺpik z jednej rectangular Polyline,
- konverzia validného obdĺžnika zo štyroch samostatných LINE na jeden Post footprint,
- bezpečné správanie pri COPY, COPYCLIP/PASTECLIP, WBLOCK a SAVE/REOPEN,
- Ribbon aj klasický dokovateľný panel,
- runtime lokalizácia SK, CS, EN, DE, PL a FR bez zmeny technických DWG dát.

## Post / Stĺpik

Nový Stĺpik je reprezentovaný jednou uzavretou rectangular Polyline. Kliknutá strana určuje orientáciu `Width`, susedná strana `Height`; skutočná dĺžka pochádza z manuálnej dĺžky, nie z obvodu footprintu. Doplnok vytvára samostatný trojriadkový label a anotáciu `⊥ 90°`.

Alternatívny vstup zo štyroch LINE najprv overí jednoznačný uzavretý obdĺžnik. Až po úspešnom vytvorení a priradení jednej Polyline odstráni pôvodné čiary. Pri chybe vstupnú geometriu nemení.

Legacy line-based Post prvky zostávajú čitateľné a kompatibilné.

## Vlastný definovaný prvok

`AK_CUSTOM` priradí vybraným LINE/LWPOLYLINE existujúcu alebo novú používateľskú definíciu. Nová definícia má stabilné technické ID, používateľský názov a jedinečný prefix (napríklad `Konzola` / `KO`). Definície sa pre pohodlné opätovné použitie ukladajú do používateľského katalógu, ale každý prvok zároveň nesie celé ID, názov aj prefix vo vlastných XData; COPY, COPYCLIP, WBLOCK a otvorenie DWG na inom počítači preto nie sú závislé od lokálnych nastavení.

Vlastné názvy sa neprekladajú. Každé stabilné ID má samostatnú výrobnú signatúru a numbering sériu, takže napríklad `KO1` a `PR1` zostávajú nezávislé aj pri rovnakom priereze a dĺžke. `AK_EDIT` umožňuje explicitne premenovať definíciu vo všetkých jej prvkoch v aktuálnom DWG bez zmeny stabilného ID, prefixu alebo položkových čísel. Automatický label zostáva stručný — položka, prierez a výrobná dĺžka — kým názov je dostupný v editácii, inspecte a reportoch.

Custom je slope-aware lineárny typ: v automatickom režime používa rovnaký centrálny prepočet skutočnej a výrobnej dĺžky podľa sklonu ako Krokva. Zmena sklonu, geometrie alebo COPY/COPYCLIP preto prejde rovnakou synchronizáciou signatúry, numberingu, labelu a reportu.

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

## Číslovanie položiek

Bežné automatické číslovanie je stabilné: pri kreslení, editácii, COPY alebo live refreshi sa existujúce čísla nekompaktujú a medzery zostávajú zachované. Nová výrobná signatúra dostane číslo podľa existujúcich stabilných pravidiel.

`AK_RENUMBER` je samostatná vedomá operácia nad všetkými platnými inteligentnými prvkami aktuálneho DWG. Po potvrdení zoradí unikátne výrobné signatúry v každom type podľa `CuttingLengthMm` od najkratšej po najdlhšiu a pridelí súvislé čísla od 1. Rovnaké signatúry ostávajú jednou položkou. Geometria, `SourceHandle` a výrobné údaje sa nemenia; labely a nové reporty používajú nové označenia.

## Materiály a reporty

Preddefinovaný katalóg používa canonical hodnoty `Smrek C24`, `Smrek C16`, `Smrekovec C30`, `KVH C24 NSi`, `KVH C24 Si` a `BSH GL24h`. Do DWG, `TimberElementSignature`, COPY/COPYCLIP a numberingu vstupuje vždy canonical hodnota; SK/CS/EN/DE/PL/FR menia iba zobrazenie v `AK_EDIT`, `AK_INSPECT` a reportoch. Neznámy materiál zo starého DWG sa zobrazí a zachová presne bez migrácie.

`AK_REPORT` a `AK_REPORTALL` zobrazujú katalógový materiál stabilne v dvoch riadkoch: hlavný názov a lokalizovaná popisná časť. Stĺpce Typ a Materiál sa rozširujú iba podľa skutočného obsahu konkrétneho reportu, nerozdeľujú bežné slová uprostred a dátový riadok zostáva najviac dvojriadkový. Číselné stĺpce majú stabilné kompaktné šírky.

## Príkazy

| Oblasť | Príkazy |
|---|---|
| Pomoc a UI | `AK_HELP`, `AK_RIBBON`, `AK_TOOLBAR`, `AK_TOOLBARSHOW`, `AK_TOOLBARHIDE` |
| Priradenie | `AK_ASSIGN`, `AK_CUSTOM`, `AK_KROKVA`, `AK_POMURNICA`, `AK_VAZNICA`, `AK_STLPIK`, `AK_KLIESTINA`, `AK_VZPERA`, `AK_VAZNYTRAM` |
| Údaje | `AK_EDIT`, `AK_INSPECT`, `AK_RECALC`, `AK_RENUMBER`, `AK_FLIPSLOPE` |
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

Najbližšou plánovanou funkciou je Select Similar / filtre. Dokumentácia, centralizácia verzie a explicitný `AK_RENUMBER` sú už dokončené základy.
