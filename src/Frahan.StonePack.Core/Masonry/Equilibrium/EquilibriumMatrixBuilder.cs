#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Equilibrium;

// =============================================================================
// EquilibriumMatrixBuilder — pure-managed C# port of compas_cra's
// equilibrium_setup + make_aeq + aeq_block.
//
// Per Kao et al. 2022 (CAD vol 146, art 103216), the equilibrium constraint
// for each free block is written as
//
//     sum over its incident interfaces, sum over each interface vertex k
//         f_n_k * n  +  f_t1_k * t1  +  f_t2_k * t2     +  W_block  =  0   (force)
//         (r_k - c) x (f_n_k * n + f_t1_k * t1 + f_t2_k * t2)  +  M_W  =  0  (moment)
//
// where r_k is the contact-vertex world position, c is the block centre of
// mass, and W_block is the block's external load (gravity in our case).
//
// Sign convention (matches compas_cra/equilibrium/cra_helper.py): the per-
// interface normal points FROM block A INTO block B. Block A sees the force
// vector with sign +1; block B sees -1. compas_cra's reverse= flag toggles
// this. We honour the same convention here.
//
// Aeq layout: 6 rows per free block (3 force, 3 moment in xyz order),
// shift columns per contact vertex (3 = n, t1, t2 in no-penalty mode; the
// builder also supports shift=4 with split normals, used by the penalty
// formulation).
// =============================================================================

/// <summary>
/// Builds the equilibrium matrix Aeq and load vector b from a
/// <see cref="MasonryAssembly"/>. Pure-managed; allocates a single
/// <see cref="SparseMatrixCoo"/> and a single double[] for b.
/// </summary>
public static class EquilibriumMatrixBuilder
{
    /// <summary>
    /// Standard gravity acceleration in the -Z direction.
    /// Override via <see cref="Build"/> when needed.
    /// </summary>
    public const double DefaultGravityZ = -9.80665;

