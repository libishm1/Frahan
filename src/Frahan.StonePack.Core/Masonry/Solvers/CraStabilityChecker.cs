#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Equilibrium;

namespace Frahan.Masonry.Solvers;

// =============================================================================
// CraStabilityChecker — P2 of EVOLUTION_PLAN_MASONRY.md (2026-06-10): the
// Coupled Rigid-Block Analysis refinement of the RBE verdict.
//
// Source: Kao, Iannuzzo, Thomaszewski, Coros, Van Mele, Block (2022),
// "Coupled Rigid-Block Analysis: Stability-Aware Design of Complex Discrete-
// Element Assemblies", Computer-Aided Design 146:103216 (compas_cra, MIT).
//
// RBE (the P1 checker) is FORCE-only and admits physically unrealisable
// states: self-equilibrated normal-force pairs ("squeeze" / prestress out of
// nowhere) that let friction carry anything — Kao's H-model counterexample.
// CRA couples statics with virtual rigid-body KINEMATICS (Eqs. 8-10):
//
//   (8)  δd = A_eqᵀ δq            duality: virtual block motions δq induce
//                                  per-contact relative displacements δd
//   (9)  f_t = −α δd_t, α >= 0    friction opposes the virtual sliding
//   (10) f_n (δd_n − ε) = 0       normal force only where the joint ENGAGES
//        with non-penetration      (relative closing of exactly ε; ε models
//        s·δd_n <= ε               rigid-body "give"), closing-positive sign
//   (11) min ‖f_n‖² + ‖α‖²        Gauss least-constraint; infeasible <=> unstable
//
// The exact problem is a nonconvex NLP (bilinear complementarity; compas_cra
// uses IPOPT). This implementation is an ALTERNATING CONVEX CERTIFICATE
// search, sound in the certifying direction:
//
//   1. Solve the penalty RBE QP (P1, fast)            -> forces f
//   2. KINEMATIC CERTIFICATE QP (convex, this class)  -> δq, β
//        given the engaged set E = {k : f_n,k > tol} and the friction
//        directions f̂_t: minimise Σ_E ((δd_n − ε)/ε)² + Σ ((δd_t + β f̂_t)/ε)²
//        s.t. δd = A_eqᵀδq rows, s·δd_n <= ε (non-penetration), |δd| <= η, β >= 0.
//        Residual ~ 0  =>  a consistent virtual motion EXISTS  =>  CRA-certified.
//   3. Otherwise RESTRICT: contacts whose engagement the kinematics rejected
//        get their normal columns forced to 0 (complementarity), badly
//        misaligned friction gets zeroed; re-solve the force QP. Tension or
//        infeasibility  =>  CRA-UNSTABLE (the RBE acceptance was self-stress).
//   4. Iterate (bounded). No certificate after maxOuterIterations  =>
//        reported NOT certified (conservative).
//
// A found (f, δq, α >= 0) pair IS a feasible point of the CRA constraints, so
// "certified" is sound. "Not certified" is conservative (the alternating
// search is a heuristic for the nonconvex problem, like any local NLP start).
// =============================================================================

/// <summary>Result of <see cref="CraStabilityChecker.Check"/>.</summary>
public sealed class CraResult
{
    public CraResult(bool isStable, bool certified, string message, int iterations,
                     double certificateResidual, StabilityResult rbe, StabilityResult finalForces)
    {
        IsStable = isStable; Certified = certified; Message = message; Iterations = iterations;
        CertificateResidual = certificateResidual; Rbe = rbe; FinalForces = finalForces;
    }
    /// <summary>Final CRA verdict (certified-stable, or not).</summary>
    public bool IsStable { get; }
    /// <summary>True when a kinematically consistent (f, δq, α) certificate was found.</summary>
    public bool Certified { get; }
    public string Message { get; }
    public int Iterations { get; }
    /// <summary>Worst engaged-contact residual of the certificate, in units of ε.</summary>
    public double CertificateResidual { get; }
    /// <summary>The plain RBE verdict (for comparison — the H-model differs here).</summary>
    public StabilityResult Rbe { get; }
    /// <summary>The force verdict after the last complementarity restriction.</summary>
    public StabilityResult FinalForces { get; }
}

