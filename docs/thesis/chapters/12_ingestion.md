# 12. Data Ingestion & Format Readers

This chapter covers the repository's data-ingestion subsystem: the `Ingest`
ribbon tab (5 components), the `Frahan.Masonry.Quarry.Ingestion` reader family
in the Core assembly, and the `Frahan.Core.ScanIngest` point-cloud readers and
out-of-process workers. The subsystem is the front door of the whole pipeline.
Every downstream chapter (nesting, quarry block-cutting, masonry, surface
packing, edge-matching) consumes geometry that entered the repository through a
reader documented here.

The design goal stated across the source is **max-coverage ingest**: read the
formats a stone-fabrication site actually produces, route each by file
extension to a dedicated reader, and never guess a proprietary binary layout.
The reader set spans vector fracture data (ESRI Shapefile, GeoJSON), terrestrial
LiDAR and photogrammetry point clouds (E57, LAS/LAZ, PLY, XYZ), and
ground-penetrating-radar radargrams (CSV, SEG-Y, MALA RD3, pulseEKKO DT1, IDS
GeoRadar DT, GSSI DZT). Where a format is proprietary with no open spec, the
reader is a deliberate dead-stop that tells the user how to convert, rather than
a silent corruptor of the depth axis.

This chapter is mostly engineering, not new mathematics: a reader's job is to
decode a documented byte layout faithfully. The two places real derivation
appears are the GPR sample-spacing recovery (turning a time axis into a metric
depth axis) and the streaming voxel-downsample (bounding memory by occupied
voxels, not input point count). Where a component merely wraps a third-party
library, it is named **vendored-library** or **wrapper-of-native** by its
upstream and licence, per the originality scheme of `90_originality.md`. The GPR
processing chain itself (migration, Hilbert energy, fracture extraction) lives
in chapter 4; this chapter stops at the clean radargram, point cloud, or
polyline the readers emit.

---

## 12.1 The ingestion architecture

The subsystem is two-layered, matching the Rhino-free Core convention. A pure
managed reader in `Frahan.Masonry.Quarry.Ingestion` or `Frahan.Core.ScanIngest`
parses bytes to a plain POCO (a `GprRadargram`, a `FractureTraceCollection`, a
flat `xyz` array); a thin Grasshopper component in the `Ingest` tab adapts that
POCO to RhinoCommon geometry on the canvas. No reader references RhinoCommon, so
all parsing is unit-testable headless and a format can be exercised without
Rhino running. The five canvas components in the `Ingest` subcategory are GPR
File Loader, GPR Radargram Mesh, GPR Picks From Points, Vector Fractures Loader,
and Import Photo Markers; the point-cloud readers (Load E57 Cloud, Read LAS/LAZ
Cloud) are filed under the `Mesh` subcategory but share the same ingestion
contract and are covered here.

Three rules govern every reader and are visible throughout the source.

**Dispatch by extension, single entry point.** `GprFileReader.Load` and
`VectorFractureReader.Load` are switch statements over the lowercased file
extension that route to the matching format reader, so the canvas component does
not have to know whether the user dropped a `.shp` or a `.geojson`, a `.rd3` or a
`.dzt` (`GprFileReader.cs:30-56`, `VectorFractureReader.cs:21-32`).

**Never bulk-pipe raw samples to the canvas.** A radargram can carry millions of
`int16` amplitudes and a LiDAR scan hundreds of millions of points. Piping that
through a Grasshopper data tree crashes the document (KB-1, the large-mesh
autosave trap). The GPR File Loader therefore emits only trace-origin points,
counts, and spacing, and documents that per-sample access goes through the Core
reader directly (`GprFileLoaderComponent.cs:28-33`). The point-cloud readers
assemble exactly one `PointCloud` object, never thousands of loose points
(`LoadE57CloudComponent.cs:21-23`).

**Bridge, do not guess.** A proprietary container with no open binary spec is
refused with an actionable error, not parsed on a guess (section 12.5).

---

## 12.2 Vector fracture readers (Shapefile / GeoJSON)

