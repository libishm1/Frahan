#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Cutting;

// =============================================================================
// SlabCutter — split convex polyhedral Slabs by oriented FracturePlanes.
//
// Algorithm: for each input slab and each plane, classify each vertex by
// signed distance to the plane (above / on / below up to eps). Walk each
// face once; for every directed edge that strictly straddles, interpolate
// to find the plane intersection point and emit it to BOTH the above and
// below sub-faces. On-plane vertices are shared by both halves.
//
// The cap polygon — the polygon where the plane intersects the slab — is
// reconstructed by taking every unique on-plane / intersection vertex,
// computing a 2D in-plane coordinate frame, and sorting CCW around
// +plane.normal. The same geometric ordering serves the below piece's cap
// (outward normal = +plane.normal, CCW seen from +normal direction); the
// above piece's cap is the reverse (outward normal = -plane.normal).
//
// Convex assumption: caller-supplied slabs must be convex. Non-convex
// input may produce caps that are non-simple polygons; the algorithm
// will not error on that, but downstream consumers (volume integration,
// MasonryBlock conversion) will misbehave. A future Phase-2 effort can
// add finite-extent fracture polygons (partial cuts → non-convex output).
//
// Reference: Goodman, R. E. & Shi, G. (1985). "Block Theory and its
// Application to Rock Engineering." Prentice-Hall. The block-pyramid
// approach to identifying removable blocks from a discrete fracture
// network is the classical foundation; this cutter implements the
// "successive plane decomposition" that builds the block list.
// =============================================================================

public static class SlabCutter
{
    public const double DefaultEps = 1e-9;

    /// <summary>
    /// Cut one slab by one plane.
    /// </summary>
    public static SlabCutResult Cut(Slab slab, FracturePlane plane, double eps = DefaultEps)
    {
        if (slab == null) throw new ArgumentNullException(nameof(slab));
        if (plane == null) throw new ArgumentNullException(nameof(plane));
        if (eps < 0.0) throw new ArgumentOutOfRangeException(nameof(eps), "must be >= 0");

        var pieces = CutOne(slab, plane, eps);
        var parents = new int[pieces.Count];
        // All pieces inherit parent 0 from the single input slab.
        return new SlabCutResult(pieces, parents);
    }

    /// <summary>
    /// Cut one slab by an ordered list of planes. Each plane in turn splits
    /// every existing piece (so the final list grows by up to 2x per plane).
    /// </summary>
    public static SlabCutResult Cut(Slab slab, IReadOnlyList<FracturePlane> planes, double eps = DefaultEps)
    {
        if (slab == null) throw new ArgumentNullException(nameof(slab));
        if (planes == null) throw new ArgumentNullException(nameof(planes));
        if (eps < 0.0) throw new ArgumentOutOfRangeException(nameof(eps), "must be >= 0");

        var current = new List<Slab> { slab };
        for (int p = 0; p < planes.Count; p++)
        {
            var plane = planes[p];
            if (plane == null)
                throw new ArgumentException($"planes[{p}] is null", nameof(planes));
            var next = new List<Slab>(current.Count * 2);
            for (int i = 0; i < current.Count; i++)
            {
                var sub = CutOne(current[i], plane, eps);
                for (int k = 0; k < sub.Count; k++) next.Add(sub[k]);
            }
            current = next;
        }
        var parents = new int[current.Count]; // single-input case: every parent is 0
        return new SlabCutResult(current, parents);
    }

