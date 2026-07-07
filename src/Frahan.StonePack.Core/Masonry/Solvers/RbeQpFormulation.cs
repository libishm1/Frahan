#nullable disable
using System;
using Frahan.Masonry.Equilibrium;

namespace Frahan.Masonry.Solvers;

// =============================================================================
// RbeQpFormulation — maps an EquilibriumSystem (and an optional friction-cone
// matrix) onto the generic ConvexQpProblem the IConvexQpSolver consumes.
//
// Source / inspiration: Kao 2022 (CAD vol 146, art 103216) and
// BlockResearchGroup/compas_cra (MIT). Per Kao 2022 section 4 the Rigid-Block
// Equilibrium (RBE) formulation is a convex QP:
//
//     min   ½ f̃^T H f̃                   (objective)
//     s.t.  Aeq f̃ = -b                   (force + moment balance per free block)
//           Afr f̃ <= 0                   (linearised friction cone, rows <= 0)
//           f_n >= 0                     (compressive-only normal forces)
//
// The Hessian H is diagonal with separate normal vs tangential weights —
// hessianScale on normal columns and hessianScale * tangentialScale on
// tangent columns. Default tangentialScale = 1.0 recovers identity (the
// minimum-norm contact-force solution); the Kao 2022 §5 hint of ~1e3 makes
// the solver prefer normal-dominated contact distributions. Callers pick
// the scale they need at Build() time.
//
// Note: this class only constructs the problem statement. Selecting and
// running a solver (ManagedQpSolver / IpoptNlpSolver) and decoding the result
// vector back into per-vertex contact forces are separate tasks.
// =============================================================================

