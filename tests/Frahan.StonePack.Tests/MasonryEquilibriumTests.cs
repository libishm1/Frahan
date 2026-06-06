#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Equilibrium;

namespace Frahan.Tests;

// Phase A.2 unit tests for the C# port of compas_cra's equilibrium-matrix
// builder. All pure-managed; no Rhino runtime needed; should run as PASS on
// the headless host. Style mirrors MasonryDataModelTests.cs: public-static
// methods, Assert(bool, string) helper, factory helpers at bottom.

static class MasonryEquilibriumTests
{
    private const double Tol = 1e-9;
    private const double TolForce = 1e-6;

    // -- SparseMatrixCoo ----------------------------------------------------

    public static void SparseMatrixCoo_ToDense_RoundTripsTriples()
    {
        var m = new SparseMatrixCoo(3, 4);
        m.Add(0, 0, 1.0);
        m.Add(0, 3, 2.5);
        m.Add(2, 1, -3.5);
        m.Add(1, 2, 7.0);

        var d = m.ToDense();
        Assert(d[0, 0] == 1.0, $"d[0,0] expected 1.0, got {d[0, 0]}");
        Assert(d[0, 3] == 2.5, $"d[0,3] expected 2.5, got {d[0, 3]}");
        Assert(d[2, 1] == -3.5, $"d[2,1] expected -3.5, got {d[2, 1]}");
        Assert(d[1, 2] == 7.0, $"d[1,2] expected 7.0, got {d[1, 2]}");
        Assert(d[0, 1] == 0.0, "d[0,1] should default to 0");
        Assert(m.NonZeroCount == 4, $"NonZeroCount expected 4, got {m.NonZeroCount}");
    }

    public static void SparseMatrixCoo_AddZero_DoesNotIncrementNnz()
    {
        var m = new SparseMatrixCoo(2, 2);
        m.Add(0, 0, 0.0);
        m.Add(1, 1, 0.0);
        Assert(m.NonZeroCount == 0, $"NonZeroCount expected 0 after adding zeros, got {m.NonZeroCount}");

        m.Add(0, 1, 4.0);
        Assert(m.NonZeroCount == 1, $"NonZeroCount expected 1 after one nonzero, got {m.NonZeroCount}");
    }

