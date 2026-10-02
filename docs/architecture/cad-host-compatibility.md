# ACAD KROVY CAD Host Compatibility Contract

**Status:** NORMATIVE (repository SSOT for CAD host / runtime compatibility)
**Date:** 2026-10-02
**Path:** `docs/architecture/cad-host-compatibility.md`
**Evidence mode:** official vendor docs preferred; labels **VERIFIED** / **PROVISIONAL** / **OPEN**

---

## 1. Status / purpose

This contract defines how ACAD KROVY remains portable across CAD hosts and AutoCAD major runtime generations while keeping lifecycle, geometry, identity, Placement, and persistence semantics in a CAD-neutral Core.

It is normative for agents and humans when changing:

- target frameworks / multi-targeting
- CAD host API usage
- adapter boundaries
- Compatibility Gate policy
- anything that could leak host types into Core / Abstractions / Localization / Infrastructure

It does **not** replace:

- `docs/geometry/roof-elevation-contract.md` (geometry SSOT)
- `docs/COMPATIBILITY_GATE.md` (gate procedure)
- `.ai/architecture.md` / `.ai/cad-abstractions.md`

---

## 2. Supported host matrix

| Host | Managed runtime (plugin TFM intent) | One binary across this row? | Separate adapter/build? | Confidence |
| --- | --- | --- | --- | --- |
| AutoCAD 2021 | .NET Framework **4.8** | Shared with 2022–2024 only if built/tested per Autodesk SDK span rules | **Yes** vs modern .NET hosts | **VERIFIED** |
| AutoCAD 2022 | .NET Framework **4.8** | Span with 2021–2023 SDKs per Autodesk table when loaded on 2022 | Same legacy slice | **VERIFIED** |
| AutoCAD 2023 | .NET Framework **4.8** | Span with 2021–2023 SDKs on 2023 | Same legacy slice | **VERIFIED** |
| AutoCAD 2024 | .NET Framework **4.8** | Span with 2021–2024 SDKs on 2024 | Same legacy slice | **VERIFIED** |
| AutoCAD 2025 | **.NET 8** (`net8.0-windows` typical) | Autodesk: 2025 SDK for 2025 host | **Yes** vs Framework 4.8 and vs .NET 10 | **VERIFIED** |
| AutoCAD 2026 | **.NET 8** | Autodesk: 2026 **or** 2025 SDK may load on 2026 | Same modern-.NET8 slice as 2025 (still separate from 2027) | **VERIFIED** |
| AutoCAD 2027 | **.NET 10** (`net10.0-windows` typical) | Autodesk: **2027 only** | **Yes** (own adapter/build) | **VERIFIED** |
| BricsCAD V26 | **.NET 8** (`net8.0-windows`); Framework 4.8 plugins **must be rebuilt** | Not binary-compatible with V25/older Framework plugins | **Yes** (BricsCAD adapter) | **VERIFIED** |
| ZWCAD 2025 | **.NET Framework** (official: Framework class library; baseline **4.7**; community/SDK practice often **4.8**) | Not interchangeable with AutoCAD .NET 8/10 binaries | **Yes** (ZWCAD adapter) | **VERIFIED** (Framework) / **OPEN** (exact project TFM per ZRXSDK README) |
| ZWCAD 2026 | **.NET Framework** (NuGet `ZWCAD.NetApi` 20.26.0 targets **net47+**; official .NET API still Framework-oriented) | Same family as 2025 for managed hosting generation | **Yes** | **VERIFIED** (Framework) / **OPEN** (exact TFM + SDK binary span 2025↔2026) |

### Corrections to prior expectations

| Expectation | Result |
| --- | --- |
| AutoCAD 2021–2024 → .NET Framework 4.8 | **Confirmed VERIFIED** (Autodesk Application Compatibility) |
| AutoCAD 2025–2026 → .NET 8 | **Confirmed VERIFIED** |
| AutoCAD 2027 → .NET 10 | **Confirmed VERIFIED** |
| BricsCAD V26 runtime | **Verified = .NET 8**, not Framework 4.8 |
| ZWCAD 2025/2026 runtime | **Not** .NET 8/10 for managed plugins on current official/NuGet evidence — remains **.NET Framework** |

---

## 3. Official runtime evidence

### AutoCAD (Autodesk Help — Application Compatibility)

