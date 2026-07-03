#nullable disable
using System;
using System.Collections.Generic;
using Frahan.GH;
using Frahan.Masonry.Vault;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.StonePack.GH.Masonry
{
    // =========================================================================
    // Vault form-finding suite (Libish 2026-07-02): four SMALL single-purpose
    // components — boundary -> net -> catenary/force-density relax -> SubD —
    // each reusable on its own (chain them for the Güell / three-prong
    // vault-generation examples). Solver = the validated TnaForceDensity3D
    // (Schek 1974 force density; VaultFromHang inversion = Gaudí hanging chain).
    // =========================================================================

    public sealed class CatenaryCurveComponent : FrahanComponentBase
    {
        public CatenaryCurveComponent()
            : base("Catenary Curve", "Catenary",
                "True catenary (hanging chain) through two points with arc length = Length Factor x chord. " +
                "The Gaudí form-finding primitive: invert it for a compression arch. Analytic cosh solve.",
                "Frahan", "Vault")
        { }
        public override Guid ComponentGuid => new Guid("B7A11500-0011-4A11-B500-0000000000B1");
        public override GH_Exposure Exposure => GH_Exposure.senary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddPointParameter("Point A", "A", "First support.", GH_ParamAccess.item);
            p.AddPointParameter("Point B", "B", "Second support.", GH_ParamAccess.item);
            p.AddNumberParameter("Length Factor", "L", "Arc length as a multiple of the chord (> 1). 1.15 = gentle sag.", GH_ParamAccess.item, 1.15);
            p.AddIntegerParameter("Segments", "S", "Polyline spans.", GH_ParamAccess.item, 24);
            p.AddBooleanParameter("Invert", "I", "Flip the sag upward (compression arch).", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("Curve", "C", "Catenary polyline.", GH_ParamAccess.item);
            p.AddNumberParameter("Sag", "Sg", "Max vertical offset from the chord (m).", GH_ParamAccess.item);
        }
        protected override void SolveSafe(IGH_DataAccess da)
        {
            Point3d a = Point3d.Unset, b = Point3d.Unset;
            double lf = 1.15; int seg = 24; bool inv = false;
            if (!da.GetData(0, ref a) || !da.GetData(1, ref b)) return;
            da.GetData(2, ref lf); da.GetData(3, ref seg); da.GetData(4, ref inv);
            var pl = FormFinding.CatenaryCurve(a, b, lf, seg);
            double sag = 0;
            var chord = new Line(a, b);
            foreach (var pt in pl) sag = Math.Max(sag, pt.DistanceTo(chord.ClosestPoint(pt, true)));
            if (inv)
            {
                var mid = 0.5 * (a + b);
                var flipped = new Polyline();
                foreach (var pt in pl)
                {
                    var cp = chord.ClosestPoint(pt, true);
                    flipped.Add(cp + (cp - pt));
                }
                pl = flipped;
            }
            da.SetData(0, new PolylineCurve(pl));
            da.SetData(1, sag);
            Message = $"sag {sag:0.00} m";
        }
    }

    public sealed class BoundaryGridMeshComponent : FrahanComponentBase
    {
        public BoundaryGridMeshComponent()
            : base("Boundary Net", "BNet",
                "Flat triangulated form-finding NET from a closed planar boundary curve. Outputs the net " +
                "plus its naked-boundary anchor points (default supports for Catenary Relax). Keep the net " +
                "under ~1500 vertices (the relax uses a dense solve).",
                "Frahan", "Vault")
        { }
        public override Guid ComponentGuid => new Guid("B7A11500-000E-4A11-B500-0000000000AE");
        public override GH_Exposure Exposure => GH_Exposure.senary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddCurveParameter("Boundary", "B", "Closed planar boundary (plan of the vault footprint).", GH_ParamAccess.item);
            p.AddNumberParameter("Edge Length", "E", "Target net edge length (m).", GH_ParamAccess.item, 0.5);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Net", "N", "Flat triangulated network.", GH_ParamAccess.item);
            p.AddPointParameter("Anchors", "A", "Naked-boundary vertices (default supports).", GH_ParamAccess.list);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }
        protected override void SolveSafe(IGH_DataAccess da)
        {
            Curve c = null; double e = 0.5;
            if (!GhGuard.Item(this, da, 0, ref c, "Boundary")) return;
            da.GetData(1, ref e);
            List<Point3d> anchors;
            var m = FormFinding.BoundaryNet(c, e, out anchors);
            if (m == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Boundary must be a CLOSED planar curve."); return; }
            da.SetData(0, m);
            da.SetDataList(1, anchors);
            da.SetData(2, $"net: {m.Vertices.Count} verts, {m.Faces.Count} tris, {anchors.Count} anchors, edge ~{e:0.00} m.");
            Message = m.Vertices.Count + " verts";
        }
    }

    public sealed class CatenaryRelaxComponent : FrahanComponentBase
    {
        public CatenaryRelaxComponent()
            : base("Catenary Relax", "Relax",
                "Force-density form-finding (Schek 1974, the validated TnaForceDensity3D solver): hang the " +
                "net from the anchors under a vertical load, then invert across the support plane " +
                "(Gaudí hanging chain) for the compression vault. Anchors empty = all naked-boundary " +
                "vertices fixed. Force Density vs Load sets the sag: lower q = deeper catenary.",
                "Frahan", "Vault")
        { }
        public override Guid ComponentGuid => new Guid("B7A11500-000F-4A11-B500-0000000000AF");
        public override GH_Exposure Exposure => GH_Exposure.senary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Net", "N", "Form-finding network (e.g. from Boundary Net).", GH_ParamAccess.item);
            p.AddPointParameter("Anchors", "A", "Support points (optional; empty = naked boundary).", GH_ParamAccess.list);
            p.AddNumberParameter("Force Density", "q", "Per-edge force density (> 0). Lower = deeper sag.", GH_ParamAccess.item, 1.0);
            p.AddNumberParameter("Load", "Ld", "Per-node vertical load (negative = hang down).", GH_ParamAccess.item, -1.0);
            p.AddBooleanParameter("Invert", "I", "Invert the hang into the compression vault.", GH_ParamAccess.item, true);
            p.AddNumberParameter("Anchor Tol", "T", "Anchor matching tolerance (m).", GH_ParamAccess.item, 0.05);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Form", "F", "Form-found vault (or hanging net if Invert = false).", GH_ParamAccess.item);
            p.AddNumberParameter("Rise", "R", "Crown rise over the support datum (m).", GH_ParamAccess.item);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }
        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh net = null; var anchors = new List<Point3d>();
            double q = 1.0, load = -1.0, tol = 0.05; bool inv = true;
            if (!GhGuard.Item(this, da, 0, ref net, "Net")) return;
            da.GetDataList(1, anchors);
            da.GetData(2, ref q); da.GetData(3, ref load); da.GetData(4, ref inv); da.GetData(5, ref tol);
            if (net.Vertices.Count > 2500)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Net too dense for the dense solve (> 2500 verts). Increase Edge Length."); return; }
            double rise, lat; int na;
            var f = FormFinding.CatenaryRelax(net, anchors, q, load, inv, tol, out rise, out lat, out na);
            if (f == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Relax failed: need >= 3 anchors on the net."); return; }
            da.SetData(0, f);
            da.SetData(1, rise);
            da.SetData(2, $"form-found: {f.Vertices.Count} verts, {na} anchors, rise {rise:0.00} m, lateral {lat:0.00} m, q {q:0.00}, load {load:0.00}.");
            Message = $"rise {rise:0.0} m";
        }
    }

    public sealed class SubDVaultComponent : FrahanComponentBase
    {
        public SubDVaultComponent()
            : base("SubD Vault", "SubDV",
                "Smooth a control/form-found mesh through SubD subdivision (Catmull-Clark) and return the " +
                "subdivided mesh — the same route that produced the Güell portico surface from its control " +
                "cage. Density 2 is the validated setting.",
                "Frahan", "Vault")
        { }
        public override Guid ComponentGuid => new Guid("B7A11500-0010-4A11-B500-0000000000B0");
        public override GH_Exposure Exposure => GH_Exposure.senary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Control", "M", "Control mesh (coarse form-found or modelled cage).", GH_ParamAccess.item);
            p.AddIntegerParameter("Density", "D", "Subdivision density 1-4 (2 = validated).", GH_ParamAccess.item, 2);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Smooth", "S", "Subdivided smooth mesh (triangulated).", GH_ParamAccess.item);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }
        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh m = null; int d = 2;
            if (!GhGuard.Item(this, da, 0, ref m, "Control")) return;
            da.GetData(1, ref d);
            string note;
            var s = FormFinding.SubDVault(m, d, out note);
            if (s == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "SubD failed: " + note); return; }
            da.SetData(0, s);
            da.SetData(1, $"{m.Faces.Count} control faces -> {s.Faces.Count} tris ({note}).");
            Message = s.Faces.Count + " faces";
        }
    }
}
