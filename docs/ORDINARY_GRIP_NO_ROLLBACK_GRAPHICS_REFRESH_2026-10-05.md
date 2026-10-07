# Ordinary GRIP_STRETCH → NO graphics refresh

Po úspešnom NO rollbacku zostáva výber aktívny, ale AutoCAD môže držať grip
cache na dočasnej natiahnutej polohe. Oprava je iba v grafickej vrstve:
obnovený balík (`Plan2D`, Physical3D a vlastné anotácie) sa označí
`RecordGraphicsModified(true)`, transakcia zavolá `QueueForGraphicsFlush()` a
po commite sa prekreslí obrazovka. Ak existuje implied selection, nastaví sa
znova na presne rovnaké `ObjectId[]`; výber sa nemaže a `REGENALL` sa nežiada.

Debug diagnostika:

```text
ROOF_ORDINARY_GRIP_ROLLBACK_REFRESH
line=
selectionPreserved=
graphicsInvalidated=
regenRequested=false
result=
```

Zmena nezasahuje lifecycle rozhodnutie, snapshoty, GROUP, designation,
IndependentMemberId ani Physical3D. Obnovenie stále prebieha pred commitom;
refresh je nevyhadzujúca UX operácia po úspešnom commite.

## Zmenené súbory

- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryGripLifecycleService.cs`
- `src/AcKrovy.Core.Tests/RoofOrdinaryGripLifecycleSourceContractTests.cs`
- tento report

## Overenie

- cielené testy: PASS, 24/24
- Portable Gate: PASS, 7 446/7 446 Core
- Full Gate: PASS, 7 446/7 446 Core + 828/828 WPF
- AutoCAD Debug x64 build: PASS, 0 warningov / 0 chýb
- `git diff --check`: PASS (existujúce LF/CRLF upozornenia iba pri dirty WIP)
- nový HOST vizuálny test: ešte neprebehol; zdrojové testy nepotvrdzujú grip cache v AutoCADe

Finálny DLL build:

- [AcKrovy.AutoCAD.dll](../src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll)
- čas: **2026-10-05 12:11:30 CEST**
- SHA-256: `453F17293B0E157C7810565F57A7060CF977601F4612E46E4CE06A44B8C760E4`

## HOST retest

Na fresh/canonical DWG spusti AUTO Ordinary endpoint GRIP_STRETCH, zmeň geometriu,
zvoľ NO a bez REGEN vizuálne skontroluj, že gripy okamžite ležia na obnovenej
čiare. Výber musí zostať aktívny; ESC ho musí odstrániť. Zachyť aj nový
`ROOF_ORDINARY_GRIP_ROLLBACK_REFRESH` riadok a existujúci lifecycle riadok.

Bez commit/push/tag.
