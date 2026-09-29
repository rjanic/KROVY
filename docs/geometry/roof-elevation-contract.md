# Geometrický slovník KROVY

**Verzia:** 1.1
**Jazyk:** slovenčina (s anglickými / kódovými termínmi)  
**Status:** NORMATÍVNY kontrakt pre 2D modul automatických väzníc a schválený 2D/3D kontrakt obyčajných automatických krokiev
**Jediný zdroj pravdy (SSOT):** tento súbor

Tento dokument je záväzný pre Cursor aj OpenAI Codex. Pred úpravou strešnej geometrie, väzníc, krokiev, technických výšok, dátumov, zapustenia alebo schématickej prezentácie ho agent **musí** prečítať.

**Autorita a konflikty (nie je to „vyber najvyšší riadok“):**

Nasledujúce zdroje musia byť **vzájomne konzistentné**:

- používateľom schválené fyzické definície,
- overené HOST merania fyzických výšok v AutoCAD,
- akceptované Core regresné testy (a WPF testy invariancie technických hodnôt),
- implementácia v `AcKrovy.Core` / planneri,
- tento kontrakt.

Pri akomkoľvek rozpore agent **zastaví prácu a nahlási rozpor**. Nesmie ticho zvoliť jeden zdroj podľa poradia v zozname.

- Aktuálna **implementačná chyba** neprebíja akceptovanú fyzickú HOST geometriu ani schválené golden hodnoty.
- Názov testu `*HostRegression*` **nie je** sám o sebe dôkaz dokončeného AutoCAD HOST retestu (pozri kapitolu I / J).
- Schématické SVG / WPF **nikdy** nie je autoritou technickej geometrie.

---

## A. Všeobecné konvencie

### Fyzická geometria / Physical geometry

CAD-neutrálna 3D/2D geometria prvkov v milimetroch: pozície stredníc, horných/dolných plôch, zapustenia a strešnej roviny. Autorita: `RoofRafterPhysicalGeometry`, `RoofAutomaticPurlinPlanner`, `RoofAutomaticPurlinRoofPlaneRules`, `RoofPurlinPhysicalPlacement`, `RoofPurlinElevationProfile`.

### Technická výška / Technical elevation

Relatívna architektonická výška prvku (SH / Os / HH / Strešná rovina) voči zvolenému dátumu. Persisted a počítaná v **mm**; zobrazená v **metroch** s tromi desatinnými miestami cez `RoofRelativeElevationDatumRules.FormatMetres`.

### Strešne-lokálne súradnice / Roof-local coordinates

Lokálny rám strechy používaný plannerom. Horná plocha krokvy pri zdrojovom odkvape (`SourceEavePlane`) má autoritatívne **ReferenceLocalZMm = 0**. Osa Z rastie nahor.

### Svetové súradnice / World coordinates / WCS

Súradnice hostiteľského DWG (AutoCAD WCS). Persistované segmenty plánu (`RoofSegment3D`) majú XY v topologickom rámci strechy a Z = fyzická strednica prvku (`PurlinCenterLocalZMm` / centerline).

### Relatívna výška / Relative elevation

`RelativeZ = ReferenceRelativeZ + PhysicalLocalZ − ReferenceLocalZ`  
Implementácia: `RoofRelativeElevationDatumRules.ToRelativeElevationMm`  
Polia: `*RelativeElevationMm` na `RoofPurlinElevationProfile` / `RoofPurlinPhysicalPlacement`.

### Referenčná výška / Reference elevation

Dátum: `RoofRelativeElevationDatum` (`ReferenceKind`, `ReferenceRelativeElevationMm`, `ReferenceLocalZMm`).

- `ReferenceLocalZMm` — lokálne Z referenčnej roviny v strešnom rámci.
- `ReferenceRelativeElevationMm` — architektonická relatívna výška **priradená** tej rovine (nie to isté ako LocalZ).

Určuje, čo znamená „nula“ zobrazenia a ako sa mapuje lokálne Z na relatívne mm.

### Schématické súradnice / Schematic coordinates

2D súradnice rezu v prezentácii (`AutomaticPurlinSectionPresentation`, `AutomaticPurlinSectionMemberMm.CenterXMm` / `CenterZMm`). Slúžia len na vykreslenie; **nie** na prepočítanie technických výšok.

### SVG master súradnice / SVG master coordinates

Master-space geometria šablóny rezu (`AutomaticPurlinSectionSvgTemplate`, master scene). Afinné mapovanie master → viewbox musí byť **rovnaké** pre krokvu aj kontakt zapustenia.

### Afinná transformácia / Affine transformation

Lineárne mapovanie schématických mm do SVG. Kontakt zapustenia sa interpoluje medzi transformovanými dolnými a hornými hranami krokvy v tom istom priestore. Nikdy nemiešať tip-offset member súradnicu s neoffsetovanou SVG hranou krokvy.

### Orientácia strany strechy / Roof-side orientation

V reze: `AutomaticPurlinSectionSide.Left` / `Right` / `Center`. Ľavá/pravá strana sklonu určuje vonkajší horný roh kontaktu (ľavý = −W/2, pravý = +W/2 od osi).

### Vodorovná pôdorysná vzdialenosť / Horizontal plan distance

Vstup režimov `PlanDistanceFromEave` / `PlanDistanceFromRidge`: vzdialenosť v **horizontálnom** pôdoryse (mm), nie pozdĺž sklonenej krokvy. Referencia umiestnenia je **os (axis)** prvku.

### Sklon strechy / Roof pitch

Autoritatívny interný parameter: `PitchDegrees` / `topology.PitchDegrees` (stupne, `(0, 90)`). Zobrazenie: napr. `CurrentRoofPitchText` („Aktuálny sklon strechy: 45,0°“).

### Percento sklonu / Roof slope percentage

Odvedená architektonická veličina `100 · tan(pitch)` (vzostup/rozpon). V module automatických väzníc **nie je** samostatný persistovaný vstup; výpočty používajú stupne. Nesmie sa zamieňať s percentom zapustenia (`PercentOfRafterHeight`).

### Jednotky a formátovanie

