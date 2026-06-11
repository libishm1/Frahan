/* =============================================================================
 * frahan_geogram - C ABI for Geogram-backed mesh operations.
 *
 * Wraps Bruno Levy's Geogram (Inria / ALICE) for use from .NET via
 * P/Invoke. Same flat-array convention as frahan_cgal / frahan_coacd:
 * vertices = 3 * N doubles, triangles = 3 * T int32s.
 *
 * v1 entry points: decimation. Future: surface remesh, restricted
 * Voronoi diagram (RVD), centroidal Voronoi tessellation (CVT) - all
 * on the natural Geogram on-ramp toward the masonry block-partition
 * pipeline (Kao 2022 CRA).
 *
 * License: Geogram is BSD-3. The shim's FRAHAN_GEOGRAM_API surface is
 * therefore safe to ship inside a binary plugin without GPL ceremony.
 *
 * Upstream: https://github.com/BrunoLevy/geogram
 * ============================================================================= */

#ifndef FRAHAN_GEOGRAM_H
#define FRAHAN_GEOGRAM_H

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32) || defined(_WIN64)
#  ifdef FRAHAN_GEOGRAM_BUILDING
#    define FRAHAN_GEOGRAM_API __declspec(dllexport)
#  else
#    define FRAHAN_GEOGRAM_API __declspec(dllimport)
#  endif
#else
#  define FRAHAN_GEOGRAM_API __attribute__((visibility("default")))
#endif

/* Returns a static, NUL-terminated version string. Always non-null. */
FRAHAN_GEOGRAM_API const char* frahan_geogram_version(void);

/* Returns the last error message produced by a frahan_geogram_* call.
 * Static buffer; copy if the value must outlive the next call. */
FRAHAN_GEOGRAM_API const char* frahan_geogram_last_error(void);

/* =============================================================================
 * Mesh decimation - vertex clustering.
 *
 * Wraps GEO::mesh_decimate_vertex_clustering. Algorithm: voxel-bin
 * vertex clustering. Higher nb_bins => finer voxel grid => more
 * detailed output mesh. This is a different decimation flavour from
 * CGAL's edge-collapse (frahan_cgal_decimate_mesh) - voxel binning is
 * fast and produces a regular spatial sampling, edge-collapse is
 * topology-preserving and quadric-error-driven. They are
 * complementary; use Geogram's for very high-poly scans where you want
 * a controlled spatial resolution, CGAL's for precise count targeting.
 *
 *   nb_bins      : voxel grid resolution per bbox dimension. Typical
 *                  range 50..300. Default 100. Higher = more detail.
 *   mode_flags   : bitwise OR of GEO::MeshDecimateMode constants:
 *                    1 = MESH_DECIMATE_DUP_F   (remove duplicate verts)
 *                    2 = MESH_DECIMATE_DEG_3   (remove degree-3 verts)
 *                    4 = MESH_DECIMATE_KEEP_B  (preserve borders)
 *                    7 = MESH_DECIMATE_DEFAULT (1|2|4)
 *
 * Output is the decimated mesh in the same flat-array format as the
 * other shims. Caller frees out_verts / out_tris via
 * frahan_geogram_free_pdouble / frahan_geogram_free_pint.
 *
 * Returns 0 on success, negative on failure. License: BSD-3.
 * ============================================================================= */
FRAHAN_GEOGRAM_API int frahan_geogram_decimate_mesh(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    int     nb_bins,
    int     mode_flags,
    double** out_verts,  int* out_vcount,
    int**    out_tris,   int* out_tcount);

/* =============================================================================
 * Mesh repair - GEO::mesh_repair (BSD-3 parallel to CGAL repair_mesh).
 *
 *   mode_flags: bitwise OR of GEO::MeshRepairMode constants:
 *     0 = TOPOLOGY    (always done; dissociate non-manifold verts)
 *     1 = COLOCATE    (merge identical vertices)
 *     2 = DUP_F       (remove duplicated facets)
 *     4 = TRIANGULATE (triangulate non-triangle facets)
 *     8 = RECONSTRUCT (post-process Co3Ne result)
 *    16 = QUIET       (suppress messages)
 *     7 = DEFAULT     (1|2|4)
 *   colocate_epsilon: tolerance for COLOCATE merge (0.0 = exact only).
 * ============================================================================= */
