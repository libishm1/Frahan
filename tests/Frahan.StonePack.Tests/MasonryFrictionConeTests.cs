#nullable disable
using System;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Equilibrium;

namespace Frahan.Tests;

// Phase A.3 unit tests for the polyhedral friction-cone builder
// (Frahan.Masonry.Equilibrium.FrictionConeBuilder). All pure-managed; no
// Rhino runtime needed; runs as PASS on the headless host. Style mirrors
// MasonryDataModelTests.cs / MasonryEquilibriumTests.cs: public-static
// methods, Assert(bool, string) helper, factory helpers at bottom.

static class MasonryFrictionConeTests
{
    private const double Tol = 1e-12;

    // -- Argument validation -----------------------------------------------

    public static void FrictionConeBuilder_NullEquilibrium_Throws()
    {
        bool threw = false;
        try { _ = FrictionConeBuilder.Build(null); }
        catch (ArgumentNullException) { threw = true; }
        Assert(threw, "null equilibrium should throw ArgumentNullException");
    }

    public static void FrictionConeBuilder_NonPositiveMu_Throws()
    {
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);

        bool threwZero = false;
        try { _ = FrictionConeBuilder.Build(system, mu: 0.0); }
        catch (ArgumentOutOfRangeException) { threwZero = true; }
        Assert(threwZero, "mu = 0 should throw ArgumentOutOfRangeException");

