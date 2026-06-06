#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Interfaces;

// =============================================================================
// InterfaceAutoDetector — given a list of convex Slabs in their final placed
// positions, finds face-face contacts and emits the corresponding
// MasonryInterfaces. This closes the gap that HANDOFF_TO_CLAUDE.md flagged
// as P3: today MasonryAssemblyComponent demands hand-authored interfaces;
// after this lands, a list of Slabs (or cut quarry pieces) can flow straight
// into MasonryAssembly.
//
// Contact convention (matches MasonryInterface):
//   - normal points FROM block A INTO block B
//   - tangent1 / tangent2 form a right-handed (n, t1, t2) basis
//   - contact polygon ordered CCW looking from B toward A
//
// Algorithm (per pair of blocks i, j with i < j):
//   1. For each face fA in slab i and each face fB in slab j:
//   2.   Skip if normals are not antiparallel within angleTol.
//   3.   Skip if fB's centroid lies further than distanceTol off fA's plane.
//   4.   Project both polygons to 2D using a basis on fA's plane.
//   5.   Reorient fB's projected polygon so it's CCW in that 2D basis
//        (its native CCW from outside B inverts the orientation in our
//        basis since nB = -nA).
//   6.   Sutherland-Hodgman convex-polygon clip of fB against fA.
//   7.   If the intersection polygon has area > eps, emit a MasonryInterface
//        with A=block i, B=block j, normal = fA outward normal.
//
// Power-of-10 hardened along the same lines as AshlarLayoutEngine: small
// helpers, all loops bounded, every helper validates inputs.
// =============================================================================

public static class InterfaceAutoDetector
{
    private const double DefaultDistanceTol = 1e-4;
    private const double DefaultAngleTolDeg = 1.0;
    private const double AreaEps = 1e-9;
    private const int MaxOutputVertices = 32;

    /// <summary>
    /// Auto-detect contacts between every pair of slabs in
    /// <paramref name="slabs"/>. Returns the corresponding
    /// MasonryInterfaces. <paramref name="blockIds"/> must have the same
    /// length as <paramref name="slabs"/> (the IDs the caller will assign
    /// to the resulting MasonryBlocks).
    /// </summary>
    public static IReadOnlyList<MasonryInterface> Detect(
        IReadOnlyList<Slab> slabs,
        IReadOnlyList<string> blockIds,
        double distanceTol = DefaultDistanceTol,
        double angleTolDeg = DefaultAngleTolDeg)
    {
        if (slabs == null) throw new ArgumentNullException(nameof(slabs));
        if (blockIds == null) throw new ArgumentNullException(nameof(blockIds));
        if (slabs.Count != blockIds.Count)
            throw new ArgumentException(
                $"slabs.Count ({slabs.Count}) must equal blockIds.Count ({blockIds.Count})",
                nameof(blockIds));
        if (distanceTol < 0.0)
            throw new ArgumentOutOfRangeException(nameof(distanceTol), "must be >= 0");
        if (!(angleTolDeg >= 0.0 && angleTolDeg < 90.0))
            throw new ArgumentOutOfRangeException(nameof(angleTolDeg), "must be in [0, 90)");

        for (int i = 0; i < blockIds.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(blockIds[i]))
                throw new ArgumentException($"blockIds[{i}] is blank", nameof(blockIds));
        }