FRAHAN_GEOGRAM_API int frahan_geogram_repair_mesh(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    int     mode_flags,
    double  colocate_epsilon,
    double** out_verts,  int* out_vcount,
    int**    out_tris,   int* out_tcount);

/* =============================================================================
 * Fill holes - GEO::fill_holes.
 *
 * Triangulates open boundary loops up to a size threshold. Use it to
 * close spurious slivers and gap holes in a Voronoi cell sub-mesh
 * before feeding to BFF, while keeping the main outer boundary open.
 *
 *   max_hole_area  : maximum hole AREA (in input units squared) to
 *                    fill. 0.0 fills nothing. A very large value
 *                    (e.g. 1e30) fills every hole regardless of size.
 *   max_hole_edges : maximum number of boundary edges per hole. <= 0
 *                    means "no edge limit" (size is governed by area
 *                    alone). Set to a small number (e.g. 30) to
 *                    target only sliver-style holes that have few
 *                    edges around them.
 *   repair_after   : 1 = run mesh_repair (DEFAULT mode, colocate_eps=0)
 *                    after filling. Cleans up duplicate vertices /
 *                    facets that the hole triangulator can leave
 *                    behind. 0 = skip.
 *
 * BSD-3.
 * ============================================================================= */
FRAHAN_GEOGRAM_API int frahan_geogram_fill_holes(
    const double* verts, int vcount,
    const int* tris,    int tcount,
    double  max_hole_area,
    int     max_hole_edges,
    int     repair_after,
    double** out_verts,  int* out_vcount,
    int**    out_tris,   int* out_tcount);

/* =============================================================================
 * Oriented bounding box (3D) via PrincipalAxes3d.
 *
 * Computes the OBB by PCA of the input vertices: the box axes are the
 * eigenvectors of the covariance matrix; extents come from projecting
 * each vertex onto each axis and taking the range. tris is informational
 * (point cloud is enough); pass tcount=0 and tris=nullptr if no
 * connectivity is available.
 *
 * Output is a fixed 15-double layout (same as frahan_cgal_obb_3d):
 *   out[ 0..2]  origin point (corner of the box)
 *   out[ 3..5]  +X axis (unit vector)
 *   out[ 6..8]  +Y axis (unit vector)
 *   out[ 9..11] +Z axis (unit vector)
 *   out[12..14] extents along the three axes (positive lengths)
 *
 * Caller allocates the output buffer (15 doubles). No malloc / no free.
 * Lighter than CGAL OBB - no Eigen dep. BSD-3.
 * ============================================================================= */
FRAHAN_GEOGRAM_API int frahan_geogram_obb_3d(
    const double* verts, int vcount,
    const int* tris, int tcount,
    double out_obb[15]);

/* =============================================================================
 * Surface remesh (uniform) - GEO::remesh_smooth.
 *
 * Resamples the input surface using a centroidal-Voronoi-driven Lloyd
 * + Newton optimization. Produces a uniform-edge mesh approximating
 * the input shape. Input mesh need NOT be manifold; output is.
 *
 *   nb_points    : desired vertex count in output (typical 5k..50k).
 *                  Lower => coarser. Geogram may emit slightly more to
 *                  resolve degeneracies.
 *   nb_lloyd     : Lloyd relaxation iterations (default 5).
 *   nb_newton    : Newton iterations after Lloyd (default 30).
 *
 * BSD-3.
 * ============================================================================= */
FRAHAN_GEOGRAM_API int frahan_geogram_remesh_uniform(
    const double* verts, int vcount,
    const int* tris, int tcount,
    int     nb_points,
    int     nb_lloyd,
    int     nb_newton,
    double** out_verts, int* out_vcount,
    int**    out_tris,  int* out_tcount);

/* =============================================================================
 * Tetrahedralize - GEO::mesh_tetrahedralize. CONSTRAINED on TetGen.
 *
 * NOTE: requires the underlying Geogram to be built with
 * GEOGRAM_WITH_TETGEN=ON. The current shim build has it OFF for
 * BSD-3 license cleanliness (TetGen is non-commercial). When OFF,
 * this entry point returns -210 with a clear error. To enable, rebuild
 * the shim with `-DGEOGRAM_WITH_TETGEN=ON` (accepts the non-commercial
 * license restriction).
 *
 *   preprocess    : 1 = clean input (merge duplicates, fill gaps); 0 = trust input.
 *   refine        : 1 = insert Steiner points to improve quality; 0 = preserve V.
 *   quality       : in [1.0, 2.0]. 1.0 = max quality (more elements).
 *   keep_regions  : 1 = output all internal regions; 0 = outermost only.
 *
 * Output cells are TETS (4-tuples). out_tets has length 4 * out_tcount.
 *
 * Caller frees out_verts via frahan_geogram_free_pdouble; out_tets via
 * frahan_geogram_free_pint.
 * ============================================================================= */
