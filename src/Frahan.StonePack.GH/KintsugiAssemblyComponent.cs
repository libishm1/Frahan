#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using Frahan.EdgeMatching;
using Frahan.GH.Attributes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Frahan.GH.Kintsugi;

// =============================================================================
// Frahan > Kintsugi > Kintsugi Pottery Joiner.
//
// 3D mesh fracture-assembly component inspired by PuzzleFusion++ (Wang et al.
// NeurIPS 2024) -- but instead of a learned diffusion+verification model,
// this component reuses Frahan's existing deterministic 5-stage edge-matching
// pipeline operating on each fragment's NAKED-EDGE rim loops.
//
// Concept (Kintsugi = Japanese art of repairing broken pottery with gold):
//   Each pottery fragment has open mesh boundaries that mark its fracture
//   rims. Two fragments were neighbours pre-break iff one's fracture rim
//   matches the other's fracture rim (the same physical edge curve).
//   We treat every naked-edge loop as a 3D Panel and let
//   Frahan.EdgeMatching.Core do the same thing it does for stone shards:
//     segment → hash → phase-correlate → ICP → beam-search assembly.
//
// Pipeline:
//   1. Extract naked-edge polylines from each input Mesh (open boundaries
//      = fracture rims).
//   2. The first fragment's rim-loops become Panel(IsAnchored=true) frames.
//   3. Every other fragment's rim-loops become Panel(Shard).
//   4. SegmentHashIndex collects rim-segments via BoundarySegmenter3D.
//   5. AssemblySolver does beam-search SE(3) placement.
//   6. Per-fragment Transform = the AppliedTransform of any of its placed
//      rim-panels (rigid placement is shared across all rims of one
//      fragment).
//   7. Transformed Meshes are emitted alongside the per-fragment Transforms.
//
// Naming: deliberately "Kintsugi" not "PuzzleFusion". This is a CLASSICAL
// solver inspired by PuzzleFusion++'s structure, not a port of it. No
// neural network, no diffusion, no pre-trained checkpoints. Inputs are
// just the meshes + a few solver knobs. Runs entirely in-process in net48.
//
// Reference: Wang, E., Liu, K., Han, X., Liu, Y., Jiang, X., et al.
//   "PuzzleFusion++: Auto-agglomerative 3D Fracture Assembly by Denoising
//   and Verification" -- the auto-agglomerative structure (group, verify,
//   refine) inspired the multi-fragment iterative path. Repository:
//   https://github.com/eric-zqwang/puzzlefusion-plusplus
//
// Limitations vs PuzzleFusion++:
//   - No learned descriptors -- matches purely on edge-geometry signature.
//   - Works best when fracture rims are well-defined (clean naked edges).
//   - Pottery with smooth, featureless rims gets few correspondences and
//     fragments may be left Unplaced.
//   - No verification stage -- one-shot beam search, not iterative refine.
// =============================================================================

[Algorithm("Kintsugi 3D pottery joiner",
    "Geometric interim. Full GPL-3.0 honest port of PuzzleFusion++ underway. " +
    "Wang, Chen, Furukawa; ICLR 2025; arXiv:2406.00259; " +
    "https://github.com/eric-zqwang/puzzlefusion-plusplus",
    Doi = "arXiv:2406.00259",
    WikiPath = "wiki/research/kintsugi_3d_fracture_reassembly.md",
    Note = "Phase 0 (current): clean-room geometric matcher using Frahan EdgeMatching.Core 3D pipeline + penetration-based verifier. NO learned model. Phase 1-8 (deferred, ~8-10 weeks): direct C# port of PuzzleFusion++ PointNet++ + VQ-VAE + diffusion denoiser + learned verifier. Will adopt GPL-3.0 per LICENSE-GPL-3.0.txt in the Kintsugi/ folder. The current component stays as the default Mode=Geometric path even after the port lands; learned path opt-in via Mode=Port input.")]
[Algorithm("Auto-agglomerative outer loop",
    "Clean-room translation of PuzzleFusion++ Section 3 outer schedule",
    Note = "Pick anchor cluster -> run 5-stage edge match against unplaced -> geometric verify -> merge into anchor -> repeat until no progress or maxRounds")]
[Algorithm("BoundarySegmenter3D + SegmentHashIndex + ConstrainedIcp3D + AssemblySolver",
    "Frahan-original 5-stage 3D edge-matching pipeline (Stages 1-5)",
    WikiPath = "wiki/algorithms/edge_matching/")]
[Algorithm("Pairwise penetration-based geometric verifier",
    "Frahan-original substitute for PuzzleFusion++ learned binary-classifier verifier",
    Note = "Rejects placements whose transformed mesh penetrates already-placed meshes beyond a tolerance. No learned features.")]
[DesignApplication(
    "3D mesh fracture-assembly via naked-edge rim matching",
    DesignFlow.BottomUp,
    Precedent = "PuzzleFusion++ Wang Chen Furukawa 2025 ICLR (arXiv:2406.00259); Kintsugi Japanese repair tradition; Frahan-original pose composition fix",
    Tolerance = "reassembled pose Hausdorff <= 2 mm to ground truth; verifier score >= 0.5")]
public sealed class KintsugiAssemblyComponent : GH_Component
{
    public KintsugiAssemblyComponent()
        : base("Frahan Kintsugi",
            "Kintsugi",
            "3D mesh fracture-assembly via naked-edge rim matching. " +
            "Each fragment's open-boundary loops are treated as 3D " +
            "panels and joined by the same deterministic 5-stage " +
            "edge-matching pipeline used by Frahan Trencadís EdgeMatch. " +
            "Inspired by PuzzleFusion++ but no learned model; runs " +
            "entirely in-process. Best when fracture rims are clean " +
            "and well-defined.",
            "Frahan", "Kintsugi")
    {
    }

