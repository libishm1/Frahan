using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;
using Frahan.Masonry.Sequencing;
using Frahan.Masonry.Solvers;

namespace Frahan.Demos.PolygonalArchElevation
{
    // =========================================================================
    // The SAME architectural brief as demos/StructuralStoneFacade case A1,
    // solved with the OTHER structural logic.
    //
    //   A1  coursed rectangular stone   openings spanned by a flat LINTEL
    //   P1  interlocking polygonal      openings spanned by a VOUSSOIR ARCH
    //
    // Identical envelope: 9.0 x 6.0 x 0.6 m wall; a central door 1.8 wide with
    // its head at 2.4 m; two windows 1.2 wide with sills at 3.0 and heads at
    // 4.8. Only the head detail and the bond differ, which is the whole point
    // of the comparison.
    //
    // HOW THE OPENING IS CUT. The polygonal wall is a PLANAR power diagram, so
    // subtracting an opening is a 2D boolean (Clipper2) rather than the 3D CGAL
    // difference used by examples/27_10_castle_keep - the cut is prismatic, so
    // the cheaper exact route is the correct one. The cut removes material out
    // to the arch EXTRADOS, and the voussoir ring is then laid back into that
    // annulus, bearing on the masonry at the springing exactly as a real arch
    // does. The cut boundary and the ring are generated from the SAME angular
    // samples, so the ring's extrados and the wall's cut edge are the same
    // polyline to the bit - which is what lets MeshContactDetector find exact
    // face-to-face contacts instead of near-misses.
    // =========================================================================
    internal static class Program
    {
        const double Density = 2650.0;      // granite, kg/m3
        const double Friction = 0.6;
        const double WallW = 9.0, WallH = 6.0, Depth = 0.6;

        sealed class Opening
        {
            public string Name;
            public double X0, X1;           // jamb faces
            public double Z0;               // sill (0 = door, to the ground)
            public double ZHead;            // crown of the intrados
            public double Ring = 0.45;      // voussoir ring thickness
            public int Voussoirs = 9;       // wedges over the half-circle

            public double Radius => 0.5 * (X1 - X0);
            public double CentreX => 0.5 * (X0 + X1);
            /// <summary>Springing level: the arch is a half circle, so the crown sits one radius above it.</summary>
            public double Spring => ZHead - Radius;
            public bool IsDoor => Z0 <= 1e-9;
        }

        sealed class Piece
        {
            public string Id;
            public bool IsVoussoir;
            /// <summary>Wall-plane outline. Y is the wall's vertical (world Z).</summary>
            public List<(double X, double Y)> Poly;
            public List<double> Coords;
            public List<int> Tris;
            public double MinZ, Area;
        }

