using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// frahan_recon_worker — out-of-process surface reconstruction.
//
// Runs ONE native reconstruction in an isolated child process so that a hard
// native crash (e.g. a C++ abort 0xC0000409 from geogram/CGAL when the HOST has
// FP exceptions unmasked, or any access violation) takes down only THIS process,
// never Rhino. The parent (OutOfProcessReconstructor in Frahan.StonePack.Core)
// detects a crash via the exit code / missing output magic and reports a clean
// error instead of dying.
//
// Usage:  frahan_recon_worker.exe <input.bin> <output.bin>
//
// INPUT  (little-endian): int32 mode; double alpha; int32 depth; double spn;
//        double rr; double bt; int32 nPts; double[3*nPts] pts;
//        int32 hasNormals; (if 1) double[3*nPts] normals
//   mode: 1=AlphaShape(CGAL) 2=Poisson(geogram) 3=AdvancingFront(CGAL) 4=Poisson(CGAL)
// OUTPUT (little-endian): int32 MAGIC(0x46524543 'FREC'); int32 status(0=ok,1=fail);
//        if ok: int32 nVerts; double[3*nVerts]; int32 nTris; int32[3*nTris];
//        int32 errLen; byte[errLen] (UTF-8; empty when ok)
internal static class Program
{
    private const int MAGIC = 0x46524543; // 'FREC'

