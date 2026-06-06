#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Equilibrium;

/// <summary>
/// Coordinate-list (COO) sparse matrix builder. Stores triples
/// <c>(row, col, value)</c> in append order; consumers either iterate
/// the triples directly or densify to a row-major <c>double[,]</c>.
///
/// Net48 / netstandard2.0 friendly. No dependencies on Math.NET / Accord.
/// Used by <see cref="EquilibriumMatrixBuilder"/> to assemble Aeq.
/// </summary>
public sealed class SparseMatrixCoo
{
    private readonly List<int> _rows = new List<int>();
    private readonly List<int> _cols = new List<int>();
    private readonly List<double> _values = new List<double>();

    public SparseMatrixCoo(int rowCount, int colCount)
    {
        if (rowCount < 0) throw new ArgumentOutOfRangeException(nameof(rowCount));
        if (colCount < 0) throw new ArgumentOutOfRangeException(nameof(colCount));
        RowCount = rowCount;
        ColCount = colCount;
    }

    public int RowCount { get; }
    public int ColCount { get; }
    public int NonZeroCount => _values.Count;

    public IReadOnlyList<int> RowIndices => _rows;
    public IReadOnlyList<int> ColIndices => _cols;
    public IReadOnlyList<double> Values => _values;

    /// <summary>
    /// Append one triple. Duplicate <c>(row, col)</c> entries are summed
    /// only when the matrix is densified — at the COO stage they coexist.
    /// </summary>
    public void Add(int row, int col, double value)
    {
        if (row < 0 || row >= RowCount)
            throw new ArgumentOutOfRangeException(nameof(row), $"row {row} out of [0, {RowCount})");
        if (col < 0 || col >= ColCount)
            throw new ArgumentOutOfRangeException(nameof(col), $"col {col} out of [0, {ColCount})");
        if (value == 0.0) return; // sparse: skip explicit zeros
        _rows.Add(row);
        _cols.Add(col);
        _values.Add(value);
    }

    /// <summary>
    /// Materialise to a row-major dense <c>double[rowCount, colCount]</c>.
    /// Duplicate triples sum into the same cell.
    /// </summary>
    public double[,] ToDense()
    {
        var m = new double[RowCount, ColCount];
        for (int k = 0; k < _values.Count; k++)
        {
            m[_rows[k], _cols[k]] += _values[k];
        }
        return m;
    }

    /// <summary>
    /// Compute <c>Aeq * x</c> directly without materialising. Useful for
    /// quick residual checks in tests.
    /// </summary>
    public double[] Multiply(IReadOnlyList<double> x)
    {
        if (x == null) throw new ArgumentNullException(nameof(x));
        if (x.Count != ColCount)
            throw new ArgumentException(
                $"x length {x.Count} does not match ColCount {ColCount}",
                nameof(x));

        var y = new double[RowCount];
        for (int k = 0; k < _values.Count; k++)
        {
            y[_rows[k]] += _values[k] * x[_cols[k]];
        }
        return y;
    }

    public override string ToString() =>
        $"SparseMatrixCoo({RowCount} x {ColCount}, nnz={NonZeroCount})";
}