        static void Main()
        {
            var openings = new List<Opening>
            {
                new Opening { Name = "door", X0 = 3.6, X1 = 5.4, Z0 = 0.0, ZHead = 2.4,
                              Ring = 0.45, Voussoirs = 9 },
                new Opening { Name = "W1",   X0 = 1.2, X1 = 2.4, Z0 = 3.0, ZHead = 4.8,
                              Ring = 0.30, Voussoirs = 5 },
                new Opening { Name = "W2",   X0 = 6.6, X1 = 7.8, Z0 = 3.0, ZHead = 4.8,
                              Ring = 0.30, Voussoirs = 5 },
            };

            Console.WriteLine("=== Polygonal arch elevation (P1) ===");
            Console.WriteLine($"the A1 brief solved polygonally: {WallW} x {WallH} x {Depth} m, "
                              + $"granite {Density} kg/m3, mu = {Friction}");
            Console.WriteLine();

            // ---- 1. the polygonal wall (shipping generator) --------------------
            var opts = new WallGenOptions
            {
                Width = WallW, Height = WallH,
                Coursing = 0.35,          // mostly irregular, a little bedding
                Courses = 8,
                GridX = 11, GridY = 8,
                Seed = 7,
                LloydIterations = 2,
                SizeGradeCv = 0.30,
                SliverMinInradiusFrac = 0.18,
                MaxSliverPasses = 3,
            };
            var wall = PolygonalWallGenerator.Generate(opts);
            Console.WriteLine($"wall pattern : {wall.Cells.Count} cells, interlock {wall.InterlockScore:F3}, "
                              + $"coverage {wall.AreaCoverage:P1}, area CV {wall.AreaCv:F2}, "
                              + $"cross vertices {wall.CrossVertexCount}");

            // ---- 2. cut the openings out (2D boolean, to the extrados) ---------
            var cutRegions = new List<List<(double X, double Y)>>();
            foreach (var op in openings) cutRegions.Add(CutRegion(op));

            var pieces = new List<Piece>();
            int cellsRemoved = 0, cellsTrimmed = 0, shards = 0;
            double shardArea = 0;
            double minKeepArea = 0.02;      // m2; below this a shard is not a stone

            foreach (var cell in wall.Cells)
            {
                var poly = new List<(double X, double Y)>();
                for (int i = 0; i < cell.VertexCount; i++) poly.Add((cell.Us[i], cell.Vs[i]));

                var current = new List<List<(double X, double Y)>> { poly };
                foreach (var region in cutRegions)
                {
                    var next = new List<List<(double X, double Y)>>();
                    foreach (var p in current) next.AddRange(Clipper2Adapter.Difference(p, region));
                    current = next;
                }

                if (current.Count == 0) { cellsRemoved++; continue; }
                double before = Math.Abs(SignedArea(poly));
                double after = 0;
                foreach (var p in current) after += Math.Abs(SignedArea(p));
                if (after < before - 1e-9) cellsTrimmed++;

                foreach (var p in current)
                {
                    double a = Math.Abs(SignedArea(p));
                    if (a < minKeepArea) { shards++; shardArea += a; continue; }
                    pieces.Add(MakePiece(p, false));
                }
            }
            Console.WriteLine($"openings cut : {openings.Count} ({cellsRemoved} cells removed, "
                              + $"{cellsTrimmed} trimmed into jamb/arch stones)");

            // No silent truncation: a cut can leave a shard too small to be a
            // structural unit. Those are dropped, and the drop is reported -
            // they show as small voids at the springings in the elevation.
            double idealArea = WallW * WallH;
            foreach (var op in openings)
                idealArea -= (op.X1 - op.X0) * (op.Spring - op.Z0)
                           + 0.5 * Math.PI * op.Radius * op.Radius;
            Console.WriteLine($"shards       : {shards} dropped below {minKeepArea:F3} m2 "
                              + $"({shardArea:F4} m2 total = {shardArea / idealArea:P2} of the wall face)");

            // ---- 3. lay the voussoir rings back into the cut -------------------
            int voussoirCount = 0;
            foreach (var op in openings)
                foreach (var v in Voussoirs(op)) { pieces.Add(MakePiece(v, true)); voussoirCount++; }
            Console.WriteLine($"voussoirs    : {voussoirCount} "
                              + $"({openings[0].Voussoirs} over the door, {openings[1].Voussoirs} per window)");

            // ---- 4. the shipping structural chain ------------------------------
            MasonrySolverRegistry.Default = null;      // deterministic managed lane
            for (int i = 0; i < pieces.Count; i++)
                pieces[i].Id = (pieces[i].IsVoussoir ? "v" : "s")
                             + i.ToString("000", CultureInfo.InvariantCulture);

            // Independent geometric check before any structural claim: the ring
            // is laid back into the annulus the cut removed, so if the cut
            // boundary and the extrados ever drifted apart, stones would
            // interpenetrate and every force result would be meaningless.
            int overlaps = 0;
            double worstOverlap = 0;
            for (int i = 0; i < pieces.Count; i++)
                for (int j = i + 1; j < pieces.Count; j++)
                {
                    if (!BoxesTouch(pieces[i], pieces[j])) continue;
                    double a = 0;
                    foreach (var loop in Clipper2Adapter.Intersect(pieces[i].Poly, pieces[j].Poly))
                        a += Math.Abs(SignedArea(loop));
                    if (a > 1e-6) { overlaps++; worstOverlap = Math.Max(worstOverlap, a); }
                }
            Console.WriteLine($"overlap check: {overlaps} overlapping pairs"
                              + (overlaps > 0 ? $", worst {worstOverlap:F6} m2" : " (exact tiling)"));

            var asm = BuildAssembly(pieces, out int fixedCount);
            Console.WriteLine();
            Console.WriteLine($"assembly     : {pieces.Count} stones, {asm.InterfaceCount} interfaces, "
                              + $"{fixedCount} fixed (lowest course)");

            var res = MasonryStabilityChecker.CheckDetailed(asm, Friction).Result;
            Console.WriteLine($"verdict      : {(res.IsStable ? "STABLE" : "UNSTABLE")} ({res.Status})"
                              + $", friction utilisation {res.MaxFrictionUtilization:P1}");

            var order = new Dictionary<string, int>();
            int layers = 0;
            try
            {
                var steps = BlockBuildOrderer.Solve(asm);
                foreach (var s in steps) { order[s.BlockId] = s.OrderIndex; layers = Math.Max(layers, s.Layer + 1); }
                Console.WriteLine($"build order  : {steps.Count} steps / {layers} layers");
            }
            catch (Exception ex)
            {
                Console.WriteLine("build order  : FAILED - " + ex.Message);
            }

            double vol = 0;
            foreach (var p in pieces) vol += p.Area * Depth;
            Console.WriteLine($"stone        : {vol:F2} m3 ({vol * Density / 1000.0:F1} t)");

            // ---- 5. emit -------------------------------------------------------
            string outDir = Path.Combine("D:", "code_ws", "outputs", "2026-07-25", "structural_stone_facade");
            Directory.CreateDirectory(outDir);
            string json = Path.Combine(outDir, "polygonal_arch.json");
            WriteJson(json, pieces, order, res.IsStable, res.Status.ToString(),
                      res.MaxFrictionUtilization, wall, asm.InterfaceCount, fixedCount, layers, vol);
            Console.WriteLine("wrote " + json);

            Environment.Exit(res.IsStable ? 0 : 1);
        }