Fracture-trace data from UAV photogrammetry and field mapping arrives as ESRI
Shapefiles or GeoJSON. `VectorFractureReader` dispatches `.shp` to
`ShapefileFractureReader` and `.geojson`/`.json` to `GeoJsonFractureReader`,
both of which delegate the actual format parsing to **NetTopologySuite.IO.Esri**
and **NetTopologySuite.IO.GeoJSON** (`ShapefileFractureReader.cs:5-6,34`,
`GeoJsonFractureReader.cs:5-7,34`).

The reader's own work is the geometry-to-trace mapping, not the byte decode. It
walks each feature's `Geometry`: a `LineString` becomes one `FractureTrace` (its
`Coordinate[]` projected to 2-D `TracePoint2D` vertices), and a
`MultiLineString` recurses into its parts; points and polygons are silently
skipped, because this reader is for linear fracture traces specifically
(`ShapefileFractureReader.cs:45-67`). The feature attribute table is flattened
to a string-keyed dictionary and carried on each trace, so per-trace metadata
(aperture, set id, confidence) survives ingest
(`ShapefileFractureReader.cs:80-91`).

Coordinate reference handling is deliberately conservative. The companion `.prj`
is read verbatim into `CrsWkt` and stored, but the reader does **not** reproject;
the caller decides whether a Loviisa file in `EUREF_FIN_TM35FIN` metres needs
transforming (`ShapefileFractureReader.cs:31,93-105`,
`VectorFracturesLoaderComponent.cs:29-32`). GeoJSON carries no CRS under RFC 7946,
so its `CrsWkt` is empty and the file is assumed already in the desired frame
(`GeoJsonFractureReader.cs:11-19`). This is the correct posture: a reader that
silently reprojected would introduce a datum error invisible to the user.

The `Vector Fractures Loader` component (VecFrac, GUID
`F2D00BEC-2026-4522-B0B0-1ABE15A0DEAD`) emits one open `PolylineCurve` per trace
in source CRS units, plus the CRS WKT, the trace count, and the attribute keys
and values as parallel `{trace_index}` data trees
(`VectorFracturesLoaderComponent.cs:65-83,104-129`).

> **Originality.** **vendored-library.** Both vector readers are thin adapters
> over NetTopologySuite.IO.Esri and NetTopologySuite.IO.GeoJSON; only the
> geometry-to-`FractureTrace` mapping and the `.prj` carry-through are ours. The
> `[Algorithm]` attribute names the standard explicitly: "ESRI Shapefile / OGC
> Simple Features (standard) ... industry format via NetTopologySuite.IO.Esri;
> not a paper" (`VectorFracturesLoaderComponent.cs:38-39`). NetTopologySuite is
> permissively licensed (BSD-3-style); attribution is owed in
> `THIRD_PARTY_NOTICES`, no copyleft (NTS 2023). This is also covered from the
> fracture-mapping angle in chapter 4.

---

## 12.3 Point-cloud readers and the streaming voxel downsample

The point-cloud path targets two scan modalities: registered terrestrial LiDAR
(E57, LAS/LAZ) and photogrammetric dense clouds (PLY, XYZ/PTS). The shared
problem is size: a single airborne tile in this corpus is a 357-million-point
LAZ. Materialising that as a full `double[]` would exhaust memory before any
downstream step runs. The repository's answer is a streaming voxel hash-grid
that reduces during the read.

### 12.3.1 The streaming voxel grid

`StreamingCloudReader` reads PLY (binary and ASCII) and plain XYZ/PTS forward,
one point at a time, folding each straight into a `VoxelGridSink`
(`StreamingCloudReader.cs:10-32`). The sink keys each point by its integer voxel
index and accumulates a running centroid:

$$
\mathbf{k}(\mathbf{p}) = \Bigl(\bigl\lfloor \tfrac{p_x}{v}\bigr\rfloor,\ \bigl\lfloor \tfrac{p_y}{v}\bigr\rfloor,\ \bigl\lfloor \tfrac{p_z}{v}\bigr\rfloor\Bigr),
\qquad
\bar{\mathbf{p}}_{\mathbf{k}} = \frac{1}{n_{\mathbf{k}}}\sum_{\mathbf{p}\,:\,\mathbf{k}(\mathbf{p})=\mathbf{k}} \mathbf{p},
$$