    // Stable GUID. NOTE: hex only (0-9, A-F). The earlier mnemonic
    // "F2D0KS01-..." had K and S which are NOT hex; Guid.Parse threw
    // FormatException on .gha load, killing the entire assembly and
    // wiping every Frahan ribbon entry. This GUID stays stable forever
    // per AGENTS.md §8 (component GUIDs are stable, new component =
    // new GUID).
    public override Guid ComponentGuid =>
        new Guid("F2D00501-2026-4522-B0B0-1ABE15A0CAFE");

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => IconProvider.Load("KintsugiAssemble.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddMeshParameter("Fragments", "F",
            "List of mesh fragments to reassemble. First fragment is " +
            "anchored at the identity transform; all others are placed " +
            "relative to it. Each mesh must have at least one open " +
            "boundary loop (naked edges) for the GEOMETRIC path. " +
            "For Mode=Port: OPTIONAL when Point Clouds (PC) is wired -- the " +
            "port path derives output meshes from the point clouds directly " +
            "if Fragments is unwired.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Joint Width", "J",
            "Edge-match residual tolerance (document units). Larger = " +
            "more forgiving rim alignment. Default 1.0.",
            GH_ParamAccess.item, 1.0);
        p.AddNumberParameter("Sample Spacing", "Sp",
            "Arc-length spacing along each naked-edge loop. Match the " +
            "mesh's edge length. Default 1.0.",
            GH_ParamAccess.item, 1.0);
        p.AddNumberParameter("Break Angle Deg", "Ba",
            "Curvature peak threshold for segment break (degrees). " +
            "Lower = more sensitive (more segments per rim). Default 8 " +
            "(was 25 -- pottery rims are usually smoother than wood, " +
            "needs lower threshold to find the curvature peaks).",
            GH_ParamAccess.item, 8.0);
        p.AddNumberParameter("Min Segment Length", "Ms",
            "Below this chord length a rim segment is treated as noise. " +
            "Lower = preserves shorter notch features. Default 1.0 " +
            "(was 5 -- too aggressive for dense rim meshes; lots of " +
            "real notches got filtered out as noise).",
            GH_ParamAccess.item, 1.0);
        p.AddIntegerParameter("Beam Width", "Bw",
            "Beam-search concurrent states. Pottery fragments have more " +
            "ambiguous matches than wood; 32 recommended.",
            GH_ParamAccess.item, 32);
        p.AddIntegerParameter("Max Iterations", "Mi",
            "AssemblySolver inner-loop iteration cap (per round).",
            GH_ParamAccess.item, 2000);
        p.AddNumberParameter("Min Loop Length", "Ll",
            "Naked-edge loops shorter than this are ignored as noise " +
            "(e.g. tiny holes from mesh artifacts). Default 10.",
            GH_ParamAccess.item, 10.0);
        p.AddIntegerParameter("Max Rounds", "Mr",
            "Auto-agglomerative outer-loop cap. After each round, " +
            "successfully-placed fragments merge into the anchor cluster " +
            "and unplaced fragments retry against the larger cluster. " +
            "Mirrors PuzzleFusion++'s up-to-6-iteration outer schedule.",
            GH_ParamAccess.item, 6);
        p.AddNumberParameter("Verifier Penetration Tol", "Vp",
            "Geometric verifier: reject placements whose transformed " +
            "mesh penetrates an already-placed mesh by more than this " +
            "distance (document units). 0 = disable the verifier. " +
            "Replaces PuzzleFusion++'s learned binary verifier. Default 0.5.",
            GH_ParamAccess.item, 0.5);
        p.AddIntegerParameter("Diffusion Steps", "T",
            "Mode=Port only. Number of diffusion sampling steps. Higher = " +
            "better assembly quality, slower. Paper default is 20; for " +
            "interactive prototyping use 5-10. Cost scales linearly " +
            "(re-encoder runs at each step).",
            GH_ParamAccess.item, 20);
        p.AddBooleanParameter("Use Port Mode", "Port",
            "FALSE (default) = geometric path via Frahan.EdgeMatching.Core; " +
            "no GPL code linked at runtime; in-process; deterministic. " +
            "TRUE = GPL-3.0 PuzzleFusion++ learned path via Frahan.Kintsugi.Port; " +
            "requires kintsugi.bin weight file in the .gha deploy folder.",
            GH_ParamAccess.item, false);
        p.AddBooleanParameter("Run", "R",
            "Execute the solver.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Use TorchSharp", "Torch",
            "Mode=Port only. FALSE (default) = manual C# port denoiser " +
            "(~3-5% per-layer drift vs paper). TRUE = TorchSharp/libtorch " +
            "denoiser using PyTorch's exact kernels for paper-quality " +
            "inference. Requires libtorch DLLs in the .gha deploy folder. " +
            "Falls back to manual port if TorchSharp init fails.",
            GH_ParamAccess.item, false);
        p.AddPointParameter("Point Clouds", "PC",
            "OPTIONAL Mode=Port override. Per-fragment point cloud as a " +
            "Grasshopper tree: one BRANCH per fragment, N=1000 points per " +
            "branch. When wired, the Port-mode pipeline uses these points " +
            "DIRECTLY for the encoder instead of sampling N=1000 points from " +
            "the Fragments meshes. Useful when you have authoritative point " +
            "data (e.g. from Load BB Sample) and don't want sampling noise.\n" +
            "If wired branch-count mismatches Fragments count, this input is " +
            "ignored and the mesh sampler runs. Leave unwired for the default " +
            "mesh-sampling path.",
            GH_ParamAccess.tree);
        p.AddNumberParameter("Verifier Accept Threshold", "Vt",
            "Mode=Port only. Minimum verifier pair-score for a fragment to be " +
            "PLACED via the network pose. Fragments whose best pair-score is " +
            "below this stay at their INPUT world position and are listed as " +
            "Unplaced. Default 0.5 (matches the 'STRONG' tag in the report). " +
            "Lower it (e.g. 0.45) to accept the network's near-miss pairs on " +
            "hard multi-fragment samples; raise it to demand higher confidence.",
            GH_ParamAccess.item, 0.5);
        // Appended LAST (index 16) so existing canvases keep their wiring.
        // GEOMETRIC path only; Mode=Port does not use the edge-match ICP.
        p.AddBooleanParameter("Non-Crossing", "Nc",
            "Geometric path only. Order-preserving rim correspondence. " +
            "FALSE (default) = free nearest-point ICP (unchanged behaviour). " +
            "TRUE = monotone, non-crossing point pairing between fracture rims " +
            "(OrderedBoundaryMatcher); more robust on wiggly / noisy rims where " +
            "free matching tangles. Ignored in Mode=Port.",
            GH_ParamAccess.item, false);

