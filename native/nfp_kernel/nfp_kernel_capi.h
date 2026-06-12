/* ============================================================================
 * nfp_kernel_capi.h — C API for the batched No-Fit-Polygon kernel.
 *
 * One exported function: nfp_batch. For ONE part it computes, for every
 * requested rotation angle and every obstacle polygon, the no-fit polygon
 *
 *     NFP(obstacle, part@angle) = MinkowskiSum(obstacle, -rotate(part, angle))
 *
 * unioned NonZero, on Clipper2's exact Int64 lane (doubles are multiplied by
 * `scale`, snapped to Int64, all booleans run in Int64, results are divided
 * back). Placing the part with its reference point (coordinate origin) at any
 * point strictly inside the returned region overlaps the obstacle.
 *
 * Backed by vendored official Clipper2 C++ (BSL-1.0), see ./clipper2/.
 *
 * MEMORY CONTRACT: the caller allocates every buffer; the kernel never
 * returns ownership across the boundary. On NFP_ERR_CAPACITY the caller
 * grows the buffers (required sizes are reported, see below) and retries.
 * The kernel is deterministic: identical inputs produce identical outputs,
 * including loop order, independent of thread count.
 * ========================================================================== */

#ifndef NFP_KERNEL_CAPI_H
#define NFP_KERNEL_CAPI_H

#ifdef __cplusplus
extern "C" {
#endif

#ifdef NFP_KERNEL_BUILD
#define NFP_API __declspec(dllexport)
#else
#define NFP_API __declspec(dllimport)
#endif

/* return codes */
#define NFP_OK             0   /* success                                            */
#define NFP_ERR_CAPACITY   1   /* outXY and/or loop arrays too small — see below     */
#define NFP_ERR_BAD_ARGS  (-1) /* null pointer / partVerts < 3 / angleCount < 1 ...  */
#define NFP_ERR_EXCEPTION (-2) /* internal failure (exception caught at boundary)    */

/*
 * partXY        : part polygon, partVerts (x,y) pairs, closed implicitly.
 * anglesRad     : angleCount rotation angles in radians (CCW about origin).
 * obstXY        : all obstacle polygons concatenated, (x,y) pairs.
 * obstVerts     : per-obstacle vertex count, obstCount entries.
 * scale         : double -> Int64 multiplier (e.g. 100.0 mirrors the managed
 *                 Clipper2 PathD lane at decimal precision 2).
 * simplifyTol   : polyline simplification (Ramer-Douglas-Peucker, closed-loop
 *                 variant with anchors at vertex 0 and n/2) applied to the
 *                 reflected part and to each obstacle BEFORE the Minkowski sum:
 *                   > 0  absolute tolerance in input units,
 *                   < 0  RELATIVE: per-shape tol = |simplifyTol| * bbox-diagonal
 *                        (pass -2e-3 to mirror the managed nester's
 *                        NfpSimplifyTol),
 *                   = 0  no simplification.
 *                 Loops with <= 8 vertices are never simplified.
 * outXY         : caller buffer for all result loops, flat (x,y) pairs in
 *                 input units (already divided back by scale).
 * outCapacityDoubles : capacity of outXY in DOUBLES (2x the vertex capacity).
 * outLoopVerts  : per-loop vertex count.
 * outLoopAngleIdx / outLoopObstIdx : per-loop tags identifying which
 *                 (angle, obstacle) pair produced the loop. Loops are emitted
 *                 in deterministic (angleIdx, obstIdx, clipper-output) order.
 * loopCapacity  : capacity of the three per-loop arrays, in loops.
 * outLoopCount  : number of loops written on NFP_OK.
 *
 * NFP_ERR_CAPACITY: nothing useful is in outXY. Required sizes are reported:
 *   *outLoopCount   = required loop capacity,
 *   outLoopVerts[0] = required outXY capacity in doubles (when loopCapacity >= 1).
 * Degenerate obstacles (< 3 vertices) yield no loops and no error.
 */
NFP_API int nfp_batch(
    const double* partXY, int partVerts,
    const double* anglesRad, int angleCount,
    const double* obstXY, const int* obstVerts, int obstCount,
    double scale, double simplifyTol,
    double* outXY, int outCapacityDoubles,
    int* outLoopVerts, int* outLoopAngleIdx, int* outLoopObstIdx,
    int loopCapacity, int* outLoopCount);

#ifdef __cplusplus
}
#endif

#endif /* NFP_KERNEL_CAPI_H */