Primary table: [About Application Compatibility (AutoCAD 2027 Help)](https://help.autodesk.com/cloudhelp/2027/ENU/AutoCAD-Customization/files/GUID-D54B0935-1638-4F97-8B37-1EC3635A1E71.htm)

| Release | Supported .NET / ObjectARX SDK (as loaded on that release) | .NET column |
| --- | --- | --- |
| 2027 | AutoCAD 2027 | **10.0** |
| 2026 | AutoCAD 2026, AutoCAD 2025 | **8.0** |
| 2025 | AutoCAD 2025 | **8.0** |
| 2024 | 2024, 2023, 2022, 2021 | **4.8** |
| 2023 | 2023, 2022, 2021 | **4.8** |
| 2022 | 2022, 2021 | **4.8** |
| 2021 | 2021 | **4.8** |

Additional Autodesk guidance:

- Managed project for 2025: .NET 8 + VS 2022 ≥ 17.8 — [Which Edition of Microsoft Visual Studio to Use (.NET)](https://help.autodesk.com/cloudhelp/2025/CHT/OARX-DevGuide-Managed/files/GUID-450FD531-B6F6-4BAE-9A8C-8230AAC48CB4.htm)
- 2024 remains Framework 4.8 in that same guide
- 2027 managed: .NET 10 / `net10.0-windows` — Autodesk Developer Blog [AutoCAD 2027 SDK: What Every Plugin Developer Needs to Know](https://blog.autodesk.io/autocad-2027-sdk-what-every-plugin-developer-needs-to-know/) and Managed DevGuide [Create a New Project (.NET)](https://help.autodesk.com/cloudhelp/2027/ESP/OARX-DevGuide-Managed/files/GUID-43564EB9-F843-4771-823C-573495EE23E0.htm)
- Multi-targeting Framework 4.8 vs .NET 8: Autodesk recommends **separate builds / release branches**, not one mixed binary — [Multi-targeting AutoCAD .NET Plugin for .NET 4.8 and .NET 8.0](https://blog.autodesk.io/multi-targeting-autocad-net-plugin-for-net-48-and-net-80/)

**API constraints (VERIFIED principles):**

- Managed plugins reference `AcCoreMgd` / `AcDbMgd` / `AcMgd` (and UI assemblies as needed) with **Copy Local = false**; host supplies them at runtime.
- Major runtime breaks: **4.8 → 8** (2025) and **8 → 10** (2027). Do not expect a single managed DLL to load across those breaks.
- Within a generation, Autodesk’s “Supported SDK” column defines which compiled SDK generations a given AutoCAD release will load — **not** a guarantee that every API used is forever forward-compatible. Retest per release; use Migration Guide / What’s New for API deltas.

### BricsCAD V26 (Bricsys)

- [How to develop and run .NET plugins in BricsCAD V26](https://help.bricsys.com/en-us/document/knowledge-base/how-to/how-to-develop-and-run-net-plugins-in-bricscad-v26) — managed host upgraded to **.NET 8**; Framework 4.8 **deprecated**; rebuild with SDK-style `net8.0-windows`; end users need .NET 8 Desktop Runtime x64.
- [BricsCAD .NET API overview](https://developer.bricsys.com/bricscad/help/en_US/CurVer/DevRef/source/dotNETAPI.htm) — V26 major host architecture change; older Framework plugins may not load.
- Release notes confirm V26 .NET 8 hosting (Bricsys release notes / KB).

### ZWCAD 2025 / 2026 (ZWSOFT)

- Official API overview: [.NET is a .NET Framework class library](https://confluence.zwcad.com/pages/viewpage.action?pageId=263914249) using `ZwManaged.dll` / `ZwDatabaseMgd.dll`; historical official baseline cites VS 2017 + **.NET Framework 4.7**.
- Public NuGet [`ZWCAD.NetApi` 20.25.0 / 20.26.0](https://www.nuget.org/packages/ZWCAD.NetApi) — targets **.NET Framework 4.7** (compatible with higher Framework monikers).
- ZWSOFT developer portal lists separate ZRXSDK / .NET docs for 2024–2026: [zwsoft.com/support/zwcad-devdoc](https://www.zwsoft.com/support/zwcad-devdoc/)
- **OPEN:** exact csproj TFM required by ZRXSDK 2025 vs 2026 README (4.7 vs 4.8), and whether a single Framework binary spans both years without rebuild.
- Third-party packages (e.g. IFox.CAD.ZCAD2025) claim Framework 4.8 for 2025–2026 — **PROVISIONAL** only; not treated as vendor SSOT.

---

## 4. Neutral Core rule

**Normative.**

`AcKrovy.Core` (and any future extracted lifecycle packages) MUST remain CAD-host-neutral:

- geometry, calculations, lifecycle semantics, identity rules, Placement models, persistence models, edit classification
- **no** Autodesk / Bricsys / ZWSOFT / ODA / Teigha API types
- **no** dependence on `ObjectId`, handle stability, AutoCAD-only events, or AutoCAD 2027-only APIs for semantic decisions

ObjectIds / handles of derived Physical3D are host implementation details. Semantic identity + Placement + canonical geometry remain authoritative across SAVE/REOPEN (aligned with accepted Structural lifecycle semantics).

Current repo reality (**VERIFIED** from `main` remote, not local WIP): `AcKrovy.Core` targets **`netstandard2.0`**.

---

## 5. Cad.Abstractions rule

**Normative.**

`AcKrovy.Cad.Abstractions` holds neutral contracts/interfaces only:

- no direct host implementation
- no references to host assemblies
- may depend on Core

Current repo reality: **`netstandard2.0`**, ProjectReference → Core.

Portable gate already forbids Autodesk references in portable projects (`docs/COMPATIBILITY_GATE.md`).

---

## 6. Host adapter responsibilities

**Normative split.**

Host adapters own:

- native command / editor events and command-context threading
- database / entity access (ObjectId, transactions, open modes)
- XData / native metadata plumbing
- Solid3d (or host solid) creation and transform application
- GROUP membership finalize / verify
- selection sets
- host UI (PaletteSet, ribbons, WPF/WinForms hosting)
- packaging (`PackageContents.xml` RuntimeRequirements, SeriesMin/Max, per-host loaders)

Core owns semantic outcomes (accept / restore / suppress / manualize / Placement update rules). Adapters translate host observations into Core requests and apply Core decisions back to the drawing.

**PROVISIONAL adapter slice names** (not final project names):

| Slice | Intent |
| --- | --- |
| `AcKrovy.AutoCAD` (current) | AutoCAD **2027** / .NET 10 host (existing on `main`: `net10.0-windows`) |
| `AcKrovy.AutoCAD.Net8` *(provisional)* | AutoCAD **2025–2026** / .NET 8 |
| `AcKrovy.AutoCAD.Net48` *(provisional)* | AutoCAD **2021–2024** / Framework 4.8 |
| `AcKrovy.BricsCAD` *(provisional)* | BricsCAD V26 / .NET 8 |
| `AcKrovy.ZWCAD` *(provisional)* | ZWCAD 2025/2026 / Framework |

Shared UI that is truly host-neutral may live behind abstractions; WPF that binds to AutoCAD PaletteSet remains adapter-side or carefully split.

---

## 7. Target framework strategy

**Recommended (PROVISIONAL strategy; aligns with verified matrix + current Core):**

| Layer | Target | Notes |
| --- | --- | --- |
| Core | **`netstandard2.0`** (keep) | Consumable by net48, net8, net10 hosts |
| Cad.Abstractions | **`netstandard2.0`** (keep) | Same |
| Infrastructure / Localization | Stay portable (no host refs) per existing gate | Confirm TFMs against gate list |
| AutoCAD 2021–2024 host | **`net48`** (Windows) | Separate adapter/build |
| AutoCAD 2025–2026 host | **`net8.0-windows`** | Separate from 2027 |
| AutoCAD 2027 host | **`net10.0-windows`** | Current `AcKrovy.AutoCAD` |
| BricsCAD V26 | **`net8.0-windows`** | May share patterns with AutoCAD net8 adapter; **not** the same binary |
| ZWCAD 2025/2026 | **`net47` or `net48`** | Confirm from ZRXSDK; **OPEN** exact moniker |

Do **not** raise Core to `net8`/`net10` solely because AutoCAD 2027 uses them — that would break Framework host consumption unless multi-targeting Core carefully. Prefer keeping Core on `netstandard2.0` until a deliberate multi-TFM Core decision is made (**OPEN** product call).

---

## 8. Cross-version binary strategy

**Normative intent:**

1. **Never** ship one managed DLL across .NET Framework 4.8 ↔ .NET 8 ↔ .NET 10 AutoCAD hosts.
2. Within AutoCAD 2021–2024 (Framework 4.8), prefer **one legacy AutoCAD adapter build** compiled against the oldest supported SDK in the product support window, then HOST-regress on each year — **PROVISIONAL** until product support window is frozen.
3. Within AutoCAD 2025–2026 (.NET 8), Autodesk documents 2026 loading 2025 SDK builds; still treat API surface as release-sensitive and HOST-test both (**PROVISIONAL** single net8 AutoCAD binary).
4. AutoCAD 2027 requires its **own** .NET 10 build (Autodesk: 2027 only).
5. BricsCAD V26 and AutoCAD net8 are both .NET 8 but **different host assemblies** → separate adapters even if source is shared via `#if` / partials / aliases (**PROVISIONAL** sharing technique).
6. ZWCAD Framework adapters are separate from AutoCAD Framework adapters (different managed assemblies).
7. Bundle layout: version-specific folders + `RuntimeRequirements` / SeriesMin–SeriesMax (AutoCAD ApplicationPlugins) — follow Autodesk packaging guidance.

Autodesk’s own blog preference for large codebases: **release branches per major AutoCAD generation** plus cherry-picks — optional process choice for KROVY (**PROVISIONAL**).

---

## 9. Compatibility gates

Reference: `docs/COMPATIBILITY_GATE.md`, `scripts/compatibility-gate.ps1`, skill `.agents/skills/portable-compatibility-gate/`.

| Gate | When | Protects against |
| --- | --- | --- |
| **Portable Compatibility Gate** | CI + machines without AutoCAD | Host assemblies leaking into Core / Abstractions / Infrastructure / Localization; forbidden Autodesk references in portable sources; portable restore/build WAE; Core tests |
| **Full Compatibility Gate** | Dev machine with AutoCAD API present | Full solution restore/build/test WAE; verifies AutoCAD adapter still builds against installed host DLLs |

Gates should continue to protect (expand as multi-host lands):

- leaking host assemblies into neutral Core
- using APIs unavailable on older supported hosts (policy + adapter isolation; gate cannot fully prove API age alone)
- accidental TFM incompatibility (portable projects must stay host-consumable)
- shared “helper” code that only compiles/runs on AutoCAD 2027
- host-specific behavior entering lifecycle semantics (Core must remain semantic)

**OPEN:** extend Portable/Full gates with explicit checks for BricsCAD/ZWCAD project graphs when those adapters exist; today Full Gate is AutoCAD-install oriented.

---

## 10. Forbidden dependencies

**In portable / neutral projects** (`AcKrovy.Core`, `AcKrovy.Cad.Abstractions`, `AcKrovy.Localization`, `AcKrovy.Infrastructure` — per `AGENTS.md` + Portable Gate):

- `Autodesk.AutoCAD.*`, `AcMgd`, `AcDbMgd`, `AcCoreMgd`, `AdWindows`, …
- BricsCAD managed assemblies (`BrxMgd`, Teigha/Bricscad namespaces as host refs)
- ZWCAD managed assemblies (`ZwManaged`, `ZwDatabaseMgd`, …)
- ODA / Teigha host kits
- `AcKrovy.AutoCAD` (or any host adapter) ProjectReference from portable projects

**In Core semantic code specifically:**

- any type whose identity is a host ObjectId/Handle
- AutoCAD command event ordering assumptions encoded as Core rules without an abstraction
- APIs gated to AutoCAD 2027-only used to decide lifecycle outcomes

---

## 11. Agent instructions

A short normative pointer lives in root [`AGENTS.md`](../../AGENTS.md). Agents MUST read this document before CAD-host / runtime-sensitive work.

Summary (do not treat this summary as a substitute for sections 2-10):

- Before changing CAD-host API usage, target frameworks, multi-targeting, Compatibility Gate rules, host adapters, or any runtime-sensitive boundary between Core and a CAD host, read this file.
- Keep `AcKrovy.Core`, `AcKrovy.Cad.Abstractions`, `AcKrovy.Localization`, and `AcKrovy.Infrastructure` free of Autodesk, BricsCAD, ZWCAD, ODA, and Teigha dependencies.
- Lifecycle and other Core semantic behavior must remain host-neutral unless this contract explicitly documents an exception.
- Do not assume one managed binary spans AutoCAD .NET Framework 4.8, .NET 8, and .NET 10 hosts.
- Run Portable Compatibility Gate for neutral-layer changes; run Full Compatibility Gate when CAD host adapter / host-reference changes are in scope.


## 12. Open / not-yet-verified items

1. **ZWCAD exact TFM** — confirm from ZRXSDK 2025/2026 official README whether projects must be `net47` vs `net48`; confirm 2025↔2026 managed binary span. (**OPEN**)
2. **ZWCAD .NET 8/9** — public TKB titles discuss “是否支持.NET8或.NET9”; treat current shipping managed API as Framework until a vendor doc states otherwise. (**OPEN** to re-verify if ZWSOFT publishes a CoreCLR host)
3. **AutoCAD legacy support window** — which of 2021–2024 KROVY will actually ship (**product OPEN**)
4. **Single vs dual net8 AutoCAD builds** for 2025 vs 2026 (**PROVISIONAL** preference: one net8 AutoCAD adapter + dual HOST smoke)
5. **BricsCAD API surface parity** with AutoCAD for GROUP / Solid3d / XData / grip events used by KROVY (**OPEN** — requires adapter spike)
6. **Gate expansion** for non-AutoCAD hosts (**OPEN**)
7. **Whether Core ever multi-targets** beyond `netstandard2.0` (**OPEN**; not required by verified matrix)
8. This draft was researched against **GitHub `main` remote** + official web docs; **local Codex WIP was not read** — re-diff when integrating into the repo.

---

## Appendix A — Current KROVY snapshot (remote `main`, 2026-10-02 research)

| Project | Observed TFM | Role |
| --- | --- | --- |
| `AcKrovy.Core` | `netstandard2.0` | Neutral |
| `AcKrovy.Cad.Abstractions` | `netstandard2.0` | Neutral contracts |
| `AcKrovy.AutoCAD` | `net10.0-windows` | AutoCAD 2027 adapter (comment in csproj already anticipates separate legacy/2025–2026 adapters) |

Sources: GitHub `rjanic/KROVY` file reads via API (read-only).

---

## Appendix B — Source URL checklist

| Topic | URL | Label |
| --- | --- | --- |
| AutoCAD runtime table | https://help.autodesk.com/cloudhelp/2027/ENU/AutoCAD-Customization/files/GUID-D54B0935-1638-4F97-8B37-1EC3635A1E71.htm | VERIFIED |
| AutoCAD VS / .NET 8 vs 4.8 | https://help.autodesk.com/cloudhelp/2025/CHT/OARX-DevGuide-Managed/files/GUID-450FD531-B6F6-4BAE-9A8C-8230AAC48CB4.htm | VERIFIED |
| AutoCAD 2027 .NET 10 blog | https://blog.autodesk.io/autocad-2027-sdk-what-every-plugin-developer-needs-to-know/ | VERIFIED |
| AutoCAD multi-target advice | https://blog.autodesk.io/multi-targeting-autocad-net-plugin-for-net-48-and-net-80/ | VERIFIED |
| BricsCAD V26 .NET 8 KB | https://help.bricsys.com/en-us/document/knowledge-base/how-to/how-to-develop-and-run-net-plugins-in-bricscad-v26 | VERIFIED |
| BricsCAD .NET API | https://developer.bricsys.com/bricscad/help/en_US/CurVer/DevRef/source/dotNETAPI.htm | VERIFIED |
| ZWCAD API overview | https://confluence.zwcad.com/pages/viewpage.action?pageId=263914249 | VERIFIED (Framework) |
| ZWCAD.NetApi 20.26.0 | https://www.nuget.org/packages/ZWCAD.NetApi/20.26.0 | VERIFIED (net47 package TFM) |
| ZWSOFT devdoc portal | https://www.zwsoft.com/support/zwcad-devdoc/ | VERIFIED (doc index) |
| KROVY Compatibility Gate | `docs/COMPATIBILITY_GATE.md` on `main` | VERIFIED (repo) |

---
