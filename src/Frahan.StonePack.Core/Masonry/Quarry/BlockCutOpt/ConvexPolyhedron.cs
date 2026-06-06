#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// ConvexPolyhedron -- minimal CPH representation for the Shao 2022 in-block
// planner. Stored as a vertex list + faces (each face a list of vertex
// indices in CCW order viewed from outside). All units in metres.
//
// Supports:
//   - Construction from an OrientedBlock (the natural starting blank).
//   - Half-space clip: given a plane (point + outward normal), keep the
//     half-space on the inside of the normal. Quickhull-free; uses the
//     classical Sutherland-Hodgman extension to 3D (per-face clipping then
//     boundary reconstruction).
//   - Volume computation by signed-tet sum.
//   - Vertex enumeration.
//
// Phase 9 of the synthesis roadmap.
// =============================================================================

public sealed class ConvexPolyhedron
{
    private readonly List<(double X, double Y, double Z)> _verts;
    private readonly List<int[]> _faces; // each face = ordered vertex indices (CCW from outside)

    private ConvexPolyhedron(List<(double X, double Y, double Z)> verts, List<int[]> faces)
    {
        _verts = verts;
        _faces = faces;
    }

    public IReadOnlyList<(double X, double Y, double Z)> Vertices => _verts;
    public IReadOnlyList<int[]> Faces => _faces;

    /// <summary>
    /// Build a CPH from an OrientedBlock. The resulting box has 8 vertices and
    /// 6 quad faces with outward CCW normals.
    /// </summary>
    public static ConvexPolyhedron FromOrientedBlock(in OrientedBlock obb)
    {
        double cx = obb.CenterX, cy = obb.CenterY, cz = obb.CenterZ;
        double uX = obb.UX, uY = obb.UY, uZ = obb.UZ;
        double vX = obb.VX, vY = obb.VY, vZ = obb.VZ;
        double wX = obb.WX, wY = obb.WY, wZ = obb.WZ;
        double hx = obb.HalfX, hy = obb.HalfY, hz = obb.HalfZ;

        // 8 corners: combinations of +/- (hx*u + hy*v + hz*w). I1: full 3D.
        var verts = new List<(double X, double Y, double Z)>(8);
        for (int k = 0; k < 8; k++)
        {
            double sx = ((k & 1) != 0 ? +1 : -1) * hx;
            double sy = ((k & 2) != 0 ? +1 : -1) * hy;
            double sz = ((k & 4) != 0 ? +1 : -1) * hz;
            verts.Add((
                cx + sx * uX + sy * vX + sz * wX,
                cy + sx * uY + sy * vY + sz * wY,
                cz + sx * uZ + sy * vZ + sz * wZ));
        }

        // 6 quad faces, CCW from outside.
        // Vertex index bit layout: bit 0 = u-sign, bit 1 = v-sign, bit 2 = w-sign.
        var faces = new List<int[]>
        {
            new[] { 0, 4, 6, 2 }, // -u face (CCW from -u)
            new[] { 1, 3, 7, 5 }, // +u face
            new[] { 0, 1, 5, 4 }, // -v face
            new[] { 2, 6, 7, 3 }, // +v face
            new[] { 0, 2, 3, 1 }, // -w face (bottom)
            new[] { 4, 5, 7, 6 }, // +w face (top)
        };

        return new ConvexPolyhedron(verts, faces);
    }

    /// <summary>
    /// Signed volume by tet-fan from origin. Faces must be CCW from outside.
    /// </summary>
    public double Volume()
    {
        double sum = 0.0;
        for (int f = 0; f < _faces.Count; f++)
        {
            var face = _faces[f];
            // fan-triangulate the face from vertex 0
            for (int i = 1; i < face.Length - 1; i++)
            {
                var a = _verts[face[0]];
                var b = _verts[face[i]];
                var c = _verts[face[i + 1]];
                sum += SignedTetVolume(a, b, c);
            }
        }
        return Math.Abs(sum);
    }

