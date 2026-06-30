#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Equilibrium;
using Frahan.Masonry.Interfaces;

namespace Frahan.Masonry.Solvers;

// =============================================================================
// MasonryStabilityChecker — the P1 "verifier wired end-to-end" of
// EVOLUTION_PLAN_MASONRY.md (2026-06-10): stones -> MasonryAssembly ->
// EquilibriumMatrixBuilder -> FrictionConeBuilder (K=8, INSCRIBED pyramid) ->
// RbeQpFormulation.BuildPhysicsCorrected -> ManagedQpSolver -> verdict.
//
// Semantics (limit-analysis static/lower-bound theorem, Kao 2021/2022 RBE):
//   * QP FEASIBLE  => an admissible compressive + friction-consistent contact
//     force state exists => the assembly is STABLE under self-weight (for the
//     rigid, no-tension, Coulomb model).
//   * QP INFEASIBLE => no such state exists => NOT stable (RBE-unstable).
// RBE is force-only; it can accept states the coupled CRA (Kao 2022 Eqs 8-14)
// rejects — that refinement is plan phase P2. This class is the shared
// "does it stand?" gate both generation flows pass through.
//
// Friction: K=8 inscribed pyramid by default (mu_eff = mu*cos(pi/8) ~ 0.924*mu)
// — conservative, fixing the V3-review blocker (compas_cra's K=4 circumscribed
// pyramid over-estimates capacity by up to sqrt(2) on the diagonal).
// =============================================================================

/// <summary>Per-interface diagnostic of <see cref="MasonryStabilityChecker"/>.</summary>
public sealed class InterfaceUtilization
{
    public InterfaceUtilization(int interfaceIndex, string blockAId, string blockBId,
                                double maxFrictionUtilization, double minNormalForce,
                                double maxNormalForce)
    {
        InterfaceIndex = interfaceIndex; BlockAId = blockAId; BlockBId = blockBId;
        MaxFrictionUtilization = maxFrictionUtilization;
        MinNormalForce = minNormalForce; MaxNormalForce = maxNormalForce;
    }
    public int InterfaceIndex { get; }
    public string BlockAId { get; }
    public string BlockBId { get; }
    /// <summary>max over vertices of |f_t| / (mu_eff * f_n); 1.0 = friction cone saturated.</summary>
    public double MaxFrictionUtilization { get; }
    public double MinNormalForce { get; }
    public double MaxNormalForce { get; }
}

/// <summary>Result of <see cref="MasonryStabilityChecker.Check"/>.</summary>
public sealed class StabilityResult
{
    public StabilityResult(bool isStable, ConvexQpStatus status, string message,
                           double maxCompression, double maxFrictionUtilization,
                           int weakestInterfaceIndex,
                           IReadOnlyList<InterfaceUtilization> interfaces,
                           int freeBlockCount, int interfaceCount, int contactVertexCount)
    {
        IsStable = isStable; Status = status; Message = message;
        MaxCompression = maxCompression; MaxFrictionUtilization = maxFrictionUtilization;
        WeakestInterfaceIndex = weakestInterfaceIndex; Interfaces = interfaces;
        FreeBlockCount = freeBlockCount; InterfaceCount = interfaceCount;
        ContactVertexCount = contactVertexCount;
    }
    /// <summary>True when an admissible contact-force state exists (RBE-stable).</summary>
    public bool IsStable { get; }
    public ConvexQpStatus Status { get; }
    public string Message { get; }
    /// <summary>Largest compressive normal force in the solution (model force units).</summary>
    public double MaxCompression { get; }
    /// <summary>Worst friction-cone utilization across all contacts (1.0 = saturated).</summary>
    public double MaxFrictionUtilization { get; }
    /// <summary>Index (into the assembly interface list) of the most-utilised interface, or -1.</summary>
    public int WeakestInterfaceIndex { get; }
    public IReadOnlyList<InterfaceUtilization> Interfaces { get; }
    public int FreeBlockCount { get; }
    public int InterfaceCount { get; }
    public int ContactVertexCount { get; }
}

/// <summary>Per-contact-vertex force decode (penalty columns), for CRA and diagnostics.</summary>
public sealed class VertexForce
{
    public VertexForce(int interfaceIndex, int vertexIndex,
                       double fnPos, double fnNeg, double ft1, double ft2)
    {
        InterfaceIndex = interfaceIndex; VertexIndex = vertexIndex;
        FnPos = fnPos; FnNeg = fnNeg; Ft1 = ft1; Ft2 = ft2;
    }
    public int InterfaceIndex { get; }
    public int VertexIndex { get; }
    public double FnPos { get; }
    public double FnNeg { get; }
    public double Ft1 { get; }
    public double Ft2 { get; }
}

/// <summary>Result of <see cref="MasonryStabilityChecker.CheckDetailed"/> — the verdict plus
/// the raw equilibrium system and per-vertex forces the CRA coupling consumes.</summary>
public sealed class DetailedStabilityResult
{
    public DetailedStabilityResult(StabilityResult result, EquilibriumSystem equilibrium,
                                   IReadOnlyList<VertexForce> vertexForces)
    { Result = result; Equilibrium = equilibrium; VertexForces = vertexForces; }
    public StabilityResult Result { get; }
    /// <summary>The penalty-mode equilibrium system (null when the guard short-circuited).</summary>
    public EquilibriumSystem Equilibrium { get; }
    public IReadOnlyList<VertexForce> VertexForces { get; }
}

/// <summary>
/// End-to-end RBE stability check for a masonry assembly. Pure managed;
/// no Rhino dependency. The shared "does it stand?" gate of both the
/// imposition (generated) and negotiation (stacked) flows.
/// </summary>
public static class MasonryStabilityChecker
{
    /// <summary>Default friction-pyramid face count (tighter than compas_cra's 4).</summary>
    public const int DefaultFaceCount = 8;