    /// <summary>
    /// Cut a list of slabs by a list of planes. Each plane is applied to
    /// every current piece in sequence. Provenance is preserved: each output
    /// piece records the index of the input slab it descended from.
    /// </summary>
    public static SlabCutResult Cut(
        IReadOnlyList<Slab> slabs,
        IReadOnlyList<FracturePlane> planes,
        double eps = DefaultEps)
    {
        if (slabs == null) throw new ArgumentNullException(nameof(slabs));
        if (planes == null) throw new ArgumentNullException(nameof(planes));
        if (eps < 0.0) throw new ArgumentOutOfRangeException(nameof(eps), "must be >= 0");

        var pieces = new List<Slab>(slabs.Count);
        var parents = new List<int>(slabs.Count);
        for (int i = 0; i < slabs.Count; i++)
        {
            if (slabs[i] == null) throw new ArgumentException($"slabs[{i}] is null", nameof(slabs));
            pieces.Add(slabs[i]);
            parents.Add(i);
        }

        for (int p = 0; p < planes.Count; p++)
        {
            var plane = planes[p];
            if (plane == null)
                throw new ArgumentException($"planes[{p}] is null", nameof(planes));

            var nextPieces = new List<Slab>(pieces.Count * 2);
            var nextParents = new List<int>(pieces.Count * 2);
            for (int i = 0; i < pieces.Count; i++)
            {
                var sub = CutOne(pieces[i], plane, eps);
                int parent = parents[i];
                for (int k = 0; k < sub.Count; k++)
                {
                    nextPieces.Add(sub[k]);
                    nextParents.Add(parent);
                }
            }
            pieces = nextPieces;
            parents = nextParents;
        }

        return new SlabCutResult(pieces, parents);
    }

    // -------------------------------------------------------------------------
    // Single-slab single-plane cut. Returns 1 (plane misses) or 2 pieces.
    // -------------------------------------------------------------------------

