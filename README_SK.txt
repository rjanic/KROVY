ACAD KROVY 0.7.0 – Manufacturing Length & Allowance Foundation

Táto verzia nadväzuje na stabilnú architektúru v0.6.0 a bola manuálne overená v AutoCAD 2027.

ČO PRIDÁVA
- Nastaviteľné predvolené výrobné prídavky podľa typu prvku v AK_SETTINGS.
- Predvolené prídavky sa ukladajú pre aktuálny účet Windows do:
  %APPDATA%\ACAD_KROVY\timber-element-default-profile.json
- Nové prvky cez AK_ASSIGN a rýchle príkazy preberajú aktuálny default podľa typu.
- Existujúce prvky si uchovávajú vlastný uložený CuttingAllowanceMm, pokiaľ používateľ nezvolí aplikovanie defaultov na výber alebo na všetky prvky.
- AK_SETTINGS obsahuje režimy:
  Uložiť a aplikovať na všetky
  Uložiť a aplikovať na výber
  Uložiť iba pre nové prvky
- CuttingLengthMm sa počíta ako:
  RoundUp(ActualLengthMm + Max(0, CuttingAllowanceMm), 100)
- Výsledná rezná dĺžka sa vždy zaokrúhľuje nahor na 100 mm.
- COPY/COPYCLIP kópia sa pri existujúcom synchronizačnom flow správa ako nový fyzický prvok a preberá aktuálny default podľa typu.
- WBLOCK/import kompatibilita zostáva chránená pred hromadným prepisom prídavkov iba kvôli zmene handle hodnôt.
- Výpočet a aplikovanie výrobných prídavkov sú centralizované v Core bez AutoCAD závislostí.
- Po AK_ASSIGN alebo ikonke typu sa pri každom prvku automaticky vytvorí MText štítok:
  K1
  80 × 160
  5000 mm
- Po AK_EDIT sa popis okamžite obnoví.
- AK_RECALC zároveň obnoví popisy po manuálnej úprave dĺžok čiar.
- AK_LABELS ručne obnoví/vytvorí popisy všetkých prvkov.
- AK_LABELSELECTED pracuje iba s aktuálnym výberom.
- AK_LABELSHOW / AK_LABELHIDE zobrazia alebo skryjú samostatnú hladinu KROV_POPIS.
- Popisy sú na hladine KROV_POPIS, sivá ACI 8, a používajú farbu ByLayer.
- Ribbon aj klasický panel dostanú nové tlačidlo „Obnoviť popisy“.

INŠTALÁCIA
1. Zavri AutoCAD.
2. Rozbaľ tento ZIP.
3. Skopíruj jeho priečinok src do koreňa projektu:
   C:\Users\Roman\Documents\CODEX\C#\CsharpProjects\ACAD_krovy\
4. Potvrď prepísanie existujúcich súborov.
5. Vo Visual Studiu spusti Build → Rebuild Solution.
6. Otvor AutoCAD, NETLOAD, vyber AcKrovy.AutoCAD.dll.

WBLOCK
Ak pri WBLOCK vyberieš prvky AJ ich textové popisy, prenesú sa spolu.
Ak vyberieš iba čiary krovu, v novom DWG zadaj AK_LABELS a popisy sa vytvoria znovu.
