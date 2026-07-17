# ACAD KROVY 0.5.0 – test klasického panela ikoniek

1. Zavri AutoCAD, skompiluj riešenie a načítaj `AcKrovy.AutoCAD.dll` cez `NETLOAD`.
2. Do príkazového riadka zadaj `AK_TOOLBAR`.
3. Over, že sa zobrazí plávajúci panel **ACAD KROVY – klasický panel** s malými ikonami.
4. Presuň panel a prichyť ho na ľavú alebo pravú stranu okna AutoCADu. Over, že sa dá opäť uvoľniť ako plávajúci.
5. Nakresli dve čiary, označ ich a klikni na malú ikonku **Krokva**. Výber sa musí prebrať bez dodatočného Enteru.
6. Klikni na **Nastavenia** a over otvorenie `AK_SETTINGS`.
7. Klikni na **Výkaz všetkého** a over vloženie výkazu.
8. Znova zadaj `AK_TOOLBAR` a over skrytie panela.
9. Zadaj `AK_TOOLBARSHOW` a `AK_TOOLBARHIDE` ako samostatné príkazy.

Poznámka: panel je PaletteSet, nie závislosť od Ribbonu. Preto funguje aj po `RIBBONCLOSE`.
