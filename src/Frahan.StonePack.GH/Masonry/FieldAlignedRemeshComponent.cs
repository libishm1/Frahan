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
    // Field-Aligned Remesh (Instant Meshes) — our own field-aligned quad remesher
    // (Jakob et al. 2015, BSD-3), run out of process. The 4-RoSy/4-PoSy field makes
    // the quad edges follow the surface flow (principal curvature ~= principal thrust
    // on a funicular shell), so the quads are the natural masonry-course directions.
    // Feed the Quad Mesh into "Vault Quad Courses (CRA)" with Edge Length = 0 to CRA
    // along this flow. Falls back to Rhino's QuadRemesh if the worker is unavailable.
    // =========================================================================
    public sealed class FieldAlignedRemeshComponent : FrahanComponentBase
    {
        public FieldAlignedRemeshComponent()
            : base("Field-Aligned Remesh", "FieldRemesh",
                "Field-aligned quad remesh via Instant Meshes (Jakob et al. 2015, BSD-3), run out of " +
                "process. 4-RoSy orientation + 4-PoSy position field -> the quad edges follow the surface " +
                "flow (principal curvature ~= principal thrust on a funicular membrane). Outputs the quad " +
                "mesh and its edge 'flow' lines. Feed into Vault Quad Courses (CRA) with Edge Length 0 to " +
                "analyse courses along this flow. Falls back to Rhino QuadRemesh if the worker is missing.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid => new Guid("B7A11500-0006-4A11-B500-0000000000A6");
        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M", "Triangle (or mixed) surface mesh to remesh.", GH_ParamAccess.item);
            p.AddNumberParameter("Edge Length", "E", "Target quad edge length (m). 0 = use Face Count instead.", GH_ParamAccess.item, 0.25);
            p.AddIntegerParameter("Face Count", "F", "Target quad count (used only when Edge Length = 0).", GH_ParamAccess.item, 0);
            p.AddBooleanParameter("Intrinsic", "I", "Intrinsic smoothing (curved surfaces). Off by default.", GH_ParamAccess.item, false);
            p.AddBooleanParameter("Align Edges", "B", "Snap quads to open boundaries for clean free edges. TRADE-OFF: adds interior singularities + lowers CRA. Off by default (plain = 0 singularities, 100% CRA).", GH_ParamAccess.item, false);
            p.AddNumberParameter("Crease", "Cr", "Crease angle (deg) to snap to sharp features; 0 = off.", GH_ParamAccess.item, 0.0);
            p.AddIntegerParameter("Smoothing", "Sm", "Extra field smoothing iterations; 0 = off.", GH_ParamAccess.item, 0);
            p.AddBooleanParameter("Run", "R", "Execute the remesh.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Quad Mesh", "Q", "Field-aligned quad-dominant mesh.", GH_ParamAccess.item);
            p.AddLineParameter("Flow", "Fl", "Quad edge lines (the field-aligned flow).", GH_ParamAccess.list);
            p.AddTextParameter("Report", "Rp", "Summary (engine used, face counts).", GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh mesh = null;
            double edge = 0.25; int faceCount = 0, smooth = 0; bool intrinsic = false, alignEdges = false, run = false;
            double crease = 0.0;

            if (!GhGuard.Item(this, da, 0, ref mesh, "Mesh")) return;
            da.GetData(1, ref edge); da.GetData(2, ref faceCount);
            da.GetData(3, ref intrinsic); da.GetData(4, ref alignEdges);
            da.GetData(5, ref crease); da.GetData(6, ref smooth); da.GetData(7, ref run);

            if (!run) { da.SetData(2, "Run = false. Toggle to remesh."); return; }

            string engine;
            Mesh q = InstantMeshRemesher.Remesh(mesh, edge, faceCount, true, intrinsic, alignEdges, crease, smooth);
            if (q != null) engine = "Instant Meshes (field-aligned)";
            else
            {
                // graceful fallback: Rhino's QuadRemesh.
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Instant Meshes worker unavailable (" + InstantMeshRemesher.LastError + "). Using Rhino QuadRemesh.");
                var qp = new QuadRemeshParameters { AdaptiveSize = 0.0, AdaptiveQuadCount = false };
                if (edge > 0) qp.TargetEdgeLength = edge; else if (faceCount > 0) qp.TargetQuadCount = faceCount;
                q = mesh.QuadRemesh(qp);
                engine = "Rhino QuadRemesh (fallback)";
            }
            if (q == null) { da.SetData(2, "Remesh failed: " + InstantMeshRemesher.LastError); return; }

            q.Normals.ComputeNormals();
            var flow = new List<Line>();
            var te = q.TopologyEdges;
            for (int i = 0; i < te.Count; i++) flow.Add(te.EdgeLine(i));

            da.SetData(0, q);
            da.SetDataList(1, flow);
            da.SetData(2, $"{engine}: {q.Faces.QuadCount} quads + {q.Faces.TriangleCount} tris, {q.Vertices.Count} verts, {te.Count} edges. " +
                          (edge > 0 ? $"target edge {edge:F3} m." : $"target {faceCount} faces."));
            Message = $"{q.Faces.QuadCount} quads";
        }
    }
}
