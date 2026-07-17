# Testovací scenár 001 – sedlová strecha, krokvy

## Cieľ

Overiť, že hromadne označené krokvy dostanú spoločný prierez, reálna dĺžka sa prepočíta zo sklonu a tabuľka zoskupí rovnaké kusy.

## Vstup

- AutoCAD jednotky: milimetre.
- 4 čiary `LINE`, každá s pôdorysnou dĺžkou 4000 mm.
- Sklon: 35°.
- Krokva: 80 × 160 mm.
- Prídavok na rezanie: 100 mm.

## Kroky

1. Spusti `AK_ASSIGN`.
2. Vyber všetky 4 čiary.
3. Vyplň alebo ponechaj prednastavené hodnoty:
   - Typ: Krokva
   - Šírka: 80
   - Výška: 160
   - Sklon: 35
   - Prídavok: 100
   - Režim dĺžky: Automaticky podľa typu
4. Potvrď dialóg.
5. Označ 4 čiary a spusti `AK_REPORT`.
6. Vlož tabuľku do voľnej časti výkresu.

## Očakávaný výsledok

```text
Pôdorysná dĺžka: 4,000 m
Skutočná dĺžka: 4,883 m
Dĺžka na rezanie po zaokrúhlení na 10 mm: 4,990 m
Objem jedného kusa: 0,0639 m³
Počet: 4 ks
Celková kubatúra: 0,2555 m³
```

Poznámka: výsledok sa môže nepatrne líšiť pri budúcej zmene pravidla zaokrúhľovania.
