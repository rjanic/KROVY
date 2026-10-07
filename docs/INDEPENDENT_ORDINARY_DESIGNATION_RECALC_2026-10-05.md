# Independent Ordinary: výrobné označenie po GRIP úprave

## Výsledná zmena

Po prijatej Ordinary endpoint GRIP úprave sa najprv dokončí Plan2D,
Physical3D a odpojenie na Independent. Následne sa v tej istej transakcii
zosúladí ElementId s aktuálnymi výrobnými skupinami. Člen so zmenenou
výrobnou signatúrou nepresadzuje svoje predchádzajúce označenie do novej
skupiny. Zhodná existujúca skupina má prednosť; inak sa použije bežný
alokátor voľných čísel príslušnej série.

Signatúra je existujúci `TimberElementSignature`: typ, materiál, šírka,
výška, zaokrúhlená rezná dĺžka a prípadná definícia vlastného typu.
Sklon sa aktualizuje z nového fyzického výsledku ešte pred meraním.
Pri nezmenenej signatúre sa označenie zachová. Staré čísla sa počas tejto
operácie nekompaktujú ani nerecyklujú; nejde o trvalú rezerváciu AUTO slotu.
Zoskupenie číta aktuálne viditeľné timber zdroje cez DrawingScanner,
nezávisle od strešnej proveniencie.

Anotácie dostanú konečné označenie a zachovajú väzbu SourceHandle.
Overenie odmietne neúplný zápis označenia, rozdielne signatúry pod rovnakým
označením alebo zastarané ElementLabel metadáta. AK_INSPECT a výkaz používajú
tie isté aktualizované timber metadáta a spoločné meranie.

IndependentMemberId sa pri ďalšom GRIP kontroluje proti command-start kópii.
Nové anotácie nesú tú istú Independent identitu. NO vetva prepočet nespúšťa;
zostáva presný snapshot rollback pôvodnej geometrie, metadát a GROUP.
Ak prijatie zlyhá, necommitnuté zmeny označenia/anotácií sa abortujú pred
existujúcim obnovením balíka. Potvrdený horizontal-width builder sa nemenil.
Žiadne nové ManualOverrides, geometry override polia ani ownership schémy.

## Zmenené súbory v tomto kroku

- `src/AcKrovy.Core/Services/TimberElementItemNumbering.cs`: zdieľaný alokátor;
  nový režim pre prijaté zmeny výrobnej signatúry, existujúce volania zachované.
- `src/AcKrovy.AutoCAD/Infrastructure/TimberElementItemIdentityService.cs`:
  porovnanie so signatúrou pred editom a kontrola zapísaných skupín.
- `src/AcKrovy.AutoCAD/Infrastructure/RoofOrdinaryGripLifecycleService.cs`:
  zachytenie starej signatúry, prepočet po rebuilde/detach, refresh anotácií,
  overenie konečného označenia a stabilnej Independent identity.
- `src/AcKrovy.Core.Tests/RoofIndependentOrdinaryDesignationTests.cs`:
  8 numerických regresií zoskupenia.
- `src/AcKrovy.Core.Tests/RoofOrdinaryGripLifecycleSourceContractTests.cs`:
  nové routing/transaction/identity/annotation guards; superseded zákaz
  zoskupenia v GRIP odstránený, zákaz legacy geometry zostáva.
- `.ai/architecture.md`: technická identita verzus výrobné označenie.
- `docs/geometry/roof-elevation-contract.md`: H1.0 nové schválené pravidlo
  označenia, verzia 1.3; skoršie fyzické WIP zmeny zachované.
- Tento report a logy overenia v `.ai/handoffs/`.

Existujúci rozsiahly necommitnutý WIP zostal zachovaný. Branch `main`, HEAD
`cb8f2ae225c7d0ddfc9b60ca5fb4e8200ef6493a`. Bez commit/push/tag.

## Automatické overenie

| Oblasť | Výsledok |
| --- | --- |
| Core | CAD-neutral zoskupenie; bez vendor API |
| Abstractions / UI / localization / metadata schema | Bez zmeny |
| Cielené regresie | PASS, 72/72 po poslednej úprave |
| Portable Gate | PASS, 7 445/7 445 Core |
| Full Gate | PASS aj po poslednej úprave, Core 7 445 + WPF 828 |
| Debug x64 rebuild, warnings-as-errors | PASS, 0 warnings / 0 errors |
| git diff --check | PASS; existujúce LF/CRLF upozornenia |
| Nový AutoCAD HOST retest | NOT RUN / INCONCLUSIVE |

Prvý Full Gate mal jedno zlyhanie nezmeneného WPF testu
`AutomaticPurlinMemberCenterAxisGeometryTests.CenterlinePen_UsesDashDotDistinctFromDatumDash`
na referenčnom porovnaní DashStyles.Dash; obe vypísané hodnoty mali `[2, 2]`.
Samostatná trieda prešla 10/10 a opakovaný celý Full Gate prešiel 828/828 WPF.
Test ani príslušná prezentácia neboli upravené. Opakovateľnosť tohto WPF
zlyhania zostáva samostatným rizikom.