        double cosAngleTol = Math.Cos(angleTolDeg * Math.PI / 180.0);
        int n = slabs.Count;
        var result = new List<MasonryInterface>(n * 2 + 8);

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                DetectPair(slabs[i], slabs[j], blockIds[i], blockIds[j],
                    distanceTol, cosAngleTol, result);
            }
        }
        if (result.Count < 0)
            throw new InvalidOperationException("interface count went negative");
        return result;
    }

    // ─── Per-pair driver ─────────────────────────────────────────────────────

    private static void DetectPair(
        Slab a, Slab b, string idA, string idB,
        double distanceTol, double cosAngleTol,
        List<MasonryInterface> sink)
    {
        if (a == null) throw new ArgumentNullException(nameof(a));
        if (b == null) throw new ArgumentNullException(nameof(b));
        if (sink == null) throw new ArgumentNullException(nameof(sink));

        for (int fa = 0; fa < a.FaceCount; fa++)
        {
            for (int fb = 0; fb < b.FaceCount; fb++)
            {
                TryEmitContact(a, fa, b, fb, idA, idB, distanceTol, cosAngleTol, sink);
            }
        }
    }

    private static void TryEmitContact(
        Slab a, int faceA, Slab b, int faceB,
        string idA, string idB,
        double distanceTol, double cosAngleTol,
        List<MasonryInterface> sink)
    {
        if (a == null) throw new ArgumentNullException(nameof(a));
        if (b == null) throw new ArgumentNullException(nameof(b));
        if (sink == null) throw new ArgumentNullException(nameof(sink));

        if (!ComputeFacePlane(a, faceA, out double aPx, out double aPy, out double aPz,
                                          out double aNx, out double aNy, out double aNz))
            return;
        if (!ComputeFacePlane(b, faceB, out double bPx, out double bPy, out double bPz,
                                          out double bNx, out double bNy, out double bNz))
            return;

        double dot = aNx * bNx + aNy * bNy + aNz * bNz;
        if (!(dot <= -cosAngleTol)) return;  // not antiparallel within tolerance

        ComputeFaceCentroid(b, faceB, out double bCx, out double bCy, out double bCz);
        double offset = (bCx - aPx) * aNx + (bCy - aPy) * aNy + (bCz - aPz) * aNz;
        if (Math.Abs(offset) > distanceTol) return;

        BuildPlanarBasis(aNx, aNy, aNz, out double ux, out double uy, out double uz,
                                         out double vx, out double vy, out double vz);

        var poly2A = ProjectFaceTo2D(a, faceA, aPx, aPy, aPz, ux, uy, uz, vx, vy, vz);
        var poly2B = ProjectFaceTo2D(b, faceB, aPx, aPy, aPz, ux, uy, uz, vx, vy, vz);
        EnsureCcw(poly2A);
        EnsureCcw(poly2B);

        var clipped = ClipConvexPolygon(poly2B, poly2A);
        if (clipped.Count < 3) return;
        if (PolygonArea2D(clipped) < AreaEps) return;

        var poly3 = LiftTo3D(clipped, aPx, aPy, aPz, ux, uy, uz, vx, vy, vz);
        var contactVerts = new ContactVertex[poly3.Count / 3];
        for (int k = 0; k < contactVerts.Length; k++)
        {
            contactVerts[k] = new ContactVertex(poly3[3 * k + 0], poly3[3 * k + 1], poly3[3 * k + 2]);
        }
        sink.Add(new MasonryInterface(
            idA, idB, contactVerts,
            aNx, aNy, aNz,
            ux, uy, uz,
            vx, vy, vz));
    }

    // ─── Geometry helpers ────────────────────────────────────────────────────

    private static bool ComputeFacePlane(
        Slab s, int faceIdx,
        out double px, out double py, out double pz,
        out double nx, out double ny, out double nz)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        if (faceIdx < 0 || faceIdx >= s.FaceCount)
            throw new ArgumentOutOfRangeException(nameof(faceIdx));

        var face = s.Faces[faceIdx];
        var v = s.VertexCoordsXyz;
        if (face.Count < 3)
        {
            px = py = pz = nx = ny = nz = 0.0;
            return false;
        }

        int v0 = face[0];
        px = v[3 * v0 + 0]; py = v[3 * v0 + 1]; pz = v[3 * v0 + 2];

        // Walk the polygon until a non-degenerate triple exists.
        for (int k = 1; k + 1 < face.Count; k++)
        {
            int v1 = face[k];
            int v2 = face[k + 1];
            double ax = v[3 * v1 + 0] - px, ay = v[3 * v1 + 1] - py, az = v[3 * v1 + 2] - pz;
            double bx = v[3 * v2 + 0] - px, by = v[3 * v2 + 1] - py, bz = v[3 * v2 + 2] - pz;
            double cx = ay * bz - az * by;
            double cy = az * bx - ax * bz;
            double cz = ax * by - ay * bx;
            double m2 = cx * cx + cy * cy + cz * cz;
            if (m2 < 1e-24) continue;
            double inv = 1.0 / Math.Sqrt(m2);
            nx = cx * inv; ny = cy * inv; nz = cz * inv;
            return true;
        }
        nx = ny = nz = 0.0;
        return false;
    }

    private static void ComputeFaceCentroid(
        Slab s, int faceIdx,
        out double cx, out double cy, out double cz)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        if (faceIdx < 0 || faceIdx >= s.FaceCount)
            throw new ArgumentOutOfRangeException(nameof(faceIdx));

        var face = s.Faces[faceIdx];
        var v = s.VertexCoordsXyz;
        cx = cy = cz = 0.0;
        for (int k = 0; k < face.Count; k++)
        {
            int vi = face[k];
            cx += v[3 * vi + 0]; cy += v[3 * vi + 1]; cz += v[3 * vi + 2];
        }
        if (face.Count <= 0)
            throw new InvalidOperationException("face is empty");
        double inv = 1.0 / face.Count;
        cx *= inv; cy *= inv; cz *= inv;
    }

    private static void BuildPlanarBasis(
        double nx, double ny, double nz,
        out double ux, out double uy, out double uz,
        out double vx, out double vy, out double vz)
    {
        // Pick a seed not parallel to n, cross to get u, then v = n × u.
        double sx, sy, sz;
        if (Math.Abs(nz) < 0.9) { sx = 0; sy = 0; sz = 1; }
        else                    { sx = 1; sy = 0; sz = 0; }
        ux = sy * nz - sz * ny;
        uy = sz * nx - sx * nz;
        uz = sx * ny - sy * nx;
        double um = Math.Sqrt(ux * ux + uy * uy + uz * uz);
        if (um < 1e-20)
            throw new InvalidOperationException("planar basis seed parallel to normal");
        ux /= um; uy /= um; uz /= um;
        vx = ny * uz - nz * uy;
        vy = nz * ux - nx * uz;
        vz = nx * uy - ny * ux;
    }

    private static List<double> ProjectFaceTo2D(
        Slab s, int faceIdx,
        double aPx, double aPy, double aPz,
        double ux, double uy, double uz,
        double vx, double vy, double vz)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        var face = s.Faces[faceIdx];
        var v = s.VertexCoordsXyz;
        var result = new List<double>(face.Count * 2);
        for (int k = 0; k < face.Count; k++)
        {
            int vi = face[k];
            double dx = v[3 * vi + 0] - aPx;
            double dy = v[3 * vi + 1] - aPy;
            double dz = v[3 * vi + 2] - aPz;
            double pu = dx * ux + dy * uy + dz * uz;
            double pv = dx * vx + dy * vy + dz * vz;
            result.Add(pu);
            result.Add(pv);
        }
        if (result.Count != face.Count * 2)
            throw new InvalidOperationException("2D projection size mismatch");
        return result;
    }

    private static List<double> LiftTo3D(
        List<double> poly2,
        double aPx, double aPy, double aPz,
        double ux, double uy, double uz,
        double vx, double vy, double vz)
    {
        if (poly2 == null) throw new ArgumentNullException(nameof(poly2));
        if (poly2.Count % 2 != 0)
            throw new ArgumentException("poly2 length must be even", nameof(poly2));
        int n = poly2.Count / 2;
        var result = new List<double>(n * 3);
        for (int k = 0; k < n; k++)
        {
            double pu = poly2[2 * k + 0];
            double pv = poly2[2 * k + 1];
            double x = aPx + pu * ux + pv * vx;
            double y = aPy + pu * uy + pv * vy;
            double z = aPz + pu * uz + pv * vz;
            result.Add(x); result.Add(y); result.Add(z);
        }
        return result;
    }

    private static double SignedArea2D(List<double> poly2)
    {
        if (poly2 == null) throw new ArgumentNullException(nameof(poly2));
        int n = poly2.Count / 2;
        if (n < 3) return 0.0;
        double a = 0.0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double x0 = poly2[2 * i + 0], y0 = poly2[2 * i + 1];
            double x1 = poly2[2 * j + 0], y1 = poly2[2 * j + 1];
            a += x0 * y1 - x1 * y0;
        }
        return 0.5 * a;
    }

    private static double PolygonArea2D(List<double> poly2)
    {
        return Math.Abs(SignedArea2D(poly2));
    }

    private static void EnsureCcw(List<double> poly2)
    {
        if (poly2 == null) throw new ArgumentNullException(nameof(poly2));
        if (SignedArea2D(poly2) < 0.0)
        {
            ReverseInPlace2D(poly2);
        }
    }

    private static void ReverseInPlace2D(List<double> poly2)
    {
        if (poly2 == null) throw new ArgumentNullException(nameof(poly2));
        int n = poly2.Count / 2;
        for (int i = 0, j = n - 1; i < j; i++, j--)
        {
            double tx = poly2[2 * i + 0], ty = poly2[2 * i + 1];
            poly2[2 * i + 0] = poly2[2 * j + 0];
            poly2[2 * i + 1] = poly2[2 * j + 1];
            poly2[2 * j + 0] = tx;
            poly2[2 * j + 1] = ty;
        }
    }

    // ─── Sutherland-Hodgman convex polygon clipping (2D) ─────────────────────

    private static List<double> ClipConvexPolygon(List<double> subject, List<double> clip)
    {
        if (subject == null) throw new ArgumentNullException(nameof(subject));
        if (clip == null) throw new ArgumentNullException(nameof(clip));

        var output = new List<double>(subject);
        int clipCount = clip.Count / 2;
        for (int e = 0; e < clipCount; e++)
        {
            int e2 = (e + 1) % clipCount;
            double e1x = clip[2 * e + 0], e1y = clip[2 * e + 1];
            double e2x = clip[2 * e2 + 0], e2y = clip[2 * e2 + 1];

            var input = output;
            output = new List<double>(input.Count + 4);
            int inN = input.Count / 2;
            if (inN == 0) break;

            double sx = input[2 * (inN - 1) + 0];
            double sy = input[2 * (inN - 1) + 1];
            for (int k = 0; k < inN; k++)
            {
                double px = input[2 * k + 0];
                double py = input[2 * k + 1];
                bool sIn = IsInsideEdge(sx, sy, e1x, e1y, e2x, e2y);
                bool pIn = IsInsideEdge(px, py, e1x, e1y, e2x, e2y);
                if (pIn)
                {
                    if (!sIn)
                    {
                        EdgeIntersect(sx, sy, px, py, e1x, e1y, e2x, e2y,
                            out double ix, out double iy);
                        output.Add(ix); output.Add(iy);
                    }
                    output.Add(px); output.Add(py);
                }
                else if (sIn)
                {
                    EdgeIntersect(sx, sy, px, py, e1x, e1y, e2x, e2y,
                        out double ix, out double iy);
                    output.Add(ix); output.Add(iy);
                }
                sx = px; sy = py;
                if (output.Count / 2 > MaxOutputVertices) break;
            }
        }
        return output;
    }

    private static bool IsInsideEdge(
        double px, double py,
        double e1x, double e1y,
        double e2x, double e2y)
    {
        double cross = (e2x - e1x) * (py - e1y) - (e2y - e1y) * (px - e1x);
        return cross >= -1e-12;  // CCW polygon: inside = cross >= 0
    }

    private static void EdgeIntersect(
        double sx, double sy, double px, double py,
        double e1x, double e1y, double e2x, double e2y,
        out double ix, out double iy)
    {
        // Solve line A: e1 + s1*(e2-e1)  ==  line B: s + t*(p-s).
        // From (sx-e1x, sy-e1y) = s1*(dx1, dy1) - t*(dx2, dy2):
        //   t = ((sx-e1x)*dy1 - (sy-e1y)*dx1) / (dx1*dy2 - dy1*dx2).
        double dx1 = e2x - e1x, dy1 = e2y - e1y;
        double dx2 = px - sx,  dy2 = py - sy;
        double denom = dx1 * dy2 - dy1 * dx2;
        if (Math.Abs(denom) < 1e-15)
        {
            ix = sx; iy = sy; return;
        }
        double t = ((sx - e1x) * dy1 - (sy - e1y) * dx1) / denom;
        ix = sx + t * dx2;
        iy = sy + t * dy2;
    }
}
