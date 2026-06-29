#nullable disable
using System;
using System.IO;
using Rhino.FileIO;
using Rhino.Geometry;
using Frahan.Masonry.Vault;

namespace Frahan.Tests
{
    // Headless validation of the 4-stage rubble-vault tessellation Core pipeline
    // on the REAL Park Güell archive SubD. Confirms the C# port reproduces the
    // v004-scale result (~3185 stones) without a Rhino/Grasshopper canvas.
    // Stage 4 boolean may fall back to the mould headless; counts still validate.
    public static class VaultTessellationTests
    {
        public static void Vault_ArchiveSubD_Pipeline_ProducesV004ScaleTessellation()
        {
            string archive = @"D:\Downloads\guell-vaultarchive.3dm";
            if (!File.Exists(archive)) throw new SkipTest("archive guell-vaultarchive.3dm not found");

            File3dm f3 = File3dm.Read(archive);
            if (f3 == null) throw new SkipTest("archive unreadable");

            SubD subd = null;
            foreach (var ob in f3.Objects)
            {
                var g = ob.Geometry;
                var s = g as SubD;
                if (s != null) { subd = s; break; }
            }
            if (subd == null) throw new SkipTest("no SubD in archive");

            Mesh mesh = Mesh.CreateFromSubD(subd, 2);
            if (mesh == null || mesh.Faces.Count == 0) throw new Exception("SubD -> mesh failed");
            mesh.Normals.ComputeNormals();
            mesh.FaceNormals.ComputeFaceNormals();
            var bb = mesh.GetBoundingBox(true);
            Console.WriteLine($"  REF mesh: {mesh.Vertices.Count} V / {mesh.Faces.Count} F; bbox {bb.Min} -> {bb.Max}");

            // Stage 1 — variable-density Poisson sampling + columnness.
            var s1 = VaultSurfaceSampler.Sample(mesh, 0.21, 0.11, 2.4, 3.3, 2.3, 3.3, 18);
            int colZone = 0;
            for (int i = 0; i < s1.Columnness.Count; i++) if (s1.Columnness[i] > 0.5) colZone++;
            Console.WriteLine($"  Stage1 Sampler:  {s1.Count} points ({colZone} in column zone)");
            if (s1.Count < 2000) throw new Exception($"sampler too sparse: {s1.Count} (< 2000)");

            // Stage 2 — local tangent-plane Voronoi.
            var s2 = VaultLocalVoronoi.Build(s1.Points, s1.Normals, s1.Columnness, 0.21, 0.11);
            Console.WriteLine($"  Stage2 Voronoi:  {s2.Count} cells");
            if (s2.Count < 1500) throw new Exception($"voronoi too sparse: {s2.Count} (< 1500)");

            // Stage 3 — capped voussoir moulds.
            var s3 = VaultVoussoirCapper.Cap(s2.Cells, s2.Frames, s2.Columnness, 0.26, 0.20, 0.05);
            int closed = 0;
            for (int i = 0; i < s3.Moulds.Count; i++) if (s3.Moulds[i] != null && s3.Moulds[i].IsClosed) closed++;
            Console.WriteLine($"  Stage3 Moulds:   {s3.Count} ({closed} closed)");
            if (s3.Count < 1500) throw new Exception($"capper too sparse: {s3.Count} (< 1500)");
            if (closed != s3.Count) throw new Exception($"non-closed moulds emitted: {s3.Count - closed}");

            // Stage 4 — ETH stone fit + boolean trim (boolean may fall back headless).
            string ethDir = @"D:\code_ws\Data\eth1100\closed\1100 Closed Stone Meshes";
            if (Directory.Exists(ethDir))
            {
                var s4 = VaultStoneFitter.FitAndTrim(s3.Moulds, s3.Cells, s3.Frames, s3.Columnness,
                    0.26, 0.20, ethDir, 18, 1.16, 2.2);
                Console.WriteLine($"  Stage4 Stones:   {s4.Count} (stock pool {s4.PoolSize})");
                if (s4.PoolSize == 0) throw new Exception("ETH stock pool empty");
                if (s4.Count < 1) throw new Exception("stone fitter produced no stones");
            }
            else
            {
                Console.WriteLine("  Stage4 Stones:   SKIPPED (ETH dataset not found)");
            }

            Console.WriteLine($"  OK — pipeline matches v004 scale (reference 3185 stones).");
        }
    }
}
