/* =============================================================================
 * frahan_coacd — C ABI for CoACD-backed approximate convex decomposition.
 *
 * Wraps SarahWeiii/CoACD (SIGGRAPH 2022) for use from .NET via P/Invoke.
 * Same flat-array convention as frahan_cgal: vertices = 3 * N doubles,
 * triangles = 3 * T int32s. The library allocates output buffers; callers
 * release them via the supplied free helpers.
 *
 * Error codes: 0 = success, negative = error (see frahan_coacd_last_error).
 *
 * Upstream reference:
 *   https://github.com/SarahWeiii/CoACD
 *   Wei, Liu, Sorkine-Hornung, Gao, Liu, "Approximate Convex Decomposition
 *   for 3D Meshes with Collision-Aware Concavity and Tree Search",
 *   ACM Transactions on Graphics 41(4), 2022.
 *
 * License: CoACD is MIT. This shim is governed by the project license.
 * ============================================================================= */

#ifndef FRAHAN_COACD_H
#define FRAHAN_COACD_H

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32) || defined(_WIN64)
#  ifdef FRAHAN_COACD_BUILDING
#    define FRAHAN_COACD_API __declspec(dllexport)
#  else
#    define FRAHAN_COACD_API __declspec(dllimport)
#  endif
#else
#  define FRAHAN_COACD_API __attribute__((visibility("default")))
#endif

/* Returns a static, NUL-terminated version string. Always non-null. */
FRAHAN_COACD_API const char* frahan_coacd_version(void);

/* Returns the last error message produced by a frahan_coacd_* call.
 * Static buffer; copy if the value must outlive the next call. */
FRAHAN_COACD_API const char* frahan_coacd_last_error(void);

/* Sets CoACD's internal log level. Accepts "off", "error", "warn", "info",
 * "debug". Pass NULL to leave unchanged. Persists for the process. */
FRAHAN_COACD_API void frahan_coacd_set_log_level(const char* level);

/* =============================================================================
 * Approximate convex decomposition.
 *
 * Decomposes one input mesh into N convex pieces (each a triangulated
 * convex hull-like shape). Returns the pieces concatenated in a single
 * pair of vertex / triangle buffers, indexed by per-part start arrays —
 * the same layout used by frahan_cgal_polygon_partition_2d.
 *
 * Inputs:
 *   verts, vcount       : input mesh vertices (3 * vcount doubles).
 *   tris, tcount        : input mesh triangles (3 * tcount int32s).
 *
 * Tunables (pass -1 for "use CoACD default"):
 *   threshold           : concavity threshold. Lower -> more pieces, finer
 *                          fit. CoACD default 0.05 in normalized units, or
 *                          in metres when real_metric=1.
 *   preprocess_mode     : 0 = auto, 1 = on, 2 = off. Default 0.
 *   preprocess_resolution : voxel grid for the manifold-isation step
 *                          (default 50, range ~30..100).
 *   sample_resolution   : sampling resolution for concavity computation
 *                          (default 2000).
 *   mcts_nodes          : MCTS nodes per cut (default 20).
 *   mcts_iters          : MCTS iterations per cut (default 150).
 *   mcts_max_depth      : MCTS tree depth (default 3).
 *   pca                 : 1 = align cuts to PCA frame, 0 = world-axis.
 *                          Default 0.
 *   merge               : 1 = merge convex pieces post-decomposition where
 *                          merging stays convex. Default 1.
 *   max_convex_hull     : cap on output piece count. -1 = unlimited.
 *   seed                : RNG seed for reproducibility.
 *   real_metric         : 1 = treat threshold as metres (CoACD -rm mode,
 *                          v1.0.11+). 0 = normalized-unit threshold.
 *
 * Outputs (all allocated by the library):
 *   out_part_count      : number of convex pieces N.
 *   out_verts           : concatenated vertex array. Length = 3 * vert_count.
 *   out_vert_starts     : N + 1 int32s. Piece i's vertices occupy
 *                          out_verts[3 * out_vert_starts[i] ..
 *                                    3 * out_vert_starts[i+1]).
 *                          out_vert_starts[N] == vert_count.
 *   out_vert_count      : total vertex count across all pieces.
 *   out_tris            : concatenated triangle array. Length = 3 * tri_count.
 *                          Triangle indices are LOCAL to the piece they
 *                          belong to (re-rooted at 0 for each piece).
 *   out_tri_starts      : N + 1 int32s. Piece i's triangles occupy
 *                          out_tris[3 * out_tri_starts[i] ..
 *                                   3 * out_tri_starts[i+1]).
 *                          out_tri_starts[N] == tri_count.
 *   out_tri_count       : total triangle count across all pieces.
 *
 * Caller frees out_verts via frahan_coacd_free_pdouble; out_vert_starts,
 * out_tris, out_tri_starts via frahan_coacd_free_pint.
 *
 * Returns 0 on success, negative on failure.
 * ============================================================================= */
FRAHAN_COACD_API int frahan_coacd_decompose(
    const double* verts, int vcount,
    const int*    tris,  int tcount,
    double  threshold,
    int     preprocess_mode,
    int     preprocess_resolution,
    int     sample_resolution,
    int     mcts_nodes,
    int     mcts_iters,
    int     mcts_max_depth,
    int     pca,
    int     merge,
    int     max_convex_hull,
    unsigned int seed,
    int     real_metric,
    int*     out_part_count,
    double** out_verts,       int** out_vert_starts, int* out_vert_count,
    int**    out_tris,        int** out_tri_starts,  int* out_tri_count);

/* Generic free helpers for the variable-shape outputs. NULL is safe.
 * After release the pointer must not be used again. */
FRAHAN_COACD_API void frahan_coacd_free_pdouble(double* p);
FRAHAN_COACD_API void frahan_coacd_free_pint(int* p);

#ifdef __cplusplus
}
#endif

#endif /* FRAHAN_COACD_H */
