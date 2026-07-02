#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Rhino.Geometry;

namespace Frahan.Masonry.Library
{
    // =========================================================================
    // StoneLibrary — load a CATALOGUE of real rubble stones (.obj scans, e.g. the
    // ETH1100 set) from a folder, tagged + measured, so a designer can SELECT
    // stones by category/lithology and MATCH them onto a vault's cells. This is
    // the published primitive behind the project's core contribution ("select a
    // rubble stone from a category/library and match it onto the vault"). It
    // promotes the .obj parse + deterministic-shuffle + chunky-AR-filter that were
    // previously private inside VaultStoneFitter, and adds provenance (filename),
    // lithology (the .obj's sub-folder), a shape Category, and cached metrics
    // (volume + sorted world-axis extents) used by the matcher's prefilter.
    //
    // Reused by: the vault stone-cell match, the VaultStoneFitter trim stage, the
    // Nbo dry-stone wall, and the voussoir matcher -- so it is its own primitive.
    // =========================================================================
    public enum StoneCategory { Blocky, Platey, Elongated }

    /// <summary>One catalogued library stone: mesh + provenance + cached metrics.</summary>
    public sealed class LibraryStone
    {
        public Mesh Mesh;
        public string SourceName;     // file name (provenance)
        public string Lithology;      // immediate sub-folder name, or "" at the root
        public StoneCategory Category;
        public double Volume;         // mesh volume (m^3), bbox fallback if not solid
        // world-axis-aligned principal dims (the .obj is assumed pre-oriented), sorted
        public Vector3d ThinAxis; public double ThinDim;
        public Vector3d MidAxis; public double MidDim;
        public Vector3d LongAxis; public double LongDim;
    }

    public sealed class StoneLibraryOptions
    {
        public int Seed = 1;
        public int MaxCount = 0;          // 0 = all
        public double ArMax = 4.0;        // chunky filter: long/thin <= ArMax (0 = no filter)
        public bool Recursive = false;
        public string LithologyFilter = null;   // keep only this sub-folder (case-insensitive); null = all
        public StoneCategory? CategoryFilter = null;  // keep only this category; null = all
    }

    public static class StoneLibraryLoader
    {
        /// <summary>Parse a Wavefront .obj into a Rhino Mesh (v + f only; tris/quads/ngons).</summary>
        public static Mesh LoadObj(string path)
        {
            var verts = new List<Point3d>();
            var m = new Mesh();
            foreach (string raw in File.ReadLines(path))
            {
                if (raw.Length < 2) continue;
                if (raw[0] == 'v' && raw[1] == ' ')
                {
                    string[] pp = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    verts.Add(new Point3d(
                        double.Parse(pp[1], CultureInfo.InvariantCulture),
                        double.Parse(pp[2], CultureInfo.InvariantCulture),
                        double.Parse(pp[3], CultureInfo.InvariantCulture)));
                }
                else if (raw[0] == 'f' && raw[1] == ' ')
                {
                    string[] pp = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    var idx = new List<int>(pp.Length - 1);
                    for (int k = 1; k < pp.Length; k++)
                    {
                        string tok = pp[k];
                        int slash = tok.IndexOf('/');
                        if (slash >= 0) tok = tok.Substring(0, slash);
                        idx.Add(int.Parse(tok, CultureInfo.InvariantCulture) - 1);
                    }
                    if (idx.Count == 3) m.Faces.AddFace(idx[0], idx[1], idx[2]);
                    else if (idx.Count == 4) m.Faces.AddFace(idx[0], idx[1], idx[2], idx[3]);
                    else for (int t = 1; t < idx.Count - 1; t++) m.Faces.AddFace(idx[0], idx[t], idx[t + 1]);
                }
            }
            for (int i = 0; i < verts.Count; i++) m.Vertices.Add(verts[i]);
            m.Normals.ComputeNormals();
            return m;
        }

