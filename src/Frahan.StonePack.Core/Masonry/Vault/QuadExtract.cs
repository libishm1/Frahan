#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry.Vault
{
    // =========================================================================
    // QuadExtract — Stage C of the thrust-following remesher. Given the Stage-B
    // field-aligned parametrization (u,v per vertex), lift the integer grid of the
    // (u,v) plane back onto the surface: each lattice node (i,j) that lands inside
    // a uv-triangle is placed in 3D by barycentric interpolation, and unit cells
    // whose four corners all resolved become quad faces. The result is a quad mesh
    // whose edges follow the combed cross-field (i.e. the thrust directions).
    //
    // This inverse-map route is exact for a SINGLE chart (no interior singularity):
    // the uv map is injective, so lattice-node location is unambiguous. A per-uv-
    // triangle signed-area scan reports folds (negative-area triangles) — a nonzero
    // FlippedTriangles count means the chart was not injective (an unhandled cone),
    // and the extraction there is unreliable until Stage B2 cuts the seam.
    // Spec: outputs/2026-06-30/thrust_remesh/HANDOFF_IMPLEMENTATION.md §4 (Route 1).
    // =========================================================================
    public sealed class QuadExtractResult
    {
        public Mesh Quads;
        public int QuadCount;
        public int NodesPlaced;
        public int NodesTotal;
        public int FlippedTriangles;   // uv-triangles with negative signed area (fold indicator)
        public int TriangleCount;
        public string Message = "";
    }

    public static class QuadExtract
    {
        /// <summary>
        /// triMesh: welded triangle mesh. U,V: per-vertex parametrization (Stage B).
        /// The integer lattice of (u,v) is lifted back onto the surface as a quad mesh.
        /// </summary>
        public static QuadExtractResult Extract(Mesh triMesh, double[] U, double[] V)
        {
            int nv = triMesh.Vertices.Count;
            var P = new Point3d[nv];
            for (int i = 0; i < nv; i++) P[i] = triMesh.Vertices[i];
            int nf = triMesh.Faces.Count;

            // uv bounding box + fold scan
            double umin = double.MaxValue, umax = -double.MaxValue, vmin = double.MaxValue, vmax = -double.MaxValue;
            for (int i = 0; i < nv; i++)
            {
                if (U[i] < umin) umin = U[i]; if (U[i] > umax) umax = U[i];
                if (V[i] < vmin) vmin = V[i]; if (V[i] > vmax) vmax = V[i];
            }
            int flipped = 0;
            for (int f = 0; f < nf; f++)
            {
                var mf = triMesh.Faces[f];
                double area = SignedUvArea(U[mf.A], V[mf.A], U[mf.B], V[mf.B], U[mf.C], V[mf.C]);
                if (area < 0) flipped++;
            }

            int i0 = (int)Math.Ceiling(umin - 1e-9), i1 = (int)Math.Floor(umax + 1e-9);
            int j0 = (int)Math.Ceiling(vmin - 1e-9), j1 = (int)Math.Floor(vmax + 1e-9);
            int nu = i1 - i0 + 1, nvv = j1 - j0 + 1;
            if (nu < 2 || nvv < 2)
                return new QuadExtractResult { Quads = new Mesh(), TriangleCount = nf, FlippedTriangles = flipped,
                    Message = "uv range too small for an integer grid" };

            // locate each lattice node inside a uv-triangle -> 3D via barycentric
            var node = new Point3d[nu, nvv];
            var have = new bool[nu, nvv];
            int placed = 0;
            for (int a = 0; a < nu; a++)
                for (int b = 0; b < nvv; b++)
                {
                    double gu = i0 + a, gv = j0 + b;
                    if (LocateAndLift(triMesh, P, U, V, gu, gv, out Point3d pos)) { node[a, b] = pos; have[a, b] = true; placed++; }
                }

            // stitch unit cells whose four corners resolved
            var quad = new Mesh();
            var idx = new int[nu, nvv];
            for (int a = 0; a < nu; a++) for (int b = 0; b < nvv; b++) idx[a, b] = -1;
            int qc = 0;
            for (int a = 0; a < nu - 1; a++)
                for (int b = 0; b < nvv - 1; b++)
                {
                    if (have[a, b] && have[a + 1, b] && have[a + 1, b + 1] && have[a, b + 1])
                    {
                        int i00 = Ensure(quad, node, idx, a, b);
                        int i10 = Ensure(quad, node, idx, a + 1, b);
                        int i11 = Ensure(quad, node, idx, a + 1, b + 1);
                        int i01 = Ensure(quad, node, idx, a, b + 1);
                        quad.Faces.AddFace(i00, i10, i11, i01);
                        qc++;
                    }
                }
            quad.Normals.ComputeNormals();
            quad.Compact();

            return new QuadExtractResult
            {
                Quads = quad, QuadCount = qc, NodesPlaced = placed, NodesTotal = nu * nvv,
                FlippedTriangles = flipped, TriangleCount = nf,
                Message = flipped == 0 ? "ok (single chart)" : $"{flipped} flipped uv-triangles — chart not injective (needs seam)",
            };
        }

        private static int Ensure(Mesh m, Point3d[,] node, int[,] idx, int a, int b)
        {
            if (idx[a, b] < 0) idx[a, b] = m.Vertices.Add(node[a, b]);
            return idx[a, b];
        }

        private static double SignedUvArea(double ax, double ay, double bx, double by, double cx, double cy)
            => 0.5 * ((bx - ax) * (cy - ay) - (cx - ax) * (by - ay));

        // find the triangle whose uv-image contains (gu,gv); lift to 3D by barycentric interp
        private static bool LocateAndLift(Mesh m, Point3d[] P, double[] U, double[] V, double gu, double gv, out Point3d pos)
        {
            pos = Point3d.Origin;
            int nf = m.Faces.Count;
            for (int f = 0; f < nf; f++)
            {
                var mf = m.Faces[f];
                double ux = U[mf.A], uy = V[mf.A], vx = U[mf.B], vy = V[mf.B], wx = U[mf.C], wy = V[mf.C];
                double d = (vy - wy) * (ux - wx) + (wx - vx) * (uy - wy);
                if (Math.Abs(d) < 1e-14) continue;
                double la = ((vy - wy) * (gu - wx) + (wx - vx) * (gv - wy)) / d;
                double lb = ((wy - uy) * (gu - wx) + (ux - wx) * (gv - wy)) / d;
                double lc = 1.0 - la - lb;
                const double e = -1e-7;
                if (la >= e && lb >= e && lc >= e)
                {
                    pos = new Point3d(
                        la * P[mf.A].X + lb * P[mf.B].X + lc * P[mf.C].X,
                        la * P[mf.A].Y + lb * P[mf.B].Y + lc * P[mf.C].Y,
                        la * P[mf.A].Z + lb * P[mf.B].Z + lc * P[mf.C].Z);
                    return true;
                }
            }
            return false;
        }
    }
}