    private static double SignedTetVolume(
        (double X, double Y, double Z) a,
        (double X, double Y, double Z) b,
        (double X, double Y, double Z) c)
    {
        // dot(a, cross(b, c)) / 6
        double cx = b.Y * c.Z - b.Z * c.Y;
        double cy = b.Z * c.X - b.X * c.Z;
        double cz = b.X * c.Y - b.Y * c.X;
        return (a.X * cx + a.Y * cy + a.Z * cz) / 6.0;
    }

    /// <summary>
    /// Clip this CPH by the half-space { x : dot(x - planePoint, planeNormal) <= 0 }.
    /// In words: keep the portion behind the plane (opposite to the normal).
    /// Returns a new CPH; this instance is unchanged. If the CPH is entirely
    /// in front of the plane the result has zero faces and zero volume.
    ///
    /// For CONVEX polyhedra (which is what Frahan.AmrrPlanner produces) this
    /// implementation is correct and O(V*F). For non-convex meshes use the
    /// CGAL mesh-difference path via NativeBridge instead -- this clipper
    /// will not handle non-convex outputs.
    /// </summary>
    public ConvexPolyhedron ClipByHalfSpace(
        double pX, double pY, double pZ,
        double nX, double nY, double nZ)
    {
        double n2 = nX * nX + nY * nY + nZ * nZ;
        if (n2 < BlockCutOptTolerances.GeometricEps)
            throw new ArgumentException("plane normal must be non-zero");
        double invN = 1.0 / Math.Sqrt(n2);
        nX *= invN; nY *= invN; nZ *= invN;

        // signed distances (positive = on the side that gets discarded)
        int nv = _verts.Count;
        var d = new double[nv];
        for (int i = 0; i < nv; i++)
        {
            var p = _verts[i];
            d[i] = (p.X - pX) * nX + (p.Y - pY) * nY + (p.Z - pZ) * nZ;
        }

        // pass 1: emit kept original vertices + intersection points (shared across faces)
        var newVerts = new List<(double X, double Y, double Z)>(nv + _faces.Count);
        var oldToNew = new int[nv];
        for (int i = 0; i < nv; i++) oldToNew[i] = -1;
        for (int i = 0; i < nv; i++)
        {
            if (d[i] <= 0.0)
            {
                oldToNew[i] = newVerts.Count;
                newVerts.Add(_verts[i]);
            }
        }

        var edgeIntersect = new Dictionary<long, int>();
        var capVertexIndices = new HashSet<int>();

        int InterpolateOrFetch(int ia, int ib)
        {
            long key = EdgeKey(ia, ib);
            if (edgeIntersect.TryGetValue(key, out int idx))
            {
                capVertexIndices.Add(idx);
                return idx;
            }
            double t = d[ia] / (d[ia] - d[ib]);
            var a = _verts[ia];
            var b = _verts[ib];
            var ip = (
                a.X + t * (b.X - a.X),
                a.Y + t * (b.Y - a.Y),
                a.Z + t * (b.Z - a.Z));
            idx = newVerts.Count;
            newVerts.Add(ip);
            edgeIntersect[key] = idx;
            capVertexIndices.Add(idx);
            return idx;
        }

        // pass 2: per-face Sutherland-Hodgman clip
        var newFaces = new List<int[]>(_faces.Count + 1);
        for (int f = 0; f < _faces.Count; f++)
        {
            var face = _faces[f];
            var kept = new List<int>(face.Length + 1);
            for (int k = 0; k < face.Length; k++)
            {
                int curr = face[k];
                int next = face[(k + 1) % face.Length];
                bool currIn = d[curr] <= 0.0;
                bool nextIn = d[next] <= 0.0;
                if (currIn) kept.Add(oldToNew[curr]);
                if (currIn != nextIn) kept.Add(InterpolateOrFetch(curr, next));
            }
            if (kept.Count >= 3) newFaces.Add(kept.ToArray());
        }

        // pass 3: build the cap face by sorting cap-vertex indices around their
        // centroid in the cut plane's 2D basis. This is robust against face
        // winding because the cap of a convex polyhedron cut by a plane is
        // itself a convex polygon, so a single angular sort yields the loop.
        if (capVertexIndices.Count >= 3)
        {
            var capList = new List<int>(capVertexIndices);
            // centroid in 3D
            double sx = 0, sy = 0, sz = 0;
            for (int i = 0; i < capList.Count; i++)
            {
                var p = newVerts[capList[i]];
                sx += p.X; sy += p.Y; sz += p.Z;
            }
            sx /= capList.Count; sy /= capList.Count; sz /= capList.Count;

            // 2D basis (e, f) in the plane perpendicular to nX, nY, nZ
            double eX, eY, eZ;
            if (Math.Abs(nZ) < 0.9) { eX = -nY; eY = nX; eZ = 0.0; }
            else                    { eX = 1.0; eY = 0.0; eZ = 0.0; }
            double el = Math.Sqrt(eX * eX + eY * eY + eZ * eZ);
            if (el >= BlockCutOptTolerances.GeometricEps)
            {
                eX /= el; eY /= el; eZ /= el;
                double fX = nY * eZ - nZ * eY;
                double fY = nZ * eX - nX * eZ;
                double fZ = nX * eY - nY * eX;

                var ang = new double[capList.Count];
                for (int i = 0; i < capList.Count; i++)
                {
                    var p = newVerts[capList[i]];
                    double rx = p.X - sx, ry = p.Y - sy, rz = p.Z - sz;
                    double u = rx * eX + ry * eY + rz * eZ;
                    double w = rx * fX + ry * fY + rz * fZ;
                    ang[i] = Math.Atan2(w, u);
                }
                var order = new int[capList.Count];
                for (int i = 0; i < capList.Count; i++) order[i] = i;
                Array.Sort(order, (a, b) => ang[a].CompareTo(ang[b]));

                var cap = new int[capList.Count];
                for (int i = 0; i < capList.Count; i++) cap[i] = capList[order[i]];
                newFaces.Add(cap);
            }
        }

        return new ConvexPolyhedron(newVerts, newFaces);
    }

