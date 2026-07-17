ACAD KROVY 0.6.0 – Automatické popisy prvkov vo výkrese

Tento patch predpokladá nainštalovanú a otestovanú verziu 0.5.2.

ČO PRIDÁVA
- Po AK_ASSIGN alebo ikonke typu sa pri každom prvku automaticky vytvorí MText štítok:
  K1
  80 × 160
  4990 mm
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