with $v$ the voxel edge length. The grid stores one $(\text{sum}, \text{count})$
pair per occupied voxel and emits one centroid per occupied voxel at the end. The
key observation is that **peak memory is bounded by the number of occupied
voxels, not the input point count**: a forward-only stream never builds a full
array of all input points, so a 28-million-point file collapses straight into
the grid as it streams (`StreamingCloudReader.cs:14-17,25-27`). The same hash
key matches `VoxelDownsampleComponent.ManagedVoxelDownsample`, so the streaming
path and the in-memory path agree centroid-for-centroid.

The compressed-LiDAR reader `LazCloudReader` wraps **Unofficial.laszip.net**, a
pure-managed C# port of LASzip (Isenburg 2013) that reads both uncompressed
`.las` and compressed `.laz` on net48. It streams points one at a time straight
into the same `VoxelGridSink`, so a 357-million-point LAZ reduces to a manageable
centroid cloud with memory bounded by occupied voxels
(`LazCloudReader.cs:9-31,41-55`). `laszip_get_coordinates` applies the LAS
header scale and offset internally, so the doubles handed to the sink are already
real-world UTM coordinates, equivalent to

$$
\mathbf{p}_{\text{real}} = \mathbf{s}\odot\mathbf{p}_{\text{raw,int}} + \mathbf{o},
$$

with header scale $\mathbf{s}$ and offset $\mathbf{o}$
(`LazCloudReader.cs:18-20`).

> **Originality.** `StreamingCloudReader` and the `VoxelGridSink` centroid grid
> are **clean-room** (elementary spatial-hash quantisation; the PLY header parse
> follows the same byte-level approach as the mesh reader). `LazCloudReader` is
> **vendored-library**: laszip.net (LGPL-style, net48-compatible) does the LAS/LAZ
> decode; only the stream-into-voxel-sink wiring is ours (Isenburg 2013; ASPRS
> LAS 1.4). PLY format per Turk 1994; the XYZ/PTS path is a plain ASCII scan.

### 12.3.2 The E57 out-of-process worker

E57 (ASTM E2807-11) is the standard registered-terrestrial-LiDAR exchange
format, and it is the hardest ingest in the subsystem for two reasons: there is
no managed .NET E57 reader, and parsing a multi-GB scan in-process inside Rhino
risks both an out-of-memory condition and a native fault that takes down the
host. The repository solves both with an **out-of-process worker**.

`Load E57 Cloud` (GUID `E4F5A6B7-3230-4F5E-A6B7-C8D9E0F12345`) shells out to a
Python worker, `frahan_e57_worker.py`, that uses `pye57` + `numpy` to read the
scans, voxel-downsample, and write a compact binary-little-endian PLY; the
component then reads that PLY back in chunks and assembles a single
`PointCloud` (`E57CloudWorker.cs:27-37`, `LoadE57CloudComponent.cs:13-31`). This
mirrors the same pattern used for surface reconstruction
(`OutOfProcessReconstructor`) and subprocess fracture detection: a crash kills
only the worker, never Rhino. The worker is launched with redirected stdout and
stderr, a 600-second timeout, and a `PROGRESS` line protocol surfaced as
component status; a `SUMMARY` line carries the all-numeric result that the C#
runner parses without a JSON dependency (`E57CloudWorker.cs:52-128,131-159`).

The worker's voxel downsample is a pure-numpy sort-reduce: it encodes each
point's per-axis voxel index (offset to non-negative) into one `int64` linear
key, sorts, and segment-reduces with `np.add.reduceat`, so only the cloud extent
drives the key range and large UTM coordinates are fine
(`frahan_e57_worker.py:37-59`). The per-scan downsample is followed by a final
merge-and-downsample pass so voxels straddling scan boundaries collapse
correctly (`frahan_e57_worker.py:122-125`).

