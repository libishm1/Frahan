// =============================================================================
// frahan_osqp — OSQP shim for Frahan masonry convex QP.
//
// Converts the ConvexQpProblem layout (equality / inequality / bounds, dense
// row-major matrices) to OSQP's unified stacked form:
//
//   l <= A_combined x <= u
//   A_combined = [Aeq ; Aineq ; I_n]   (meq + mineq + n rows)
//   l = [beq ; -inf ; lb]
//   u = [beq ; bineq ; ub]
//
// P must be the UPPER TRIANGLE (OSQP convention).
//
// Converts dense inputs → CSC for OSQP internally.  No CSC data ever crosses
// the P/Invoke boundary.
//
// OSQP index type: int (OSQP_USE_LONG=OFF, the vcpkg default for x64-windows).
// =============================================================================

#include "frahan_osqp.h"
#include <osqp/osqp.h>

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <cstdarg>
#include <vector>

namespace {

static char g_last_error[1024] = "";

void set_error(const char* fmt, ...) {
    va_list ap; va_start(ap, fmt);
    vsnprintf(g_last_error, sizeof(g_last_error), fmt, ap);
    va_end(ap);
}

// OSQP v1.0 renamed the public types (c_int -> OSQPInt, c_float -> OSQPFloat,
// the CSC struct -> OSQPCscMatrix). Compat aliases keep the body below unchanged.
using c_int    = OSQPInt;
using c_float  = OSQPFloat;
using OSQPCsc  = OSQPCscMatrix;
using osqp_int = c_int;   // aliased so a single change fixes all uses below

static constexpr double INF  = 1e30;   // OSQP_INFTY ≈ 1e30
static constexpr double NINF = -1e30;

// -------------------------------------------------------------------------
// Dense upper-triangle matrix → CSC (upper triangle, OSQP convention for P).
// Row-major input P[n*n].  Returns CSC triplet vectors.
// Only entries P[i,j] with i <= j (upper triangle) and value != 0 included.
// -------------------------------------------------------------------------
void dense_upper_to_csc(
    const double* P, int n,
    std::vector<osqp_int>& col_ptr,
    std::vector<osqp_int>& row_idx,
    std::vector<double>&   vals)
{
    col_ptr.resize(n + 1, 0);
    // Count per column (only upper triangle).
    for (int j = 0; j < n; j++)
        for (int i = 0; i <= j; i++)
            if (P != nullptr && P[i * n + j] != 0.0) col_ptr[j + 1]++;
    for (int j = 0; j < n; j++) col_ptr[j + 1] += col_ptr[j];
    int nnz = col_ptr[n];
    row_idx.resize(nnz);
    vals.resize(nnz);
    std::vector<int> pos(n);
    for (int j = 0; j < n; j++) pos[j] = col_ptr[j];
    for (int j = 0; j < n; j++) {
        for (int i = 0; i <= j; i++) {
            double v = (P != nullptr) ? P[i * n + j] : 0.0;
            if (v != 0.0) {
                row_idx[pos[j]] = (osqp_int)i;
                vals[pos[j]]    = v;
                pos[j]++;
            }
        }
    }
}

// -------------------------------------------------------------------------
// Stacked constraint matrix → CSC.
// Rows: [Aeq (meq×n) ; Aineq (mineq×n) ; I_n (n×n)]
// Input matrices are row-major dense, NULL permitted for empty blocks.
// -------------------------------------------------------------------------
void stack_a_csc(
    const double* Aeq,   int meq,
    const double* Aineq, int mineq,
    int n,
    std::vector<osqp_int>& col_ptr,
    std::vector<osqp_int>& row_idx,
    std::vector<double>&   vals)
{
    int m_total = meq + mineq + n;
    col_ptr.resize(n + 1, 0);

    // Count nnz per column.
    for (int j = 0; j < n; j++) {
        int cnt = 0;
        if (Aeq)    for (int i = 0; i < meq;   i++) if (Aeq  [i * n + j] != 0.0) cnt++;
        if (Aineq)  for (int i = 0; i < mineq; i++) if (Aineq[i * n + j] != 0.0) cnt++;
        cnt++; // identity row
        col_ptr[j + 1] = cnt;
    }
    for (int j = 0; j < n; j++) col_ptr[j + 1] += col_ptr[j];
    int nnz = col_ptr[n];
    row_idx.resize(nnz);
    vals.resize(nnz);

    std::vector<int> pos(n);
    for (int j = 0; j < n; j++) pos[j] = col_ptr[j];

    for (int j = 0; j < n; j++) {
        // Equality rows.
        if (Aeq)
            for (int i = 0; i < meq; i++) {
                double v = Aeq[i * n + j];
                if (v != 0.0) { row_idx[pos[j]] = (osqp_int)i; vals[pos[j]] = v; pos[j]++; }
            }
        // Inequality rows.
        if (Aineq)
            for (int i = 0; i < mineq; i++) {
                double v = Aineq[i * n + j];
                if (v != 0.0) {
                    row_idx[pos[j]] = (osqp_int)(meq + i);
                    vals[pos[j]] = v; pos[j]++;
                }
            }
        // Identity (bound) row for variable j.
        row_idx[pos[j]] = (osqp_int)(meq + mineq + j);
        vals[pos[j]] = 1.0; pos[j]++;
    }
}

} // anonymous namespace

