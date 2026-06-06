#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Geometry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // BlockGroundTransformsComponent — recover the placement transform for
    // each block in an assembly. Two paths:
    //
    //   • If a per-block Existing Transform is wired in, pass it through.
    //   • Otherwise, recover via vertex-paired Horn QAO (Source vs Placed
    //     mesh). Requires both meshes to have the same vertex count and
    //     vertex order (true when the placed mesh was produced by
    //     transforming the source without remeshing or dedup).
    //
    // The output transform is expressed in the Ground Plane's local frame
    // by similarity. Default ground = world XY → output is the raw world-
    // frame transform.
    //
    // ComponentGuid: 23456789-ABCD-EF01-2345-6789ABCDEF01
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Block Ground Transforms.
    /// Recovers the rigid transform per block from canonical (source)
    /// to placed pose, expressed relative to a ground plane.
    /// </summary>
        [Algorithm("Closed-form absolute orientation (Horn QAO)",
        "Horn 1987, Closed-form solution of absolute orientation using unit quaternions, JOSA A 4(4):629-642",
        Doi = "10.1364/JOSAA.4.000629")]
        [DesignApplication(
        "Recovers the rigid transform per placed block",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original per-block ground-transform recovery")]
    public sealed class BlockGroundTransformsComponent : GH_Component
    {
        public BlockGroundTransformsComponent()
            : base(
                "Block Ground Transforms", "BlkXform",
                "Recovers the rigid transform per placed block. Wire " +
                "Source Meshes (canonical) and Placed Meshes (post- " +
                "assembly) for vertex-paired Horn QAO recovery, OR wire " +
                "Existing Transforms for direct pass-through. Output " +
                "transforms are expressed in the Ground Plane's frame " +
                "(default: world XY). Implements Horn QAO (Horn 1987).",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("23456789-ABCD-EF01-2345-6789ABCDEF01");

        protected override Bitmap Icon => IconProvider.Load("RigidTransform.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Placed Meshes", "P",
                "Meshes after assembly (one per block). The recovered " +
                "transform takes the matching Source Mesh to this pose.",
                GH_ParamAccess.list);
            p.AddMeshParameter("Source Meshes", "S",
                "Canonical / pre-placement meshes (one per block, parallel " +
                "to Placed). Required when Existing Transforms is empty. " +
                "Vertex count and order must match the placed mesh.",
                GH_ParamAccess.list);
            p[1].Optional = true;
            p.AddTransformParameter("Existing Transforms", "Tx",
                "Optional pre-known transforms (parallel to Placed). When " +
                "supplied at index i, this transform is passed through " +
                "verbatim and Horn QAO is skipped for that block.",
                GH_ParamAccess.list);
            p[2].Optional = true;
            p.AddPlaneParameter("Ground Plane", "G",
                "Reference frame. Output transforms are re-expressed in " +
                "this plane's local basis. Default: world XY.",
                GH_ParamAccess.item, Plane.WorldXY);
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTransformParameter("Transforms", "T",
                "Per-block rigid transform (canonical → placed) in the " +
                "ground-plane frame.",
                GH_ParamAccess.list);
            p.AddPointParameter("Origins", "O",
                "Where each block's local origin lands. Useful for " +
                "anchoring labels on the canvas.",
                GH_ParamAccess.list);
            p.AddNumberParameter("RMS", "E",
                "Per-block Horn QAO residual (root-mean-square of " +
                "vertex-pair distances after fit). 0.0 for pass-through.",
                GH_ParamAccess.list);
            p.AddTextParameter("Status", "St",
                "'passthrough' / 'kabsch (V=…)' / 'failed: …' per block.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var placed = new List<Mesh>();
            if (!da.GetDataList(0, placed) || placed.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No placed meshes provided.");
                return;
            }

            var source = new List<Mesh>();
            da.GetDataList(1, source);  // optional

            var existing = new List<Transform>();
            da.GetDataList(2, existing);  // optional

            Plane ground = Plane.WorldXY;
            da.GetData(3, ref ground);

            // Validate parallel lengths (only when populated).
            if (source.Count > 0 && source.Count != placed.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Source Meshes count ({source.Count}) must equal " +
                    $"Placed Meshes count ({placed.Count}).");
                return;
            }
            if (existing.Count > 0 && existing.Count != placed.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Existing Transforms count ({existing.Count}) must " +
                    $"equal Placed Meshes count ({placed.Count}).");
                return;
            }

            // Pre-compute the basis-change transforms for the ground plane.
            // worldToGround maps world coords into ground-plane-local
            // coords; groundToWorld is its inverse.
            Transform worldToGround = Transform.PlaneToPlane(ground, Plane.WorldXY);
            Transform groundToWorld = Transform.PlaneToPlane(Plane.WorldXY, ground);

            int n = placed.Count;
            var outTx = new List<Transform>(n);
            var outOrigins = new List<Point3d>(n);
            var outRms = new List<double>(n);
            var outStatus = new List<string>(n);

            for (int i = 0; i < n; i++)
            {
                Transform tWorld;
                double rms;
                string status;

                bool hasExisting =
                    existing.Count == n && IsRigid(existing[i]);
                if (hasExisting)
                {
                    tWorld = existing[i];
                    rms = 0.0;
                    status = "passthrough";
                }
                else if (source.Count == n && source[i] != null && placed[i] != null)
                {
                    if (TryRecover(source[i], placed[i], out tWorld, out rms, out string err))
                    {
                        status = $"kabsch (V={source[i].Vertices.Count}, rms={rms:0.######})";
                    }
                    else
                    {
                        tWorld = Transform.Identity;
                        rms = double.NaN;
                        status = $"failed: {err}";
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            $"Block {i}: {err}");
                    }
                }
                else
                {
                    tWorld = Transform.Identity;
                    rms = double.NaN;
                    status = "failed: no Source mesh and no Existing transform supplied";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Block {i}: " + status);
                }

                // Re-express in ground-plane basis: T_g = M · T_w · M⁻¹.
                Transform tGround = worldToGround * tWorld * groundToWorld;
                outTx.Add(tGround);

                // Origin = where (0, 0, 0) in source-local lands. With the
                // basis-change applied this is just the translation column.
                outOrigins.Add(new Point3d(tGround.M03, tGround.M13, tGround.M23));
                outRms.Add(rms);
                outStatus.Add(status);
            }

            da.SetDataList(0, outTx);
            da.SetDataList(1, outOrigins);
            da.SetDataList(2, outRms);
            da.SetDataList(3, outStatus);
        }

        // ─── Recovery helper ────────────────────────────────────────────────

        private static bool TryRecover(
            Mesh src, Mesh dst,
            out Transform world, out double rms, out string error)
        {
            world = Transform.Identity;
            rms = double.NaN;
            error = null;

            int vs = src.Vertices.Count;
            int vd = dst.Vertices.Count;
            if (vs != vd)
            {
                error = $"vertex-count mismatch (source V={vs}, placed V={vd})";
                return false;
            }
            if (vs < 3)
            {
                error = $"need at least 3 vertices, got V={vs}";
                return false;
            }

            // Flatten vertex coords. Mesh.Vertices is float in Rhino; cast
            // up to double for the QAO solver.
            var sxyz = new double[vs * 3];
            var dxyz = new double[vs * 3];
            for (int v = 0; v < vs; v++)
            {
                var sp = src.Vertices[v];
                var dp = dst.Vertices[v];
                sxyz[3 * v + 0] = sp.X; sxyz[3 * v + 1] = sp.Y; sxyz[3 * v + 2] = sp.Z;
                dxyz[3 * v + 0] = dp.X; dxyz[3 * v + 1] = dp.Y; dxyz[3 * v + 2] = dp.Z;
            }

            try
            {
                var r = RigidTransformRecovery.Solve(sxyz, dxyz);
                var t = Transform.Identity;
                t.M00 = r.Rotation[0, 0]; t.M01 = r.Rotation[0, 1]; t.M02 = r.Rotation[0, 2]; t.M03 = r.Translation[0];
                t.M10 = r.Rotation[1, 0]; t.M11 = r.Rotation[1, 1]; t.M12 = r.Rotation[1, 2]; t.M13 = r.Translation[1];
                t.M20 = r.Rotation[2, 0]; t.M21 = r.Rotation[2, 1]; t.M22 = r.Rotation[2, 2]; t.M23 = r.Translation[2];
                t.M30 = 0; t.M31 = 0; t.M32 = 0; t.M33 = 1.0;
                world = t;
                rms = r.RmsError;
                return true;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        // ─── Transform sanity ───────────────────────────────────────────────

        // Rhino populates uninitialised wires with the zero transform (all
        // entries 0). That's not a valid rigid motion, so treat it as
        // "no transform supplied" and fall through to recovery.
        private static bool IsRigid(Transform t)
        {
            // A rigid motion has |det(R)| ≈ 1 and M33 == 1. Zero transform
            // fails both. Any user-authored transform passes.
            double det =
                t.M00 * (t.M11 * t.M22 - t.M12 * t.M21)
              - t.M01 * (t.M10 * t.M22 - t.M12 * t.M20)
              + t.M02 * (t.M10 * t.M21 - t.M11 * t.M20);
            return Math.Abs(det) > 1e-9 && Math.Abs(t.M33) > 1e-9;
        }
    }
}
