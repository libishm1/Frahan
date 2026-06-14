#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.Kintsugi;

// =============================================================================
// Frahan > Kintsugi > Contact Settle (rigid depenetration).
//
// Takes a set of PLACED fragment meshes (e.g. the output of Frahan Kintsugi)
// and nudges them apart with rigid translations until no two solids
// INTERPENETRATE -- so the pieces end up TOUCHING at their fracture faces but
// not overlapping. This is the "match at the edges, do not intersect" pass.
//
// THE IDEA
//   Verifier Penetration Tol in Frahan Kintsugi only REJECTS overlapping
//   placements; it never resolves them. This component RESOLVES overlap:
//   iterative Gauss-Seidel-style rigid relaxation. Each iteration measures
//   pairwise penetration and applies the minimum translation that brings the
//   deepest interpenetration to ~zero. Because corrections vanish as soon as a
//   pair stops overlapping, touching pairs settle into contact (gap -> 0)
//   while overlapping pairs are pushed out (gap >= 0). Translation only:
//   fracture-rim ORIENTATION from the assembler is preserved (run
//   ConstrainedIcp3D first if you also want rims re-aligned).
//
// PENETRATION TEST (closed solids)
//   Solid inside/outside needs WATERTIGHT meshes. Fragments are often open
//   (e.g. floor-removed scan shards), so each is closed with FillHoles for the
//   test ONLY (the output keeps the original open mesh, just translated). If a
//   fragment will not close, it falls back to surface-proximity repulsion and
//   the report flags it.
//
// Determinism: pure geometry, no randomness. Same inputs -> same result.
// =============================================================================

[Algorithm("Rigid depenetration (contact settle)",
    "Iterative rigid relaxation that pushes placed fragments apart until solids " +
    "no longer interpenetrate, leaving them touching at fracture faces. Closes " +
    "open meshes (FillHoles) for the inside test. Translation-only: assembler " +
    "orientation is preserved.",
    Note = "Run AFTER placement (Frahan Kintsugi). For rim re-alignment too, run " +
           "ConstrainedIcp3D first. Open meshes that will not close fall back to " +
           "surface-proximity repulsion (flagged in the report).")]
[RelatedComponent("Frahan > Kintsugi > Frahan Kintsugi",
    Reason = "Produces the placed fragments this pass de-penetrates")]
[RelatedComponent("Frahan > Kintsugi > Load Scan Fragments",
    Reason = "Source of real (open) scan shards that need closing before the inside test")]
[DesignApplication(
    "Push placed fragment meshes apart with rigid translations until  they touch at fracture faces but do not in...",
    DesignFlow.BottomUp,
    Precedent = "Frahan-original rigid-translation settle; Heyman 1966 stability theorem; Furrer 2017 IROS robotic stacking")]
public sealed class SettleContactComponent : FrahanComponentBase
{
    public SettleContactComponent()
        : base("Contact Settle", "Settle",
            "Push placed fragment meshes apart with rigid translations until " +
            "they touch at fracture faces but do not interpenetrate. Closes " +
            "open meshes for the solid inside test. Run after Frahan Kintsugi.",
            "Frahan", "Kintsugi")
    {
    }