FRAHAN_GEOGRAM_API int frahan_geogram_tetrahedralize(
    const double* verts, int vcount,
    const int* tris, int tcount,
    int     preprocess,
    int     refine,
    double  quality,
    int     keep_regions,
    double** out_verts, int* out_vcount,
    int**    out_tets,  int* out_tcount);

/* =============================================================================
 * Centroidal Voronoi Tessellation (CVT) - optimized seed placement.
 *
 * Distributes nb_points points evenly over the input surface via
 * Lloyd + Newton-Lloyd iterations. Output is the seed point set,
 * suitable for feeding into restricted_voronoi_3d to get block cells.
 *
 *   nb_points    : seed count (typical 50..500 for masonry blocks).
 *   nb_lloyd     : Lloyd relaxation iterations (default 5).
 *   nb_newton    : Newton iterations after Lloyd (default 30).
 *
 * Output is a flat point array: 3 * out_pcount doubles.
 * Caller frees via frahan_geogram_free_pdouble.
 *
 * BSD-3.
 * ============================================================================= */
FRAHAN_GEOGRAM_API int frahan_geogram_cvt_compute(
    const double* verts, int vcount,
    const int* tris, int tcount,
    int     nb_points,
    int     nb_lloyd,
    int     nb_newton,
    double** out_points, int* out_pcount);

/* =============================================================================
 * Restricted Voronoi Diagram (RVD) on a surface mesh.
 *
 * Given an input surface mesh and N seed points, computes the surface
 * partitioned into N Voronoi cells (each cell = the surface region
 * closest to its seed). Output is a single triangulated mesh PLUS a
 * per-facet seed_id attribute (0..N-1). Caller groups facets by
 * seed_id to extract per-cell sub-meshes.
 *
 * For the masonry pipeline: feed CVT-optimized seeds to get
 * uniform-area Voronoi cells on a quarry face.
 *
 *   seed_points  : 3 * seed_count doubles.
 *
 * Outputs:
 *   out_verts, out_vcount     : flat vertex array (3 * out_vcount doubles)
 *   out_tris,  out_tcount     : flat triangle array (3 * out_tcount int32s)
 *   out_seed_ids, out_idcount : per-facet seed_id (length == out_tcount)
 *
 * Caller frees out_verts via frahan_geogram_free_pdouble; out_tris and
 * out_seed_ids via frahan_geogram_free_pint.
 *
 * BSD-3.
 * ============================================================================= */
FRAHAN_GEOGRAM_API int frahan_geogram_rvd_compute(
    const double* mesh_verts, int mesh_vcount,
    const int* mesh_tris,    int mesh_tcount,
    const double* seed_points, int seed_count,
    double** out_verts,    int* out_vcount,
    int**    out_tris,     int* out_tcount,
    int**    out_seed_ids, int* out_idcount);

/* =============================================================================
 * Surface RVD with anti-sawtooth pre-remesh.
 *
 * Same outputs as frahan_geogram_rvd_compute (one triangulated mesh +
 * per-facet seed_id). The extra knob eliminates the discrete-
 * triangulation stair-step seen along Voronoi cell boundaries when the
 * input mesh is coarse:
 *
 *   remesh_nb_points : if > 0, the input mesh is first resampled to a
 *                      uniform-edge mesh of ~remesh_nb_points vertices via
 *                      GEO::remesh_smooth (Lloyd + Newton-Lloyd) BEFORE
 *                      RVD. Finer triangles -> finer cuts -> smoother
 *                      cell boundary. Pass 0 to skip the pre-remesh
 *                      (then this entry point matches rvd_compute).
 *   remesh_nb_lloyd  : Lloyd iterations for the pre-remesh. Default 5.
 *   remesh_nb_newton : Newton iterations for the pre-remesh. Default 30.
 *
 * For sliver / co-located vertex cleanup, run frahan_geogram_repair_mesh
 * per-cell on the managed side AFTER splitting by seed_id - that avoids
 * accidentally fusing adjacent cells, which a global repair pass would
 * do (their boundary vertices are coincident by construction).
 *
 * Output buffer ownership is identical to frahan_geogram_rvd_compute.
 * BSD-3 (Geogram only - this entry point uses no AGPL components).
 * ============================================================================= */
