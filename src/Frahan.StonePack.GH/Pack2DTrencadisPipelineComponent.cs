#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Frahan.GH.Attributes;
using Frahan.GH.TwoD;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using KangarooSolver;
using KangarooSolver.Goals;
using Rhino.Display;
using Rhino.Geometry;

namespace Frahan.GH;

/// <summary>
/// Trencadís pipeline (F-2D-002.F8b) — all-in-one variant kept alongside
/// the standard Dynamic Settle.
///
/// Pipeline:
///   1. Deterministic boundary packing (TrencadisFill, Mode 2).
///   2. Residual-overlap measurement on the packer's output.
///   3. If residual > 0 AND Apply Physics is true, light Kangaroo 2
///      settle. Standard Step loop with plateau detection; on plateau
///      auto-fall-back to MomentumStep for the remainder. Exposes
///      Convergence Threshold and Momentum controls so the user can
///      override the default solver behaviour where the deterministic
///      pass leaves residue.
///
/// Notes on Kangaroo:
///   • PhysicalSystem.Step(goals, parallel, ke) does ONE explicit
///     integration step. ke is the convergence threshold but the
///     step doesn't early-exit; the caller must poll GetvSum().
///   • PhysicalSystem.MomentumStep(goals, damping, iters) runs N
///     internal sub-steps with velocity damping. Better at escaping
///     flat / oscillating energy plateaus where plain Step stalls.
///   • Plain Step is faster per call but stalls when the energy
///     landscape is shallow. Plateau detection swaps to MomentumStep
///     once the std-Step-vs-prev ratio stays under 1% for 10 iters.
///   • Anchor goals with explicit (int, Point3d, double) ctor have
///     PIndex pre-set; AssignPIndex would re-resolve. We skip
///     AssignPIndex on Anchor and only call it for SphereCollide and
///     OnCurve which carry Point3d positions only.
///
/// Resilience:
///   • Top-level try/catch surfaces any internal failure as a GH
///     runtime warning rather than crashing the canvas.
///   • Sane empty outputs on every failure / Run=false path so
///     downstream components don't NRE on missing connections.
///   • Skips physics entirely when there are <2 valid placements or
///     when packer-residual is already zero (deterministic was enough).
/// </summary>
[Algorithm("Trencadis greedy pack", "Gaudi Park Guell broken-tile mosaic technique", Note = "Frahan-original pipeline integration")]
[Algorithm("NFP boundary slide", "Minkowski-difference arc-length sampler", WikiPath = "wiki/algorithms/packing_2D/trencadis_pipeline.md")]
[Algorithm("CVD-Lloyd interior seeding", "Lloyd 1982 centroidal Voronoi diagram relaxation", WikiPath = "wiki/algorithms/surface_mosaicing/primitives/cvd_lloyd.md")]
[Algorithm("Kangaroo 2 dynamic settle", "Daniel Piker goal-based physics", Note = "Optional final relaxation step")]
[DesignApplication(
    "All-in-one trencadís pipeline",
    DesignFlow.BottomUp,
    Precedent = "Gaudi Park Guell Trencadis; Battiato 2013 CVD+GVF full-pipeline reconstruction")]
public sealed class Pack2DTrencadisPipelineComponent : FrahanComponentBase, IGH_VariableParameterComponent
{
    // Progressive-disclosure toggles. Default: all collapsed. Each
    // toggle is exposed in the right-click context menu and reveals
    // a group of advanced inputs in-place.
    private bool _showPacking;
    private bool _showPhysics;
    private bool _showAnimation;
    public Pack2DTrencadisPipelineComponent()
        : base("Trencadís Pipeline", "TrencadisPipe",
            "All-in-one trencadís pipeline. Deterministic boundary " +
            "pack first; if residual overlap remains, Kangaroo 2 " +
            "settle fills the gaps. Exposes solver controls (kinetic " +
            "energy threshold, momentum) for cases where the " +
            "deterministic pass alone is insufficient.",
            "Frahan", "Trencadis")
    {
    }

    public override Guid ComponentGuid => new Guid("F2D00009-CADC-4F2D-9009-7E60CADA15A0");
    public override GH_Exposure Exposure => GH_Exposure.tertiary;
    protected override Bitmap Icon => IconProvider.Load("Trencadis.png");
    public override bool IsPreviewCapable => true;