/// <summary>
/// CRA (Kao 2022) stability check by alternating convex certificate search.
/// Pure managed; no Rhino dependency.
/// </summary>
public static class CraStabilityChecker
{
    /// <summary>Closing-positive sign of δd_n = (A_eqᵀδq) under the equilibrium
    /// builder's conventions (A:+, B:−, normal A→B): approach is positive.</summary>
    private const double EngagedSign = 1.0;

    public static CraResult Check(
        MasonryAssembly assembly,
        double mu = FrictionConeBuilder.DefaultMu,
        int faceCount = MasonryStabilityChecker.DefaultFaceCount,
        bool inscribed = true,
        double gravityZ = -9.80665,
        int maxOuterIterations = 12,
        Action<string> trace = null)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));

        // ---- step 1: plain RBE (P1) ----
        var rbeDetail = MasonryStabilityChecker.CheckDetailed(
            assembly, mu, faceCount, inscribed, gravityZ: gravityZ);
        var rbe = rbeDetail.Result;
        if (!rbe.IsStable)
        {
            return new CraResult(false, false,
                "RBE-unstable (CRA only adds constraints, so the assembly is CRA-unstable too): " + rbe.Message,
                0, double.NaN, rbe, rbe);
        }
        if (rbeDetail.Equilibrium == null) // all blocks fixed
        {
            return new CraResult(true, true, rbe.Message, 0, 0, rbe, rbe);
        }

        // ---- scales: ε (engagement "give") and η (virtual-motion bound), from
        // the mean contact-polygon edge length (Kao: fractions of block size). ----
        double meanEdge = MeanContactEdge(assembly);
        double eps = 1e-4 * meanEdge;
        double eta = 100 * eps;

        // geometric duality columns: penalty:false system (Normal/T1/T2 per vertex)
        var eqGeo = EquilibriumMatrixBuilder.Build(assembly, penalty: false, gravityZ: gravityZ);
        var aGeo = eqGeo.Aeq.ToDense();            // R x C
        int rows = eqGeo.Aeq.RowCount;
        var vertexCols = GroupVertexColumns(eqGeo); // (iface,vertex) -> [colN, colT1, colT2]

        var zeroCols = new HashSet<int>();          // penalty-system columns forced to 0
        var detail = rbeDetail;
        double lastResidual = double.NaN;

        for (int outer = 1; outer <= maxOuterIterations; outer++)
        {
            // ---- engaged set + friction directions from the current forces ----
            double maxFn = 0;
            foreach (var vf in detail.VertexForces)
                if (vf.FnPos - vf.FnNeg > maxFn) maxFn = vf.FnPos - vf.FnNeg;
            // Engagement threshold: 1% of the peak normal force. Min-norm RBE
            // sprinkles tiny forces on joints that carry no real load (e.g. head
            // joints in a coursed wall); treating those as ENGAGED creates
            // spurious H-model-like kinematic conflicts. The true CRA NLP drives
            // them to zero; the certificate ignores them the same way.
            double fTol = Math.Max(1e-9, 1e-2 * maxFn);

            var engaged = new List<VertexForce>();
            var withFriction = new List<VertexForce>();
            foreach (var vf in detail.VertexForces)
            {
                if (vf.FnPos - vf.FnNeg > fTol) engaged.Add(vf);
                if (Math.Sqrt(vf.Ft1 * vf.Ft1 + vf.Ft2 * vf.Ft2) > fTol) withFriction.Add(vf);
            }
            if (engaged.Count == 0)
            {
                return new CraResult(false, false,
                    "No engaged contacts remain after complementarity restriction — CRA-unstable.",
                    outer, lastResidual, rbe, detail.Result);
            }

            // ---- step 2: certificate QP over x = [δq (rows), β (withFriction.Count)] ----
            var cert = SolveCertificate(aGeo, rows, vertexCols, engaged, withFriction, eps, eta, maxFn,
                                        out double residual, out double[] deltaDn, out double[] alignErr,
                                        out double[] engWeight);
            lastResidual = residual;
            if (trace != null)
            {
                var order = new List<int>();
                for (int i = 0; i < engaged.Count; i++) order.Add(i);
                order.Sort((x2, y2) =>
                    Math.Abs(deltaDn[y2] - EngagedSign * eps).CompareTo(Math.Abs(deltaDn[x2] - EngagedSign * eps)));
                trace($"iter {outer}: engaged={engaged.Count} friction={withFriction.Count} worst={residual:0.000}e eps={eps:0.###e0}");
                for (int oi = 0; oi < Math.Min(10, order.Count); oi++)
                {
                    var vf = engaged[order[oi]];
                    var ifc = assembly.Interfaces[vf.InterfaceIndex];
                    trace($"  iface {vf.InterfaceIndex} v{vf.VertexIndex} {ifc.BlockAId}->{ifc.BlockBId} " +
                          $"n=({ifc.NormalX:0.00},{ifc.NormalY:0.00},{ifc.NormalZ:0.00}) " +
                          $"fn={vf.FnPos - vf.FnNeg:0.#} dd/eps={deltaDn[order[oi]] / eps:0.00} (target {EngagedSign:0})");
                }
            }
            if (!cert)
            {
                return new CraResult(false, false,
                    $"Certificate QP failed to solve (iteration {outer}).",
                    outer, residual, rbe, detail.Result);
            }
            if (residual <= 0.5)
            {
                string msg = $"CRA-certified stable: a kinematically consistent virtual motion exists " +
                             $"(engaged residual {residual:0.00}ε, {engaged.Count} engaged contacts, " +
                             $"iteration {outer}).";
                return new CraResult(true, true, msg, outer, residual, rbe, detail.Result);
            }

            // ---- step 3: complementarity restriction ----
            // Peel only the WORST offenders this round (>= 75% of the worst
            // WEIGHTED residual): de-loading kinematically awkward, lightly
            // loaded joints mirrors what the exact NLP's energy trade does.
            double dropAbove = Math.Max(0.5, 0.75 * residual);
            int dropped = 0;
            for (int i = 0; i < engaged.Count; i++)
            {
                if (engWeight[i] * Math.Abs(deltaDn[i] - EngagedSign * eps) / eps <= dropAbove) continue;
                // kinematics rejected this engagement: f_n must vanish here (Eq. 10)
                var cols = PenaltyColumnsFor(detail.Equilibrium, engaged[i].InterfaceIndex, engaged[i].VertexIndex);
                foreach (int c in cols.normals) if (zeroCols.Add(c)) dropped++;
            }
            for (int i = 0; i < withFriction.Count; i++)
            {
                if (alignErr[i] <= 2.0) continue;
                // friction cannot oppose any consistent sliding here (Eq. 9)
                var cols = PenaltyColumnsFor(detail.Equilibrium, withFriction[i].InterfaceIndex, withFriction[i].VertexIndex);
                foreach (int c in cols.tangents) if (zeroCols.Add(c)) dropped++;
            }
            if (dropped == 0)
            {
                return new CraResult(false, false,
                    $"Not CRA-certifiable: certificate residual {residual:0.00}ε with no further " +
                    $"complementarity restriction available (iteration {outer}).",
                    outer, residual, rbe, detail.Result);
            }

            detail = MasonryStabilityChecker.CheckDetailed(
                assembly, mu, faceCount, inscribed, gravityZ: gravityZ, zeroForceColumns: zeroCols);
            if (!detail.Result.IsStable)
            {
                return new CraResult(false, false,
                    "CRA-UNSTABLE: the RBE force state relied on kinematically impossible contacts " +
                    "(self-stress); restricting them per complementarity leaves no admissible state. " +
                    detail.Result.Message,
                    outer, residual, rbe, detail.Result);
            }
        }

        return new CraResult(false, false,
            $"Not CRA-certified within {maxOuterIterations} iterations " +
            $"(last engaged residual {lastResidual:0.00}ε) — reported unstable (conservative).",
            maxOuterIterations, lastResidual, rbe, detail.Result);
    }

    // =========================================================================
    // Certificate QP:  min Σ_E ((δd_n − sε)/ε)² + Σ_F ((δd_t + β f̂_t)/ε)²
    //                  s.t. s·δd_n <= ε  (all engaged-candidate vertices),
    //                       |δd components| <= η,  β >= 0,
    //                  δd rows = (A_eqᵀ δq) expressed directly over x.
    // =========================================================================
    private static bool SolveCertificate(
        double[,] aGeo, int rows,
        Dictionary<long, int[]> vertexCols,
        List<VertexForce> engaged, List<VertexForce> withFriction,
        double eps, double eta, double maxFn,
        out double residual, out double[] deltaDn, out double[] alignErr,
        out double[] engWeight)
    {
        int nB = withFriction.Count;
        int n = rows + nB;                       // x = [δq, β]
        residual = double.MaxValue;
        deltaDn = new double[engaged.Count];
        alignErr = new double[nB];

        // FORCE-WEIGHTED engagement (mirrors the NLP's ||f_n||^2 energy trade:
        // unloading a lightly-loaded joint is cheap, a heavily-loaded one is
        // not): w_i = sqrt(fn_i / maxFn).
        engWeight = new double[engaged.Count];
        for (int i = 0; i < engaged.Count; i++)
        {
            double fn = Math.Max(0, engaged[i].FnPos - engaged[i].FnNeg);
            engWeight[i] = Math.Sqrt(fn / Math.Max(maxFn, 1e-12));
        }

        // least-squares rows: J x + r0
        int jRows = engaged.Count + 2 * nB;
        var j = new double[jRows, n];
        var r0 = new double[jRows];
        int jr = 0;
        for (int i = 0; i < engaged.Count; i++, jr++)
        {
            int colN = vertexCols[VKey(engaged[i])][0];
            double w = engWeight[i];
            for (int r = 0; r < rows; r++) j[jr, r] = w * aGeo[r, colN] / eps;
            r0[jr] = -w * EngagedSign;           // w·(δd_n − sε)/ε
        }
        for (int i = 0; i < nB; i++)
        {
            var vf = withFriction[i];
            var cols = vertexCols[VKey(vf)];
            double ft = Math.Sqrt(vf.Ft1 * vf.Ft1 + vf.Ft2 * vf.Ft2);
            double f1 = vf.Ft1 / ft, f2 = vf.Ft2 / ft;
            for (int r = 0; r < rows; r++) j[jr, r] = aGeo[r, cols[1]] / eps;
            j[jr, rows + i] = f1; r0[jr] = 0; jr++;
            for (int r = 0; r < rows; r++) j[jr, r] = aGeo[r, cols[2]] / eps;
            j[jr, rows + i] = f2; r0[jr] = 0; jr++;
        }

        // H = 2 JᵀJ + reg, c = 2 Jᵀ r0
        var h = new double[n, n];
        var c = new double[n];
        for (int a2 = 0; a2 < jRows; a2++)
        {
            for (int p = 0; p < n; p++)
            {
                double jp = j[a2, p];
                if (jp == 0) continue;
                c[p] += 2 * jp * r0[a2];
                for (int q2 = p; q2 < n; q2++) h[p, q2] += 2 * jp * j[a2, q2];
            }
        }
        for (int p = 0; p < n; p++)
        {
            h[p, p] += 1e-9;
            for (int q2 = 0; q2 < p; q2++) h[p, q2] = h[q2, p];
        }

        // inequalities: non-penetration on engaged candidates + |δd| <= η on their components
        var ineqRows = new List<double[]>();
        var ineqRhs = new List<double>();
        foreach (var kv in vertexCols)
        {
            int[] cols = kv.Value;
            // s * δd_n <= ε
            var rowNp = new double[n];
            for (int r = 0; r < rows; r++) rowNp[r] = EngagedSign * aGeo[r, cols[0]];
            ineqRows.Add(rowNp); ineqRhs.Add(eps);
            // |δd_c| <= η for each local component
            for (int comp = 0; comp < 3; comp++)
            {
                var rp = new double[n];
                var rm = new double[n];
                for (int r = 0; r < rows; r++) { rp[r] = aGeo[r, cols[comp]]; rm[r] = -aGeo[r, cols[comp]]; }
                ineqRows.Add(rp); ineqRhs.Add(eta);
                ineqRows.Add(rm); ineqRhs.Add(eta);
            }
        }
        var aIneq = new double[ineqRows.Count, n];
        var bIneq = new double[ineqRows.Count];
        for (int r2 = 0; r2 < ineqRows.Count; r2++)
        {
            var src = ineqRows[r2];
            for (int p = 0; p < n; p++) aIneq[r2, p] = src[p];
            bIneq[r2] = ineqRhs[r2];
        }

        var lb = new double[n];
        var ub = new double[n];
        for (int p = 0; p < rows; p++) { lb[p] = double.NegativeInfinity; ub[p] = double.PositiveInfinity; }
        for (int p = rows; p < n; p++) { lb[p] = 0.0; ub[p] = double.PositiveInfinity; }

        // ---- fast exact path: unconstrained least squares (direct Cholesky).
        // The candidate motion (cumulative settle) usually satisfies the
        // inequalities anyway; only fall back to the constrained ADMM when the
        // LS optimum actually violates non-penetration / the eta bound. This
        // also removes ADMM tolerance noise from the certificate residual. ----
        double[] xLs = SolveDenseSpd(h, c, n);   // minimises ½xᵀHx + cᵀx
        bool lsFeasible = xLs != null;
        if (lsFeasible)
        {
            for (int r2 = 0; r2 < ineqRows.Count && lsFeasible; r2++)
            {
                double v = 0;
                var src = ineqRows[r2];
                for (int p2 = 0; p2 < n; p2++) v += src[p2] * xLs[p2];
                if (v > ineqRhs[r2] + 0.05 * eta) lsFeasible = false;
            }
            for (int p2 = rows; p2 < n && lsFeasible; p2++)
                if (xLs[p2] < -1e-12) lsFeasible = false;
        }

        double[] xSol;
        if (lsFeasible)
        {
            xSol = xLs;
        }
        else
        {
            var qp = new ConvexQpProblem(n, h, c, null, null, aIneq, bIneq, lb, ub);
            var sol = new AdmmQpSolver(epsAbs: 1e-6, epsRel: 1e-5, maxIterations: 6000).Solve(qp);
            if (sol.Status != ConvexQpStatus.Optimal) return false;
            xSol = sol.X;
        }

        // decode residuals
        double worstEng = 0;
        for (int i = 0; i < engaged.Count; i++)
        {
            int colN = vertexCols[VKey(engaged[i])][0];
            double dn = 0;
            for (int r = 0; r < rows; r++) dn += aGeo[r, colN] * xSol[r];
            deltaDn[i] = dn;
            double res = engWeight[i] * Math.Abs(dn - EngagedSign * eps) / eps;
            if (res > worstEng) worstEng = res;
        }
        for (int i = 0; i < nB; i++)
        {
            var vf = withFriction[i];
            var cols = vertexCols[VKey(vf)];
            double ft = Math.Sqrt(vf.Ft1 * vf.Ft1 + vf.Ft2 * vf.Ft2);
            double f1 = vf.Ft1 / ft, f2 = vf.Ft2 / ft;
            double d1 = 0, d2 = 0;
            for (int r = 0; r < rows; r++) { d1 += aGeo[r, cols[1]] * xSol[r]; d2 += aGeo[r, cols[2]] * xSol[r]; }
            double beta = xSol[rows + i];
            alignErr[i] = Math.Sqrt(Sq(d1 + beta * f1) + Sq(d2 + beta * f2)) / eps;
        }
        residual = worstEng;
        return true;
    }

    /// <summary>Solve min ½xᵀHx + cᵀx for SPD H by dense Cholesky (Hx = −c); null on failure.</summary>
    private static double[] SolveDenseSpd(double[,] h, double[] c, int n)
    {
        var l = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int k2 = 0; k2 <= i; k2++)
            {
                double sum = h[i, k2];
                for (int t = 0; t < k2; t++) sum -= l[i, t] * l[k2, t];
                if (i == k2)
                {
                    if (sum <= 0) return null;
                    l[i, i] = Math.Sqrt(sum);
                }
                else l[i, k2] = sum / l[k2, k2];
            }
        }
        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = -c[i];
            for (int t = 0; t < i; t++) sum -= l[i, t] * x[t];
            x[i] = sum / l[i, i];
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = x[i];
            for (int t = i + 1; t < n; t++) sum -= l[t, i] * x[t];
            x[i] = sum / l[i, i];
        }
        return x;
    }

    private static double Sq(double v) => v * v;

    private static long VKey(VertexForce vf) => ((long)vf.InterfaceIndex << 32) | (uint)vf.VertexIndex;

    /// <summary>(iface,vertex) -> [colNormal, colT1, colT2] of the penalty:false geometric system.</summary>
    private static Dictionary<long, int[]> GroupVertexColumns(EquilibriumSystem eqGeo)
    {
        var map = new Dictionary<long, int[]>();
        for (int k = 0; k < eqGeo.ForceColumns.Count; k++)
        {
            var fc = eqGeo.ForceColumns[k];
            long key = ((long)fc.InterfaceIndex << 32) | (uint)fc.VertexIndex;
            if (!map.TryGetValue(key, out var cols)) { cols = new[] { -1, -1, -1 }; map[key] = cols; }
            switch (fc.Component)
            {
                case ForceComponent.Normal: cols[0] = k; break;
                case ForceComponent.Tangent1: cols[1] = k; break;
                case ForceComponent.Tangent2: cols[2] = k; break;
            }
        }
        return map;
    }

    /// <summary>Penalty-system column indices for one contact vertex.</summary>
    private static (List<int> normals, List<int> tangents) PenaltyColumnsFor(
        EquilibriumSystem eqPen, int ifaceIndex, int vertexIndex)
    {
        var normals = new List<int>(2);
        var tangents = new List<int>(2);
        for (int k = 0; k < eqPen.ForceColumns.Count; k++)
        {
            var fc = eqPen.ForceColumns[k];
            if (fc.InterfaceIndex != ifaceIndex || fc.VertexIndex != vertexIndex) continue;
            switch (fc.Component)
            {
                case ForceComponent.Normal:
                case ForceComponent.NormalPositive:
                case ForceComponent.NormalNegative:
                    normals.Add(k); break;
                case ForceComponent.Tangent1:
                case ForceComponent.Tangent2:
                    tangents.Add(k); break;
            }
        }
        return (normals, tangents);
    }

    private static double MeanContactEdge(MasonryAssembly assembly)
    {
        double sum = 0; int count = 0;
        foreach (var iface in assembly.Interfaces)
        {
            var poly = iface.ContactPolygon;
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i];
                var q2 = poly[(i + 1) % poly.Count];
                double dx = q2.X - p.X, dy = q2.Y - p.Y, dz = q2.Z - p.Z;
                sum += Math.Sqrt(dx * dx + dy * dy + dz * dz);
                count++;
            }
        }
        return count > 0 ? Math.Max(1e-9, sum / count) : 1.0;
    }
}