**The coordinate shift (precision derivation).** PLY stores coordinates as
`float32`, which carries about 24 bits of mantissa, roughly 7 significant decimal
digits. A projected UTM coordinate is order $10^6$ metres, so a raw `float32`
store would resolve only to about $10^6 / 10^7 = 0.1$ m, far coarser than a
scan's sub-millimetre detail. The worker therefore subtracts an integer-metre
global offset (the floor of the bounding-box minimum) before the `float32` cast,

$$
\mathbf{s} = \lfloor \mathbf{p}_{\min} \rfloor, \qquad
\mathbf{p}_{\text{ply}} = \bigl(\mathbf{p} - \mathbf{s}\bigr)\ \text{as float32},
$$

so the stored magnitudes are bounded by the cloud's extent (tens to hundreds of
metres), where `float32` keeps sub-millimetre accuracy
(`frahan_e57_worker.py:127-131`, `E57CloudWorker.cs:11-25`). The shift is
reported back as the component `Shift` output; adding it to the cloud restores
the georeferenced position, so precision and georeferencing are both preserved
(`LoadE57CloudComponent.cs:87-90,216-224`). Bounds are reported in the original
unshifted frame.

The component is non-blocking: it derives from `AsyncScanComponent` with a
default-false `Run` gate, so the worker run and the chunked ingest happen on a
background thread and the canvas never freezes (the source/terminal-node async
convention of chapter 10; `LoadE57CloudComponent.cs:29-31,36-37`). The flat `xyz`
is read off-thread, and the `PointCloud` is built on the UI thread in
`EmitResult` in million-point blocks, keeping the transient `Point3d[]` bounded
(`LoadE57CloudComponent.cs:103-112,188-213`).

> **Originality.** **wrapper-of-native.** The E57 decode is `pye57` (a binding
> over the libE57Format C++ library) driven out-of-process; the voxel sort-reduce
> is a clean-room numpy kernel. The component owns the subprocess orchestration,
> the chunked PLY read-back, and the coordinate-shift precision scheme; the heavy
> format parse is not ours. The `[Algorithm]` attribute states it plainly:
> "Frahan-original; subprocess isolates the E57 parse from Rhino, coords shifted
> to origin" (`LoadE57CloudComponent.cs:33-35`). Runtime deps (python + pye57 +
> numpy + the worker `.py` beside the `.gha`) are external and absent from the
> default install, so no native E57 library ships in the default path. E57 per
> ASTM E2807-11.

---

## 12.4 GPR radargram readers

Ground-penetrating radar is the deepest reader set: six binary or text formats,
each with a different header and sample encoding. The single entry point
`GprFileReader.Load` dispatches by extension to CSV, SEG-Y (`.sgy`/`.segy`), MALA
RD3 (`.rd3`), Sensors & Software pulseEKKO DT1 (`.dt1`), IDS GeoRadar GRED DT
(`.dt`), and GSSI DZT (`.dzt`) (`GprFileReader.cs:30-46`). Each reader returns
the same `GprRadargram` POCO so the canvas does not branch on format.

### 12.4.1 The format decoders

Each reader implements a documented byte layout. **SEG-Y** (SEG standard,
revisions 0/1/2) is the industry interchange format: a 3200-byte EBCDIC textual
header, a 400-byte binary header carrying sample count and interval and the
sample-format code, then per-trace 240-byte headers and sample blocks. The
reader handles format codes 1 (4-byte IBM-360 float, decoded), 2 (int32 BE), 3
(int16 BE), and 5 (IEEE-754 BE), with format 8 raising a clear
`NotSupportedException`; source and receiver coordinates come from trace-header
bytes 73-80 scaled by the coordinate scalar at bytes 71-72 per SEG-Y rev1
(`GprSegYReader.cs:8-35,51-56`).

