#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Equilibrium;

// =============================================================================
// FrictionConeBuilder — pure-managed C# port of compas_cra's
// friction_setup + _make_afr (BlockResearchGroup/compas_cra, MIT licence).
//
// Per Kao et al. 2022 (CAD vol 146, art 103216), the Coulomb friction
// constraint at each contact vertex is
//
//     sqrt(f_t1^2 + f_t2^2)  <=  mu * f_n
//
// which is non-linear and second-order. The RBE / CRA formulations linearise
// it with a polyhedral pyramid: take K equally spaced face directions in the
// (t1, t2) plane and require the projection of (f_t1, f_t2) onto each face
// normal to be bounded by mu * f_n. compas_cra's _make_afr uses K=4 (a
// 4-face square pyramid). We expose K via the <c>faceCount</c> parameter so
// callers can trade accuracy for problem size (K=8 halves the linearisation
// error at the cost of doubling the row count).
//
// One face row at angle theta_k = 2*pi*k/K is
//
//     cos(theta_k) * f_t1  +  sin(theta_k) * f_t2  -  mu * f_n  <=  0.
//
// Output Afr is a sparse matrix with K rows per contact vertex and the same
// column layout as Aeq, so the inequality reads <c>Afr * f &lt;= 0</c>
// component-wise. In penalty mode (shift=4) the f_n term splits onto the
// f_n_pos and f_n_neg columns: <c>-mu</c> on f_n_pos and <c>+mu</c> on
// f_n_neg, which is the linear expansion of <c>-mu * (f_n_pos - f_n_neg)</c>.
// =============================================================================

/// <summary>
/// Result of <see cref="FrictionConeBuilder.Build"/>. Holds the polyhedral
/// friction-cone constraint matrix Afr together with the parameters used to
/// build it, so a downstream solver can record (or echo) the linearisation.
/// </summary>
public sealed class FrictionConeMatrix
{
    public FrictionConeMatrix(SparseMatrixCoo afr, int faceCount, double mu)
    {
        Afr = afr ?? throw new ArgumentNullException(nameof(afr));
        if (faceCount < 3)
            throw new ArgumentOutOfRangeException(nameof(faceCount),
                $"faceCount must be >= 3, got {faceCount}");
        if (mu <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(mu),
                $"mu must be > 0, got {mu}");
        FaceCount = faceCount;
        Mu = mu;
    }

    /// <summary>
    /// Sparse inequality matrix; the friction constraint is <c>Afr * f &lt;= 0</c>.
    /// Row count = <c>FaceCount * (number of contact vertices)</c>.
    /// Column count matches the corresponding <see cref="EquilibriumSystem.Aeq"/>.
    /// </summary>
    public SparseMatrixCoo Afr { get; }

    /// <summary>Number of pyramidal faces used to linearise the cone (K).</summary>
    public int FaceCount { get; }

    /// <summary>Coulomb friction coefficient used to populate the rows.</summary>
    public double Mu { get; }

    public override string ToString() =>
        $"FrictionConeMatrix(rows={Afr.RowCount}, cols={Afr.ColCount}, " +
        $"nnz={Afr.NonZeroCount}, faces={FaceCount}, mu={Mu})";
}

/// <summary>
/// Builds the polyhedral friction-cone constraint matrix Afr for an
/// <see cref="EquilibriumSystem"/>. Pure-managed; no Rhino dependency.
/// Mirrors <c>friction_setup(assembly, mu, penalty=False)</c> from
/// BlockResearchGroup/compas_cra (MIT) and the CRA/RBE formulation in
/// Kao et al. 2022 (CAD vol 146, art 103216).
/// </summary>
public static class FrictionConeBuilder
{
    /// <summary>
    /// Default Coulomb coefficient. Matches compas_cra's <c>mu=0.84</c>
    /// default (corresponds to a 40 deg friction angle, typical for dry stone).
    /// </summary>
    public const double DefaultMu = 0.84;