    // Live-animation state (persists across SolveInstance calls).
    private PhysicalSystem _livePs;
    private List<IGoal> _liveGoals;
    private int[] _livePieceIdx;
    private List<Curve> _livePackedCurves;
    private Point3d[] _livePackedAnchors;  // post-packing centroids = anchor targets
    private bool[] _livePackedValid;
    private int _liveIter;
    private int _liveMaxIter;
    private double _liveLastVSum;
    private bool _liveConverged;
    private double _liveMeanRadius;
    private string _liveStatus = "";
    private long _liveInputHash;
    // Animated piece curves (translated to current physics positions).
    private Curve[] _liveCurrentCurves;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // Always-visible core inputs (6). Right-click the component to
        // reveal Packing / Physics / Animation tuning groups.
        pManager.AddCurveParameter("Parts", "P",
            "Closed planar shard curves to pack.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Outlines", "S",
            "Closed planar sheet outlines.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Sheet Holes", "H",
            "Hole curves as a tree.", GH_ParamAccess.tree);
        pManager[2].Optional = true;
        pManager.AddBooleanParameter("Run", "Run",
            "Master toggle. False = no output.",
            GH_ParamAccess.item, false);
        pManager.AddBooleanParameter("Apply Physics", "Phys",
            "Toggle for the dynamic settle stage. Right-click → Show " +
            "Physics Tuning to expose strength / convergence / momentum " +
            "controls.",
            GH_ParamAccess.item, false);
        pManager.AddBooleanParameter("Live Animate", "Live",
            "Step-by-step animated settle with viewport overlay. Right-" +
            "click → Show Animation Tuning to expose frame controls.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Settled Pieces", "C",
            "Final pieces.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Packed Pieces", "Cp",
            "Boundary-packed pieces BEFORE physics.",
            GH_ParamAccess.list);
        pManager.AddVectorParameter("Translations", "V",
            "Per-piece translation applied during settle.",
            GH_ParamAccess.list);
        pManager.AddPointParameter("Final Centroids", "X",
            "Per-piece centroid after the pipeline.",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Source Indices", "Src",
            "Original input curve index per placed shard.",
            GH_ParamAccess.list);
        pManager.AddNumberParameter("Final vSum", "v",
            "Final kinetic-energy sum.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Residual Overlap", "Res",
            "Sum of polygon-pair intersection areas after pipeline.",
            GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R",
            "Pipeline report.", GH_ParamAccess.item);
        pManager.AddCurveParameter("Pre-Trim Pieces", "Pt",
            "Pre-trim placed curves directly out of the boundary " +
            "packer (TrencadisFill.PackedCurves). One curve per " +
            "placed source. Compare against Packed Pieces (Cp) to see " +
            "what was trimmed off near the sheet edge / hole edges.",
            GH_ParamAccess.list);
        pManager.AddTransformParameter("Transforms", "T",
            "Per-placed-piece rigid transform from the source curve's " +
            "frame to its world placement. One Transform per piece, " +
            "parallel to Packed Pieces (Cp) and Source Indices (Src). " +
            "Apply to any source-frame geometry (drill points, hatch " +
            "patterns) to bring it into the placed frame.",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Trim Adjacency", "TA",
            "Per-piece tree where branch {i} holds the source-indices " +
            "of OTHER pieces that trimmed piece i during the deterministic " +
            "boundary pass. Empty branch = piece i was not trimmed " +
            "against any other piece. Useful for auditing chain-cut " +
            "relationships in trencadís layouts.",
            GH_ParamAccess.tree);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        try
        {
            SolveInstanceCore(da);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Pipeline failed: {ex.GetType().Name}: {ex.Message}");
            try { da.SetData(7, $"FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); }
            catch { /* best effort */ }
        }
    }

    private void SolveInstanceCore(IGH_DataAccess da)
    {
        var parts = new List<Curve>();
        var sheets = new List<Curve>();
        GH_Structure<GH_Curve> holesTree = null;
        double spacing = 0.05;
        var rotationsDeg = new List<double>();
        double tolerance = 0.01;
        int seed = 0;
        bool run = false;
        int maxCandidates = 600;
        double trimTolerance = 0.2;
        double grout = 0.02;
        double cutBudget = 0.35;
        double minBoundaryAffinity = 0.5;
        bool applyPhysics = false;
        int physicsIter = 100;
        double anchorS = 0.05;
        double collideS = 1.0;
        double boundaryS = 0.5;
        double convergenceThreshold = 1e-6;
        bool useMomentum = false;
        double momentumDamping = 0.95;
        bool liveAnimate = false;
        int stepsPerFrame = 5;
        int frameDelayMs = 30;

        // Read inputs by NickName. Indices shift as users toggle the
        // progressive-disclosure groups; fixed-index reads would fall
        // out of sync. Hidden inputs use the field defaults declared above.
        // 2026-05-22 fix v2: use canonical fixed indices for the first 3
        // inputs. They are registered by RegisterInputParams in this
        // order and never shift -- SyncInputs only inserts tuning params
        // BETWEEN Run (index 3) and Phys. Earlier NickName lookup returned
        // -1 when a saved canvas restored params with a different
        // NickName drift; fixed indices defend against that.
        // Each early return calls EmitEmptyOutputs so the Report output
        // carries an actionable diagnostic instead of bare <null>.
        var iParts = 0;
        var iSheets = 1;
        var iHoles = 2;

        if (Params.Input.Count < 6)
        {
            EmitEmptyOutputs(da, $"Internal: component has {Params.Input.Count} input params, expected at least 6 (P/S/H/Run/Phys/Live). Drop a fresh Pipeline component from the ribbon onto a new canvas.");
            return;
        }
        if (!da.GetDataList(iParts, parts))
        {
            var pName = Params.Input[iParts].NickName;
            EmitEmptyOutputs(da, $"Parts input (index 0, NickName='{pName}') not wired or empty. Connect a list of closed planar shard curves.");
            return;
        }
        if (!da.GetDataList(iSheets, sheets))
        {
            var sName = Params.Input[iSheets].NickName;
            EmitEmptyOutputs(da, $"Sheet Outlines input (index 1, NickName='{sName}') not wired or empty. Connect at least one closed planar outline.");
            return;
        }
        da.GetDataTree(iHoles, out holesTree);
        ReadItemBool(da, "Run", ref run);
        ReadItemBool(da, "Phys", ref applyPhysics);
        ReadItemBool(da, "Live", ref liveAnimate);
        // Packing tuning (only present when group enabled).
        ReadItemDouble(da, "Gap", ref spacing);
        ReadListDouble(da, "R", rotationsDeg);
        ReadItemDouble(da, "T", ref tolerance);
        ReadItemInt(da, "Seed", ref seed);
        ReadItemInt(da, "Max", ref maxCandidates);
        ReadItemDouble(da, "TrimT", ref trimTolerance);
        ReadItemDouble(da, "Gr", ref grout);
        ReadItemDouble(da, "Cut", ref cutBudget);
        ReadItemDouble(da, "BAff", ref minBoundaryAffinity);
        // Physics tuning.
        ReadItemInt(da, "PIter", ref physicsIter);
        ReadItemDouble(da, "Anc", ref anchorS);
        ReadItemDouble(da, "Col", ref collideS);
        ReadItemDouble(da, "Bp", ref boundaryS);
        ReadItemDouble(da, "Eps", ref convergenceThreshold);
        ReadItemBool(da, "Mom", ref useMomentum);
        ReadItemDouble(da, "Damp", ref momentumDamping);
        // Animation tuning.
        ReadItemInt(da, "Sf", ref stepsPerFrame);
        ReadItemInt(da, "Dly", ref frameDelayMs);

        if (!run)
        {
            ResetLiveState();
            EmitEmptyOutputs(da, "Run is false. Set Run=true to pack.");
            return;
        }

        var validParts = parts.Where(c => c != null && c.IsValid).ToList();
        var validSheets = sheets.Where(c => c != null && c.IsValid).ToList();
        if (validSheets.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "All sheet outlines null/invalid.");
            EmitEmptyOutputs(da, "No valid sheets.");
            return;
        }
        if (validParts.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "All input parts null/invalid.");
            EmitEmptyOutputs(da, "No valid parts.");
            return;
        }