| Doména | Jednotka | Poznámka |
|--------|----------|----------|
| Interná geometria | mm | `*Mm` polia |
| Zobrazené výšky | m, 3 desatinné | `FormatMetres` |
| Nula po zaokrúhlení | `±0,000` / `±0.000` | nikdy `+0.000` / `-0.000` |
| Kladné / záporné | `+x,xxx` / `-x,xxx` | podľa kultúry (oddeľovač) |

**Zaokrúhlenie zobrazenia nesmie byť vstupom do fyzických výpočtov.**

Rozlišuj vždy:

- fyzické súradnice (mm, lokálne / WCS),
- relatívne technické hodnoty (mm → formátované m),
- vizuálne SVG / schématické súradnice.

---

## B. Definície vodorovných drevených prvkov

Platí pre `WallPlate` (`RoofAutomaticPurlinGeneratorRole.WallPlate`), `Intermediate` / väznica (`Intermediate`), `Ridge` / hrebeňová väznica (`Ridge`) a ďalšie vodorovné prvky s rovnakou profilovou logikou.

| SK | EN / kód | Definícia |
|----|----------|-----------|
| SH / Spodná | Bottom / `BottomLocalZMm`, `BottomRelativeElevationMm` | Fyzická spodná vodorovná plocha |
| Os | Center / `CenterLocalZMm`, `CenterRelativeElevationMm` | Geometrický stred v polovici výšky |
| HH / Horná | Top / `TopLocalZMm`, `TopRelativeElevationMm` | Fyzická horná vodorovná plocha |
| H / Výška | Height / `HeightMm` | Vertikálny rozmer prierezu |
| W / Šírka | Width / `WidthMm` | Horizontálny rozmer priečne na pozdĺžnu os |

**Invariant:**

```
CenterZ = BottomZ + H/2
TopZ    = BottomZ + H
```

Implementácia: `RoofPurlinElevationProfile`, `RoofRafterPhysicalGeometry.CreatePurlinPlacement` (`PurlinBottom*`, `PurlinCenter*`, `PurlinTop*`).

**Zákaz:** CenterZ zo schématického obdĺžnika SVG **nie je** autoritatívne fyzické `CenterLocalZMm`.

---

## C. Definície krokvy

| SK | EN / kód | Definícia |
|----|----------|-----------|
| Horná plocha krokvy | Physical upper rafter face / `UpperSurfacePoint`, `RafterUpperSurfaceLocalZMm` | Matematická horná plocha; v aktuálnom modeli autoritatívna strešná plocha |
| Dolná plocha krokvy | Physical lower rafter face / `LowerSurfacePoint`, `RafterLowerSurfaceLocalZMm` | Dolná plocha prierezu |
| Strednica krokvy | Rafter centerline / `CenterlinePoint`, `RafterCenterLocalZMm` | Ťažisko prierezu pri rovnakom XY; **nie** `RoofPlane` |
| Výška krokvy | Rafter height / `HeightMm`, `RafterHeightMm` | Meraná **kolmo** na os/plochu krokvy |
| Sklon | Roof pitch / `PitchDegrees` | Uhol strešnej plochy voči horizontále |
| Ľavý / pravý sklon | Left / right slope | `AutomaticPurlinSectionSide.Left` / `Right` |
| Kontaktná stanica | Contact station | Horizontálna poloha vonkajšieho horného rohu sedenia |
| Vonkajší horný roh | Outer top contact corner | Downslope horný roh prierezu |

**Kontaktná stanica (horizontálne):**

- Odstup vonkajšieho kontaktného rohu od **osi** prvku je **W/2**.
- Znamienko určuje orientácia strany strechy (`Left` → −W/2, `Right` → +W/2; Ridge používa oba rohy).
- S tým spojený **vertikálny** rozdiel výšky hornej plochy krokvy medzi osou a vonkajším rohom je `(W/2) · tan(pitch)`.
- **Násobenie samotnej horizontálnej stanice** (pôdorysná vzdialenosť osi / plan distance) faktorom `tan(pitch)` **nie je** vzorec kontaktnej stanice — `d · tan(pitch)` patrí len do plan-distance umiestnenia osi (kapitola G).

`RoofRafterPhysicalGeometry`: pri pevnej horizontálnej stanici je vertikálne rozpätie kolmej hrúbky `height / cos(pitch) = height / n_z`.

**Horná plocha krokvy je autoritatívny fyzický povrch strechy.** Strednica krokvy ≠ Strešná rovina / RoofPlane.

---

## D. Strešná rovina / RoofPlane

Rozlišuj tri vrstvy:

| Pojem | Význam |
|-------|--------|
| Physical roof surface | Fyzická **horná** plocha krokvy (`UpperSurfacePoint` / upper rafter face) |
| Technical RoofPlane elevation | Z-výška tej fyzickej plochy na **definovanej meracej stanici**, vyjadrená lokálne alebo relatívne voči dátumu |
| Visual SVG roof line | Schématická čiara sklonu — **iba prezentácia**, nikdy geometry authority |

Autorita výpočtu technickej RoofPlane: `RoofAutomaticPurlinRoofPlaneRules` (odvodené z fyziky, **nikdy** z SVG/afinných hrán).

### WallPlate

Meracia stanica = **zvislá os** pomúrnice. Technická RoofPlane = horná plocha krokvy na tej osi.

Od hornej plochy pomúrnice (`Top`, ktorá sedí na vonkajšom kontakte) na os:

```
RoofPlane = Top + (W/2)·tan(pitch) + (H − D)/cos(pitch)
```

kde `(W/2)·tan(pitch)` je vertikálny vzostup od vonkajšieho rohu k osi, `D` = zapustenie, `H` = výška krokvy.  
Metóda: `TryResolveMemberRoofPlaneRelativeMm` / `TryResolveFromPlanItem`.

### IntermediatePurlin

Rovnaká členová formula; meracia stanica = **zvislá os** väznice.

### Ridge

Technická RoofPlane = **priesečník ľavej a pravej** fyzickej hornej plochy krokvy (apex).

Implementácia používa **tú istú členovú formulu** (`TryResolveRidgeRoofPlaneRelativeMm` → `TryResolveMemberRoofPlaneRelativeMm`) a komentár v kóde potvrdzuje ekvivalenciu s priesečníkom, keď `Top` je výška vonkajšieho sedenia.

**Zákaz:** aproximácia `Center + (H_rafter/2)/cos(pitch)` (alebo „center + half rafter height“). Overené testom `Ridge_UsesSameMemberFormula_NotCenterlineHalfOverCos`.

