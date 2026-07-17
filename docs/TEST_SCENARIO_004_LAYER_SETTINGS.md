# ACAD KROVY 0.4.0 – test nastavení prvkov a hladín

## Predpoklady
- AutoCAD je zavretý pred buildom.
- V projektoch je použitý Ribbon input hotfix 0.3.2.
- Doplnok bol po Rebuild Solution načítaný cez `NETLOAD`.

## Test A: predvolený profil
1. Otvor nový DWG.
2. Zadaj `AK_SETTINGS`.
3. Over defaulty: `KROKVA`, `POMURNICA`, `VAZNICA`, `STLPIK`, `KLIESTINA`, `VZPERA`, `VAZNY_TRAM`.
4. Klikni **Uložiť a použiť vo výkrese**.
5. V Layer Properties Manager over, že hladiny vznikli vo farbách podľa tabuľky.

## Test B: nový prvok
1. Nakresli čiaru.
2. Označ ju a klikni Ribbon **Krokva**.
3. Potvrď údaje.
4. V Properties over: `Layer = KROKVA`, `Color = ByLayer`.

## Test C: zmena typu
1. Vyber inteligentnú krokvu.
2. Spusti `AK_EDIT`.
3. Zaškrtni typ a zmeň ho na **Stĺpik**.
4. Over: `Layer = STLPIK`, `Color = ByLayer`.

## Test D: vlastná hladina
1. Zadaj `AK_SETTINGS`.
2. Pri Krokve zmeň hladinu na `KROV_KROKVA`, farbu na zelenú.
3. Klikni **Uložiť a použiť vo výkrese**.
4. Over, že existujúce krokvy aj nová krokva sú na `KROV_KROKVA` so zelenou farbou ByLayer.

## Test E: uloženie
1. Ulož DWG, zavri a znovu otvor.
2. Over, že hladiny a farby ostali vo DWG.
3. Spusti `AK_REPORTALL` a over, že výkaz/kubatúra ostali bez zmeny.
