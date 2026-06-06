#nullable disable
// TODO: citation pending verification (audit row 4, top-10 priority gaps).
// The "Cockroach-style" tag below references the third-party Cockroach
// Grasshopper plugin family but a primary peer-reviewed source for the
// proximity-based contact-detection recipe has not been verified.
// Candidate sources to confirm before promoting to [Algorithm] attribute:
//   (a) Cockroach 3 GH plugin (Petras Vestartas / mesh-machine proximity ops)
//   (b) Whiting 2009 "Procedurally-Assembled Stable Masonry" contact-stability
//   (c) Frahan-original proximity-contact heuristic (declare explicitly)
// Per AGENTS.md §9: no invented citations. Marked **needs citation —
// unresolved** in algorithm_references_audit.md.
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.DataModel;
using Frahan.Masonry.Interfaces;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // RobustAutoInterfacesComponent — Cockroach-style proximity-based contact
    // detection. Use this when the simpler polygon-face Auto Interfaces
    // doesn't pick up contacts because:
    //   - Meshes have slight gaps between blocks (scan / photogrammetry).
    //   - Contact regions are non-planar (curved stone surfaces).
    //   - Triangulation is irregular (not nicely flat-faced).
    //
    // Trade-off: O(|VA| × |TB|) per pair without spatial indexing → slower
    // on large meshes. AABB pre-filter helps. For typical wall sizes with
    // simple blocks this is sub-second.
    //
    // ComponentGuid: F2D000B4-CADC-4F2D-A0B4-7E60CADA15A0
    // (was EF012345-6789-ABCD-EF01-23456789ABCD; collided with BlockSizeDistributionComponent)
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Robust Auto Interfaces.
    /// Proximity-based contact detection for messy real-world meshes.
    /// </summary>
        [Algorithm("Auto interface detection",
        "Frahan-original",
        Note = "contact-interface heuristic; no peer-reviewed source")]
        [DesignApplication(
        "Detects block-to-block contacts via mesh-vertex proximity",
        DesignFlow.BottomUp,
        Precedent = "Cockroach mesh-machine plug-in inspiration (citation TODO per audit row 4); Whiting 2009 procedurally-assembled stable masonry as candidate alternative")]
    public sealed class RobustAutoInterfacesComponent : GH_Component
    {
        public RobustAutoInterfacesComponent()
            : base(
                "Robust Auto Interfaces", "RAutoIf",
                "Detects block-to-block contacts via mesh-vertex proximity. " +
                "Robust to slight gaps, non-planar contact regions, and " +
                "irregular triangulation (scan-derived meshes). Use this " +
                "when 'Auto Interfaces' (polygon-based) misses contacts. " +
                "Frahan-original method.",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("F2D000B4-CADC-4F2D-A0B4-7E60CADA15A0");

        protected override Bitmap Icon => IconProvider.Load("ContactDetector.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Meshes", "M",
                "Block meshes in their final placed positions. Standard Rhino " +
                "mesh wires; this component finds proximity-based contacts.",
                GH_ParamAccess.list);
            p.AddTextParameter("Block Ids", "Ids",
                "One ID per mesh in the same order. Must match the IDs used " +
                "to construct MasonryBlocks.",
                GH_ParamAccess.list);
            p.AddNumberParameter("Distance Tolerance", "Dtol",
                "Max distance between two surfaces to count as contact " +
                "(document units). Default 0.001 (1 mm). Raise for noisy " +
                "scan data; lower for exact-coord meshes.",
                GH_ParamAccess.item, 0.001);
            p.AddNumberParameter("Angle Tolerance Deg", "Atol",
                "Contact points are grouped when their surface normals agree " +
                "within this angle. Default 5° — accommodates mild surface " +
                "curvature; tighten to 1° for sharply-faceted blocks.",
                GH_ParamAccess.item, 5.0);
            p.AddIntegerParameter("Min Contact Points", "MinN",
                "Minimum contact points required to emit a MasonryInterface. " +
                "Default 3 (= polygon triangle minimum). Raise to filter " +
                "out spurious single-vertex grazes.",
                GH_ParamAccess.item, 3);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Interfaces", "I",
                "Detected MasonryInterfaces. Wire into Masonry Assembly.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Count", "N",
                "Number of detected interfaces.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var meshes = new List<Mesh>();
            if (!da.GetDataList(0, meshes))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No meshes provided.");
                return;
            }
            var ids = new List<string>();
            if (!da.GetDataList(1, ids))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No block ids provided.");
                return;
            }
            if (ids.Count != meshes.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Block Ids count ({ids.Count}) must equal Meshes count ({meshes.Count}).");
                return;
            }

            double dTol = 0.001, aTolDeg = 5.0;
            int minPts = 3;
            da.GetData(2, ref dTol);
            da.GetData(3, ref aTolDeg);
            da.GetData(4, ref minPts);

            var snapshots = new List<MeshSnapshot>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++)
            {
                var snap = ToSnapshot(meshes[i]);
                if (snap == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Meshes[{i}] is invalid (need >= 3 vertices and >= 1 face).");
                    return;
                }
                snapshots.Add(snap);
            }

            IReadOnlyList<MasonryInterface> result;
            try
            {
                result = MeshContactDetector.Detect(snapshots, ids, dTol, aTolDeg, minPts);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Robust auto-interface detection failed: {ex.Message}");
                return;
            }

            if (result.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No interfaces detected. Try raising Distance Tolerance or " +
                    "checking that the meshes actually touch.");
            }
            da.SetDataList(0, result);
            da.SetData(1, result.Count);
        }

        private static MeshSnapshot ToSnapshot(Mesh mesh)
        {
            if (mesh == null) return null;
            int v = mesh.Vertices.Count;
            int f = mesh.Faces.Count;
            if (v < 3 || f < 1) return null;
            var verts = new double[v * 3];
            for (int i = 0; i < v; i++)
            {
                var pt = mesh.Vertices[i];
                verts[3 * i + 0] = pt.X;
                verts[3 * i + 1] = pt.Y;
                verts[3 * i + 2] = pt.Z;
            }
            var tris = new List<int>(f * 3);
            for (int fi = 0; fi < f; fi++)
            {
                var face = mesh.Faces[fi];
                tris.Add(face.A); tris.Add(face.B); tris.Add(face.C);
                if (face.IsQuad)
                {
                    tris.Add(face.A); tris.Add(face.C); tris.Add(face.D);
                }
            }
            return new MeshSnapshot(verts, tris.ToArray());
        }
    }
}
