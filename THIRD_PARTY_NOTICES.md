# Third-party notices — Frahan StonePack

Frahan StonePack (GPL-3.0) bundles or links the components below. Each is the
property of its authors under the stated license. This file is the attribution +
corresponding-license register required for redistribution. Data provenance is in
`data/ATTRIBUTION.md`; the design rationale + licensing register is in
`docs/thesis/90_originality.md`. Style: short sentences, no em dashes.

## Code / libraries

| Component | Where | Upstream | License | Notes |
|---|---|---|---|---|
| **PuzzleFusion++** (Kintsugi port + weights) | `src/Frahan.Kintsugi.Port`, `install/weights/kintsugi.bin` | Wang, Chen, Furukawa 2025 (arXiv:2406.00259) | **Non-commercial research-only; GPLv3 for research** | The defining restriction of this release; see `NOTICE.md`. Weights ~267 MB (LFS). |
| Jigsaw matching subtree | inside PuzzleFusion++ vendor tree | Lu et al. | MIT (ships its own LICENSE) | Not compiled into the shipped `.gha`; treated under the parent non-commercial terms. |
| **CGAL** (PMP, surface mesh, simplification, reconstruction, straight-skeleton, partition, SDF segmentation, heat geodesics) | `native/cgal_shim`, `install/plugin/frahan_cgal.dll` | cgal.org | **GPL-3.0** | Corresponding shim source vendored in `native/cgal_shim/`. Optional at runtime; BSP/MeshCsg fallback exists. |
| **GMP** | `install/plugin/gmp-10.dll` | gmplib.org | **LGPL-3.0 / GPL-2.0 (dual)** | Dependency of CGAL. Dynamically linked; replaceable per LGPL. |
| Boundary First Flattening (BFF) | `install/tools/bff-command-line.exe` | Sawhney & Crane 2017 (DOI 10.1145/3072959.3056432) | MIT (as published) | External static exe; called out-of-process. |
| Geogram | `install/plugin/frahan_geogram.dll` | INRIA / B. Levy | BSD-3-Clause (license bundles its own deps) | Reconstruction + remesh; bundles Kazhdan Poisson (see Geogram notices). |
| CoACD | `install/plugin/frahan_coacd.dll` | Wei et al. 2022 | MIT | Approximate convex decomposition. |
| Bullet / BulletSharp | `install/plugin/libbulletc.dll`, `BulletSharp.dll` | bulletphysics.org / AndresTraks | zlib / MIT | Rigid-body settle. |
| Clipper2 | `install/plugin/Clipper2Lib.dll`, `native/nfp_kernel/clipper2` | Angus Johnson | Boost Software License 1.0 | 2D boolean / NFP kernel. |
| NetTopologySuite (+ IO.Esri, IO.GeoJSON, Features) | `install/plugin/NetTopologySuite*.dll` | NTS team | BSD-3-Clause / LGPL (per package) | Vector ingest (SHP/GeoJSON). |
| csg.js (MeshCsg port) | `src/Frahan.StonePack.Core` (Ch.10) | Evan Wallace | MIT | Pure-managed CSG fallback. |
| TorchSharp / libtorch | optional runtime for Kintsugi | .NET Foundation / Meta | MIT / BSD | Optional; only for the learned path. |
| System.* / Microsoft.* runtime DLLs | `install/plugin/System.*.dll` | Microsoft | MIT | .NET Framework redistributables. |

## Datasets

Bundled / referenced datasets keep their own licenses; several are research-use-only,
consistent with this research-preview release. Full table with DOIs: `data/ATTRIBUTION.md`.
Notable: Stanford 3D scans (commercial use needs permission); OpenTopography Granite Dells
(license "Not Provided"); ETH1100, Tongjiang, Grimsel/Bondua/TU1208 GPR (CC-BY-4.0);
GeoCrack (MIT); Loviisa fracture traces (CC-BY-4.0).

## A note on the geometry path
The CGAL-backed geometry path is GPL-3.0 and bundles GMP (LGPL). These permanently
prevent relicensing the geometry path under a permissive license. They are optional at
runtime (BSP / MeshCsg fallbacks) and the corresponding source for the shim is vendored.
