# ACAD KROVY 0.6.0 – test automatických popisov

1. Otvor nový DWG v mm a načítaj AcKrovy.AutoCAD.dll.
2. Nakresli 4 čiary, každú dlhú 4000 mm.
3. Označ ich, klikni Krokva a zadaj 80 × 160 mm, sklon 35°, prídavok 100 mm.
4. Over, že pri každej čiare vznikol sivý MText popis na hladine KROV_POPIS: K1/K2..., 80 × 160, 4990 mm.
5. Zmeň dĺžku jednej čiary na 4200 mm a zadaj AK_RECALC. Popis musí ukazovať 5230 mm (4200 / cos 35° + 100, zaokrúhlené hore na 10 mm).
6. Zadaj AK_LABELHIDE. Hladina KROV_POPIS musí zmiznúť.
7. Zadaj AK_LABELSHOW. Popisy sa musia znovu zobraziť.
8. Urob WBLOCK s čiarami aj popismi. V novom DWG over AK_REPORTALL a AK_LABELS.
9. Urob WBLOCK iba s čiarami. V novom DWG zadaj AK_LABELS: musia sa vytvoriť nové popisy bez ručného prepisovania údajov.