        var sw = Stopwatch.StartNew();

        if (rotationsDeg.Count == 0)
            rotationsDeg.AddRange(new[] { 0.0, 45.0, 90.0, 135.0 });

        // Stage 1: deterministic boundary pack (Mode 2).
        var holesBySheet = SheetHolesUtil.BuildHolesBySheet(
            validSheets, holesTree, validSheets.Count, tolerance);

        PackingResult packResult;
        try
        {
            var solver = new TrencadisFill(
                sheetOutlines: validSheets,
                sheetHoles: holesBySheet.Select(l => (IReadOnlyList<Curve>)l).ToList(),
                spacing: spacing,
                rotationsDeg: rotationsDeg,
                tolerance: tolerance,
                seed: seed,
                maxCandidates: maxCandidates,
                trimTolerance: trimTolerance,
                grout: grout,
                boundaryMode: 2,
                minBoundaryAffinity: minBoundaryAffinity,
                cutBudget: cutBudget,
                useCvdSeeds: true,
                useGvf: true,
                gvfMu: 0.2);
            packResult = solver.Pack(validParts);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Packing failed: {ex.Message}");
            EmitEmptyOutputs(da, $"Packing failed: {ex.Message}");
            return;
        }

        if (packResult == null)
        {
            EmitEmptyOutputs(da, "Packing returned null.");
            return;
        }

        var packedCurves = (packResult.TrimmedCurves != null && packResult.TrimmedCurves.Count > 0)
            ? packResult.TrimmedCurves
            : (packResult.PackedCurves ?? new List<Curve>());

        var sourceIndices = packResult.SourceIndices ?? new List<int>();
        var packMs = sw.ElapsedMilliseconds;

        // Per-piece centroid + bounding radius. Centroid is the answer
        // point — anchor target for the dynamic stage.
        var packedCentroids = new Point3d[packedCurves.Count];
        var packedRadii = new double[packedCurves.Count];
        var packedValid = new bool[packedCurves.Count];
        var validCount = 0;
        for (int i = 0; i < packedCurves.Count; i++)
        {
            var c = packedCurves[i];
            if (c == null || !c.IsValid) continue;
            try
            {
                var ap = AreaMassProperties.Compute(c);
                packedCentroids[i] = ap?.Centroid ?? c.GetBoundingBox(true).Center;
            }
            catch { packedCentroids[i] = c.GetBoundingBox(true).Center; }
            var bb = c.GetBoundingBox(true);
            packedRadii[i] = Math.Max(0.5 * bb.Diagonal.Length, 1e-3);
            packedValid[i] = true;
            validCount++;
        }

        // Pre-physics residual: how much overlap did the deterministic
        // pass leave behind? If it's already zero, skip physics
        // (deterministic was sufficient).
        var packTol = Math.Max(tolerance, 0.001);
        var preResidual = ComputeResidualOverlap(packedCurves, packTol);

        var translations = new List<Vector3d>(packedCurves.Count);
        var finalCentroids = new List<Point3d>(packedCurves.Count);
        var settled = new List<Curve>(packedCurves.Count);
        double finalVSum = 0;
        int physicsItUsed = 0;
        int physicsParticles = 0;
        int physicsGoals = 0;
        string solverMode = "skipped";

