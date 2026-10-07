# Ordinary GRIP_STRETCH → NO deferred native-grip refresh

Predchádzajúce invalidácie a clear/reselect cykly opravili obyčajné gripy,
ale ponechali izolované natívne hot-grip transienty v dočasnej polohe.

Po úspešnom NO rollback commite sa teraz naplánuje jednorazový callback na
`Autodesk.AutoCAD.ApplicationServices.Application.Idle`. CommandEnded handler
sa najprv úplne dokončí; callback potom:

1. invaliduje obnovené entity cez `RecordGraphicsModified(true)`,
2. vykoná `QueueForGraphicsFlush()`,
3. zavolá `Editor.Regen()` (nie `REGENALL`),
4. overí a podľa potreby obnoví pôvodný implied selection,
5. zavolá `UpdateScreen()`.

Selection sa v callbacku zámerne necykluje cez empty selection. Cieľom je
odstrániť native GRIP_STRETCH transienty až po úplnom command unwind.

DEBUG diagnostika:

```text
ROOF_ORDINARY_GRIP_ROLLBACK_REFRESH
line=
selectionBefore=
selectionCleared=false
selectionRestored=
graphicsInvalidated=
deferredRegenScheduled=true
deferredRegenExecuted=
commandActiveAtRegen=false
result=api-verified|failed
```

`api-verified` neznamená vizuálny HOST PASS; potvrdzuje iba vykonanie
post-command sequence a overenie selection state.

## Overenie

- source-contract testy: PASS, 7/7
- Full Gate: PASS, 7 446/7 446 Core + 828/828 WPF
- finálny Debug x64 rebuild: PASS, 0 warningov / 0 chýb
- AutoCAD spustený: `C:\Program Files\Autodesk\AutoCAD 2027\acad.exe`
- DWG: `C:\Users\Roman\Documents\3d.dwg`
- PID: `57304`
- finálny DLL čas: **2026-10-05 12:33:55 CEST**
- SHA-256: `BA9919D0AEE16FF3ED8AD91277B3CCD5895EB331A0212652D55F5C0DF58E9FCE`

## HOST retest

Na otvorenom fresh/canonical výkrese vyber AUTO Ordinary endpoint GRIP_STRETCH,
zmeň geometriu a vyber NO. Bez ESC a bez REGEN/REGENALL skontroluj obnovenú
čiaru, bežné gripy aj neprítomnosť izolovaných modrých štvorcov v starej
natiahnutej polohe. Zachyť `ROOF_ORDINARY_GRIP_ROLLBACK_REFRESH` a výsledok
vizuálneho testu.

Bez commit/push/tag.
