#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// JointSetDfnPlyEmitter -- convert the infinite FracturePlanes produced by
// JointSetDfnGenerator into finite triangulated fracture polygons clipped to
// a bench AABB, packaged as a single PlyMesh consumable by BlockCutOptSolver.
//
// Algorithm (plane vs AABB polygon extraction):
//   1. Evaluate the signed distance of each AABB corner to the plane.
//   2. For each of the 12 AABB edges, if endpoint signs differ, compute the
//      intersection point.
//   3. Order the intersection points around the plane normal in a stable 2D
//      basis derived from the normal.
//   4. Fan-triangulate the ordered polygon.
//
// All output triangles live on the plane and stay inside the bench AABB.
// This is the synthetic-DFN ingestion path called out in
// `D:\code_ws\wiki\papers\equations_and_diagrams\09_dataset_reproduction_report.md`
// section 7 and the next-step entry of `10_consensus_update_and_forward_plan.md`.
// =============================================================================

public static class JointSetDfnPlyEmitter
{
    /// <summary>
    /// Emit a single PlyMesh containing every fracture plane clipped to the
    /// bench AABB. Planes that do not intersect the AABB are skipped.
    /// </summary>
    public static PlyMesh Emit(IReadOnlyList<FracturePlane> planes, BoundingBox3 bench)
    {
        if (planes == null) throw new ArgumentNullException(nameof(planes));
        if (bench == null) throw new ArgumentNullException(nameof(bench));

        var verts = new List<double>(planes.Count * 18);
        var tris = new List<int>(planes.Count * 12);

        for (int i = 0; i < planes.Count; i++)
        {
            var plane = planes[i];
            if (plane == null) continue;
            AppendClippedPolygon(plane, bench, verts, tris);
        }

        if (tris.Count == 0)
        {
            // emit a single far-away degenerate triangle so the PlyMesh
            // constructor (which requires at least one triangle) is happy
            verts.Add(1e9); verts.Add(1e9); verts.Add(1e9);
            verts.Add(1e9 + 1e-3); verts.Add(1e9); verts.Add(1e9);
            verts.Add(1e9); verts.Add(1e9 + 1e-3); verts.Add(1e9);
            tris.Add(0); tris.Add(1); tris.Add(2);
        }

        return new PlyMesh(verts, tris, null);
    }

    private static void AppendClippedPolygon(
        FracturePlane plane,
        BoundingBox3 bench,
        List<double> verts,
        List<int> tris)
    {
        // 8 AABB corners
        double[] cx = { bench.MinX, bench.MaxX, bench.MaxX, bench.MinX, bench.MinX, bench.MaxX, bench.MaxX, bench.MinX };
        double[] cy = { bench.MinY, bench.MinY, bench.MaxY, bench.MaxY, bench.MinY, bench.MinY, bench.MaxY, bench.MaxY };
        double[] cz = { bench.MinZ, bench.MinZ, bench.MinZ, bench.MinZ, bench.MaxZ, bench.MaxZ, bench.MaxZ, bench.MaxZ };
        double[] d = new double[8];
        for (int k = 0; k < 8; k++)
            d[k] = plane.SignedDistance(cx[k], cy[k], cz[k]);

        // 12 AABB edges: (cornerA, cornerB)
        int[,] edges =
        {
            {0,1},{1,2},{2,3},{3,0},   // bottom face
            {4,5},{5,6},{6,7},{7,4},   // top face
            {0,4},{1,5},{2,6},{3,7}    // verticals
        };

        var px = new List<double>();
        var py = new List<double>();
        var pz = new List<double>();

        for (int e = 0; e < 12; e++)
        {
            int a = edges[e, 0], b = edges[e, 1];
            double da = d[a], db = d[b];
            // strictly crossing or touching one side
            if ((da > 0 && db < 0) || (da < 0 && db > 0))
            {
                double t = da / (da - db);
                px.Add(cx[a] + t * (cx[b] - cx[a]));
                py.Add(cy[a] + t * (cy[b] - cy[a]));
                pz.Add(cz[a] + t * (cz[b] - cz[a]));
            }
            else if (da == 0.0)
            {
                px.Add(cx[a]); py.Add(cy[a]); pz.Add(cz[a]);
            }
        }

        if (px.Count < 3) return;

        // dedupe by tight tolerance
        const double dedupeTol = 1e-9;
        var uX = new List<double>(); var uY = new List<double>(); var uZ = new List<double>();
        for (int i = 0; i < px.Count; i++)
        {
            bool dup = false;
            for (int j = 0; j < uX.Count; j++)
            {
                if (Math.Abs(uX[j] - px[i]) < dedupeTol
                    && Math.Abs(uY[j] - py[i]) < dedupeTol
                    && Math.Abs(uZ[j] - pz[i]) < dedupeTol)
                {
                    dup = true; break;
                }
            }
            if (!dup) { uX.Add(px[i]); uY.Add(py[i]); uZ.Add(pz[i]); }
        }
        if (uX.Count < 3) return;

        // build a 2D in-plane basis from the plane normal
        double nx = plane.NormalX, ny = plane.NormalY, nz = plane.NormalZ;
        double ex, ey, ez;
        if (Math.Abs(nz) < 0.9) { ex = -ny; ey = nx; ez = 0.0; }
        else                    { ex =  1.0; ey = 0.0; ez = 0.0; }
        double el = Math.Sqrt(ex * ex + ey * ey + ez * ez);
        if (el < 1e-12) return;
        ex /= el; ey /= el; ez /= el;
        double fx = ny * ez - nz * ey;
        double fy = nz * ex - nx * ez;
        double fz = nx * ey - ny * ex;

        // centroid in 3D
        double sx = 0, sy = 0, sz = 0;
        for (int i = 0; i < uX.Count; i++) { sx += uX[i]; sy += uY[i]; sz += uZ[i]; }
        sx /= uX.Count; sy /= uX.Count; sz /= uX.Count;

        // angle around centroid in the (e, f) basis
        var ang = new double[uX.Count];
        for (int i = 0; i < uX.Count; i++)
        {
            double rx = uX[i] - sx, ry = uY[i] - sy, rz = uZ[i] - sz;
            double u = rx * ex + ry * ey + rz * ez;
            double w = rx * fx + ry * fy + rz * fz;
            ang[i] = Math.Atan2(w, u);
        }
        var order = new int[uX.Count];
        for (int i = 0; i < uX.Count; i++) order[i] = i;
        Array.Sort(order, (a, b) => ang[a].CompareTo(ang[b]));

        int v0 = verts.Count / 3;
        for (int i = 0; i < order.Length; i++)
        {
            verts.Add(uX[order[i]]);
            verts.Add(uY[order[i]]);
            verts.Add(uZ[order[i]]);
        }
        // fan-triangulate from order[0]
        for (int i = 1; i < order.Length - 1; i++)
        {
            tris.Add(v0);
            tris.Add(v0 + i);
            tris.Add(v0 + i + 1);
        }
    }
}