        /// <summary>
        /// Load + tag + measure a stone library from a folder. Centers each stone, sorts its
        /// world-axis extents (thin/mid/long), classifies a Category, reads lithology from the
        /// sub-folder, applies the chunky-AR + lithology + category filters, then deterministically
        /// shuffles by Seed and keeps the first MaxCount.
        /// </summary>
        public static List<LibraryStone> Load(string folder, StoneLibraryOptions opt = null)
        {
            opt = opt ?? new StoneLibraryOptions();
            var pool = new List<LibraryStone>();
            // Fallback (2026-07-02): missing machine-specific path -> the bundled
            // 16-stone ETH1100 subset beside the plugin (see VaultStoneFitter).
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                folder = Frahan.Masonry.Vault.VaultStoneFitter.BundledSubsetDir();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return pool;

            var files = new List<string>(Directory.GetFiles(folder, "*.obj",
                opt.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
            files.Sort(StringComparer.Ordinal);
            // deterministic shuffle (Fisher-Yates) so a Seed gives a repeatable subset
            var rng = new Random(opt.Seed);
            for (int i = files.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                string tmp = files[i]; files[i] = files[j]; files[j] = tmp;
            }

            var axisOf = new[] { new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1) };
            foreach (string file in files)
            {
                if (opt.MaxCount > 0 && pool.Count >= opt.MaxCount) break;

                string lith = LithologyOf(folder, file);
                if (!string.IsNullOrEmpty(opt.LithologyFilter) &&
                    !string.Equals(lith, opt.LithologyFilter, StringComparison.OrdinalIgnoreCase)) continue;

                Mesh mm;
                try { mm = LoadObj(file); } catch { continue; }
                if (mm.Faces.Count == 0) continue;

                BoundingBox bb = mm.GetBoundingBox(true);
                Point3d cen = bb.Center;
                mm.Translate(-cen.X, -cen.Y, -cen.Z);
                bb = mm.GetBoundingBox(true);

                var dims = new[] { bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y, bb.Max.Z - bb.Min.Z };
                var order = new[] { 0, 1, 2 };
                for (int a = 0; a < 3; a++)
                    for (int b = a + 1; b < 3; b++)
                        if (dims[order[b]] < dims[order[a]]) { int t = order[a]; order[a] = order[b]; order[b] = t; }

                double td = dims[order[0]], md = dims[order[1]], kd = dims[order[2]];
                if (td < 1e-4) continue;
                if (opt.ArMax > 0 && kd / td > opt.ArMax) continue;   // chunky filter

                var cat = Classify(td, md, kd);
                if (opt.CategoryFilter.HasValue && cat != opt.CategoryFilter.Value) continue;

                double vol;
                try
                {
                    var vm = VolumeMassProperties.Compute(mm);
                    double v = vm != null ? Math.Abs(vm.Volume) : double.NaN;
                    vol = (v > 0.0 && !double.IsNaN(v) && !double.IsInfinity(v)) ? v : td * md * kd;
                }
                catch { vol = td * md * kd; }

                pool.Add(new LibraryStone
                {
                    Mesh = mm,
                    SourceName = Path.GetFileName(file),
                    Lithology = lith,
                    Category = cat,
                    Volume = vol,
                    ThinAxis = axisOf[order[0]], ThinDim = td,
                    MidAxis = axisOf[order[1]], MidDim = md,
                    LongAxis = axisOf[order[2]], LongDim = kd,
                });
            }
            return pool;
        }

        // shape category from sorted dims: long >> mid -> Elongated; mid >> thin -> Platey; else Blocky
        static StoneCategory Classify(double thin, double mid, double lng)
        {
            if (lng > 2.0 * mid) return StoneCategory.Elongated;
            if (mid > 2.0 * thin) return StoneCategory.Platey;
            return StoneCategory.Blocky;
        }

        static string LithologyOf(string root, string file)
        {
            try
            {
                string dir = Path.GetDirectoryName(file);
                string rootFull = Path.GetFullPath(root).TrimEnd('\\', '/');
                string dirFull = Path.GetFullPath(dir).TrimEnd('\\', '/');
                if (string.Equals(dirFull, rootFull, StringComparison.OrdinalIgnoreCase)) return "";
                // immediate sub-folder name under root
                string rel = dirFull.Substring(rootFull.Length).TrimStart('\\', '/');
                int sep = rel.IndexOfAny(new[] { '\\', '/' });
                return sep < 0 ? rel : rel.Substring(0, sep);
            }
            catch { return ""; }
        }
    }
}