### Ďalšie rozlíšenie

| Pojem | Význam |
|-------|--------|
| Rafter centerline | Strednica krokvy — **nie** RoofPlane |
| Member axis RoofPlane (WP / Intermediate) | Upper face na zvislej osi prvku |
| Ridge apex RoofPlane | Priesečník L/R upper faces |

Vizuálne umiestnenie **nesmie** redefinovať RoofPlane. Technické hodnoty v dialógu musia sledovať akceptovanú fyziku / Core, nie SVG (HOST: `+0.343`, nie historické SVG `≈+0.331`).

---

## E. Referenčné dátumy

Typ: `RoofRelativeElevationReferenceKind` + `RoofRelativeElevationDatum`.

### 1. `SourceEavePlane` (Source eave)

- Lokálny dátum = fyzická **horná** plocha krokvy pri definovanom zdrojovom odkvape.
- Autoritatívne `ReferenceLocalZMm = 0`.
- `ReferenceRelativeElevationMm` môže byť ľubovoľné konečné (vrátane nuly).

**Nekonzistentný persistovaný SourceEave:** ak `ReferenceKind == SourceEavePlane` a `ReferenceLocalZMm ≠ 0`, validácia **fail-closed** (`InconsistentSourceEaveLocalZ`) — **bez tichej normalizácie**. Host load nesmie XData prepísať; dialóg seeduje bezpečný draft `SourceEave(0, Rel)` a vyžaduje explicitné vyriešenie (`IsInconsistentSourceEaveLocalZ`, `AutomaticPurlinReferenceDatumSwitchTests`).

### 2. `ExplicitLocalPlane`

- Používateľ zadá `ReferenceLocalZMm` a `ReferenceRelativeElevationMm`.
- Editable „Lokálna poloha“ len pre tento kind.
- Zadaná LocalZ sa **nesmie** nahradiť zastaraným cache iného kindu; Explicit LocalZ sa uchováva oddelene (`_explicitReferenceLocalZMm`) a pri návrate na Explicit sa obnoví.

### 3. `WallPlateBottom`

- Dátum = skutočný **fyzický spodok** vyriešenej pomúrnice (`BottomLocalZMm`), nie os, horná plocha ani vizuálny obdĺžnik.
- `RoofAutomaticPurlinPlanner.ResolveEffectiveDatum`: pri `WallPlatesEnabled` najprv bootstrap umiestnenie voči `SourceEavePlane(0,0)`, výsledný spodok → `ReferenceLocalZMm`; `ReferenceRelativeElevationMm` zostáva z požiadavky.
- Relatívne výšky sa následne rebase-ujú na vyriešený dátum.

### Všeobecná konverzia

```
RelativeZ_mm = ReferenceRelativeElevationMm
             + PhysicalLocalZ_mm
             − ReferenceLocalZMm
```

Všetky tri veličiny v **mm**. Inverzia: `ToLocalZMm`.

### Prepínanie dátumov (aktuálne správanie)

- Prepínač kindu mení sémantiku LocalZ; SourceEave **vždy** núti LocalZ = 0 (ignoruje skrytý/stale text).
- Explicit ↔ WallPlateBottom ↔ SourceEave: per-kind LocalZ sémantika sa zachováva bez Apply (WPF testy).
- Fyzická montáž pri rovnakom layoute je pri SourceEave a WallPlateBottom **identická**; menia sa len relatívne hodnoty (offset = fyzický spodok WP nad SourceEave).

### OPEN — WallPlateBottom bez pomúrnice

Ak `ReferenceKind == WallPlateBottom` a `WallPlatesEnabled == false`, `ResolveEffectiveDatum` aktuálne **neanchoruje** a vráti validovaný requested datum (passthrough LocalZ). UI pri tomto stave nevykresľuje referenčnú rovinu WallPlateBottom. **Produktové pravidlo nie je definitívne uzavreté** — pozri kapitolu K.

---

## F. Zapustenie krokvy / Seating / Recess

**Zapustenie / SeatingDepth** (`RoofAutomaticPurlinSeatingDepth`, `SeatingDepthMm`):

- Merané **kolmo** od fyzickej **dolnej** plochy krokvy smerom k **hornej**.
- Režimy: `PercentOfRafterHeight`, `AbsoluteMm` (`RoofAutomaticPurlinSeatingDepthMode`).
- Percento: `Depth = RafterHeight · Percentage / 100` (0…100 inkluzívne).

Pre `H = 125 mm`:

| % | Depth |
|---|-------|
| 0% | 0.00 mm |
| 25% | 31.25 mm |
| 50% | 62.50 mm |
| 75% | 93.75 mm |
| 100% | 125.00 mm |

Význam na kontaktnej stanici (`CreatePurlinPlacement`):

- **0%:** horná plocha prvku (`Top`) na dolnej ploche krokvy — dolná plocha krokvy dosiahne úroveň sedenia.
- **100%:** `Top` na hornej ploche krokvy.

Produktový default WallPlate/plan-distance: `DefaultWallPlateSeatingPercent = 25`.

### Kontaktná stanica

Pre WallPlate a Intermediate (a Ridge v tom istom fyzickom sedení): kontakt = **fyzický vonkajší horný roh** prvku (downslope).

- Horizontálny odstup od osi: **W/2** (znamienko podľa strany; pozri C).
- Vertikálny rozdiel hornej plochy medzi osou a vonkajším rohom: `(W/2) · tan(pitch)`.
- `CreatePurlinPlacement` pri sedení používa hornú plochu na vonkajšej stanici; ekvivalentne môže pri rovnakom XY znížiť Z o `(W/2)·tan(pitch)` voči hodnote na osi (implementačný tvar — **nie** `planDistance · tan(pitch)`).

**Ridge — vizuálny kontakt (prezentácia):** ľavý vonkajší roh (−W/2) a pravý (+W/2); `TryResolveCornerContactTopZMm` berie obe strany. Fyzický apex / RoofPlane ostáva priesečníkom L/R upper faces podľa Core.

Samostatný fyzický strešný apex (priesečník ľavej/pravej hornej plochy) = Ridge RoofPlane; **nie** je to strednica hrebeňového dreva.

### Vertikálne oddelenie pri pevnej horizontálnej stanici