        bool needPhysics = applyPhysics && validCount >= 2 && preResidual > packTol;
        if (!needPhysics)
        {
            ResetLiveState();
            for (int i = 0; i < packedCurves.Count; i++)
            {
                settled.Add(packedCurves[i]?.DuplicateCurve());
                translations.Add(Vector3d.Zero);
                finalCentroids.Add(packedValid[i] ? packedCentroids[i] : Point3d.Origin);
            }
            if (applyPhysics && preResidual <= packTol)
                solverMode = "deterministic-clean (skipped)";
        }
        else if (liveAnimate)
        {
            // Live animation branch — run stepsPerFrame iterations, output
            // the current animated state, schedule the next frame if not
            // converged. State persists across SolveInstance calls via
            // private _live* fields. Input hash detects when packing
            // result has changed and triggers a rebuild.
            var hash = ComputeLiveHash(packedCurves, packedCentroids,
                anchorS, collideS, boundaryS, physicsIter, convergenceThreshold,
                useMomentum, momentumDamping);
            if (_livePs == null || _liveInputHash != hash)
            {
                BuildLivePhysics(packedCurves, packedCentroids, packedRadii,
                    packedValid, validCount, validSheets, holesTree,
                    anchorS, collideS, boundaryS, physicsIter);
                _liveInputHash = hash;
                physicsParticles = _livePs.ParticleCount();
                physicsGoals = _liveGoals.Count;
            }
            else
            {
                physicsParticles = _livePs.ParticleCount();
                physicsGoals = _liveGoals.Count;
            }

            // Run a batch of steps. Plateau-detection swap to MomentumStep
            // happens inside RunLiveBatch.
            int batchSize = Math.Max(1, stepsPerFrame);
            RunLiveBatch(batchSize, convergenceThreshold, useMomentum, momentumDamping);
            UpdateLiveCurves();

            for (int i = 0; i < packedCurves.Count; i++)
            {
                if (_liveCurrentCurves != null && i < _liveCurrentCurves.Length && _liveCurrentCurves[i] != null)
                {
                    settled.Add(_liveCurrentCurves[i].DuplicateCurve());
                    var newPos = packedValid[i] && _livePieceIdx[i] >= 0
                        ? _livePs.GetPosition(_livePieceIdx[i])
                        : packedCentroids[i];
                    translations.Add(newPos - packedCentroids[i]);
                    finalCentroids.Add(newPos);
                }
                else
                {
                    settled.Add(packedCurves[i]?.DuplicateCurve());
                    translations.Add(Vector3d.Zero);
                    finalCentroids.Add(packedValid[i] ? packedCentroids[i] : Point3d.Origin);
                }
            }

            finalVSum = _liveLastVSum;
            physicsItUsed = _liveIter;
            solverMode = $"Live ({_liveIter}/{_liveMaxIter}, {batchSize}/frame)";
            _liveStatus = _liveConverged
                ? $"Converged at iter {_liveIter}  vSum={_liveLastVSum:E2}"
                : $"Settling iter {_liveIter}/{_liveMaxIter}  vSum={_liveLastVSum:E2}";

            // Schedule next frame if more work remains.
            if (!_liveConverged && _liveIter < _liveMaxIter)
            {
                var doc = OnPingDocument();
                if (doc != null)
                {
                    var delay = Math.Max(0, frameDelayMs);
                    doc.ScheduleSolution(delay, d => ExpireSolution(false));
                }
            }
        }
        else
        {
            ResetLiveState();
            double meanR = 0;
            for (int i = 0; i < packedCurves.Count; i++)
                if (packedValid[i]) meanR += packedRadii[i];
            meanR /= validCount;

            var ps = new PhysicalSystem();
            var pieceIdx = new int[packedCurves.Count];
            for (int i = 0; i < packedCurves.Count; i++)
            {
                if (!packedValid[i]) { pieceIdx[i] = -1; continue; }
                ps.AddParticle(packedCentroids[i], 1.0);
                pieceIdx[i] = ps.ParticleCount() - 1;
            }
            physicsParticles = ps.ParticleCount();

            var goals = new List<IGoal>(validCount + 8);

            if (anchorS > 0)
            {
                for (int i = 0; i < packedCurves.Count; i++)
                    if (packedValid[i])
                        goals.Add(new Anchor(pieceIdx[i], packedCentroids[i], anchorS));
            }

            if (collideS > 0 && validCount >= 2)
            {
                var centroidList = new List<Point3d>(validCount);
                for (int i = 0; i < packedCurves.Count; i++)
                    if (packedValid[i]) centroidList.Add(packedCentroids[i]);
                goals.Add(new SphereCollide(centroidList, meanR, collideS));
            }

            if (boundaryS > 0)
            {
                var boundaryCurves = new List<Curve>();
                foreach (var s in validSheets)
                    if (s != null && s.IsClosed) boundaryCurves.Add(s);
                if (holesTree != null)
                {
                    foreach (var path in holesTree.Paths)
                        foreach (var ghc in holesTree.get_Branch(path))
                            if (ghc is GH_Curve gc && gc.Value != null && gc.Value.IsClosed)
                                boundaryCurves.Add(gc.Value);
                }
                if (boundaryCurves.Count > 0)
                {
                    var threshold = meanR * 1.5;
                    for (int i = 0; i < packedCurves.Count; i++)
                    {
                        if (!packedValid[i]) continue;
                        foreach (var bc in boundaryCurves)
                        {
                            if (bc == null) continue;
                            if (!bc.ClosestPoint(packedCentroids[i], out var t)) continue;
                            var dist = packedCentroids[i].DistanceTo(bc.PointAt(t));
                            if (dist > threshold) continue;
                            goals.Add(new OnCurve(
                                new List<Point3d> { packedCentroids[i] },
                                bc, boundaryS));
                            break;
                        }
                    }
                }
            }
            physicsGoals = goals.Count;

            // AssignPIndex only on goals carrying Point3d positions
            // (SphereCollide, OnCurve). Anchor goals already have
            // explicit indices from the (int, Point3d, double) ctor.
            const double Ptol = 1e-4;
            for (int gi = 0; gi < goals.Count; gi++)
            {
                if (goals[gi] is Anchor) continue;
                ps.AssignPIndex(goals[gi], Ptol);
            }

            // Solver loop. Plain Step with plateau detection by default;
            // user can force MomentumStep via the toggle. Plateau = 10
            // consecutive iterations where vSum changes by less than 1%.
            if (useMomentum)
            {
                ps.MomentumStep(goals, momentumDamping, physicsIter);
                finalVSum = ps.GetvSum();
                physicsItUsed = physicsIter;
                solverMode = $"MomentumStep (forced, damping={momentumDamping:F2})";
            }
            else
            {
                double prevVSum = double.MaxValue;
                int plateau = 0;
                int plateauTrigger = 10;
                bool fellBack = false;
                for (int it = 0; it < physicsIter; it++)
                {
                    ps.Step(goals, true, convergenceThreshold);
                    finalVSum = ps.GetvSum();
                    physicsItUsed = it + 1;
                    if (finalVSum < convergenceThreshold) break;
                    if (prevVSum > 0 &&
                        Math.Abs(finalVSum - prevVSum) / prevVSum < 0.01)
                        plateau++;
                    else
                        plateau = 0;
                    prevVSum = finalVSum;
                    if (plateau >= plateauTrigger && it < physicsIter - plateauTrigger)
                    {
                        // Plateau hit. Hand off remaining iters to
                        // MomentumStep — the "Kangaroo fills the gaps"
                        // path when plain Step stalls.
                        var remaining = physicsIter - physicsItUsed;
                        ps.MomentumStep(goals, momentumDamping, remaining);
                        finalVSum = ps.GetvSum();
                        physicsItUsed += remaining;
                        fellBack = true;
                        break;
                    }
                }
                solverMode = fellBack
                    ? $"Step + MomentumStep auto-fallback (damping={momentumDamping:F2})"
                    : "Step";
            }

            for (int i = 0; i < packedCurves.Count; i++)
            {
                if (!packedValid[i] || packedCurves[i] == null)
                {
                    settled.Add(packedCurves[i]?.DuplicateCurve());
                    translations.Add(Vector3d.Zero);
                    finalCentroids.Add(packedValid[i] ? packedCentroids[i] : Point3d.Origin);
                    continue;
                }
                var newPos = ps.GetPosition(pieceIdx[i]);
                var v = newPos - packedCentroids[i];
                var c = packedCurves[i].DuplicateCurve();
                c.Translate(v);
                settled.Add(c);
                translations.Add(v);
                finalCentroids.Add(newPos);
            }
        }

        var residualOverlap = ComputeResidualOverlap(settled, packTol);
        sw.Stop();

