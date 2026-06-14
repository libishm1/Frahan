# Rock-face & discontinuity datasets

Catalog of rock-face / rock-mass-discontinuity point-cloud and fracture
datasets for testing an automated joint-set / discontinuity-extraction
worker (per-facet normals -> region-grow facets -> cluster joint sets by
orientation + spacing).

## What the worker needs

The discontinuity worker consumes **clean in-situ rock-face point clouds**:
plain XYZ geometry, ideally with some per-point normals, at a resolution
fine enough to resolve individual joint facets (cm-scale or better). From
those it estimates per-point/per-facet normals, region-grows planar facets,
and clusters joint sets by orientation (Watson/Fisher mean-shift) and spacing.

Two practical consequences shape this catalog:

- **Registration datasets are usable inputs.** RockAlign / RockCloud-Align
  ships rigid-registration *pairs* of real rock-face scans. It carries no
  joint labels and no normals, but each cloud is a real single-outcrop
  rock face  -  exactly the geometry the worker needs. Take one cloud per
  outcrop (dedupe overlapping source/target crops), compute normals
  yourself, and proceed.
- **Image / trace datasets are NOT direct inputs.** GeoCrack, the NTNU/NGI
  joint-trace set, and the Sketchfab catalog are 2D imagery, trace masks,
  or hand-specimen meshes. They are reference / validation material, not
  point-cloud input.

The most directly usable point-cloud assets are listed first within each
section.

---


## Hands-on note: RockCloud-Align archive (verified 2026-06-14)

The 7.4 GB `RockCloud-Align.rar` was inspected directly. Confirmed structure and the
correct way to consume it:

- Layout: `{split}/{scene}/rock/rock_r{N}_k{count}_overlap{O}_prob{P}.txt` (the point
  clouds, ASCII) + `{split}/{scene}/pose/transformation_matrix_r{N}.txt` (4x4 GT poses).
  21 scenes (TLS scans, a 2018-03-23 campaign), ~28k `.txt` clouds, ~16k-30k pts each.
- Columns vary by scene: some `x y z 0 0` (space), some `x,y,z,r,g,b,0` (comma+RGB).
- **DO NOT merge a scene's fragments with their poses.** Each `rock_r{N}.txt` is already
  ONE complete (cropped+resampled) single-outcrop cloud; the poses are the registration
  ground truth, not surface-assembly transforms. Merging fans the overlapping crops into
  a radial "spaghetti" smear (a misregistration artifact, NOT discontinuities). Take ONE
  cloud per outcrop.
- **Units are density-normalized, not metric** - orientation (dip/dip-direction) is
  scale-invariant and valid; **spacing / block-size are NOT** on these clouds.
- Verified: the worker on a single cloud (16,938 pts, ~1 m crop) returns 3 clean joint
  sets (e.g. dip 72/204 63%, 37/20 26%, 76/74 7%) - small but coherent. For full-face
  in-situ analysis the Granite Dells full scan (A4) is the better input; RockCloud-Align
  clouds are small registration crops.

## A. Point-cloud rock-face / outcrop datasets (primary inputs)

These are the real 3D rock-face clouds the worker can run on directly.

### A1. RockCloud-Align dataset (the actual data archive)

- **Source:** Wang, Yu, Liu, Xiao (UCAS). Hosted on Baidu PaddlePaddle
  AI Studio (Xinghe / Galaxy community). The GitHub README link
  `https://bit.ly/RockCloud-Align` 301-redirects here.
- **URL:** https://aistudio.baidu.com/datasetdetail/294264
  (via `https://bit.ly/RockCloud-Align`)
- **Format:** ASCII `.txt` point clouds, XYZ  -  **strongly inferred, not
  paper-stated**. Archive holds ~28,000 `.txt` files; 14,139 pairs x 2 =
  28,278, so the `.txt` files are the per-pair SOURCE and TARGET clouds.
  Ground-truth 4x4 transforms stored separately. Expect 3 columns
  (x y z), no header, **no normals, no color**  -  verify by opening one file.
- **Size:** ~28k point-cloud text files at ~20k points each -> order of a
  few GB uncompressed. Exact byte size visible only after Baidu login.
