#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Equilibrium;

/// <summary>
/// Output of <see cref="EquilibriumMatrixBuilder.Build"/>. Holds the
/// equilibrium matrix Aeq, the external load vector b, and the index
/// tables needed to interpret a solution f back into per-interface,
/// per-vertex contact forces.
///
/// Sign convention: Aeq * f + b == 0 expresses force + moment balance
/// for every free block. (Equivalent forms in literature: Aeq * f = -b
/// or Aeq * f = b with sign flip on b. We pick "Aeq * f + b = 0" because
/// it matches compas_cra's <c>ceq</c> rule definition.)
/// </summary>
public sealed class EquilibriumSystem
{
    public EquilibriumSystem(
        SparseMatrixCoo aeq,
        IReadOnlyList<double> b,
        IReadOnlyList<string> freeBlockIds,
        IReadOnlyList<ForceColumn> forceColumns,
        int forceComponentsPerVertex)
    {
        Aeq = aeq ?? throw new ArgumentNullException(nameof(aeq));
        B = b ?? throw new ArgumentNullException(nameof(b));
        FreeBlockIds = freeBlockIds ?? throw new ArgumentNullException(nameof(freeBlockIds));
        ForceColumns = forceColumns ?? throw new ArgumentNullException(nameof(forceColumns));
        ForceComponentsPerVertex = forceComponentsPerVertex;

        if (b.Count != aeq.RowCount)
            throw new ArgumentException(
                $"b length {b.Count} != Aeq row count {aeq.RowCount}",
                nameof(b));
        if (forceColumns.Count != aeq.ColCount)
            throw new ArgumentException(
                $"forceColumns count {forceColumns.Count} != Aeq col count {aeq.ColCount}",
                nameof(forceColumns));
    }

    /// <summary>Equilibrium matrix; rows = 6 per free block, cols = forceComponentsPerVertex per contact vertex.</summary>
    public SparseMatrixCoo Aeq { get; }

    /// <summary>External load vector (gravity); length = Aeq.RowCount.</summary>
    public IReadOnlyList<double> B { get; }

    /// <summary>The block ids whose 6 DOFs occupy the rows, in row order.</summary>
    public IReadOnlyList<string> FreeBlockIds { get; }

    /// <summary>For each Aeq column k, what does <c>f[k]</c> mean? (Which interface, which vertex, which component.)</summary>
    public IReadOnlyList<ForceColumn> ForceColumns { get; }

    /// <summary>3 (no-penalty: f_n, f_t1, f_t2) or 4 (penalty: f_n+, f_n-, f_t1, f_t2).</summary>
    public int ForceComponentsPerVertex { get; }

    public override string ToString() =>
        $"EquilibriumSystem(rows={Aeq.RowCount}, cols={Aeq.ColCount}, " +
        $"nnz={Aeq.NonZeroCount}, free_blocks={FreeBlockIds.Count}, " +
        $"shift={ForceComponentsPerVertex})";
}

/// <summary>
/// Decoder for one column of <see cref="EquilibriumSystem.Aeq"/>: which
/// interface, which vertex of that interface, which force component.
/// </summary>
public sealed class ForceColumn
{
    public ForceColumn(int interfaceIndex, int vertexIndex, ForceComponent component)
    {
        if (interfaceIndex < 0) throw new ArgumentOutOfRangeException(nameof(interfaceIndex));
        if (vertexIndex < 0) throw new ArgumentOutOfRangeException(nameof(vertexIndex));
        InterfaceIndex = interfaceIndex;
        VertexIndex = vertexIndex;
        Component = component;
    }

    public int InterfaceIndex { get; }
    public int VertexIndex { get; }
    public ForceComponent Component { get; }

    public override string ToString() =>
        $"ForceColumn(iface={InterfaceIndex}, vert={VertexIndex}, {Component})";
}

/// <summary>
/// Which component of the per-vertex contact force. The penalty form
/// splits the normal into compressive (NormalPositive) and tensile
/// (NormalNegative) halves so the QP can keep both bound at zero
/// simultaneously to enforce the Signorini complementarity condition.
/// </summary>
public enum ForceComponent
{
    Normal,           // shift==3, no-penalty mode
    NormalPositive,   // shift==4, penalty mode (compressive only)
    NormalNegative,   // shift==4, penalty mode (tensile only; should equal 0 at convergence)
    Tangent1,
    Tangent2,
}
