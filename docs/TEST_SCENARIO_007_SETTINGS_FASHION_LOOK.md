# TEST SCENARIO 007 – SETTINGS FASHION LOOK v0.18.0

Platforma: AutoCAD 2027, čistý Debug alebo Release build v0.18.0.

Pred testom si priprav DWG s minimálne jednou Krokvou, ďalším timber typom,
existujúcou konfliktnou vrstvou a framed anotáciami Circle/Slot/Rectangle.
Zapíš si počiatočnú hodnotu `DBMOD`.

## Manuálny vizuálny a funkčný test

1. Pri 100 % DPI spusti `AK_SETTINGS`; over približne 1180 × 720 px okno,
   modernú ľavú navigáciu a verziu `ACAD KROVY 0.18.0`.
2. Preklikaj Hladiny, Výrobné prídavky, Popis/kótovanie a Jazyk; over hover,
   selected stav, accent pruh, vektorové ikony a keyboard focus.
3. Zavri okno bez Apply a porovnaj `DBMOD` s počiatočnou hodnotou.
4. Znovu otvor okno; over zachovanú poslednú sekciu.
5. Zmeň veľkosť a polohu okna, maximalizuj ho, zavri a znovu otvor; over
   lokálne obnovenie geometrie.
6. Zmenši okno na minimum 980 × 620; over wrapping a neprítomnosť
   horizontálneho chaosu alebo odrezaného obsahu.
7. V Hladinách otvor ACI picker; over 255 vzoriek, aktuálnu veľkú vzorku,
   text `ACI n`, priamy vstup a tooltipy.
8. Postupne vyber a potvrď ACI 1, 30, 142 a 255; over konzistentný náhľad
   farby v pickeri aj layer riadku.
9. Priamo zadaj 1 a 255; over prijatie. Zadaj 0, 256 a text; over
   lokalizovanú chybu a otvorený picker.
10. Vyber inú farbu a stlač Esc; over, že pôvodný ACI index v riadku zostal.
11. Potvrď novú ACI farbu a použi Selection Apply; over správny ACI index
    vrstvy v AutoCADe a `Color = ByLayer` na timber entite.
12. Over Continuous ako plnú čiaru, DASHDOT ako bodko-čiarkovanú čiaru,
    zobrazenú mierku a technický tooltip neznámeho linetype.
13. V Popis/kótovanie vyber postupne FullLabel, ItemNumberLeader a
    DimensionsLeader; over karty a zachovaný výber.
14. Pri ItemNumberLeader vyber Plain, Circle, Slot a Rectangle. Pri inom
    hlavnom režime over disabled frame selector so stále zobrazenou hodnotou.
15. Preklikaj Slovenčina, Čeština, English, Deutsch, Polski a Français;
    over okamžitý preklad navigácie, kariet, pickeru, bannerov a tlačidiel,
    zachovanú sekciu, ACI indexy, enumy a rozpracované hodnoty.
16. Prepnúť Light a Dark; over kontrast, focus, disabled stav, validation
    a nezmenené formulárové hodnoty. Porovnaj `DBMOD`: samotná téma ho nemení.
17. Spusť Selection Apply najmenej trikrát bez zmeny profilu, vždy s novým
    výberom; over otvorené okno, výsledný banner a žiadny leak/exception.
18. Spusť All Apply najmenej dvakrát bez zmeny profilu; over nový check
    výkresu a zápis iba skutočných rozdielov.
19. Zvoľ NewElementsOnly s nezmeneným profilom; over no-op a zachovanie
    existujúcej konfliktnej vrstvy.
20. Zopakuj kroky 1–16 pri 125 %, 150 % a 200 % DPI. Nakoniec over framed
    Circle/Slot/Rectangle: Spline, insertion-point attachment, 40°, 350 mm,
    Circle 520 mm, klasický STRETCH a persistentný manuálny offset.

## Screenshot checklist

- `01-layers-light.png` – sekcia Hladiny v Light téme,
- `02-layers-dark.png` – sekcia Hladiny v Dark téme,
- `03-aci-picker.png` – otvorený picker s paletou a priamym indexom,
- `04-annotation-cards.png` – Popis/kótovanie s mode a frame kartami,
- `05-language-cards.png` – všetkých šesť jazykov,
- `06-status-banner.png` – veľký overlay banner nad obsahom,
- `07-german.png` – nemecká lokalizácia s dlhými zalomenými textami,
- `08-french.png` – francúzska lokalizácia s dlhými zalomenými textami.

Pri každom screenshote zaznamenaj DPI, tému, rozmer okna a výsledok `DBMOD`.

## Retest po opravách ACI, hladín a režimu Bez popisov

1. Otvor ACI picker.
2. Vyber ACI 1 a klikni na Potvrdiť.
3. Over zmenu swatchu a technického textu v príslušnom riadku.
4. Picker znovu otvor, vyber ACI 142 a zruš ho klávesom Esc.
5. Over, že v riadku zostala ACI 1 a formulár nedostal ďalšiu zmenu.
6. Over samostatný horný pás 1–9, mriežku 10–249 vo formáte 24 × 10 a spodný pás 250–255.
7. Otvor editovateľný zoznam názvu hladiny a vyber Defpoints.
8. Zadaj nový vlastný názov hladiny a over, že výber sám nemení DWG.
9. V režime Iba nové prvky nastav pre existujúcu KROKVA odlišné vlastnosti.
10. Klikni Použiť a over vytvorenie KROKVA_01 aj lokalizovaný banner.
11. Zopakuj konflikt so základom KROKVA a over vytvorenie KROKVA_02.
12. Over, že pôvodné entity zostali na KROKVA a nové predvolené nastavenie používa suffixovaný názov.
13. Pre jeden vybraný inteligentný prvok nastav Bez popisov a použi režim Na výber.
14. Podľa typu over odstránenie hlavného popisu, slope šípky/textu a Post 90° anotácie.
15. Prepnúť prvok späť na FullLabel alebo ItemNumberLeader.
16. Over korektnú regeneráciu, pôvodný frame style a absenciu duplicít alebo orphan anotácií.
17. Over DBMOD: otvorenie pickeru, Cancel, zmena ComboBoxu a navigácia sú čisté; vytvorenie suffixovanej hladiny DBMOD zmení.
