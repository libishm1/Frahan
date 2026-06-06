#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.Masonry.Geometry;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.GH.Attributes;

namespace Frahan.GH.Masonry
{
    // =========================================================================
    // MatchBlockTransformComponent — given a SMALL library of canonical
    // mesh shapes and a list of placed (target) meshes, for each target
    // find which library entry it's a transformed copy of and recover the
    // rigid transform via Horn QAO.
    //
    // Use case: 100 placed blocks but only ~5 unique cut shapes. The user
    // supplies 5 + 100 instead of 100 + 100 (saves authoring effort).
    //
    // Selection rule:
    //   1. Skip library entries whose vertex count differs from the target.
    //   2. Run Horn QAO on each surviving candidate.
    //   3. Pick the candidate with the lowest RMS.
    //   4. If best RMS > RmsThreshold, status = "low confidence"; the
    //      transform is still emitted because a higher-tolerance match
    //      may still be useful for downstream visualisation.
    //
    // Compared to PolytopeSolutions' MatchMeshTransformation:
    //   • Their impl picks 3 RANDOM vertices and uses Plane.PlaneToPlane.
    //     Random picks can be near-collinear → catastrophic accuracy loss.
    //   • Ours: full N-vertex least-squares Horn QAO; reports per-fit RMS.
    //
    // ComponentGuid: 89ABCDEF-0123-4567-89AB-CDEF01234567
    // =========================================================================

    /// <summary>
    /// Frahan &gt; Masonry &gt; Match Block Transform.
    /// Library-based auto-match: per target mesh, find the best canonical
    /// candidate and recover the transform.
    /// </summary>
        [Algorithm("Closed-form absolute orientation (Horn QAO)",
        "Horn 1987, Closed-form solution of absolute orientation using unit quaternions, JOSA A 4(4):629-642",
        Doi = "10.1364/JOSAA.4.000629")]
        [DesignApplication(
        "Library-based auto-match: given a list of canonical  library meshes and a list of placed (target) meshes,  ...",
        DesignFlow.BottomUp,
        Precedent = "Frahan-original block-transform matcher")]
    public sealed class MatchBlockTransformComponent : GH_Component
    {
        public MatchBlockTransformComponent()
            : base(
                "Match Block Transform", "MatchBlk",
                "Library-based auto-match: given a list of canonical " +
                "library meshes and a list of placed (target) meshes, " +
                "find which library entry each target was transformed " +
                "from and recover the placement transform via Horn QAO. " +
                "Strictly more accurate than 3-random-vertex matching " +
                "because the fit is least-squares over all N vertex pairs. " +
                "Implements Horn QAO (Horn 1987).",
                "Frahan", "Masonry")
        {
        }

        public override Guid ComponentGuid =>
            new Guid("89ABCDEF-0123-4567-89AB-CDEF01234567");

        protected override Bitmap Icon => IconProvider.Load("RigidTransform.png");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Library Meshes", "Lib",
                "Canonical mesh shapes (the 'pick' representations).",
                GH_ParamAccess.list);
            p.AddMeshParameter("Target Meshes", "T",
                "Placed meshes to match against the library.",
                GH_ParamAccess.list);
            p.AddNumberParameter("RMS Threshold", "Rms",
                "Maximum acceptable per-fit RMS for a high-confidence " +
                "match. Targets whose best library fit exceeds this " +
                "threshold are still emitted but tagged 'low confidence'. " +
                "Default 1e-3.",
                GH_ParamAccess.item, 1e-3);
            p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTransformParameter("Transforms", "T",
                "Per-target transform: library[matched] → target.",
                GH_ParamAccess.list);
            p.AddIntegerParameter("Matched Library Index", "Idx",
                "Per-target library index. -1 when no candidate had a " +
                "matching vertex count.",
                GH_ParamAccess.list);
            p.AddNumberParameter("RMS", "E",
                "Per-target Horn QAO residual at the chosen library entry.",
                GH_ParamAccess.list);
            p.AddTextParameter("Status", "St",
                "'matched (rms=…)' / 'low confidence (rms=…)' / 'no match'.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var library = new List<Mesh>();
            var targets = new List<Mesh>();
            if (!da.GetDataList(0, library) || library.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No Library Meshes provided.");
                return;
            }
            if (!da.GetDataList(1, targets) || targets.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No Target Meshes provided.");
                return;
            }
            double rmsThr = 1e-3;
            da.GetData(2, ref rmsThr);
            if (rmsThr < 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"RMS Threshold must be >= 0, got {rmsThr}.");
                return;
            }