        // =====================================================================
        // Opening geometry
        // =====================================================================

        /// <summary>
        /// The region removed from the wall: the rectangular jamb opening below
        /// the springing, plus the half disc out to the arch EXTRADOS. Cutting
        /// to the extrados (not the intrados) is what leaves room for the ring
        /// and gives it a bearing ledge at the springing.
        /// </summary>
        static List<(double X, double Y)> CutRegion(Opening op)
        {
            double xc = op.CentreX, zs = op.Spring, r = op.Radius, R = r + op.Ring;
            var p = new List<(double X, double Y)>
            {
                (op.X0, op.Z0), (op.X1, op.Z0), (op.X1, zs), (xc + R, zs),
            };
            for (int k = 0; k <= op.Voussoirs; k++)
            {
                double t = Math.PI * k / op.Voussoirs;
                p.Add((xc + R * Math.Cos(t), zs + R * Math.Sin(t)));
            }
            p.Add((op.X0, zs));
            return EnsureCcw(p);
        }

        /// <summary>
        /// The voussoir ring: wedges between intrados r and extrados r+Ring,
        /// on the SAME angular samples as <see cref="CutRegion"/> so the ring's
        /// outer face and the wall's cut edge coincide exactly.
        /// </summary>
        static List<List<(double X, double Y)>> Voussoirs(Opening op)
        {
            double xc = op.CentreX, zs = op.Spring, r = op.Radius, R = r + op.Ring;
            var outp = new List<List<(double X, double Y)>>();
            for (int k = 0; k < op.Voussoirs; k++)
            {
                double t0 = Math.PI * k / op.Voussoirs, t1 = Math.PI * (k + 1) / op.Voussoirs;
                var q = new List<(double X, double Y)>
                {
                    (xc + r * Math.Cos(t0), zs + r * Math.Sin(t0)),
                    (xc + r * Math.Cos(t1), zs + r * Math.Sin(t1)),
                    (xc + R * Math.Cos(t1), zs + R * Math.Sin(t1)),
                    (xc + R * Math.Cos(t0), zs + R * Math.Sin(t0)),
                };
                outp.Add(EnsureCcw(q));
            }
            return outp;
        }

        // =====================================================================
        // Polygon helpers
        // =====================================================================

