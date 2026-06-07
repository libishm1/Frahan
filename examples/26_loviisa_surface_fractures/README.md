# Example 26 - Surface fracture map from a shapefile (Loviisa rapakivi granite)

Read a real ESRI Shapefile of mapped surface fracture traces through the Frahan vector-ingest layer and
render it as a fracture map. This is the vector-input counterpart to the GPR depth surveys (examples
03/08): GPR sees fractures with depth, a shapefile carries the mapped surface trace network. Units:
meters. Style: short sentences, no em dashes.

![Loviisa KB11 surface fracture map](26_surface_fracture_map.png)
*708 manual fracture traces over the KB11 outcrop (~54 x 45 m). Coloured by strike: blue = NE-trending
set, orange = ESE/SE-trending set. Grey = the mapped-area boundary polygon.*

## Data (colocated, works out of the box)
`shp_data/KB11_tulkinta.{shp,shx,dbf,prj,cpg}` (traces) and `kb11_area.{shp,...}` (boundary). Real data:
Chudasama 2022 Loviisa rapakivi-granite fracture maps (DOI 10.5281/zenodo.7077494, CC-BY 4.0), CRS
EUREF_FIN_TM35FIN (EPSG:3067), metres. The full 8-site dataset is in `../../data/loviisa/`. Provenance:
`shp_data/_SOURCE.md`.

## Reader
`ShapefileFractureReader.Load(shp)` (Frahan > Quarry > Ingestion, via NetTopologySuite.IO.Esri) returns a
`FractureTraceCollection`: polyline `Traces` (each with `.Vertices` X/Y and the per-trace `.dbf`
attributes) plus the CRS WKT. Polyline shapefiles only; polygons (the area boundary) are read separately.
The GH component is `Vector Fractures Loader` (F2D00BEC); `26_loviisa_vector_load.gh` is the canvas (set
the File input to `shp_data/KB11_tulkinta.shp`). The NetTopologySuite assemblies now ship in
`install/plugin/` so the component resolves at runtime.

## Measured (this run, verified through the Frahan reader)
- 708 fracture traces, 6483 vertices; total trace length 1593.5 m, mean 2.25 m, max 33.1 m.
- CRS: EUREF_FIN_TM35FIN; outcrop extent 54.2 x 44.6 m.
- Two conjugate fracture sets: strike peaks at ~15 deg (NNE) and ~105-120 deg (ESE). The strike
  histogram is the input a quarry needs to orient block cuts away from the dominant joint set.

## Why this matters
A surface fracture map is the cheapest fracture data a quarry has (drone photo + tracing, no GPR). It
sets the joint orientation and spacing that bound dimension blocks in plan, and combined with a GPR depth
survey (example 08) it constrains the 3D intact-block volume. The same `FractureTraceCollection` feeds
`Slab Cut By Fractures` and the fracture-aware block packers.

## Files
- `shp_data/` - the colocated KB11 shapefile sets + provenance.
- `26_surface_fracture_map.png` - the rendered map (strike-coloured).
- `26_loviisa_surface_fractures.3dm` - baked traces (2 strike-set layers) + area boundary.
- `26_loviisa_vector_load.gh` - the Vector Fractures Loader canvas.