**MALA RD3** pairs a binary `.rd3` of int16 little-endian samples (rows = traces,
columns = samples) with an ASCII `.rad` header; the layout was reverse-engineered
from the open RGPR R-package source (Huber and Hans 2018) and the official MALA
format appendix (`GprMalaRd3Reader.cs:9-37`). **pulseEKKO DT1** pairs a binary
`.DT1` (a 25-float per-trace header plus int16 samples) with an ASCII `.HD`
header, decoded against the public-domain USGS Open-File Report 02-166 spec
(Lucius and Powers 1999; `GprDt1Reader.cs:9-36`). **GSSI DZT** is a single file
with a 1024-byte-per-channel header followed by raw scans of 8/16/32-bit
little-endian samples, cross-referenced from the BSD-3 `readgssi` library and
RGPR and validated against a real granite file
(`GprDztReader.cs:8-33`). **IDS GeoRadar GRED DT** is a record-structured `.dt`
with a `V`-magic header, fixed `len_rec` stride, and `R`-flagged trace records,
with physical scaling from the companion `.hdr_dt`; the public file structure was
understood with reference to RGPR's `readIDS.R` and reimplemented clean-room,
with strict trailing-byte validation that throws on mismatch so the caller can
fall back to a SEG-Y export (`GprIdsDtReader.cs:9-32,62-63`).

### 12.4.2 The depth-axis derivation

A radargram's native vertical axis is **two-way travel time**, not depth. Every
reader must turn the time axis into the metric sample spacing the rest of the
pipeline expects, and the conversion differs by what the header supplies. The
governing relation is the standard GPR depth equation: a wave at velocity $v$
travels down and back, so a one-way depth $z$ corresponds to a two-way time
$t = 2z/v$, giving a per-sample depth step

$$
\mathrm{d}z = \frac{v\,\mathrm{d}t}{2},
$$

where $\mathrm{d}t$ is the sample interval in time and $v$ is the medium
velocity. The IDS reader applies this directly when the companion header gives
both the time-cell and the propagation velocity:
$\mathrm{d}z = T_{\text{cell}}\cdot v_{\text{prop}} / 2$ for the two-way path
(`GprIdsDtReader.cs:67`). Critically, it also carries the **velocity-independent**
true two-way sample interval $\mathrm{d}t$ in nanoseconds separately
(`GprIdsDtReader.cs:69-71,84`), so a downstream step can rescale depth with the
correct stone velocity rather than the value baked in at ingest.

Where a reader has no medium velocity it standardises on the free-space two-way
constant. The MALA and pulseEKKO readers convert with $c_0/2 = 0.15$ m/ns (the
vacuum two-way step), and document that a caller needing a dielectric-corrected
depth in, say, granite ($\varepsilon_r \approx 5.6$, $v \approx 0.13$ m/ns) must
rescale `GprTrace.SampleSpacingMetres` themselves
(`GprMalaRd3Reader.cs:28-32`, `GprDt1Reader.cs:33-35`). This separation, a
neutral free-space spacing at ingest plus a preserved time interval, is the
correct contract: ingest must not silently commit to a velocity the survey did
not record. The downstream `RadargramProcessor` consumes the preserved
$\mathrm{d}t$ when present and only falls back to recovering it from the metres
step at vacuum velocity when it is unknown (chapter 4).

### 12.4.3 The canvas components

`GPR File Loader` (GprLoad, GUID `F2D00BEC-2026-4523-B0B0-2ABE15A0DEAD`) emits
the trace count, one trace-origin `Point3d` per trace, the sample spacing, the
sample count, and the echoed source path; it explicitly does not pipe sample
amplitudes to the canvas (`GprFileLoaderComponent.cs:73-122`). `GPR Radargram
Mesh` (GprMesh, GUID `F2D05A04-...`) builds the readable thing: a vertical
"curtain" section mesh that follows the survey line in plan and goes down by
sample depth, with each vertex coloured by reflection amplitude
(`GprRadargramMeshComponent.cs:13-19`). `GPR Picks From Points` (GprPicks, GUID
`F2D05A07-...`) is the interactive complement: most GPR files carry no
interpreted reflectors, so the user snaps Rhino points onto the curtain section
and this converts them to reflector picks (recovering true depth by undoing the
display depth scale) plus a reusable picks CSV
(`GprPicksFromPointsComponent.cs:13-21`).

