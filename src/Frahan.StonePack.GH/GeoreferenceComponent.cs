#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH;

// =============================================================================
// Georeference / Align by Points (2026-05-28). Best-fit rigid (optionally
// similarity) transform from 3+ corresponding control points, via Horn's
// closed-form absolute orientation (unit-quaternion; no external linear-algebra
// dependency -- dominant eigenvector of the 4x4 N matrix by power iteration).
//
// Use it to bring GPR fractures, scan meshes, and quarry geometry into ONE
// georeferenced frame: pick >=3 markers visible in both datasets, feed their
// coordinates as Source (the frame you're moving) and Target (the reference
// frame), and the whole geometry snaps over. Chain the Transform into
// Cloud ICP's "Initial Guess" for a coarse-georeference -> fine-ICP workflow.
// =============================================================================

[RelatedComponent("Frahan > Mesh > Cloud ICP", Reason = "Feed this Transform into Cloud ICP's Initial Guess for coarse-georef then fine-ICP.")]
[RelatedComponent("Frahan > Quarry > GPR Fractures on Mesh", Reason = "Georeference GPR picks into the scan/bench frame before overlaying.")]
[RelatedComponent("Frahan > Mesh > Move to Origin", Reason = "Move to Origin recenters; this aligns to another dataset via control points.")]
[Algorithm("Absolute orientation (Horn 1987)",
    "Horn, 'Closed-form solution of absolute orientation using unit quaternions', JOSA A 4(4) 1987",
    Note = "Dominant eigenvector of the 4x4 N matrix (power iteration). Rigid; optional uniform scale (similarity).")]
[DesignApplication(
    "Best-fit transform from 3+ corresponding control points (Horn's  absolute orientation)",
    DesignFlow.Bridges,
    Precedent = "Standard UTM / EPSG transforms + Horn 1987 best-fit absolute orientation")]