```
(RafterHeight − SeatingDepth) / cos(RoofPitch)
```

alebo ekvivalentne `/ n_z`.

**Platí** pri pevnej horizontálnej XY stanici na prevod kolmej hrúbky na vertikálne Z (zvyškové pokrytie nad `Top`, inverzia BottomEdge, RoofPlane formula).

**Neplatí** ako „posun kolmo na plochu“ v zmysle zmeny XY, ani s prevráteným `cos` / `1/cos` na nesprávnom mieste.

---

## G. Režimy umiestnenia / Placement modes

Enum: `RoofAutomaticPurlinPlacementMode`

| Identifikátor | Význam |
|---------------|--------|
| `PlanDistanceFromEave` | Pôdorysná vzdialenosť od zdrojového odkvapu; os prvku; `Z_surface = d · tan(pitch)` |
| `PlanDistanceFromRidge` | Pôdorysná vzdialenosť od referenčného hrebeňa (kde podporované); `Z = Z_ridge − d · tan(pitch)` |
| `BottomEdgeHeightAboveReference` | Požadovaný **offset spodku nad zvoleným dátumom** (`PlacementValueMm`) |

Ďalšie relevantné automatické / generované umiestnenia v module:

- Wall plate set: `WallPlateEnabled` + `WallPlatePlacement` (shared layout item).
- Ridge set: `RidgeEnabled` + `RidgeWidthMm` / `RidgeHeightMm` / `RidgeSeatingDepth`.
- Intermediate riadky: `RoofAutomaticPurlinLayoutItem` so stabilným `LayoutItemId`.

### Plan-distance

- Vzdialenosť je **horizontálna**.
- Referencia = **os (axis)** prvku.
- **Distance 0 je podporovaná** (`PlanDistanceFromEave_Zero_IsValidAlongSourceEave`): os na odkvape; vonkajší roh môže prečnievať o **polovicu šírky**.
- Exkluzívna horná medza (strecha-špecifická):

```
maxExclusive = RiseMm / tan(pitch)
```

`TryResolvePlanDistanceFromEaveExclusiveMaxMm`; platné `d` spĺňa `0 ≤ d < maxExclusive` a `d·tan(pitch) < RiseMm`. Pri `d ≥ maxExclusive` → `ElevationOutsideRoof`.

**Nikdy** neuvádzať 1100 mm (ani inú konštantu) ako všeobecné maximum — 1100 mm je len príklad v testoch pre konkrétnu geometriu `4000×3000 @ 45°`.

### BottomEdge (`BottomEdgeHeightAboveReference`)

Aktuálna implementácia (`RoofAutomaticPurlinPlanner.ResolvePlacement`):

```
BottomLocalZMm = ReferenceLocalZMm + PlacementValueMm
```

Rozlíšenie polí dátumu vs. placement:

| Pole | Význam |
|------|--------|
| `ReferenceLocalZMm` | Lokálne Z referenčnej roviny v strešnom rámci (mm) |
| `ReferenceRelativeElevationMm` | Architektonická relatívna výška **priradená** tej referenčnej rovine (mm) |
| `PlacementValueMm` | **Podpísaný offset** fyzického spodku voči `ReferenceLocalZMm` (mm). Kladné = nad dátumom, záporné = pod dátumom. Nie je to samostatný „absolútny“ display string. |

Dôsledok pre zobrazenú relatívnu výšku spodku:

```
BottomRelativeElevationMm = ReferenceRelativeElevationMm + PlacementValueMm
```

Príklad (elevovaný ExplicitLocalPlane):

```
ReferenceLocalZMm = 1000
ReferenceRelativeElevationMm = 0
PlacementValueMm = -200
→ PhysicalBottomLocalZ = 800
→ DisplayedBottomRelativeZ = -200
```

Záporný vstup **nie je** záporná fyzická elevácia — je to offset voči zvolenému dátumu. Fyzické medze strechy (`ValidateElevation` na slice LocalZ, seating, pitch, hranice) ostávajú autoritou; UI/persistencia **nesmú** zamietnuť platný záporný offset iba preto, že je číselne &lt; 0.

- Keď `ReferenceRelativeElevationMm = 0` (bežné SourceEave / WallPlateBottom fixtúry), `PlacementValueMm` **číselne súhlasí** so zobrazeným Bottom relative (v mm pred formátovaním na metre).
- Keď `ReferenceRelativeElevationMm ≠ 0`, `PlacementValueMm` **nie je** priamo absolútna zobrazená relatívna výška; je to offset nad/pod dátumom. Produktová sémantika sa týmto **nemení** — len sa dokumentuje.

Ďalšie pravidlá:

- Po umiestnení musí skutočný fyzický spodok zodpovedať tomuto `BottomLocalZMm`.
- So seating: `TryResolveUpperFaceLocalZFromSeatedBottom` odvodí hornú plochu na osi; **nesmie** sa podvrhnúť strednica namiesto hornej plochy (HOST regresia Bottom ≈ −233 mm).

OPEN: sémantika BottomEdge voči samotnému dátumu `WallPlateBottom` (okrem overeného prípadu requested 0 → Bottom_rel = 0) — pozri K.

---

## H. Prezentácia vs. fyzická geometria

### Core je autorita pre

- fyzické pozície prvkov,
- fyzické zapustenie,
- Bottom / Center / Top,
- RoofPlane,
- rozlíšenie dátumu,
- persistovanú geometriu (`RoofAutomaticPurlinLayout`, datum XData, generated plan).

Kľúčové typy: `RoofPurlinElevationProfile`, `RoofPurlinPhysicalPlacement`, planner, roof-plane rules.

### WPF / SVG je zodpovedné za

- vykreslenie vypočítanej fyziky,
- mapovanie do schématických súradníc,
- vizuálne zapustenie a kontakty,
- labely a referenčné vodiace čiary,
- tooltipy a aktuálne hodnoty.

Triedy: `AutomaticPurlinSectionPresentation`, `AutomaticPurlinSectionSvgTemplate`, `AutomaticPurlinDialogViewModel`, `AutomaticPurlinElevationTooltipCatalog`.

**Schéma nie je geometry solver.**

## H1. Schválený kontrakt 2D/3D obyčajných automatických krokiev

