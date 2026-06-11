#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Frahan.Masonry.Equilibrium;
using Frahan.Masonry.Solvers;

namespace Frahan.Tests;

// KB-9 diagnostics: is the detector-path equality system INCONSISTENT (frame /
// assembly bug) or consistent-but-ADMM-unsolvable (conditioning)? Probe the
// 3-cube stack across tilt angles: least-squares residual of Aeq f = -b tells
// which. Temporary instrumentation; prints, never fails.

static class Kb9DiagnosticsTests
{
    public static void Kb9_TiltSweep_EqualityConsistency()
    {
        foreach (var deg in new[] { 0.0, 5, 10, 15, 20 })
        {
            var coords = new List<IReadOnlyList<double>>();
            var tris = new List<IReadOnlyList<int>>();
            for (int i = 0; i < 3; i++)
            {
                var (v, t) = Box(new[] { 0.0, 0, (double)i }, 1, 1, 1);
                coords.Add(RotY(v, deg * Math.PI / 180));
                tris.Add(t);
            }
            var asm = MasonryStabilityChecker.BuildAssemblyFromMeshes(
                coords, tris, density: 1.0, contactDistanceTol: 1e-3,
                contactAngleTolDeg: 5.0, fixBelowZ: 0.01);
            var sys = EquilibriumMatrixBuilder.Build(asm, penalty: false);
            var a = sys.Aeq.ToDense();
            int m = a.GetLength(0), n = a.GetLength(1);
            var b = sys.B.ToArray();

            double resRel = LeastSquaresResidual(a, b);

            // interface forensics
            var ifaceInfo = string.Join(" ; ", asm.Interfaces.Select(itf =>
                $"v{itf.ContactPolygon.Count} n=({itf.NormalX:0.00},{itf.NormalY:0.00},{itf.NormalZ:0.00})"));
            var rbe = MasonryStabilityChecker.CheckMeshes(
                coords, tris, density: 1.0, contactDistanceTol: 1e-3, contactAngleTolDeg: 5.0,
                fixBelowZ: 0.01, mu: 0.84, faceCount: 8, inscribed: true);
            Console.WriteLine($"      [kb9] tilt {deg,4:0.#} deg: free {sys.FreeBlockIds.Count}, " +
                              $"A {m}x{n}, LSresidual(rel) {resRel:0.000e0}, verdict {(rbe.IsStable ? "STABLE" : "FAIL")} | {ifaceInfo}");
        }
    }

    public static void Kb9_Arch_Forensics()
    {
        var (coords, tris) = ArchFixture();
        var asm = MasonryStabilityChecker.BuildAssemblyFromMeshes(
            coords, tris, density: 1.0, contactDistanceTol: 1e-3,
            contactAngleTolDeg: 5.0, fixBelowZ: 0.01);
        var sys = EquilibriumMatrixBuilder.Build(asm, penalty: false);
        var a = sys.Aeq.ToDense();
        double resRel = LeastSquaresResidual(a, sys.B.ToArray());
        var vcounts = string.Join(",", asm.Interfaces.Select(i => i.ContactPolygon.Count));
        Console.WriteLine($"      [kb9] arch: free {sys.FreeBlockIds.Count}/{asm.Blocks.Count}, ifaces {asm.Interfaces.Count}, " +
                          $"A {a.GetLength(0)}x{a.GetLength(1)}, LSresidual(rel) {resRel:0.000e0}, polyverts [{vcounts}]");
    }

