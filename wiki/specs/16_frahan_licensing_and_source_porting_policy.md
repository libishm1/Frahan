# 16 - Frahan Licensing and Source-Porting Policy

**Spec version:** 0.1
**Sources:** runbook § 18, `docs/index/frahan_reference_register.md`,
`Template-General/AGENTS.md` (no-assumption / Context7 rules).

## 1. Hard rules

1. **Do not copy incompatible licensed code into Frahan.**
2. **Treat external repositories as references** unless the license
   explicitly allows direct reuse.
3. **Preserve attribution** every time third-party material is
   included (whether code, comments, doc text, or images).
4. **Track license type for every source** in
   `docs/index/frahan_reference_register.md`.
5. **Isolate third-party code** in dedicated folders / assemblies.
   No third-party identifier in a Frahan public API.
6. **Prefer clean-room re-implementation** for algorithms.
7. **Do not mix GPL** code into a closed or permissively-licensed
   plugin unless the project intentionally accepts the GPL
   obligations (and Libish has signed off in writing).
8. **CGAL package-level licensing must be checked** before any
   linking or redistribution. CGAL is mostly GPLv3+ with selected
   packages under LGPL or commercial.
9. **Native libraries must remain optional** until license and
   deployment risks are resolved.
10. **Do not paste large third-party source files into Frahan.**
11. **Keep a reference register** (`docs/index/frahan_reference_register.md`)
    with URL, license, use case, reuse status, and risk.
12. **Distinguish three states** for every external source:
    `studied as reference` | `used as dependency` |
    `copied source`. Each requires a different attribution strategy.

## 2. Action tiers

| Use case | Required artefact |
| --- | --- |
| `studied as reference` | reference register row; no in-tree copy |
| `used as dependency` | reference register row; `THIRD_PARTY_NOTICES.md` row; csproj `<PackageReference>` or in-tree DLL with NOTICE |
| `copied source` | reference register row; `THIRD_PARTY_NOTICES.md` row; per-file attribution comment block at the top of every copied `.cs` / `.cpp` / `.h` file; explicit license-compatibility check; written sign-off from Libish |

## 3. Specific situations on disk today

| Situation | Status | Required action |
| --- | --- | --- |
| BFF runtime bundled in `dist/frahan_stonepack-0.5.6-rh8-win.zip` | `used as dependency` | author `THIRD_PARTY_NOTICES.md`; pin BFF upstream LICENSE; document SuiteSparse / OpenBLAS / GFortran notices |
| `references/original_gh_2d_packing_plugin/Gh2DPacking/` | currently `copied source` (Frahan derivatives in `src/Frahan.StonePack.GH/TwoD/*.cs`) | obtain explicit license / permission; record per-file attribution in the derivative files |
| `frahan_*_backend_starter*.zip` inside the research bundle | `studied as reference` | leave inside the zip; do not extract until license confirmed |
| `PolytopeSolutions_GrasshopperTools.dll/.pdb` at repo root | currently `used as dependency` (during 3dpacking sessions) | obtain license; either remove from the repo or bundle with attribution |
| `Frahan_MASTER_RESEARCH_KNOWLEDGE_BASE_v0_2_20260503.md` and the rest of `frahan/` | Libish-owned content | none required; treat as primary source |
| `wiki/3d_reconstruction/3d_data_science_python.md` (1.1 MB textbook extract) | likely `studied as reference` but stored as `copied source` | leave in place; do not redistribute; do not promote any text into Frahan specs |

## 4. Per-fix attribution template (when copying source)

```csharp
// SPDX-License-Identifier: <SPDX>
// Adapted from <upstream project> @ <commit / version>
// Original author(s): <author>
// Original license:    <license>
// Modifications:       <date> <author> <short description>
```

## 5. License compatibility quick-reference

| Upstream | Compatible with Frahan (closed plugin)? | Notes |
| --- | --- | --- |
| MIT, Apache-2.0, BSD-3-Clause, Boost, MPL-2.0, ISC | yes | preserve attribution |
| LGPL-2.1 / LGPL-3 | yes if dynamically linked and replacement of the LGPL component is documented | follow LGPL section 4/6 |
| GPLv2 / GPLv3 | **no** unless Frahan accepts GPL obligations | CGAL is mostly GPLv3+ |
| Custom / "All rights reserved" | **no** without written permission | request license |
| Public domain | yes | preserve provenance for academic citation |

## 6. Process

1. New external source identified → add a row to
   `docs/index/frahan_reference_register.md`.
2. License confirmed → if compatible, decide tier (`reference`,
   `dependency`, `copied source`).
3. If `copied source` → add per-file attribution and a row in
   `THIRD_PARTY_NOTICES.md`.
4. If `dependency` → add `<PackageReference>` or commit the DLL with
   a NOTICE.
5. Until the row in the reference register is filled, the source
   **cannot ship**.

## 7. Periodic review

- Every release tag triggers a license-review pass.
- The release-engineering script (proposed) refuses to build a
  `dist/` zip if any DLL inside the bundle does not have a row in
  `THIRD_PARTY_NOTICES.md`.
- Annual review of upstream license texts (some upstreams change
  license between major versions; e.g. MeshLib's GPL+commercial
  dual-licence is version-dependent).