    [DllImport("frahan_geogram", CallingConvention = CallingConvention.Cdecl)]
    private static extern int frahan_geogram_poisson_reconstruct(
        double[] points, int pcount, double[] normals, int depth, double spn,
        out IntPtr v, out int vc, out IntPtr t, out int tc);
    [DllImport("frahan_geogram", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr frahan_geogram_last_error();
    [DllImport("frahan_geogram", CallingConvention = CallingConvention.Cdecl)]
    private static extern void frahan_geogram_free_pdouble(IntPtr p);
    [DllImport("frahan_geogram", CallingConvention = CallingConvention.Cdecl)]
    private static extern void frahan_geogram_free_pint(IntPtr p);

    [DllImport("frahan_cgal", CallingConvention = CallingConvention.Cdecl)]
    private static extern int frahan_cgal_alpha_shape_3(
        double[] points, int pcount, double alpha,
        out IntPtr v, out int vc, out IntPtr t, out int tc);
    [DllImport("frahan_cgal", CallingConvention = CallingConvention.Cdecl)]
    private static extern int frahan_cgal_advancing_front_reconstruct(
        double[] points, int pcount, double radiusRatio, double beta,
        out IntPtr v, out int vc, out IntPtr t, out int tc);
    [DllImport("frahan_cgal", CallingConvention = CallingConvention.Cdecl)]
    private static extern int frahan_cgal_poisson_reconstruct(
        double[] points, int pcount, double[] normals,
        double smAngle, double smRadius, double smDistance,
        out IntPtr v, out int vc, out IntPtr t, out int tc);
    [DllImport("frahan_cgal", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr frahan_cgal_last_error();
    [DllImport("frahan_cgal", CallingConvention = CallingConvention.Cdecl)]
    private static extern void frahan_cgal_free_buffers(IntPtr v, IntPtr t);

    private static string Err(IntPtr p) => p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p);

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: frahan_recon_worker <input.bin> <output.bin>");
            return 2;
        }
        try
        {
            RunReconstruction(args[0], args[1]);
            return 0;
        }
        catch (Exception ex)
        {
            // Handled managed failure: still write a status=1 output so the
            // parent can read the message (exit 0 = "completed, see status").
            try { WriteFail(args[1], ex.GetType().Name + ": " + ex.Message); } catch { }
            return 0;
        }
        // NOTE: a NATIVE crash (abort / AV) bypasses this and terminates the
        // process with a nonzero exit and no valid output -> parent detects it.
    }

    private static void RunReconstruction(string inPath, string outPath)
    {
        byte[] inb = File.ReadAllBytes(inPath);
        int o = 0;
        int mode = ReadI(inb, ref o);
        double alpha = ReadD(inb, ref o);
        int depth = ReadI(inb, ref o);
        double spn = ReadD(inb, ref o);
        double rr = ReadD(inb, ref o);
        double bt = ReadD(inb, ref o);
        int nPts = ReadI(inb, ref o);
        var pts = new double[3 * nPts];
        Buffer.BlockCopy(inb, o, pts, 0, 3 * nPts * sizeof(double)); o += 3 * nPts * sizeof(double);
        int hasN = ReadI(inb, ref o);
        double[] nrm = null;
        if (hasN != 0)
        {
            nrm = new double[3 * nPts];
            Buffer.BlockCopy(inb, o, nrm, 0, 3 * nPts * sizeof(double)); o += 3 * nPts * sizeof(double);
        }

        int rc; IntPtr v, t; int vc, tc; string err;
        switch (mode)
        {
            case 1:
                rc = frahan_cgal_alpha_shape_3(pts, nPts, alpha, out v, out vc, out t, out tc);
                err = Err(frahan_cgal_last_error());
                WriteResult(outPath, rc, v, vc, t, tc, err, cgal: true);
                break;
            case 2:
                rc = frahan_geogram_poisson_reconstruct(pts, nPts, nrm, depth, spn, out v, out vc, out t, out tc);
                err = Err(frahan_geogram_last_error());
                WriteResult(outPath, rc, v, vc, t, tc, err, cgal: false);
                break;
            case 3:
                rc = frahan_cgal_advancing_front_reconstruct(pts, nPts, rr, bt, out v, out vc, out t, out tc);
                err = Err(frahan_cgal_last_error());
                WriteResult(outPath, rc, v, vc, t, tc, err, cgal: true);
                break;
            case 4:
                rc = frahan_cgal_poisson_reconstruct(pts, nPts, nrm, 0, 0, 0, out v, out vc, out t, out tc);
                err = Err(frahan_cgal_last_error());
                WriteResult(outPath, rc, v, vc, t, tc, err, cgal: true);
                break;
            default:
                WriteFail(outPath, "unknown mode " + mode);
                break;
        }
    }

    private static void WriteResult(string outPath, int rc, IntPtr v, int vc, IntPtr t, int tc,
                                    string err, bool cgal)
    {
        try
        {
            if (rc != 0)
            {
                WriteFail(outPath, $"native returned {rc}: {err}");
                return;
            }
            var verts = new double[3 * vc];
            if (vc > 0) Marshal.Copy(v, verts, 0, 3 * vc);
            var tris = new int[3 * tc];
            if (tc > 0) Marshal.Copy(t, tris, 0, 3 * tc);

            using (var bw = new BinaryWriter(File.Create(outPath)))
            {
                bw.Write(MAGIC);
                bw.Write(0);            // status ok
                bw.Write(vc);
                var vb = new byte[3 * vc * sizeof(double)];
                Buffer.BlockCopy(verts, 0, vb, 0, vb.Length); bw.Write(vb);
                bw.Write(tc);
                var tb = new byte[3 * tc * sizeof(int)];
                Buffer.BlockCopy(tris, 0, tb, 0, tb.Length); bw.Write(tb);
                bw.Write(0);            // errLen
            }
        }
        finally
        {
            if (cgal) frahan_cgal_free_buffers(v, t);
            else { frahan_geogram_free_pdouble(v); frahan_geogram_free_pint(t); }
        }
    }

    private static void WriteFail(string outPath, string msg)
    {
        var eb = Encoding.UTF8.GetBytes(msg ?? "");
        using (var bw = new BinaryWriter(File.Create(outPath)))
        {
            bw.Write(MAGIC);
            bw.Write(1);                // status fail
            bw.Write(eb.Length);
            bw.Write(eb);
        }
    }

    private static int ReadI(byte[] b, ref int o) { int x = BitConverter.ToInt32(b, o); o += 4; return x; }
    private static double ReadD(byte[] b, ref int o) { double x = BitConverter.ToDouble(b, o); o += 8; return x; }
}