/// <summary>
/// Maps an <see cref="EquilibriumSystem"/> (and an optional friction-cone
/// matrix) to the generic <see cref="ConvexQpProblem"/> the
/// <see cref="IConvexQpSolver"/> consumes.
///
/// RBE objective per Kao 2022 §5: minimise ½ f̃^T H f̃ where H is the
/// diagonal Hessian with separate normal vs tangential weights, exposed
/// via the hessianScale and tangentialScale parameters of Build().
/// </summary>
public static class RbeQpFormulation
{
    /// <summary>
    /// Build the QP from an <see cref="EquilibriumSystem"/>.
    /// </summary>
    /// <param name="equilibrium">From <see cref="EquilibriumMatrixBuilder.Build"/>.</param>
    /// <param name="frictionAfr">
    ///     Sparse friction-cone inequality (rows &lt;= 0). Must have the same
    ///     column count as <c>equilibrium.Aeq</c>. Pass <c>null</c> to omit
    ///     friction (useful for pure-equilibrium probes).
    /// </param>
    /// <param name="hessianScale">Diagonal Hessian value used for ½ f^T (hessianScale * I) f.
    ///     Default 1.0 — produces minimum-norm contact-force solutions.</param>
    [Obsolete("Legacy RHS sign convention: with the equilibrium builder's b (gravity " +
              "negative) this produces f_n = -m*g against lowerBounds = 0, an INFEASIBLE " +
              "QP for any real assembly (risk register M2). Use BuildPhysicsCorrected. " +
              "Kept only for tests that pin the legacy sign expectations.")]
    public static ConvexQpProblem Build(
        EquilibriumSystem equilibrium,
        SparseMatrixCoo frictionAfr,
        double hessianScale = 1.0,
        double tangentialScale = 1.0,
        double negativeNormalScale = 1.0)
    {
        if (equilibrium == null) throw new ArgumentNullException(nameof(equilibrium));
        if (hessianScale <= 0.0)
            throw new ArgumentOutOfRangeException(
                nameof(hessianScale),
                $"hessianScale must be > 0 to keep the QP convex; got {hessianScale}.");
        if (tangentialScale <= 0.0)
            throw new ArgumentOutOfRangeException(
                nameof(tangentialScale),
                $"tangentialScale must be > 0 to keep the QP convex; got {tangentialScale}.");
        if (negativeNormalScale <= 0.0)
            throw new ArgumentOutOfRangeException(
                nameof(negativeNormalScale),
                $"negativeNormalScale must be > 0 to keep the QP convex; got {negativeNormalScale}.");

        int n = equilibrium.Aeq.ColCount;
        int meq = equilibrium.Aeq.RowCount;

        // ---- Hessian: diagonal with separate normal vs tangential weights. ----
        // Per Kao 2022 §5 the published Hessian penalises tangential force
        // components more heavily than normal — the structure here is correct
        // for that family. tangentialScale = 1.0 reproduces the original I*c
        // behaviour; tangentialScale > 1.0 (paper hint: ~1e3) makes the
        // solver prefer normal-dominated contact force distributions.
        var hessDiag = new double[n];
        for (int i = 0; i < n; i++)
        {
            ForceComponent component = equilibrium.ForceColumns[i].Component;
            double diag;
            switch (component)
            {
                case ForceComponent.Normal:
                case ForceComponent.NormalPositive:
                    diag = hessianScale;
                    break;
                case ForceComponent.NormalNegative:
                    // Kao 2022 Eq. 14 penalty weight gamma: a large value makes
                    // tensile normal forces expensive, so ||f_n-|| localises and
                    // measures instability. Default 1.0 preserves the legacy
                    // identity-Hessian behaviour pinned by existing tests.
                    diag = hessianScale * negativeNormalScale;
                    break;
                case ForceComponent.Tangent1:
                case ForceComponent.Tangent2:
                    diag = hessianScale * tangentialScale;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unhandled ForceComponent {component} at column {i}.");
            }
            hessDiag[i] = diag;
        }

        // ---- Linear objective: zero vector (pure quadratic). ----
        var linearObjective = new double[n];

        // ---- Equality rhs: Aeq f = -b  (EquilibriumSystem stores Aeq f + b = 0). ----
        var equalityRhs = new double[meq];
        for (int i = 0; i < meq; i++)
        {
            equalityRhs[i] = -equilibrium.B[i];
        }

        if (frictionAfr != null && frictionAfr.ColCount != n)
            throw new ArgumentException(
                $"frictionAfr column count {frictionAfr.ColCount} != equilibrium.Aeq column count {n}.",
                nameof(frictionAfr));
        int mfr = frictionAfr != null ? frictionAfr.RowCount : 0;

        // ---- Dense/sparse gate (2026-07-02). Densifying past ~1e7 cells OOMs
        // (Güell portico: 822 interfaces = OutOfMemory in ToDense; even 572 ran
        // ~20 min in the dense Cholesky). Above the gate the COO blocks go
        // STRAIGHT to the sparse/CG ADMM path — no dense intermediate exists.
        long denseCells = (long)n * n + (long)meq * n + (long)mfr * n;
        bool sparse = denseCells > 12_000_000;

        double[,] hessian = null, equalityMatrix = null, inequalityMatrix = null;
        double[] inequalityRhs = mfr > 0 ? new double[mfr] : null; // zero rhs by construction
        if (!sparse)
        {
            hessian = new double[n, n];
            for (int i = 0; i < n; i++) hessian[i, i] = hessDiag[i];
            equalityMatrix = equilibrium.Aeq.ToDense();
            if (frictionAfr != null) inequalityMatrix = frictionAfr.ToDense();
        }

        // ---- Box bounds: normal-force columns >= 0; tangents unbounded. ----
        var lowerBounds = new double[n];
        var upperBounds = new double[n];
        for (int k = 0; k < n; k++)
        {
            ForceComponent component = equilibrium.ForceColumns[k].Component;
            switch (component)
            {
                case ForceComponent.Normal:
                case ForceComponent.NormalPositive:
                case ForceComponent.NormalNegative:
                    // Compressive only: f_n >= 0. The penalty form keeps both
                    // halves >= 0 individually; complementarity (f_n+ * f_n- = 0)
                    // is enforced by the objective penalty, not by these bounds.
                    lowerBounds[k] = 0.0;
                    upperBounds[k] = double.PositiveInfinity;
                    break;
                case ForceComponent.Tangent1:
                case ForceComponent.Tangent2:
                    lowerBounds[k] = double.NegativeInfinity;
                    upperBounds[k] = double.PositiveInfinity;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unhandled ForceComponent {component} at column {k}.");
            }
        }

        if (sparse)
            return new ConvexQpProblem(
                hessianDiagonal: hessDiag,
                linearObjective: linearObjective,
                equalitySparse: equilibrium.Aeq,
                equalityRhs: equalityRhs,
                inequalitySparse: frictionAfr,
                inequalityRhs: inequalityRhs,
                lowerBounds: lowerBounds,
                upperBounds: upperBounds);

        return new ConvexQpProblem(
            variableCount: n,
            hessian: hessian,
            linearObjective: linearObjective,
            equalityMatrix: equalityMatrix,
            equalityRhs: equalityRhs,
            inequalityMatrix: inequalityMatrix,
            inequalityRhs: inequalityRhs,
            lowerBounds: lowerBounds,
            upperBounds: upperBounds);
    }

