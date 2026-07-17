# ACAD KROVY – Test 003: Ribbon a ikonky

## Predpoklad
- AutoCAD 2027, doplnok ACAD KROVY 0.3.0 načítaný cez `NETLOAD`.
- V príkazovom riadku `AK_HELP` vypíše `ACAD KROVY 0.3.0`.

## Kontrola karty
1. Počkajte niekoľko sekúnd po `NETLOAD`.
2. Overte kartu **ACAD KROVY** v páse Ribbon.
3. Overte panely **Prvky**, **Údaje**, **Výkaz**.
4. Ak karta chýba, zadajte `AK_RIBBON`.

## Kontrola ikon
- Každé tlačidlo má text a grafickú ikonku.
- Pri zmenšení Ribbonu sa má použiť malá ikona.
- Tooltip stručne vysvetľuje funkciu tlačidla.

## Funkčný test PickFirst
1. Nakreslite 4 čiary dĺžky 4 000 mm.
2. Označte ich pred kliknutím na ikonu.
3. Kliknite **Krokva**.
4. Dialóg musí mať predvolený typ `Krokva`.
5. Zadajte `80 × 160 mm`, `35°`, prídavok `100 mm`; potvrďte.
6. Kliknite **Výkaz z výberu** a vložte tabuľku.
7. Očakávaný riadok výkazu: 4 kusy krokiev, rezná dĺžka približne 4,990 m.

## Doplnkový test
- **Upraviť**: zmeniť výšku všetkým vybraným krokvám na 180 mm.
- **Skontrolovať**: kliknúť na jednu krokvu a overiť nové údaje.
- **Prepočítať**: očakáva sa 0 chýb.
- **Výkaz všetkého**: zobrazí všetky inteligentné prvky vo výkrese.
