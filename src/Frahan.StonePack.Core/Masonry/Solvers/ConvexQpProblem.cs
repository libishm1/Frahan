#nullable disable
using System;

namespace Frahan.Masonry.Solvers;

// =============================================================================
// ConvexQpProblem — solver-agnostic problem statement for IConvexQpSolver.
//
// All matrices are dense double[,] for simplicity at this stage; sparse
// variants can be added later when a managed sparse-aware solver is plugged
// in (Phase C). Densification is fine for the typical Frahan masonry sizes
// (a few hundred contact vertices => a few thousand columns).
//
// Source / inspiration: Kao 2022, BlockResearchGroup/compas_cra (MIT).
// =============================================================================

/// <summary>
/// QP problem statement, decoupled from the masonry-specific layout. All
/// matrices are dense double[,] for simplicity at this stage; sparse
/// variants can be added when a managed sparse-aware solver is plugged in.
/// </summary>
public sealed class ConvexQpProblem
{
    /// <summary>
    /// Construct a convex QP. All array arguments are kept by reference; the
    /// caller must not mutate them after handing them to the solver. Pass
    /// <c>null</c> for any unused block (e.g. no inequality constraints =>
    /// <paramref name="inequalityMatrix"/> and <paramref name="inequalityRhs"/>
    /// both null). Bounds default to the unbounded interval (-inf, +inf) when
    /// the corresponding array argument is null.
    /// </summary>
    /// <param name="variableCount">Number of decision variables n.</param>
    /// <param name="hessian">Symmetric n x n Hessian; may be the zero matrix for a pure LP.</param>
    /// <param name="linearObjective">Length-n linear-cost vector c; may be the zero vector for a pure quadratic.</param>
    /// <param name="equalityMatrix">[meq, n] equality matrix Aeq, or null for no equalities.</param>
    /// <param name="equalityRhs">[meq] equality right-hand side beq, or null for no equalities.</param>
    /// <param name="inequalityMatrix">[mineq, n] inequality matrix Aineq with Aineq x &lt;= bineq, or null for no inequalities.</param>
    /// <param name="inequalityRhs">[mineq] inequality right-hand side bineq, or null for no inequalities.</param>
    /// <param name="lowerBounds">[n] elementwise lower bound on x, or null (defaults to -infinity).</param>
    /// <param name="upperBounds">[n] elementwise upper bound on x, or null (defaults to +infinity).</param>
    public ConvexQpProblem(
        int variableCount,
        double[,] hessian,
        double[]  linearObjective,
        double[,] equalityMatrix,
        double[]  equalityRhs,
        double[,] inequalityMatrix,
        double[]  inequalityRhs,
        double[]  lowerBounds,
        double[]  upperBounds)
    {
        if (variableCount < 0)
            throw new ArgumentOutOfRangeException(nameof(variableCount), $"variableCount must be >= 0, got {variableCount}.");

        // ---- Hessian: required, must be n x n. ----
        if (hessian == null)
            throw new ArgumentNullException(nameof(hessian), "Hessian is required (use a zero matrix for LP-style problems).");
        if (hessian.GetLength(0) != variableCount || hessian.GetLength(1) != variableCount)
            throw new ArgumentException(
                $"hessian is [{hessian.GetLength(0)}, {hessian.GetLength(1)}], expected [{variableCount}, {variableCount}].",
                nameof(hessian));

        // ---- Linear objective: required, length n. ----
        if (linearObjective == null)
            throw new ArgumentNullException(nameof(linearObjective), "linearObjective is required (use a zero vector for pure-quadratic problems).");
        if (linearObjective.Length != variableCount)
            throw new ArgumentException(
                $"linearObjective length {linearObjective.Length} != variableCount {variableCount}.",
                nameof(linearObjective));

        // ---- Equality block: both null or both non-null. ----
        int meq;
        if (equalityMatrix == null && equalityRhs == null)
        {
            meq = 0;
        }
        else if (equalityMatrix == null || equalityRhs == null)
        {
            throw new ArgumentException(
                "equalityMatrix and equalityRhs must both be null or both be non-null.",
                nameof(equalityMatrix));
        }
        else
        {
            meq = equalityMatrix.GetLength(0);
            if (equalityMatrix.GetLength(1) != variableCount)
                throw new ArgumentException(
                    $"equalityMatrix has {equalityMatrix.GetLength(1)} columns, expected {variableCount}.",
                    nameof(equalityMatrix));
            if (equalityRhs.Length != meq)
                throw new ArgumentException(
                    $"equalityRhs length {equalityRhs.Length} != equalityMatrix row count {meq}.",
                    nameof(equalityRhs));
        }

        // ---- Inequality block: both null or both non-null. ----
        int mineq;
        if (inequalityMatrix == null && inequalityRhs == null)
        {
            mineq = 0;
        }
        else if (inequalityMatrix == null || inequalityRhs == null)
        {
            throw new ArgumentException(
                "inequalityMatrix and inequalityRhs must both be null or both be non-null.",
                nameof(inequalityMatrix));
        }
        else
        {
            mineq = inequalityMatrix.GetLength(0);
            if (inequalityMatrix.GetLength(1) != variableCount)
                throw new ArgumentException(
                    $"inequalityMatrix has {inequalityMatrix.GetLength(1)} columns, expected {variableCount}.",
                    nameof(inequalityMatrix));
            if (inequalityRhs.Length != mineq)
                throw new ArgumentException(
                    $"inequalityRhs length {inequalityRhs.Length} != inequalityMatrix row count {mineq}.",
                    nameof(inequalityRhs));
        }

        // ---- Bounds: optional; null -> unbounded. Length must equal n if supplied. ----
        if (lowerBounds == null)
        {
            lowerBounds = new double[variableCount];
            for (int i = 0; i < variableCount; i++) lowerBounds[i] = double.NegativeInfinity;
        }
        else if (lowerBounds.Length != variableCount)
        {
            throw new ArgumentException(
                $"lowerBounds length {lowerBounds.Length} != variableCount {variableCount}.",
                nameof(lowerBounds));
        }

        if (upperBounds == null)
        {
            upperBounds = new double[variableCount];
            for (int i = 0; i < variableCount; i++) upperBounds[i] = double.PositiveInfinity;
        }
        else if (upperBounds.Length != variableCount)
        {
            throw new ArgumentException(
                $"upperBounds length {upperBounds.Length} != variableCount {variableCount}.",
                nameof(upperBounds));
        }

        VariableCount = variableCount;
        Hessian = hessian;
        LinearObjective = linearObjective;
        EqualityMatrix = equalityMatrix;
        EqualityRhs = equalityRhs;
        InequalityMatrix = inequalityMatrix;
        InequalityRhs = inequalityRhs;
        LowerBounds = lowerBounds;
        UpperBounds = upperBounds;
    }