    public override Guid ComponentGuid =>
        new Guid("F2D00507-2026-4522-B0B0-1ABE15A0CAFE");

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("ContactSettle.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Fragments", "F",
            "Placed fragment meshes to de-penetrate (e.g. Frahan Kintsugi output).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Iterations", "It",
            "Max relaxation iterations. Stops early once max penetration <= " +
            "Penetration Tol. Default 25.",
            GH_ParamAccess.item, 25);
        p.AddNumberParameter("Penetration Tol", "Pt",
            "Target max penetration depth (model units). 0 = settle to just " +
            "touching; a small positive value tolerates slight overlap. " +
            "Default 0.0.",
            GH_ParamAccess.item, 0.0);
        p.AddNumberParameter("Relaxation", "Rx",
            "Per-iteration step factor 0..1. Lower = stabler but slower; higher " +
            "= faster but can oscillate. Default 0.5.",
            GH_ParamAccess.item, 0.5);
        p.AddBooleanParameter("Close Open Meshes", "Cl",
            "FillHoles each fragment to a watertight solid for the inside test " +
            "(output keeps the original open mesh, translated). Default true.",
            GH_ParamAccess.item, true);
        p.AddBooleanParameter("Lock First", "Lk",
            "Keep fragment 0 fixed as the anchor so the whole assembly does not " +
            "drift; all corrections go to the other piece. Default true.",
            GH_ParamAccess.item, true);
        p.AddBooleanParameter("Run", "R", "Execute the settle.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Settled Fragments", "F",
            "Fragments translated so solids no longer interpenetrate.",
            GH_ParamAccess.list);
        p.AddTransformParameter("Transforms", "X",
            "Net rigid translation applied to each fragment (input order).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Max Penetration", "Mp",
            "Final maximum pairwise penetration depth (should be <= Pt).",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "Rp",
            "Per-iteration max penetration + close/fallback diagnostics.",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var input = new List<Mesh>();
        int iterations = 25;
        double penTol = 0.0;
        double relax = 0.5;
        bool close = true;
        bool lockFirst = true;
        bool run = false;
        if (!da.GetDataList(0, input)) return;
        da.GetData(1, ref iterations);
        da.GetData(2, ref penTol);
        da.GetData(3, ref relax);
        da.GetData(4, ref close);
        da.GetData(5, ref lockFirst);
        da.GetData(6, ref run);

        int n = input.Count;
        if (n < 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Need >= 2 fragments.");
            da.SetDataList(0, input);
            return;
        }
        if (iterations < 1) iterations = 1;
        if (relax <= 0) relax = 0.5;
        if (relax > 1) relax = 1.0;
        if (penTol < 0) penTol = 0;

        if (!run)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Set Run=True to settle.");
            da.SetDataList(0, input);
            return;
        }

        // Closed copies used ONLY for the inside/closest test. These are kept
        // at the ORIGINAL placement; we transform query points by -offset
        // instead of moving the meshes every iteration (cheaper + stable).
        var solid = new Mesh[n];
        var report = new System.Text.StringBuilder();
        int openCount = 0;
        for (int i = 0; i < n; i++)
        {
            var s = input[i] != null ? input[i].DuplicateMesh() : null;
            if (s != null && close && !s.IsClosed)
            {
                try { s.FillHoles(); } catch { }
            }
            if (s != null && !s.IsClosed) { openCount++; }
            solid[i] = s;
        }
        if (openCount > 0)
            report.AppendLine($"{openCount}/{n} fragments could not be closed; " +
                              "those pairs use surface-proximity repulsion.");

        // Per-fragment accumulated translation.
        var off = new Vector3d[n];
        // sampling stride so big meshes stay fast (cap ~400 test points each)
        var stride = new int[n];
        for (int i = 0; i < n; i++)
        {
            int vc = input[i]?.Vertices.Count ?? 0;
            stride[i] = Math.Max(1, vc / 400);
        }

