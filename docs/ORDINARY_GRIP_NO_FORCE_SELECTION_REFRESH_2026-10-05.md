# Ordinary GRIP_STRETCH → NO force selection refresh

Predchádzajúce `RecordGraphicsModified(true)` + `QueueForGraphicsFlush()` +
`UpdateScreen()` ponechali AutoCAD grip cache na natiahnutej polohe. Oprava
po úspešnom rollback commite vykoná krátku grafickú transakciu, označí
obnovené entity ako graficky zmenené a zavolá flush. Následne vykoná presný
selection rebuild:

1. `Editor.SetImpliedSelection(Array.Empty<ObjectId>())`
2. `UpdateScreen()`
3. `Editor.SetImpliedSelection(originalIds)`
4. `UpdateScreen()`

Pôvodné implied selection ObjectIds sa zachytávajú pred rollbackom. Výber sa
po cykle obnoví; nejde o trvalé zrušenie výberu. Nepoužíva sa `REGENALL` ani
`Editor.Regen()`.

DEBUG diagnostika teraz obsahuje:

```text
ROOF_ORDINARY_GRIP_ROLLBACK_REFRESH
line=
selectionBefore=
selectionCleared=true|false
selectionRestored=true|false
graphicsInvalidated=true|false
regenRequested=false
result=api-verified|failed
```

`api-verified` znamená iba overený selection transition, grafickú invalidáciu
a flush na API úrovni. Vizuálny grip PASS je možné potvrdiť až v AutoCAD HOST.

## Overenie

- cielené source-contract testy: PASS, 7/7
- Full Gate: PASS, 7 446/7 446 Core + 828/828 WPF
- finálny Debug x64 rebuild: PASS, 0 warningov / 0 chýb
- `git diff --check`: PASS
- AutoCAD HOST vizuálny retest: čaká na vykonanie

Finálny DLL:

- [AcKrovy.AutoCAD.dll](../src/AcKrovy.AutoCAD/bin/x64/Debug/net10.0-windows/AcKrovy.AutoCAD.dll)
- čas: **2026-10-05 12:22:48 CEST**
- SHA-256: `3BE66F6A5DC0B345B580EA62A784339ECD1B2B2F8C3A50F6AB01E30458011C12`

## HOST retest

Fresh AUTO Ordinary, endpoint GRIP_STRETCH, zmeniť geometriu, zvoliť NO.
Bez ESC a bez REGEN/REGENALL overiť, že čiara aj všetky modré gripy sú
okamžite na obnovenej geometrii. Výber môže zostať aktívny. Zachytiť nový
`ROOF_ORDINARY_GRIP_ROLLBACK_REFRESH` riadok; API diagnostika sama nie je
vizuálny HOST PASS.

Bez commit/push/tag.