Táto kapitola je normatívna pre `ordinary automatic rafters` / obyčajné automatické krokvy.
Jedna krokva je jeden logický člen s jednou identitou a spoločnými dátami. Z člena vznikajú
dve nezávislé CAD reprezentácie:

- `2D representation` — pôdorysná os, prípadne voliteľný pôdorysný obrys,
- `3D representation` — fyzické jednoduché hranolové teleso.

2D reprezentácia sa nesmie vytvárať presunom 3D telesa na `Z=0` a 3D reprezentácia sa
nesmie vytvárať transformáciou 2D čiary. 2D nesmie spätne generovať 3D a 3D nesmie
spätne generovať 2D. Obe reprezentácie zdieľajú minimálne vlastníka strechy, identitu
člena, rolu, šírku, výšku, sklon, fyzickú dĺžku a logickú/referenčnú geometriu.

### H1.1 Nezmeniteľné 2D invarianty

Pre každú obyčajnú automatickú krokvu platí:

```text
2D rafter Z = 0
AxisEaveEndpoint = roof-plane eave boundary
```

Koniec 2D osi pri okape je vždy hrana strešnej roviny / hranica okapu. Nesmie byť
určený fyzickým čelom 3D hranola. Zmena spodného rezu, hrebeňového spoja, fyzickej
dĺžky alebo absolútnej výšky 3D telesa nesmie posunúť, skrátiť, predĺžiť ani inak
zmeniť pôdorysnú 2D os. Táto väzba platí aj pri prepínaní zobrazenia `2D`, `3D`
a `2D + 3D`.

### H1.2 Autoritatívna 3D strešná rovina a okap

Autoritatívne platí:

```text
top face of 3D rafter = roof plane
3D eave top edge = roof eave boundary
```

Horná plocha krokvy patrí strešnej rovine; strednica krokvy ju nenahrádza. Pri okape
končí horná hrana 3D telesa presne na hrane okapu. Od tohto horného referenčného bodu
sa konštruuje fyzické čelo podľa `LowerEndCutMode`. Jednoduché hranoly sa môžu
geometricky prekrývať; pri okape a hrebeni sa v tejto etape nepoužíva zapustenie,
birdsmouth, notch ani výrobný tesársky spoj. Výnimkou je schválený fyzický
Hip/Valley koncový rez podľa H1.7.

### H1.3 Spodné čelo — `LowerEndCutMode`

Nastavenie je na úrovni automatických krokiev / strechy. Stable enum hodnoty a ich
význam sú:

| Hodnota | Slovenské UI | Definícia |
|---|---|---|
| `Vertical` | Zvislo | Čelo je zvislé voči globálnej osi Z. |
| `Perpendicular` | Kolmo | Čelo je kolmé na pozdĺžny smer krokvy; `Ncut = D`, kde `D` je normalizovaný smer krokvy. |
| `Horizontal` | Vodorovne | Čelo je vodorovné; `Ncut = GlobalZ`. |

Default je `LowerEndCutMode = Vertical`. Vo všetkých troch režimoch zostáva horný
okapový referenčný bod rovnaký a platí:

```text
LowerEndCutMode does not modify 2D geometry
```

### H1.4 Hrebeň — `RidgeJoinMode`

`RidgeJoinMode` je samostatné nastavenie a nesmie používať `LowerEndCutMode`.

| Hodnota | Slovenské UI | Definícia |
|---|---|---|
| `Meet` | Kolmo k sebe | Obe krokvy končia na spoločnej hrebeňovej deliacej rovine; bez medzery a bez overlapu. |
| `Overlap` | Vzájomné prekrytie | Obe samostatné 3D telesá sa môžu symetricky prekrývať v oblasti hrebeňa. |

Default je `RidgeJoinMode = Meet`. Pri oboch hodnotách sa nepoužíva `union`,
`subtract`, `boolean cut` ani orezanie jednej krokvy podľa druhej. Nesmie existovať
`first/second`, `master/slave` ani ľavá/pravá priorita krokvy. Hrebeňový režim nesmie
meniť 2D os:

```text
RidgeJoinMode does not modify 2D geometry
```

Pri `Overlap` sa fyzický presah každej ordinary krokvy rieši podľa jej skutočnej
3D osi a výšky; prípadný materiál nad skutočnou susednou strešnou rovinou sa
odstráni rovinným rezom podľa topology, nie podľa druhého `Solid3d`. Obe telesá
zostávajú samostatné, ich horné plochy ležia na vlastných roof planes a 45°
nie je špeciálny konštrukčný prípad. `Meet` zostáva nezmenený.

Pri sklone pod 45° nesmie pôvodné spodné hrebeňové čelo ostať pod protiľahlou
strešnou rovinou ako trojuholníkový hrot. Dočasný zdrojový hranol každej
krokvy sa najprv predĺži po jej skutočnej 3D osi aspoň po prienik **oboch**
spodných pozdĺžnych hrebeňových hrán s protiľahlou roof plane; potrebná dĺžka
sa vypočíta z prieniku priamky a roviny, nie z konštanty ani z 45° predpokladu.
Až potom sa zdrojový hranol oreže protiľahlou roof plane. Tento fyzický presah
nemení 2D os, member identity ani režim `Meet`.

### H1.5 Vlastníctvo a zobrazenie

2D aj 3D reprezentácia patria rovnakému logickému členovi a z oboch musí byť možné
zistiť minimálne `RoofOwnerId`, `MemberId` a `MemberRole`. Používa sa existujúci
ownership / metadata mechanizmus projektu; nový konkurenčný ownership systém sa
nezavádza.

Viditeľnosť je prezentačný stav bez zmeny modelu, metadata alebo dĺžok:

```text
2D       -> 2D visible, 3D hidden
3D       -> 2D hidden, 3D visible
2D + 3D  -> both visible
```

### H1.6 Priorita kontraktu

Pri práci na 2D/3D obyčajných automatických krokvách sú záväzné najmä tieto
invarianty:

1. 2D os má vždy `Z = 0`.
2. Koniec 2D osi pri okape je hranica strešnej roviny / okapu.
3. Koniec osi nie je definovaný fyzickým koncom 3D telesa.
4. Horná plocha 3D krokvy patrí strešnej rovine.
5. Horná hrana 3D krokvy pri okape končí na hrane okapu.
6. `LowerEndCutMode` nemení 2D geometriu.
7. `RidgeJoinMode` nemení 2D geometriu.

