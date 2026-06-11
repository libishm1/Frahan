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
        var solver = new AdmmQpSolver(epsAbs: 1e-4, epsRel: 1e-4);
        var sol = solver.Solve(qp);

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
}
