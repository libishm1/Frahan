# Loviisa rapakivi-granite surface fracture maps (Chudasama 2022)

Manually interpreted surface fracture-trace maps and mapped-area boundaries from 8 outcrops at the
Loviisa site, southern Finland (Olkiluoto/Loviisa rapakivi granite). Style: short sentences, no em dashes.

## Contents (in git, small)
- `traces/` - 8 sites, expert manual fracture-trace polylines (`*_tulkinta.shp`, Finnish tulkinta =
  interpretation): KB11, KB2, KB3, KB7, KB9, KL2_1, KL5, OG1. Each is a PolyLine shapefile set
  (`.shp/.shx/.dbf/.prj/.cpg`).
- `area/` - the mapped-domain boundary polygon for each site (`*_area.shp`).
- The U-Net auto-traced version and the 0.5 m / 1 m / 5 m generalisations stay in the external archive
  (Zenodo + Drive); the 20 m manual interpretation here is the validated canonical map.

## Provenance
- Source: Chudasama, B. (2022). Loviisa rapakivi-granite fracture-trace dataset.
- DOI: 10.5281/zenodo.7077494 (and companion records). License: CC-BY 4.0.
- CRS: EUREF_FIN_TM35FIN (EPSG:3067), units metres. Coordinates are large (E ~466,000, N ~6,691,000);
  move-to-origin before local inspection.

## Read it in Frahan
`ShapefileFractureReader.Load(path.shp)` (Frahan > Quarry > Ingestion) or the `Vector Fractures Loader`
GH component (F2D00BEC) returns a `FractureTraceCollection` (polyline traces + per-trace .dbf attributes
+ CRS WKT). KB11 alone: 708 traces, 6483 vertices, two conjugate sets (strike ~15 deg and ~105-120 deg).
Worked example: `../../examples/26_loviisa_surface_fractures/`.

## Use
Surface fracture maps feed the fracture-aware block layout: project / extrapolate the mapped sets to
depth (with GPR, see example 08) to bound intact blocks. Polygon area boundaries clip the domain.