Schválenie tohto kontraktu nemení samo osebe stav implementácie ani nepredstavuje
HOST PASS. Pri rozpore medzi týmto kontraktom, schválenou fyzickou definíciou,
akceptovanými Core testami a overením v AutoCADe agent zastaví prácu a nahlási rozpor.

### H1.7 Physical ordinary end at Hip/Valley

Canonical 2D ordinary endpoint zostáva na osi topologického Hip/Valley prvku,
vždy v `Z=0`; handle, owner, MemberId, logical key a station sa nemenia. Iba 3D
ordinary teleso končí na bočnej ploche structural timberu. Structural Hip/Valley
zostáva samostatným členom a nemusí byť `Solid3d`.

Autoritatívny model bočnej plochy je *plumb side*: šírka konkrétneho Hip/Valley
člena je meraná v XY. Z jeho 3D osi sa normalizuje pôdorysný smer `Dxy`; horizontálny
priečny vektor `W = (-Dxy.Y, Dxy.X, 0)`. Obe bočné plochy sú zvislé WCS roviny
vo vzdialenosti `actualStructuralWidth/2` od structural osi v XY. Vyberie sa tá,
ktorá smeruje k ordinary členovi; 3D ordinary hranol sa prereže touto rovinou a
zachová sa časť mimo structural timberu. Skrátenie po ordinary osi nie je všeobecne
`width/2`, ale závisí od uhla napojenia. Pri neplatnom alebo degenerovanom prieniku
sa nevytvorí neorezaný náhradný solid.

Hip/Valley rez sa nevzťahuje na RidgeJoinMode ani LowerEndCutMode; tieto režimy
zostávajú nezávislé. Geometrickú pravdu rezu počíta Core; AutoCAD môže použiť
`Solid3d.Slice` len na materializáciu už vyriešenej Core geometrie. Podporované
Valley topológie existujúceho solvera smú materializovať ordinary solids aj tam,
kde zatiaľ nie sú podporované fyzické roof-surface Faces/Edges. Toto nie je
rozšírenie solvera ani AutoCAD HOST PASS.

### H1.8 Physical structural Hip/Valley timber

Existujúca generated structural `Line` zostáva logickou/reference reprezentáciou
toho istého člena, ale jej oba koncové body sú XY projekcia v `Z=0`. Fyzický
`StructuralRafterSolid` je samostatný owned `Solid3d` pod rovnakým roof ownerom
a structural key. Fyzická horná os používa resolved eave elevation a roof
topology, nikdy Z referenčnej `Line`. Display role Hip `200–203` sú schematické
strešné hrany, nie tretie fyzické timber telesá. Pri Hip je stredná horná
hrana prienikom susedných strešných rovín a obe horné fazety ležia na svojich
roof planes. Boky sú zvislé WCS roviny v XY offsete `StructuralWidthMm/2`,
presne totožné s rovinami, na ktoré sa režú ordinary solids. 2D ordinary osi
zostávajú na Hip/Valley centerline a nemenia sa podľa fyzického konca.

Default Hip šírka je 120 mm, ale autoritatívna hodnota je owner-scoped persisted
structural width. Default výška je `Automatic`; dialóg zobrazuje aktuálne
vypočítané číselné mm zaokrúhlené na jedno desatinné miesto podľa jazyka, bez
zmeny režimu. Ručná úprava čísla prepína na `Explicit`; tlačidlo „Automaticky“
režim a výpočet obnoví. Vypočítaná výška vychádza z maxima skutočných zvislých rozpätí
pripojených ordinary physical cut-faces a zohľadňuje úbytok výšky na hornom
roof-plane bevel. Explicitná výška zostáva explicitná; ak nestačí na odporúčaný
kontakt, používateľ dostane upozornenie, hodnota sa bez súhlasu neprepíše.

Fyzický koniec pri okape sa zreže dvoma priľahlými zvislými eave boundary
rovinami a zostáva v roof footprint. Pri `Vertical` a `Perpendicular` sa
konštrukčný 3D hranol pred týmito rezmi predĺži za canonical eave corner;
obe fyzické strany tak končia na
príslušných okapových hraniciach a horná stredová hrana siaha do ich
priesečníka. Logická Hip os sa tým neskracuje. Pri
hrebeni prechádza horná stredová hrana fyzického timberu kanonickým structural
uzlom bez Boolean union s Ridge. Pri presne dvoch kompatibilných structural členoch so spoločným
horným topologickým uzlom sa oba fyzické hranoly prerežú tou istou zvislou
WCS miter rovinou určenou osami v XY; ponechajú sa opačné polpriestory. Toto
nemení logické osi, 2D referencie ani eave clip. Rovnaký Core resolver je
použiteľný pre Hip/Valley aj Valley/Valley bez syntetického vytvárania Valley.
Rovnaký Core builder môže obslúžiť Valley len pri existujúcej
jednoznačnej topology/structural identity; chýbajúci Valley sa nevymýšľa.
Po oboch eave pôdorysných rezoch sa na fyzický Hip/Valley timber aplikuje
rovnaký owner-scoped `LowerEndCutMode` ako na ordinary krokvy. `Vertical` drží
čelo zvislé vo WCS, `Perpendicular` ho reže kolmo na skutočný pozdĺžny 3D smer
a `Horizontal` používa vodorovnú WCS rovinu v resolved physical eave elevation
(nie `Z=0`). Pri `Vertical` a `Perpendicular` sú dva nezávislé rezy:
prvá rovina určuje čelo (zvislé WCS alebo kolmé na skutočnú 3D pozdĺžnu os),
druhá je vodorovná WCS spodná rezová rovina s normálou `GlobalZ`.
Jej výška `OrdinaryLowestZ` je najnižší globálny Z bod celého finálneho
fyzického ordinary hranola, nie iba jeho spodnej kontaktnej hrany pri Hip.
Z každej strany sa vyberie geometricky najbližšia relevantná ordinary krokva
pri okape a z ich fyzických telies sa použije minimum Z,
nie priemer, horná hrana, kontaktná hrana ani projekcia na Hip os.
Orientácia čelnej roviny vyplýva iba z `LowerEndCutMode`; jej pozdĺžna poloha
je viazaná na canonical eave corner, nikdy na ordinary contact, projekciu
`OrdinaryLowestZ` na Hip os ani na priesečník so spodným rezom. Okapové
boundary planes určujú pôdorysný dosah nezávisle od spodnej WCS výšky.
V špičke rohu môže byť prienik čelnej roviny s oboma okapovými polpriestormi
iba hrana alebo bod; konečná plocha čela by nevyhnutne skrátila pôdorysný
dosah pred canonical corner. Normála čelnej roviny stále zodpovedá režimu.
Fyzický Hip/Valley hranol
zachová polpriestor `Z >= OrdinaryLowestZ`; všetok materiál pod ním je odpad.
`Horizontal` zostáva jedinou vodorovnou WCS rovinou v resolved physical eave
elevation, bez redundantného druhého rezu.
Tento profilový rez nemení existujúce dva plan/eave klipy, ordinary telesá ani
2D osi. Pri V miteri sa každá fyzická polovica konštruuje až za stredový uzol
o geometricky potrebný presah podľa šírky a uhla skutočných osí; koniec sa
určí spoločnou miter rovinou, nie kolmým ridge klipom v uzle. Horná stredová
hrana dosahuje kanonický uzol a fyzické vrcholy sa prípadne orežú ďalšími
incidentnými roof planes, aby nič nepretŕčalo nad roof envelope. Implementácia
zostáva bez AutoCAD HOST
PASS, kým sa neoverí skutočný `Solid3d` v HOST.