    private static List<Slab> CutOne(Slab slab, FracturePlane plane, double eps)
    {
        int vCount = slab.VertexCount;
        var v = slab.VertexCoordsXyz;

        // ---- Classify vertices: -1 below, 0 on, +1 above ----
        var cls = new int[vCount];
        var dist = new double[vCount];
        bool anyAboveStrict = false;
        bool anyBelowStrict = false;
        for (int i = 0; i < vCount; i++)
        {
            double d = plane.SignedDistance(v[3 * i], v[3 * i + 1], v[3 * i + 2]);
            dist[i] = d;
            if (d > eps) { cls[i] = 1; anyAboveStrict = true; }
            else if (d < -eps) { cls[i] = -1; anyBelowStrict = true; }
            else cls[i] = 0;
        }

        // ---- Plane misses entirely → return original as single piece ----
        if (!anyAboveStrict || !anyBelowStrict)
        {
            return new List<Slab> { slab };
        }

        // ---- Build above and below vertex pools ----
        // Original vertices that are class >= 0 go into above_pool;
        // original vertices that are class <= 0 go into below_pool.
        // Maps original index -> pool index.
        var aboveMap = new int[vCount];
        var belowMap = new int[vCount];
        for (int i = 0; i < vCount; i++) { aboveMap[i] = -1; belowMap[i] = -1; }

        var aboveCoords = new List<double>();
        var belowCoords = new List<double>();
        for (int i = 0; i < vCount; i++)
        {
            if (cls[i] >= 0)
            {
                aboveMap[i] = aboveCoords.Count / 3;
                aboveCoords.Add(v[3 * i]); aboveCoords.Add(v[3 * i + 1]); aboveCoords.Add(v[3 * i + 2]);
            }
            if (cls[i] <= 0)
            {
                belowMap[i] = belowCoords.Count / 3;
                belowCoords.Add(v[3 * i]); belowCoords.Add(v[3 * i + 1]); belowCoords.Add(v[3 * i + 2]);
            }
        }

        // ---- Edge intersection cache: (min_v, max_v) -> (above_idx, below_idx) ----
        // Each straddling edge contributes exactly one new vertex (geometrically),
        // appearing in both the above_pool and the below_pool (at different indices).
        var edgeAboveIdx = new Dictionary<long, int>();
        var edgeBelowIdx = new Dictionary<long, int>();
        // Track every cap vertex (as a (above_idx, below_idx) pair) so we can
        // build the cap polygon at the end.
        var capPairs = new List<(int Above, int Below, double X, double Y, double Z)>();
        // Track on-plane original vertices once (deduplicated) for cap inclusion.
        var capOnPlaneSeen = new HashSet<int>();

        long EdgeKey(int va, int vb)
        {
            int lo = va < vb ? va : vb;
            int hi = va < vb ? vb : va;
            return (long)lo * (long)int.MaxValue + (long)hi;
        }

        int InternIntersection(int va, int vb)
        {
            long key = EdgeKey(va, vb);
            if (edgeAboveIdx.TryGetValue(key, out int aboveIdx))
            {
                return key.GetHashCode(); // not used; presence is enough
            }
            // Compute intersection point at param t along edge a->b
            double da = dist[va];
            double db = dist[vb];
            double t = -da / (db - da);
            double x = v[3 * va] + t * (v[3 * vb] - v[3 * va]);
            double y = v[3 * va + 1] + t * (v[3 * vb + 1] - v[3 * va + 1]);
            double z = v[3 * va + 2] + t * (v[3 * vb + 2] - v[3 * va + 2]);

            int aIdx = aboveCoords.Count / 3;
            aboveCoords.Add(x); aboveCoords.Add(y); aboveCoords.Add(z);
            int bIdx = belowCoords.Count / 3;
            belowCoords.Add(x); belowCoords.Add(y); belowCoords.Add(z);

            edgeAboveIdx[key] = aIdx;
            edgeBelowIdx[key] = bIdx;
            capPairs.Add((aIdx, bIdx, x, y, z));
            return aIdx; // signal we created
        }

        // ---- Walk each face, build above and below sub-faces ----
        var aboveFaces = new List<IReadOnlyList<int>>();
        var belowFaces = new List<IReadOnlyList<int>>();

        for (int fi = 0; fi < slab.Faces.Count; fi++)
        {
            var face = slab.Faces[fi];
            var aboveFace = new List<int>(face.Count + 2);
            var belowFace = new List<int>(face.Count + 2);

            int n = face.Count;
            for (int k = 0; k < n; k++)
            {
                int va = face[k];
                int vb = face[(k + 1) % n];
                int ca = cls[va];
                int cb = cls[vb];

                // Emit va to side(s) it belongs to.
                if (ca >= 0) aboveFace.Add(aboveMap[va]);
                if (ca <= 0) belowFace.Add(belowMap[va]);

                // If va is on the plane, record once for the cap polygon.
                if (ca == 0 && capOnPlaneSeen.Add(va))
                {
                    capPairs.Add((
                        aboveMap[va],
                        belowMap[va],
                        v[3 * va], v[3 * va + 1], v[3 * va + 2]));
                }

                // If edge ab strictly straddles, intern the intersection point
                // (it lands in both pools) and emit to both sub-faces.
                if (ca * cb < 0)
                {
                    long key = EdgeKey(va, vb);
                    if (!edgeAboveIdx.ContainsKey(key))
                    {
                        InternIntersection(va, vb);
                    }
                    aboveFace.Add(edgeAboveIdx[key]);
                    belowFace.Add(edgeBelowIdx[key]);
                }
            }

            // Strip duplicate consecutive indices that can arise when an
            // on-plane vertex sits at the end of a face wrap.
            DropConsecutiveDuplicates(aboveFace);
            DropConsecutiveDuplicates(belowFace);

            if (aboveFace.Count >= 3) aboveFaces.Add(aboveFace);
            if (belowFace.Count >= 3) belowFaces.Add(belowFace);
        }

        // ---- Build cap polygon, add to both pieces with correct orientation ----
        if (capPairs.Count >= 3)
        {
            int[] capOrder = OrderCapCcwAroundNormal(capPairs, plane);
            // Below piece: cap outward normal = +plane.normal; CCW seen from
            // +normal direction is the natural ordering we computed.
            var capForBelow = new int[capOrder.Length];
            for (int i = 0; i < capOrder.Length; i++)
                capForBelow[i] = capPairs[capOrder[i]].Below;
            DropConsecutiveDuplicates(capForBelow, out var capForBelowList);
            if (capForBelowList.Count >= 3) belowFaces.Add(capForBelowList);

            // Above piece: cap outward normal = -plane.normal; ordering reverses.
            var capForAbove = new int[capOrder.Length];
            for (int i = 0; i < capOrder.Length; i++)
                capForAbove[i] = capPairs[capOrder[capOrder.Length - 1 - i]].Above;
            DropConsecutiveDuplicates(capForAbove, out var capForAboveList);
            if (capForAboveList.Count >= 3) aboveFaces.Add(capForAboveList);
        }

        var pieces = new List<Slab>();
        if (aboveFaces.Count >= 4) pieces.Add(new Slab(aboveCoords, aboveFaces));
        if (belowFaces.Count >= 4) pieces.Add(new Slab(belowCoords, belowFaces));

        // Numerical degeneracy: if the cut produced fewer than 2 valid pieces
        // (e.g. plane was effectively coincident with a face), fall back to
        // returning the original.
        if (pieces.Count < 2)
            return new List<Slab> { slab };

        return pieces;
    }