> **Originality.** **clean-room** per-format readers over open or public-domain
> specs (SEG-Y is the SEG standard; pulseEKKO DT1/HD is the public-domain USGS
> OFR 02-166, Lucius and Powers 1999; MALA, DZT, and IDS DT layouts are decoded
> from the open RGPR R-package and the BSD-3 readgssi, with the IDS reader an
> independent clean-room implementation since a binary file layout is not itself
> copyrightable). The dispatcher is a thin switch and adds no algorithm. The
> radargram-mesh and interactive-pick components are **facade-over-primitives**
> (Frahan-original visualisation and pick-conversion over the same readers),
> with `[Algorithm]` attributes naming them Frahan-original
> (`GprRadargramMeshComponent.cs:23-25`, `GprPicksFromPointsComponent.cs:25-27`).
> The GPR processing chain (migration, Hilbert energy, fracture extraction) is
> chapter 4.

---

## 12.5 The proprietary-format dead-stop

The Geoscanners AKULA `.gsf` format is a proprietary container with no open
binary spec. The reader refuses it rather than guessing: `GprFileReader` raises a
`NotSupportedException` whose message tells the user exactly how to proceed,
"Convert it to SEG-Y with GPRSoft or RGPR, then load the resulting .sgy with
this reader" (`GprFileReader.cs:46-51`). The same posture applies to the default
extension fall-through, which lists the supported formats and points proprietary
files to a SEG-Y conversion (`GprFileReader.cs:52-55`).

This is a correctness decision, not a missing feature. A wrong header guess on a
closed container would not fail loudly; it would silently mis-scale the depth
axis or transpose traces, and the error would surface only as a wrong block-yield
estimate three workflows downstream. The honesty boundary is held in source: the
reader names the format as proprietary, confirms no open spec exists, and routes
to the open conversion path. The cost is that any dataset shipping only `.gsf`
needs a one-time GPRSoft or RGPR conversion before it can enter the pipeline.

---

## 12.6 The example and the photogrammetry path

Example 07 (`07_scan_ingest_full`) is the full ingestion entrypoint: pull a raw
site scan into Rhino as clean geometry the downstream workflows consume, across
all three modalities (LiDAR, photogrammetry cloud, GPR). It references data by
local path and never internalises the large cloud, per KB-1
(`examples/07_scan_ingest_full/README.md`).

The verified results on real data (2026-06-06) are recorded honestly in the
README. Photogrammetry point-cloud ingestion **works**: the Tongjiang quarry
`detail_cloudAB.ply` imports as a 6,857,772-point cloud.

![Photogrammetry point cloud, Tongjiang quarry, 6.86M points](../../../examples/07_scan_ingest_full/07_photogrammetry_ingest.png)

Scan-to-mesh reconstruction **works** on the subsampled cloud: it reconstructs
to a closed surface via the Advancing-Front backend (out-of-process worker),
59,971 verts / 111,973 tris in 3.9 s, with the long spanning triangles being cap
artifacts that the cleanup node peels (reconstruction is chapter 10).

![Scan-to-mesh reconstruction via the out-of-process Advancing-Front worker](../../../examples/07_scan_ingest_full/07_scan_to_mesh.png)

Two limitations are documented rather than hidden. LiDAR `.laz` **needs laszip,
not Rhino import**: a plain Rhino `-Import` of `ot_GD_TLS_data_UTM.laz` produces
zero objects, so `.laz`/`.las` must route through `LazCloudReader` (the
laszip.net path) or convert to E57 for the worker
(`examples/07_scan_ingest_full/README.md`). GPR `.rd3` **reads via the Core**
reader (986 traces with picks on the Grimsel granite file). The
`Import Photo Markers` component (GUID `F2D07A03-...`) completes the
photogrammetry path: it reads markers/GCPs from a Metashape/COLMAP/RealityCapture
export or a plain GCP CSV and feeds them into the Georeference align-by-points
node, so a floating photogrammetry result is positioned and scaled onto a known
base. The repository ingests markers but deliberately does not reconstruct
photogrammetry (`ImportPhotoMarkersComponent.cs:12-25`).

---