    public static void Kb9_Arch_CoplanarResolver()
    {
        // same arch, detector path, but with the coplanar-coincidence resolver ON
        var (coords, tris) = ArchFixture();
        int n = coords.Count;
        var snaps = new List<Frahan.Masonry.Interfaces.MeshSnapshot>(n);
        var ids = new List<string>(n);
        var blocks = new List<Frahan.Masonry.DataModel.MasonryBlock>(n);
        for (int i = 0; i < n; i++)
        {
            snaps.Add(new Frahan.Masonry.Interfaces.MeshSnapshot(coords[i], tris[i]));
            ids.Add("vouss_" + i.ToString("000"));
            blocks.Add(new Frahan.Masonry.DataModel.MasonryBlock(ids[i], coords[i], tris[i], 1.0));
        }
        var ifaces = Frahan.Masonry.Interfaces.MeshContactDetector.Detect(
            snaps, ids, distanceTol: 1e-3, angleTolDeg: 5.0,
            minContactPoints: 3, adaptiveToleranceFactor: 0.0, useCoplanarResolver: true);
        var bc = new Frahan.Masonry.DataModel.BoundaryConditions(new[] { ids[0], ids[n - 1] });
        var asm = new Frahan.Masonry.DataModel.MasonryAssembly(blocks, ifaces, bc);
        var vcounts = string.Join(",", asm.Interfaces.Select(i2 => i2.ContactPolygon.Count));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rbe = MasonryStabilityChecker.Check(asm, mu: 0.7, faceCount: 8, inscribed: true);
        long tR = sw.ElapsedMilliseconds; sw.Restart();
        var cra = CraStabilityChecker.Check(asm, mu: 0.7, faceCount: 8, inscribed: true);
        long tC = sw.ElapsedMilliseconds;
        Console.WriteLine($"      [kb9] arch RESOLVER on: ifaces {asm.Interfaces.Count} polyverts [{vcounts}] | " +
                          $"RBE {(rbe.IsStable ? "STABLE" : "FAIL")} {tR} ms | " +
                          $"CRA {(cra.IsStable ? "STABLE" : "FAIL")}{(cra.Certified ? " CERTIFIED" : "")} {tC} ms ({cra.Iterations} iter)");
    }

    public static void Kb9_Arch_CleanJoints()
    {
        // same arch, but with HANDMADE exact 4-vertex joints (the generator-
        // assembler style) instead of the detector polygons -> isolates the
        // detector-dirt hypothesis.
        var (coords, tris) = ArchFixture();
        int n = coords.Count;
        var blocks = new List<Frahan.Masonry.DataModel.MasonryBlock>(n);
        var ids = new List<string>(n);
        for (int i = 0; i < n; i++)
        {
            ids.Add("vouss_" + i.ToString("000"));
            blocks.Add(new Frahan.Masonry.DataModel.MasonryBlock(ids[i], coords[i], tris[i], 1.0));
        }
        var ifaces = new List<Frahan.Masonry.DataModel.MasonryInterface>(n - 1);
        for (int i = 0; i + 1 < n; i++)
        {
            // shared quad = vertices 4..7 of block i (its "top") == 0..3 of block i+1
            var c = coords[i];
            var quad = new List<Frahan.Masonry.DataModel.ContactVertex>(4);
            for (int k = 4; k < 8; k++)
                quad.Add(new Frahan.Masonry.DataModel.ContactVertex(c[k * 3], c[k * 3 + 1], c[k * 3 + 2]));
            // normal: from block i toward block i+1 = direction of decreasing angle.
            // plane spanned by (v1-v0, v3-v0); orient toward i+1 centroid
            double e1x = quad[1].X - quad[0].X, e1y = quad[1].Y - quad[0].Y, e1z = quad[1].Z - quad[0].Z;
            double e2x = quad[3].X - quad[0].X, e2y = quad[3].Y - quad[0].Y, e2z = quad[3].Z - quad[0].Z;
            double nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
            double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx /= nl; ny /= nl; nz /= nl;
            // centroid of block i+1 vs quad centre to orient A->B
            var c2 = coords[i + 1];
            double bx = 0, by = 0, bz = 0;
            for (int k = 0; k < 8; k++) { bx += c2[k * 3]; by += c2[k * 3 + 1]; bz += c2[k * 3 + 2]; }
            bx /= 8; by /= 8; bz /= 8;
            double qx = (quad[0].X + quad[1].X + quad[2].X + quad[3].X) / 4;
            double qy = (quad[0].Y + quad[1].Y + quad[2].Y + quad[3].Y) / 4;
            double qz = (quad[0].Z + quad[1].Z + quad[2].Z + quad[3].Z) / 4;
            if ((bx - qx) * nx + (by - qy) * ny + (bz - qz) * nz < 0) { nx = -nx; ny = -ny; nz = -nz; }
            // tangents: t1 = e1 normalised, t2 = n x t1
            double t1l = Math.Sqrt(e1x * e1x + e1y * e1y + e1z * e1z);
            double t1x = e1x / t1l, t1y = e1y / t1l, t1z = e1z / t1l;
            double t2x = ny * t1z - nz * t1y, t2y = nz * t1x - nx * t1z, t2z = nx * t1y - ny * t1x;
            ifaces.Add(new Frahan.Masonry.DataModel.MasonryInterface(
                ids[i], ids[i + 1], quad, nx, ny, nz, t1x, t1y, t1z, t2x, t2y, t2z));
        }
        var bc = new Frahan.Masonry.DataModel.BoundaryConditions(new[] { ids[0], ids[n - 1] });
        var asm = new Frahan.Masonry.DataModel.MasonryAssembly(blocks, ifaces, bc);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rbe = MasonryStabilityChecker.Check(asm, mu: 0.7, faceCount: 8, inscribed: true);
        long tR = sw.ElapsedMilliseconds; sw.Restart();
        var cra = CraStabilityChecker.Check(asm, mu: 0.7, faceCount: 8, inscribed: true);
        long tC = sw.ElapsedMilliseconds;
        Console.WriteLine($"      [kb9] arch CLEAN joints: RBE {(rbe.IsStable ? "STABLE" : "FAIL")} {tR} ms | " +
                          $"CRA {(cra.IsStable ? "STABLE" : "FAIL")}{(cra.Certified ? " CERTIFIED" : "")} {tC} ms ({cra.Iterations} iter) | {rbe.Message.Split('|')[0].Trim()}");
    }

