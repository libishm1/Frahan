/* =============================================================================
 * frahan_cgal — C ABI for CGAL-backed mesh Boolean operations.
 *
 * Designed to be P/Invoked from .NET. Callers pass flat vertex / triangle
 * arrays in the same convention used elsewhere in Frahan
 * (vertices = 3 * N doubles; triangles = 3 * T int32s). The library
 * allocates output buffers and returns them via output pointers; callers
 * must free them via frahan_cgal_free_buffers when done.
 *
 * Error codes: 0 = success, negative = error (see frahan_cgal_last_error).
 * ============================================================================= */

#ifndef FRAHAN_CGAL_H
#define FRAHAN_CGAL_H

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32) || defined(_WIN64)
#  ifdef FRAHAN_CGAL_BUILDING
#    define FRAHAN_CGAL_API __declspec(dllexport)
#  else
#    define FRAHAN_CGAL_API __declspec(dllimport)
#  endif
#else
#  define FRAHAN_CGAL_API __attribute__((visibility("default")))
#endif

/* Returns a static, NUL-terminated version string. Always non-null. */
FRAHAN_CGAL_API const char* frahan_cgal_version(void);

/* Returns the last error message produced by a frahan_cgal_* call.
 * Static buffer; copy if the value must outlive the next call. */
FRAHAN_CGAL_API const char* frahan_cgal_last_error(void);

/* Common signature for all three Boolean operations.
 *
 *   a_verts, a_vcount  : input mesh A vertices (3 * a_vcount doubles).
 *   a_tris,  a_tcount  : input mesh A triangles (3 * a_tcount int32s).
 *   b_verts, b_vcount  : input mesh B vertices.
 *   b_tris,  b_tcount  : input mesh B triangles.
 *   out_verts, out_vcount, out_tris, out_tcount : output, allocated by
 *     the library. Pass to frahan_cgal_free_buffers when done.
 *
 * Returns 0 on success, negative on failure. Use frahan_cgal_last_error
 * for the message.
 *
 * Inputs MUST be triangulated, closed, manifold, and consistently
 * oriented. CGAL's PMP corefinement requires this for predictable
 * output. */
FRAHAN_CGAL_API int frahan_cgal_mesh_union(
    const double* a_verts, int a_vcount,
    const int* a_tris,    int a_tcount,
    const double* b_verts, int b_vcount,
    const int* b_tris,    int b_tcount,
    double** out_verts,    int* out_vcount,
    int**    out_tris,     int* out_tcount);

FRAHAN_CGAL_API int frahan_cgal_mesh_intersection(
    const double* a_verts, int a_vcount,
    const int* a_tris,    int a_tcount,
    const double* b_verts, int b_vcount,
    const int* b_tris,    int b_tcount,
    double** out_verts,    int* out_vcount,
    int**    out_tris,     int* out_tcount);

FRAHAN_CGAL_API int frahan_cgal_mesh_difference(
    const double* a_verts, int a_vcount,
    const int* a_tris,    int a_tcount,
    const double* b_verts, int b_vcount,
    const int* b_tris,    int b_tcount,
    double** out_verts,    int* out_vcount,
    int**    out_tris,     int* out_tcount);

/* Free buffers returned by the mesh ops. Either pointer may be NULL.
 * After this call the pointers must not be used again. */
FRAHAN_CGAL_API void frahan_cgal_free_buffers(double* verts, int* tris);

/* =============================================================================
 * Mesh repair — robust manifold cleanup pipeline.
 *
 * Runs CGAL's Polygon_mesh_processing repair primitives in sequence:
 *   1. triangulate_faces       — ensures every face is a triangle.
 *   2. stitch_borders          — merges coincident half-edges
 *                                (closes fissures from independent face sets).
 *   3. remove_degenerate_faces — drops zero-area triangles.
 *   4. orient_to_bound_a_volume — flips inward-facing patches when the mesh
 *                                is closed, so face normals consistently
 *                                point outward.
 *   5. collect_garbage         — reclaims indices freed by the steps above.
 *
 * Output is the cleaned mesh in the same flat-array format as the
 * boolean ops. Caller frees out_verts / out_tris via
 * frahan_cgal_free_buffers.
 *
 * Inputs that CGAL refuses outright (non-manifold input that
 * Surface_mesh::add_face rejects) cause an early-exit return code; less
 * pathological problems (degenerate faces, dangling vertices, inverted
 * face strips) get fixed by the pipeline.
 *
 * Returns 0 on success, negative on failure. License note: PMP repair
 * primitives are GPL.
 * ============================================================================= */