    private static long EdgeKey(int a, int b) =>
        a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    /// <summary>
    /// I14 + Zhang 2024 cut-code compat: emit each face's half-space
    /// inequality as `(b, Nx, Ny, Nz)` where `Nx*x + Ny*y + Nz*z <= b` and
    /// `N` is the outward face normal. Useful for algebraic clipping
    /// pipelines and Layer-3 cross-validation against cut-code's
    /// `addConvex_byC` input format.
    /// </summary>
    public IReadOnlyList<(double B, double Nx, double Ny, double Nz)> ToInequalities()
    {
        var rows = new List<(double, double, double, double)>(_faces.Count);
        for (int f = 0; f < _faces.Count; f++)
        {
            var face = _faces[f];
            if (face.Length < 3) continue;
            var a = _verts[face[0]];
            var b = _verts[face[1]];
            var c = _verts[face[2]];
            double e1x = b.X - a.X, e1y = b.Y - a.Y, e1z = b.Z - a.Z;
            double e2x = c.X - a.X, e2y = c.Y - a.Y, e2z = c.Z - a.Z;
            double nX = e1y * e2z - e1z * e2y;
            double nY = e1z * e2x - e1x * e2z;
            double nZ = e1x * e2y - e1y * e2x;
            double len = Math.Sqrt(nX * nX + nY * nY + nZ * nZ);
            if (len < BlockCutOptTolerances.GeometricEps) continue;
            nX /= len; nY /= len; nZ /= len;
            double bDot = nX * a.X + nY * a.Y + nZ * a.Z;
            rows.Add((bDot, nX, nY, nZ));
        }
        return rows;
    }