    private static (List<IReadOnlyList<double>>, List<IReadOnlyList<int>>) ArchFixture()
    {
        // mirror of CraCompasParityTests.CompasArch(5, 10, 0.5, 0.5, 20)
        double height = 5, span = 10, thickness = 0.5, depth = 0.5; int n = 20;
        double radius = height / 2 + (span * span) / (8 * height);
        var center = new[] { 0.0, 0.0, height - radius };
        double vx = -span / 2 - center[0], vz = 0.0 - center[2];
        double springing = Math.Acos((-vx) / Math.Sqrt(vx * vx + vz * vz));
        double sector = Math.PI - 2 * springing;
        double angle = sector / n;
        var quad = new[]
        {
            new[] { 0.0, 0.0, height }, new[] { 0.0, depth, height },
            new[] { 0.0, depth, height + thickness }, new[] { 0.0, 0.0, height + thickness },
        };
        Func<double[][], double, double[][]> rot = (pts, aa) =>
        {
            double ca = Math.Cos(aa), sa = Math.Sin(aa);
            var op = new double[pts.Length][];
            for (int i = 0; i < pts.Length; i++)
            {
                double x = pts[i][0] - center[0], y = pts[i][1], z = pts[i][2] - center[2];
                op[i] = new[] { center[0] + ca * x + sa * z, y, center[2] - sa * x + ca * z };
            }
            return op;
        };
        var bottom = rot(quad, 0.5 * sector);
        var coords = new List<IReadOnlyList<double>>();
        var tris = new List<IReadOnlyList<int>>();
        int[][] quadFaces =
        {
            new[] { 0, 1, 2, 3 }, new[] { 7, 6, 5, 4 }, new[] { 3, 7, 4, 0 },
            new[] { 6, 2, 1, 5 }, new[] { 7, 3, 2, 6 }, new[] { 5, 1, 0, 4 },
        };
        for (int i = 0; i < n; i++)
        {
            var top = rot(bottom, -angle);
            var verts = new List<double>(24);
            foreach (var p2 in bottom) { verts.Add(p2[0]); verts.Add(p2[1]); verts.Add(p2[2]); }
            foreach (var p2 in top) { verts.Add(p2[0]); verts.Add(p2[1]); verts.Add(p2[2]); }
            var t = new List<int>(36);
            foreach (var f in quadFaces)
            { t.Add(f[0]); t.Add(f[1]); t.Add(f[2]); t.Add(f[0]); t.Add(f[2]); t.Add(f[3]); }
            coords.Add(verts); tris.Add(t);
            bottom = top;
        }
        return (coords, tris);
    }