        // Fragments and Point Clouds are mutually-substitutable sources:
        // the geometric path needs Fragments; Mode=Port can run from either
        // Fragments (mesh sampling) OR a Point Clouds tree. Mark BOTH
        // optional so Grasshopper does not emit "Input parameter X failed to
        // collect data" when only one is wired. SolveInstance already checks
        // for the absent one and routes accordingly.
        p[0].Optional = true;    // Fragments
        p[14].Optional = true;   // Point Clouds
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Assembled Fragments", "M",
            "Input fragments transformed into their joined placement. " +
            "First fragment is at identity; others composed.",
            GH_ParamAccess.list);
        p.AddTransformParameter("Transforms", "X",
            "Per-fragment rigid SE(3) Transform. Parallel to Fragments.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Placed Indices", "Pi",
            "Source-list indices of fragments that the solver placed.",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Unplaced Indices", "Ui",
            "Source-list indices of fragments left unjoined (no rim match found).",
            GH_ParamAccess.list);
        p.AddNumberParameter("Residuals", "Re",
            "Per-rim-match ICP residual for diagnostics.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Total Residual", "Tr",
            "Sum of per-rim residuals.", GH_ParamAccess.item);
        p.AddCurveParameter("Rim Polylines", "Rim",
            "Extracted naked-edge rim polylines per fragment (placed " +
            "frame). Diagnostic for tuning Sample Spacing / Break Angle.",
            GH_ParamAccess.list);
        p.AddTextParameter("Report", "Rp",
            "Human-readable assembly summary.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var fragments = new List<Mesh>();
        double jointWidth = 1.0;
        double sampleSpacing = 1.0;
        double breakAngleDeg = 8.0;
        double minSegmentLength = 1.0;
        int beamWidth = 32;
        int maxIterations = 2000;
        double minLoopLength = 10.0;
        int maxRounds = 6;
        double verifierPenetration = 0.5;
        int diffusionSteps = 20;
        bool usePortMode = false;
        bool run = false;
        bool useTorchSharp = false;
        double verifierAcceptThreshold = 0.5;
        bool nonCrossing = false;

        // Fragments may be optional in Mode=Port when Point Clouds is
        // wired -- defer the empty check to after the param sweep so we
        // can evaluate that branch first.
        bool fragmentsProvided = da.GetDataList(0, fragments);
        da.GetData(1, ref jointWidth);
        da.GetData(2, ref sampleSpacing);
        da.GetData(3, ref breakAngleDeg);
        da.GetData(4, ref minSegmentLength);
        da.GetData(5, ref beamWidth);
        da.GetData(6, ref maxIterations);
        da.GetData(7, ref minLoopLength);
        da.GetData(8, ref maxRounds);
        da.GetData(9, ref verifierPenetration);
        // Backward-compatible param read. Current input order:
        // 10=Diffusion Steps, 11=Use Port Mode, 12=Run,
        // 13=Use TorchSharp, 14=Point Clouds (tree),
        // 15=Verifier Accept Threshold (new).
        if (Params.Input.Count > 15) da.GetData(15, ref verifierAcceptThreshold);
        // Non-Crossing toggle appended at index 16 (geometric path only).
        if (Params.Input.Count > 16) da.GetData(16, ref nonCrossing);
        if (Params.Input.Count > 13)
        {
            da.GetData(10, ref diffusionSteps);
            da.GetData(11, ref usePortMode);
            da.GetData(12, ref run);
            da.GetData(13, ref useTorchSharp);
        }
        else if (Params.Input.Count > 12)
        {
            da.GetData(10, ref diffusionSteps);
            da.GetData(11, ref usePortMode);
            da.GetData(12, ref run);
        }
        else if (Params.Input.Count > 11)
        {
            da.GetData(10, ref usePortMode);
            da.GetData(11, ref run);
        }
        else
        {
            da.GetData(10, ref run);
        }
        if (diffusionSteps < 1) diffusionSteps = 1;
        if (diffusionSteps > 100) diffusionSteps = 100;

        if (!run)
        {
            // Run toggled false MUST stop any in-flight Mode=Port
            // background task. Before 2026-05-24 this early-returned
            // without cancelling, so the (minutes-long, CPU-libtorch)
            // inference kept running on a background thread inside
            // Rhino.exe -- the only way to stop it was to kill Rhino.
            bool wasRunning = CancelPortTaskIfRunning();
            EmitEmpty(da, wasRunning
                ? "Run is false. In-flight Port inference cancelled "
                  + "(stops within ~one denoise step; watch Message for 'cancelled')."
                : "Run is false. Toggle to execute.");
            return;
        }

        // For Mode=Port we ALSO accept a Point Clouds tree as the primary
        // input (Fragments optional). For the geometric path Fragments is
        // still required.
        // Either layout has a Point Clouds input PAST the Run input: 13
        // (no TorchSharp toggle) or 14 (with TorchSharp toggle).
        bool portCanRunFromPC = usePortMode && Params.Input.Count > 13;
        if (!portCanRunFromPC && fragments.Count < 2)
        {
            EmitEmpty(da, "Kintsugi needs at least 2 fragments to assemble. " +
                          "(In Mode=Port you can also supply a Point Clouds tree " +
                          "to bypass the Fragments input entirely.)");
            return;
        }
        if (portCanRunFromPC && fragments.Count < 2)
        {
            // Sanity: don't fail the geometric path purely on a missing input
            // when port mode is going to take over.
        }

        if (usePortMode)
        {
            string deployDir = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            // TorchSharp/libtorch native loading: when hosted in Rhino the
            // process base directory is Rhino's install dir, NOT this .gha's
            // deploy folder, so the OS loader can't resolve the libtorch DLL
            // chain (LibTorchSharp -> torch_cuda -> cudnn/cublas/...). Add the
            // deploy folder to the native search path so the whole chain
            // resolves. Process-wide + idempotent; only set on the TorchSharp
            // path to avoid touching Rhino's default search otherwise.
            if (useTorchSharp && deployDir != null)
            {
                try { SetDllDirectory(deployDir); } catch { }
            }
            string weightPath = deployDir == null ? null
                : System.IO.Path.Combine(deployDir, "kintsugi.bin");
            if (weightPath == null || !System.IO.File.Exists(weightPath))
            {
                EmitEmpty(da,
                    "Port mode requires the converted PuzzleFusion++ weights at " +
                    $"'{weightPath ?? "<deploy>/kintsugi.bin"}'. " +
                    "Run Frahan.Kintsugi.Port/Weights/convert_pytorch_checkpoint.py " +
                    "against the upstream PyTorch checkpoint to produce kintsugi.bin, " +
                    "then drop it next to Frahan.StonePack.gha. " +
                    "Use Port=False for the geometric path until weights are validated.");
                return;
            }
            // ---- ASYNC Mode=Port ----
            //
            // The inference is heavy (minutes). To keep the GH canvas
            // navigable we run the compute on a background Task and
            // re-solve the document when the result is ready. While the
            // task runs the component's Message property updates with
            // step-by-step progress. The user can pan/zoom the canvas
            // and inspect other components meanwhile.
            //
            // Triggering a new solve while a task is running cancels the
            // in-flight task and starts fresh (e.g. toggling Run=False
            // then True; or changing inputs).
            try
            {
                int Nsamp = 1000;
                // PC-first fragment count derivation: when Point Clouds is
                // wired with valid branches, use that to determine F. This
                // lets the user run Mode=Port with PC alone -- no Fragments
                // input required.
                Grasshopper.Kernel.Data.GH_Structure<Grasshopper.Kernel.Types.GH_Point> pcSnapshot = null;
                int pcIdx = (Params.Input.Count > 14) ? 14 : (Params.Input.Count > 13 ? 13 : -1);
                if (pcIdx >= 0)
                {
                    try { da.GetDataTree(pcIdx, out pcSnapshot); } catch { pcSnapshot = null; }
                }
                int F;
                if (fragments.Count >= 2)
                {
                    F = fragments.Count;
                }
                else if (pcSnapshot != null && pcSnapshot.Branches.Count >= 2)
                {
                    F = pcSnapshot.Branches.Count;
                    // Synthesize placeholder meshes from each PC branch so the
                    // output path has SOMETHING to apply transforms to.
                    fragments.Clear();
                    for (int f = 0; f < F; f++)
                    {
                        var br = pcSnapshot.Branches[f];
                        var pts = new List<Point3d>(br.Count);
                        foreach (var gp in br) pts.Add(gp.Value);
                        // Coarse bbox-pull mesh = same as BB Loader Style 0;
                        // valid enough for display. User can swap to their own
                        // visualisation mesh downstream.
                        var bbox = new BoundingBox(pts);
                        var pm = Mesh.CreateFromBox(bbox, 1, 1, 1);
                        if (pm != null) pm.Normals.ComputeNormals();
                        fragments.Add(pm);
                    }
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Port mode: deriving {F} fragments from Point Clouds " +
                        "(Fragments input not wired; using bbox placeholders for output meshes).");
                }
                else
                {
                    EmitEmpty(da, "Mode=Port needs either >=2 Fragments or a Point Clouds tree " +
                                   "with >=2 branches.");
                    return;
                }

                // -------- Check the task state machine --------
                lock (_portTaskLock)
                {
                    if (_portTask != null && !_portTask.IsCompleted)
                    {
                        // A compute is already in flight. Echo progress
                        // and emit empty outputs until it completes.
                        Message = _portProgress;
                        EmitEmpty(da, "Port mode computing... " + _portProgress +
                                       "  (Canvas remains navigable. To cancel: set Run=False.)");
                        return;
                    }
                    if (_portResult != null)
                    {
                        // Result arrived from a prior solve. Consume + emit.
                        EmitPortResult(da, _portResult, fragments, (float)verifierAcceptThreshold);
                        Message = "Port done";
                        _portResult = null;
                        return;
                    }
                }

                // -------- Snapshot inputs on the UI thread --------
                var reader = new Frahan.Kintsugi.Port.Weights.WeightReader(weightPath);

                // Per-fragment point cloud preparation. Default: sample
                // N=1000 points from the Fragments mesh surfaces.
                // Override: if the optional "Point Clouds" input (index 13)
                // is a tree whose branches match the fragment count, use
                // those points directly. Skips sampling noise and gives
                // the encoder authoritative cloud features.
                var clouds = new float[F][];
                // Per-fragment normalisation params captured during
                // NormaliseInPlace -- used downstream to undo the
                // normalisation when mapping the network's normalised-space
                // poses back into the anchor's world frame. Without these,
                // network output (which lives in [-1,1] per-fragment frames)
                // would be applied directly to document-coords meshes,
                // producing fragments rotated around the world origin with
                // tiny translations: exactly the misalignment that caused
                // the 2026-05-24 HitL fail.
                var norms = new NormParams[F];
                bool usedOverride = false;
                int pcIdxA = (Params.Input.Count > 14) ? 14 : (Params.Input.Count > 13 ? 13 : -1);
                if (pcIdxA >= 0)
                {
                    Grasshopper.Kernel.Data.GH_Structure<Grasshopper.Kernel.Types.GH_Point> pcTree = null;
                    try { da.GetDataTree(pcIdxA, out pcTree); } catch { pcTree = null; }
                    if (pcTree != null && pcTree.Branches.Count >= F)
                    {
                        usedOverride = true;
                        for (int f = 0; f < F; f++)
                        {
                            var br = pcTree.Branches[f];
                            int Nf = br?.Count ?? 0;
                            if (Nf < 100)
                            {
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                                    $"Point Clouds branch {f} only has {Nf} points; " +
                                    "need >=100. Falling back to mesh sampling.");
                                usedOverride = false;
                                break;
                            }
                            var cloud = new float[Nsamp * 3];
                            for (int i = 0; i < Nsamp; i++)
                            {
                                int srcIdx = (Nf >= Nsamp) ? i : (i % Nf);
                                var p = br[srcIdx].Value;
                                cloud[i * 3 + 0] = (float)p.X;
                                cloud[i * 3 + 1] = (float)p.Y;
                                cloud[i * 3 + 2] = (float)p.Z;
                            }
                            norms[f] = NormaliseInPlace(cloud);
                            clouds[f] = cloud;
                        }
                    }
                }
                if (!usedOverride)
                {
                    for (int f = 0; f < F; f++)
                    {
                        var cloud = SamplePointsOnMeshUniform(fragments[f], Nsamp, seed: f);
                        norms[f] = NormaliseInPlace(cloud);
                        clouds[f] = cloud;
                    }
                }

                var fragmentsCopy = new List<Mesh>(F);
                foreach (var m in fragments) fragmentsCopy.Add(m.DuplicateMesh());

                // Capture the doc on the UI thread; safe to use from Task continuation.
                var doc = OnPingDocument();
                var instanceGuid = this.InstanceGuid;
                var headerReport = new StringBuilder();
                headerReport.AppendLine("Port mode: end-to-end inference.");
                // NOTE: this is the REQUESTED path from the toggle. The
                // ACTUAL path (after libtorch init) is reported by
                // EmitPortResult from the result -- it may differ if
                // TorchSharp failed to initialise and fell back to manual.
                headerReport.AppendLine($"  Denoiser path requested: {(useTorchSharp ? "TorchSharp / libtorch (paper-exact)" : "manual C# port (~3-5% drift)")}.");
                headerReport.AppendLine(usedOverride
                    ? $"  Input: Point Clouds tree (override, {F} branches; sampling bypassed)."
                    : $"  Input: Fragments mesh sampling ({F} meshes x {Nsamp} pts).");
                headerReport.AppendLine($"  kintsugi.bin: {new System.IO.FileInfo(weightPath).Length:N0} bytes loaded.");
                headerReport.AppendLine($"  {F} fragments, {diffusionSteps} diffusion steps.");
                // Surface the selected GPU device. If the user is on a
                // multi-GPU laptop, this confirms we're on the discrete
                // NVIDIA card (e.g. Quadro RTX 4000) and not an integrated
                // chip or the CPU emitter.
                if (Frahan.Kintsugi.Port.Primitives.GpuMatmul.IsAvailable)
                {
                    headerReport.AppendLine(
                        $"  GPU: {Frahan.Kintsugi.Port.Primitives.GpuMatmul.SelectedDeviceType} " +
                        $"'{Frahan.Kintsugi.Port.Primitives.GpuMatmul.SelectedDeviceName}'");
                }
                else
                {
                    headerReport.AppendLine(
                        $"  GPU: NOT AVAILABLE ({Frahan.Kintsugi.Port.Primitives.GpuMatmul.Diagnostic}) -- " +
                        "compute is on CPU. Performance will be very slow.");
                }

                // -------- Kick off background Task --------
                _portCancel?.Cancel();
                _portCancel = new System.Threading.CancellationTokenSource();
                var token = _portCancel.Token;
                _portProgress = "starting...";
                Message = _portProgress;
                int steps = diffusionSteps;
                bool useTorchSharpCapture = useTorchSharp;
                _portTask = System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var inference = new Frahan.Kintsugi.Port.Outer.KintsugiPortInference(
                            reader, numInferenceSteps: steps, useTorchSharpDenoiser: useTorchSharpCapture);
                        var asm = inference.RunAssembly(clouds, Nsamp,
                            anchorIndex: 0, seed: 42,
                            progress: (cur, total, label) =>
                            {
                                _portProgress = label;
                                // Cheap UI refresh: just update Message on the
                                // component without forcing a full solve.
                                try { doc?.ScheduleSolution(50, d =>
                                {
                                    var obj = d?.FindComponent(instanceGuid) as GH_Component;
                                    if (obj != null) { obj.Message = label; }
                                }); } catch { }
                            },
                            cancel: token);
                        _portResult = new PortResult
                        {
                            Asm = asm,
                            HeaderReport = headerReport.ToString(),
                            FragmentsSnapshot = fragmentsCopy,
                            NormParamsSnapshot = norms,
                        };
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancelled (Run=false or a fresh run superseded this
                        // one). Leave NO result so the next Run=True starts
                        // clean instead of replaying a stale "cancelled" payload.
                        _portResult = null;
                        _portProgress = "";
                    }
                    catch (Exception ex)
                    {
                        _portResult = new PortResult { Error = $"{ex.GetType().Name}: {ex.Message}" };
                    }
                    finally
                    {
                        try { reader = null; } catch { }
                        // Schedule a re-solve so the result reaches the canvas.
                        try { doc?.ScheduleSolution(10, d =>
                        {
                            var obj = d?.FindComponent(instanceGuid) as GH_Component;
                            if (obj != null) { obj.ExpireSolution(true); }
                        }); } catch { }
                    }
                }, token);

                EmitEmpty(da, "Port mode: started. Canvas is navigable. " +
                               "Watch the component Message for progress; results will pop in automatically.");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Port mode running in background (canvas navigable). " +
                    "Set Run=False to cancel.");
                return;
            }
            catch (Exception ex)
            {
                EmitEmpty(da, $"Port mode failed: {ex.GetType().Name}: {ex.Message}");
                return;
            }
        }

        // 1. Extract naked-edge rim polylines per fragment.
        var rimLoopsPerFragment = new List<List<PolylineCurve>>();
        var diagnosticRims = new List<Curve>();
        int rimCount = 0;
        for (int f = 0; f < fragments.Count; f++)
        {
            var loops = ExtractNakedRimLoops(fragments[f], minLoopLength);
            rimLoopsPerFragment.Add(loops);
            rimCount += loops.Count;
            diagnosticRims.AddRange(loops.Select(p => (Curve)p.DuplicateCurve()));
        }
        if (rimCount == 0)
        {
            EmitEmpty(da, "No naked-edge rims found on any fragment. " +
                          "Are your meshes closed? Open boundaries needed for rim-matching.");
            return;
        }

        // 1b. Pre-compute segment counts per rim for diagnostic. Tells
        // the user whether BoundarySegmenter3D found enough curvature
        // peaks to lock onto. If every rim shows 1 segment, the rim is
        // too smooth/symmetric and no match is possible regardless of
        // solver settings -- add asymmetric features to the rim
        // (notches, corners) and retry.
        var segmentCountPerFragment = new int[fragments.Count];
        var segOptDiag = new SegmenterOptions
        {
            SampleSpacing = sampleSpacing,
            BreakAngleDeg = breakAngleDeg,
            MinSegmentLength = minSegmentLength,
        };
        var segOpt3DDiag = new SegmenterOptions3D
        {
            SampleSpacing = sampleSpacing,
            BreakAngleDeg = breakAngleDeg,
            MinSegmentLength = minSegmentLength,
        };
        for (int f = 0; f < fragments.Count; f++)
        {
            int total = 0;
            foreach (var loop in rimLoopsPerFragment[f])
            {
                var probe = new Panel($"diag_f{f:D3}", loop, PanelKind.Shard);
                var segs = probe.Mode == PanelMode.Spatial3D
                    ? BoundarySegmenter3D.Segment(probe, segOpt3DDiag)
                    : BoundarySegmenter.Segment(probe, segOptDiag);
                total += segs.Count;
            }
            segmentCountPerFragment[f] = total;
        }

        // 2. Auto-agglomerative outer loop. Mirrors PuzzleFusion++ Sec 3
        // outer schedule: anchor cluster grows each round; unplaced
        // fragments retry against the larger anchor. Pure geometric --
        // no learned encoder, no diffusion, no learned verifier.
        var fragmentTransform = new Transform[fragments.Count];
        var placedFragmentMask = new bool[fragments.Count];
        var rejectedByVerifier = new List<int>();
        fragmentTransform[0] = Transform.Identity;
        placedFragmentMask[0] = true;

        var segOpt = new SegmenterOptions
        {
            SampleSpacing = sampleSpacing,
            BreakAngleDeg = breakAngleDeg,
            MinSegmentLength = minSegmentLength,
        };
        var segOpt3D = new SegmenterOptions3D
        {
            SampleSpacing = sampleSpacing,
            BreakAngleDeg = breakAngleDeg,
            MinSegmentLength = minSegmentLength,
        };
        var asmOpt = new AssemblyOptions
        {
            BeamWidth = Math.Max(1, beamWidth),
            MaxIterations = Math.Max(1, maxIterations),
            ResidualThreshold = jointWidth,
            NonCrossingCorrespondence = nonCrossing,
        };

        var allResiduals = new List<double>();
        double totalResidual = 0.0;
        int placedPanelsTotal = 0;
        int totalRounds = 0;

        for (int round = 0; round < Math.Max(1, maxRounds); round++)
        {
            totalRounds = round + 1;

            // Build panels for THIS round. Anchor-cluster fragments
            // contribute Frame panels (already placed; locked). All others
            // contribute Shard panels.
            var roundPanels = new List<Panel>();
            var roundPanelToFragment = new Dictionary<string, int>();
            for (int f = 0; f < fragments.Count; f++)
            {
                if (rimLoopsPerFragment[f].Count == 0) continue;
                for (int l = 0; l < rimLoopsPerFragment[f].Count; l++)
                {
                    var id = $"r{round:D2}_f{f:D3}_l{l:D2}";
                    // Apply the current accepted transform so the panel's
                    // contour matches the anchor cluster's frame.
                    var contour = (PolylineCurve)rimLoopsPerFragment[f][l].DuplicateCurve();
                    if (placedFragmentMask[f]) contour.Transform(fragmentTransform[f]);
                    var kind = placedFragmentMask[f] ? PanelKind.Frame : PanelKind.Shard;
                    var panel = new Panel(id, contour, kind);
                    if (placedFragmentMask[f]) panel.IsAnchored = true;
                    roundPanels.Add(panel);
                    roundPanelToFragment[id] = f;
                }
            }
            var roundFrames = roundPanels.Where(p => p.IsAnchored).ToList();
            var roundShards = roundPanels.Where(p => !p.IsAnchored).ToList();
            if (roundShards.Count == 0) break; // everything placed

            // Re-seed the SegmentHashIndex per round so it reflects the
            // current panel positions (anchor panels have moved with the
            // anchor cluster).
            var index = new SegmentHashIndex();
            foreach (var p in roundPanels) AddSegmentsFor(p, segOpt, segOpt3D, index);

            var solver = new AssemblySolver(index, asmOpt, segOpt, segOpt3D);
            var state = solver.Solve(roundFrames, roundShards);

            int progressedThisRound = 0;
            foreach (var panel in state.PlacedPanels)
            {
                if (!roundPanelToFragment.TryGetValue(panel.Id, out int fIdx)) continue;
                if (placedFragmentMask[fIdx]) continue; // anchor

                // Geometric verifier: reject if the candidate placement
                // penetrates any already-placed fragment beyond tolerance.
                if (verifierPenetration > 0)
                {
                    var candidateMesh = fragments[fIdx]?.DuplicateMesh();
                    if (candidateMesh != null)
                    {
                        candidateMesh.Transform(panel.AppliedTransform);
                        if (PenetratesAnyPlaced(candidateMesh, fragments, fragmentTransform,
                                                placedFragmentMask, verifierPenetration))
                        {
                            rejectedByVerifier.Add(fIdx);
                            continue;
                        }
                    }
                }

                fragmentTransform[fIdx] = panel.AppliedTransform;
                placedFragmentMask[fIdx] = true;
                progressedThisRound++;
            }
            foreach (var h in state.History) allResiduals.Add(h.Residual);
            totalResidual += state.TotalResidual;
            placedPanelsTotal += state.PlacedPanels.Count;

            if (progressedThisRound == 0) break; // no progress; stop.
        }

        var assembled = new List<Mesh>(fragments.Count);
        var transforms = new List<Transform>(fragments.Count);
        var placedIdx = new List<int>();
        var unplacedIdx = new List<int>();
        for (int f = 0; f < fragments.Count; f++)
        {
            var m = fragments[f]?.DuplicateMesh();
            if (m != null && placedFragmentMask[f])
            {
                m.Transform(fragmentTransform[f]);
                placedIdx.Add(f);
            }
            else
            {
                unplacedIdx.Add(f);
            }
            assembled.Add(m);
            transforms.Add(placedFragmentMask[f] ? fragmentTransform[f] : Transform.Identity);
        }

        var report = new StringBuilder();
        report.AppendLine($"Kintsugi: {placedIdx.Count}/{fragments.Count} fragments placed");
        report.AppendLine($"Rounds      : {totalRounds} of {maxRounds} max");
        report.AppendLine($"Rims        : {rimCount} across all fragments");
        report.AppendLine($"Segments    : [{string.Join(",", segmentCountPerFragment)}] per fragment");
        if (segmentCountPerFragment.All(s => s <= 1))
        {
            report.AppendLine($"WARNING     : every rim has <= 1 segment.");
            report.AppendLine($"              Rims are too smooth/symmetric for edge matching.");
            report.AppendLine($"              Lower Break Angle (Ba) below {breakAngleDeg:F1}, or");
            report.AppendLine($"              add asymmetric features (notches, corners) to the rims.");
        }
        report.AppendLine($"Solver      : beam {beamWidth}, maxIter {maxIterations}, jointW {jointWidth:F3}");
        if (verifierPenetration > 0)
            report.AppendLine($"Verifier    : penetration tol {verifierPenetration:F3} ({rejectedByVerifier.Count} rejections)");
        else
            report.AppendLine($"Verifier    : OFF (Vp = 0)");
        report.AppendLine($"Total resid : {totalResidual:F4}");
        if (unplacedIdx.Count > 0)
        {
            report.AppendLine($"Unplaced    : {string.Join(", ", unplacedIdx)} -- no rim match found");
        }

        da.SetDataList(0, assembled);
        da.SetDataList(1, transforms);
        da.SetDataList(2, placedIdx);
        da.SetDataList(3, unplacedIdx);
        da.SetDataList(4, allResiduals);
        da.SetData(5, totalResidual);
        da.SetDataList(6, diagnosticRims);
        da.SetData(7, report.ToString());
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometric verifier. Clean-room substitute for PuzzleFusion++'s
    /// learned binary classifier. Rejects a candidate fragment placement
    /// if its transformed mesh has any vertex penetrating an already-
    /// placed fragment by more than <paramref name="tol"/>.
    /// </summary>
    private static bool PenetratesAnyPlaced(
        Mesh candidate, List<Mesh> sourceFragments,
        Transform[] placedTransforms, bool[] placedMask, double tol)
    {
        if (candidate == null || candidate.Vertices.Count == 0) return false;
        var candidatePoints = new List<Point3d>(candidate.Vertices.Count);
        for (int i = 0; i < candidate.Vertices.Count; i++)
            candidatePoints.Add(candidate.Vertices[i]);

        for (int f = 0; f < sourceFragments.Count; f++)
        {
            if (!placedMask[f]) continue;
            var other = sourceFragments[f]?.DuplicateMesh();
            if (other == null) continue;
            other.Transform(placedTransforms[f]);
            // Bbox prefilter.
            var bbA = candidate.GetBoundingBox(false);
            var bbB = other.GetBoundingBox(false);
            if (!bbA.IsValid || !bbB.IsValid) continue;
            bbA.Inflate(-tol, -tol, -tol); // shrink so touching surfaces don't trigger
            if (bbA.Max.X < bbB.Min.X || bbA.Min.X > bbB.Max.X) continue;
            if (bbA.Max.Y < bbB.Min.Y || bbA.Min.Y > bbB.Max.Y) continue;
            if (bbA.Max.Z < bbB.Min.Z || bbA.Min.Z > bbB.Max.Z) continue;

            // Sample a subset of candidate vertices for speed; if any
            // are deeply inside `other` by more than `tol`, the placement
            // penetrates.
            int step = Math.Max(1, candidatePoints.Count / 64); // ~64 samples
            for (int i = 0; i < candidatePoints.Count; i += step)
            {
                var p = candidatePoints[i];
                if (!other.GetBoundingBox(false).Contains(p)) continue;
                var closest = other.ClosestMeshPoint(p, tol * 4.0);
                if (closest == null) continue;
                var d = p.DistanceTo(closest.Point);
                // ClosestMeshPoint returns the surface point; if `p` is
                // INSIDE other, normal direction would invert. Cheap
                // proxy: vertex is "inside" if its closest-surface
                // distance is less than tol AND signed by mesh.IsPointInside.
                if (d < tol && other.IsPointInside(p, tol * 0.5, false))
                    return true;
            }
        }
        return false;
    }

    // -------------------------------------------------------------------------

    private static List<PolylineCurve> ExtractNakedRimLoops(Mesh mesh, double minLoopLength)
    {
        var result = new List<PolylineCurve>();
        if (mesh == null) return result;
        Polyline[] naked = null;
        try { naked = mesh.GetNakedEdges(); }
        catch { naked = null; }
        if (naked == null) return result;
        foreach (var loop in naked)
        {
            if (loop == null || loop.Count < 4) continue;
            if (!loop.IsClosed) continue;
            if (loop.Length < minLoopLength) continue;
            result.Add(loop.ToPolylineCurve());
        }
        return result;
    }

    private static void AddSegmentsFor(
        Panel panel, SegmenterOptions segOpt, SegmenterOptions3D segOpt3D,
        SegmentHashIndex index)
    {
        var segs = panel.Mode == PanelMode.Spatial3D
            ? BoundarySegmenter3D.Segment(panel, segOpt3D)
            : BoundarySegmenter.Segment(panel, segOpt);
        foreach (var s in segs) index.Add(s);
    }

    // Adds a directory to the native DLL search path (process-wide). Used
    // to point the OS loader at this .gha's deploy folder so libtorch's
    // native dependency chain resolves when hosted inside Rhino.
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string lpPathName);

    // -------- Async Mode=Port state --------
    private static readonly object _portTaskLock = new object();
    private static System.Threading.Tasks.Task _portTask;
    private static System.Threading.CancellationTokenSource _portCancel;
    private static volatile string _portProgress = "";
    private static PortResult _portResult;

    /// <summary>
    /// Cancel any in-flight Mode=Port background task. Called when Run
    /// toggles false. Cooperative cancel: KintsugiPortInference checks
    /// the token at each diffusion step and fragment encode, so the task
    /// stops within roughly one denoise step rather than instantly (a
    /// single CPU-libtorch denoise step can take several seconds). Clears
    /// stale result + progress so the next Run=True starts fresh instead
    /// of consuming a cancelled payload. Returns true if a task was
    /// actually running.
    /// </summary>
    private bool CancelPortTaskIfRunning()
    {
        lock (_portTaskLock)
        {
            bool running = _portTask != null && !_portTask.IsCompleted;
            if (running)
            {
                try { _portCancel?.Cancel(); } catch { }
                Message = "cancelling...";
            }
            // Drop any pending/in-flight result so a stale "cancelled"
            // payload can't block the next Run=True.
            _portResult = null;
            _portProgress = "";
            return running;
        }
    }

    private sealed class PortResult
    {
        public Frahan.Kintsugi.Port.Outer.KintsugiPortInference.AssemblyResult Asm;
        public List<Mesh> FragmentsSnapshot;
        // Per-fragment normalisation captured BEFORE encoding. Required to
        // un-normalise the network's poses back into the anchor's world
        // frame. Parallel to FragmentsSnapshot.
        public NormParams[] NormParamsSnapshot;
        public string HeaderReport;
        public string Error;
    }

    /// <summary>
    /// Per-fragment cloud normalisation captured during NormaliseInPlace.
    /// Centroid (Cx,Cy,Cz) and MaxAbs are the document-units offset + scale
    /// applied so the cloud landed in [-1,1] before the encoder saw it.
    /// </summary>
    private struct NormParams
    {
        public float Cx;
        public float Cy;
        public float Cz;
        public float MaxAbs;
    }

    private void EmitPortResult(IGH_DataAccess da, PortResult result, List<Mesh> currentFragments,
                                float verifierAcceptThreshold)
    {
        if (result.Error != null)
        {
            EmitEmpty(da, "Port mode error: " + result.Error);
            return;
        }
        int F = result.Asm.Poses.Count;
        // Convert each [trans(3) | quat(4)] pose to a Rhino Transform.
        //
        // The network operates in PER-FRAGMENT NORMALISED SPACE: each cloud
        // was centred at its own centroid and scaled to max-abs=1 before
        // encoding. The network's pose is therefore a normalised-space
        // SE(3). Applying it directly to a document-coords mesh rotates the
        // mesh around the world origin and translates by sub-unit
        // distances -- producing the 2026-05-24 misalignment HitL fail.
        //
        // Correct composition (anchor is fragment 0, whose pose is reset to
        // identity each diffusion step):
        //
        //     T_world(f) = T_unnorm(anchor) * T_network(f) * T_norm(f)
        //
        // where
        //   T_norm(f)        = scale(1/MaxAbs_f) * translate(-Centroid_f)
        //   T_unnorm(anchor) = translate(+Centroid_0) * scale(MaxAbs_0)
        //
        // For f=0: T_network is identity and T_unnorm * T_norm collapses to
        // identity -- the anchor stays at its original document position.
        // For f>0: the fragment is brought to its own normalised frame,
        // placed by the network, then lifted into the anchor's world frame.
        var np = result.NormParamsSnapshot;
        bool haveNorm = np != null && np.Length >= F;
        var anchor = haveNorm ? np[0] : default;
        Transform tUnnormAnchor = haveNorm
            ? Transform.Multiply(
                Transform.Translation(anchor.Cx, anchor.Cy, anchor.Cz),
                Transform.Scale(Point3d.Origin, anchor.MaxAbs))
            : Transform.Identity;

        // Per-fragment verifier confidence: max pair-score over all pairs
        // containing this fragment. Used to gate whether to trust the
        // denoiser's predicted pose. Below the threshold the fragment is
        // KEPT AT ITS INPUT WORLD POSITION (identity transform) and listed
        // as Unplaced -- matches PuzzleFusion++ Sec 3's auto-agglomerative
        // schedule where weak-pair fragments are not merged into the
        // anchor cluster.
        //
        // 2026-05-24: added after the 5-frag HitL fail. The 5-frag BB
        // sample produced only 1/10 strong pairs (top (3,4)=0.549, others
        // <0.5). Without gating, every weak-prediction fragment was
        // composed as T_unnorm(anchor) * Identity * T_norm(f), collapsing
        // them all onto the anchor's centroid -- the "blob" the user saw.
        //
        // Threshold is the "Verifier Accept Threshold" (Vt) input; default
        // 0.5 matches the "STRONG" tag in the per-pair score report. Lower
        // it to accept the network's near-miss pairs on hard samples.
        float VerifierAcceptThreshold = verifierAcceptThreshold > 0 ? verifierAcceptThreshold : 0.5f;
        var confidence = new float[F];
        if (result.Asm.VerifierScores != null && result.Asm.VerifierScores.Count > 0)
        {
            int e = 0;
            for (int i = 0; i < F; i++)
            {
                for (int j = i + 1; j < F; j++)
                {
                    if (e >= result.Asm.VerifierScores.Count) break;
                    float s = result.Asm.VerifierScores[e++];
                    if (s > confidence[i]) confidence[i] = s;
                    if (s > confidence[j]) confidence[j] = s;
                }
            }
        }

        var portTransforms = new List<Transform>(F);
        var portPlacedIdx = new List<int>();
        var portUnplacedIdx = new List<int>();
        for (int f = 0; f < F; f++)
        {
            // Anchor (f=0): always at identity per orchestrator.
            // Others: verifier-gated. Weak fragments stay at INPUT world
            // position (identity) and join Unplaced; for BB GT-aligned data
            // this preserves their canonical positions.
            bool isAnchor = (f == 0);
            bool accept = isAnchor || confidence[f] >= VerifierAcceptThreshold;

            if (!accept)
            {
                portTransforms.Add(Transform.Identity);
                portUnplacedIdx.Add(f);
                continue;
            }

            var p = result.Asm.Poses[f];
            var trans = new float[] { p[0], p[1], p[2] };
            var quat  = new float[] { p[3], p[4], p[5], p[6] };
            var tNetwork = QuaternionTransToTransform(quat, trans);
            if (haveNorm && !isAnchor)
            {
                var nf = np[f];
                Transform tNormF = Transform.Multiply(
                    Transform.Scale(Point3d.Origin, 1.0 / nf.MaxAbs),
                    Transform.Translation(-nf.Cx, -nf.Cy, -nf.Cz));
                portTransforms.Add(Transform.Multiply(tUnnormAnchor,
                                                       Transform.Multiply(tNetwork, tNormF)));
            }
            else
            {
                // Anchor case: tNetwork is identity (forced by orchestrator),
                // mesh stays at its input world position.
                portTransforms.Add(isAnchor ? Transform.Identity : tNetwork);
            }
            portPlacedIdx.Add(f);
        }
        // Apply transforms to fragment copies. Use the snapshot we
        // captured before kicking off the task -- guarantees the
        // result matches the inputs the task saw.
        var source = result.FragmentsSnapshot ?? currentFragments;
        var portAssembled = new List<Mesh>(F);
        for (int f = 0; f < F && f < source.Count; f++)
        {
            var m = source[f].DuplicateMesh();
            m.Transform(portTransforms[f]);
            portAssembled.Add(m);
        }
        int strongPairs = 0;
        if (result.Asm.VerifierScores != null)
            foreach (var s in result.Asm.VerifierScores) if (s > 0.5f) strongPairs++;

        var rep = new StringBuilder();
        if (result.HeaderReport != null) rep.Append(result.HeaderReport);
        // ACTUAL denoiser path (truth, not the toggle). Surfaces a silent
        // TorchSharp->manual fallback + the init error that caused it.
        if (result.Asm.UsedTorchSharp)
        {
            rep.AppendLine($"  Denoiser path ACTUAL: TorchSharp / libtorch on {result.Asm.DenoiserDevice?.ToUpperInvariant() ?? "CPU"} (paper-exact).");
            if (!string.IsNullOrEmpty(result.Asm.DeviceFallbackReason))
                rep.AppendLine($"  (CUDA available but self-test failed -> ran on CPU: {result.Asm.DeviceFallbackReason})");
        }
        else
        {
            rep.AppendLine("  Denoiser path ACTUAL: manual C# port (~3-5% drift).");
            if (!string.IsNullOrEmpty(result.Asm.TorchSharpInitError))
                rep.AppendLine($"  *** TorchSharp was requested but FELL BACK: {result.Asm.TorchSharpInitError}");
        }
        rep.AppendLine($"  {result.Asm.Report}");
        rep.AppendLine($"  Verifier: {result.Asm.VerifierScores?.Count ?? 0} pairs scored, " +
                       $"{strongPairs} with score > 0.5.");
        // Score distribution + top 6 pairs by score so the user can see
        // exactly what the verifier is producing (esp. helpful when 0
        // pairs cross threshold).
        if (result.Asm.VerifierScores != null && result.Asm.VerifierScores.Count > 0)
        {
            var scores = result.Asm.VerifierScores;
            float min = scores[0], max = scores[0], sum = 0;
            foreach (var s in scores) { if (s < min) min = s; if (s > max) max = s; sum += s; }
            rep.AppendLine($"  Score distribution: min={min:F3} max={max:F3} mean={sum / scores.Count:F3}");
            // Top 6 (or fewer) sorted desc.
            var indexed = new List<(int idx, float sc)>();
            for (int i = 0; i < scores.Count; i++) indexed.Add((i, scores[i]));
            indexed.Sort((a, b) => b.sc.CompareTo(a.sc));
            int top = Math.Min(6, indexed.Count);
            rep.AppendLine($"  Top {top} pair scores (descending):");
            // Map flat edge index -> (i, j) using the same i<j enumeration as the orchestrator.
            int fragCount = result.Asm.Poses.Count;
            var pairLookup = new List<(int i, int j)>(scores.Count);
            for (int i = 0; i < fragCount; i++) for (int j = i + 1; j < fragCount; j++) pairLookup.Add((i, j));
            for (int k = 0; k < top; k++)
            {
                var (idx, sc) = indexed[k];
                var (i, j) = (idx < pairLookup.Count) ? pairLookup[idx] : (-1, -1);
                rep.AppendLine($"    ({i},{j}) score={sc:F4}{(sc > 0.5f ? "  STRONG" : "")}");
            }
        }

        rep.AppendLine($"  Verifier gate: threshold={VerifierAcceptThreshold:F2}, " +
                       $"{portPlacedIdx.Count} placed, {portUnplacedIdx.Count} unplaced " +
                       "(unplaced fragments kept at INPUT world position).");

        da.SetDataList(0, portAssembled);
        da.SetDataList(1, portTransforms);
        da.SetDataList(2, portPlacedIdx);
        da.SetDataList(3, portUnplacedIdx);
        var resList = new List<double>();
        if (result.Asm.VerifierScores != null)
            foreach (var s in result.Asm.VerifierScores) resList.Add(s);
        da.SetDataList(4, resList);
        da.SetData(5, 0.0);
        da.SetDataList(6, new List<Curve>());
        da.SetData(7, rep.ToString());
        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
            $"Port mode done: {F} fragments, {result.Asm.InferenceSteps} steps, " +
            $"{strongPairs} pairs > 0.5.");
    }

    private void EmitEmpty(IGH_DataAccess da, string report)
    {
        int outCount = Params.Output.Count;
        if (outCount > 0) da.SetDataList(0, new List<Mesh>());
        if (outCount > 1) da.SetDataList(1, new List<Transform>());
        if (outCount > 2) da.SetDataList(2, new List<int>());
        if (outCount > 3) da.SetDataList(3, new List<int>());
        if (outCount > 4) da.SetDataList(4, new List<double>());
        if (outCount > 5) da.SetData(5, 0.0);
        if (outCount > 6) da.SetDataList(6, new List<Curve>());
        if (outCount > 7) da.SetData(7, report);
    }

    /// <summary>
    /// Uniform surface sampler. Sums triangle areas, draws K uniform random
    /// samples weighted by area, samples barycentric (u,v,1-u-v) via the
    /// sqrt(r1) warp. Output is [N, 3] channels-LAST row-major (the layout
    /// Frahan.Kintsugi.Port.Outer.EncoderWeightLoader.RunEncoder expects).
    /// Deterministic per `seed`.
    /// </summary>
    private static float[] SamplePointsOnMeshUniform(Mesh m, int K, int seed)
    {
        if (m == null) throw new ArgumentNullException(nameof(m));
        var faces = m.Faces;
        var verts = m.Vertices;
        if (faces.Count == 0) throw new ArgumentException("Mesh has no faces.");
        // Triangulate quads on the fly by computing cumulative-area weights
        // per triangle.
        var triIndices = new List<(int a, int b, int c)>(faces.Count * 2);
        var cumArea = new List<double>(faces.Count * 2);
        double total = 0;
        for (int f = 0; f < faces.Count; f++)
        {
            var face = faces[f];
            int[] tris;
            if (face.IsQuad) tris = new[] { face.A, face.B, face.C, face.A, face.C, face.D };
            else tris = new[] { face.A, face.B, face.C };
            for (int t = 0; t < tris.Length; t += 3)
            {
                var va = verts[tris[t]];     var vb = verts[tris[t + 1]]; var vc = verts[tris[t + 2]];
                var ab = new Point3d(vb.X - va.X, vb.Y - va.Y, vb.Z - va.Z);
                var ac = new Point3d(vc.X - va.X, vc.Y - va.Y, vc.Z - va.Z);
                var cross = new Point3d(
                    ab.Y * ac.Z - ab.Z * ac.Y,
                    ab.Z * ac.X - ab.X * ac.Z,
                    ab.X * ac.Y - ab.Y * ac.X);
                double area = 0.5 * Math.Sqrt(cross.X * cross.X + cross.Y * cross.Y + cross.Z * cross.Z);
                total += area;
                triIndices.Add((tris[t], tris[t + 1], tris[t + 2]));
                cumArea.Add(total);
            }
        }
        if (total <= 0) throw new InvalidOperationException("Mesh total area is zero.");
        var rng = new Random(seed);
        var output = new float[K * 3];
        for (int k = 0; k < K; k++)
        {
            double t = rng.NextDouble() * total;
            int lo = 0, hi = cumArea.Count - 1;
            while (lo < hi) { int mid = (lo + hi) / 2; if (cumArea[mid] < t) lo = mid + 1; else hi = mid; }
            var (ia, ib, ic) = triIndices[lo];
            var va = verts[ia]; var vb = verts[ib]; var vc = verts[ic];
            double r1 = rng.NextDouble(); double r2 = rng.NextDouble();
            double s = Math.Sqrt(r1);
            double w0 = 1.0 - s;
            double w1 = s * (1.0 - r2);
            double w2 = s * r2;
            output[k * 3 + 0] = (float)(w0 * va.X + w1 * vb.X + w2 * vc.X);
            output[k * 3 + 1] = (float)(w0 * va.Y + w1 * vb.Y + w2 * vc.Y);
            output[k * 3 + 2] = (float)(w0 * va.Z + w1 * vb.Z + w2 * vc.Z);
        }
        return output;
    }

    /// <summary>
    /// Convert a unit quaternion (w, x, y, z) + translation (tx, ty, tz)
    /// into a Rhino SE(3) Transform. PuzzleFusion++ uses
    /// (w, x, y, z) ordering per pytorch3d convention.
    /// </summary>
    private static Transform QuaternionTransToTransform(float[] quat, float[] trans)
    {
        float w = quat[0], x = quat[1], y = quat[2], z = quat[3];
        // Normalise once more defensively.
        float n = (float)Math.Sqrt(w * w + x * x + y * y + z * z);
        if (n > 1e-8f) { w /= n; x /= n; y /= n; z /= n; }
        // Standard quat -> 3x3 rotation matrix.
        double r00 = 1 - 2 * (y * y + z * z);
        double r01 = 2 * (x * y - z * w);
        double r02 = 2 * (x * z + y * w);
        double r10 = 2 * (x * y + z * w);
        double r11 = 1 - 2 * (x * x + z * z);
        double r12 = 2 * (y * z - x * w);
        double r20 = 2 * (x * z - y * w);
        double r21 = 2 * (y * z + x * w);
        double r22 = 1 - 2 * (x * x + y * y);
        var T = Transform.Identity;
        T.M00 = r00; T.M01 = r01; T.M02 = r02; T.M03 = trans[0];
        T.M10 = r10; T.M11 = r11; T.M12 = r12; T.M13 = trans[1];
        T.M20 = r20; T.M21 = r21; T.M22 = r22; T.M23 = trans[2];
        T.M30 = 0;   T.M31 = 0;   T.M32 = 0;   T.M33 = 1;
        return T;
    }

    /// <summary>
    /// Centre at origin then scale so max|coord| = 1. Matches the
    /// PuzzleFusion++ per-fragment input normalisation. Returns the
    /// captured centroid + scale so the caller can later undo the
    /// normalisation when applying the network's normalised-space pose
    /// back into world coords.
    /// </summary>
    private static NormParams NormaliseInPlace(float[] cloud)
    {
        int N = cloud.Length / 3;
        double mx = 0, my = 0, mz = 0;
        for (int i = 0; i < N; i++) { mx += cloud[i * 3]; my += cloud[i * 3 + 1]; mz += cloud[i * 3 + 2]; }
        mx /= N; my /= N; mz /= N;
        float maxAbs = 0f;
        for (int i = 0; i < N; i++)
        {
            cloud[i * 3]     -= (float)mx;
            cloud[i * 3 + 1] -= (float)my;
            cloud[i * 3 + 2] -= (float)mz;
            float ax = Math.Abs(cloud[i * 3]);
            float ay = Math.Abs(cloud[i * 3 + 1]);
            float az = Math.Abs(cloud[i * 3 + 2]);
            if (ax > maxAbs) maxAbs = ax;
            if (ay > maxAbs) maxAbs = ay;
            if (az > maxAbs) maxAbs = az;
        }
        if (maxAbs > 1e-12f)
        {
            float inv = 1.0f / maxAbs;
            for (int i = 0; i < cloud.Length; i++) cloud[i] *= inv;
        }
        return new NormParams
        {
            Cx = (float)mx,
            Cy = (float)my,
            Cz = (float)mz,
            MaxAbs = maxAbs > 1e-12f ? maxAbs : 1f,
        };
    }
}