### Povinné zákazy

- Oprava len vizuálu **nesmie** meniť technické výšky ani persistovanú geometriu bez samostatnej schválenej úlohy fyziky.
- `ElevationProfile.BottomLocalZMm` / `CenterLocalZMm` / `TopLocalZMm` sa **nesmú** prepísať z SVG seating / visual contact.
- Schématické `CenterZMm` je rendering; nie automaticky fyzické `CenterLocalZMm`.
- Labely z `ElevationProfile` (relatívne texty) sa pri vizuálnom posune obdĺžnika **neprepisujú**.

### PlanDistance / Ridge vs BottomEdge v prezentácii

- **PlanDistanceFromEave / PlanDistanceFromRidge / Ridge:** `ShouldAlignRectangleTopToVisualSeating` — horná hrana obdĺžnika sa zarovná na SVG vizuálny kontakt pri frakcii zapustenia (0 % = dolná nakreslená hrana, 100 % = horná), **rovnakou affinou** ako krokva; kontakt na correct outer corner.
- **BottomEdgeHeightAboveReference:** zachová tip-mapped fyzický profil tak, aby spodok ±0.000 sedel na referenčnej čiare; neprepisuje CenterZ na vizuálny seating.

Ak vizuálny kontakt a fyzická pozícia **nemôžu** byť súčasne splnené, agent **diagnostikuje a nahlási rozpor** — nesmie ho maskovať zmenou Core vzorcov.

WPF regresia: `AutomaticPurlinVisualSeatingContactTests` (vizuál + invariancia technického profilu).

---

## I. Technické referenčné príklady (golden)

Overené proti akceptovaným Core testom pred publikáciou ako normatívne.  
Fixtúra: pitch **45°**, rafter **125 mm**, WallPlate **140×140 mm**, seating **25 %**, kde uvedené.

### Príklad 1 — Plan distance 700 mm od SourceEave

Zdroj: `RoofAutomaticPurlinSourceEaveHostRegressionTests`, `RoofRafterPhysicalGeometryTests`.

**SourceEavePlane** (Rel = 0, LocalZ = 0) — tá istá fyzika:

| Veličina | mm (presne) | Display |
|----------|-------------|---------|
| Bottom | 357.4174785275226 | **+0.357 m** |
| Center | 427.4174785275226 | **+0.427 m** |
| Top | 497.4174785275226 | **+0.497 m** |
| RoofPlane | 700 | **+0.700 m** |

**WallPlateBottom** pre **tú istú** fyzickú montáž:

| Veličina | mm | Display |
|----------|-----|---------|
| Bottom | 0 | **±0.000 m** |
| Center | 70 | **+0.070 m** |
| Top | 140 | **+0.140 m** |
| RoofPlane | 342.5825214724774 | **+0.343 m** |

Fyzická RoofPlane nad WallPlateBottom: **342.582521… mm** → zobrazenie **+0.343 m**.

### Príklad 2 — WallPlateBottom = 0, seating sweep

Zdroj: `RoofAutomaticPurlinRoofPlaneRulesTests.HostWallPlate_SeatingSweep_RemainsConfirmed`.

Očakávané relatívne RoofPlane (45°, H=125, WP 140×140):

| Seating | Display |
|---------|---------|
| 0% | **+0.387 m** |
| 25% | **+0.343 m** |
| 50% | **+0.298 m** |
| 75% | **+0.254 m** |
| 100% | **+0.210 m** |

### Ďalšie potvrdené HOST/Core body (nie úplný katalóg)

- Ridge @ WallPlateBottom (WP plan 700, ridge 160×220, 25%): Bottom/Center/Top **+2.210 / +2.320 / +2.430 m**, RoofPlane **+2.643 m** (`RoofAutomaticPurlinRidgeHostRegressionTests`).
- BottomEdge @ SourceEave, requested 0, 25%: Bottom **±0.000**, RoofPlane **+0.343 m** (inverzia seating).

### Poznámka k verifikácii (oddelené statusy)

| Status | Význam | Čo **nie** je |
|--------|--------|----------------|
| Core regression PASS | Focused / unit / source-contract test v CI alebo lokálne | HOST AutoCAD retest |
| HOST physical elevation PASS | Overené meranie fyzických výšok v AutoCAD | Vizuálny SVG PASS; samotný názov `*HostRegression*` |
| HOST schematic visual PASS | Overený vzhľad schémy / kontaktov v AutoCAD | Fyzická elevation PASS |
| HOST retest pending | AutoCAD overenie ešte nebolo (znovu) vykonané | Implicitný PASS z Core testov |

Testy pomenované `*HostRegression*` môžu **zakódovať** HOST-odvodené golden čísla do Core — to je Core PASS nad tými hodnotami, **nie** automatický dôkaz, že aktuálny AutoCAD HOST retest práve prebehol.

Vizuálny PASS neospravedlňuje zmenu Core výšok; fyzický PASS neznamená automaticky vizuálny PASS.