    /// <summary>
    /// Build the equilibrium system.
    /// </summary>
    /// <param name="assembly">Block + interface graph + boundary conditions.</param>
    /// <param name="penalty">If true, columns split the normal force into f_n+ / f_n- pair (shift=4); else single normal (shift=3).</param>
    /// <param name="gravityZ">Z-component of gravity acceleration (default -9.80665 m/s^2).</param>
    public static EquilibriumSystem Build(
        MasonryAssembly assembly,
        bool penalty = false,
        double gravityZ = DefaultGravityZ)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));

        int shift = penalty ? 4 : 3;

        // ---- Row layout: 6 dofs per free block, in fixed iteration order. ----
        var freeBlockIds = new List<string>();
        var blockRowOffset = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < assembly.Blocks.Count; i++)
        {
            var b = assembly.Blocks[i];
            if (assembly.BoundaryConditions.IsFixed(b.Id)) continue;
            blockRowOffset[b.Id] = freeBlockIds.Count * 6;
            freeBlockIds.Add(b.Id);
        }

        // ---- Column layout: shift dofs per contact vertex, per interface. ----
        var forceColumns = new List<ForceColumn>();
        for (int ifIndex = 0; ifIndex < assembly.Interfaces.Count; ifIndex++)
        {
            var iface = assembly.Interfaces[ifIndex];
            for (int v = 0; v < iface.VertexCount; v++)
            {
                if (penalty)
                {
                    forceColumns.Add(new ForceColumn(ifIndex, v, ForceComponent.NormalPositive));
                    forceColumns.Add(new ForceColumn(ifIndex, v, ForceComponent.NormalNegative));
                }
                else
                {
                    forceColumns.Add(new ForceColumn(ifIndex, v, ForceComponent.Normal));
                }
                forceColumns.Add(new ForceColumn(ifIndex, v, ForceComponent.Tangent1));
                forceColumns.Add(new ForceColumn(ifIndex, v, ForceComponent.Tangent2));
            }
        }

        int rowCount = freeBlockIds.Count * 6;
        int colCount = forceColumns.Count;
        var aeq = new SparseMatrixCoo(rowCount, colCount);

        // ---- Block centre-of-mass cache (used by moment balance). ----
        var comById = new Dictionary<string, (double X, double Y, double Z)>(StringComparer.Ordinal);
        for (int i = 0; i < assembly.Blocks.Count; i++)
        {
            var b = assembly.Blocks[i];
            comById[b.Id] = BlockCenterOfMass.VolumeWeighted(b, out _);
        }

        // ---- Walk interfaces and contribute to Aeq. ----
        int colCursor = 0;
        for (int ifIndex = 0; ifIndex < assembly.Interfaces.Count; ifIndex++)
        {
            var iface = assembly.Interfaces[ifIndex];
            int verticesInThisIface = iface.VertexCount;
            int colsForThisIface = verticesInThisIface * shift;

            // Block A sees +1 on all force/moment contributions; block B sees -1.
            ContributeBlock(
                aeq, iface, comById, blockRowOffset,
                blockId: iface.BlockAId, sign: +1.0,
                shift: shift, ifaceColStart: colCursor);
            ContributeBlock(
                aeq, iface, comById, blockRowOffset,
                blockId: iface.BlockBId, sign: -1.0,
                shift: shift, ifaceColStart: colCursor);

            colCursor += colsForThisIface;
        }

        // ---- External load: gravity per free block. ----
        var b_ = new double[rowCount];
        for (int i = 0; i < freeBlockIds.Count; i++)
        {
            var block = assembly.GetBlock(freeBlockIds[i]);
            double volume = Math.Abs(BlockCenterOfMass.SignedVolume(block));
            double weightZ = block.Density * volume * gravityZ;
            int rowBase = i * 6;
            // Force balance: + W_z must be balanced by contact forces, so we
            // put the weight on the same side and require Aeq * f + b = 0.
            b_[rowBase + 2] = weightZ;
            // Moment of gravity about COM is zero (gravity acts at COM).
        }

        return new EquilibriumSystem(
            aeq: aeq,
            b: b_,
            freeBlockIds: freeBlockIds,
            forceColumns: forceColumns,
            forceComponentsPerVertex: shift);
    }

    private static void ContributeBlock(
        SparseMatrixCoo aeq,
        MasonryInterface iface,
        Dictionary<string, (double X, double Y, double Z)> comById,
        Dictionary<string, int> blockRowOffset,
        string blockId,
        double sign,
        int shift,
        int ifaceColStart)
    {
        if (!blockRowOffset.TryGetValue(blockId, out int rowBase))
        {
            // Block is fixed (boundary condition); its rows are not in Aeq.
            return;
        }

        var (cx, cy, cz) = comById[blockId];

        double nx = iface.NormalX, ny = iface.NormalY, nz = iface.NormalZ;
        double t1x = iface.Tangent1X, t1y = iface.Tangent1Y, t1z = iface.Tangent1Z;
        double t2x = iface.Tangent2X, t2y = iface.Tangent2Y, t2z = iface.Tangent2Z;

        for (int v = 0; v < iface.VertexCount; v++)
        {
            var vert = iface.ContactPolygon[v];
            double rx = vert.X - cx;
            double ry = vert.Y - cy;
            double rz = vert.Z - cz;

            int colVertexStart = ifaceColStart + v * shift;

            if (shift == 3)
            {
                // f = f_n * n + f_t1 * t1 + f_t2 * t2
                AddForceAndMoment(aeq, rowBase, colVertexStart + 0, sign, nx, ny, nz, rx, ry, rz);
                AddForceAndMoment(aeq, rowBase, colVertexStart + 1, sign, t1x, t1y, t1z, rx, ry, rz);
                AddForceAndMoment(aeq, rowBase, colVertexStart + 2, sign, t2x, t2y, t2z, rx, ry, rz);
            }
            else // shift == 4 (penalty)
            {
                // f_n_pos contributes +n, f_n_neg contributes -n.
                AddForceAndMoment(aeq, rowBase, colVertexStart + 0, sign, nx, ny, nz, rx, ry, rz);
                AddForceAndMoment(aeq, rowBase, colVertexStart + 1, -sign, nx, ny, nz, rx, ry, rz);
                AddForceAndMoment(aeq, rowBase, colVertexStart + 2, sign, t1x, t1y, t1z, rx, ry, rz);
                AddForceAndMoment(aeq, rowBase, colVertexStart + 3, sign, t2x, t2y, t2z, rx, ry, rz);
            }
        }
    }

    /// <summary>
    /// Add a single basis-vector contribution: force component (3 rows) +
    /// moment component (3 rows = (r - c) x basis) at the given column.
    /// </summary>
    private static void AddForceAndMoment(
        SparseMatrixCoo aeq, int rowBase, int col,
        double sign,
        double bx, double by, double bz,   // basis vector (n / t1 / t2 in world)
        double rx, double ry, double rz)   // r = vertex - com
    {
        // Force balance: rows 0..2
        aeq.Add(rowBase + 0, col, sign * bx);
        aeq.Add(rowBase + 1, col, sign * by);
        aeq.Add(rowBase + 2, col, sign * bz);

        // Moment balance about COM: r x b
        double mx = ry * bz - rz * by;
        double my = rz * bx - rx * bz;
        double mz = rx * by - ry * bx;
        aeq.Add(rowBase + 3, col, sign * mx);
        aeq.Add(rowBase + 4, col, sign * my);
        aeq.Add(rowBase + 5, col, sign * mz);
    }
}