FRAHAN_GEOGRAM_API int frahan_geogram_rvd_compute_smooth(
    const double* mesh_verts, int mesh_vcount,
    const int* mesh_tris,    int mesh_tcount,
    const double* seed_points, int seed_count,
    int     remesh_nb_points,
    int     remesh_nb_lloyd,
    int     remesh_nb_newton,
    double** out_verts,    int* out_vcount,
    int**    out_tris,     int* out_tcount,
    int**    out_seed_ids, int* out_idcount);

/* =============================================================================
 * Volumetric Voronoi block decomposition - CLOSED polyhedral cells.
 *
 * Steps (all internal):
 *   1. Tetrahedralize the input solid (TetGen, requires
 *      GEOGRAM_WITH_TETGEN=ON; returns -270 if OFF).
 *   2. Construct RVD over the tet mesh with set_volumetric(true).
 *   3. compute_RVD with cell_borders_only=true: each facet emitted is
 *      part of a per-cell closed surface (input boundary intersected
 *      with the Voronoi cell, plus planar separator faces between
 *      adjacent cells). The "region" attribute on each facet holds the
 *      seed_id (0..seed_count-1).
 *
 * Separator faces between cells A and B are emitted twice in the output -
 * once with seed_id=A (oriented outward of A), once with seed_id=B - so
 * that a downstream split-by-seed produces ONE CLOSED MESH per cell with
 * no shared vertices across cells. This is the property the upstream
 * "Mesh Repair (CGAL)" component needs to certify each cell as a solid.
 *
 *   seed_points  : 3 * seed_count doubles. Points must lie INSIDE the
 *                  input solid (or on its surface); seeds outside are
 *                  silently dropped from the output cell set.
 *
 * Outputs identical in shape to frahan_geogram_rvd_compute. License: the
 * native dependency chain pulls in TetGen (AGPL) when this entry point
 * is enabled at build time. The surface-only entry points
 * (frahan_geogram_rvd_compute, ..._smooth) remain BSD-3 and work
 * regardless of TetGen state.
 * ============================================================================= */
FRAHAN_GEOGRAM_API int frahan_geogram_voronoi_blocks_compute(
    const double* mesh_verts, int mesh_vcount,
    const int* mesh_tris,    int mesh_tcount,
    const double* seed_points, int seed_count,
    double** out_verts,    int* out_vcount,
    int**    out_tris,     int* out_tcount,
    int**    out_seed_ids, int* out_idcount);

/* Generic free helpers for the variable-shape outputs. NULL is safe.
 * After release the pointer must not be used again. */
FRAHAN_GEOGRAM_API void frahan_geogram_free_pdouble(double* p);
FRAHAN_GEOGRAM_API void frahan_geogram_free_pint(int* p);

/* =============================================================================
 * Phase H — Poisson surface reconstruction. Wraps the PoissonRecon
 * library bundled inside Geogram (see NOTICE.md). Requires
 * FRAHAN_GEOGRAM_ENABLE_POISSON at build time.
 * ============================================================================= */
FRAHAN_GEOGRAM_API int frahan_geogram_poisson_reconstruct(
    const double* points,  int pcount,
    const double* normals,
    int     depth,
    double  samples_per_node,
    double**out_verts,     int* out_vcount,
    int**   out_tris,      int* out_tcount);

/* =============================================================================
 * Phase I.6-I15 — KD-tree NN search + voxel downsample.
 * ============================================================================= */
FRAHAN_GEOGRAM_API int frahan_geogram_voxel_downsample(
    const double* points, int pcount,
    double  voxel_size,
    double**out_centroids, int* out_count);

FRAHAN_GEOGRAM_API int frahan_geogram_kdtree_query(
    const double* tree_points,  int tree_count,
    const double* query_points, int query_count,
    int**   out_indices,
    double**out_sq_distances);

#ifdef __cplusplus
}
#endif

#endif /* FRAHAN_GEOGRAM_H */