FRAHAN_CGAL_API int frahan_cgal_repair_mesh(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    double** out_verts,  int* out_vcount,
    int**    out_tris,   int* out_tcount);

/* =============================================================================
 * Mesh decimation — Surface_mesh_simplification edge collapse.
 *
 * Wraps CGAL's Surface_mesh_simplification::edge_collapse with the
 * default Lindstrom-Turk cost/placement policies (quadric-error-like
 * metric, well-tested). Three stop predicates exposed:
 *
 *   stop_kind = 0 : COUNT RATIO. stop_value in (0, 1). Stop when
 *                   remaining_edges / initial_edges <= stop_value.
 *                   E.g. 0.5 halves the edge count.
 *   stop_kind = 1 : TARGET EDGE COUNT. stop_value rounded to size_t.
 *                   Stop when remaining edges <= stop_value.
 *   stop_kind = 2 : EDGE LENGTH. stop_value > 0. Stop when the next
 *                   edge to collapse has length >= stop_value. Preserves
 *                   edges shorter than the threshold (useful for keeping
 *                   sharp features intact).
 *
 * Output is the decimated mesh in the same flat-array format as the
 * boolean ops. Caller frees out_verts / out_tris via
 * frahan_cgal_free_buffers.
 *
 * Returns 0 on success, negative on failure. License: CGAL
 * Surface_mesh_simplification is GPL.
 * ============================================================================= */
FRAHAN_CGAL_API int frahan_cgal_decimate_mesh(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    int     stop_kind,
    double  stop_value,
    double** out_verts,  int* out_vcount,
    int**    out_tris,   int* out_tcount);

/* =============================================================================
 * Mesh boolean — HYBRID kernel.
 *
 * Robustness upgrade over the EPICK-only entry points above. Pattern
 * lifted from COMPAS_CGAL (BRG, LGPL-3.0):
 *
 *   - Mesh storage / traversal stays in EPICK doubles (fast cache
 *     locality, no lazy exact arithmetic on every face touch).
 *   - Each vertex carries a parallel EPECK exact-coordinate property
 *     map. CGAL's corefinement reads/writes vertex points through a
 *     custom property map that round-trips via Cartesian_converter
 *     so intersection vertices are CONSTRUCTED in EPECK and written
 *     back to both the EPECK store and the inexact mesh.
 *
 * Effect: same numerical robustness as a full EPECK kernel run, with
 * a fraction of the cost (typical 2–5x slowdown vs EPICK-only,
 * compared to 50–100x for full EPECK).
 *
 *   op_kind: 0 = union, 1 = intersection, 2 = difference.
 *
 * All other parameters and free contract identical to the EPICK
 * entry points above. Returns 0 on success, negative on failure.
 *
 * License: same GPL profile as the EPICK entry points (PMP
 * corefinement is GPL).
 * ============================================================================= */
FRAHAN_CGAL_API int frahan_cgal_mesh_boolean_hybrid(
    int op_kind,
    const double* a_verts, int a_vcount,
    const int* a_tris,    int a_tcount,
    const double* b_verts, int b_vcount,
    const int* b_tris,    int b_tcount,
    double** out_verts,    int* out_vcount,
    int**    out_tris,     int* out_tcount);

/* Generic free helpers for the variable-shape outputs of the
 * non-mesh operations below. Each output pointer is malloc'd by the
 * library and must be released individually. NULL is safe.
 * After release the pointer must not be used again. */
FRAHAN_CGAL_API void frahan_cgal_free_pdouble(double* p);
FRAHAN_CGAL_API void frahan_cgal_free_pint(int* p);