        /// <summary>Cheap reject before the exact 2D intersection test.</summary>
        static bool BoxesTouch(Piece a, Piece b)
        {
            double ax0 = double.MaxValue, ax1 = double.MinValue, ay0 = double.MaxValue, ay1 = double.MinValue;
            foreach (var p in a.Poly) { ax0 = Math.Min(ax0, p.X); ax1 = Math.Max(ax1, p.X); ay0 = Math.Min(ay0, p.Y); ay1 = Math.Max(ay1, p.Y); }
            double bx0 = double.MaxValue, bx1 = double.MinValue, by0 = double.MaxValue, by1 = double.MinValue;
            foreach (var p in b.Poly) { bx0 = Math.Min(bx0, p.X); bx1 = Math.Max(bx1, p.X); by0 = Math.Min(by0, p.Y); by1 = Math.Max(by1, p.Y); }
            return ax0 < bx1 - 1e-9 && bx0 < ax1 - 1e-9 && ay0 < by1 - 1e-9 && by0 < ay1 - 1e-9;
        }

        static double SignedArea(IReadOnlyList<(double X, double Y)> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var u = p[i]; var v = p[(i + 1) % p.Count];
                a += u.X * v.Y - v.X * u.Y;
            }
            return 0.5 * a;
        }

        static List<(double X, double Y)> EnsureCcw(List<(double X, double Y)> p)
        {
            if (SignedArea(p) < 0) p.Reverse();
            return p;
        }

        /// <summary>Ear clipping. The cut cells are simple but not always convex.</summary>
        static List<int> Triangulate(IReadOnlyList<(double X, double Y)> p)
        {
            int n = p.Count;
            var tris = new List<int>();
            if (n < 3) return tris;

            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);

            int guard = 0;
            while (idx.Count > 3 && guard++ < 10 * n)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    var a = p[ia]; var b = p[ib]; var c = p[ic];
                    double cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                    if (cross <= 1e-12) continue;                       // reflex or degenerate

                    bool contains = false;
                    for (int j = 0; j < idx.Count && !contains; j++)
                    {
                        int ip = idx[j];
                        if (ip == ia || ip == ib || ip == ic) continue;
                        if (PointInTriangle(p[ip], a, b, c)) contains = true;
                    }
                    if (contains) continue;

                    tris.Add(ia); tris.Add(ib); tris.Add(ic);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;        // degenerate; fall through to a fan
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            else if (tris.Count == 0)
                for (int i = 1; i + 1 < n; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
            return tris;
        }