    // min ||A x + b|| via ridge-regularised normal equations; returns ||Ax+b||/||b||
    private static double LeastSquaresResidual(double[,] a, double[] b)
    {
        int m = a.GetLength(0), n = a.GetLength(1);
        var ata = new double[n, n];
        var atb = new double[n];
        for (int i = 0; i < m; i++)
            for (int j = 0; j < n; j++)
            {
                double aij = a[i, j];
                if (aij == 0) continue;
                atb[j] += aij * b[i];
                for (int k = 0; k < n; k++) ata[j, k] += aij * a[i, k];
            }
        double tr = 0; for (int j = 0; j < n; j++) tr += ata[j, j];
        double lam = 1e-10 * Math.Max(tr / n, 1.0);
        for (int j = 0; j < n; j++) ata[j, j] += lam;
        var x = CholSolve(ata, atb, n);          // solves (AtA+lam) x = At b  -> minimises ||A x - b||... sign below
        // we want min ||A f + b||  ->  f = -x
        double rr = 0, bb = 0;
        for (int i = 0; i < m; i++)
        {
            double s = b[i];
            for (int j = 0; j < n; j++) s -= a[i, j] * x[j] * 1.0; // A(-x)+b = b - A x
            rr += s * s;
        }
        for (int i = 0; i < m; i++) bb += b[i] * b[i];
        return Math.Sqrt(rr) / Math.Max(Math.Sqrt(bb), 1e-30);
    }

    private static double[] CholSolve(double[,] m, double[] rhs, int n)
    {
        var l = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j <= i; j++)
            {
                double s = m[i, j];
                for (int k = 0; k < j; k++) s -= l[i, k] * l[j, k];
                if (i == j) l[i, i] = Math.Sqrt(Math.Max(s, 1e-30));
                else l[i, j] = s / l[j, j];
            }
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = rhs[i];
            for (int k = 0; k < i; k++) s -= l[i, k] * y[k];
            y[i] = s / l[i, i];
        }
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = y[i];
            for (int k = i + 1; k < n; k++) s -= l[k, i] * x[k];
            x[i] = s / l[i, i];
        }
        return x;
    }

    private static (IReadOnlyList<double>, IReadOnlyList<int>) Box(double[] c, double sx, double sy, double sz)
    {
        double hx = sx / 2, hy = sy / 2, hz = sz / 2;
        var v = new List<double>(24);
        foreach (var dz in new[] { -hz, hz })
            foreach (var (dx, dy) in new[] { (-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy) })
            { v.Add(c[0] + dx); v.Add(c[1] + dy); v.Add(c[2] + dz); }
        var t = new List<int>
        {
            0,2,1, 0,3,2,  4,5,6, 4,6,7,
            0,1,5, 0,5,4,  2,3,7, 2,7,6,
            0,4,7, 0,7,3,  1,2,6, 1,6,5,
        };
        return (v, t);
    }

    private static IReadOnlyList<double> RotY(IReadOnlyList<double> coords, double a)
    {
        double ca = Math.Cos(a), sa = Math.Sin(a);
        var o = new double[coords.Count];
        for (int i = 0; i + 2 < coords.Count; i += 3)
        {
            double x = coords[i], y = coords[i + 1], z = coords[i + 2];
            o[i] = ca * x + sa * z; o[i + 1] = y; o[i + 2] = -sa * x + ca * z;
        }
        return o;
    }
}