    public static void SparseMatrixCoo_OutOfBoundsRow_Throws()
    {
        var m = new SparseMatrixCoo(2, 2);
        bool threw = false;
        try { m.Add(2, 0, 1.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "out-of-bounds row should throw ArgumentOutOfRangeException");
    }

    // -- BlockCenterOfMass --------------------------------------------------

    public static void BlockCenterOfMass_UnitCube_VolumeWeighted_IsCenter()
    {
        var cube = MakeUnitCubeAt("cube", 0, 0, 0);
        var (cx, cy, cz) = BlockCenterOfMass.VolumeWeighted(cube, out double v);

        Assert(Math.Abs(v - 1.0) < Tol, $"signedVolume expected 1.0, got {v}");
        Assert(Math.Abs(cx - 0.5) < Tol, $"cx expected 0.5, got {cx}");
        Assert(Math.Abs(cy - 0.5) < Tol, $"cy expected 0.5, got {cy}");
        Assert(Math.Abs(cz - 0.5) < Tol, $"cz expected 0.5, got {cz}");
    }

    public static void BlockCenterOfMass_UnitCube_VertexMean_IsCenter()
    {
        var cube = MakeUnitCubeAt("cube", 0, 0, 0);
        var (cx, cy, cz) = BlockCenterOfMass.VertexMean(cube);

        Assert(Math.Abs(cx - 0.5) < Tol, $"cx expected 0.5, got {cx}");
        Assert(Math.Abs(cy - 0.5) < Tol, $"cy expected 0.5, got {cy}");
        Assert(Math.Abs(cz - 0.5) < Tol, $"cz expected 0.5, got {cz}");
    }

    public static void BlockCenterOfMass_DegenerateBlock_FallsBackToVertexMean()
    {
        // Three collinear vertices, one zero-area triangle. The signed-volume
        // accumulator gets a single tetV6 that is identically zero (since
        // a x b x c is degenerate when the points are collinear).
        var verts = new double[] { 0, 0, 0, 1, 0, 0, 2, 0, 0 };
        var tris = new[] { 0, 1, 2 };
        var degen = new MasonryBlock("degen", verts, tris, density: 1.0);

        var (cx, cy, cz) = BlockCenterOfMass.VolumeWeighted(degen, out double v);
        Assert(v == 0.0, $"signedVolume expected 0.0 for degenerate block, got {v}");

        // Vertex mean: ((0 + 1 + 2) / 3, 0, 0) = (1, 0, 0).
        Assert(Math.Abs(cx - 1.0) < Tol, $"fallback cx expected 1.0, got {cx}");
        Assert(Math.Abs(cy - 0.0) < Tol, $"fallback cy expected 0.0, got {cy}");
        Assert(Math.Abs(cz - 0.0) < Tol, $"fallback cz expected 0.0, got {cz}");
    }

    // -- EquilibriumMatrixBuilder shape -------------------------------------

    public static void EquilibriumBuilder_OneFreeBlockOnGround_AeqShape_IsCorrect()
    {
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);

        Assert(system.Aeq.RowCount == 6, $"rowCount expected 6, got {system.Aeq.RowCount}");
        Assert(system.Aeq.ColCount == 12, $"colCount expected 12 (4 verts * 3 components), got {system.Aeq.ColCount}");
        Assert(system.FreeBlockIds.Count == 1, $"freeBlockIds count expected 1, got {system.FreeBlockIds.Count}");
        Assert(system.FreeBlockIds[0] == "top", $"free block expected 'top', got '{system.FreeBlockIds[0]}'");
        Assert(system.ForceComponentsPerVertex == 3, $"shift expected 3, got {system.ForceComponentsPerVertex}");
    }

    public static void EquilibriumBuilder_PenaltyShift_DoublesNormalColumns()
    {
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: true);