        static bool PointInTriangle((double X, double Y) p, (double X, double Y) a,
                                    (double X, double Y) b, (double X, double Y) c)
        {
            double d1 = (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
            double d2 = (p.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (p.Y - c.Y);
            double d3 = (p.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (p.Y - a.Y);
            bool neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool pos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(neg && pos);
        }

        // =====================================================================
        // Prism meshing
        // =====================================================================

        /// <summary>
        /// Extrudes a wall-plane polygon through the wall depth (y = 0..Depth)
        /// into a closed triangle mesh, then fixes the winding by signed volume
        /// so every face normal points outward.
        /// </summary>
        static Piece MakePiece(List<(double X, double Y)> poly, bool isVoussoir)
        {
            poly = Dedupe(EnsureCcw(poly));
            int n = poly.Count;
            var coords = new List<double>(6 * n);
            for (int i = 0; i < n; i++) { coords.Add(poly[i].X); coords.Add(0.0);   coords.Add(poly[i].Y); }
            for (int i = 0; i < n; i++) { coords.Add(poly[i].X); coords.Add(Depth); coords.Add(poly[i].Y); }

            var cap = Triangulate(poly);
            var tris = new List<int>();
            for (int t = 0; t + 2 < cap.Count; t += 3)
            {
                tris.Add(cap[t]); tris.Add(cap[t + 1]); tris.Add(cap[t + 2]);                 // y = 0
                tris.Add(n + cap[t]); tris.Add(n + cap[t + 2]); tris.Add(n + cap[t + 1]);     // y = Depth
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                tris.Add(i); tris.Add(n + i); tris.Add(n + j);
                tris.Add(i); tris.Add(n + j); tris.Add(j);
            }

            if (SignedVolume(coords, tris) < 0)
                for (int t = 0; t + 2 < tris.Count; t += 3)
                { int tmp = tris[t + 1]; tris[t + 1] = tris[t + 2]; tris[t + 2] = tmp; }

            double minZ = double.MaxValue;
            for (int i = 0; i < n; i++) minZ = Math.Min(minZ, poly[i].Y);

            return new Piece
            {
                IsVoussoir = isVoussoir, Poly = poly, Coords = coords, Tris = tris,
                MinZ = minZ, Area = Math.Abs(SignedArea(poly)),
            };
        }

        static List<(double X, double Y)> Dedupe(List<(double X, double Y)> p)
        {
            var q = new List<(double X, double Y)>(p.Count);
            for (int i = 0; i < p.Count; i++)
            {
                var a = p[i]; var b = p[(i + 1) % p.Count];
                if (Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9) continue;
                q.Add(a);
            }
            return q.Count >= 3 ? q : p;
        }

        static double SignedVolume(List<double> coords, List<int> tris)
        {
            double v = 0;
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int a = 3 * tris[t], b = 3 * tris[t + 1], c = 3 * tris[t + 2];
                double ax = coords[a], ay = coords[a + 1], az = coords[a + 2];
                double bx = coords[b], by = coords[b + 1], bz = coords[b + 2];
                double cx = coords[c], cy = coords[c + 1], cz = coords[c + 2];
                v += ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx);
            }
            return v / 6.0;
        }

        // =====================================================================
        // Assembly
        // =====================================================================

        static MasonryAssembly BuildAssembly(List<Piece> pieces, out int fixedCount)
        {
            var snaps = new List<MeshSnapshot>();
            var ids = new List<string>();
            foreach (var p in pieces) { snaps.Add(new MeshSnapshot(p.Coords, p.Tris)); ids.Add(p.Id); }

            var interfaces = MeshContactDetector.Detect(snaps, ids);

            double globalMinZ = double.MaxValue;
            foreach (var p in pieces) globalMinZ = Math.Min(globalMinZ, p.MinZ);

            var blocks = new List<MasonryBlock>();
            var fixedIds = new List<string>();
            foreach (var p in pieces)
            {
                blocks.Add(new MasonryBlock(p.Id, p.Coords, p.Tris, Density));
                if (p.MinZ <= globalMinZ + 1e-6) fixedIds.Add(p.Id);
            }
            fixedCount = fixedIds.Count;
            return new MasonryAssembly(blocks, interfaces, new BoundaryConditions(fixedIds));
        }

        // =====================================================================
        // Emit
        // =====================================================================

        static void WriteJson(string path, List<Piece> pieces, Dictionary<string, int> order,
            bool stable, string status, double util, WallGenResult wall,
            int interfaceCount, int fixedCount, int layers, double volume)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"kind\": \"polygonal-arch\",\n");
            sb.Append("  \"wall\": {\"w\": ").Append(WallW.ToString(inv))
              .Append(", \"h\": ").Append(WallH.ToString(inv))
              .Append(", \"depth\": ").Append(Depth.ToString(inv)).Append("},\n");
            sb.Append("  \"stable\": ").Append(stable ? "true" : "false").Append(",\n");
            sb.Append("  \"status\": \"").Append(status).Append("\",\n");
            sb.Append("  \"frictionUtilisation\": ").Append(util.ToString("0.#####", inv)).Append(",\n");
            sb.Append("  \"interfaces\": ").Append(interfaceCount.ToString(inv)).Append(",\n");
            sb.Append("  \"fixed\": ").Append(fixedCount.ToString(inv)).Append(",\n");
            sb.Append("  \"layers\": ").Append(layers.ToString(inv)).Append(",\n");
            sb.Append("  \"volume\": ").Append(volume.ToString("0.####", inv)).Append(",\n");
            sb.Append("  \"interlock\": ").Append(wall.InterlockScore.ToString("0.####", inv)).Append(",\n");
            sb.Append("  \"crossVertices\": ").Append(wall.CrossVertexCount.ToString(inv)).Append(",\n");
            sb.Append("  \"blocks\": [\n");
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                int ord;
                sb.Append("    {\"id\":\"").Append(p.Id).Append("\",\"voussoir\":")
                  .Append(p.IsVoussoir ? "true" : "false")
                  .Append(",\"order\":").Append(order.TryGetValue(p.Id, out ord) ? ord.ToString(inv) : "null")
                  .Append(",\"poly\":[");
                for (int k = 0; k < p.Poly.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append('[').Append(p.Poly[k].X.ToString("0.####", inv)).Append(',')
                      .Append(p.Poly[k].Y.ToString("0.####", inv)).Append(']');
                }
                sb.Append("]}").Append(i + 1 < pieces.Count ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            File.WriteAllText(path, sb.ToString());
        }
    }
}