    /// <summary>
    /// SPARSE construction (2026-07-02): diagonal Hessian + COO constraint
    /// blocks, NO dense intermediates. Built by RbeQpFormulation above the
    /// dense-size gate (the Güell portico OOMed in ToDense at 822 interfaces).
    /// Dense properties (Hessian / EqualityMatrix / InequalityMatrix) are NULL
    /// on this path — only AdmmQpSolver's sparse/CG route consumes these
    /// problems; other solvers must check for null and decline.
    /// </summary>
    public ConvexQpProblem(
        double[] hessianDiagonal,
        double[] linearObjective,
        Frahan.Masonry.Equilibrium.SparseMatrixCoo equalitySparse,
        double[] equalityRhs,
        Frahan.Masonry.Equilibrium.SparseMatrixCoo inequalitySparse,
        double[] inequalityRhs,
        double[] lowerBounds,
        double[] upperBounds)
    {
        if (hessianDiagonal == null) throw new ArgumentNullException(nameof(hessianDiagonal));
        int n = hessianDiagonal.Length;
        if (linearObjective == null || linearObjective.Length != n)
            throw new ArgumentException("linearObjective must be length n.", nameof(linearObjective));
        if (equalitySparse != null && equalitySparse.ColCount != n)
            throw new ArgumentException("equalitySparse column count != n.", nameof(equalitySparse));
        if (equalitySparse != null && (equalityRhs == null || equalityRhs.Length != equalitySparse.RowCount))
            throw new ArgumentException("equalityRhs length != equalitySparse rows.", nameof(equalityRhs));
        if (inequalitySparse != null && inequalitySparse.ColCount != n)
            throw new ArgumentException("inequalitySparse column count != n.", nameof(inequalitySparse));
        if (inequalitySparse != null && (inequalityRhs == null || inequalityRhs.Length != inequalitySparse.RowCount))
            throw new ArgumentException("inequalityRhs length != inequalitySparse rows.", nameof(inequalityRhs));

        if (lowerBounds == null)
        {
            lowerBounds = new double[n];
            for (int i = 0; i < n; i++) lowerBounds[i] = double.NegativeInfinity;
        }
        if (upperBounds == null)
        {
            upperBounds = new double[n];
            for (int i = 0; i < n; i++) upperBounds[i] = double.PositiveInfinity;
        }

        VariableCount = n;
        Hessian = null;
        HessianDiagonal = hessianDiagonal;
        LinearObjective = linearObjective;
        EqualitySparse = equalitySparse;
        EqualityRhs = equalityRhs;
        InequalitySparse = inequalitySparse;
        InequalityRhs = inequalityRhs;
        LowerBounds = lowerBounds;
        UpperBounds = upperBounds;
    }