    /// <summary>
    /// Build the friction-cone inequality matrix Afr.
    /// </summary>
    /// <param name="equilibrium">
    /// The equilibrium system whose <see cref="EquilibriumSystem.ForceColumns"/>
    /// define the column layout. Afr operates on the same force vector f.
    /// </param>
    /// <param name="mu">
    /// Coulomb friction coefficient (must be &gt; 0). One coefficient is used
    /// for every contact vertex; per-interface variation is left to a future
    /// overload.
    /// </param>
    /// <param name="faceCount">
    /// Number of pyramidal faces (K, must be &gt;= 3). compas_cra hard-codes
    /// K=4. Increase to 8 or 16 for tighter linearisation; row count scales
    /// linearly.
    /// </param>
    public static FrictionConeMatrix Build(
        EquilibriumSystem equilibrium,
        double mu = DefaultMu,
        int faceCount = 4)
    {
        if (equilibrium == null) throw new ArgumentNullException(nameof(equilibrium));
        if (mu <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(mu),
                $"mu must be > 0, got {mu}");
        if (faceCount < 3)
            throw new ArgumentOutOfRangeException(nameof(faceCount),
                $"faceCount must be >= 3, got {faceCount}");

        int colCount = equilibrium.Aeq.ColCount;
        int shift = equilibrium.ForceComponentsPerVertex;
        var forceColumns = equilibrium.ForceColumns;

        // ---- Group columns by (interface, vertex) to find each contact's column triple/quad. ----
        // ForceColumns are emitted in fixed order by EquilibriumMatrixBuilder, so a sequential
        // scan that records the first column index of every (iface, vertex) group is enough.
        var contactGroups = new List<ContactColumns>();
        var seen = new Dictionary<long, int>(); // (iface << 32 | vertex) -> index in contactGroups

        for (int k = 0; k < forceColumns.Count; k++)
        {
            var fc = forceColumns[k];
            long key = ((long)fc.InterfaceIndex << 32) | (uint)fc.VertexIndex;

            if (!seen.TryGetValue(key, out int idx))
            {
                idx = contactGroups.Count;
                seen[key] = idx;
                contactGroups.Add(new ContactColumns
                {
                    NormalCol = -1,
                    NormalPosCol = -1,
                    NormalNegCol = -1,
                    Tangent1Col = -1,
                    Tangent2Col = -1,
                });
            }

            var group = contactGroups[idx];
            switch (fc.Component)
            {
                case ForceComponent.Normal:         group.NormalCol = k;    break;
                case ForceComponent.NormalPositive: group.NormalPosCol = k; break;
                case ForceComponent.NormalNegative: group.NormalNegCol = k; break;
                case ForceComponent.Tangent1:       group.Tangent1Col = k;  break;
                case ForceComponent.Tangent2:       group.Tangent2Col = k;  break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown force component {fc.Component}");
            }
            contactGroups[idx] = group;
        }

        // ---- Sanity check: every contact must expose a tangent pair and a normal axis. ----
        for (int g = 0; g < contactGroups.Count; g++)
        {
            var grp = contactGroups[g];
            if (grp.Tangent1Col < 0 || grp.Tangent2Col < 0)
                throw new InvalidOperationException(
                    $"Contact group {g} is missing a tangent column; " +
                    $"check EquilibriumMatrixBuilder column layout.");
            if (shift == 3)
            {
                if (grp.NormalCol < 0)
                    throw new InvalidOperationException(
                        $"Contact group {g} (no-penalty) is missing the normal column.");
            }
            else // shift == 4
            {
                if (grp.NormalPosCol < 0 || grp.NormalNegCol < 0)
                    throw new InvalidOperationException(
                        $"Contact group {g} (penalty) is missing a split-normal column.");
            }
        }

        // ---- Allocate Afr with K rows per contact vertex. ----
        int rowCount = faceCount * contactGroups.Count;
        var afr = new SparseMatrixCoo(rowCount, colCount);

        // ---- Emit K face rows per contact. ----
        // Face k uses theta_k = 2*pi*k/K. Special-case K=4 to use exact
        // {+1, 0, -1, 0} coefficients so we exactly match compas_cra._make_afr.
        for (int g = 0; g < contactGroups.Count; g++)
        {
            var grp = contactGroups[g];
            int rowBase = g * faceCount;

            for (int k = 0; k < faceCount; k++)
            {
                int row = rowBase + k;

                double cos_k, sin_k;
                if (faceCount == 4)
                {
                    // 0, 90, 180, 270 deg → exact integer coefficients.
                    switch (k)
                    {
                        case 0: cos_k =  1.0; sin_k =  0.0; break;
                        case 1: cos_k =  0.0; sin_k =  1.0; break;
                        case 2: cos_k = -1.0; sin_k =  0.0; break;
                        case 3: cos_k =  0.0; sin_k = -1.0; break;
                        default:
                            throw new InvalidOperationException(
                                "Unreachable: faceCount==4 must have k in [0,4).");
                    }
                }
                else
                {
                    double theta = 2.0 * Math.PI * k / faceCount;
                    cos_k = Math.Cos(theta);
                    sin_k = Math.Sin(theta);
                }

                // Tangent contributions: cos_k * f_t1 + sin_k * f_t2
                afr.Add(row, grp.Tangent1Col, cos_k);
                afr.Add(row, grp.Tangent2Col, sin_k);

                // Normal contribution: -mu * f_n. In penalty mode the normal is
                // (f_n_pos - f_n_neg), so the row contributes -mu on the pos
                // column and +mu on the neg column.
                if (shift == 3)
                {
                    afr.Add(row, grp.NormalCol, -mu);
                }
                else // shift == 4
                {
                    afr.Add(row, grp.NormalPosCol, -mu);
                    afr.Add(row, grp.NormalNegCol, +mu);
                }
            }
        }

        return new FrictionConeMatrix(afr, faceCount, mu);
    }

    /// <summary>
    /// Mutable bag of column indices for one contact vertex. Used only inside
    /// <see cref="Build"/>; not exposed.
    /// </summary>
    private struct ContactColumns
    {
        public int NormalCol;
        public int NormalPosCol;
        public int NormalNegCol;
        public int Tangent1Col;
        public int Tangent2Col;
    }
}