        bool threwNeg = false;
        try { _ = FrictionConeBuilder.Build(system, mu: -1.0); }
        catch (ArgumentOutOfRangeException) { threwNeg = true; }
        Assert(threwNeg, "mu = -1 should throw ArgumentOutOfRangeException");
    }

    public static void FrictionConeBuilder_FaceCountBelow3_Throws()
    {
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);

        bool threw = false;
        try { _ = FrictionConeBuilder.Build(system, faceCount: 2); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "faceCount = 2 should throw ArgumentOutOfRangeException");
    }

    // -- Shape -------------------------------------------------------------

    public static void FrictionConeBuilder_OneFreeBlock_4Faces_Has16Rows()
    {
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        var cone = FrictionConeBuilder.Build(system, mu: 0.5, faceCount: 4);

        // 4 contact vertices (one quad interface) * 4 faces = 16 rows.
        Assert(cone.Afr.RowCount == 16,
            $"Afr.RowCount expected 16 (4 verts * 4 faces), got {cone.Afr.RowCount}");
        Assert(cone.Afr.ColCount == system.Aeq.ColCount,
            $"Afr.ColCount expected {system.Aeq.ColCount}, got {cone.Afr.ColCount}");
        Assert(cone.Afr.ColCount == 12,
            $"Afr.ColCount expected 12 (4 verts * 3 components), got {cone.Afr.ColCount}");
    }

    public static void FrictionConeBuilder_OneFreeBlock_8Faces_Has32Rows()
    {
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        var cone = FrictionConeBuilder.Build(system, mu: 0.5, faceCount: 8);

        // 4 contact vertices * 8 faces = 32 rows.
        Assert(cone.Afr.RowCount == 32,
            $"Afr.RowCount expected 32 (4 verts * 8 faces), got {cone.Afr.RowCount}");
        Assert(cone.Afr.ColCount == system.Aeq.ColCount,
            $"Afr.ColCount expected {system.Aeq.ColCount}, got {cone.Afr.ColCount}");
    }

    // -- Coefficient correctness for K=4 -----------------------------------

    public static void FrictionConeBuilder_4FaceCoefficients_AreExactInteger()
    {
        // For K=4 the per-vertex 4 face rows must be exactly:
        //   row 0: (-mu, +1,  0)   (normal, tangent1, tangent2)
        //   row 1: (-mu,  0, +1)
        //   row 2: (-mu, -1,  0)
        //   row 3: (-mu,  0, -1)
        // These come from cos(theta_k), sin(theta_k) with theta_k=pi/2*k and
        // the FrictionConeBuilder K=4 special case.
        const double mu = 0.5;
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        var cone = FrictionConeBuilder.Build(system, mu: mu, faceCount: 4);

        // Find the column triple (Normal, Tangent1, Tangent2) for vertex 0
        // of interface 0.
        int nCol = -1, t1Col = -1, t2Col = -1;
        for (int col = 0; col < system.ForceColumns.Count; col++)
        {
            var fc = system.ForceColumns[col];
            if (fc.InterfaceIndex != 0 || fc.VertexIndex != 0) continue;
            if (fc.Component == ForceComponent.Normal) nCol = col;
            else if (fc.Component == ForceComponent.Tangent1) t1Col = col;
            else if (fc.Component == ForceComponent.Tangent2) t2Col = col;
        }
        Assert(nCol >= 0 && t1Col >= 0 && t2Col >= 0,
            "Normal/Tangent1/Tangent2 columns for (iface 0, vert 0) not found");

        var dense = cone.Afr.ToDense();

        // Vertex 0 occupies rows 0..3 (rowBase = vertexGroupIndex * faceCount = 0*4 = 0).
        // Row 0: (-mu, +1, 0).
        Assert(Math.Abs(dense[0, nCol]  - (-mu)) < Tol, $"row 0 normal expected -mu, got {dense[0, nCol]}");
        Assert(Math.Abs(dense[0, t1Col] - ( 1.0)) < Tol, $"row 0 t1 expected +1, got {dense[0, t1Col]}");
        Assert(Math.Abs(dense[0, t2Col] - ( 0.0)) < Tol, $"row 0 t2 expected 0, got {dense[0, t2Col]}");

        // Row 1: (-mu, 0, +1).
        Assert(Math.Abs(dense[1, nCol]  - (-mu)) < Tol, $"row 1 normal expected -mu, got {dense[1, nCol]}");
        Assert(Math.Abs(dense[1, t1Col] - ( 0.0)) < Tol, $"row 1 t1 expected 0, got {dense[1, t1Col]}");
        Assert(Math.Abs(dense[1, t2Col] - ( 1.0)) < Tol, $"row 1 t2 expected +1, got {dense[1, t2Col]}");

        // Row 2: (-mu, -1, 0).
        Assert(Math.Abs(dense[2, nCol]  - (-mu)) < Tol, $"row 2 normal expected -mu, got {dense[2, nCol]}");
        Assert(Math.Abs(dense[2, t1Col] - (-1.0)) < Tol, $"row 2 t1 expected -1, got {dense[2, t1Col]}");
        Assert(Math.Abs(dense[2, t2Col] - ( 0.0)) < Tol, $"row 2 t2 expected 0, got {dense[2, t2Col]}");

        // Row 3: (-mu, 0, -1).
        Assert(Math.Abs(dense[3, nCol]  - (-mu)) < Tol, $"row 3 normal expected -mu, got {dense[3, nCol]}");
        Assert(Math.Abs(dense[3, t1Col] - ( 0.0)) < Tol, $"row 3 t1 expected 0, got {dense[3, t1Col]}");
        Assert(Math.Abs(dense[3, t2Col] - (-1.0)) < Tol, $"row 3 t2 expected -1, got {dense[3, t2Col]}");
    }

    // -- Penalty-mode normal split -----------------------------------------

    public static void FrictionConeBuilder_PenaltyMode_SplitsNormalIntoPair()
    {
        // In penalty mode (shift=4) the normal contribution -mu * f_n splits
        // onto -mu * f_n_pos + mu * f_n_neg, which expands the linear term
        // -mu * (f_n_pos - f_n_neg).
        const double mu = 0.5;
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: true);
        var cone = FrictionConeBuilder.Build(system, mu: mu, faceCount: 4);

        int nPosCol = -1, nNegCol = -1;
        for (int col = 0; col < system.ForceColumns.Count; col++)
        {
            var fc = system.ForceColumns[col];
            if (fc.InterfaceIndex != 0 || fc.VertexIndex != 0) continue;
            if (fc.Component == ForceComponent.NormalPositive) nPosCol = col;
            else if (fc.Component == ForceComponent.NormalNegative) nNegCol = col;
        }
        Assert(nPosCol >= 0 && nNegCol >= 0,
            "NormalPositive/NormalNegative columns for (iface 0, vert 0) not found");

        var dense = cone.Afr.ToDense();

        // Vertex 0, row 0 (theta_0 = 0): -mu on f_n_pos, +mu on f_n_neg.
        Assert(Math.Abs(dense[0, nPosCol] - (-mu)) < Tol,
            $"row 0 NormalPositive expected -mu, got {dense[0, nPosCol]}");
        Assert(Math.Abs(dense[0, nNegCol] - (+mu)) < Tol,
            $"row 0 NormalNegative expected +mu, got {dense[0, nNegCol]}");
    }

    // -- Constants ---------------------------------------------------------

    public static void FrictionConeBuilder_DefaultMu_Is084()
    {
        Assert(FrictionConeBuilder.DefaultMu == 0.84,
            $"FrictionConeBuilder.DefaultMu expected 0.84, got {FrictionConeBuilder.DefaultMu}");

        // FrictionConeMatrix.Mu round-trips the value passed in.
        var assembly = OneFreeOnGroundAssembly();
        var system = EquilibriumMatrixBuilder.Build(assembly, penalty: false);
        var cone = FrictionConeBuilder.Build(system, mu: 0.84, faceCount: 4);
        Assert(cone.Mu == 0.84, $"FrictionConeMatrix.Mu round-trip expected 0.84, got {cone.Mu}");
        Assert(cone.FaceCount == 4, $"FrictionConeMatrix.FaceCount round-trip expected 4, got {cone.FaceCount}");
    }

    // -- Helpers -----------------------------------------------------------

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