        Assert(system.Aeq.ColCount == 16, $"colCount expected 16 (4 verts * 4 components), got {system.Aeq.ColCount}");
        Assert(system.ForceComponentsPerVertex == 4, $"shift expected 4 (penalty mode), got {system.ForceComponentsPerVertex}");
        Assert(system.Aeq.RowCount == 6, $"rowCount expected 6, got {system.Aeq.RowCount}");
    }

    // -- EquilibriumMatrixBuilder gravity -----------------------------------

    public static void EquilibriumBuilder_GravityPopulatesZForceRow()
    {
        // Single free unit cube of density 2400, volume 1.0. The Z-force row
        // (row index 2 of free block 0) should hold 2400 * 1 * (-9.80665).
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);

        double expected = 2400.0 * 1.0 * (-9.80665);
        Assert(Math.Abs(system.B[2] - expected) < TolForce,
            $"b[2] expected {expected}, got {system.B[2]}");

        // Other rows should be zero (gravity has no x/y component, no moment
        // about COM since it acts at the COM).
        for (int r = 0; r < system.B.Count; r++)
        {
            if (r == 2) continue;
            Assert(Math.Abs(system.B[r]) < TolForce,
                $"b[{r}] expected 0, got {system.B[r]}");
        }
    }

    // -- EquilibriumMatrixBuilder column structure --------------------------

    public static void EquilibriumBuilder_NormalForcesContributeOnlyToZForce_ForFlatHorizontalInterface()
    {
        // Sign note: interface is (blockA="ground", blockB="top"). "top" is
        // the free block, so it sees sign=-1 on its contributions. With
        // n=(0,0,1), each Normal column writes (sign*0, sign*0, sign*1) =
        // (0, 0, -1) into the force-balance rows of the free block. We check
        // X-force (row 0) and Y-force (row 1) are exactly zero, and that
        // Z-force (row 2) is non-zero. Exact value is sign * 1 = -1.
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        var dense = system.Aeq.ToDense();

        int normalColumnsSeen = 0;
        for (int col = 0; col < system.ForceColumns.Count; col++)
        {
            var fc = system.ForceColumns[col];
            if (fc.Component != ForceComponent.Normal) continue;
            normalColumnsSeen++;

            Assert(Math.Abs(dense[0, col]) < Tol,
                $"X-force row of Normal col {col} expected 0, got {dense[0, col]}");
            Assert(Math.Abs(dense[1, col]) < Tol,
                $"Y-force row of Normal col {col} expected 0, got {dense[1, col]}");
            Assert(Math.Abs(dense[2, col]) > Tol,
                $"Z-force row of Normal col {col} expected non-zero, got {dense[2, col]}");
            // Sign-corrected expected: top is block B, sign=-1, n_z=+1.
            Assert(Math.Abs(dense[2, col] - (-1.0)) < Tol,
                $"Z-force row of Normal col {col} expected -1.0, got {dense[2, col]}");
        }

        Assert(normalColumnsSeen == 4, $"expected 4 Normal columns (one per vertex), got {normalColumnsSeen}");
    }

    public static void EquilibriumBuilder_TangentForceMomentArmIsCorrect()
    {
        // Free block "top" sits at z in [1, 2]; COM = (0.5, 0.5, 1.5).
        // Contact polygon is at z = 1 with vertex 0 at (0, 0, 1).
        // r = vertex - COM = (-0.5, -0.5, -0.5).
        // tangent1 = (1, 0, 0).
        // r x t1 = (ry*0 - rz*0, rz*1 - rx*0, rx*0 - ry*1)
        //        = (0, -0.5, 0.5).
        // Sign convention: blockA="ground" (fixed, dropped), blockB="top"
        // contributes with sign = -1. So the moment-row entries become
        // (0, +0.5, -0.5). Force-row entries are sign * t1 = (-1, 0, 0).
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        var dense = system.Aeq.ToDense();

        // Locate the Tangent1 column for vertex 0 of the only interface.
        int t1Col = -1;
        for (int col = 0; col < system.ForceColumns.Count; col++)
        {
            var fc = system.ForceColumns[col];
            if (fc.InterfaceIndex == 0 && fc.VertexIndex == 0 && fc.Component == ForceComponent.Tangent1)
            {
                t1Col = col;
                break;
            }
        }
        Assert(t1Col >= 0, "Tangent1 column for (iface 0, vertex 0) not found");

        // Force rows: sign * t1 = -(1, 0, 0).
        Assert(Math.Abs(dense[0, t1Col] - (-1.0)) < Tol,
            $"force row 0 (X) expected -1.0, got {dense[0, t1Col]}");
        Assert(Math.Abs(dense[1, t1Col]) < Tol,
            $"force row 1 (Y) expected 0, got {dense[1, t1Col]}");
        Assert(Math.Abs(dense[2, t1Col]) < Tol,
            $"force row 2 (Z) expected 0, got {dense[2, t1Col]}");

        // Moment rows (3..5): sign * (r x t1) = -(0, -0.5, 0.5) = (0, 0.5, -0.5).
        Assert(Math.Abs(dense[3, t1Col] - 0.0) < Tol,
            $"moment row 3 (Mx) expected 0, got {dense[3, t1Col]}");
        Assert(Math.Abs(dense[4, t1Col] - 0.5) < Tol,
            $"moment row 4 (My) expected 0.5, got {dense[4, t1Col]}");
        Assert(Math.Abs(dense[5, t1Col] - (-0.5)) < Tol,
            $"moment row 5 (Mz) expected -0.5, got {dense[5, t1Col]}");
    }

    // -- EquilibriumMatrixBuilder multi-block -------------------------------

    public static void EquilibriumBuilder_TwoStackedBlocks_BothFree_HasTwoBlockRows()
    {
        // ground (z=0..1, fixed) + middle (z=1..2, free) + top (z=2..3, free).
        // Two interfaces: ground/middle at z=1, middle/top at z=2.
        var ground = MakeUnitCubeAt("ground", 0, 0, 0);
        var middle = MakeUnitCubeAt("middle", 0, 0, 1);
        var top = MakeUnitCubeAt("top", 0, 0, 2);

        var ifaceGroundMiddle = QuadInterfaceAtZ("ground", "middle", z: 1.0);
        var ifaceMiddleTop = QuadInterfaceAtZ("middle", "top", z: 2.0);

        var assembly = new MasonryAssembly(
            blocks: new[] { ground, middle, top },
            interfaces: new[] { ifaceGroundMiddle, ifaceMiddleTop },
            boundaryConditions: new BoundaryConditions(new[] { "ground" }));

        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);

        Assert(system.Aeq.RowCount == 12, $"rowCount expected 12 (2 free blocks * 6), got {system.Aeq.RowCount}");
        Assert(system.Aeq.ColCount == 24, $"colCount expected 24 ((4+4) verts * 3), got {system.Aeq.ColCount}");

        Assert(system.FreeBlockIds.Count == 2, $"freeBlockIds count expected 2, got {system.FreeBlockIds.Count}");
        Assert(system.FreeBlockIds[0] == "middle", $"free block 0 expected 'middle', got '{system.FreeBlockIds[0]}'");
        Assert(system.FreeBlockIds[1] == "top", $"free block 1 expected 'top', got '{system.FreeBlockIds[1]}'");
    }

    // -- Helpers ------------------------------------------------------------

    /// <summary>
    /// Two-block assembly: ground (fixed) + top (free) sharing one horizontal
    /// interface at z = 1. "top" is translated by (0, 0, 1), so its COM is
    /// (0.5, 0.5, 1.5) and its bottom face is in contact with the top of
    /// "ground".
    /// </summary>
    private static MasonryAssembly OneFreeOnGroundAssembly()
    {
        var ground = MakeUnitCubeAt("ground", 0, 0, 0);
        var top = MakeUnitCubeAt("top", 0, 0, 1);
        var iface = QuadInterfaceAtZ("ground", "top", z: 1.0);
        return new MasonryAssembly(
            blocks: new[] { ground, top },
            interfaces: new[] { iface },
            boundaryConditions: new BoundaryConditions(new[] { "ground" }));
    }

    /// <summary>
    /// Unit cube translated by (dx, dy, dz). 8 vertices, 12 outward-oriented
    /// triangles. Density 2400 kg/m^3 (typical stone).
    /// </summary>
    private static MasonryBlock MakeUnitCubeAt(string id, double dx, double dy, double dz)
    {
        var verts = new double[]
        {
            0+dx, 0+dy, 0+dz,
            1+dx, 0+dy, 0+dz,
            1+dx, 1+dy, 0+dz,
            0+dx, 1+dy, 0+dz,
            0+dx, 0+dy, 1+dz,
            1+dx, 0+dy, 1+dz,
            1+dx, 1+dy, 1+dz,
            0+dx, 1+dy, 1+dz,
        };
        var tris = new[]
        {
            0,2,1, 0,3,2, // -Z
            4,5,6, 4,6,7, // +Z
            0,1,5, 0,5,4, // -Y
            2,3,7, 2,7,6, // +Y
            1,2,6, 1,6,5, // +X
            0,4,7, 0,7,3, // -X
        };
        return new MasonryBlock(id, verts, tris, density: 2400.0);
    }

    /// <summary>
    /// 4-vertex quad contact polygon in the XY plane at the given z, with
    /// outward normal +Z, tangent1 = +X, tangent2 = +Y (right-handed).
    /// </summary>
    private static MasonryInterface QuadInterfaceAtZ(string a, string b, double z)
    {
        return new MasonryInterface(
            blockAId: a, blockBId: b,
            contactPolygon: new[]
            {
                new ContactVertex(0, 0, z),
                new ContactVertex(1, 0, z),
                new ContactVertex(1, 1, z),
                new ContactVertex(0, 1, z),
            },
            normalX: 0, normalY: 0, normalZ: 1,
            tangent1X: 1, tangent1Y: 0, tangent1Z: 0,
            tangent2X: 0, tangent2Y: 1, tangent2Z: 0);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