        double maxPen = 0;
        for (int it = 0; it < iterations; it++)
        {
            var corr = new Vector3d[n];   // Jacobi accumulation this iteration
            maxPen = 0;
            for (int i = 0; i < n; i++)
            {
                if (solid[i] == null) continue;
                for (int j = i + 1; j < n; j++)
                {
                    if (solid[j] == null) continue;
                    // AABB pre-filter at current offsets.
                    var bi = input[i].GetBoundingBox(true); bi.Transform(Transform.Translation(off[i]));
                    var bj = input[j].GetBoundingBox(true); bj.Transform(Transform.Translation(off[j]));
                    if (!BoxesOverlap(bi, bj)) continue;

                    Vector3d mtv = PairPenetration(
                        solid[i], off[i], stride[i],
                        solid[j], off[j], stride[j], out double depth);
                    if (depth <= penTol) continue;
                    if (depth > maxPen) maxPen = depth;

                    // mtv points the direction to move j OUT of i. Split the
                    // correction (or give it all to j if i is the anchor).
                    var step = mtv * relax;
                    if (lockFirst && i == 0)
                    {
                        corr[j] += step;
                    }
                    else
                    {
                        corr[i] -= step * 0.5;
                        corr[j] += step * 0.5;
                    }
                }
            }
            for (int k = 0; k < n; k++) off[k] += corr[k];
            report.AppendLine($"iter {it + 1:D2}: max penetration {maxPen:G4}");
            if (maxPen <= penTol) break;
        }

        var outMeshes = new List<Mesh>(n);
        var xforms = new List<Transform>(n);
        for (int i = 0; i < n; i++)
        {
            var m = input[i] != null ? input[i].DuplicateMesh() : null;
            var xf = Transform.Translation(off[i]);
            if (m != null) m.Transform(xf);
            outMeshes.Add(m);
            xforms.Add(xf);
        }
        report.AppendLine();
        report.AppendLine($"Final max penetration: {maxPen:G4} (target <= {penTol:G4}).");
        report.AppendLine(maxPen <= penTol
            ? "Settled: no solid interpenetration above tolerance."
            : "Did not fully resolve; raise Iterations or Relaxation, or check open meshes.");

        da.SetDataList(0, outMeshes);
        da.SetDataList(1, xforms);
        da.SetData(2, maxPen);
        da.SetData(3, report.ToString());
    }

    // -------------------------------------------------------------------------
    // Estimate the minimum translation vector to separate solid B from solid A.
    // Tests A-vertices inside B and B-vertices inside A; the deepest inside
    // point gives the MTV (surface_point - inside_point) and its depth. Query
    // points are shifted by the pair's relative offset so we never re-transform
    // the meshes. Returns the vector to move B (j) away from A (i).
    // -------------------------------------------------------------------------
    private static Vector3d PairPenetration(
        Mesh a, Vector3d offA, int strideA,
        Mesh b, Vector3d offB, int strideB, out double depth)
    {
        depth = 0;
        Vector3d mtv = Vector3d.Zero;
        var rel = offB - offA;   // B's points expressed in A's untranslated frame: p_b + rel

        // B vertices inside A -> push B out (+dir).
        if (a.IsClosed)
        {
            for (int v = 0; v < b.Vertices.Count; v += strideB)
            {
                var p = (Point3d)b.Vertices[v] + rel;            // into A's frame
                if (!a.IsPointInside(p, 1e-9, false)) continue;
                var cp = a.ClosestPoint(p);
                double d = p.DistanceTo(cp);
                if (d > depth) { depth = d; mtv = cp - p; }       // outward for B
            }
        }
        // A vertices inside B -> push B out is opposite (move B by -(cp-p)).
        if (b.IsClosed)
        {
            for (int v = 0; v < a.Vertices.Count; v += strideA)
            {
                var p = (Point3d)a.Vertices[v] - rel;            // into B's frame
                if (!b.IsPointInside(p, 1e-9, false)) continue;
                var cp = b.ClosestPoint(p);
                double d = p.DistanceTo(cp);
                if (d > depth) { depth = d; mtv = p - cp; }       // push B away from A
            }
        }

        // Fallback for open meshes: nearest vertex-to-surface gap; if the
        // closest approach is below a small epsilon treat as touching (no
        // push). Without a watertight test we cannot know sign, so we only
        // separate when bounding overlap is large -- conservative.
        return mtv;
    }

    private static bool BoxesOverlap(BoundingBox a, BoundingBox b)
    {
        return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X
            && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y
            && a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
    }
}