/* =============================================================================
 * Oriented bounding box (3D). EIGEN-GATED.
 *
 * CGAL's CGAL/optimal_bounding_box.h transitively requires Eigen3
 * (the optimisation step solves a small SDP via Eigen). When the
 * native shim is built without Eigen, this entry point is omitted
 * from the binary; callers should check IsAvailable AND probe for
 * the symbol (or check the version string). The CMake build
 * defines FRAHAN_CGAL_HAVE_EIGEN when find_package(Eigen3) succeeds.
 *
 * Inputs are the same flat vertex array convention as the mesh ops.
 * Triangle topology is not required — pass tcount = 0 and tris = NULL
 * to use only the points.
 *
 * Output is a fixed 15-double layout:
 *   out[ 0..2]  origin point (corner of the box)
 *   out[ 3..5]  +X axis (unit vector)
 *   out[ 6..8]  +Y axis (unit vector)
 *   out[ 9..11] +Z axis (unit vector)
 *   out[12..14] extents along the three axes (positive lengths)
 *
 * Caller allocates the output buffer (15 doubles). Library writes into it.
 * No malloc; no free needed.
 *
 * Returns 0 on success, negative on failure. License note: CGAL's
 * Optimal_bounding_box is GPL-licensed in the open-source distribution.
 * ============================================================================= */
#ifdef FRAHAN_CGAL_HAVE_EIGEN
FRAHAN_CGAL_API int frahan_cgal_obb_3d(
    const double* verts, int vcount,
    const int* tris, int tcount,
    double out_obb[15]);
#endif

/* =============================================================================
 * Straight skeleton (2D, interior).
 *
 * Computes the interior straight skeleton of a polygon (with optional
 * holes). Inputs are flat 2D vertex arrays — vertices come in order
 * around the polygon: first the outer ring (CCW), then each hole (CW).
 *   outer_verts, outer_vc : outer ring vertices (2 * outer_vc doubles)
 *   hole_verts            : flat array of hole vertices (2 * sum(hole_vcounts) doubles)
 *   hole_vcounts          : per-hole vertex count (hole_count entries)
 *   hole_count            : number of holes (0 = simple polygon)
 *
 * Output:
 *   out_verts, out_vcount : 2 * out_vcount doubles, all skeleton + boundary nodes
 *   out_edges, out_ecount : 2 * out_ecount int32s, pairs of vertex indices
 *   out_times, out_tcount : out_tcount doubles, time-of-arrival per vertex
 *                          (out_tcount == out_vcount on success). Use NULL if
 *                          the time field is not needed; it is still allocated
 *                          because CGAL emits it. Caller frees with
 *                          frahan_cgal_free_pdouble.
 *
 * Caller frees out_verts, out_edges, out_times via frahan_cgal_free_pdouble /
 * frahan_cgal_free_pint as appropriate.
 *
 * Returns 0 on success, negative on failure. CGAL component is GPL.
 * ============================================================================= */
FRAHAN_CGAL_API int frahan_cgal_straight_skeleton_2d(
    const double* outer_verts, int outer_vc,
    const double* hole_verts, const int* hole_vcounts, int hole_count,
    double** out_verts, int* out_vcount,
    int** out_edges,    int* out_ecount,
    double** out_times, int* out_tcount);

/* =============================================================================
 * Polygon partition (2D, simple polygon → convex pieces).
 *
 * Decomposes a 2D simple polygon (no holes) into a set of convex sub-
 * polygons via CGAL's Partition_2 algorithms.
 *
 *   verts, vcount : flat 2D vertex array (2 * vcount doubles, CCW order)
 *   kind          : 0 = approximate convex (Hertel-Mehlhorn, fast)
 *                   1 = optimal convex (Greene, O(n^4) but minimal pieces)
 *                   2 = y-monotone partition
 *
 * Output:
 *   out_verts, out_vcount   : 2 * out_vcount doubles. Output vertices are a
 *                             subset of input vertices (no Steiner points
 *                             from Partition_2). May equal input verts in
 *                             order; do not assume.
 *   out_indices, out_icount : flat int array of vertex indices into out_verts
 *                             listing each polygon's verts in order.
 *                             out_icount == sum of polygon sizes.
 *   out_starts, out_pcount  : out_pcount + 1 int32s. polygon i occupies
 *                             out_indices[out_starts[i] .. out_starts[i+1]).
 *                             out_starts[out_pcount] == out_icount.
 *
 * Caller frees out_verts via frahan_cgal_free_pdouble; out_indices and
 * out_starts via frahan_cgal_free_pint.
 *
 * Returns 0 on success, negative on failure. CGAL Partition_2 is GPL.
 * ============================================================================= */
FRAHAN_CGAL_API int frahan_cgal_polygon_partition_2d(
    const double* verts, int vcount,
    int kind,
    double** out_verts,   int* out_vcount,
    int**    out_indices, int* out_icount,
    int**    out_starts,  int* out_pcount);

