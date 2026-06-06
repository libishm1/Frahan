#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Core.ScanIngest
{
    // =========================================================================
    // ReconstructionCleanup -- post-process a reconstruction triangle soup so the
    // alpha-shape (and advancing-front) output stops being "weird".
    //
    // Root cause of the weird meshes (frahan_cgal.cpp alpha_shape_3): the native
    // code ran the alpha-shape in GENERAL mode and collected REGULAR *and* SINGULAR
    // facets. SINGULAR facets are bounded by exterior on BOTH sides -- they are
    // dangling/isolated triangles (spikes, loose flaps) that do not bound the solid.
    // The native fix is REGULARIZED mode + REGULAR-only (done in frahan_cgal.cpp);
    // this managed pass is the SAFETY NET that cleans the soup regardless of which
    // native DLL is loaded, so the fix is visible without a native rebuild.
    //
    // What it does (Rhino-free, deterministic, headless-testable):
    //  1. drop degenerate triangles (repeated/out-of-range vertex index),
    //  2. drop duplicate triangles (same unordered index triple),
    //  3. keep the LARGEST edge-connected component (removes the isolated SINGULAR
    //     spikes + loose clusters; a single welded stone surface is one component),
    //  4. compact unused vertices and remap indices.
    //
    // Also hosts Translate, the restore half of the GeometryNumerics recenter
    // (T1) wrapped around the native call by ScanReconstructComponent.
    // =========================================================================
    public static class ReconstructionCleanup
    {
        /// <summary>Add (dx,dy,dz) to every flat-xyz vertex, in place. The restore
        /// half of a recenter-to-centroid round trip (GeometryNumerics.Recenter).</summary>
        public static void Translate(double[] verts, double dx, double dy, double dz)
        {
            if (verts == null) return;
            for (int i = 0; i + 2 < verts.Length; i += 3)
            {
                verts[i] += dx; verts[i + 1] += dy; verts[i + 2] += dz;
            }
        }

        /// <summary>In-place variant: clean the soup and reassign the arrays.</summary>
        public static void Clean(ref double[] verts, ref int[] tris)
        {
            var (v, t) = Clean(verts, tris);
            verts = v; tris = t;
        }

        /// <summary>
        /// Clean a reconstruction triangle soup. Returns new (verts, tris) arrays.
        /// Pure; does not mutate the inputs. Empty/degenerate input returns empty.
        /// </summary>
        public static (double[] verts, int[] tris) Clean(double[] verts, int[] tris)
        {
            if (verts == null || tris == null || tris.Length < 3 || verts.Length < 9)
                return (verts ?? new double[0], tris ?? new int[0]);

            int vcount = verts.Length / 3;

            // 1+2: keep non-degenerate, de-duplicated triangles.
            var kept = new List<int[]>(tris.Length / 3);
            var seen = new HashSet<long>();
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vcount || b >= vcount || c >= vcount) continue;
                if (a == b || b == c || a == c) continue;                 // degenerate
                // unordered key for de-dup
                int x = a, y = b, z = c;
                if (x > y) { int tmp = x; x = y; y = tmp; }
                if (y > z) { int tmp = y; y = z; z = tmp; }
                if (x > y) { int tmp = x; x = y; y = tmp; }
                long key = ((long)x * 2147483647L + y) * 2147483647L + z;
                if (!seen.Add(key)) continue;                              // duplicate
                kept.Add(new[] { a, b, c });
            }
            if (kept.Count == 0) return (new double[0], new int[0]);

            // 3: largest edge-connected component via union-find over triangles.
            int nt = kept.Count;
            var parent = new int[nt];
            for (int i = 0; i < nt; i++) parent[i] = i;
            Func<int, int> find = null;
            find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            Action<int, int> union = (p, q) => { int rp = find(p), rq = find(q); if (rp != rq) parent[rp] = rq; };

            var edgeFirstTri = new Dictionary<long, int>(nt * 3);
            for (int ti = 0; ti < nt; ti++)
            {
                int[] tr = kept[ti];
                for (int e = 0; e < 3; e++)
                {
                    int u = tr[e], w = tr[(e + 1) % 3];
                    if (u > w) { int tmp = u; u = w; w = tmp; }
                    long ek = (long)u * 2147483648L + w;
                    if (edgeFirstTri.TryGetValue(ek, out int other)) union(ti, other);
                    else edgeFirstTri[ek] = ti;
                }
            }

            // component sizes; pick the largest
            var size = new Dictionary<int, int>();
            for (int ti = 0; ti < nt; ti++)
            {
                int r = find(ti);
                size.TryGetValue(r, out int s);
                size[r] = s + 1;
            }
            int bestRoot = -1, bestSize = -1;
            foreach (var kv in size) if (kv.Value > bestSize) { bestSize = kv.Value; bestRoot = kv.Key; }

            // 4: compact vertices used by the largest component, remap indices.
            var remap = new int[vcount];
            for (int i = 0; i < vcount; i++) remap[i] = -1;
            var outVerts = new List<double>(vcount * 3);
            var outTris = new List<int>(bestSize * 3);
            for (int ti = 0; ti < nt; ti++)
            {
                if (find(ti) != bestRoot) continue;
                int[] tr = kept[ti];
                for (int e = 0; e < 3; e++)
                {
                    int vi = tr[e];
                    if (remap[vi] < 0)
                    {
                        remap[vi] = outVerts.Count / 3;
                        outVerts.Add(verts[3 * vi + 0]);
                        outVerts.Add(verts[3 * vi + 1]);
                        outVerts.Add(verts[3 * vi + 2]);
                    }
                    outTris.Add(remap[vi]);
                }
            }
            return (outVerts.ToArray(), outTris.ToArray());
        }
    }
}