// =============================================================================
// Exported C entry points.
// =============================================================================
extern "C" {

const char* frahan_osqp_version(void) {
    return "frahan_osqp 1.0 (osqp v1.0.0)";
}
const char* frahan_osqp_last_error(void) { return g_last_error; }

int frahan_osqp_solve(
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
    int            msg_len)
{
    g_last_error[0] = '\0';
    if (status_val) *status_val = -99;
    if (iter_out)   *iter_out  = 0;
    if (obj_out)    *obj_out   = 0.0;
    if (msg && msg_len > 0) msg[0] = '\0';

    if (n <= 0 || !x_out) {
        set_error("frahan_osqp_solve: n=%d, x_out=%p — invalid inputs", n, x_out);
        return -1;
    }

    int m = meq + mineq + n;

    // -----------------------------------------------------------------------
    // Build P CSC (upper triangle).
    // -----------------------------------------------------------------------
    std::vector<osqp_int> P_col, P_row;
    std::vector<double>   P_val;
    dense_upper_to_csc(P, n, P_col, P_row, P_val);

    OSQPCsc P_csc;
    P_csc.m    = (osqp_int)n;
    P_csc.n    = (osqp_int)n;
    P_csc.nzmax= (osqp_int)P_val.size();
    P_csc.nz   = (osqp_int)-1; // CSC format flag
    P_csc.p    = P_col.data();
    P_csc.i    = P_row.data();
    P_csc.x    = P_val.data();

    // -----------------------------------------------------------------------
    // Build stacked A CSC.
    // -----------------------------------------------------------------------
    std::vector<osqp_int> A_col, A_row;
    std::vector<double>   A_val;
    stack_a_csc(Aeq, meq, Aineq, mineq, n, A_col, A_row, A_val);

    OSQPCsc A_csc;
    A_csc.m    = (osqp_int)m;
    A_csc.n    = (osqp_int)n;
    A_csc.nzmax= (osqp_int)A_val.size();
    A_csc.nz   = (osqp_int)-1;
    A_csc.p    = A_col.data();
    A_csc.i    = A_row.data();
    A_csc.x    = A_val.data();

    // -----------------------------------------------------------------------
    // Build q (linear objective).
    // -----------------------------------------------------------------------
    std::vector<double> q_vec(n, 0.0);
    if (q) for (int i = 0; i < n; i++) q_vec[i] = q[i];

    // -----------------------------------------------------------------------
    // Build stacked l, u bounds.
    //   rows 0..meq-1   : l = u = beq[i]     (equality)
    //   rows meq..meq+mineq-1 : l = -INF, u = bineq[i]   (Aineq x <= bineq)
    //   rows meq+mineq..m-1  : l = lb[i], u = ub[i]       (bounds)
    // -----------------------------------------------------------------------
    std::vector<double> l_vec(m), u_vec(m);
    for (int i = 0; i < meq; i++) {
        l_vec[i] = u_vec[i] = beq ? beq[i] : 0.0;
    }
    for (int i = 0; i < mineq; i++) {
        l_vec[meq + i] = NINF;
        u_vec[meq + i] = bineq ? bineq[i] : INF;
    }
    for (int i = 0; i < n; i++) {
        l_vec[meq + mineq + i] = lb ? lb[i] : NINF;
        u_vec[meq + mineq + i] = ub ? ub[i] : INF;
        // Clamp ±inf substitutes to OSQP-safe values.
        if (l_vec[meq + mineq + i] < NINF) l_vec[meq + mineq + i] = NINF;
        if (u_vec[meq + mineq + i] > INF)  u_vec[meq + mineq + i] = INF;
    }

    // -----------------------------------------------------------------------
    // OSQP settings.
    // -----------------------------------------------------------------------
    OSQPSettings settings;
    osqp_set_default_settings(&settings);
    settings.verbose      = 0;
    settings.eps_abs      = (eps_abs > 0) ? eps_abs : 1e-5;
    settings.eps_rel      = (eps_rel > 0) ? eps_rel : 1e-5;
    settings.max_iter     = (max_iter > 0) ? (c_int)max_iter : 4000;
    settings.polishing    = (c_int)(polish ? 1 : 0);
    settings.adaptive_rho = 1;
    settings.warm_starting= (warm_start_x != nullptr) ? 1 : 0;

    // -----------------------------------------------------------------------
    // Setup.
    // -----------------------------------------------------------------------
    OSQPSolver* solver = nullptr;
    c_int ret = osqp_setup(&solver, &P_csc, q_vec.data(), &A_csc,
                           l_vec.data(), u_vec.data(),
                           (c_int)m, (c_int)n, &settings);
    if (ret != 0 || !solver) {
        set_error("osqp_setup failed (code %d)", (int)ret);
        snprintf(msg, msg_len, "osqp_setup failed (code %d)", (int)ret);
        if (status_val) *status_val = -1;
        return -1;
    }

    // -----------------------------------------------------------------------
    // Warm start (if provided).
    // -----------------------------------------------------------------------
    if (warm_start_x) {
        osqp_warm_start(solver, warm_start_x, nullptr);
    }

    // -----------------------------------------------------------------------
    // Solve.
    // -----------------------------------------------------------------------
    osqp_solve(solver);

    OSQPInfo*     info = solver->info;
    OSQPSolution* sol  = solver->solution;

    int sv = (info != nullptr) ? (int)info->status_val : -99;
    int ni = (info != nullptr) ? (int)info->iter       :  0;
    double ov = (info != nullptr) ? (double)info->obj_val : 0.0;

    if (status_val) *status_val = sv;
    if (iter_out)   *iter_out   = ni;
    if (obj_out)    *obj_out    = ov;

    if (sv == OSQP_SOLVED || sv == OSQP_SOLVED_INACCURATE) {
        if (sol && sol->x) {
            for (int i = 0; i < n; i++) x_out[i] = sol->x[i];
        }
        if (msg && msg_len > 0 && info) {
            snprintf(msg, msg_len,
                "OSQP %s in %d iters (obj=%.6g, eps_abs=%.2g, eps_rel=%.2g).",
                info->status, ni, ov, settings.eps_abs, settings.eps_rel);
        }
    } else {
        if (msg && msg_len > 0 && info) {
            snprintf(msg, msg_len, "OSQP status: %s (val=%d, iter=%d).",
                info->status, sv, ni);
        }
    }

    osqp_cleanup(solver);
    return 0;
}

} // extern "C"