## 12.7 Status & what's left

- **`.gsf` proprietary dead-stop.** Any dataset that ships only Geoscanners AKULA
  `.gsf` cannot enter the pipeline without a manual GPRSoft or RGPR conversion to
  SEG-Y (`GprFileReader.cs:46-51`). This is a deliberate bridge, not a defect, but
  it blocks `.gsf`-only sources. *Severity: low.*
- **Multi-channel DZT not de-interleaved.** A GSSI `.dzt` with `rh_nchan > 1` is
  read as a single concatenated scan stream; all known test files are
  single-channel, and de-interleaving is a TODO when a multi-channel granite file
  appears (`GprDztReader.cs:30-33`). *Severity: medium.*
- **MALA marker positions not applied.** Traces from a `.rd3` are laid along the
  +X axis at the header distance interval; companion `.cor`/`.mrk` GPS marker
  positions are parsed but not yet written to `GprTrace.X/Y`
  (`GprMalaRd3Reader.cs:34-37`). The trace geometry is therefore a straight line,
  not the true survey path, until markers are wired. *Severity: medium.*
- **Vector readers do not reproject.** Output curves are in source-CRS units; a
  file in projected metres (Loviisa `EUREF_FIN_TM35FIN`) stays in those units, and
  GeoJSON carries no CRS at all (`ShapefileFractureReader.cs:31`,
  `GeoJsonFractureReader.cs:11-19`). This is correct (no silent datum error) but
  pushes reprojection onto the user. *Severity: low.*
- **E57 worker is an external dependency.** `Load E57 Cloud` needs python + pye57
  + numpy on PATH and `frahan_e57_worker.py` deployed beside the `.gha`; if any is
  missing the component reports the failure but cannot read the file
  (`E57CloudWorker.cs:167-191`, `LoadE57CloudComponent.cs:42-48`). *Severity:
  medium.*
- **No `.las`/`.laz` canvas reader in the default install path is validated
  end-to-end in Rhino.** The README marks `.laz` ingest as routed through the
  harness `laszip.net.dll`; a live in-Rhino validation of the LAS/LAZ component on
  the 357M-point tile is the remaining truth-criterion step
  (`examples/07_scan_ingest_full/README.md`). *Severity: low.*
- **Third-party notices owed.** NetTopologySuite, laszip.net, and the pye57 worker
  dependency chain each owe a `THIRD_PARTY_NOTICES.md` row before public release
  (the licensing register, `90_originality.md`). *Severity: medium.*

---

## References (this chapter)

- Huber, E., Hans, G. (2018). RGPR — an open-source package to process and
  visualize GPR data. 2018 17th International Conference on Ground Penetrating
  Radar (GPR), IEEE, pp 1-4. DOI 10.1109/ICGPR.2018.8441658.
- Lucius, J.E., Powers, M.H. (1999). USGS Open-File Report 02-166: GPR
  data-format documentation (pulseEKKO DT1/HD public-domain spec).
- Isenburg, M. (2013). LASzip: lossless compression of LiDAR data.
  Photogrammetric Engineering & Remote Sensing 79(2):209-217. DOI
  10.14358/PERS.79.2.209.
- Turk, G. (1994). The PLY polygon file format. Stanford University Graphics
  Laboratory.
- ASTM E2807-11 (2011, reapproved). Standard specification for 3D imaging data
  exchange, version 1.0 (E57 format).
- ASPRS. LAS specification version 1.4-R15. American Society for Photogrammetry
  and Remote Sensing.
- SEG Technical Standards Committee. SEG-Y data exchange format, revisions
  0/1/2. Society of Exploration Geophysicists.
- NetTopologySuite.IO.Esri. ESRI Shapefile and GeoJSON readers implementing OGC
  Simple Features. https://github.com/NetTopologySuite.
- Annan, A.P. (2009). Electromagnetic principles of ground penetrating radar. In:
  Jol, H.M. (ed.) Ground penetrating radar: theory and applications. Elsevier,
  Amsterdam, pp 3-40. ISBN 9780444533487. (Two-way travel-time depth relation.)
