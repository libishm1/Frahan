#nullable disable
using System;
using System.Drawing;
using Frahan.Masonry.Geometry;
using Frahan.Masonry.Interfaces;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // MeshQualityReportComponent — surfaces a MeshQualityReport on the
    // canvas. Use as a precondition / debugging step before any robustness-
    // sensitive operation (contact detection, packing, cutting).
    //
    // ComponentGuid: 9ABCDEF0-1234-5678-9ABC-DEF012345678
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Mesh Quality Report.
    /// Topology + geometry diagnostics for a Rhino mesh: manifold,
    /// closed, normal consistency, edge-length stats, surface area,
    /// signed volume.
    /// </summary>
        [Algorithm("Mesh-quality metrics",
        "Frey & Borouchaki 1999, Surface mesh quality evaluation, Int. J. Numer. Methods Eng. 45(1):101-118",
        Doi = "10.1002/(SICI)1097-0207(19990510)45:1<101::AID-NME582>3.0.CO;2-4",
        WikiPath = "wiki/index/references.md#FreyBorouchaki1999MeshQuality",
        Note = "Surface mesh shape/size quality metrics")]
        [DesignApplication(
        "Topology + geometry diagnostics for a Rhino mesh",
        DesignFlow.Bridges,
        Precedent = "Standard mesh quality metrics (Frey Borouchaki 1999 mesh quality)")]
    public sealed class MeshQualityReportComponent : FrahanComponentBase
    {
        public MeshQualityReportComponent()
            : base(
                "Mesh Quality Report", "MQ",
                "Topology + geometry diagnostics for a Rhino mesh. Use as " +
                "a precondition for contact detection, packing, or cutting. " +
                "Metrics per Frey & Borouchaki 1999.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("9ABCDEF0-1234-5678-9ABC-DEF012345678");

        public override GH_Exposure Exposure => GH_Exposure.senary;

        protected override Bitmap Icon => IconProvider.Load("PackDiagnostics.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M",
                "Mesh to analyse.",
                GH_ParamAccess.item);
            p.AddNumberParameter("Dedup Tolerance", "Td",
                "Vertex-merge tolerance for duplicate detection. Default 1e-9.",
                GH_ParamAccess.item, 1e-9);
            p[1].Optional = true;
            p.AddNumberParameter("Degenerate Area Tol", "Ta",
                "Triangle-area threshold below which a triangle counts as " +
                "degenerate. Default 1e-12.",
                GH_ParamAccess.item, 1e-12);
            p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("Is Clean Solid", "OK",
                "True iff closed AND manifold AND consistent normals AND no " +
                "degenerate triangles AND no duplicate vertices.",
                GH_ParamAccess.item);
            p.AddBooleanParameter("Manifold", "Mf",
                "Every edge is incident to 1 or 2 triangles.",
                GH_ParamAccess.item);
            p.AddBooleanParameter("Closed", "Cl",
                "Every edge is incident to exactly 2 triangles.",
                GH_ParamAccess.item);
            p.AddBooleanParameter("Consistent Normals", "Nm",
                "No two triangles share an edge in the same winding direction.",
                GH_ParamAccess.item);
            p.AddIntegerParameter("Duplicate Vertices", "DupV",
                "Vertices closer than the dedup tolerance to an earlier one.",
                GH_ParamAccess.item);
            p.AddIntegerParameter("Degenerate Triangles", "DegT",
                "Triangles below the area threshold.",
                GH_ParamAccess.item);
            p.AddIntegerParameter("Boundary Edges", "Be",
                "Edges incident to exactly one triangle (open boundaries).",
                GH_ParamAccess.item);
            p.AddIntegerParameter("Non-manifold Edges", "Nme",
                "Edges incident to three or more triangles.",
                GH_ParamAccess.item);
            p.AddNumberParameter("Median Edge Length", "MedE",
                "Median triangle-edge length. Useful as an adaptive-tolerance " +
                "scale factor.",
                GH_ParamAccess.item);
            p.AddNumberParameter("Surface Area", "A",
                "Sum of triangle areas.",
                GH_ParamAccess.item);
            p.AddNumberParameter("Signed Volume", "V",
                "Divergence-theorem volume. Negative means normals are " +
                "inward-facing on a closed mesh.",
                GH_ParamAccess.item);
            p.AddTextParameter("Report", "R",
                "Single-line human-readable summary.",
                GH_ParamAccess.item);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh m = null;
            if (!da.GetData(0, ref m) || m == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No mesh provided.");
                return;
            }
            double dedupTol = 1e-9;
            double degenTol = 1e-12;
            da.GetData(1, ref dedupTol);
            da.GetData(2, ref degenTol);

            var snap = MeshToSnapshot(m);
            var rep = MeshSanitizer.Analyse(snap, dedupTol, degenTol);

            da.SetData(0, rep.IsCleanSolid);
            da.SetData(1, rep.IsManifold);
            da.SetData(2, rep.IsClosed);
            da.SetData(3, rep.HasConsistentNormals);
            da.SetData(4, rep.DuplicateVertexCount);
            da.SetData(5, rep.DegenerateTriangleCount);
            da.SetData(6, rep.BoundaryEdgeCount);
            da.SetData(7, rep.NonManifoldEdgeCount);
            da.SetData(8, rep.MedianEdgeLength);
            da.SetData(9, rep.SurfaceArea);
            da.SetData(10, rep.SignedVolume);
            da.SetData(11, rep.ToString());
        }

        internal static MeshSnapshot MeshToSnapshot(Mesh m)
        {
            int v = m.Vertices.Count;
            var verts = new double[v * 3];
            for (int i = 0; i < v; i++)
            {
                var p = m.Vertices[i];
                verts[3 * i + 0] = p.X;
                verts[3 * i + 1] = p.Y;
                verts[3 * i + 2] = p.Z;
            }
            // Triangulate non-tri faces by fan.
            int triCount = 0;
            for (int i = 0; i < m.Faces.Count; i++)
                triCount += m.Faces[i].IsTriangle ? 1 : 2;
            var tris = new int[triCount * 3];
            int next = 0;
            for (int i = 0; i < m.Faces.Count; i++)
            {
                var f = m.Faces[i];
                if (f.IsTriangle)
                {
                    tris[3 * next + 0] = f.A; tris[3 * next + 1] = f.B; tris[3 * next + 2] = f.C;
                    next++;
                }
                else
                {
                    tris[3 * next + 0] = f.A; tris[3 * next + 1] = f.B; tris[3 * next + 2] = f.C;
                    next++;
                    tris[3 * next + 0] = f.A; tris[3 * next + 1] = f.C; tris[3 * next + 2] = f.D;
                    next++;
                }
            }
            return new MeshSnapshot(verts, tris);
        }
    }
}