/* =============================================================================
 * Surface mesh segmentation via Shape Diameter Function (SDF).
 *
 * CGAL's mesh_segmentation pipeline (see
 * https://doc.cgal.org/latest/Surface_mesh_segmentation/). Returns one
 * segment_id per input triangle. Caller groups by segment_id to extract
 * per-segment sub-meshes (same pattern as the Geogram RVD seed_id).
 *
 *   nb_clusters         : target number of segments (>= 2). CGAL may
 *                         return fewer if some clusters end up empty;
 *                         the actual count is written to
 *                         out_actual_clusters.
 *   smoothing_lambda    : graph-cut smoothness penalty in [0, 1].
 *                         CGAL default is 0.26. Higher = more spatially
 *                         coherent / fewer islands.
 *   cone_angle_radians  : SDF inward cone half-angle. <= 0 selects
 *                         CGAL's default (2/3 * pi, ~120 degrees).
 *   nb_rays             : rays per facet for SDF estimation. <= 0
 *                         selects CGAL's default (25). More rays =
 *                         smoother SDF, slower compute.
 *   postprocess         : 1 = run CGAL's SDF postprocess
 *                         (smoothing + connected-component cleanup).
 *
 * Output:
 *   out_segment_ids     : malloc'd int[tcount]. One entry per input
 *                         triangle, value in [0, *out_actual_clusters).
 *   out_idcount         : equals input tcount on success.
 *   out_actual_clusters : actual segment count CGAL produced.
 *
 * Caller frees out_segment_ids via frahan_cgal_free_pint.
 *
 * SEMANTIC NOTE: SDF segmentation cuts at concave features. Inputs
 * with no concavity (a convex block) collapse to ~1 segment regardless
 * of nb_clusters. For Voronoi-style spatial chopping, use the Geogram
 * block partition instead.
 *
 * License: CGAL's Surface_mesh_segmentation package is GPL.
 * ============================================================================= */
FRAHAN_CGAL_API int frahan_cgal_segment_sdf(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    int     nb_clusters,
    double  smoothing_lambda,
    double  cone_angle_radians,
    int     nb_rays,
    int     postprocess,
    int**   out_segment_ids, int* out_idcount,
    int*    out_actual_clusters);

/* =============================================================================
 * Angle-based face segmentation - cluster faces by dihedral-angle change.
 *
 * Two-stage CGAL pipeline:
 *   1. PMP::detect_sharp_edges marks every edge whose dihedral angle
 *      exceeds angle_threshold_degrees as a feature edge.
 *   2. PMP::connected_components flood-fills faces while treating those
 *      feature edges as walls.
 *
 * The result is one segment_id per input triangle. Faces that are
 * connected through "soft" edges (dihedral below the threshold) end
 * up in the same segment; sharp edges become segment boundaries.
 *
 *   angle_threshold_degrees : dihedral angle in degrees, in (0, 180).
 *                             Typical 30-60 for smooth-band detection
 *                             on curved organic forms; lower for
 *                             strict planarity; higher for permissive
 *                             grouping that ignores all but the
 *                             sharpest creases.
 *
 * Output:
 *   out_segment_ids     : malloc'd int[tcount] aligned with input tris.
 *   out_idcount         : equals input tcount on success.
 *   out_actual_clusters : connected-component count.
 *
 * Caller frees out_segment_ids via frahan_cgal_free_pint.
 *
 * License: PMP (CGAL) is GPL.
 * ============================================================================= */
FRAHAN_CGAL_API int frahan_cgal_segment_by_angle(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    double  angle_threshold_degrees,
    int**   out_segment_ids, int* out_idcount,
    int*    out_actual_clusters);

/* =============================================================================
 * Geodesic Voronoi via the Heat Method (Crane et al. 2013).
 *
 * For each input seed point, snaps to the nearest mesh vertex, then
 * uses CGAL::Heat_method_3 to compute a per-vertex geodesic distance
 * field FROM that vertex. Each vertex is then assigned to the seed
 * with the minimum geodesic distance (running argmin over the N
 * back-solves), and each face inherits the majority seed_id of its
 * three vertices.
 *
 * The factorisation of the cotangent Laplacian is cached and reused
 * across seeds, so cost scales as one factorisation + N back-solves
 * rather than N independent solves.
 *
 * The result is an on-surface Voronoi partition: cell-boundary curves
 * follow geodesic equidistance, which respects the mesh curvature
 * instead of cutting through it (the failure mode of Geogram RVD with
 * Euclidean seeds).
 *
 *   seed_points : 3 * seed_count doubles. Snapped to nearest vertex.
 *
 * Output:
 *   out_segment_ids     : malloc'd int[tcount], in [0, seed_count).
 *   out_idcount         : equals input tcount on success.
 *   out_actual_clusters : equals seed_count (no empty cells, since
 *                         every seed is the nearest cell to at least
 *                         one vertex - itself).
 *
 * License: CGAL Heat_method_3 is GPL.
 * ============================================================================= */