    /// <summary>Diagonal Hessian (sparse path); null on the dense path.</summary>
    public double[] HessianDiagonal { get; }

    /// <summary>COO equality block (sparse path); null on the dense path.</summary>
    public Frahan.Masonry.Equilibrium.SparseMatrixCoo EqualitySparse { get; }

    /// <summary>COO inequality block (sparse path); null on the dense path.</summary>
    public Frahan.Masonry.Equilibrium.SparseMatrixCoo InequalitySparse { get; }

    /// <summary>Number of decision variables n.</summary>
    public int VariableCount { get; }

    /// <summary>Symmetric n x n Hessian H. Always non-null; may be the zero matrix.</summary>
    public double[,] Hessian { get; }

    /// <summary>Length-n linear-cost vector c. Always non-null; may be the zero vector.</summary>
    public double[] LinearObjective { get; }

    /// <summary>[meq, n] equality matrix Aeq, or null when there are no equality constraints.</summary>
    public double[,] EqualityMatrix { get; }

    /// <summary>[meq] equality right-hand side beq, or null when there are no equality constraints.</summary>
    public double[] EqualityRhs { get; }

    /// <summary>[mineq, n] inequality matrix Aineq (Aineq x &lt;= bineq), or null when there are no inequality constraints.</summary>
    public double[,] InequalityMatrix { get; }

    /// <summary>[mineq] inequality right-hand side bineq, or null when there are no inequality constraints.</summary>
    public double[] InequalityRhs { get; }

    /// <summary>Length-n elementwise lower bound on x. Always non-null; entries default to <see cref="double.NegativeInfinity"/>.</summary>
    public double[] LowerBounds { get; }

    /// <summary>Length-n elementwise upper bound on x. Always non-null; entries default to <see cref="double.PositiveInfinity"/>.</summary>
    public double[] UpperBounds { get; }

    /// <summary>Number of equality rows, or 0 when <see cref="EqualityMatrix"/> is null.</summary>
    public int EqualityRowCount => EqualityMatrix == null ? 0 : EqualityMatrix.GetLength(0);

    /// <summary>Number of inequality rows, or 0 when <see cref="InequalityMatrix"/> is null.</summary>
    public int InequalityRowCount => InequalityMatrix == null ? 0 : InequalityMatrix.GetLength(0);

    public override string ToString() =>
        $"ConvexQpProblem(n={VariableCount}, meq={EqualityRowCount}, mineq={InequalityRowCount})";
}
