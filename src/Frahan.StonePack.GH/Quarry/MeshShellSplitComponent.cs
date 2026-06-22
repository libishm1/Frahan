#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Cutting;
using Frahan.Masonry.Quarry;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // MeshShellSplitComponent — separates a multi-shell Rhino mesh into
    // one Slab per connected shell. Each shell is assumed convex (Slab
    // requirement); SlabFromMesh's caveat applies.
    //
    // ComponentGuid: DBECFDAE-BFCA-4123-4567-89012345678A
    // =========================================================================

    /// <summary>
    /// Frahan Cut &gt; Quarry &gt; Mesh Shell Split.
    /// </summary>
    [Algorithm("Connected-components labelling", "Frahan-original", Note = "Textbook graph traversal over the mesh face graph; no specific paper implemented.")]
        [DesignApplication(
        "Separates a multi-shell Rhino mesh into one Slab per  connected shell",
        DesignFlow.Bridges,
        Precedent = "Frahan-original mesh-shell splitter (multi-shell decomposition)")]
    public sealed class MeshShellSplitComponent : FrahanComponentBase
    {
        public MeshShellSplitComponent()
            : base(
                "Mesh Shell Split", "ShellSplit",
                "Separates a multi-shell Rhino mesh into one Slab per " +
                "connected shell. Each output shell is assumed convex " +
                "(Slab's input requirement). Frahan-original method.",
                "Frahan", "Block Cutting")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("DBECFDAE-BFCA-4123-4567-89012345678A");

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override Bitmap Icon => IconProvider.Load("CoacdDecompose.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M",
                "Multi-shell Rhino mesh.",
                GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Slabs", "S",
                "One Slab per connected shell.",
                GH_ParamAccess.list);
            p.AddMeshParameter("Mesh", "M",
                "One Rhino Mesh per shell (parallel to the Slabs list).",
                GH_ParamAccess.list);
        }

        protected override void SolveSafe(IGH_DataAccess da)
        {
            Mesh mesh = null;
            if (!da.GetData(0, ref mesh) || mesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No mesh provided.");
                return;
            }
            if (mesh.Faces.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh has no faces.");
                return;
            }

            int vCount = mesh.Vertices.Count;
            var verts = new double[vCount * 3];
            for (int i = 0; i < vCount; i++)
            {
                var pt = mesh.Vertices[i];
                verts[3 * i + 0] = pt.X;
                verts[3 * i + 1] = pt.Y;
                verts[3 * i + 2] = pt.Z;
            }
            // Triangulate quads on the fly so the splitter sees only triangles.
            var tris = new List<int>(mesh.Faces.Count * 3);
            for (int f = 0; f < mesh.Faces.Count; f++)
            {
                var face = mesh.Faces[f];
                tris.Add(face.A); tris.Add(face.B); tris.Add(face.C);
                if (face.IsQuad)
                {
                    tris.Add(face.A); tris.Add(face.C); tris.Add(face.D);
                }
            }

            IReadOnlyList<MeshShellSplitter.Shell> shells;
            try
            {
                shells = MeshShellSplitter.Split(verts, tris.ToArray());
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Mesh shell split failed: {ex.Message}");
                return;
            }

            var slabs = new List<Slab>(shells.Count);
            for (int s = 0; s < shells.Count; s++)
            {
                try
                {
                    var faceList = ToTriangleFaceList(shells[s].TriangleIndices);
                    slabs.Add(new Slab(shells[s].VertexCoordsXyz, faceList));
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Shell {s} produced an invalid Slab: {ex.Message}");
                }
            }
            da.SetDataList(0, slabs);
            da.SetDataList(1, GhInterop.SlabsToMeshes(slabs));
        }

        private static IReadOnlyList<IReadOnlyList<int>> ToTriangleFaceList(int[] tris)
        {
            int n = tris.Length / 3;
            var faces = new IReadOnlyList<int>[n];
            for (int t = 0; t < n; t++)
            {
                faces[t] = new[] { tris[3 * t + 0], tris[3 * t + 1], tris[3 * t + 2] };
            }
            return faces;
        }
    }
}