---

## J. Preview a validačný kontrakt

| Pojem | Význam v kóde |
|-------|----------------|
| Draft configuration | Aktuálny stav dialógu (riadky, dátum, seating) pred Apply |
| Valid calculated layout | `RoofAutomaticPurlinPlanner.Create` → `IsValid` + `Plan` |
| Current invalid input | `_currentDraftIsValid == false`; `ValidationMessage` / field errors |
| Last valid preview | Posledný úspešný `_previewPlan` + `_sectionPresentation` + derived technical values |
| Apply gating | `CanApply` = ProductionEdit ∧ ¬ApplyInProgress ∧ `_currentDraftIsValid` ∧ `_previewPlan ≠ null` ∧ (items > 0 ∨ existing count > 0) |
| Preflight validation | `RoofAutomaticPurlinLiveRegenerationService.TryValidatePersistedLayoutForProposedGeometry` — dry-run, **bez** DB zápisu |
| Live regeneration | `RoofAutomaticPurlinLiveRegenerationService` — len pri persistovanom layoute; neotvára dialóg; neinventuje defaulty |

### Pravidlá

1. Jedno neplatné draft pole **nesmie** ticho zmazať nesúvisiace predtým platné technické informácie (retain last valid schematic + technical values).
2. **Implementované:** pri invalid draft sa zachová last-valid `_previewPlan` / schéma / derived technical values; `CanApply == false`; zobrazí sa `ValidationMessage`.
3. **OPEN (UX):** samostatný vizuálny „stale preview“ indikátor (chrome) nad rámec ValidationMessage + disabled Apply — pozri K; nie je rovnaký ako implementovaný retain/gating.
4. Apply ostáva vypnuté pre neplatnú aktuálnu konfiguráciu.
5. Neplatná pitch/elevation konfigurácia **nesmie** čiastočne mutovať DWG (preflight fail-closed pred editom strechy; Apply via `TryBeginApply` len pri `CanApply`).
6. Neoverené HOST správanie **neopisovať** ako už akceptované; `*HostRegression*` ≠ hotový HOST retest.

`CanPreview` môže ostať true pri invalid draft (posledný platný plan ešte existuje) — to **nie** je súhlas s Apply.

---

## K. Rozsah, otvorené rozhodnutia a change policy

### Aktuálne akceptovaný rozsah (2D automatic purlin)

- Hip (a podporované) strechy s uniform pitch v existujúcom topologickom solveri.
- Generovanie WallPlate / Intermediate / Ridge centerline plánu.
- Tri dátumy: `SourceEavePlane`, `ExplicitLocalPlane`, `WallPlateBottom` (s WP enabled).
- Placement: `PlanDistanceFromEave`, `PlanDistanceFromRidge`, `BottomEdgeHeightAboveReference`.
- Seating percent / absolute; outer-corner physical seating; RoofPlane formula vyššie.
- Schématický 2D rez ako **prezentácia** Core výsledkov.

### Budúce 3D správanie mimo schváleného kontraktu obyčajných krokiev

- Ďalšia plná 3D XYZ autorita pre prvky mimo obyčajných automatických krokiev a
  mimo pravidiel H1.
- Ďalšie host-only 3D inspection semantics.

Tieto rozšírenia zostávajú **FUTURE / NON-NORMATIVE**. Kapitola H1 je pre obyčajné
automatické krokvy normatívna a nie je týmto otvoreným rozsahom zrušená.

### OPEN DECISIONS (nezavádzať ticho)

1. **WallPlateBottom bez WallPlate** — passthrough vs. fail-closed vs. zákaz voľby.
2. **BottomEdge voči WallPlateBottom** — úplná produktová sémantika mimo overených nula/offset prípadov.
3. **Budúca 3D XYZ model authority**.
4. **Ďalšie seating endpoint restrikcie** nepotvrdené aktuálnymi testami (napr. tvrdé limity mimo 0…H).
5. Úplný vizuálny „stale preview“ chrome (oddelený od už implementovaného retain last-valid + ValidationMessage + disabled Apply).

### Change-control

1. Zmena **fyzického** geometrického kontraktu vyžaduje: samostatne schválenú úlohu, aktualizované focused Core testy, HOST verifikáciu výšok.
2. Úloha **len prezentácie** musí preukázať **numerickú invarianciu** technických výšok (ElevationProfile / RoofPlane / datum) voči predchádzajúcemu Core výsledku.
3. Tento súbor meniť len v rámci dokumentačnej / kontraktovej úlohy; pri zmene fyziky aktualizovať golden príklady podľa nových akceptovaných testov.
4. Necommittovať / nepushovať WIP bez explicitnej žiadosti používateľa.

---

## Mapovanie na zdrojové súbory (orientácia)

| Oblasť | Typ / súbor |
|--------|-------------|
| Fyzika krokvy / seating | `RoofRafterPhysicalGeometry`, `RoofPurlinPhysicalPlacement` |
| Planner / placement | `RoofAutomaticPurlinPlanner`, `RoofAutomaticPurlinPlacementMode` |
| RoofPlane | `RoofAutomaticPurlinRoofPlaneRules` |
| Dátum | `RoofRelativeElevationDatum`, `RoofRelativeElevationDatumRules` |
| Profil výšok | `RoofPurlinElevationProfile` |
| Layout persist | `RoofAutomaticPurlinLayout`, `RoofPurlinLayoutPersistenceRules` |
| UI draft / Apply | `AutomaticPurlinDialogViewModel` |
| Schéma | `AutomaticPurlinSectionPresentation`, `AutomaticPurlinSectionSvgTemplate` |
| Live / preflight | `RoofAutomaticPurlinLiveRegenerationService` |
| Core golden | `RoofAutomaticPurlinSourceEaveHostRegressionTests`, `RoofAutomaticPurlinRoofPlaneRulesTests`, `RoofRafterPhysicalGeometryTests`, `RoofAutomaticPurlinRidgeHostRegressionTests`, `RoofAutomaticPurlinPlanDistanceBoundaryTests` |
| WPF golden | `AutomaticPurlinRoofPlaneTechnicalValueTests`, `AutomaticPurlinVisualSeatingContactTests`, `AutomaticPurlinReferenceDatumSwitchTests` |

---

*Koniec Geometrického slovníka KROVY v1.0*