- **Content:** High-resolution TLS scans of rock masses from 26 site IDs
  across 3 regions  -  Beijing CN (BJ-01/02), Ontario CA (ON-01..08),
  Qinghai CN (QH-01..16). Scanners: Leica P30 and Leica HDS6000. Raw
  scenes 1M to >10M pts; released registration clouds cropped + resampled
  to ~20,000 pts each (min 10k, max 30k). 14,139 pairs (11,383 train /
  2,756 test; test = held-out ON-07/08, QH-14/15/16). Mean overlap
  ~42-44%. Rock type per site is NOT stated (paper says only "diverse rock
  types"). **Coordinates are density-normalized** (`s = (N/(V*rho_t))^(1/3)`),
  so released coords are NOT guaranteed true-metric.
- **License:** GPL-2.0 per the Baidu page meta tag  -  **conflicts** with
  the paper's CC BY. GPL-2.0 is unusual for data; confirm before
  redistribution.
- **Accessibility:** **login-required.** Baidu AI Studio gates the file
  list and downloads behind a Baidu account; no anonymous file-list/API;
  no Hugging Face / Zenodo / Kaggle mirror found.
- **Relevance:** HIGH as raw geometry. Each `.txt` is a clean
  single-outcrop rock-face cloud (~20k pts), directly loadable for
  normal estimation, facet region-growing, and orientation+spacing
  joint-set clustering. Caveats: (a) ~20k-point downsampling limits fine
  fracture detail; (b) **density renormalization breaks true-metric
  spacing**  -  orientation-based clustering is scale-invariant and fine,
  but spacing metrics will be wrong on normalized clouds; (c) no normals
  (compute them); (d) no discontinuity labels; (e) overlapping
  source/target crops are near-duplicates  -  take one cloud per outcrop.

### A2. RockCloud-Align (Baidu AI Studio dataset #294264)  -  extended record

- **Source:** Baidu PaddlePaddle AI Studio (Baidu PaddlePaddle AI Studio). UCAS
  School of AI  -  Wang, Yu, Liu, Xiao. Corresponding: liulupeng@ucas.ac.cn /
  Jun Xiao.
- **URL:** https://aistudio.baidu.com/datasetdetail/294264
  (canonical server-rendered: https://aistudio.baidu.com/studio/dataset/detail/294264)
- **Format:** Not explicitly stated on the page or in the paper. Raw 3D
  point clouds (XYZ) + per-pair ground-truth rigid transforms. UNCERTAIN
  whether PLY / PCD / LAS / plain-text XYZ  -  verify after download. The
  AI Studio container itself downloads as an archive (typically .zip/.tar).
- **Size:** 14,139 pairs (11,383 train + 2,756 test). Per-cloud examples
  cited in the paper: 11,143,133 / 11,527,740 / 11,558,045 points (~1M to
  >10M each, raw scene scale). Total archive byte-size NOT published  - 
  plausibly multi-GB. Verify on the file listing (login).
- **Content:** Large-scale rock-mass point-cloud REGISTRATION benchmark.
  Scenes span "cracked rock formations" (close-range fractured surfaces)
  through "expansive cliff faces" with deliberately varied point density.
  Locations: Beijing CN, Qinghai CN, Ontario CA. Leica P30 + Leica
  HDS6000. NO joint-set / dip / dip-direction / facet / discontinuity
  annotations  -  raw paired clouds + alignment ground truth only.
- **License:** AMBIGUOUS  -  Baidu page tags GPL-2.0; companion paper is
  CC-BY 4.0 (Crossref). Confirm intended data license with the authors
  before redistribution.
- **Accessibility:** **login-required** (Baidu Passport). Page is a JS
  SPA; download button + file listing render only after authentication.
  The `aistudio` Python SDK/CLI can pull public datasets by ID once
  authenticated. No GitHub/Zenodo mirror; the MDPI paper's Data
  Availability Statement is empty, so this page is the only retrieval path.
- **Relevance:** INDIRECT but useful  -  one of very few OPEN, large-scale
  TLS datasets of natural fractured rock surfaces and cliff faces, in
  exactly the worker's data regime, with 14k clouds for tuning
  normal-estimation and facet growing on real rock. Limitation: it is a
  registration benchmark (no joint-set labels, no dip/dip-direction);
  de-duplicate the partial-overlap/density-varied copies of each scene
  before discontinuity extraction. A real-rock SOURCE, not a labelled
  benchmark.

### A3. Extraction recipe  -  one usable rock-face cloud for the worker

- **URL:** https://aistudio.baidu.com/datasetdetail/294264
- **Source:** Synthesis of the RockCloud-Align paper structure + GitHub
  repo layout + GeoTransformer/3DMatch data convention.
- **Procedure:**
  1. **DOWNLOAD:** log into Baidu AI Studio, download and unzip the archive.
  2. **IDENTIFY CLOUDS vs POSES:** the ~28k `.txt` files are point clouds
     (source/target per pair). Open one: ~10k-30k rows of 3 floats = an
     XYZ cloud (no header expected). Files of exactly 4x4 (or 16 numbers)
     are 4x4 transformation matrices = POSES  -  skip them for discontinuity
     work. A pickle/.npy/info/metadata/index file maps pairs to GT transforms.
  3. **PICK ONE CLOUD PER OUTCROP:** you do not need pairs. Take a single
     cloud per site (e.g. each pair's source, deduplicated) to analyze
     distinct rock faces, not overlapping crops.
  4. **LOAD:** `numpy.loadtxt` / Open3D `read_point_cloud` (treat as xyz) / PCL.
  5. **NORMALS:** none stored  -  compute per-point normals yourself
     (k-NN PCA / the CSR worker's k=24 analytic-eigen path), then
     region-grow facets and cluster joint sets by orientation
     (Watson/Fisher mean-shift) + spacing.
  6. **UNITS WARNING:** coordinates may be density-normalized, not true
     metres; spacing-based statistics will be wrong on normalized clouds.
     Orientation-only clustering is unaffected by uniform scaling.
- **Format (worker input):** ASCII XYZ `.txt` (3 columns; ~10k-30k rows;
  no normals; no color  -  **verify by opening one file**).
- **License:** dataset GPL-2.0 (Baidu tag)  -  confirm before redistributing
  derived products.
- **Accessibility:** login-required (Baidu); clouds are plain text and
  trivially parseable offline once unzipped.
- **Relevance:** DIRECT operational guidance for a per-facet-normal /
  region-grow / orientation-spacing worker. Key risks: ~20k-point
  downsampling limits fine detail; density renormalization breaks
  true-metric spacing (orientation clustering still valid); dedupe
  overlapping source/target crops; no normals/labels supplied. Until a
  file is opened the column layout (3 vs 6, header?, delimiter) is
  UNVERIFIED  -  validate on the first file. If files are 6-column
  (x y z nx ny nz) the dataset would include normals, but there is NO
  evidence for that  -  assume XYZ-only.

### A4. Granite Dells Terrestrial Laser Scanning Dataset (OpenTopography)

- **Source:** OpenTopography (SDSC, UC San Diego). Collector: David E.
  Haddad, Arizona State University. Funded by NSF (EAR 0651098) + SCEC.
- **URL:** https://portal.opentopography.org/datasetMetadata?otCollectionID=OT.122010.26912.1
  (DOI https://doi.org/10.5069/G9Z60KZ8)
- **Format:** Point cloud in LAS/LAZ; on-demand DEM/hillshade rasters via
  the portal. Horizontal WGS84/UTM 12N [EPSG:26912]; vertical NAVD88
  (GEOID 03) [EPSG:5703], meters.
- **Size:** 4,977,725 points; density 497.77 pts/m^2; survey area
  0.01 km^2 (one small outcrop/hillslope plot).
- **Content:** Centimeter-resolution TLS of a small population of
  precariously balanced rocks (PBRs) on hillslopes by a stream channel.
  Rock type: Proterozoic Dells Granite pluton (weathered granite tors,
  exfoliation surfaces, joint-bounded boulders), Granite Dells near
  Prescott, Yavapai County, Arizona, USA (~34.602-34.603 N,
  -112.4195 to -112.4205 W). Collected 2009-09-11 (ground-based tripod TLS).
- **License:** Use License = "Not Provided" (UNCERTAIN/unspecified  -  NOT
  explicitly public domain or CC). Governed by OpenTopography's Citation
  Policy + Terms of Use; cite DOI 10.5069/G9Z60KZ8 and acknowledge
  OpenTopography + the data provider. (Contrast: the airborne sibling is
  CC BY 4.0; this TLS dataset is not.)
- **Accessibility:** **open** (no fee). Point-cloud download via the
  portal (LAS/LAZ) and on-the-fly DEM generation; "Bulk Download"
  requires a free OpenTopography login. REST/Point-Cloud API available.
- **Relevance:** HIGH. A dense (~498 pts/m^2), cm-resolution TLS scan of
  bare weathered granite with exfoliation surfaces and joint-bounded tors
   -  the surface morphology a per-facet-normal + region-grow +
  orientation/spacing pipeline targets. Caveats: (1) small single plot
  (0.01 km^2)  -  a unit-test/single-outcrop case, not a large slope;
  (2) survey goal was PBR/geomorphology + seismic hazard, so **no
  ground-truth joint-set labels**  -  derive orientations yourself;
  (3) granite is rounded/weathered (spheroidal weathering, tors), so
  planar joint facets may be partly obscured by curvature; (4) license
  unspecified  -  confirm terms before redistribution.

### A5. Granite Dells, AZ (airborne lidar)  -  sibling, cross-reference only

- **Source:** OpenTopography; airborne lidar by NCALM; provider David E.
  Haddad (ASU).
- **URL:** https://portal.opentopography.org/lidarDataset?opentopoID=OTLAS.102010.26912.1
  (Collection OT.102010.26912.1)
- **Format:** LAZ point cloud; portal-generated DEM, hillshade, slope,
  aspect, roughness. NAD83/UTM 12N [EPSG:26912]; vertical NAVD88
  (GEOID 03) [EPSG:5703].
- **Size:** ~357 million points over 47 km^2; 7.61 pts/m^2.
- **Content:** Regional-scale ALS of the Granite Dells region near
  Prescott, AZ  -  exposed Proterozoic granitic bedrock landforms; the
  regional companion to the close-range TLS scan above.
- **License:** CC BY 4.0 (acknowledge OpenTopography + source).
- **Accessibility:** **open.** Portal LAZ download + on-demand rasters;
  bulk via AWS CLI / CyberDuck; OpenTopography API.
- **Relevance:** MEDIUM-LOW for fine discontinuity work  -  airborne density
  (7.6 pts/m^2) is far too coarse to resolve individual joint facets or
  per-facet normals. Useful only for regional lineament/landform context.
  Included to **disambiguate** from the TLS dataset (A4), which is the
  relevant one for the pipeline. Do not conflate the two collection IDs.

---

## B. Fracture / crack annotation datasets

These are 2D imagery, trace masks, or simulation geometry  -  reference and
validation material, not direct point-cloud input.

### B1. Dataset for image-based rock joint trace mapping (NTNU + NGI)

- **Source:** Chiu (NTNU), Hansen (NGI), Paulsen (NGI), Mengshoel (NTNU).
  Hosted on Zenodo (CERN).
- **URL:** https://zenodo.org/records/18078781
- **Format:** 2D images (`rockmass.zip`) + label masks (`label.zip`) +
  four datasheet PDFs. **No** LAS/LAZ/PLY/E57/PCD/XYZ point-cloud files
  and **no** OBJ/STL/PLY meshes. (PNG vs JPG inside the zips not stated  - 
  inferred as raster images.)
- **Size:** ~2.97 GB total (rockmass.zip 2.9 GB, label.zip 32.9 MB,
  4 datasheets ~80-85 KB each). ~29,752 images (200 + 3,000 + 25,584 + 968).
- **Content:** Supervised-learning training data for automated rock joint
  TRACE mapping (behind arXiv:2602.07590). Four sub-datasets:
  (1) Real-world box  -  200 images (gaps = joints);
  (2) Real-world ROCK SLOPE  -  3,000 images from two Norwegian road cuts:
  **E18 Larvik** (~80 m, up to 23 m high, **larvikite** monzonitic
  igneous bedrock, point cloud via SfM on Google Streetview imagery) and
  **Rv4 Roa-Gran** (~200 m, **limestone**, ~8 m high, drone-photo SfM),
  joints manually picked in Maptek PointStudio and fitted with planar
  polygons;
  (3) Synthetic DFN  -  25,584 rendered images (FracMan DFN + Rhino/Grasshopper;
  27 block shapes x 8 textures; perfect labels);
  (4) Synthetic box  -  968 images, five boxes, 1 cm joint openings.
  **The rock-slope items are RENDERED 2D IMAGES of coloured point clouds
  plus binary joint-trace MASKS  -  the underlying 3D point clouds are NOT
  released here.**
- **License:** CC-BY-4.0.
- **Accessibility:** **open** (Zenodo, no login; direct file links;
  account optional).
- **Relevance:** INDIRECT / LOW-to-MODERATE. Ships 2D rendered images +
  2D trace masks, not 3D points, so it cannot feed the
  normal-estimation/region-grow/orientation-clustering pipeline directly.
  Value: (a) geologist-labelled ground-truth joint TRACES to validate a
  downstream trace-extraction step; (b) realistic joint
  persistence/spacing/orientation distributions worth citing
  (larvikite + limestone road cuts); (c) the synthetic-DFN recipe
  (FracMan + Rhino/Grasshopper) is directly relevant tooling. Raw clouds
  (Larvik/Rv4) are referenced as inputs but appear not openly
  redistributed (Streetview-derived SfM may carry source constraints).

### B2. GeoCrack dataset (Harvard Dataverse)

- **Source:** Harvard Dataverse. Authors: M.Y. Ansari, M. Ishaq,
  M.Y. Ansari, V.R.S. Konagandla, T. Al Tamimi, S. Tavani, A. Corradetti,
  T.D. Seers. Lead: Texas A&M (yansari@tamu.edu); collab. Univ. Naples
  Federico II / Univ. Trieste.
- **URL:** https://doi.org/10.7910/DVN/E4OXHQ
- **Format:** PNG images (164 PNG) + binary edge masks (PNG); 224x224
  patch pairs in `patched_images.zip` (~1.03 GB). Split manifests as `.tab`
  (Dataverse) / `.csv` (GitHub). One Python script `Make_Dataset.py`.
  **No 3D point clouds, meshes, or photogrammetric models**  -  2D raster
  imagery and masks only.
- **Size:** ~5.20 GB total (Original Images ~2.07 GB, Cleaned Images
  ~2.07 GB, Edge Masks ~27.5 MB, patched_images.zip ~1.03 GB). 11 sites.
  12,158 patch pairs (224x224); split train 6,079 / test 3,040 /
  validation 3,039.
- **Content:** First large-scale open annotated dataset of natural
  fracture traces from rock outcrops across 11 characterized study areas
  in Europe (Greece, Italy, Malta) and the Middle East (Oman, UAE).
  Lithologies span Cretaceous/Eocene/Oligocene/Paleocene limestones,
  dolomites, ultrabasic peridotite (Balmuccia, Val Sesia), and flysch
  sandstones+marls (Villa Giulia). Acquisition: terrestrial photography +
  UAV/drone; each image cleaned, normalized, manually segmented, then
  recursively vetted to binary fracture-edge masks.
- **License:** **CC0 1.0** (Public Domain Dedication)  -  confirmed from the
  Dataverse API (SPDX `CC0-1.0`). This is the DATA license; it is NOT MIT.
  The GitHub code repo has no LICENSE file  -  code license unspecified.
- **Accessibility:** **open** (no login). DOI landing page or Dataverse API
  (`/api/access/datafile/<id>`). `patched_images.zip` is the ready-to-train
  224x224 corpus.
- **Relevance:** INDIRECT / LOW for the point-cloud worker. Purely 2D:
  orthophoto/UAV outcrop images + hand-digitized binary fracture-TRACE
  masks. No XYZ, no normals, no facets, no 3D scans  -  cannot feed the
  pipeline directly. Upstream value: (1) the 11 carbonate/peridotite/flysch
  outcrops are the same lithology+discontinuity-style family; (2) a trained
  fracture-edge segmenter could pre-segment 2D mesh faces or seed/validate
  3D facet region-growing; (3) trace-derived orientation/spacing statistics
  could cross-check joint-set clustering. Still needs 3D clouds to drive the
  normals->facets->joint-sets worker. CSVs use authors' local Windows paths  - 
  re-path before use.

### B3. GeoCrack GitHub repository (code + split manifests)

- **Source:** YaqoobAnsari (M.Y. Ansari), Texas A&M University.
- **URL:** https://github.com/YaqoobAnsari/GeoCrack-A-High-Resolution-Dataset-of-Fracture-Edges-in-Geological-Outcrops
- **Format:** Python (`.py`) + CSV manifests (`patch_pairs.csv` 12,158;
  `train.csv` 6,079; `test.csv` 3,040; `validation.csv` 3,039). README is
  a 77-byte title-only stub.
- **Size:** Small (scripts + CSVs only; imagery lives on Harvard
  Dataverse). ~6 stars.
- **Content:** Code/manifest mirror for GeoCrack. `Make_Dataset.py` =
  patch-generation/assembly; manifest rows pair "Original Image Patch,
  Binary Mask Image Patch".
- **License:** **UNSPECIFIED** for the code (no LICENSE file). The DATA on
  Dataverse is CC0 1.0; do not assume the repo is MIT.
- **Accessibility:** **open** (git clone / raw download; no login).
- **Relevance:** LOW-INDIRECT. Useful only to regenerate the 224x224
  patching pipeline and the train/val/test split. Not a point-cloud
  source. CSV paths are hardcoded Windows absolute paths  -  remap to the
  Dataverse `patched_images.zip` contents.

### B4. DFN benchmark & breakthrough curves (DECOVALEX-2023 Task F)

- **Source:** Paul Mariner, Sandia National Laboratories (NM, USA). Zenodo.
  Supporting Task F1 Final Report (OSTI 2481307) co-authored with
  R. Leone, E. Stein.
- **URL:** https://zenodo.org/records/14873207
  (DOI 10.5281/zenodo.14873207)
- **Format:** Two ZIP archives of dfnWorks output/definition files
  (Python/LaGriT/PFLOTRAN); network-definition + simulation/transport
  results + moment statistics. **NOT mesh/point-cloud files.** Interior
  file types not enumerated on the page (uncertain without downloading).
- **Size:** ~8.9 MB total (8.5 MB + 394.5 kB).
- **Content:** Simulation/benchmark dataset (NOT a scanned cloud).
  Benchmark Discrete Fracture Networks + tracer breakthrough curves with
  first/second moment statistics from multiple teams. Family: (1) 4
  intersecting fractures; (2) that + 1089 stochastic fractures; (3) a
  continuous-point-source variant. Context: km-scale 3D DFNs based on
  fractured granite at the **Forsmark site, Sweden** (crystalline bedrock,
  nuclear-waste performance assessment).
- **License:** CC-BY-4.0.
- **Accessibility:** **open** (Zenodo, no login).
- **Relevance:** LOW / indirect. Abstract fracture-network geometry +
  flow/transport simulation output, not a scanned cloud  -  no XYZ, no
  normals, no facets. Tangential use as a SYNTHETIC ground-truth source:
  dfnWorks DFN definitions encode each fracture's plane (orientation,
  position, size), so one could generate a synthetic cloud or validate
  set-orientation/spacing statistics against a known DFN. As shipped it
  needs the dfnWorks toolchain and provides no cloud. A validation /
  synthetic-DFN reference, not a primary input.

### B5. Sketchfab.com rock & mineral catalog (Version 1.0)

- **Source:** West Virginia University + Kansas State University
  (NSF RAPID, COVID remote-teaching). Andrews, Brueseke, Himelstein,
  McFarland. Zenodo.
- **URL:** https://zenodo.org/records/3988525
- **Format:** Single PDF catalog ("Sketchfab.com rock & mineral
  catalog.pdf"); the actual 3D assets are textured meshes hosted
  externally on Sketchfab. No point clouds or mesh files attached to the
  Zenodo record.
- **Size:** 1 file, 3.7 MB PDF.
- **Content:** A searchable catalog (snapshot of a Google Sheet) that
  LISTS/links freely available 3D models of minerals and rocks on
  Sketchfab. Models are teaching-oriented photogrammetry meshes of HAND
  SAMPLES / specimens  -  **not rock-mass outcrops, not fracture scans,
  not point clouds.**
- **License:** CC-BY-4.0 on the catalog. Individual Sketchfab models carry
  their own per-model licenses.
- **Accessibility:** **open** (PDF, no login). Geometry requires following
  per-model Sketchfab links; downloadability/license vary per model.
- **Relevance:** VERY LOW / essentially not relevant. A PDF index, not
  data; the linked assets are hand-sample/specimen meshes for teaching
  with no in-situ joint sets, no spacing, no orientation frame to cluster.
  The worker needs metrically-scaled, georeferenced rock-MASS clouds,
  which this does not provide.

---

## C. Reference / method papers

Literature anchoring the per-facet-normal -> region-grow -> orientation+spacing
pipeline. None ship a usable point cloud.

### C1. Slob, van Knapen, Hack, Turner & Kemeny (2005)  -  automated discontinuity analysis from 3D laser scanning

- **Source:** Slob, van Knapen, Hack (ITC/Univ. Twente), Turner (Colorado
  School of Mines), Kemeny (Univ. Arizona). Transportation Research Record
  1913(1):187-194, 2005. DOI 10.1177/0361198105191300118. URL is the
  Univ. Twente green-OA copy.
- **URL:** https://ris.utwente.nl/ws/portalfiles/portal/313350472/slob.pdf
- **Format:** PDF (paper). No bundled dataset.
- **Content:** Foundational method paper defining the canonical pipeline:
  (1) acquire dense TLS cloud (spacing ~5 mm-1 cm); (2) reconstruct a
  meshed (TIN) surface; (3) compute the unit normal of every facet;
  (4) map facet normals onto a hemispherical / Gaussian-sphere projection
  so sets appear as pole-density clusters; (5) identify joint sets with
  **fuzzy k-means**, yielding each set's mean dip/dip-direction;
  (6) remove per-set outliers via a Fisher distribution; (7) compute set
  spacing from assigned facets. Named test sites/rock types not verifiable
  from the abstract  -  UNCERTAIN.
- **License:** Paper   TRB/SAGE; the Twente copy is a green-OA
  author/accepted version. Reference-only; no data license (no dataset).
- **Accessibility:** **open** PDF from the Twente repository (no login).
  Publisher version paywalled at SAGE.
- **Relevance:** HIGHEST. Essentially the reference architecture for the
  consumer: per-facet normals -> meshed surface -> cluster poles into joint
  sets -> derive spacing. The fuzzy-k-means-on-the-Gaussian-sphere step is
  the direct ancestor of DSE (Riquelme), FACETS (CloudCompare), and the
  project's own Watson/mean-shift clustering. Use to justify and cite the
  normals->stereonet->cluster->spacing pipeline and to benchmark the
  project's deterministic clustering against fuzzy k-means. Slob's 2008
  TU Delft PhD thesis is the extended treatment.

### C2. Hurtgen & Detert (2024)  -  a data-driven approach to mapping rock-mass discontinuities

- **Source:** M. Hurtgen, R. Detert, Mine Vision Systems Inc. (Pittsburgh,
  PA). IOP Conf. Ser.: Earth Environ. Sci. 1435:012006, 2024. DOI
  10.1088/1755-1315/1435/1/012006. 1st Intl Rock Mass Classification
  Conference, Oslo, 30-31 Oct 2024.
- **URL:** https://iopscience.iop.org/article/10.1088/1755-1315/1435/1/012006
- **Format:** PDF / HTML (open-access conference paper). No point cloud or
  mesh released.
- **Content:** Industry workflow detecting/classifying rock-mass
  discontinuities from point-cloud data and derived 3D meshes captured
  during normal mine operation, so dip/dip-direction can be extracted
  "at any point" without exposing personnel. Data from Mine Vision
  Systems' photogrammetry/3D-mapping hardware (FaceCapture line). The
  internal algorithm is UNSPECIFIED  -  abstract names no clustering/
  segmentation method (likely semi-automated plane-fitting/picking on the
  mesh); "data-driven" here means "derived from the captured 3D model,"
  **not** machine learning. No rock types, mines, or point counts disclosed.
- **License:** CC BY 4.0 (paper text/figures); no accompanying dataset.
- **Accessibility:** **open** HTML + PDF at IOP (no login).
- **Relevance:** MODERATE  -  context and commercial-validation value, not
  algorithmic depth. Confirms operational demand for automatic
  dip/dip-direction extraction from operational clouds/meshes; useful for
  the "why automate" framing and the field-to-factory / mine-safety angle.
  But a vendor conference abstract  -  no reusable algorithm, code,
  parameters, or benchmark data; weaker than Slob 2005 / DSE / FACETS as a
  method reference. Use for motivation/positioning.

### C3. Shabanimashcool (2025)  -  a robust approach for digital mapping of rock masses with a stereo camera

- **Source:** Mahdi Shabanimashcool (NGI), DINAMINE project lead.
  EU Horizon Europe DINAMINE (grant 101091541). Zenodo (DINAMINE
  community / EU Open Research Repository).
- **URL:** https://zenodo.org/records/16969451
  (DOI 10.5281/zenodo.16969451; concept 10.5281/zenodo.16968983)
- **Format:** Single PDF (`MS_2025_DigitalMappingRockMasses.pdf`,
  ~565 kB). No PLY/LAS/XYZ/PCD/image data attached. (The page's
  "36.7 MB this version / 216.2 MB total" figures are UI artifacts  -  only
  the PDF exists; v1 had an empty files array.)
- **Content:** Conference/method PAPER, not a dataset. A mapping algorithm
  for rock-mass analysis from stereo-camera imagery: generates point
  clouds of exposed rock surfaces, captures both surface texture
  (discontinuity traces) and visible planar elements, identifies planar
  segments = discontinuity surfaces, supports georeferencing of mapped
  discontinuities, and demonstrates in-situ rock block size distribution.
  Argues stereo texture captures traces that LiDAR often misses. No rock
  type or field site confirmed (uncertain).
- **License:** CC-BY-4.0 (the PDF).
- **Accessibility:** **open** PDF from Zenodo (no login).
- **Relevance:** MEDIUM-HIGH as a METHODOLOGICAL reference; ZERO as a
  dataset. The pipeline (stereo cloud -> planar segments = discontinuity
  surfaces -> georeference -> block-size distribution) is the
  photogrammetry-to-discontinuity bridge the worker targets, overlapping
  the planar-facet-extraction and joint-set-characterisation steps. Useful
  to compare the worker's per-facet-normal + region-grow +
  orientation/spacing clustering against a published stereo-camera
  approach; its emphasis on discontinuity TRACES (texture) complements
  purely geometric methods. Ships no cloud, no normals, no labels  -  a
  literature/method reference only.

### C4. RockAlign / RockCloud-Align (reference paper)  -  Remote Sensing 2025, 17(2):345

- **Source:** Yunbiao Wang, Dongbo Yu, Lupeng Liu, Jun Xiao (School of AI,
  UCAS). Remote Sensing (MDPI) 2025, Vol 17, Issue 2, Art. 345.
  DOI 10.3390/rs17020345 (pub 2025-01-20). Funded by NSFC, Beijing NSF,
  China Postdoctoral Science Foundation.
- **URL:** https://www.mdpi.com/2072-4292/17/2/345
- **Format:** HTML + PDF article (~2.8 MB PDF), CC BY. eISSN 2072-4292;
  43 references. The registration network reads raw XYZ point sets
  (~20k pts/cloud); normals are not a required input.
- **Content:** **RockAlign is the METHOD; RockCloud-Align is the DATASET
  it introduces.** Task = rigid pairwise rock-mass point-cloud
  REGISTRATION (estimate a 4x4 transform aligning a source rock-mass scan
  to a target of the same outcrop). It is **NOT** a discontinuity/joint-set
  dataset and ships no joint labels or normals. The network builds on
  **GeoTransformer with a KPConv backbone** and an overlap-aware attention
  mechanism, deliberately "eliminating dependence on feature points and
  RANSAC" (KPConv learns geometric features directly; no precomputed
  normals or FPFH as input). Baselines: 3DSN, FCGF, D3Feat, Predator,
  OMNet, DGR, PCAM, GeoTransformer, RegTR. The paper specifies the
  acquisition hardware (Leica P30, HDS6000), scene types (fractured rock
  -> cliff faces), provenance (Beijing, Qinghai, Ontario), point-density
  ranges, and ground-truth methodology  -  read this to know what the
  Baidu download contains. **The dataset file format is NOT documented in
  the paper** (no extension, columns, or normals stated); the XYZ-.txt
  conclusion is inferred from the file count and the GeoTransformer/3DMatch
  lineage. **Data Availability Statement is empty**  -  the data lives on
  Baidu AI Studio #294264.
- **License:** CC BY 4.0 (Gold OA, MDPI; confirmed via Crossref + DOAJ +
  Semantic Scholar).
- **Accessibility:** **open** (CC BY) but the MDPI host **blocks scripted
  fetch** (Cloudflare, HTTP 403 via curl/WebFetch). Use the DOI in a
  browser, or the Jina reader proxy
  (`https://r.jina.ai/https://www.mdpi.com/2072-4292/17/2/345`). Indexed
  open-access in DOAJ (record b28f6bbd44754e3faa481a8f38f53994); mirrored
  at The Free Library (also Cloudflare-protected).
- **Relevance:** INDIRECT for joint-set extraction; the **dataset** (A1/A2)
  is the practical asset. Clean, ICP-refined, single-outcrop rock-face
  clouds at ~20k points are good inputs to a worker that estimates
  per-facet normals, region-grows facets, and clusters joint sets. But the
  dataset ships NO discontinuity labels, NO orientation/dip-direction
  ground truth, and NO per-point normals  -  it labels only registration
  transforms. The paper's density/overlap taxonomy is useful for
  stress-testing normal-estimation robustness. Treat as a source of real
  rock-face geometry, not a discontinuity benchmark.
- **Citation:** Wang, Y.; Yu, D.; Liu, L.; Xiao, J. RockCloud-Align: A
  High-Precision Benchmark for Rock-Mass Point-Cloud Registration. Remote
  Sens. 2025, 17, 345. https://doi.org/10.3390/rs17020345

### C5. RockAlign GitHub repository (code + trained weights, NOT data)

- **Source:** Org `RockAilab` on GitHub. Created 2024-09-14, last push
  2025-12-10. Default branch `master`. 2 stars. Release 1.0.0.
- **URL:** https://github.com/RockAilab/RockAlign
- **Format:** `README.md` (text), `weight.pth.tar` (PyTorch checkpoint
  tarball, 67,671,788 bytes / 67.7 MB, real binary not an LFS pointer;
  SHA 2d811a78), `.gitignore`.
- **Size:** ~11 KB tracked metadata + the 67.7 MB checkpoint.
- **Content:** Sparse repo. **NO data, NO data loader (no `dataset.py`),
  NO training/eval scripts, NO documentation of the `.txt` format, NO
  LICENSE file.** README is a near-empty merge-conflict stub whose only
  content is the dataset download link (`bit.ly/RockCloud-Align` -> Baidu),
  ending with "# git 2025 08 18". The 1.0.0 release attaches no data
  assets. The data is NOT in the repo.
- **License:** No license file (repo `.license` = null via API). Code/weights
  effectively all-rights-reserved unless stated otherwise. (Dataset on Baidu
  tagged GPL-2.0; paper CC BY.)
- **Accessibility:** **open** (public GitHub; `git clone` includes weights).
  Dataset must come from the Baidu link.
- **Relevance:** LOW for discontinuity extraction directly  -  ships a
  registration model, not joint-set tooling. Useful only to (a) pre-align
  overlapping rock-face scans before discontinuity analysis, or (b) confirm
  the GeoTransformer/KPConv data convention implying the XYZ-`.txt` format.
  No facet/normal/joint-set code. Because the repo lacks a data loader,
  the authoritative `.txt` column spec is unavailable from code too  - 
  reinforcing that you must open a sample file to confirm format.

---

## D. RockBench reference corpus

The canonical open repository the automated joint-set / discontinuity-extraction
literature benchmarks against. The original host is down; recover via the
Wayback Machine and redistributors.

### D1. RockBench repository (original site, www.rockbench.org)

- **Source:** Founders R.M. Harrap (Queen's Univ., Kingston, ON),
  M.J. Lato, J. Kemeny (Univ. Arizona), G. Bevan. Co-hosted at Queen's
  (geol.queensu.ca/faculty/harrap/RockBench/).
- **URL:** http://www.rockbench.org/
- **Format:** Point clouds (XYZ / XYZI intensity / XYZ-RGB), plus meshed
  surface models for some sites; metadata records.
- **Size:** ~10 documented test sites at launch (2013); point counts vary
  (e.g. Ouray ~1.0-1.5M points). Total repository size not published.
- **Content:** Prototype open-access repository of rock-mass reference
  point clouds + metadata for benchmarking LiDAR/photogrammetry rock-mass
  characterization algorithms. ~10 test sites (Canada, USA, Europe) plus
  simple regular-geometry test objects; each site had local-geology docs,
  photos, raw LiDAR and/or photogrammetry clouds, and access notes.
- **License:** Open access intended ("open access into the future"); no
  formal per-dataset license found. UNCERTAIN  -  treat as
  attribution-by-citation unless a redistributor states otherwise.
- **Accessibility:** **dead-link / archived.** `www.rockbench.org` returns
  connection-refused (ECONNREFUSED) as of 2026-06-14; the Queen's mirror
  is also gone. Recover landing/download pages via the Wayback Machine;
  recover datasets via redistributors (D5, D6).
- **Relevance:** DIRECT  -  THE canonical reference corpus the automated
  joint-set literature benchmarks against. The worker should validate
  against these clouds for like-for-like comparison with DSE/region-growing
  papers.
- **Paper:** Lato, Kemeny, Harrap, Bevan (2013), "Rock bench: Establishing
  a common repository and standards for assessing rockmass characteristics
  using LiDAR and photogrammetry," Computers & Geosciences 50:106-114,
  DOI 10.1016/j.cageo.2012.06.014.

### D2. RockBench data-downloads page (archived)

- **Source:** Queen's University (R.M. Harrap); Internet Archive Wayback
  Machine.
- **URL:** http://web.archive.org/web/20171026142438/http://geol.queensu.ca/faculty/harrap/RockBench/dataDownloads/index.html
- **Format:** Archived HTML index linking to point-cloud files.
- **Size:** n/a (index page).
- **Content:** The actual download-index page (`dataDownloads` directory)  - 
  where the per-site point-cloud download links lived.
- **License:** n/a.
- **Accessibility:** **archived** (Wayback snapshot, status 200, captured
  2017-10-26; confirmed via the IA availability API). To read the link
  list, open the URL in a browser. UNVERIFIED whether the crawl captured
  the downstream binary point-cloud files (Wayback often archives the HTML
  index but not large linked binaries).
- **Relevance:** The precise historical entry point to the downloadable
  clouds; needed to enumerate exact filenames/links if the live site cannot
  be recovered. A second archived landing page also exists:
  http://web.archive.org/web/20170725165722/http://geol.queensu.ca/faculty/harrap/RockBench/styled/index.html
  (captured 2017-07-25, status 200).

### D3. RockBench standards / metadata schema (the "standards" deliverable)

- **Source:** Lato, Kemeny, Harrap, Bevan (2013), Computers & Geosciences
  50:106-114.
- **URL:** https://www.sciencedirect.com/science/article/abs/pii/S0098300412002099
- **Format:** Documentation / metadata schema (Dublin Core-based).
- **Size:** n/a.
- **Content:** Not a dataset  -  the proposed STANDARDS. (1) Two principles:
  accessible, fully documented test sites; and open test databases so any
  researcher can run their algorithm on the same data (modeled on the Lena
  image / Stanford Bunny). (2) A three-tier metadata structure:
  data-collection metadata (Dublin Core: creator, contributor, date,
  coverage, format, description, identifier, rights, source, subject),
  approach/sensor metadata (sensor characteristics + acquisition
  parameters, split into abstract sensor / specific device / per-acquisition),
  and site metadata (links between datasets, users, related content). Goal:
  meaningful comparison of sensors, field methods, and processing
  algorithms. Listed sites: Hwy-15 Sunbury ON, Kingston ON
  (limestone/granite/gneiss), Ouray CO quartzite, Bergen Norway limestone
  roadcut, Tucson AZ open-pit, El Paso TX gneiss roadcut, Oslo Norway
  amphibolite-gneiss tunnel, Albany NY weathered gneiss. Per-site point
  counts/formats not fully tabulated  -  UNCERTAIN beyond Ouray.
- **License:**   Elsevier (paper); standards themselves openly described.
- **Accessibility:** **open** abstract + bibliographic data; full PDF
  paywalled at Elsevier (403 on direct fetch). Open copies on Academia.edu
  (https://www.academia.edu/85756947/) and ResearchGate
  (https://www.researchgate.net/publication/256938742). ADS:
  https://ui.adsabs.harvard.edu/abs/2013CG.....50..106L/abstract.
- **Relevance:** Medium-direct  -  the schema tells you what provenance to
  record per cloud (sensor, spacing, acquisition params, coordinate frame)
  so joint-set results are reproducible and comparable; the benchmarking
  principle is exactly the use case (run the worker on the shared clouds,
  compare set counts/orientations against published results).

### D4. Internet Archive Wayback Machine  -  recovery route for RockBench pages

- **Source:** Internet Archive.
- **URL:** http://archive.org/wayback/available?url=geol.queensu.ca/faculty/harrap/RockBench/dataDownloads/index.html
- **Format:** Archived HTML (and possibly some linked files).
- **Size:** n/a.
- **Content:** The only confirmed-working route to original RockBench
  content. The availability API returns valid 200-status snapshots for the
  dataDownloads index (2017-10-26) and the styled landing page (2017-07-25).
- **License:** n/a.
- **Accessibility:** **archived** (API reachable). Query
  `archive.org/wayback/available?url=<rockbench path>` to get the snapshot
  URL, then open `http://web.archive.org/web/<timestamp>/<original-url>`.
  Use the `id_` raw form (`web/<timestamp>id_/<url>`) to fetch link lists.
  For binary point clouds not captured by Wayback, fall back to
  redistributors (Riquelme, D5/D6) or email the authors (Harrap @ Queen's,
  Kemeny @ Arizona).
- **Relevance:** Indirect but essential  -  the path to reconstruct the exact
  dataset inventory/filenames if you need the full original list beyond
  Ouray + simple-figures. Confirmed snapshots: dataDownloads/index.html ->
  20171026142438 (200); styled/index.html -> 20170725165722 (200).

### D5. Ouray (Colorado) rock-slope LiDAR  -  the flagship RockBench dataset

- **Source:** Acquired by John Kemeny (Univ. Arizona), 2004; published via
  RockBench. Now redistributed by Adri n Riquelme (Univ. Alicante) on his
  personal site and via a ResearchGate DOI.
- **URL:** https://personal.ua.es/en/ariquelme/a-new-approach-for-semi-automatic-rock-mass-joints-recognition-from-lidar-data.html
- **Format:** XYZ / XYZ-RGB (also described as XYZI with
  intensity/reflectance). Plain-text point list, loads in CloudCompare /
  PolyWorks.
- **Size:** ~1,515,722 points (Riquelme redistribution); point spacing
  < 2 cm. An alternative figure of 1,024,521 points appears in some sources
  (likely a different scan-station subset)  -  UNCERTAIN which count matches
  a given download; verify after download.
- **Content:** Terrestrial LiDAR scan of a road-cut / natural rock slope in
  Ouray, Colorado, USA. Rock type quartzite / orthoquartzite; natural slope
  exhibiting wedge failure. The de-facto standard benchmark cloud for
  discontinuity-set / joint-set extraction algorithms. Acquired with an
  Optech ILRIS-3D time-of-flight TLS.
- **License:** Not explicitly stated; redistributed for research with
  attribution to RockBench + Kemeny. UNCERTAIN  -  cite RockBench
  (Lato et al. 2013) and the acquisition (Kemeny 2004).
- **Accessibility:** **open** (recoverable via the Riquelme redistribution /
  ResearchGate DOI). Original RockBench copy dead/archived. ResearchGate
  DOI: http://dx.doi.org/10.13140/RG.2.2.11917.82403 (ResearchGate login
  may be required to pull the file).
- **Relevance:** HIGHEST. The single most-used real-world test cloud in the
  automated-discontinuity literature (Riquelme DSE 2014, Chen 2016, and
  many since). Running the per-facet-normal -> region-grow ->
  orientation/spacing worker on Ouray gives a direct, published,
  reproducible comparison: DSE reports ~5 discontinuity sets on this exact
  cloud. Use it as the **primary regression cloud.** Used by Riquelme
  et al. 2014 (Computers & Geosciences 68:38-52,
  DOI 10.1016/j.cageo.2014.03.014).

### D6. Simple geometric figures (regular-geometry test objects)  -  RockBench/Riquelme calibration set

- **Source:** Scanned with LiDAR at the University of Lausanne by Antonio
  Abell n; distributed alongside the Riquelme DSE method; the "regular
  geometries" RockBench advertised.
- **URL:** https://personal.ua.es/en/ariquelme/a-new-approach-for-semi-automatic-rock-mass-joints-recognition-from-lidar-data.html
- **Format:** XYZ / XYZ-RGB text, ready for CloudCompare.
- **Size:** Small (synthetic-scale test objects); exact point counts not
  published  -  UNCERTAIN.
- **Content:** Point clouds of simple known shapes (e.g. a pyramid built
  from blocks, planar/box forms) with ground-truth planar orientations.
  Used to validate that a planar-facet extractor recovers known plane
  normals before being trusted on real rock.
- **License:** Research use with attribution; not explicitly stated  - 
  UNCERTAIN.
- **Accessibility:** **open** (via Riquelme site / ResearchGate). Exact
  standalone DOI not confirmed  -  bundled with the DSE materials.
- **Relevance:** High for unit-testing the facet-normal + region-grow stage:
  known plane orientations let you measure angular error of estimated facet
  normals and verify the orientation-clustering step recovers the correct
  number of families before running on noisy rock. Distinct from the Ouray
  real-rock cloud  -  this is the controlled-geometry validation set.

---

## Status / TODO

| Dataset | Direct point-cloud input? | Status |
| --- | --- | --- |
| A1/A2 RockCloud-Align (Baidu #294264) | Yes (XYZ `.txt`, inferred) | **PENDING**  -  Baidu login required; not downloaded; verify file format on first file |
| A4 Granite Dells TLS (OpenTopography) | Yes (LAS/LAZ, 4.97M pts) | **PENDING**  -  open download via portal; bulk needs free OT login |
| A5 Granite Dells airborne (sibling) | No (too coarse, ref only) | PENDING / reference only |
| D5 Ouray rock-slope LiDAR | Yes (XYZ/RGB, ~1.5M pts) | **PENDING**  -  recover via Riquelme / ResearchGate; **primary regression cloud** |
| D6 Simple geometric figures | Yes (XYZ, calibration) | **PENDING**  -  recover via Riquelme; unit-test the facet stage |
| D1/D2/D4 RockBench (orig + archive) | Mixed | Original site DOWN; recover via Wayback + redistributors |
| B1 NTNU/NGI joint-trace images | No (2D images/masks) | PENDING  -  open Zenodo; reference/validation only |
| B2/B3 GeoCrack | No (2D images/masks) | PENDING  -  open Dataverse (CC0); reference/validation only |
| B4 DECOVALEX DFN benchmark | No (DFN simulation) | PENDING  -  open Zenodo; synthetic-DFN reference only |
| B5 Sketchfab catalog | No (hand-specimen meshes) | Not relevant; skip |
| C1-C5, D3 reference papers | No | Read as method/standards references |

Note: the project memory records earlier work on Granite Dells TLS,
Tongjiang quarry, and Stanford scans under `Data/` and
`Template-General/raw/2026-05-27/`  -  cross-check those local assets before
re-downloading A4.

## Reading list (RockBench reference list)

(none provided)

File written to: D:\code_ws\rockfaces_dataset.md