    // -------------------------------------------------------------------------
    // Cap-polygon ordering (CCW around plane.normal as seen from +normal side).
    // -------------------------------------------------------------------------

    private static int[] OrderCapCcwAroundNormal(
        List<(int Above, int Below, double X, double Y, double Z)> capPairs,
        FracturePlane plane)
    {
        int n = capPairs.Count;

        // Centroid of cap points
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < n; i++)
        {
            cx += capPairs[i].X; cy += capPairs[i].Y; cz += capPairs[i].Z;
        }
        cx /= n; cy /= n; cz /= n;

        // Build orthonormal in-plane basis (u, v) with u x v = +normal.
        double nx = plane.NormalX, ny = plane.NormalY, nz = plane.NormalZ;
        double ax = Math.Abs(nx), ay = Math.Abs(ny), az = Math.Abs(nz);
        double rx, ry, rz;
        if (ax <= ay && ax <= az) { rx = 1; ry = 0; rz = 0; }
        else if (ay <= ax && ay <= az) { rx = 0; ry = 1; rz = 0; }
        else { rx = 0; ry = 0; rz = 1; }

        // u = cross(n, r)
        double ux = ny * rz - nz * ry;
        double uy = nz * rx - nx * rz;
        double uz = nx * ry - ny * rx;
        double ulen = Math.Sqrt(ux * ux + uy * uy + uz * uz);
        ux /= ulen; uy /= ulen; uz /= ulen;
        // v = cross(n, u)
        double vx = ny * uz - nz * uy;
        double vy = nz * ux - nx * uz;
        double vz = nx * uy - ny * ux;
        double vlen = Math.Sqrt(vx * vx + vy * vy + vz * vz);
        vx /= vlen; vy /= vlen; vz /= vlen;

        var indexAndAngle = new (int Idx, double Theta)[n];
        for (int i = 0; i < n; i++)
        {
            double dx = capPairs[i].X - cx;
            double dy = capPairs[i].Y - cy;
            double dz = capPairs[i].Z - cz;
            double du = dx * ux + dy * uy + dz * uz;
            double dv = dx * vx + dy * vy + dz * vz;
            indexAndAngle[i] = (i, Math.Atan2(dv, du));
        }
        Array.Sort(indexAndAngle, (a, b) => a.Theta.CompareTo(b.Theta));
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = indexAndAngle[i].Idx;
        return order;
    }

    // -------------------------------------------------------------------------
    // Degenerate-edge cleanup: collapse consecutive duplicate indices in a
    // face polygon (and the wrap-around between the last and first entry).
    // -------------------------------------------------------------------------

    private static void DropConsecutiveDuplicates(List<int> face)
    {
        for (int i = face.Count - 1; i > 0; i--)
            if (face[i] == face[i - 1])
                face.RemoveAt(i);
        if (face.Count > 1 && face[0] == face[face.Count - 1])
            face.RemoveAt(face.Count - 1);
    }

    private static void DropConsecutiveDuplicates(int[] face, out List<int> outList)
    {
        outList = new List<int>(face.Length);
        for (int i = 0; i < face.Length; i++)
        {
            if (outList.Count > 0 && outList[outList.Count - 1] == face[i]) continue;
            outList.Add(face[i]);
        }
        if (outList.Count > 1 && outList[0] == outList[outList.Count - 1])
            outList.RemoveAt(outList.Count - 1);
    }
}
