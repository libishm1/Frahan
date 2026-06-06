#nullable disable
using System;

namespace Frahan.Masonry.Solvers;

// =============================================================================
// DenseLinAlg — minimal pure-managed dense linear algebra. Just enough to
// support ManagedQpSolver's projection-onto-equality-manifold step:
// Cholesky factorisation + forward/back substitution + matrix-vector products.
//
// Net48 / netstandard2.0 friendly. No NuGet dependencies; no Math.NET.
// Sized for small dense problems (<= a few hundred rows/columns), which is
// the regime the masonry RBE problem lives in.
// =============================================================================

internal static class DenseLinAlg
{
    /// <summary>
    /// In-place Cholesky factorisation. Replaces <paramref name="A"/> with its
    /// lower-triangular Cholesky factor L (in the lower triangle, including
    /// diagonal). Upper triangle is untouched (treat as scratch).
    /// </summary>
    /// <returns>true on success; false if the matrix is not numerically positive definite.</returns>
    public static bool CholeskyInPlace(double[,] A, double regularization = 0.0)
    {
        int n = A.GetLength(0);
        if (A.GetLength(1) != n)
            throw new ArgumentException("A must be square", nameof(A));

        if (regularization > 0.0)
            for (int i = 0; i < n; i++)
                A[i, i] += regularization;

        for (int j = 0; j < n; j++)
        {
            double sum = A[j, j];
            for (int k = 0; k < j; k++) sum -= A[j, k] * A[j, k];
            if (sum <= 0.0) return false;
            double Ljj = Math.Sqrt(sum);
            A[j, j] = Ljj;

            for (int i = j + 1; i < n; i++)
            {
                double s = A[i, j];
                for (int k = 0; k < j; k++) s -= A[i, k] * A[j, k];
                A[i, j] = s / Ljj;
            }
        }
        return true;
    }

    /// <summary>
    /// Solve L y = b via forward substitution, where L is lower-triangular
    /// and stored in the lower triangle of <paramref name="L"/> (output of
    /// <see cref="CholeskyInPlace"/>). Result written into <paramref name="y"/>.
    /// </summary>
    public static void ForwardSubstitution(double[,] L, double[] b, double[] y)
    {
        int n = L.GetLength(0);
        for (int i = 0; i < n; i++)
        {
            double s = b[i];
            for (int j = 0; j < i; j++) s -= L[i, j] * y[j];
            y[i] = s / L[i, i];
        }
    }

    /// <summary>
    /// Solve L^T x = y via back substitution. <paramref name="L"/> stores L
    /// in its lower triangle.
    /// </summary>
    public static void BackSubstitutionTranspose(double[,] L, double[] y, double[] x)
    {
        int n = L.GetLength(0);
        for (int i = n - 1; i >= 0; i--)
        {
            double s = y[i];
            for (int j = i + 1; j < n; j++) s -= L[j, i] * x[j];
            x[i] = s / L[i, i];
        }
    }

    /// <summary>
    /// Solve A x = b via Cholesky (chained forward + back-substitution). A is
    /// modified in place (replaced by its Cholesky factor). Returns true if
    /// the system was solved; false if A was not positive-definite.
    /// </summary>
    public static bool CholeskySolve(double[,] A, double[] b, double[] x, double regularization = 0.0)
    {
        if (!CholeskyInPlace(A, regularization)) return false;
        var y = new double[b.Length];
        ForwardSubstitution(A, b, y);
        BackSubstitutionTranspose(A, y, x);
        return true;
    }

    /// <summary>y = A x for dense [m x n] A.</summary>
    public static double[] MatVec(double[,] A, double[] x)
    {
        int m = A.GetLength(0);
        int n = A.GetLength(1);
        if (x.Length != n)
            throw new ArgumentException($"x length {x.Length} != A.cols {n}", nameof(x));
        var y = new double[m];
        for (int i = 0; i < m; i++)
        {
            double s = 0.0;
            for (int j = 0; j < n; j++) s += A[i, j] * x[j];
            y[i] = s;
        }
        return y;
    }

    /// <summary>y = A^T x for dense [m x n] A (so y has length n).</summary>
    public static double[] MatTVec(double[,] A, double[] x)
    {
        int m = A.GetLength(0);
        int n = A.GetLength(1);
        if (x.Length != m)
            throw new ArgumentException($"x length {x.Length} != A.rows {m}", nameof(x));
        var y = new double[n];
        for (int j = 0; j < n; j++)
        {
            double s = 0.0;
            for (int i = 0; i < m; i++) s += A[i, j] * x[i];
            y[j] = s;
        }
        return y;
    }

    /// <summary>K = A A^T for dense [m x n] A. K is m x m, symmetric.</summary>
    public static double[,] AAt(double[,] A)
    {
        int m = A.GetLength(0);
        int n = A.GetLength(1);
        var K = new double[m, m];
        for (int i = 0; i < m; i++)
        {
            for (int j = i; j < m; j++)
            {
                double s = 0.0;
                for (int k = 0; k < n; k++) s += A[i, k] * A[j, k];
                K[i, j] = s;
                if (i != j) K[j, i] = s;
            }
        }
        return K;
    }

    /// <summary>Element-wise vector subtraction (returns a fresh array).</summary>
    public static double[] Sub(double[] a, double[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("length mismatch");
        var r = new double[a.Length];
        for (int i = 0; i < a.Length; i++) r[i] = a[i] - b[i];
        return r;
    }

    /// <summary>L2 norm of <paramref name="v"/>.</summary>
    public static double Norm2(double[] v)
    {
        double s = 0.0;
        for (int i = 0; i < v.Length; i++) s += v[i] * v[i];
        return Math.Sqrt(s);
    }
}
