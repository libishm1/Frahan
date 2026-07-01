#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Frahan.Masonry.DataModel;

namespace Frahan.Masonry.Solvers
{
    // =========================================================================
    // CraAssemblyIO — compact binary (de)serialization of a MasonryAssembly + the
    // CRA solve parameters, and of the verdict. Rhino-free, so the SAME code links
    // into both Frahan.StonePack.Core (GH side writes the input, reads the result)
    // and frahan_cra_worker (reads the input, writes the result). Binary keeps the
    // out-of-process hop cheap and dependency-free (no JSON package on net48).
    // Format version 1.
    // =========================================================================
    public static class CraAssemblyIO
    {
        private const int Magic = 0x43524131; // "CRA1"

        public static void WriteAssembly(Stream s, MasonryAssembly a,
            double mu, int faceCount, bool inscribed, double tangentialScale, double gravityZ)
        {
            var w = new BinaryWriter(s);
            w.Write(Magic);
            w.Write(mu); w.Write(faceCount); w.Write(inscribed); w.Write(tangentialScale); w.Write(gravityZ);

            var blocks = a.Blocks;
            w.Write(blocks.Count);
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                w.Write(b.Id);
                w.Write(b.Density);
                var c = b.VertexCoordsXyz; w.Write(c.Count);
                for (int k = 0; k < c.Count; k++) w.Write(c[k]);
                var t = b.TriangleIndices; w.Write(t.Count);
                for (int k = 0; k < t.Count; k++) w.Write(t[k]);
            }

            var ifs = a.Interfaces;
            w.Write(ifs.Count);
            for (int i = 0; i < ifs.Count; i++)
            {
                var f = ifs[i];
                w.Write(f.BlockAId); w.Write(f.BlockBId);
                var p = f.ContactPolygon; w.Write(p.Count);
                for (int k = 0; k < p.Count; k++) { w.Write(p[k].X); w.Write(p[k].Y); w.Write(p[k].Z); }
                w.Write(f.NormalX); w.Write(f.NormalY); w.Write(f.NormalZ);
                w.Write(f.Tangent1X); w.Write(f.Tangent1Y); w.Write(f.Tangent1Z);
                w.Write(f.Tangent2X); w.Write(f.Tangent2Y); w.Write(f.Tangent2Z);
            }

            var fixedIds = new List<string>(a.BoundaryConditions.FixedBlockIds);
            w.Write(fixedIds.Count);
            for (int i = 0; i < fixedIds.Count; i++) w.Write(fixedIds[i]);
            w.Flush();
        }

        public static MasonryAssembly ReadAssembly(Stream s,
            out double mu, out int faceCount, out bool inscribed, out double tangentialScale, out double gravityZ)
        {
            var r = new BinaryReader(s);
            if (r.ReadInt32() != Magic) throw new InvalidDataException("bad CRA assembly magic");
            mu = r.ReadDouble(); faceCount = r.ReadInt32(); inscribed = r.ReadBoolean();
            tangentialScale = r.ReadDouble(); gravityZ = r.ReadDouble();

            int nb = r.ReadInt32();
            var blocks = new List<MasonryBlock>(nb);
            for (int i = 0; i < nb; i++)
            {
                string id = r.ReadString();
                double density = r.ReadDouble();
                int nc = r.ReadInt32(); var c = new double[nc]; for (int k = 0; k < nc; k++) c[k] = r.ReadDouble();
                int nt = r.ReadInt32(); var t = new int[nt]; for (int k = 0; k < nt; k++) t[k] = r.ReadInt32();
                blocks.Add(new MasonryBlock(id, c, t, density));
            }

            int nf = r.ReadInt32();
            var ifs = new List<MasonryInterface>(nf);
            for (int i = 0; i < nf; i++)
            {
                string a = r.ReadString(), b = r.ReadString();
                int np = r.ReadInt32();
                var poly = new List<ContactVertex>(np);
                for (int k = 0; k < np; k++) poly.Add(new ContactVertex(r.ReadDouble(), r.ReadDouble(), r.ReadDouble()));
                double nx = r.ReadDouble(), ny = r.ReadDouble(), nz = r.ReadDouble();
                double t1x = r.ReadDouble(), t1y = r.ReadDouble(), t1z = r.ReadDouble();
                double t2x = r.ReadDouble(), t2y = r.ReadDouble(), t2z = r.ReadDouble();
                ifs.Add(new MasonryInterface(a, b, poly, nx, ny, nz, t1x, t1y, t1z, t2x, t2y, t2z));
            }

            int nx2 = r.ReadInt32();
            var fixedIds = new List<string>(nx2);
            for (int i = 0; i < nx2; i++) fixedIds.Add(r.ReadString());

            return new MasonryAssembly(blocks, ifs, new BoundaryConditions(fixedIds));
        }

        public static void WriteResult(Stream s, bool isStable, int status, double maxCompression, string message)
        {
            var w = new BinaryWriter(s);
            w.Write(isStable); w.Write(status); w.Write(maxCompression); w.Write(message ?? "");
            w.Flush();
        }

        public static void ReadResult(Stream s, out bool isStable, out int status, out double maxCompression, out string message)
        {
            var r = new BinaryReader(s);
            isStable = r.ReadBoolean(); status = r.ReadInt32(); maxCompression = r.ReadDouble(); message = r.ReadString();
        }
    }
}