Regresie pokrývajú kratší 80×160 kus s reznou dĺžkou 2 100 oproti nezmenenému
K4 s 4 350 mm, prevzatie existujúcej skupiny aj keď je editovaný člen prvý,
nezmenenú zaokrúhlenú signatúru, rigidný MOVE, samostatný zmenený člen,
viac súčasne upravených kusov, aktuálny AUTO replacement a alokáciu bez
kompaktovania. NO/stabilita identity/poradie transakcie sú source-contract
guards; týmto sa nedokazuje natívny HOST event flow ani vizuálna anotácia.

## Spustené príkazy

```powershell
dotnet test src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj --no-restore -c Debug --filter 'FullyQualifiedName~RoofIndependentOrdinaryDesignationTests|FullyQualifiedName~TimberElementItemNumberingTests|FullyQualifiedName~RoofOrdinaryGripLifecycleSourceContractTests|FullyQualifiedName~RoofOrdinaryLogicalMove|FullyQualifiedName~RoofOrdinaryMoveCancel|FullyQualifiedName~RoofIndependentOrdinaryHorizontalFrameTests|FullyQualifiedName~RoofOrdinaryGripLifecycleTests' -m:1 -nr:false
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Portable
pwsh -NoProfile -File scripts/compatibility-gate.ps1 -Full
dotnet test src/AcKrovy.Wpf.Tests/AcKrovy.Wpf.Tests.csproj --no-restore --no-build -c Debug -p:Platform=x64 --filter 'FullyQualifiedName~AutomaticPurlinMemberCenterAxisGeometryTests' -m:1 -nr:false
dotnet build src/AcKrovy.AutoCAD/AcKrovy.AutoCAD.csproj --no-restore -t:Rebuild -c Debug -p:Platform=x64 -warnaserror -m:1 -nr:false
git diff --check
```

Pred buildmi nebol aktívny proces acad. AutoCAD nebol spustený týmto krokom.

## Finálny build

- DLL: `C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy\src\AcKrovy.AutoCAD\bin\x64\Debug\net10.0-windows\AcKrovy.AutoCAD.dll`
- Čas posledného rebuildu: **2026-10-05 11:24:14 CEST (Europe/Bratislava)**.
- SHA-256: `F909DA96B7448236385B2A75BEBCA2EB16DF7A3DF2033645A52B614EC5767F93`.
- Posledný Full Gate log: `.ai/handoffs/independent-ordinary-designation-final-full-gate-2026-10-05.log`.
- Rebuild po finálnom Full Gate: PASS, 0 warnings / 0 errors.

## HOST retest

Predchádzajúci HOST PASS lifecycle a horizontal-width orientácie je
používateľom potvrdená vstupná evidencia. Nový designation retest ešte
neprebehol. [HOST Regression Test workflow](../.agents/skills/host-regression-test/SKILL.md)
stanovuje: „HOST regression requires human observation or trusted AutoCAD execution.“

AutoCAD 2027, fresh/canonical DWG, Debug x64 DLL z tohto buildu:

1. AUTO K4, endpoint GRIP s podstatným skrátením, YES, STOP. Overiť nové
   označenie v popise, AK_INSPECT a výkaze; nezmenený sused ostáva K4.
   Overiť stable IndependentMemberId, geometrickú zhodu, 80×160, GROUP exclusion
   a neprítomnosť nových ManualOverrides.
2. Malá ďalšia Independent úprava v tej istej zaokrúhlenej výrobnej signatúre:
   označenie aj IndependentMemberId ostávajú stabilné.
3. Upraviť Independent do existujúcej výrobnej signatúry: prevziať jej označenie.
4. V samostatnom čistom prípade AUTO GRIP → NO: presná pôvodná geometria,
   pôvodné označenie a GROUP, žiadny pretrvávajúci prepočet.
5. Rigidný MOVE: označenie a výrobná signatúra sa nemenia.
6. SAVE/REOPEN, UNDO/REDO: overiť metadáta, stabilnú identitu a popisy.

Zachytiť ROOF_ORDINARY_GRIP_LIFECYCLE, ROOF_ORDINARY_PHYSICAL_FRAME a nový
ROOF_ORDINARY_DESIGNATION (old/new ElementId, IndependentMemberId, zmena
signatúry, stará/nová rezná dĺžka, plan/true length, prierez).
Designation diagnostika má `stage=preCommit`; úspešný koniec potvrdzuje až
následný lifecycle `result=pass` po commite. Pri následnom rollback nie je
preCommit riadok dôkazom pretrvávajúcej zmeny.