public sealed class GeoreferenceComponent : GH_Component
{
    public GeoreferenceComponent()
        : base("Georeference (Align by Points)", "Georef",
            "Best-fit transform from 3+ corresponding control points (Horn's " +
            "absolute orientation). Aligns GPR / scan / quarry geometry into one " +
            "georeferenced frame: Source = control points in the frame you move, " +
            "Target = matching points in the reference frame. Rigid by default; " +
            "enable Scale for similarity. Feed the Transform into Cloud ICP's " +
            "Initial Guess for fine registration.",
            "Frahan", "Mesh")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D05A06-1A2B-4C3D-9E4F-5A6B7C8D9E06");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap Icon => IconProvider.Load("Downsample.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGeometryParameter("Geometry", "G", "Geometry to align (moved by the fitted transform).", GH_ParamAccess.list);
        p[0].Optional = true;
        p.AddPointParameter("Source", "S", "Control points in the SOURCE frame (>= 3).", GH_ParamAccess.list);
        p.AddPointParameter("Target", "T", "Matching control points in the TARGET / reference frame (>= 3, same order).", GH_ParamAccess.list);
        p.AddBooleanParameter("Scale", "Sc", "Allow uniform scale (similarity transform). Default false = rigid.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGeometryParameter("Geometry", "G", "Geometry mapped into the target frame.", GH_ParamAccess.list);
        p.AddTransformParameter("Transform", "X", "Fitted source -> target transform.", GH_ParamAccess.item);
        p.AddTransformParameter("Inverse", "Xi", "Inverse transform (target -> source).", GH_ParamAccess.item);
        p.AddNumberParameter("RMS", "RMS", "Root-mean-square control-point residual after the fit (model units).", GH_ParamAccess.item);
        p.AddTextParameter("Report", "R", "Fit summary.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var geo = new List<IGH_GeometricGoo>();
        var src = new List<Point3d>();
        var tgt = new List<Point3d>();
        bool allowScale = false;
        da.GetDataList(0, geo);
        if (!da.GetDataList(1, src) || !da.GetDataList(2, tgt))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Source + Target control points required.");
            return;
        }
        da.GetData(3, ref allowScale);

        if (src.Count != tgt.Count)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Source ({src.Count}) and Target ({tgt.Count}) counts must match.");
            return;
        }
        if (src.Count < 3)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need at least 3 control-point pairs.");
            return;
        }

        if (!Horn.Solve(src, tgt, allowScale, out Transform xf, out double scale, out string err))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, err);
            return;
        }

        // Residual.
        double sse = 0;
        for (int i = 0; i < src.Count; i++)
        {
            var p = src[i]; p.Transform(xf);
            sse += p.DistanceToSquared(tgt[i]);
        }
        double rms = Math.Sqrt(sse / src.Count);

        var moved = new List<IGH_GeometricGoo>(geo.Count);
        foreach (var g in geo)
        {
            if (g == null) { moved.Add(null); continue; }
            var dup = g.DuplicateGeometry();
            moved.Add(dup == null ? null : dup.Transform(xf));
        }

        xf.TryGetInverse(out Transform inv);

        da.SetDataList(0, moved);
        da.SetData(1, xf);
        da.SetData(2, inv);
        da.SetData(3, rms);
        da.SetData(4,
            $"Control pairs : {src.Count}\n" +
            $"Mode          : {(allowScale ? "similarity (rigid + scale)" : "rigid")}\n" +
            $"Scale         : {scale:F6}\n" +
            $"RMS residual  : {rms:G4} model units\n" +
            $"Geometry moved: {geo.Count}");
        if (rms > 1e-3 * Math.Max(1.0, src[0].DistanceTo(src[src.Count - 1])))
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"RMS {rms:G3}: control points don't fit a single rigid transform well (mislabeled pairs, or non-rigid distortion).");
    }

    // ─── Horn's absolute orientation (unit quaternion) ──────────────────────
    private static class Horn
    {
        public static bool Solve(IReadOnlyList<Point3d> s, IReadOnlyList<Point3d> t,
                                 bool allowScale, out Transform xf, out double scale, out string error)
        {
            xf = Transform.Identity; scale = 1.0; error = null;
            int n = s.Count;
            Point3d cs = Centroid(s), ct = Centroid(t);

            // Cross-covariance.
            double Sxx = 0, Sxy = 0, Sxz = 0, Syx = 0, Syy = 0, Syz = 0, Szx = 0, Szy = 0, Szz = 0;
            double sumSrc2 = 0, sumTgt2 = 0;
            for (int i = 0; i < n; i++)
            {
                double ax = s[i].X - cs.X, ay = s[i].Y - cs.Y, az = s[i].Z - cs.Z;
                double bx = t[i].X - ct.X, by = t[i].Y - ct.Y, bz = t[i].Z - ct.Z;
                Sxx += ax * bx; Sxy += ax * by; Sxz += ax * bz;
                Syx += ay * bx; Syy += ay * by; Syz += ay * bz;
                Szx += az * bx; Szy += az * by; Szz += az * bz;
                sumSrc2 += ax * ax + ay * ay + az * az;
                sumTgt2 += bx * bx + by * by + bz * bz;
            }
            if (sumSrc2 < 1e-12) { error = "Source control points are coincident."; return false; }

            // 4x4 symmetric N matrix (Horn 1987).
            double[,] N =
            {
                { Sxx + Syy + Szz, Syz - Szy,        Szx - Sxz,        Sxy - Syx        },
                { Syz - Szy,       Sxx - Syy - Szz,  Sxy + Syx,        Szx + Sxz        },
                { Szx - Sxz,       Sxy + Syx,        -Sxx + Syy - Szz, Syz + Szy        },
                { Sxy - Syx,       Szx + Sxz,        Syz + Szy,        -Sxx - Syy + Szz },
            };
            // Shift to positive-definite so power iteration converges to the
            // most-positive eigenvalue (the optimal rotation), not |max|.
            double shift = 0;
            for (int i = 0; i < 4; i++) { double r = 0; for (int j = 0; j < 4; j++) r += Math.Abs(N[i, j]); shift = Math.Max(shift, r); }
            for (int i = 0; i < 4; i++) N[i, i] += shift;

            double[] q = { 1, 0, 0, 0 };
            for (int it = 0; it < 100; it++)
            {
                double[] y = new double[4];
                for (int i = 0; i < 4; i++) { double acc = 0; for (int j = 0; j < 4; j++) acc += N[i, j] * q[j]; y[i] = acc; }
                double norm = Math.Sqrt(y[0] * y[0] + y[1] * y[1] + y[2] * y[2] + y[3] * y[3]);
                if (norm < 1e-300) { error = "degenerate control configuration (no rotation)."; return false; }
                for (int i = 0; i < 4; i++) y[i] /= norm;
                double dot = Math.Abs(q[0] * y[0] + q[1] * y[1] + q[2] * y[2] + q[3] * y[3]);
                q = y;
                if (dot > 1.0 - 1e-15) break;
            }

            // Quaternion (w,x,y,z) -> rotation matrix.
            double w = q[0], x = q[1], yy = q[2], z = q[3];
            double r00 = 1 - 2 * (yy * yy + z * z), r01 = 2 * (x * yy - w * z),  r02 = 2 * (x * z + w * yy);
            double r10 = 2 * (x * yy + w * z),      r11 = 1 - 2 * (x * x + z * z), r12 = 2 * (yy * z - w * x);
            double r20 = 2 * (x * z - w * yy),      r21 = 2 * (yy * z + w * x),    r22 = 1 - 2 * (x * x + yy * yy);

            if (allowScale)
            {
                // Horn's scale: sum(b . R a) / sum(|a|^2).
                double num = 0;
                for (int i = 0; i < n; i++)
                {
                    double ax = s[i].X - cs.X, ay = s[i].Y - cs.Y, az = s[i].Z - cs.Z;
                    double rax = r00 * ax + r01 * ay + r02 * az;
                    double ray = r10 * ax + r11 * ay + r12 * az;
                    double raz = r20 * ax + r21 * ay + r22 * az;
                    double bx = t[i].X - ct.X, by = t[i].Y - ct.Y, bz = t[i].Z - ct.Z;
                    num += bx * rax + by * ray + bz * raz;
                }
                scale = num / sumSrc2;
                if (scale <= 1e-9) scale = 1.0;
            }

            // Translation: t = ct - s*R*cs.
            double tx = ct.X - scale * (r00 * cs.X + r01 * cs.Y + r02 * cs.Z);
            double ty = ct.Y - scale * (r10 * cs.X + r11 * cs.Y + r12 * cs.Z);
            double tz = ct.Z - scale * (r20 * cs.X + r21 * cs.Y + r22 * cs.Z);

            xf = Transform.Identity;
            xf.M00 = scale * r00; xf.M01 = scale * r01; xf.M02 = scale * r02; xf.M03 = tx;
            xf.M10 = scale * r10; xf.M11 = scale * r11; xf.M12 = scale * r12; xf.M13 = ty;
            xf.M20 = scale * r20; xf.M21 = scale * r21; xf.M22 = scale * r22; xf.M23 = tz;
            return true;
        }

        private static Point3d Centroid(IReadOnlyList<Point3d> pts)
        {
            double x = 0, y = 0, z = 0;
            foreach (var p in pts) { x += p.X; y += p.Y; z += p.Z; }
            return new Point3d(x / pts.Count, y / pts.Count, z / pts.Count);
        }
    }
}