FRAHAN_CGAL_API int frahan_cgal_geodesic_voronoi(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    const double* seed_points, int seed_count,
    int**   out_segment_ids, int* out_idcount,
    int*    out_actual_clusters);

/* =============================================================================
 * Phase H — Reconstruction primitives (UX architecture report §7.7.E).
 * Surface reconstruction from a 3D point cloud. Output buffers (vertices
 * + triangle indices) are allocated by the library and must be freed via
 * frahan_cgal_free_buffers.
 * ============================================================================= */

/*
 * Alpha Shape 3D reconstruction. CGAL::Alpha_shape_3<Delaunay_triangulation_3>.
 *
 *   points, pcount : input point cloud (3 * pcount doubles).
 *   alpha          : alpha value; if <= 0, find_optimal_alpha(1) is used.
 *   out_verts, out_vcount, out_tris, out_tcount : output mesh.
 *
 * License: CGAL Alpha_shapes_3 is GPLv3+.
 */
FRAHAN_CGAL_API int frahan_cgal_alpha_shape_3(
    const double* points, int pcount,
    double  alpha,
    double**out_verts,   int* out_vcount,
    int**   out_tris,    int* out_tcount);

/*
 * Advancing-Front surface reconstruction (BPA-equivalent).
 * CGAL::Advancing_front_surface_reconstruction.
 *
 *   points, pcount : input point cloud (3 * pcount doubles).
 *   radius_ratio   : optional radius ratio (CGAL default 5.0 if <= 0).
 *   beta           : optional smoothness / sharp-edge parameter
 *                    (CGAL default 0.52 if <= 0).
 *   out_verts, out_vcount, out_tris, out_tcount : output mesh.
 *
 * License: CGAL Advancing_front_surface_reconstruction is GPLv3+.
 */
FRAHAN_CGAL_API int frahan_cgal_advancing_front_reconstruct(
    const double* points, int pcount,
    double  radius_ratio,
    double  beta,
    double**out_verts,   int* out_vcount,
    int**   out_tris,    int* out_tcount);

/*
 * Delaunay/screened Poisson surface reconstruction (CGAL). Needs ORIENTED
 * normals (wire Estimate Normals upstream). sm_angle / sm_radius / sm_distance
 * are the surface-mesher criteria; <= 0 uses CGAL defaults 20 / 30 / 0.375.
 * out_verts / out_tris are malloc'd flat arrays (free via the *_free_* fns).
 * License: CGAL Poisson_surface_reconstruction is GPLv3+ (uses Eigen).
 */
FRAHAN_CGAL_API int frahan_cgal_poisson_reconstruct(
    const double* points, int pcount,
    const double* normals,
    double  sm_angle, double sm_radius, double sm_distance,
    double**out_verts,   int* out_vcount,
    int**   out_tris,    int* out_tcount);

/* =============================================================================
 * Phase I.6-I15 — Cloud-cloud ICP helpers.
 * ============================================================================= */

/*
 * Estimate oriented normals on an unstructured point cloud.
 * CGAL::pca_estimate_normals + CGAL::mst_orient_normals.
 *
 *   points, pcount   : input cloud (3 * pcount doubles).
 *   k_neighbours     : k for PCA fit; CGAL recommends 18-24 for dense
 *                      clouds. <= 0 uses 18.
 *   out_normals      : output normals (3 * pcount doubles); same order
 *                      as input. Caller frees via frahan_cgal_free_pdouble.
 *
 * License: CGAL Point_set_processing_3 is GPLv3+.
 */
FRAHAN_CGAL_API int frahan_cgal_estimate_normals(
    const double* points, int pcount,
    int     k_neighbours,
    double**out_normals);

#ifdef __cplusplus
}
#endif

#endif /* FRAHAN_CGAL_H */
