/* =============================================================================
 * frahan_osqp — thin wrapper around the OSQP C library for Frahan masonry QP.
 *
 * OSQP (Stellato et al. 2020) solves:
 *     min  ½ xᵀPx + qᵀx
 *     s.t. l ≤ Ax ≤ u
 *
 * This shim accepts the ConvexQpProblem layout from C# (separate equality /
 * inequality / bound blocks, dense row-major matrices) and internally converts
 * them to the stacked OSQP form before calling osqp_solve.  No CSC matrices
 * are ever constructed on the managed side.
 *
 * DLL is statically linked against OSQP (no osqp.dll dependency at runtime).
 * ============================================================================= */

#ifndef FRAHAN_OSQP_H
#define FRAHAN_OSQP_H

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32) || defined(_WIN64)
#  ifdef FRAHAN_OSQP_BUILDING
#    define FRAHAN_OSQP_API __declspec(dllexport)
#  else
#    define FRAHAN_OSQP_API __declspec(dllimport)
#  endif
#else
#  define FRAHAN_OSQP_API __attribute__((visibility("default")))
#endif

FRAHAN_OSQP_API const char* frahan_osqp_version(void);
FRAHAN_OSQP_API const char* frahan_osqp_last_error(void);

/* --------------------------------------------------------------------------
 * frahan_osqp_solve — one-shot solve matching the ConvexQpProblem layout.
 *
 * Inputs (all row-major dense, nullable where noted):
 *   n          : number of decision variables.
 *   meq        : number of equality rows (Aeq x = beq).  0 → Aeq/beq ignored.
 *   mineq      : number of inequality rows (Aineq x ≤ bineq).  0 → ignored.
 *   P          : upper-triangle Hessian [n×n row-major].  NULL → zero Hessian.
 *   q          : linear objective [n].  NULL → zero vector.
 *   Aeq        : equality matrix [meq×n row-major].  NULL allowed when meq=0.
 *   beq        : equality rhs [meq].  NULL allowed when meq=0.
 *   Aineq      : inequality matrix [mineq×n row-major].  NULL when mineq=0.
 *   bineq      : inequality rhs [mineq].  NULL when mineq=0.
 *   lb         : lower bounds [n].  Use -1e30 for -∞.  NULL → all -∞.
 *   ub         : upper bounds [n].  Use +1e30 for +∞.  NULL → all +∞.
 *   eps_abs    : absolute tolerance.  0 → OSQP default (1e-5).
 *   eps_rel    : relative tolerance.  0 → OSQP default (1e-5).
 *   max_iter   : iteration cap.      0 → OSQP default (4000).
 *   polish     : 1 = enable OSQP polishing (slower but more accurate).
 *   warm_start_x: warm-start primal [n] in the original (unscaled) space.
 *                 NULL → cold start.
 *
 * Outputs (caller allocates):
 *   x_out      : primal solution [n].  Filled only when status_val = 1.
 *   obj_out    : objective value.  Filled only when status_val = 1.
 *   iter_out   : number of OSQP iterations taken.
 *   status_val : OSQP status code (1 = SOLVED, -3 = PRIMAL_INFEASIBLE,
 *                -4 = DUAL_INFEASIBLE, -7 = NON_CVX, negative on setup error).
 *   msg        : caller-supplied buffer, filled with a human-readable string.
 *   msg_len    : buffer length.
 *
 * Returns 0 on success (OSQP setup + solve attempted), -1 on setup failure.
 * Check status_val for the solve verdict.
 * -------------------------------------------------------------------------- */
FRAHAN_OSQP_API int frahan_osqp_solve(
    int            n,
    int            meq,
    int            mineq,
    const double*  P,
    const double*  q,
    const double*  Aeq,
    const double*  beq,
    const double*  Aineq,
    const double*  bineq,
    const double*  lb,
    const double*  ub,
    double         eps_abs,
    double         eps_rel,
    int            max_iter,
    int            polish,
    const double*  warm_start_x,
    double*        x_out,
    double*        obj_out,
    int*           iter_out,
    int*           status_val,
    char*          msg,
    int            msg_len
);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* FRAHAN_OSQP_H */
