# Compatibility Gate

Compatibility Gate je automatická kontrola, ktorá chráni prenositeľné jadro projektu ACAD KROVY pred nechceným naviazaním na konkrétny CAD adaptér a zároveň overuje build/test stav riešenia.

Táto infraštruktúra nemení runtime správanie plug-inu, metadata formát, XData schému ani pravidlá COPY/COPYCLIP/WBLOCK.

## Portable Gate

Portable Gate je možné spustiť aj bez nainštalovaného AutoCADu. Používa sa v GitHub Actions a na strojoch, kde nie sú dostupné Autodesk DLL.

Spustenie:

```powershell
.\scripts\compatibility-gate.ps1 -Portable
```

Kontroluje:

- `AcKrovy.Core` restore/build s warnings-as-errors,
- `AcKrovy.Cad.Abstractions` restore/build s warnings-as-errors,
- všetky `AcKrovy.Core.Tests` testy,
- `ProjectReference`, `Reference` a `PackageReference` v portable projektoch,
- zdrojové súbory portable projektov na priame odkazy na Autodesk AutoCAD API.

Portable projekty nesmú referencovať:

- `Autodesk.AutoCAD.*`,
- `AcMgd`,
- `AcDbMgd`,
- `AcCoreMgd`,
- `AcKrovy.AutoCAD`.

## Full Local Gate

Full Local Gate je určený pre vývojový počítač s AutoCAD 2027 API assemblies v:

```text
C:\Program Files\Autodesk\AutoCAD 2027
```

Spustenie:

```powershell
.\scripts\compatibility-gate.ps1 -Full
```

Bez parametrov skript automaticky spustí Full Gate, ak nájde AutoCAD API assemblies. Ak ich nenájde, spustí Portable Gate a jasne oznámi, že AutoCAD adapter build bol preskočený.

Full Gate navyše kontroluje:

- restore celého `AcKrovy.sln`,
- build celého riešenia s warnings-as-errors,
- testy celého riešenia,
- dostupnosť AutoCAD API assemblies.

Parameter `-Full` zlyhá, ak AutoCAD API assemblies nie sú dostupné. Plný gate teda nikdy nehlási PASS bez reálneho AutoCAD adapter buildu.

## GitHub Actions

GitHub runner neobsahuje Autodesk AutoCAD assemblies a repozitár ich neukladá. Workflow preto spúšťa iba Portable Gate:

```text
.github/workflows/compatibility-gate.yml
```

Workflow beží pri:

- push do `main`,
- pull request do `main`,
- manuálnom `workflow_dispatch`.

## Manuálne release smoke testy

Niektoré integračné scenáre vyžadujú reálny AutoCAD runtime a DWG operácie. Compatibility Gate ich nesmie predstierať falošnými unit testami.

Zatiaľ zostávajú manuálnymi release smoke testami:

- SAVE / REOPEN,
- WBLOCK Objects,
- COPY,
- COPYCLIP / PASTECLIP,
- STRETCH,
- TRIM,
- EXTEND,
- grip edit,
- MOVE,
- vizuálna kontrola labelov.
