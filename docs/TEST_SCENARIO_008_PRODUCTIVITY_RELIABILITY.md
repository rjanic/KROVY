# TEST SCENARIO 008 – PRODUCTIVITY & RELIABILITY v0.19.0

Platforma: plný AutoCAD 2027, produkčný x64 build v0.19.0.

## Zaznamenaný výsledok finálneho manuálneho retestu – 27. 7. 2026

Používateľ v AutoCADe 2027 úspešne overil:

- `AK_SELECTSIMILAR` a jeho PickFirst prepojenie s `AK_EDIT`,
- `AK_EDIT` no-op pri prázdnom patchi aj pri zadaní už existujúcej hodnoty,
- `AK_EXPORTCSV` v režimoch Individual a Summarized,
- export celého model space, ručný výber s preskočením neinteligentných
  objektov a bezpečný Cancel,
- `DBMOD = 0` pri read-only operáciách,
- layout `AK_DIAGNOSTICS`,
- EN, SK a DE lokalizáciu diagnostických udalostí,
- clipboard diagnostický súhrn s anonymizovanou cestou
  `%LOCALAPPDATA%\ACAD_KROVY\Logs`.

Nasledujúci úplný protokol zostáva regresným checklistom; tento záznam
netvrdí manuálne vykonanie bodov, ktoré nie sú uvedené vyššie.

Pred testom:

1. Vytvor kópiu DWG s viacerými typmi, rovnakými aj rozdielnymi prierezmi,
   materiálmi, položkami, výrobnými dĺžkami a aspoň dvoma Custom definíciami.
2. Pridaj LINE bez ACAD KROVY metadata a paper-space geometriu.
3. Zapíš `DBMOD`, aktívny jazyk a cestu
   `%LOCALAPPDATA%\ACAD_KROVY\Logs`.

## A. `AK_SELECTSIMILAR`

1. PickFirst označ presne jeden inteligentný prvok a spusti príkaz.
2. Over defaulty: Typ, Prierez a Materiál zapnuté; Položka a Výrobná dĺžka
   vypnuté; Vlastná definícia zapnutá iba pre Custom.
3. Potvrď defaulty a over implied selection vrátane vzorového prvku.
4. Zopakuj test osobitne iba s Typom, Prierezom, Materiálom a Položkou.
5. Over, že prierez 80 × 160 sa automaticky nezhoduje so 160 × 80.
6. Zapni výrobnú dĺžku; otestuj toleranciu 0, 1 a vyššiu hodnotu vrátane
   presnej hranice. Záporná hodnota musí zostať vo validácii okna.
7. Pre Custom over zhodu stabilného Custom ID a nezhodu inej definície aj pri
   rovnakom názve/rozmeroch.
8. Otestuj nulový výsledok; implied selection musí byť prázdny.
9. Otestuj neinteligentný objekt, viac než jeden PickFirst objekt a Esc pri
   výbere aj vo WPF okne. Cancel sa nesmie objaviť ako error v logu.
10. Over, že paper space sa ignoruje.
11. Porovnaj `DBMOD` pred/po každom úspešnom aj zrušenom behu; musí zostať
    nezmenený. Over aj nezmenené XData a anotácie.

## B. `AK_EXPORTCSV`

1. Otestuj zdroj PickFirst s inteligentnými aj neinteligentnými objektmi.
2. Otestuj ručný výber a celý model space; paper space sa nesmie exportovať.
3. Pre každý zdroj otestuj režimy Individual aj Summarized.
4. V Summarized porovnaj grouping, quantity, total length a volume s
   `AK_REPORT`/`AK_REPORTALL`.
5. Over UTF-8 BOM, oddeľovač `;`, CRLF a lokalizované hlavičky s jednotkami.
6. Otestuj custom názvy a poznámky s Unicode, bodkočiarkou, úvodzovkami a
   novým riadkom; CSV musí zostať korektne escapované.
7. Prepnúť všetkých šesť jazykov; over hlavičky, lokalizovaný materiál a
   desatinný formát aktívnej kultúry.
8. Zruš výber, options okno aj Save File dialóg; nesmie vzniknúť súbor.
9. Ulož na existujúcu cestu a over štandardné overwrite potvrdenie.
10. Otvor CSV v Exceli a over stĺpce, Unicode a desatinné hodnoty.
11. Over výslednú správu: cesta, počet riadkov/skupín a preskočené objekty.
12. Porovnaj `DBMOD` pred/po exporte; musí zostať nezmenený.

## C. `AK_DIAGNOSTICS`, logger a recovery

1. Otvor okno v Light aj Dark téme a vo všetkých jazykoch SK/CS/EN/DE/PL/FR.
2. Over ProductVersion 0.19.0, metadata schema 4, layer profile schema 3,
   AutoCAD/runtime, jazyk, päť settings stavov a log path.
3. Klikni Kopírovať súhrn a over clipboard.
4. Klikni Otvoriť logy a over otvorenie
   `%LOCALAPPDATA%\ACAD_KROVY\Logs`.
5. Vyvolaj bezpečne zachytenú testovaciu neočakávanú chybu a over command,
   exception type a stack trace; log nesmie obsahovať obsah/geometriu DWG,
   názvy/hodnoty prvkov, plnú DWG cestu ani používateľské meno z profile path.
6. Over denný názov logu, rotáciu po 5 MB a čistenie súborov starších než
   14 dní bez pádu príkazu.
7. Mimo AutoCADu zálohuj a potom poškod postupne:
   `application-settings.json`, `settings-ui.json`,
   `element-layer-profile.json`, `timber-element-default-profile.json` a
   `custom-element-definitions.json`.
