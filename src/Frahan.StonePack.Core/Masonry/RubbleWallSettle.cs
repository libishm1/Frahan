#nullable disable
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Frahan.Masonry
{
    // =========================================================================
    // RubbleWallSettle — concave-aware Z-up rubble-wall settle.
    //
    // Faithful C# port of the user-signed-off Python prototype
    //   outputs/2026-05-25/eth1100_pack/eth1100_rubble_settle.py  (algorithm)
    //   outputs/2026-05-25/eth1100_pack/stability.py              (COM-over-support)
    // and the RhinoCommon Python port gh_python/rubble_core.py (settle_wall).
    //
    // Z-up wall: gravity = -Z (courses stack UPWARD in +Z), wall length = X,
    // wall thickness = Y (single wythe). Each stone is PCA-oriented for flat
    // bedding (largest extent -> X, mid -> Y, SMALLEST -> Z so the broad face
    // beds DOWN), then settled into the dimples of the course below per
    // (x,y)-cell with a small X-slot search. Stability is the shared
    // COM-over-support test (COM projected to the bed must lie inside the
    // convex hull of the contact footprint, with margin).
    //
    // Pure managed + RhinoCommon types only. Deterministic: contact profiles
    // come from mesh VERTICES (not random surface sampling), so there is no
    // RNG and repeated runs are bit-identical. Non-penetrating by construction
    // (per-cell vertical contact against a running height map).
    //
    // No new dependencies: the 2D convex hull (Andrew monotone chain) and
    // point-in-hull clearance are implemented inline.
    // =========================================================================

    /// <summary>
    /// Result for one settled stone: the rigid transform that places the input
    /// mesh upright in the wall, whether its COM sits over its contact support,
    /// and the signed support clearance (&gt; 0 = inside the support polygon).
    /// </summary>
    public sealed class RubbleStonePlacement
    {
        public RubbleStonePlacement(Transform transform, bool stable, double clearance)
        {
            Transform = transform;
            Stable = stable;
            Clearance = clearance;
        }

        /// <summary>World transform; apply to the input mesh to place it in the wall.</summary>
        public Transform Transform { get; }

        /// <summary>True if the COM is inside the contact support polygon by at least the margin.</summary>
        public bool Stable { get; }

        /// <summary>
        /// Signed clearance of the projected COM from the support-polygon edge.
        /// &gt; 0 inside; &lt;= 0 would topple; -1 if support is degenerate.
        /// </summary>
        public double Clearance { get; }
    }

    /// <summary>
    /// Concave-aware Z-up rubble-wall settle. See class remarks for the algorithm.
    /// </summary>
    public static class RubbleWallSettle
    {
        // 180-deg flips about each principal axis. They PRESERVE per-axis extents
        // (so the smallest extent stays vertical and the flat face keeps bedding
        // down) but change which broad face beds down and the in-plane heading.
        // Identity + the 3 flips = 4 variants per stone. Rows are the diagonal.
        private static readonly double[][] Flips =
        {
            new[] {  1.0,  1.0,  1.0 },
            new[] {  1.0, -1.0, -1.0 },
            new[] { -1.0,  1.0, -1.0 },
            new[] { -1.0, -1.0,  1.0 },
        };

        private static readonly int[] KShifts = { -2, -1, 0, 1, 2 };

        /// <summary>
        /// Settle <paramref name="meshes"/> into an upright Z-up rubble wall.
        /// Returns one placement per input mesh, order preserved.
        /// </summary>
        /// <param name="meshes">Stone meshes (inventory). Null entries are skipped (identity placement).</param>
        /// <param name="widthMult">Wall length in mean-stone X-extents. Default 7.0.</param>
        /// <param name="stabilityAware">Prefer a seat whose COM sits over its support, then deepest.</param>
        /// <param name="margin">Required COM-over-support clearance to count as stable.</param>
        /// <param name="cellsPerWidth">Contact-grid resolution: cell = meanX / cellsPerWidth. Default 20.</param>
        public static IList<RubbleStonePlacement> Settle(
            IList<Mesh> meshes,
            double widthMult = 7.0,
            bool stabilityAware = true,
            double margin = 0.0,
            int cellsPerWidth = 20)
        {
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));
            if (cellsPerWidth < 1) throw new ArgumentOutOfRangeException(nameof(cellsPerWidth));

            int n = meshes.Count;
            var results = new RubbleStonePlacement[n];
            if (n == 0) return results;

            // ---- PCA flat-bed orient: per-stone rotation R (cols = new axes) + centroid c.
            // Vr = (V - c) @ R gives oriented, centred vertices; ext = per-axis extent.
            var baseR = new double[n][];   // 3x3 row-major; v_oriented = R^T (v - c)
            var baseC = new double[n][];   // centroid
            var ext = new double[n][];     // [extX, extY, extZ] of the oriented cloud
            int valid = 0;
            for (int i = 0; i < n; i++)
            {
                var m = meshes[i];
                if (m == null || m.Vertices.Count < 3)
                {
                    results[i] = new RubbleStonePlacement(Transform.Identity, false, -1.0);
                    baseR[i] = null;
                    continue;
                }
                PcaFlatbed(m, out double[] R, out double[] c, out double[] e);
                baseR[i] = R; baseC[i] = c; ext[i] = e; valid++;
            }
            if (valid == 0) return results;

            double meanW = 0.0; int cnt = 0;
            for (int i = 0; i < n; i++) { if (ext[i] != null) { meanW += ext[i][0]; cnt++; } }
            meanW /= cnt;
            if (meanW <= 0.0) meanW = 1.0;
            double length = widthMult * meanW;
            double cell = meanW / cellsPerWidth;

            // ---- Course layout along X. Flips preserve extents, so layout is variant-free.
            var baseX = new double[n];
            var baseZ = new double[n];
            {
                double x = 0.0, courseZ = 0.0, rowh = 0.0;
                for (int i = 0; i < n; i++)
                {
                    if (ext[i] == null) continue;
                    double w = ext[i][0];
                    if (x + w > length) { x = 0.0; courseZ += rowh * 1.02; rowh = 0.0; }
                    baseX[i] = x; baseZ[i] = courseZ;
                    x += w + 0.02 * meanW;
                    if (ext[i][2] > rowh) rowh = ext[i][2];
                }
            }

            // ---- Build per-(stone, flip) variants: world profiles, parked-min, COM, linear map.
            var varProfs = new Dictionary<long, double[]>[n][];   // cell key -> [zmin, zmax]
            var varM = new double[n][][];                          // v_local -> centred+flipped (row-major 3x3)
            var varMin = new double[n][][];                        // parked min, base_x removed from X
            var varCom = new double[n][][];                        // (comX incl. base_x, comY)
            for (int i = 0; i < n; i++)
            {
                if (ext[i] == null) continue;
                var m = meshes[i];
                int vc = m.Vertices.Count;

                // Oriented, centred vertices Vr = R^T (v - c).
                var Vr = new double[vc][];
                double cx = 0, cy = 0, cz = 0;
                for (int k = 0; k < vc; k++)
                {
                    var v = m.Vertices[k];
                    double[] o = OrientPoint(baseR[i], baseC[i], v.X, v.Y, v.Z);
                    Vr[k] = o;
                    cx += o[0]; cy += o[1]; cz += o[2];
                }
                cx /= vc; cy /= vc; cz /= vc;     // COM proxy = vertex centroid (matches prototype)

                varProfs[i] = new Dictionary<long, double[]>[Flips.Length];
                varM[i] = new double[Flips.Length][];
                varMin[i] = new double[Flips.Length][];
                varCom[i] = new double[Flips.Length][];

                for (int fi = 0; fi < Flips.Length; fi++)
                {
                    double[] F = Flips[fi];   // diagonal sign flip about X/Y/Z
                    // Vf = Vr @ F^T ; since F is diagonal, this scales each coord by F.
                    double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
                    var Vf = new double[vc][];
                    for (int k = 0; k < vc; k++)
                    {
                        double fx = Vr[k][0] * F[0];
                        double fy = Vr[k][1] * F[1];
                        double fz = Vr[k][2] * F[2];
                        Vf[k] = new[] { fx, fy, fz };
                        if (fx < minX) minX = fx;
                        if (fy < minY) minY = fy;
                        if (fz < minZ) minZ = fz;
                    }

                    // Parked +octant, then bake course X. prof keyed on (ix, iy).
                    var prof = new Dictionary<long, double[]>();
                    for (int k = 0; k < vc; k++)
                    {
                        double px = (Vf[k][0] - minX) + baseX[i];
                        double py = (Vf[k][1] - minY);
                        double pz = (Vf[k][2] - minZ);
                        int ix = FloorDiv(px, cell);
                        int iy = FloorDiv(py, cell);
                        long key = CellKey(ix, iy);
                        if (prof.TryGetValue(key, out double[] lohi))
                        {
                            if (pz < lohi[0]) lohi[0] = pz;
                            if (pz > lohi[1]) lohi[1] = pz;
                        }
                        else
                        {
                            prof[key] = new[] { pz, pz };
                        }
                    }
                    varProfs[i][fi] = prof;

                    // Linear map v_local -> centred+flipped:  M = (R applied) then flip.
                    // OrientPoint computes R^T (v - c); flip scales rows by F.
                    // var_M row r = F[r] * R_col(r)  (R stored row-major; R^T row r = R column r).
                    varM[i][fi] = new[]
                    {
                        F[0] * baseR[i][0], F[0] * baseR[i][3], F[0] * baseR[i][6],
                        F[1] * baseR[i][1], F[1] * baseR[i][4], F[1] * baseR[i][7],
                        F[2] * baseR[i][2], F[2] * baseR[i][5], F[2] * baseR[i][8],
                    };
                    // var_min holds (min - [base_x,0,0]) so the world translation can add base_x once.
                    varMin[i][fi] = new[] { minX - baseX[i], minY, minZ };

                    // COM of this flip, parked, with base_x baked into X.
                    double comFx = (cx * F[0]) - minX + baseX[i];
                    double comFy = (cy * F[1]) - minY;
                    varCom[i][fi] = new[] { comFx, comFy };
                }
            }

            // ---- Drop settle (deepest, or stable-preferred) with X-slot search.
            // Bottom course first; tie-break by leftmost cell of variant 0 (matches prototype).
            var order = new List<int>(n);
            for (int i = 0; i < n; i++) if (ext[i] != null) order.Add(i);
            order.Sort((a, b) =>
            {
                int c = baseZ[a].CompareTo(baseZ[b]);
                if (c != 0) return c;
                return MinCellX(varProfs[a][0]).CompareTo(MinCellX(varProfs[b][0]));
            });

            var top = new Dictionary<long, double>();
            foreach (int i in order)
            {
                bool haveBest = false;
                int bestStable = 0; double bestZ = 0; int bestV = 0; int bestK = 0;
                Dictionary<long, double[]> bestProf = null; double bestClr = -1.0;

                for (int v = 0; v < Flips.Length; v++)
                {
                    var p = varProfs[i][v];
                    double ptop = double.NegativeInfinity, pbot = double.PositiveInfinity;
                    foreach (var kv in p)
                    {
                        if (kv.Value[1] > ptop) ptop = kv.Value[1];
                        if (kv.Value[0] < pbot) pbot = kv.Value[0];
                    }
                    double tol = 0.12 * Math.Max(ptop - pbot, 1e-9);
                    double comx0 = varCom[i][v][0];
                    double comy0 = varCom[i][v][1];

                    foreach (int k in KShifts)
                    {
                        double zk = RestZ(p, top, k);
                        var pts = Contacts(p, top, k, zk, cell, tol);
                        double clr = SupportClearance(comx0 + k * cell, comy0, pts);
                        int stableFlag = stabilityAware ? (clr >= margin ? 0 : 1) : 0;

                        // Lexicographic compare on (stableFlag, zk) when aware, else (zk).
                        bool better;
                        if (!haveBest)
                        {
                            better = true;
                        }
                        else if (stabilityAware)
                        {
                            better = stableFlag < bestStable ||
                                     (stableFlag == bestStable && zk < bestZ);
                        }
                        else
                        {
                            better = zk < bestZ;
                        }

                        if (better)
                        {
                            haveBest = true;
                            bestStable = stableFlag; bestZ = zk; bestV = v; bestK = k;
                            bestProf = p; bestClr = clr;
                        }
                    }
                }

                // Raise the height map with the chosen seat.
                foreach (var kv in bestProf)
                {
                    DecodeKey(kv.Key, out int ix, out int iy);
                    long key = CellKey(ix + bestK, iy);
                    double t = kv.Value[1] + bestZ;
                    if (!top.TryGetValue(key, out double cur) || t > cur) top[key] = t;
                }

                // World transform: v -> M v + tvec.
                double[] M = varM[i][bestV];
                double[] mn = varMin[i][bestV];
                double[] c0 = baseC[i];
                // tvec = -(M @ base_c) - var_min + [bestK*cell, 0, bestZ]
                double mcx = M[0] * c0[0] + M[1] * c0[1] + M[2] * c0[2];
                double mcy = M[3] * c0[0] + M[4] * c0[1] + M[5] * c0[2];
                double mcz = M[6] * c0[0] + M[7] * c0[1] + M[8] * c0[2];
                double tx = -mcx - mn[0] + bestK * cell;
                double ty = -mcy - mn[1];
                double tz = -mcz - mn[2] + bestZ;

                results[i] = new RubbleStonePlacement(
                    BuildTransform(M, tx, ty, tz),
                    bestClr >= margin,
                    bestClr);
            }

            // Any never-assigned slot (degenerate mesh) already has an identity placement.
            for (int i = 0; i < n; i++)
                if (results[i] == null)
                    results[i] = new RubbleStonePlacement(Transform.Identity, false, -1.0);

            return results;
        }

        // ─── PCA flat-bed orientation ────────────────────────────────────────

        // Returns R (row-major 3x3, columns = principal axes largest/mid/smallest),
        // centroid c, and the per-axis extents of the oriented cloud.
        private static void PcaFlatbed(Mesh m, out double[] R, out double[] c, out double[] ext)
        {
            int n = m.Vertices.Count;
            double cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < n; i++)
            {
                var v = m.Vertices[i];
                cx += v.X; cy += v.Y; cz += v.Z;
            }
            cx /= n; cy /= n; cz /= n;
            c = new[] { cx, cy, cz };

            double sxx = 0, syy = 0, szz = 0, sxy = 0, sxz = 0, syz = 0;
            for (int i = 0; i < n; i++)
            {
                var v = m.Vertices[i];
                double dx = v.X - cx, dy = v.Y - cy, dz = v.Z - cz;
                sxx += dx * dx; syy += dy * dy; szz += dz * dz;
                sxy += dx * dy; sxz += dx * dz; syz += dy * dz;
            }
            // np.cov divides by (n - 1); the eigenvectors are scale-invariant so
            // the divisor does not affect the axes, but match it for parity.
            double inv = n > 1 ? 1.0 / (n - 1) : 1.0;
            sxx *= inv; syy *= inv; szz *= inv; sxy *= inv; sxz *= inv; syz *= inv;

            var cov = new[,]
            {
                { sxx, sxy, sxz },
                { sxy, syy, syz },
                { sxz, syz, szz },
            };
            JacobiEigen3(cov, out double[] eigVals, out double[][] eigVecs);

            // Sort eigen indices by DESCENDING eigenvalue (largest, mid, smallest).
            int[] o = { 0, 1, 2 };
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                    if (eigVals[o[i]] < eigVals[o[j]])
                    { int t = o[i]; o[i] = o[j]; o[j] = t; }

            // R columns = sorted eigenvectors (largest -> X, mid -> Y, smallest -> Z).
            // Stored row-major: R[row*3 + col].
            R = new double[9];
            for (int col = 0; col < 3; col++)
            {
                double[] e = eigVecs[o[col]];
                R[0 * 3 + col] = e[0];
                R[1 * 3 + col] = e[1];
                R[2 * 3 + col] = e[2];
            }
            // Right-handed: if det < 0, flip the Z (smallest) column.
            if (Det3(R) < 0)
            {
                R[0 * 3 + 2] = -R[0 * 3 + 2];
                R[1 * 3 + 2] = -R[1 * 3 + 2];
                R[2 * 3 + 2] = -R[2 * 3 + 2];
            }

            // Extents of the oriented cloud: max-min of (R^T (v - c)) per axis.
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var v = m.Vertices[i];
                double[] po = OrientPoint(R, c, v.X, v.Y, v.Z);
                if (po[0] < minX) minX = po[0]; if (po[0] > maxX) maxX = po[0];
                if (po[1] < minY) minY = po[1]; if (po[1] > maxY) maxY = po[1];
                if (po[2] < minZ) minZ = po[2]; if (po[2] > maxZ) maxZ = po[2];
            }
            ext = new[] { maxX - minX, maxY - minY, maxZ - minZ };
        }

        // R^T (v - c): R stored row-major with columns = axes, so the oriented
        // coord along axis j is the dot of (v - c) with column j of R.
        private static double[] OrientPoint(double[] R, double[] c, double x, double y, double z)
        {
            double dx = x - c[0], dy = y - c[1], dz = z - c[2];
            return new[]
            {
                dx * R[0] + dy * R[3] + dz * R[6],   // dot with column 0
                dx * R[1] + dy * R[4] + dz * R[7],   // column 1
                dx * R[2] + dy * R[5] + dz * R[8],   // column 2
            };
        }

        private static double Det3(double[] R)
        {
            return R[0] * (R[4] * R[8] - R[5] * R[7])
                 - R[1] * (R[3] * R[8] - R[5] * R[6])
                 + R[2] * (R[3] * R[7] - R[4] * R[6]);
        }

        // ─── Settle helpers (mirror the prototype's _rest_z / _contacts) ──────

        private static double RestZ(Dictionary<long, double[]> p, Dictionary<long, double> top, int kshift)
        {
            // z = -min(lo)  (lowest point drops to the z=0 floor baseline)
            double minLo = double.PositiveInfinity;
            foreach (var kv in p) if (kv.Value[0] < minLo) minLo = kv.Value[0];
            double z = -minLo;
            foreach (var kv in p)
            {
                DecodeKey(kv.Key, out int ix, out int iy);
                if (top.TryGetValue(CellKey(ix + kshift, iy), out double t))
                {
                    double s = t - kv.Value[0];
                    if (s > z) z = s;
                }
            }
            return z;
        }

        private static List<double[]> Contacts(
            Dictionary<long, double[]> p, Dictionary<long, double> top,
            int kshift, double z, double cell, double tol)
        {
            var pts = new List<double[]>();
            foreach (var kv in p)
            {
                DecodeKey(kv.Key, out int ix, out int iy);
                double s = top.TryGetValue(CellKey(ix + kshift, iy), out double t) ? t : 0.0;
                if ((s - kv.Value[0]) >= z - tol)
                    pts.Add(new[] { (ix + kshift) * cell, iy * cell });
            }
            return pts;
        }

        // ─── COM-over-support (port of stability.support_clearance) ──────────

        // Signed clearance of (cx,cy) from the convex hull of contact_pts.
        //  > 0 : inside the support polygon (distance to nearest edge).
        //  <= 0: on/outside (would topple); -1 if support is degenerate.
        internal static double SupportClearance(double cx, double cy, IList<double[]> contactPts)
        {
            var hull = ConvexHull2D(contactPts);
            if (hull == null) return -1.0;
            int n = hull.Count;
            double worst = double.PositiveInfinity;
            for (int k = 0; k < n; k++)
            {
                double[] a = hull[k];
                double[] b = hull[(k + 1) % n];
                double ex = b[0] - a[0], ey = b[1] - a[1];
                // Inward normal for a CCW hull is [-ey, ex]; signed distance > 0 inside.
                double nx = -ey, ny = ex;
                double ln = Math.Sqrt(nx * nx + ny * ny);
                if (ln < 1e-12) continue;
                nx /= ln; ny /= ln;
                double d = (cx - a[0]) * nx + (cy - a[1]) * ny;
                if (d < worst) worst = d;
            }
            return worst == double.PositiveInfinity ? -1.0 : worst;
        }

        // Andrew monotone chain. Returns CCW hull (>= 3 unique pts) or null.
        private static List<double[]> ConvexHull2D(IList<double[]> pts)
        {
            if (pts == null || pts.Count < 3) return null;

            // Collapse duplicates (round to 9 dp, mirrors np.unique(round(...))).
            var seen = new HashSet<long>();
            var uniq = new List<double[]>(pts.Count);
            foreach (var p in pts)
            {
                long rx = (long)Math.Round(p[0] * 1e9);
                long ry = (long)Math.Round(p[1] * 1e9);
                long key = unchecked(rx * 1000003L + ry);
                // Exact de-dup on the rounded coords (hash collisions resolved by linear scan below).
                bool dup = false;
                if (seen.Contains(key))
                {
                    foreach (var q in uniq)
                        if ((long)Math.Round(q[0] * 1e9) == rx && (long)Math.Round(q[1] * 1e9) == ry)
                        { dup = true; break; }
                }
                if (!dup) { seen.Add(key); uniq.Add(new[] { p[0], p[1] }); }
            }
            if (uniq.Count < 3) return null;

            uniq.Sort((a, b) => a[0] == b[0] ? a[1].CompareTo(b[1]) : a[0].CompareTo(b[0]));

            var lower = new List<double[]>();
            foreach (var p in uniq)
            {
                while (lower.Count >= 2 &&
                       Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0)
                    lower.RemoveAt(lower.Count - 1);
                lower.Add(p);
            }
            var upper = new List<double[]>();
            for (int i = uniq.Count - 1; i >= 0; i--)
            {
                var p = uniq[i];
                while (upper.Count >= 2 &&
                       Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0)
                    upper.RemoveAt(upper.Count - 1);
                upper.Add(p);
            }
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower.Count >= 3 ? lower : null;
        }

        private static double Cross(double[] o, double[] a, double[] b)
            => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);

        // ─── Transform assembly ──────────────────────────────────────────────

        // v -> M v + t, with M row-major 3x3.
        private static Transform BuildTransform(double[] M, double tx, double ty, double tz)
        {
            var x = Transform.Identity;
            x.M00 = M[0]; x.M01 = M[1]; x.M02 = M[2]; x.M03 = tx;
            x.M10 = M[3]; x.M11 = M[4]; x.M12 = M[5]; x.M13 = ty;
            x.M20 = M[6]; x.M21 = M[7]; x.M22 = M[8]; x.M23 = tz;
            x.M30 = 0; x.M31 = 0; x.M32 = 0; x.M33 = 1;
            return x;
        }

        // ─── Cell-key + floor-div helpers ────────────────────────────────────

        // Integer floor division for a coordinate into a grid of size `cell`.
        private static int FloorDiv(double coord, double cell)
            => (int)Math.Floor(coord / cell);

        // Pack two signed ints into one long key (range -2^20 .. 2^20 is ample
        // for these grids; shift keeps negatives distinct).
        private static long CellKey(int ix, int iy)
            => ((long)(ix + (1 << 30)) << 32) | (uint)(iy + (1 << 30));

        private static void DecodeKey(long key, out int ix, out int iy)
        {
            iy = (int)((uint)(key & 0xFFFFFFFFL)) - (1 << 30);
            ix = (int)(key >> 32) - (1 << 30);
        }

        private static int MinCellX(Dictionary<long, double[]> prof)
        {
            int min = int.MaxValue;
            foreach (var kv in prof)
            {
                DecodeKey(kv.Key, out int ix, out _);
                if (ix < min) min = ix;
            }
            return min == int.MaxValue ? 0 : min;
        }

        // ─── 3×3 symmetric Jacobi eigendecomposition (matches MeshPcaComponent) ─

        private static void JacobiEigen3(double[,] a, out double[] eigVals, out double[][] eigVecs)
        {
            double[,] A = (double[,])a.Clone();
            double[,] V = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

            const int maxSweeps = 50;
            for (int sweep = 0; sweep < maxSweeps; sweep++)
            {
                double off = Math.Abs(A[0, 1]) + Math.Abs(A[0, 2]) + Math.Abs(A[1, 2]);
                if (off < 1e-15) break;
                JacobiRotate(A, V, 0, 1);
                JacobiRotate(A, V, 0, 2);
                JacobiRotate(A, V, 1, 2);
            }

            eigVals = new[] { A[0, 0], A[1, 1], A[2, 2] };
            eigVecs = new double[3][];
            for (int k = 0; k < 3; k++)
                eigVecs[k] = new[] { V[0, k], V[1, k], V[2, k] };
        }

        private static void JacobiRotate(double[,] A, double[,] V, int p, int q)
        {
            double apq = A[p, q];
            if (Math.Abs(apq) < 1e-20) return;
            double app = A[p, p];
            double aqq = A[q, q];
            double theta = (aqq - app) / (2.0 * apq);
            double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(1.0 + theta * theta));
            if (theta == 0.0) t = 1.0;
            double c = 1.0 / Math.Sqrt(1.0 + t * t);
            double s = t * c;

            A[p, p] = app - t * apq;
            A[q, q] = aqq + t * apq;
            A[p, q] = 0.0;
            A[q, p] = 0.0;

            for (int r = 0; r < 3; r++)
            {
                if (r != p && r != q)
                {
                    double arp = A[r, p];
                    double arq = A[r, q];
                    A[r, p] = c * arp - s * arq;
                    A[p, r] = A[r, p];
                    A[r, q] = c * arq + s * arp;
                    A[q, r] = A[r, q];
                }
                double vrp = V[r, p];
                double vrq = V[r, q];
                V[r, p] = c * vrp - s * vrq;
                V[r, q] = c * vrq + s * vrp;
            }
        }
    }
}