            // Pre-flatten library vertex coords once (we re-use across targets).
            var libCoords = new List<double[]>(library.Count);
            for (int i = 0; i < library.Count; i++)
            {
                libCoords.Add(FlattenVerts(library[i]));
            }

            var outTx = new List<Transform>(targets.Count);
            var outIdx = new List<int>(targets.Count);
            var outRms = new List<double>(targets.Count);
            var outStatus = new List<string>(targets.Count);

            for (int t = 0; t < targets.Count; t++)
            {
                var tgt = targets[t];
                var tgtCoords = FlattenVerts(tgt);
                int tgtV = tgtCoords.Length / 3;

                int bestIdx = -1;
                double bestRms = double.PositiveInfinity;
                Transform bestTx = Transform.Identity;

                for (int j = 0; j < library.Count; j++)
                {
                    if (libCoords[j].Length != tgtCoords.Length) continue;  // V mismatch
                    if (tgtV < 3) continue;
                    try
                    {
                        var r = RigidTransformRecovery.Solve(libCoords[j], tgtCoords);
                        if (r.RmsError < bestRms)
                        {
                            bestRms = r.RmsError;
                            bestIdx = j;
                            bestTx = ToRhinoTransform(r);
                        }
                    }
                    catch
                    {
                        // Skip degenerate fits.
                    }
                }

                if (bestIdx < 0)
                {
                    outTx.Add(Transform.Identity);
                    outIdx.Add(-1);
                    outRms.Add(double.NaN);
                    outStatus.Add("no match");
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Target {t}: no library candidate with matching vertex count " +
                        $"(target V={tgtV}).");
                }
                else
                {
                    outTx.Add(bestTx);
                    outIdx.Add(bestIdx);
                    outRms.Add(bestRms);
                    outStatus.Add(bestRms <= rmsThr
                        ? $"matched (rms={bestRms:0.######})"
                        : $"low confidence (rms={bestRms:0.######})");
                }
            }

            da.SetDataList(0, outTx);
            da.SetDataList(1, outIdx);
            da.SetDataList(2, outRms);
            da.SetDataList(3, outStatus);
        }

        private static double[] FlattenVerts(Mesh m)
        {
            int v = m.Vertices.Count;
            var arr = new double[v * 3];
            for (int i = 0; i < v; i++)
            {
                var p = m.Vertices[i];
                arr[3 * i + 0] = p.X;
                arr[3 * i + 1] = p.Y;
                arr[3 * i + 2] = p.Z;
            }
            return arr;
        }

        private static Transform ToRhinoTransform(RigidTransformResult r)
        {
            var t = Transform.Identity;
            t.M00 = r.Rotation[0, 0]; t.M01 = r.Rotation[0, 1]; t.M02 = r.Rotation[0, 2]; t.M03 = r.Translation[0];
            t.M10 = r.Rotation[1, 0]; t.M11 = r.Rotation[1, 1]; t.M12 = r.Rotation[1, 2]; t.M13 = r.Translation[1];
            t.M20 = r.Rotation[2, 0]; t.M21 = r.Rotation[2, 1]; t.M22 = r.Rotation[2, 2]; t.M23 = r.Translation[2];
            t.M30 = 0; t.M31 = 0; t.M32 = 0; t.M33 = 1.0;
            return t;
        }
    }
}
