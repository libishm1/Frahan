#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH;
using Frahan.Masonry.Vault;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.StonePack.GH.Masonry
{
    // =========================================================================
    // Vault Quad Courses (CRA) — the field-aligned course-stability workflow.
    // QuadRemesh a funicular shell to a target edge length so the quad field
    // follows the surface/thrust flow, walk the quad face-strips as masonry
    // courses, and run rigid-block CRA (native OSQP when present) on each.
    // Outputs the quad mesh, the per-course centerlines + stable flags, and a
    // coverage-coloured mesh (green = stable both ways, yellow = one way, red =
    // none). This is the discrete masonry counterpart of the funicular form.
    // =========================================================================
    public sealed class VaultQuadCourseComponent : FrahanComponentBase
    {
        public VaultQuadCourseComponent()
            : base("Vault Quad Courses (CRA)", "VaultQuadCRA",
                "Field-aligned masonry course analysis on a funicular vault shell. QuadRemesh to a " +
                "target edge length (the quad field follows the thrust flow), walk the quad face-strips " +
                "as voussoir courses (Striatus-style: depth along the surface normal), and CRA each " +
                "course (rigid-block equilibrium, native OSQP). Outputs the quad mesh, course centerlines, " +
                "per-course stability, and a coverage mesh coloured by how many of the <=2 courses through " +
                "each face are stable (green both-way / yellow one-way / red none).",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-0005-4A11-B500-0000000000A5");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Shell", "M", "Funicular vault shell mesh (e.g. from the TNA form-finder).", GH_ParamAccess.item);
            p.AddNumberParameter("Edge Length", "E", "Target quad edge length (m). 0 = analyse the input mesh as-is (no remesh).", GH_ParamAccess.item, 0.25);
            p.AddNumberParameter("Thickness", "T", "Voussoir/section thickness through the shell (m).", GH_ParamAccess.item, 0.30);
            p.AddNumberParameter("Course Width", "W", "Course (block) width (m). Default ~0.9 x edge length.", GH_ParamAccess.item, 0.22);
            p.AddNumberParameter("Friction", "F", "Mohr-Coulomb friction coefficient (tan phi).", GH_ParamAccess.item, 0.84);
            p.AddBooleanParameter("Run", "R", "Execute the quad-course CRA.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Quad Mesh", "Q", "The field-aligned quad mesh (courses run along its strips).", GH_ParamAccess.item);
            p.AddMeshParameter("Coverage", "C", "Coverage mesh coloured by course stability (green both-way / yellow one-way / red none).", GH_ParamAccess.item);
            p.AddCurveParameter("Courses", "Cr", "Per-course centerline polylines.", GH_ParamAccess.list);
            p.AddBooleanParameter("Stable", "S", "Per-course stability flag (aligned with Courses).", GH_ParamAccess.list);
            p.AddNumberParameter("Stable %", "%", "Percentage of courses that are stable.", GH_ParamAccess.item);
            p.AddTextParameter("Report", "Rp", "Summary.", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh shell = null;
            double edge = 0.25, thick = 0.30, width = 0.22, friction = 0.84;
            bool run = false;

            if (!GhGuard.Item(this, da, 0, ref shell, "Shell")) return;
            da.GetData(1, ref edge); da.GetData(2, ref thick);
            da.GetData(3, ref width); da.GetData(4, ref friction);
            da.GetData(5, ref run);

            if (!run) { da.SetData(5, "Run = false. Toggle to analyse courses."); return; }

            var res = VaultQuadCourses.Analyze(shell, edge, thick, width, friction);

            // crisp per-face coverage mesh: one quad patch per face, vertex-coloured by score.
            var cov = new Mesh();
            var green = Color.FromArgb(95, 175, 110);
            var yellow = Color.FromArgb(225, 200, 95);
            var red = Color.FromArgb(210, 105, 90);
            int baseIdx = 0;
            for (int fi = 0; fi < res.QuadFaces.Count; fi++)
            {
                var q = res.QuadFaces[fi];
                var col = res.FaceScore[fi] >= 2 ? green : (res.FaceScore[fi] == 1 ? yellow : red);
                for (int j = 0; j < 4; j++) { cov.Vertices.Add(res.QuadMesh.Vertices[q[j]]); cov.VertexColors.Add(col); }
                cov.Faces.AddFace(baseIdx, baseIdx + 1, baseIdx + 2, baseIdx + 3);
                baseIdx += 4;
            }
            cov.Normals.ComputeNormals();

            var courses = new List<Curve>();
            foreach (var pl in res.Courses) courses.Add(new PolylineCurve(pl));

            da.SetData(0, res.QuadMesh);
            da.SetData(1, cov);
            da.SetDataList(2, courses);
            da.SetDataList(3, new List<bool>(res.StripStable));
            da.SetData(4, res.StablePercent);
            da.SetData(5,
                $"{res.StableCount}/{res.StripCount} courses stable ({res.StablePercent:F0}%). " +
                $"Coverage: {res.FacesBothWay} both-way / {res.FacesOneWay} one-way / {res.FacesNone} none of {res.QuadFaces.Count} faces. " +
                $"edge {edge:F2}m, thickness {thick:F2}m, friction {friction:F2}.");
            Message = $"{res.StableCount}/{res.StripCount} stable";
        }
    }
}