    /// <summary>
    /// Check a pre-built assembly. Blocks listed in
    /// <c>assembly.BoundaryConditions</c> are fixed (ground / supports).
    /// </summary>
    public static StabilityResult Check(
        MasonryAssembly assembly,
        double mu = FrictionConeBuilder.DefaultMu,
        int faceCount = DefaultFaceCount,
        bool inscribed = true,
        double tangentialScale = 1.0,
        double gravityZ = -9.80665)
        => CheckDetailed(assembly, mu, faceCount, inscribed, tangentialScale, gravityZ).Result;

    /// <summary>
    /// Detailed variant: also returns the penalty equilibrium system and the
    /// per-vertex force decode, and optionally forces selected force columns to
    /// zero (the CRA coupling's complementarity restriction step).
    /// </summary>
    public static DetailedStabilityResult CheckDetailed(
        MasonryAssembly assembly,
        double mu = FrictionConeBuilder.DefaultMu,
        int faceCount = DefaultFaceCount,
        bool inscribed = true,
        double tangentialScale = 1.0,
        double gravityZ = -9.80665,
        ISet<int> zeroForceColumns = null)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));

        // ---- Guard: a free block with no interface at all can never be supported. ----
        var touched = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < assembly.Interfaces.Count; i++)
        {
            touched.Add(assembly.Interfaces[i].BlockAId);
            touched.Add(assembly.Interfaces[i].BlockBId);
        }
        int freeCount = 0;
        for (int i = 0; i < assembly.Blocks.Count; i++)
        {
            var b = assembly.Blocks[i];
            if (assembly.BoundaryConditions.IsFixed(b.Id)) continue;
            freeCount++;
            if (!touched.Contains(b.Id))
            {
                return new DetailedStabilityResult(new StabilityResult(false, ConvexQpStatus.Infeasible,
                    $"Free block '{b.Id}' has no contact interface (floating).",
                    0, 0, -1, new List<InterfaceUtilization>(),
                    freeCount, assembly.Interfaces.Count, 0), null, new List<VertexForce>());
            }
        }
        if (freeCount == 0)
        {
            return new DetailedStabilityResult(new StabilityResult(true, ConvexQpStatus.Optimal,
                "All blocks are fixed; nothing to check.",
                0, 0, -1, new List<InterfaceUtilization>(),
                0, assembly.Interfaces.Count, 0), null, new List<VertexForce>());
        }

        // ---- Guard: a degenerate (near-zero signed volume) FREE block. Self-weight is
        // density*|V|*g and BlockCenterOfMass zeroes V below 1e-12 (open / inward-wound /
        // flat triangulation), so the block becomes WEIGHTLESS -- and a weightless block
        // trivially "balances", producing a SILENT false-'stable' verdict. This is the
        // exact hazard a raw triangle-mesh / scan-rubble feeder hits. Reject it instead
        // of certifying garbage; the fix is upstream (weld + unify normals + close). ----
        for (int i = 0; i < assembly.Blocks.Count; i++)
        {
            var b = assembly.Blocks[i];
            if (assembly.BoundaryConditions.IsFixed(b.Id)) continue;
            if (Math.Abs(BlockCenterOfMass.SignedVolume(b)) < 1e-12)
            {
                return new DetailedStabilityResult(new StabilityResult(false, ConvexQpStatus.Infeasible,
                    $"Free block '{b.Id}' has degenerate signed volume (open / inward-wound / flat) -> zero self-weight; " +
                    "the verdict would be a false 'stable'. Repair the block (weld + unify normals + close it) before checking.",
                    0, 0, -1, new List<InterfaceUtilization>(),
                    freeCount, assembly.Interfaces.Count, 0), null, new List<VertexForce>());
            }
        }

        // ---- Formulate + solve the PENALTY RBE QP (Kao 2022 Eq. 14 semantics,
        // minus the displacement coupling, which is evolution phase P2):
        // f_n = f_n+ - f_n-, both >= 0, with a large Hessian weight gamma on
        // f_n-. The problem is always feasible, so the verdict is read from the
        // optimum instead of from infeasibility: max f_n- ~ 0  =>  stable;
        // max f_n- >> 0  =>  the assembly needs tension there  =>  unstable,
        // and ||f_n-|| localises WHERE. Solved with the OSQP-style AdmmQpSolver
        // (the Dykstra ManagedQpSolver diverges on real masonry row scales).
        // Gamma note: the verdict reads the tension MAGNITUDE (fixed by the
        // statics), not the objective value, so a moderate gamma suffices and
        // keeps the Hessian condition number ADMM-friendly (1e6 stalls it).
        const double TensionPenaltyGamma = 1e3;
        var equilibrium = EquilibriumMatrixBuilder.Build(assembly, penalty: true, gravityZ: gravityZ);
        var friction = FrictionConeBuilder.Build(equilibrium, mu, faceCount, inscribed);
        var qp = RbeQpFormulation.BuildPhysicsCorrected(
            equilibrium, friction.Afr, hessianScale: 1.0, tangentialScale: tangentialScale,
            negativeNormalScale: TensionPenaltyGamma);
        if (zeroForceColumns != null)
        {
            foreach (int col in zeroForceColumns)
            {
                if (col < 0 || col >= qp.VariableCount) continue;
                qp.LowerBounds[col] = 0.0;
                qp.UpperBounds[col] = 0.0;
            }
        }
        // Tolerance note: the verdict reads max f_n- against 1e-3 * maxCompression,
        // so the QP only needs ~1e-4 relative accuracy — OSQP-class defaults.
        // (1e-6/1e-5 multiplies the iteration count for no verdict change.)
        //
        // KB-10 (2026-06-11) LS-first warm start: the penalty Hessian is
        // DIAGONAL, so the equality-constrained QP has a closed-form KKT
        // solution (dense Cholesky on the small 6*freeBlocks dual system).
        // When that point — projected onto the complementarity split, which
        // preserves Aeq f and Afr f exactly — satisfies the friction cone and
        // bounds and demands no tension, it IS an admissible compressive force
        // state: by the static (lower-bound) theorem the verdict is decoded
        // from it directly and ADMM is skipped. Otherwise it warm-starts the
        // ADMM (cold ADMM degrades steeply past ~50 exact-joint interfaces).
        // When the LS path declines (non-diagonal H, singular dual system),
        // behaviour is exactly the previous cold-start solve.
        double[] lsPoint = TryLsFirstKktPoint(equilibrium, qp, out bool lsCertified, out string lsCertificate);
        ConvexQpResult sol;
        if (lsPoint != null && lsCertified)
            sol = new ConvexQpResult(ConvexQpStatus.Optimal, lsPoint,
                                     DiagonalQpObjective(qp, lsPoint), lsCertificate);
        else if (MasonrySolverRegistry.Default is OsqpQpSolver osqp)
            sol = osqp.Solve(qp); // native OSQP (frahan_osqp) — robust on ill-conditioned QPs
        else
            sol = new AdmmQpSolver(epsAbs: 1e-4, epsRel: 1e-4).Solve(qp, lsPoint); // managed fallback (unchanged)

        int vertexCount = equilibrium.ForceColumns.Count / Math.Max(1, equilibrium.ForceComponentsPerVertex);

        if (sol.Status != ConvexQpStatus.Optimal)
        {
            return new DetailedStabilityResult(new StabilityResult(false, sol.Status,
                $"Penalty-RBE QP {sol.Status}: {sol.SolverMessage}",
                0, 0, -1, new List<InterfaceUtilization>(),
                freeCount, assembly.Interfaces.Count, vertexCount), equilibrium, new List<VertexForce>());
        }

        // ---- Decode per-vertex forces -> tension verdict + per-interface utilization. ----
        double muEff = friction.Mu; // Build() stored the effective coefficient
        var perVertex = new Dictionary<long, double[]>(); // key -> [fnPos, fnNeg, ft1, ft2]
        for (int k = 0; k < equilibrium.ForceColumns.Count; k++)
        {
            var fc = equilibrium.ForceColumns[k];
            long key = ((long)fc.InterfaceIndex << 32) | (uint)fc.VertexIndex;
            if (!perVertex.TryGetValue(key, out var f)) { f = new double[4]; perVertex[key] = f; }
            switch (fc.Component)
            {
                case ForceComponent.Normal: f[0] = sol.X[k]; break;
                case ForceComponent.NormalPositive: f[0] = sol.X[k]; break;
                case ForceComponent.NormalNegative: f[1] = sol.X[k]; break;
                case ForceComponent.Tangent1: f[2] = sol.X[k]; break;
                case ForceComponent.Tangent2: f[3] = sol.X[k]; break;
            }
        }

        // Pass 1: force scale (needed to floor the utilization ratio — vertices
        // carrying ~zero normal force produce meaningless ft/(mu*fn) dust).
        double maxCompression = 0, maxTension = 0;
        foreach (var kv in perVertex)
        {
            if (kv.Value[0] > maxCompression) maxCompression = kv.Value[0];
            if (kv.Value[1] > maxTension) maxTension = kv.Value[1];
        }
        double fnFloor = Math.Max(1e-12, 1e-4 * maxCompression);

        // Pass 2: per-interface aggregation with the floored utilization.
        var perIface = new Dictionary<int, double[]>(); // iface -> [maxUtil, minFn, maxFn, maxTension]
        double maxUtil = 0;
        foreach (var kv in perVertex)
        {
            int iface = (int)(kv.Key >> 32);
            double fnPos = kv.Value[0], fnNeg = kv.Value[1];
            double ft = Math.Sqrt(kv.Value[2] * kv.Value[2] + kv.Value[3] * kv.Value[3]);
            double util = fnPos >= fnFloor ? ft / (muEff * fnPos) : 0.0;
            if (util > maxUtil) maxUtil = util;
            if (!perIface.TryGetValue(iface, out var agg))
            {
                agg = new[] { 0.0, double.MaxValue, 0.0, 0.0 };
                perIface[iface] = agg;
            }
            if (util > agg[0]) agg[0] = util;
            double fn = fnPos - fnNeg;
            if (fn < agg[1]) agg[1] = fn;
            if (fn > agg[2]) agg[2] = fn;
            if (fnNeg > agg[3]) agg[3] = fnNeg;
        }

        // Stable iff the residual tension is negligible relative to the force scale.
        double forceScale = Math.Max(maxCompression, 1e-9);
        double tensionTol = 1e-3 * forceScale;
        bool stable = maxTension <= tensionTol;

        var utils = new List<InterfaceUtilization>();
        int weakest = -1; double weakestScore = -1;
        foreach (var kv in perIface)
        {
            var iface = assembly.Interfaces[kv.Key];
            utils.Add(new InterfaceUtilization(kv.Key, iface.BlockAId, iface.BlockBId,
                                               kv.Value[0], kv.Value[1], kv.Value[2]));
            // weakest = the interface demanding the most tension; fall back to
            // friction utilization when no tension exists anywhere.
            double score = kv.Value[3] > tensionTol ? 1e6 + kv.Value[3] : kv.Value[0];
            if (score > weakestScore) { weakestScore = score; weakest = kv.Key; }
        }

        string verdict = stable
            ? $"RBE-stable: admissible compressive force state (max compression {maxCompression:0.###}, " +
              $"worst friction utilization {maxUtil:0.00}, residual tension {maxTension:0.###e0})."
            : $"NOT RBE-stable: the assembly demands tensile contact force " +
              $"(max tension {maxTension:0.###} vs compression {maxCompression:0.###}; " +
              $"||f_n-|| localises the unstable region per Kao 2022 Eq. 14).";

        var vertexForces = new List<VertexForce>(perVertex.Count);
        foreach (var kv in perVertex)
        {
            vertexForces.Add(new VertexForce((int)(kv.Key >> 32), (int)(uint)(kv.Key & 0xFFFFFFFF),
                                             kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3]));
        }

        return new DetailedStabilityResult(new StabilityResult(stable, sol.Status, verdict,
            maxCompression, maxUtil, weakest, utils,
            freeCount, assembly.Interfaces.Count, vertexCount), equilibrium, vertexForces);
    }

    /// <summary>
    /// Convenience overload: triangulated stone meshes -> auto-detected contacts
    /// -> assembly -> check. Blocks whose lowest vertex is within
    /// <paramref name="fixBelowZ"/> of the global minimum Z are treated as fixed
    /// (ground course).
    /// </summary>
    /// <param name="vertexCoordsXyz">Per stone: flat xyz triples.</param>
    /// <param name="triangleIndices">Per stone: flat triangle index triples.</param>
    /// <summary>
    /// Detector-path stability check. NOTE <paramref name="fixBelowZ"/> is a
    /// TOLERANCE ABOVE THE LOWEST VERTEX of the whole assembly (blocks with
    /// minZ &lt;= globalMinZ + fixBelowZ become supports), NOT an absolute
    /// world-Z plane. Passing a negative value anchors nothing.
    /// </summary>
    public static StabilityResult CheckMeshes(
        IReadOnlyList<IReadOnlyList<double>> vertexCoordsXyz,
        IReadOnlyList<IReadOnlyList<int>> triangleIndices,
        double density = 2400.0,
        double contactDistanceTol = 1e-3,
        double contactAngleTolDeg = 5.0,
        double fixBelowZ = 1e-3,
        double mu = FrictionConeBuilder.DefaultMu,
        int faceCount = DefaultFaceCount,
        bool inscribed = true)
    {
        if (vertexCoordsXyz == null) throw new ArgumentNullException(nameof(vertexCoordsXyz));
        if (triangleIndices == null) throw new ArgumentNullException(nameof(triangleIndices));
        if (vertexCoordsXyz.Count != triangleIndices.Count)
            throw new ArgumentException("vertexCoordsXyz and triangleIndices must be parallel lists");
        if (vertexCoordsXyz.Count == 0)
            throw new ArgumentException("at least one stone is required", nameof(vertexCoordsXyz));

        int n = vertexCoordsXyz.Count;
        var snapshots = new List<MeshSnapshot>(n);
        var ids = new List<string>(n);
        var minZ = new double[n];
        double globalMinZ = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            snapshots.Add(new MeshSnapshot(vertexCoordsXyz[i], triangleIndices[i]));
            ids.Add("stone_" + i.ToString("000"));
            double mz = double.MaxValue;
            var coords = vertexCoordsXyz[i];
            for (int k = 2; k < coords.Count; k += 3)
                if (coords[k] < mz) mz = coords[k];
            minZ[i] = mz;
            if (mz < globalMinZ) globalMinZ = mz;
        }

        var interfaces = MeshContactDetector.Detect(
            snapshots, ids, contactDistanceTol, contactAngleTolDeg);

        var blocks = new List<MasonryBlock>(n);
        var fixedIds = new List<string>();
        for (int i = 0; i < n; i++)
        {
            blocks.Add(new MasonryBlock(ids[i], vertexCoordsXyz[i], triangleIndices[i], density));
            if (minZ[i] <= globalMinZ + fixBelowZ) fixedIds.Add(ids[i]);
        }

        var assembly = new MasonryAssembly(blocks, interfaces, new BoundaryConditions(fixedIds));
        return Check(assembly, mu, faceCount, inscribed);
    }

    /// <summary>
    /// Build a MasonryAssembly from triangulated stone meshes (auto-detected
    /// contacts; lowest course fixed) without solving — for callers that want
    /// to run <see cref="Check"/> or <see cref="CraStabilityChecker.Check"/>
    /// themselves.
    /// </summary>
    /// <summary>
    /// Build a MasonryAssembly via contact detection. NOTE
    /// <paramref name="fixBelowZ"/> is a TOLERANCE ABOVE THE LOWEST VERTEX
    /// (blocks with minZ &lt;= globalMinZ + fixBelowZ become supports), NOT an
    /// absolute world-Z plane.
    /// </summary>
    public static MasonryAssembly BuildAssemblyFromMeshes(
        IReadOnlyList<IReadOnlyList<double>> vertexCoordsXyz,
        IReadOnlyList<IReadOnlyList<int>> triangleIndices,
        double density = 2400.0,
        double contactDistanceTol = 1e-3,
        double contactAngleTolDeg = 5.0,
        double fixBelowZ = 1e-3)
    {
        if (vertexCoordsXyz == null) throw new ArgumentNullException(nameof(vertexCoordsXyz));
        if (triangleIndices == null) throw new ArgumentNullException(nameof(triangleIndices));
        if (vertexCoordsXyz.Count != triangleIndices.Count)
            throw new ArgumentException("vertexCoordsXyz and triangleIndices must be parallel lists");
        int n = vertexCoordsXyz.Count;
        var snapshots = new List<MeshSnapshot>(n);
        var ids = new List<string>(n);
        var minZ = new double[n];
        double globalMinZ = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            snapshots.Add(new MeshSnapshot(vertexCoordsXyz[i], triangleIndices[i]));
            ids.Add("stone_" + i.ToString("000"));
            double mz = double.MaxValue;
            var coords = vertexCoordsXyz[i];
            for (int k = 2; k < coords.Count; k += 3)
                if (coords[k] < mz) mz = coords[k];
            minZ[i] = mz;
            if (mz < globalMinZ) globalMinZ = mz;
        }
        var interfaces = MeshContactDetector.Detect(
            snapshots, ids, contactDistanceTol, contactAngleTolDeg); // coplanar resolver ON by default (KB-9)
        var blocks = new List<MasonryBlock>(n);
        var fixedIds = new List<string>();
        for (int i = 0; i < n; i++)
        {
            blocks.Add(new MasonryBlock(ids[i], vertexCoordsXyz[i], triangleIndices[i], density));
            if (minZ[i] <= globalMinZ + fixBelowZ) fixedIds.Add(ids[i]);
        }
        return new MasonryAssembly(blocks, interfaces, new BoundaryConditions(fixedIds));
    }

    // =========================================================================
    // KB-10 (2026-06-11) — LS-first warm start for the penalty-RBE path.
    //
    // The equality-constrained relaxation  min ½fᵀHf + cᵀf  s.t. Aeq f = beq
    // has a closed-form KKT solution because H is DIAGONAL in the penalty
    // formulation:
    //
    //     f = H⁻¹(Aeqᵀ y − c),   (Aeq H⁻¹ Aeqᵀ) y = beq + Aeq H⁻¹ c
    //
    // with the dual system only m = 6·freeBlocks square (dense Cholesky).
    // The raw KKT point splits each normal pair fn± with a small negative
    // component (fn∓ = −v/(1+γ) dust), so it is projected onto the
    // complementarity split fn+ = max(v,0), fn- = max(−v,0). The fn+/fn-
    // columns are EXACT negatives of each other in both Aeq
    // (EquilibriumMatrixBuilder shift=4) and Afr (FrictionConeBuilder ±mu),
    // so the projection preserves Aeq·f and Afr·f exactly. The per-pair
    // split is the optimal one for any fixed net value v.
    //
    // If the projected point satisfies the friction cone, the bounds, and
    // demands no tension (max fn- <= 1e-3 · max fn+, the verdict tolerance),
    // it is an admissible compressive force state — the static lower-bound
    // theorem certifies STABLE from its existence alone, so ADMM is skipped
    // (the decoded diagnostics come from this min-weighted-norm point, within
    // ~γ⁻¹ of the unique QP optimum on the normal split). When only the
    // friction cone blocks the point, a POCS cone polish (below) searches for
    // a nearby admissible point before giving up. The short-circuit NEVER
    // declares unstable: any remaining infeasibility or residual tension
    // falls through to the ADMM (warm-started at the projected point).
    //
    // Measured on the KB-10 exact-joint walls (Debug, this host): 54 ifaces
    // 5.4 s -> 0.07 s, 95 ifaces 24 s -> 0.4 s, 147 ifaces 86 s -> 1.1 s.
    // =========================================================================

    /// <summary>
    /// Closed-form equality-KKT point for the diagonal-Hessian penalty QP,
    /// projected onto the complementarity split. Returns null when the path
    /// declines (no equalities, non-diagonal Hessian, fixed columns at a
    /// non-zero value, or a singular dual system) — callers then run the
    /// unchanged cold-start ADMM. <paramref name="certified"/> is true only
    /// when the point is verified feasible (equality + cone + bounds) AND
    /// tension-free, i.e. the verdict can be decoded from it directly.
    /// </summary>
    private static double[] TryLsFirstKktPoint(
        EquilibriumSystem equilibrium, ConvexQpProblem qp,
        out bool certified, out string certificate)
    {
        certified = false;
        certificate = null;
        int n = qp.VariableCount;
        int meq = qp.EqualityRowCount;
        if (meq == 0 || n == 0) return null;
        var aeq = qp.EqualityMatrix;
        var beq = qp.EqualityRhs;
        var c = qp.LinearObjective;

        // ---- H must be diagonal positive (it is, for every RBE formulation). ----
        var hess = qp.Hessian;
        var invH0 = new double[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
                if (i != j && hess[i, j] != 0) return null; // not diagonal -> decline
            double d = hess[i, i];
            if (d <= 0) return null;
            invH0[i] = 1.0 / d;
        }
        // Fixed columns (lb == ub, e.g. the CRA complementarity restriction
        // pins forces to 0): exclude from the KKT system via invH = 0, which
        // forces f = 0 there. Only the fixed-at-zero case is supported.
        for (int i = 0; i < n; i++)
        {
            if (qp.LowerBounds[i] == qp.UpperBounds[i])
            {
                if (qp.LowerBounds[i] != 0.0) return null;
                invH0[i] = 0.0;
            }
        }

        // ---- Column row-support (each force column touches <= 12 equality rows). ----
        var colRows = new int[n][];
        {
            var scratch = new List<int>(16);
            for (int k = 0; k < n; k++)
            {
                scratch.Clear();
                for (int r = 0; r < meq; r++)
                    if (aeq[r, k] != 0) scratch.Add(r);
                colRows[k] = scratch.ToArray();
            }
        }

        // ---- Round 0: plain equality-KKT. Certified directly when it already
        // sits inside the friction cone (the common case for squat coursed
        // walls — the LS point is then the QP optimum restricted to the
        // equality manifold and feasible, hence admissible). ----
        double[] f0 = SolveLsCertificateRound(
            equilibrium, qp, invH0, colRows, new List<int>(),
            out bool ok0, out string cert0, out List<(int Row, double Violation)> violated0);
        if (f0 == null) return null;
        if (ok0)
        {
            certified = true;
            certificate = cert0;
            return f0;
        }
        if (violated0 == null || violated0.Count == 0) return f0; // blocked by bounds/tension, not the cone

        // ---- Cone polish: alternating projection (POCS) between the
        // equality manifold (H-metric projection, one cached Cholesky of the
        // SAME dual matrix) and the per-vertex friction cone (closed-form
        // projection onto the SOC inscribed in the polyhedral cone, which
        // also zeroes fn- => the polished point is exactly cone-, bound- and
        // tension-feasible; only the equality residual needs the gate). ----
        double[] fp = PolishConeByAlternatingProjection(
            equilibrium, qp, invH0, colRows, f0, out bool okP, out string certP);
        if (fp != null && okP)
        {
            certified = true;
            certificate = certP;
            return fp;
        }
        return f0; // not certified; still the ADMM warm start
    }

    /// <summary>One LS-certificate round: equality-KKT solve with the listed
    /// friction-cone rows pinned to the cone boundary (appended equality rows
    /// at 0), complementarity projection, then full feasibility + zero-tension
    /// verification. Returns the projected point (null when the dual solve
    /// fails); <paramref name="certified"/> says whether it passed;
    /// <paramref name="violatedConeRows"/> lists the cone rows it violated.</summary>
    private static double[] SolveLsCertificateRound(
        EquilibriumSystem equilibrium, ConvexQpProblem qp,
        double[] invH, int[][] colRows, List<int> activeConeRows,
        out bool certified, out string certificate,
        out List<(int Row, double Violation)> violatedConeRows)
    {
        certified = false;
        certificate = null;
        violatedConeRows = null;
        int n = qp.VariableCount;
        int meq = qp.EqualityRowCount;
        var aeq = qp.EqualityMatrix;
        var beq = qp.EqualityRhs;
        var c = qp.LinearObjective;
        var aineq = qp.InequalityMatrix;
        int na = activeConeRows.Count;
        int mAug = meq + na;

        // ---- Augmented per-column row support: equality rows (from colRows)
        // plus the active cone rows (each Afr row touches one vertex's 4
        // columns). Augmented row r >= meq maps to aineq[activeConeRows[r-meq]]. ----
        var colRowsAug = colRows;
        if (na > 0)
        {
            colRowsAug = new int[n][];
            var extra = new List<int>[n];
            for (int j = 0; j < na; j++)
            {
                int src = activeConeRows[j];
                for (int k = 0; k < n; k++)
                {
                    if (aineq[src, k] == 0) continue;
                    if (extra[k] == null) extra[k] = new List<int>(4);
                    extra[k].Add(meq + j);
                }
            }
            for (int k = 0; k < n; k++)
            {
                if (extra[k] == null) { colRowsAug[k] = colRows[k]; continue; }
                var merged = new int[colRows[k].Length + extra[k].Count];
                Array.Copy(colRows[k], merged, colRows[k].Length);
                for (int t = 0; t < extra[k].Count; t++) merged[colRows[k].Length + t] = extra[k][t];
                colRowsAug[k] = merged;
            }
        }
        double A(int r, int k) => r < meq ? aeq[r, k] : aineq[activeConeRows[r - meq], k];

        // ---- Dual system M y = bAug + A H⁻¹ c, M = A H⁻¹ Aᵀ (mAug x mAug);
        // bAug = [beq; 0] (active cone rows sit exactly on the boundary). ----
        var mtx = new double[mAug, mAug];
        var rhsY = new double[mAug];
        for (int i = 0; i < meq; i++) rhsY[i] = beq[i];
        for (int k = 0; k < n; k++)
        {
            double w = invH[k];
            if (w == 0) continue;
            var rows = colRowsAug[k];
            for (int a = 0; a < rows.Length; a++)
            {
                int r1 = rows[a];
                double v1 = w * A(r1, k);
                rhsY[r1] += v1 * c[k];
                for (int b = a; b < rows.Length; b++)
                {
                    int r2 = rows[b];
                    mtx[r1, r2] += v1 * A(r2, k);
                }
            }
        }
        for (int i = 0; i < mAug; i++)
            for (int j = 0; j < i; j++)
            {
                // symmetrise (the accumulation above fills whichever triangle
                // the row order produced; merge both halves).
                double v = mtx[i, j] + mtx[j, i];
                mtx[i, j] = v; mtx[j, i] = v;
            }

        var y = CholeskySolve(mtx, rhsY, mAug);
        if (y == null)
        {
            // one regularised retry (rank dust from degenerate joints); the
            // acceptance below is gated on ACTUAL feasibility of f, so a
            // perturbed dual stays sound.
            double maxDiag = 0;
            for (int i = 0; i < mAug; i++) if (mtx[i, i] > maxDiag) maxDiag = mtx[i, i];
            if (maxDiag <= 0) return null;
            for (int i = 0; i < mAug; i++) mtx[i, i] += 1e-10 * maxDiag;
            y = CholeskySolve(mtx, rhsY, mAug);
            if (y == null) return null;
        }

        // ---- Primal recovery f = H⁻¹(Aᵀ y − c). ----
        var f = new double[n];
        for (int k = 0; k < n; k++)
        {
            if (invH[k] == 0) { f[k] = 0; continue; }
            double s = -c[k];
            var rows = colRowsAug[k];
            for (int a = 0; a < rows.Length; a++) s += A(rows[a], k) * y[rows[a]];
            f[k] = invH[k] * s;
        }

        // ---- Complementarity projection per normal pair (exactly preserves
        // Aeq f and Afr f; see class comment). ----
        var forceColumns = equilibrium.ForceColumns;
        for (int k = 0; k + 1 < n; k++)
        {
            if (forceColumns[k].Component != ForceComponent.NormalPositive) continue;
            var next = forceColumns[k + 1];
            if (next.Component != ForceComponent.NormalNegative ||
                next.InterfaceIndex != forceColumns[k].InterfaceIndex ||
                next.VertexIndex != forceColumns[k].VertexIndex) return null; // unexpected layout
            if (invH[k] == 0 || invH[k + 1] == 0) continue; // pinned pair stays 0
            double v = f[k] - f[k + 1];
            f[k] = v > 0 ? v : 0.0;
            f[k + 1] = v < 0 ? -v : 0.0;
        }

        // ---- Verification: the short-circuit is used ONLY when the point is
        // demonstrably feasible and tension-free; tolerances are far tighter
        // than the ADMM acceptance (1e-4 abs/rel). ----
        double scale = 1.0;
        for (int k = 0; k < n; k++) if (Math.Abs(f[k]) > scale) scale = Math.Abs(f[k]);
        for (int i = 0; i < meq; i++) if (Math.Abs(beq[i]) > scale) scale = Math.Abs(beq[i]);

        double eqRes = 0;
        for (int i = 0; i < meq; i++)
        {
            double s = -beq[i];
            for (int k = 0; k < n; k++) s += aeq[i, k] * f[k];
            if (Math.Abs(s) > eqRes) eqRes = Math.Abs(s);
        }
        bool feasible = eqRes <= 1e-6 * scale;

        if (feasible)
        {
            for (int k = 0; k < n && feasible; k++)
                if (f[k] < qp.LowerBounds[k] - 1e-9 * scale ||
                    f[k] > qp.UpperBounds[k] + 1e-9 * scale) feasible = false;
        }
        if (feasible && aineq != null)
        {
            int mineq = qp.InequalityRowCount;
            for (int i = 0; i < mineq; i++)
            {
                double s = -qp.InequalityRhs[i];
                for (int k = 0; k < n; k++) s += aineq[i, k] * f[k];
                if (s > 1e-7 * scale)
                {
                    feasible = false;
                    if (violatedConeRows == null) violatedConeRows = new List<(int, double)>();
                    violatedConeRows.Add((i, s));
                }
            }
            if (violatedConeRows != null) return f; // next round pins these
        }

        if (feasible)
        {
            double maxCompression = 0, maxTension = 0;
            for (int k = 0; k < n; k++)
            {
                switch (forceColumns[k].Component)
                {
                    case ForceComponent.Normal:
                    case ForceComponent.NormalPositive:
                        if (f[k] > maxCompression) maxCompression = f[k];
                        break;
                    case ForceComponent.NormalNegative:
                        if (f[k] > maxTension) maxTension = f[k];
                        break;
                }
            }
            if (maxTension <= 1e-3 * Math.Max(maxCompression, 1e-9))
            {
                certified = true;
                certificate = $"LS-first KKT certificate (KB-10): equality-feasible " +
                              $"(res {eqRes:0.###e0}), cone- and bound-feasible, tension-free " +
                              $"(m={meq}, n={n}, {na} cone rows pinned) — ADMM skipped.";
            }
        }
        return f;
    }

    /// <summary>
    /// Cone polish for the LS-first certificate (KB-10): alternating
    /// projection (POCS) between the equality manifold E = {f : Aeq f = beq}
    /// — projected in the H-metric, reusing ONE Cholesky factorisation of the
    /// dual matrix M = Aeq H⁻¹ Aeqᵀ — and the per-vertex friction cone C,
    /// projected in closed form onto the second-order cone with coefficient
    /// mu_eff·cos(pi/K), which is INSCRIBED in the K-face polyhedral cone
    /// (so membership is conservative), with fn- zeroed. The returned point
    /// is the last C-projection: exactly cone-, bound- and tension-feasible
    /// by construction — certification only needs its equality residual to
    /// pass the same 1e-6 gate as round 0. Soundness: any feasible
    /// tension-free point is an admissible state (lower-bound theorem);
    /// failure to converge merely declines the short-circuit.
    /// </summary>
    private static double[] PolishConeByAlternatingProjection(
        EquilibriumSystem equilibrium, ConvexQpProblem qp, double[] invH,
        int[][] colRows, double[] start, out bool certified, out string certificate)
    {
        certified = false;
        certificate = null;
        int n = qp.VariableCount;
        int meq = qp.EqualityRowCount;
        var aeq = qp.EqualityMatrix;
        var beq = qp.EqualityRhs;
        var aineq = qp.InequalityMatrix;
        if (aineq == null || equilibrium.ForceComponentsPerVertex != 4 || n % 4 != 0) return null;
        int vGroups = n / 4;
        int mineq = qp.InequalityRowCount;
        if (vGroups == 0 || mineq % vGroups != 0) return null;
        int faceK = mineq / vGroups;
        if (faceK < 3) return null;

        // Per-vertex effective mu from the cone rows (coefficient on fn+ is
        // -mu_eff by FrictionConeBuilder construction) and the inscribed-SOC
        // coefficient a = mu_eff cos(pi/K).
        double cosK = Math.Cos(Math.PI / faceK);
        var aSoc = new double[vGroups];
        for (int g = 0; g < vGroups; g++)
        {
            double mu = -aineq[g * faceK, g * 4];
            if (mu <= 0) return null;
            aSoc[g] = mu * cosK;
        }

        // ---- Factor M = Aeq H⁻¹ Aeqᵀ once (same accumulation as the KKT round). ----
        var mtx = new double[meq, meq];
        for (int k = 0; k < n; k++)
        {
            double w = invH[k];
            if (w == 0) continue;
            var rows = colRows[k];
            for (int a = 0; a < rows.Length; a++)
            {
                double v1 = w * aeq[rows[a], k];
                for (int b = a; b < rows.Length; b++)
                    mtx[rows[a], rows[b]] += v1 * aeq[rows[b], k];
            }
        }
        for (int i = 0; i < meq; i++)
            for (int j = 0; j < i; j++)
            { double v = mtx[i, j] + mtx[j, i]; mtx[i, j] = v; mtx[j, i] = v; }
        var chol = CholeskyFactor(mtx, meq);
        if (chol == null)
        {
            double maxDiag = 0;
            for (int i = 0; i < meq; i++) if (mtx[i, i] > maxDiag) maxDiag = mtx[i, i];
            if (maxDiag <= 0) return null;
            for (int i = 0; i < meq; i++) mtx[i, i] += 1e-10 * maxDiag;
            chol = CholeskyFactor(mtx, meq);
            if (chol == null) return null;
        }

        var f = (double[])start.Clone();
        var resid = new double[meq];
        var z = new double[meq];
        double scale = 1.0;
        for (int i = 0; i < meq; i++) if (Math.Abs(beq[i]) > scale) scale = Math.Abs(beq[i]);
        for (int k = 0; k < n; k++) if (Math.Abs(f[k]) > scale) scale = Math.Abs(f[k]);

        // Cap + stagnation cutoff: when E ∩ C is EMPTY (RBE-unstable assembly,
        // or a stable one whose only admissible states need the polyhedral
        // corners outside the inscribed SOC) the alternating projection
        // converges to a positive-gap cycle; eqRes then plateaus and the
        // cutoff hands control to the warm-started ADMM without burning the
        // full budget.
        const int MaxPocsIterations = 20000;
        const int StagnationStride = 250;
        double stagnationRef = double.MaxValue;
        for (int it = 1; it <= MaxPocsIterations; it++)
        {
            // ---- C-projection (per vertex; exact bound/tension feasibility). ----
            for (int g = 0; g < vGroups; g++)
            {
                int c0 = 4 * g;
                bool nPinned = invH[c0] == 0 || invH[c0 + 1] == 0;
                bool tPinned = invH[c0 + 2] == 0 || invH[c0 + 3] == 0;
                if (nPinned) { f[c0] = 0; f[c0 + 1] = 0; f[c0 + 2] = 0; f[c0 + 3] = 0; continue; }
                double v = f[c0] - f[c0 + 1];
                if (tPinned)
                {
                    f[c0 + 2] = 0; f[c0 + 3] = 0;
                    f[c0] = v > 0 ? v : 0.0; f[c0 + 1] = 0;
                    continue;
                }
                double t1 = f[c0 + 2], t2 = f[c0 + 3];
                double zt = Math.Sqrt(t1 * t1 + t2 * t2);
                double a = aSoc[g];
                if (zt <= a * v)
                { f[c0] = v; f[c0 + 1] = 0; }                       // inside (v >= 0 here)
                else if (a * zt <= -v)
                { f[c0] = 0; f[c0 + 1] = 0; f[c0 + 2] = 0; f[c0 + 3] = 0; } // polar cone -> origin
                else
                {
                    double t = (a * zt + v) / (a * a + 1.0);
                    double sc = zt > 1e-300 ? a * t / zt : 0.0;
                    f[c0] = t; f[c0 + 1] = 0;
                    f[c0 + 2] = t1 * sc; f[c0 + 3] = t2 * sc;
                }
            }

            // ---- Equality residual of the (feasible-in-C) point. ----
            for (int i = 0; i < meq; i++) resid[i] = -beq[i];
            for (int k = 0; k < n; k++)
            {
                double fk = f[k];
                if (fk == 0) continue;
                var rows = colRows[k];
                for (int a = 0; a < rows.Length; a++) resid[rows[a]] += aeq[rows[a], k] * fk;
            }
            double eqRes = 0;
            for (int i = 0; i < meq; i++) if (Math.Abs(resid[i]) > eqRes) eqRes = Math.Abs(resid[i]);
            if (it % StagnationStride == 0)
            {
                if (eqRes > 0.95 * stagnationRef) return null; // gap cycle -> decline
                stagnationRef = eqRes;
            }

            if (eqRes <= 1e-6 * scale)
            {
                // exact final check against the POLYHEDRAL rows (4 non-zeros each)
                double coneViol = 0;
                for (int g = 0; g < vGroups; g++)
                {
                    int c0 = 4 * g;
                    for (int r = g * faceK; r < (g + 1) * faceK; r++)
                    {
                        double s = aineq[r, c0] * f[c0] + aineq[r, c0 + 1] * f[c0 + 1]
                                 + aineq[r, c0 + 2] * f[c0 + 2] + aineq[r, c0 + 3] * f[c0 + 3]
                                 - qp.InequalityRhs[r];
                        if (s > coneViol) coneViol = s;
                    }
                }
                double maxCompression = 0;
                for (int k = 0; k < n; k += 4) if (f[k] > maxCompression) maxCompression = f[k];
                if (coneViol <= 1e-7 * scale && maxCompression > 0)
                {
                    certified = true;
                    certificate = $"LS-first cone-polish certificate (KB-10): POCS converged in {it} " +
                                  $"iterations (eq res {eqRes:0.###e0}, cone viol {coneViol:0.###e0}, " +
                                  $"tension-free by construction, m={meq}, n={n}) — ADMM skipped.";
                    return f;
                }
            }

            // ---- E-projection in the H-metric (cached factor). ----
            CholeskySolveFactored(chol, resid, z, meq);
            for (int k = 0; k < n; k++)
            {
                double w = invH[k];
                if (w == 0) continue;
                double s = 0;
                var rows = colRows[k];
                for (int a = 0; a < rows.Length; a++) s += aeq[rows[a], k] * z[rows[a]];
                f[k] -= w * s;
            }
        }
        return null; // no certificate; caller falls back to the warm-started ADMM
    }

    /// <summary>½ fᵀHf + cᵀf for the diagonal-Hessian QPs this checker builds.</summary>
    private static double DiagonalQpObjective(ConvexQpProblem qp, double[] f)
    {
        double obj = 0;
        for (int k = 0; k < f.Length; k++)
            obj += 0.5 * qp.Hessian[k, k] * f[k] * f[k] + qp.LinearObjective[k] * f[k];
        return obj;
    }

    /// <summary>Dense SPD solve M x = rhs by Cholesky; null when M is not SPD.</summary>
    private static double[] CholeskySolve(double[,] m, double[] rhs, int n)
    {
        var l = CholeskyFactor(m, n);
        if (l == null) return null;
        var x = new double[n];
        CholeskySolveFactored(l, rhs, x, n);
        return x;
    }

    /// <summary>Lower-triangular Cholesky factor of SPD M; null when not SPD.</summary>
    private static double[,] CholeskyFactor(double[,] m, int n)
    {
        var l = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k <= i; k++)
            {
                double sum = m[i, k];
                for (int t = 0; t < k; t++) sum -= l[i, t] * l[k, t];
                if (i == k)
                {
                    if (sum <= 0) return null;
                    l[i, i] = Math.Sqrt(sum);
                }
                else l[i, k] = sum / l[k, k];
            }
        }
        return l;
    }

    /// <summary>Solve L Lᵀ x = rhs given the Cholesky factor L.</summary>
    private static void CholeskySolveFactored(double[,] l, double[] rhs, double[] x, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double sum = rhs[i];
            for (int t = 0; t < i; t++) sum -= l[i, t] * x[t];
            x[i] = sum / l[i, i];
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = x[i];
            for (int t = i + 1; t < n; t++) sum -= l[t, i] * x[t];
            x[i] = sum / l[i, i];
        }
    }
}