8. Pri každom súbore spusti príslušné načítanie a over presne jednu zálohu
   `<name>.corrupt.<yyyyMMdd-HHmmss>.json`, bezpečné defaults a použiteľný plugin.
9. Simuluj zlyhanie zálohy oprávneniami/lockom; originál musí zostať
   nedotknutý, defaults iba v pamäti a neskorší Save ho nesmie prepísať.
10. Opakované načítanie rovnakého poškodeného súboru nesmie vytvoriť backup storm.
11. Otvor `AK_DIAGNOSTICS` postupne v SK/CS/EN/DE/PL/FR a v Light/Dark téme.
    Over kompaktnú prirodzenú výšku, centrovanie na aktuálnom monitore,
    rolovanie stredného obsahu a vždy viditeľné spodné tlačidlá.
12. Zmenši okno a potom ho zväčši. Footer musí zostať mimo rolovanej oblasti,
    obsah sa nesmie orezať a nesmie vzniknúť neprimeraná prázdna plocha.
13. Pred otvorením diagnostiky zmeň a ulož rozmery/pozíciu `AK_SETTINGS`.
    `AK_DIAGNOSTICS` ich nesmie prevziať ani spätne zmeniť v `settings-ui.json`.
14. Klikni Kopírovať súhrn. UI môže ukazovať reálny log path, ale clipboard
    musí obsahovať `%LOCALAPPDATA%\ACAD_KROVY\Logs` a nesmie obsahovať meno
    Windows používateľa ani `C:\Users\<meno>`.
15. V posledných udalostiach over názov príkazu pri CommandStarted/Completed
    a krátky bezpečný detail SettingsConfiguration bez stack trace.

## D. Regresie v0.18.0

1. Otestuj `AK_ASSIGN`, `AK_EDIT`, `AK_REPORT`, `AK_REPORTALL`, `AK_LABELS`,
   `AK_RENUMBER`, `AK_SETTINGS` a `AK_FLIPSLOPE`.
2. Otestuj COPY, COPYCLIP/PASTECLIP, WBLOCK a SAVE/REOPEN.
3. Otestuj STRETCH, TRIM, EXTEND, MOVE a ROTATE vrátane framed manuálneho offsetu.
4. Over všetkých 10 annotation presetov, NoAnnotations, standalone framed
   a combined framed Circle/Rectangle/Slot.
5. Over prvý framed segment 60°, combined LandingDistance 350 mm,
   horizontálny landing, rozmery pod sebou a MText middle-center.
6. V Settings over 1500 × 900 default, 1250 × 720 minimum a sekčné footery.
7. Pri konflikte vrstvy over vytvorenie KROKVA_01, opätovné použitie zhodnej
   KROKVA_01 a vznik KROKVA_02 iba pri ďalšom skutočnom konflikte.

Automatické testy a source-contract kontroly nie sú náhradou tohto reálneho
hostiteľského overenia DBMOD, dialógov, Excelu ani AutoCAD výberu.

## E. Retest persistencie jazyka

1. Spusti AutoCAD 2027, načítaj plug-in a skontroluj log.
2. Otvor Settings bez zmeny jazyka, zavri ho a otvor ešte raz.
3. Over, že pre `Application language` vznikli iba udalosti `Settings file loaded`
   a žiadna udalosť `Settings file saved`.
4. Zmeň jazyk presne raz a over presne jednu udalosť `Settings file saved`.
5. Znovu klikni na už aktívny jazyk; ďalší Save nesmie vzniknúť.
6. Over okamžitý preklad Settings, Ribbonu, Classic Toolbaru a ostatných
   otvorených lokalizovaných okien.
7. Ukonči a znovu spusti AutoCAD; over obnovený jazyk a log s načítaním bez Save.

## F. Retest `AK_EDIT` no-op a PickFirst

1. Nastav `DBMOD = 0`.
2. Spusti `AK_SELECTSIMILAR` tak, aby výsledný implied výber obsahoval napríklad
   9 inteligentných prvkov.
3. Spusti `AK_EDIT`; príkaz nesmie zobraziť ďalší selection prompt a okno musí
   pracovať so všetkými 9 prvkami.
4. Bez zaškrtnutia editovateľného poľa klikni Použiť. Over lokalizované
   oznámenie o nevybranej zmene, 0 upravených prvkov a `DBMOD` stále 0.
5. Znova spusti `AK_EDIT` nad rovnakým výberom, zaškrtni jednu vlastnosť, ale
   ponechaj hodnotu zhodnú so všetkými vybranými prvkami. Over 0 upravených
   prvkov, bez obnovy anotácií a bez zmeny `DBMOD`.
6. Zopakuj `AK_SELECTSIMILAR` a `AK_EDIT`, zaškrtni jednu vlastnosť a nastav
   skutočne novú hodnotu. Over počet upravených iba pre prvky, ktorých výsledná
   hodnota sa zmenila, a následnú očakávanú zmenu `DBMOD`.
7. Zmiešaj inteligentné a obyčajné entity. `AK_EDIT` musí použiť všetky platné
   prvky, neinteligentné bezpečne preskočiť a uviesť ich oddelene.
8. Pri prázdnom alebo výhradne neplatnom implied výbere over existujúci ručný
   selection prompt a bezpečný Esc/cancel.
9. Vykonaj `UNDO` a over návrat metadata, hladín aj anotácií do pôvodného stavu.

Automatické pravidlá efektívnej zmeny a source-contract testy chránia tok kódu,
ale nemenia tento AutoCAD host test na automatický dôkaz správania `DBMOD`.