    /// <summary>
    /// I14 + Zhang 2024 cut-code compat: build a ConvexPolyhedron from a list
    /// of half-space inequalities `Nx*x + Ny*y + Nz*z &lt;= b`. Implements the
    /// Con3_updateByC vertex-enumeration algorithm: every triple of planes
    /// is solved as a 3x3 linear system; the resulting point is a polytope
    /// vertex iff it satisfies all other inequalities. Faces are then built
    /// by collecting vertices lying on each plane.
    ///
    /// Returns null if the intersection is empty or degenerate.
    /// </summary>
    public static ConvexPolyhedron FromInequalities(
        IReadOnlyList<(double B, double Nx, double Ny, double Nz)> rows)
    {
        if (rows == null) throw new ArgumentNullException(nameof(rows));
        const double tol = 1.0e-6;
        int n = rows.Count;
        if (n < 4) return null;

        var verts = new List<(double X, double Y, double Z)>();
        // Track which inequalities (faces) contributed at least one vertex.
        var faceVerts = new List<List<int>>();
        for (int i = 0; i < n; i++) faceVerts.Add(new List<int>());

        for (int i = 0; i < n - 2; i++)
        for (int j = i + 1; j < n - 1; j++)
        for (int k = j + 1; k < n; k++)
        {
            // Solve the 3x3 linear system [N_i; N_j; N_k] x = [b_i; b_j; b_k]
            var ri = rows[i]; var rj = rows[j]; var rk = rows[k];
            double det =
                  ri.Nx * (rj.Ny * rk.Nz - rj.Nz * rk.Ny)
                - ri.Ny * (rj.Nx * rk.Nz - rj.Nz * rk.Nx)
                + ri.Nz * (rj.Nx * rk.Ny - rj.Ny * rk.Nx);
            if (det * det < tol * tol) continue;
            double invDet = 1.0 / det;

            double x =
                  (ri.B * (rj.Ny * rk.Nz - rj.Nz * rk.Ny)
                 - ri.Ny * (rj.B * rk.Nz - rj.Nz * rk.B)
                 + ri.Nz * (rj.B * rk.Ny - rj.Ny * rk.B)) * invDet;
            double y =
                  (ri.Nx * (rj.B * rk.Nz - rj.Nz * rk.B)
                 - ri.B * (rj.Nx * rk.Nz - rj.Nz * rk.Nx)
                 + ri.Nz * (rj.Nx * rk.B - rj.B * rk.Nx)) * invDet;
            double z =
                  (ri.Nx * (rj.Ny * rk.B - rj.B * rk.Ny)
                 - ri.Ny * (rj.Nx * rk.B - rj.B * rk.Nx)
                 + ri.B * (rj.Nx * rk.Ny - rj.Ny * rk.Nx)) * invDet;

            // Verify against all other half-spaces.
            bool inside = true;
            for (int q = 0; q < n && inside; q++)
            {
                var rq = rows[q];
                double val = rq.Nx * x + rq.Ny * y + rq.Nz * z - rq.B;
                if (val > tol) inside = false;
            }
            if (!inside) continue;

            // De-duplicate vertices.
            int existing = -1;
            for (int v = 0; v < verts.Count; v++)
            {
                if (Math.Abs(verts[v].X - x) < tol
                    && Math.Abs(verts[v].Y - y) < tol
                    && Math.Abs(verts[v].Z - z) < tol)
                {
                    existing = v;
                    break;
                }
            }
            if (existing < 0)
            {
                existing = verts.Count;
                verts.Add((x, y, z));
            }
            if (!faceVerts[i].Contains(existing)) faceVerts[i].Add(existing);
            if (!faceVerts[j].Contains(existing)) faceVerts[j].Add(existing);
            if (!faceVerts[k].Contains(existing)) faceVerts[k].Add(existing);
        }

        if (verts.Count < 4) return null;

        // Order each face's vertices CCW around the outward normal.
        var faces = new List<int[]>();
        for (int f = 0; f < n; f++)
        {
            var fv = faceVerts[f];
            if (fv.Count < 3) continue;
            var rf = rows[f];
            double cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < fv.Count; i++)
            {
                cx += verts[fv[i]].X; cy += verts[fv[i]].Y; cz += verts[fv[i]].Z;
            }
            cx /= fv.Count; cy /= fv.Count; cz /= fv.Count;

            // 2D basis (e, g) in the face plane perpendicular to N
            double eX, eY, eZ;
            if (Math.Abs(rf.Nz) < 0.9) { eX = -rf.Ny; eY = rf.Nx; eZ = 0; }
            else                       { eX = 1; eY = 0; eZ = 0; }
            double eLen = Math.Sqrt(eX * eX + eY * eY + eZ * eZ);
            if (eLen < BlockCutOptTolerances.GeometricEps) continue;
            eX /= eLen; eY /= eLen; eZ /= eLen;
            double gX = rf.Ny * eZ - rf.Nz * eY;
            double gY = rf.Nz * eX - rf.Nx * eZ;
            double gZ = rf.Nx * eY - rf.Ny * eX;

            var sorted = new List<int>(fv);
            sorted.Sort((va, vb) =>
            {
                var pa = verts[va]; var pb = verts[vb];
                double aE = (pa.X - cx) * eX + (pa.Y - cy) * eY + (pa.Z - cz) * eZ;
                double aG = (pa.X - cx) * gX + (pa.Y - cy) * gY + (pa.Z - cz) * gZ;
                double bE = (pb.X - cx) * eX + (pb.Y - cy) * eY + (pb.Z - cz) * eZ;
                double bG = (pb.X - cx) * gX + (pb.Y - cy) * gY + (pb.Z - cz) * gZ;
                return Math.Atan2(aG, aE).CompareTo(Math.Atan2(bG, bE));
            });
            faces.Add(sorted.ToArray());
        }
        if (faces.Count < 4) return null;
        return new ConvexPolyhedron(verts, faces);
    }

    /// <summary>
    /// I14 + Zhang 2024 cut-code compat: vectorised point-in-convex test.
    /// True iff the point lies inside (or on the boundary within `tol` of)
    /// every face's half-space.
    /// </summary>
    public bool ContainsPoint(double x, double y, double z, double tol = 0.0)
    {
        var ineq = ToInequalities();
        for (int i = 0; i < ineq.Count; i++)
        {
            var r = ineq[i];
            double val = r.Nx * x + r.Ny * y + r.Nz * z - r.B;
            if (val > tol) return false;
        }
        return true;
    }

    /// <summary>
    /// I14 + Zhang 2024 cut-code compat: signed gap to the polytope.
    /// `> 0` means the point is outside (the value is the largest plane
    /// violation), `= 0` means it lies on the boundary, `&lt; 0` means it
    /// lies inside (the value is the negative of the smallest face clearance).
    /// </summary>
    public double SignedGap(double x, double y, double z)
    {
        var ineq = ToInequalities();
        double maxVal = double.NegativeInfinity;
        for (int i = 0; i < ineq.Count; i++)
        {
            var r = ineq[i];
            double val = r.Nx * x + r.Ny * y + r.Nz * z - r.B;
            if (val > maxVal) maxVal = val;
        }
        return maxVal;
    }

    /// <summary>
    /// I14 + Zhang 2024 cut-code `add_cut_bothSide` analogue: one call,
    /// returns BOTH half-spaces. `Kept` is the side opposite the plane
    /// normal (same convention as `ClipByHalfSpace`); `Discarded` is the
    /// side the normal points to.
    /// </summary>
    public (ConvexPolyhedron Kept, ConvexPolyhedron Discarded) ClipBothSides(
        double pX, double pY, double pZ,
        double nX, double nY, double nZ)
    {
        var kept = ClipByHalfSpace(pX, pY, pZ, nX, nY, nZ);
        var discarded = ClipByHalfSpace(pX, pY, pZ, -nX, -nY, -nZ);
        return (kept, discarded);
    }

    /// <summary>
    /// Build a triangulated PlyMesh from this CPH by fan-triangulating each
    /// face. The result is consumable by SharedEdgeSlicer for cross-section
    /// extraction in the AmrrPlanner.
    /// </summary>
    public Frahan.Masonry.Geometry.PlyMesh ToPlyMesh()
    {
        var verts = new List<double>(_verts.Count * 3);
        for (int i = 0; i < _verts.Count; i++)
        {
            verts.Add(_verts[i].X);
            verts.Add(_verts[i].Y);
            verts.Add(_verts[i].Z);
        }
        var tris = new List<int>(_faces.Count * 3);
        for (int f = 0; f < _faces.Count; f++)
        {
            var face = _faces[f];
            for (int i = 1; i < face.Length - 1; i++)
            {
                tris.Add(face[0]);
                tris.Add(face[i]);
                tris.Add(face[i + 1]);
            }
        }
        return new Frahan.Masonry.Geometry.PlyMesh(verts, tris, null);
    }
}