    /// <summary>
    /// Sign-corrected variant of <see cref="Build"/> that produces a QP whose
    /// closed-form solution has positive normal forces under the existing
    /// equilibrium-builder conventions.
    /// </summary>
    /// <remarks>
    /// The existing pipeline has a long-standing inconsistency: the equilibrium
    /// builder writes <c>b[F_z] = -m * g_abs</c> (gravity Z negative, so weightZ
    /// is negative); RbeQpFormulation.Build then sets <c>equalityRhs = -b</c>.
    /// Combined with <c>Aeq[F_z][f_n_col] = -1</c> on free blocks (B sees -1),
    /// the constraint <c>Aeq f = beq</c> becomes <c>-f_n = +m*g_abs</c>, i.e.
    /// <c>f_n = -m*g_abs</c> (negative for compression). The
    /// <c>lowerBounds[k] = 0</c> entry then makes the QP infeasible for any
    /// real masonry assembly. The bug was masked because no end-to-end test
    /// had ever solved the pipeline.
    ///
    /// This variant flips equalityRhs so that <c>f_n &gt;= 0</c> means
    /// compression in the same convention used by the existing equilibrium
    /// builder. It is used by <c>StageBSolverTests.EndToEnd_*</c> and is the
    /// recommended entry point for anyone solving a masonry RBE QP today.
    /// The original <see cref="Build"/> stays in place for backward compat with
    /// existing tests that pin specific Aeq / b sign expectations; those will
    /// be reconciled in a follow-up cleanup pass.
    /// </remarks>
    public static ConvexQpProblem BuildPhysicsCorrected(
        EquilibriumSystem equilibrium,
        SparseMatrixCoo frictionAfr,
        double hessianScale = 1.0,
        double tangentialScale = 1.0,
        double negativeNormalScale = 1.0)
    {
#pragma warning disable CS0618 // intentional: the corrected variant wraps the legacy Build and flips its rhs
        var qp = Build(equilibrium, frictionAfr, hessianScale, tangentialScale, negativeNormalScale);
#pragma warning restore CS0618
        var newRhs = new double[qp.EqualityRhs.Length];
        for (int i = 0; i < newRhs.Length; i++) newRhs[i] = -qp.EqualityRhs[i];
        if (qp.Hessian == null) // SPARSE-built (size gate): re-wrap with the flipped rhs
            return new ConvexQpProblem(
                hessianDiagonal: qp.HessianDiagonal,
                linearObjective: qp.LinearObjective,
                equalitySparse: qp.EqualitySparse,
                equalityRhs: newRhs,
                inequalitySparse: qp.InequalitySparse,
                inequalityRhs: qp.InequalityRhs,
                lowerBounds: qp.LowerBounds,
                upperBounds: qp.UpperBounds);
        return new ConvexQpProblem(
            variableCount: qp.VariableCount,
            hessian: qp.Hessian,
            linearObjective: qp.LinearObjective,
            equalityMatrix: qp.EqualityMatrix,
            equalityRhs: newRhs,
            inequalityMatrix: qp.InequalityMatrix,
            inequalityRhs: qp.InequalityRhs,
            lowerBounds: qp.LowerBounds,
            upperBounds: qp.UpperBounds);
    }
}