        var report =
            $"Trencadís pipeline: {validCount}/{validParts.Count} placed\n" +
            $"Stage 1 pack  : {packMs} ms (Mode 2)\n" +
            $"Pre-residual  : {preResidual:F4}\n" +
            $"Stage 2 phys  : {(needPhysics ? $"{sw.ElapsedMilliseconds - packMs} ms" : "skipped")}\n" +
            $"Solver mode   : {solverMode}\n" +
            $"Iterations    : {physicsItUsed}/{physicsIter}\n" +
            $"Particles     : {physicsParticles}\n" +
            $"Goals         : {physicsGoals}\n" +
            $"Final vSum    : {finalVSum:E2} (eps {convergenceThreshold:E1})\n" +
            $"Final residual: {residualOverlap:F4}\n" +
            $"Improvement   : {Math.Max(0, preResidual - residualOverlap):F4}\n" +
            $"Total runtime : {sw.ElapsedMilliseconds} ms\n";

        da.SetDataList(0, settled);
        da.SetDataList(1, packedCurves);
        da.SetDataList(2, translations);
        da.SetDataList(3, finalCentroids);
        da.SetDataList(4, sourceIndices);
        da.SetData(5, finalVSum);
        da.SetData(6, residualOverlap);
        da.SetData(7, report);
        // 2026-05-22: surface PackingResult internals (pre-trim curves,
        // per-piece transforms, trim adjacency tree). Guarded against
        // saved-canvas instances that pre-date these outputs.
        int outCount = Params.Output.Count;
        if (outCount > 8)
            da.SetDataList(8, packResult.PackedCurves ?? new List<Curve>());
        if (outCount > 9)
            da.SetDataList(9, packResult.Transforms ?? new List<Transform>());
        if (outCount > 10)
        {
            var trimAdjTree = new GH_Structure<GH_Integer>();
            if (packResult.TrimAdjacency != null)
            {
                for (int i = 0; i < packResult.TrimAdjacency.Count; i++)
                {
                    var path = new GH_Path(i);
                    var row = packResult.TrimAdjacency[i];
                    if (row != null)
                        foreach (var srcIdx in row)
                            trimAdjTree.Append(new GH_Integer(srcIdx), path);
                }
            }
            da.SetDataTree(10, trimAdjTree);
        }
    }

    private void EmitEmptyOutputs(IGH_DataAccess da, string report)
    {
        // 2026-05-22: defensive guards against saved-canvas instances
        // that pre-date the new outputs at indices 8/9/10. Older saved
        // Pipeline components have only 8 outputs; SetData(8..10) on
        // those throws "Output parameter Index [N] too high".
        int outCount = Params.Output.Count;
        if (outCount > 0) da.SetDataList(0, new List<Curve>());
        if (outCount > 1) da.SetDataList(1, new List<Curve>());
        if (outCount > 2) da.SetDataList(2, new List<Vector3d>());
        if (outCount > 3) da.SetDataList(3, new List<Point3d>());
        if (outCount > 4) da.SetDataList(4, new List<int>());
        if (outCount > 5) da.SetData(5, 0.0);
        if (outCount > 6) da.SetData(6, 0.0);
        if (outCount > 7) da.SetData(7, report);
        if (outCount > 8) da.SetDataList(8, new List<Curve>());
        if (outCount > 9) da.SetDataList(9, new List<Transform>());
        if (outCount > 10) da.SetDataTree(10, new GH_Structure<GH_Integer>());
    }

    // ─── Live-animation helpers ──────────────────────────────────────────

    private void ResetLiveState()
    {
        _livePs = null;
        _liveGoals = null;
        _livePieceIdx = null;
        _livePackedCurves = null;
        _livePackedAnchors = null;
        _livePackedValid = null;
        _liveIter = 0;
        _liveMaxIter = 0;
        _liveLastVSum = 0;
        _liveConverged = false;
        _liveMeanRadius = 0;
        _liveStatus = "";
        _liveInputHash = 0;
        _liveCurrentCurves = null;
    }

    private static long ComputeLiveHash(
        IList<Curve> packedCurves, Point3d[] centroids,
        double anchorS, double collideS, double boundaryS,
        int maxIter, double convThreshold, bool useMomentum, double damping)
    {
        long h = packedCurves?.Count ?? 0;
        if (centroids != null)
        {
            foreach (var p in centroids)
            {
                h = h * 397 + (long)Math.Round(p.X * 1000);
                h = h * 397 + (long)Math.Round(p.Y * 1000);
            }
        }
        h = h * 397 + (long)Math.Round(anchorS * 1e6);
        h = h * 397 + (long)Math.Round(collideS * 1e6);
        h = h * 397 + (long)Math.Round(boundaryS * 1e6);
        h = h * 397 + maxIter;
        h = h * 397 + (long)Math.Round(convThreshold * 1e12);
        h = h * 397 + (useMomentum ? 1 : 0);
        h = h * 397 + (long)Math.Round(damping * 1e6);
        return h;
    }

    private void BuildLivePhysics(
        List<Curve> packedCurves, Point3d[] centroids, double[] radii,
        bool[] valid, int validCount,
        List<Curve> validSheets, GH_Structure<GH_Curve> holesTree,
        double anchorS, double collideS, double boundaryS, int maxIter)
    {
        double meanR = 0;
        for (int i = 0; i < packedCurves.Count; i++)
            if (valid[i]) meanR += radii[i];
        meanR /= Math.Max(1, validCount);

        var ps = new PhysicalSystem();
        var pieceIdx = new int[packedCurves.Count];
        for (int i = 0; i < packedCurves.Count; i++)
        {
            if (!valid[i]) { pieceIdx[i] = -1; continue; }
            ps.AddParticle(centroids[i], 1.0);
            pieceIdx[i] = ps.ParticleCount() - 1;
        }

        var goals = new List<IGoal>(validCount + 8);

        if (anchorS > 0)
        {
            for (int i = 0; i < packedCurves.Count; i++)
                if (valid[i])
                    goals.Add(new Anchor(pieceIdx[i], centroids[i], anchorS));
        }

        if (collideS > 0 && validCount >= 2)
        {
            var centroidList = new List<Point3d>(validCount);
            for (int i = 0; i < packedCurves.Count; i++)
                if (valid[i]) centroidList.Add(centroids[i]);
            goals.Add(new SphereCollide(centroidList, meanR, collideS));
        }

        if (boundaryS > 0)
        {
            var boundaryCurves = new List<Curve>();
            foreach (var s in validSheets)
                if (s != null && s.IsClosed) boundaryCurves.Add(s);
            if (holesTree != null)
            {
                foreach (var path in holesTree.Paths)
                    foreach (var ghc in holesTree.get_Branch(path))
                        if (ghc is GH_Curve gc && gc.Value != null && gc.Value.IsClosed)
                            boundaryCurves.Add(gc.Value);
            }
            if (boundaryCurves.Count > 0)
            {
                var threshold = meanR * 1.5;
                for (int i = 0; i < packedCurves.Count; i++)
                {
                    if (!valid[i]) continue;
                    foreach (var bc in boundaryCurves)
                    {
                        if (bc == null) continue;
                        if (!bc.ClosestPoint(centroids[i], out var t)) continue;
                        var dist = centroids[i].DistanceTo(bc.PointAt(t));
                        if (dist > threshold) continue;
                        goals.Add(new OnCurve(
                            new List<Point3d> { centroids[i] }, bc, boundaryS));
                        break;
                    }
                }
            }
        }

        const double Ptol = 1e-4;
        for (int gi = 0; gi < goals.Count; gi++)
        {
            if (goals[gi] is Anchor) continue;
            ps.AssignPIndex(goals[gi], Ptol);
        }

        _livePs = ps;
        _liveGoals = goals;
        _livePieceIdx = pieceIdx;
        _livePackedCurves = packedCurves;
        _livePackedAnchors = (Point3d[])centroids.Clone();
        _livePackedValid = (bool[])valid.Clone();
        _liveIter = 0;
        _liveMaxIter = maxIter;
        _liveLastVSum = 0;
        _liveConverged = false;
        _liveMeanRadius = meanR;
        _liveCurrentCurves = new Curve[packedCurves.Count];
    }

    private void RunLiveBatch(int steps, double convThreshold,
        bool forceMomentum, double damping)
    {
        if (_livePs == null || _liveGoals == null) return;
        if (_liveConverged || _liveIter >= _liveMaxIter) return;

        if (forceMomentum)
        {
            var n = Math.Min(steps, _liveMaxIter - _liveIter);
            _livePs.MomentumStep(_liveGoals, damping, n);
            _liveLastVSum = _livePs.GetvSum();
            _liveIter += n;
            if (_liveLastVSum < convThreshold) _liveConverged = true;
            return;
        }

        for (int i = 0; i < steps; i++)
        {
            if (_liveIter >= _liveMaxIter) break;
            _livePs.Step(_liveGoals, true, convThreshold);
            _liveLastVSum = _livePs.GetvSum();
            _liveIter++;
            if (_liveLastVSum < convThreshold) { _liveConverged = true; break; }
        }
    }

    private void UpdateLiveCurves()
    {
        if (_livePackedCurves == null || _livePs == null || _liveCurrentCurves == null)
            return;
        var positions = _livePs.GetPositionArray();
        for (int i = 0; i < _livePackedCurves.Count; i++)
        {
            if (_livePackedValid == null || !_livePackedValid[i] ||
                _livePackedCurves[i] == null || _livePieceIdx[i] < 0)
            {
                _liveCurrentCurves[i] = _livePackedCurves[i]?.DuplicateCurve();
                continue;
            }
            var newPos = positions[_livePieceIdx[i]];
            var v = newPos - _livePackedAnchors[i];
            var c = _livePackedCurves[i].DuplicateCurve();
            c.Translate(v);
            _liveCurrentCurves[i] = c;
        }
    }

    // ─── Viewport overlay ────────────────────────────────────────────────

    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
        base.DrawViewportWires(args);
        if (_livePs == null || _livePackedAnchors == null) return;

        // Draw the animated piece curves so the user sees motion frame-
        // by-frame even before the upstream output bakes back into the
        // canvas. Lime green so they're visually distinct from the
        // packed (gray-default) output preview.
        if (_liveCurrentCurves != null)
        {
            foreach (var c in _liveCurrentCurves)
                if (c != null)
                    args.Display.DrawCurve(c, Color.LimeGreen, 2);
        }

        // Anchor leashes — gray translucent line from anchor target to
        // current particle position. Reveals which pieces are being
        // pulled where.
        var positions = _livePs.GetPositionArray();
        var leash = Color.FromArgb(140, 180, 180, 180);
        for (int i = 0; i < _livePackedAnchors.Length; i++)
        {
            if (_livePieceIdx == null || _livePieceIdx[i] < 0) continue;
            if (_livePieceIdx[i] >= positions.Length) continue;
            var anchor = _livePackedAnchors[i];
            var current = positions[_livePieceIdx[i]];
            if (anchor.DistanceToSquared(current) < 1e-12) continue;
            args.Display.DrawLine(new Line(anchor, current), leash, 1);
        }

        // Status overlay — iteration counter + vSum near the top of the
        // pieces' bounding box.
        if (!string.IsNullOrEmpty(_liveStatus))
        {
            var bb = ClippingBox;
            if (bb.IsValid && bb.Diagonal.Length > 1e-6)
            {
                var labelX = bb.Min.X;
                var labelY = bb.Max.Y + bb.Diagonal.Y * 0.05;
                var plane = new Plane(new Point3d(labelX, labelY, 0),
                    Vector3d.XAxis, Vector3d.YAxis);
                var height = Math.Max(bb.Diagonal.Length * 0.025, 0.5);
                var text = new Text3d(_liveStatus, plane, height);
                args.Display.Draw3dText(text,
                    _liveConverged ? Color.LimeGreen : Color.OrangeRed);
                text.Dispose();
            }
        }
    }

    public override BoundingBox ClippingBox
    {
        get
        {
            var bb = base.ClippingBox;
            if (_liveCurrentCurves != null)
            {
                foreach (var c in _liveCurrentCurves)
                    if (c != null) bb.Union(c.GetBoundingBox(false));
            }
            else if (_livePackedCurves != null)
            {
                foreach (var c in _livePackedCurves)
                    if (c != null) bb.Union(c.GetBoundingBox(false));
            }
            return bb;
        }
    }

    private static double ComputeResidualOverlap(IList<Curve> curves, double tol)
    {
        if (curves == null) return 0;
        double total = 0;
        var bboxes = new BoundingBox[curves.Count];
        for (int i = 0; i < curves.Count; i++)
            bboxes[i] = curves[i] == null
                ? BoundingBox.Empty
                : curves[i].GetBoundingBox(false);

        for (int i = 0; i < curves.Count; i++)
        {
            var ci = curves[i];
            if (ci == null || !ci.IsClosed) continue;
            var bbI = bboxes[i];
            for (int j = i + 1; j < curves.Count; j++)
            {
                var cj = curves[j];
                if (cj == null || !cj.IsClosed) continue;
                var bbJ = bboxes[j];
                if (bbI.Max.X < bbJ.Min.X - tol || bbI.Min.X > bbJ.Max.X + tol ||
                    bbI.Max.Y < bbJ.Min.Y - tol || bbI.Min.Y > bbJ.Max.Y + tol) continue;

                Curve[] inter = null;
                try { inter = Curve.CreateBooleanIntersection(ci, cj, tol); }
                catch { inter = null; }
                if (inter == null) continue;
                foreach (var c in inter)
                {
                    if (c == null || !c.IsClosed) continue;
                    var ap = AreaMassProperties.Compute(c);
                    if (ap != null) total += ap.Area;
                }
            }
        }
        return total;
    }

    // ─── NickName-based input lookup ─────────────────────────────────────

    private int ParamIndex(string nick)
    {
        for (int i = 0; i < Params.Input.Count; i++)
            if (Params.Input[i].NickName == nick) return i;
        return -1;
    }

    private void ReadItemBool(IGH_DataAccess da, string nick, ref bool value)
    {
        var idx = ParamIndex(nick);
        if (idx >= 0) da.GetData(idx, ref value);
    }

    private void ReadItemInt(IGH_DataAccess da, string nick, ref int value)
    {
        var idx = ParamIndex(nick);
        if (idx >= 0) da.GetData(idx, ref value);
    }

    private void ReadItemDouble(IGH_DataAccess da, string nick, ref double value)
    {
        var idx = ParamIndex(nick);
        if (idx >= 0) da.GetData(idx, ref value);
    }

    private void ReadListDouble(IGH_DataAccess da, string nick, List<double> list)
    {
        var idx = ParamIndex(nick);
        if (idx >= 0) da.GetDataList(idx, list);
    }

    // ─── Progressive-disclosure menu + group sync ────────────────────────

    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        Menu_AppendItem(menu, "Show Packing Tuning",
            (s, e) => { _showPacking = !_showPacking; SyncInputs(); },
            true, _showPacking);
        Menu_AppendItem(menu, "Show Physics Tuning",
            (s, e) => { _showPhysics = !_showPhysics; SyncInputs(); },
            true, _showPhysics);
        Menu_AppendItem(menu, "Show Animation Tuning",
            (s, e) => { _showAnimation = !_showAnimation; SyncInputs(); },
            true, _showAnimation);
    }

    private void SyncInputs()
    {
        // Add or remove each group's inputs to match the current toggle
        // state. Order matters: pack inputs sit between Run and Apply
        // Physics; physics inputs sit between Apply Physics and Live
        // Animate; animation inputs go after Live Animate.
        SyncPackingGroup();
        SyncPhysicsGroup();
        SyncAnimationGroup();

        Params.OnParametersChanged();
        OnAttributesChanged();
        ExpireSolution(true);
    }

    private void SyncPackingGroup()
    {
        var present = ParamIndex("Gap") >= 0;
        if (_showPacking == present) return;
        if (_showPacking)
        {
            // Insert in order between "Run" and "Phys".
            var runIdx = ParamIndex("Run");
            var insertAt = runIdx + 1;
            InsertAt(insertAt + 0, BuildNumber("Spacing", "Gap",
                "Pre-trim part-to-part clearance.", 0.05));
            InsertAt(insertAt + 1, BuildNumberList("Rotations", "R",
                "Allowed rotation angles in degrees.", optional: true));
            InsertAt(insertAt + 2, BuildNumber("Tolerance", "T",
                "Geometric tolerance.", 0.01));
            InsertAt(insertAt + 3, BuildInteger("Seed", "Seed",
                "0 = deterministic.", 0));
            InsertAt(insertAt + 4, BuildInteger("Max Candidates", "Max",
                "Candidate budget per part per rotation.", 600));
            InsertAt(insertAt + 5, BuildNumber("Trim Tolerance", "TrimT",
                "Maximum allowed part-to-part overlap depth.", 0.2));
            InsertAt(insertAt + 6, BuildNumber("Grout", "Gr",
                "Inward offset applied AFTER trim.", 0.02));
            InsertAt(insertAt + 7, BuildNumber("Cut Budget", "Cut",
                "Battiato cumulative-cut cap.", 0.35));
            InsertAt(insertAt + 8, BuildNumber("Min Boundary Affinity", "BAff",
                "Edge-match threshold.", 0.5));
        }
        else
        {
            RemoveByNick("Gap");
            RemoveByNick("R");
            RemoveByNick("T");
            RemoveByNick("Seed");
            RemoveByNick("Max");
            RemoveByNick("TrimT");
            RemoveByNick("Gr");
            RemoveByNick("Cut");
            RemoveByNick("BAff");
        }
    }

    private void SyncPhysicsGroup()
    {
        var present = ParamIndex("PIter") >= 0;
        if (_showPhysics == present) return;
        if (_showPhysics)
        {
            // Insert between "Phys" and "Live".
            var physIdx = ParamIndex("Phys");
            var insertAt = physIdx + 1;
            InsertAt(insertAt + 0, BuildInteger("Physics Iterations", "PIter",
                "Maximum solver step count.", 100));
            InsertAt(insertAt + 1, BuildNumber("Anchor Strength", "Anc",
                "Centroid pull-back strength.", 0.05));
            InsertAt(insertAt + 2, BuildNumber("Collide Strength", "Col",
                "SphereCollide strength.", 1.0));
            InsertAt(insertAt + 3, BuildNumber("Boundary Pull", "Bp",
                "OnCurve strength for boundary-near pieces.", 0.5));
            InsertAt(insertAt + 4, BuildNumber("Convergence Threshold", "Eps",
                "Kinetic-energy threshold for early exit.", 1e-6));
            InsertAt(insertAt + 5, BuildBoolean("Use Momentum", "Mom",
                "Force MomentumStep instead of plain Step.", false));
            InsertAt(insertAt + 6, BuildNumber("Momentum Damping", "Damp",
                "Velocity damping for MomentumStep. 0.5–0.99.", 0.95));
        }
        else
        {
            RemoveByNick("PIter");
            RemoveByNick("Anc");
            RemoveByNick("Col");
            RemoveByNick("Bp");
            RemoveByNick("Eps");
            RemoveByNick("Mom");
            RemoveByNick("Damp");
        }
    }

    private void SyncAnimationGroup()
    {
        var present = ParamIndex("Sf") >= 0;
        if (_showAnimation == present) return;
        if (_showAnimation)
        {
            // Append after "Live" (which sits at the end of always-visible).
            var liveIdx = ParamIndex("Live");
            var insertAt = liveIdx + 1;
            InsertAt(insertAt + 0, BuildInteger("Steps Per Frame", "Sf",
                "Solver steps per redraw frame.", 5));
            InsertAt(insertAt + 1, BuildInteger("Frame Delay", "Dly",
                "Milliseconds between successive frames.", 30));
        }
        else
        {
            RemoveByNick("Sf");
            RemoveByNick("Dly");
        }
    }

    private void InsertAt(int index, IGH_Param p)
    {
        Params.RegisterInputParam(p, index);
    }

    private void RemoveByNick(string nick)
    {
        var p = Params.Input.FirstOrDefault(x => x.NickName == nick);
        if (p != null)
        {
            // Detach any wires before unregistering.
            foreach (var src in p.Sources.ToList()) p.RemoveSource(src);
            Params.UnregisterInputParameter(p);
        }
    }

    private static IGH_Param BuildNumber(string name, string nick, string desc, double def)
    {
        var p = new Param_Number
        {
            Name = name,
            NickName = nick,
            Description = desc,
            Access = GH_ParamAccess.item,
        };
        p.PersistentData.Append(new GH_Number(def));
        return p;
    }

    private static IGH_Param BuildNumberList(string name, string nick, string desc, bool optional)
    {
        var p = new Param_Number
        {
            Name = name,
            NickName = nick,
            Description = desc,
            Access = GH_ParamAccess.list,
            Optional = optional,
        };
        return p;
    }

    private static IGH_Param BuildInteger(string name, string nick, string desc, int def)
    {
        var p = new Param_Integer
        {
            Name = name,
            NickName = nick,
            Description = desc,
            Access = GH_ParamAccess.item,
        };
        p.PersistentData.Append(new GH_Integer(def));
        return p;
    }

    private static IGH_Param BuildBoolean(string name, string nick, string desc, bool def)
    {
        var p = new Param_Boolean
        {
            Name = name,
            NickName = nick,
            Description = desc,
            Access = GH_ParamAccess.item,
        };
        p.PersistentData.Append(new GH_Boolean(def));
        return p;
    }

    // ─── State persistence ───────────────────────────────────────────────

    public override bool Write(GH_IWriter writer)
    {
        writer.SetBoolean("ShowPacking", _showPacking);
        writer.SetBoolean("ShowPhysics", _showPhysics);
        writer.SetBoolean("ShowAnimation", _showAnimation);
        return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
        bool b = false;
        if (reader.TryGetBoolean("ShowPacking", ref b)) _showPacking = b;
        if (reader.TryGetBoolean("ShowPhysics", ref b)) _showPhysics = b;
        if (reader.TryGetBoolean("ShowAnimation", ref b)) _showAnimation = b;
        var ok = base.Read(reader);
        // Param add/remove must happen AFTER base Read has restored
        // any persisted parameters; otherwise the structure clashes.
        SyncInputs();
        // 2026-05-22: auto-upgrade saved canvases that pre-date the new
        // outputs at indices 8/9/10.
        SyncOutputs();
        return ok;
    }

    private void SyncOutputs()
    {
        // Adds any newly-registered outputs that a saved older Pipeline
        // instance doesn't have yet. 2026-05-22 batch added Pre-Trim
        // Pieces (8), Transforms (9), Trim Adjacency (10).
        bool changed = false;
        if (Params.Output.Count < 9)
        {
            var p = new Grasshopper.Kernel.Parameters.Param_Curve
            {
                Name = "Pre-Trim Pieces",
                NickName = "Pt",
                Description = "TrencadisFill.PackedCurves (pre-trim). " +
                              "Compare against Cp to see what was trimmed off.",
                Access = GH_ParamAccess.list,
            };
            Params.RegisterOutputParam(p);
            changed = true;
        }
        if (Params.Output.Count < 10)
        {
            var p = new Grasshopper.Kernel.Parameters.Param_Transform
            {
                Name = "Transforms",
                NickName = "T",
                Description = "Per-placed-piece rigid Transform " +
                              "(source frame to world).",
                Access = GH_ParamAccess.list,
            };
            Params.RegisterOutputParam(p);
            changed = true;
        }
        if (Params.Output.Count < 11)
        {
            var p = new Grasshopper.Kernel.Parameters.Param_Integer
            {
                Name = "Trim Adjacency",
                NickName = "TA",
                Description = "Per-piece tree of trim source-indices.",
                Access = GH_ParamAccess.tree,
            };
            Params.RegisterOutputParam(p);
            changed = true;
        }
        if (changed) Params.OnParametersChanged();
    }

    // ─── IGH_VariableParameterComponent ──────────────────────────────────

    public bool CanInsertParameter(GH_ParameterSide side, int index) => false;
    public bool CanRemoveParameter(GH_ParameterSide side, int index) => false;
    public IGH_Param CreateParameter(GH_ParameterSide side, int index) => null;
    public bool DestroyParameter(GH_ParameterSide side, int index) => false;
    public void VariableParameterMaintenance() { }
}
