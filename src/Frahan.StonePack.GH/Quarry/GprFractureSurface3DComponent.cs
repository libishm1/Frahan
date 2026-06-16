using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Frahan.Masonry.Quarry.Processing;

namespace Frahan.GH;

/// <summary>
/// GPR Fracture Surfaces 3D -- builds true 3D fracture SURFACES from a multi-line GPR pick cloud
/// and reports the GPR->fracture->mesh TOLERANCE LADDER per surface. Adaptive depth-clustering
/// assigns every pick to a fracture; each fracture is interpolated by managed C# KRIGING (Core
/// Kriging) onto a grid, whose posterior std is sigma_interp. Per vertex the deviation-from-truth
/// sigma_total = sqrt(sigma_recon^2 + sigma_interp^2 + sigma_mesh^2) (FractureUncertainty) is
/// colour-mapped (green = within tolerance -> red), and the CONFIDENCE (mean P(|dev|<=T)) is the
/// optimisation metric. Fully managed -- no native shim, no Python.
/// </summary>
public sealed class GprFractureSurface3DComponent : FrahanComponentBase
{
    public GprFractureSurface3DComponent()
        : base("GPR Fracture Surfaces 3D", "GprFrac3D",
               "Cluster a multi-line GPR pick cloud into fractures, krige each into a 3D surface, " +
               "and colour it by the GPR->fracture->mesh deviation-from-truth (tolerance ladder). " +
               "Outputs the confidence-within-tolerance metric. Managed (C# kriging; no Python).",
               "Frahan", "Quarry")
    { }

    public override Guid ComponentGuid => new Guid("A7E0B0F2-0C0F-4A16-9E3D-0FACE0FACE03");
    protected override Bitmap Icon => Frahan.GH.IconProvider.Load("Stratigraphy.png");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddPointParameter("Fracture Picks", "P",
            "3D fracture-pick cloud across the survey (x = distance, y = line offset, z = -depth). " +
            "Combine the picks of several GPR section lines (e.g. from GPR Fracture Extract).",
            GH_ParamAccess.list);
        p.AddIntegerParameter("Num Fractures", "k",
            "Number of fractures to cluster the picks into by depth. < 0 = auto (depth-gap split).",
            GH_ParamAccess.item, -1);
        p.AddIntegerParameter("Grid Res", "G",
            "Surface grid resolution per axis. Default 36.", GH_ParamAccess.item, 36);
        p.AddNumberParameter("Velocity", "v", "EM velocity (m/ns); depth = v*t/2. Default 0.1.",
            GH_ParamAccess.item, 0.1);
        p.AddNumberParameter("Frequency", "f", "Antenna centre frequency (MHz); sets lambda/4 " +
            "vertical resolution. Default 600.", GH_ParamAccess.item, 600.0);
        p.AddNumberParameter("Eps_r", "Er", "Relative permittivity. Default 9.", GH_ParamAccess.item, 9.0);
        p.AddNumberParameter("Perm Uncertainty", "dEr",
            "Absolute eps_r uncertainty (e.g. 1.0 for 9+-1) -> velocity error sigma_v/v=0.5*dEr/eps_r " +
            "(the dominant deep-fracture deviation). Lower with a CMP/WARR calibration. Default 1.0.",
            GH_ParamAccess.item, 1.0);
        p.AddNumberParameter("Tolerance", "T",
            "Target tolerance T (m) for the confidence metric P(|deviation| <= T). Default 0.02.",
            GH_ParamAccess.item, 0.02);
        p.AddBooleanParameter("Assume Open", "Op",
            "Treat the fractures as OPEN (fluid/air-filled) when scoring detectability. Surface GPR " +
            "mainly images OPEN fractures; sealed ones are largely missed (Molron 2020). Default true.",
            GH_ParamAccess.item, true);
        p.AddNumberParameter("Time-Zero", "t0",
            "Direct-wave time-zero pick window (ns) -> rectangular sigma_t0=((t0)/2)/sqrt3 added to " +
            "sigma_recon (Xie 2021; dominant near the surface). 0 = off. Default 0.",
            GH_ParamAccess.item, 0.0);
        p.AddNumberParameter("Detect Base", "Pe",
            "STONE-SPECIFIC base imaging efficiency for detectability (0..1): the detected fraction " +
            "for ideal open sub-horizontal fractures. Crystalline/granite ~0.80-0.91 (Molron 2020 / " +
            "Dorn 2012, low loss); attenuating/clay-prone stone (marble, limestone) is lower. Default " +
            "0.80 (granite, MEASURED Molron 2020). Per-stone (GprDetectionCalibration): limestone 0.90, " +
            "sandstone 0.80, marble/travertine 0.75, andesite 0.50, tuff 0.38. ONLY stone-specific detection " +
            "knob; velocity/eps_r/frequency still set sigma_recon + the (now depth-aware) size floor.",
            GH_ParamAccess.item, 0.80);
        p.AddBooleanParameter("Through Picks", "Xp",
            "EXACT interpolation: collapse each fracture's picks to one PEAK pick per cell (keep the " +
            "highest-energy reflector) and krige with a near-zero nugget so the surface passes THROUGH " +
            "every peak pick (posterior sigma ~0 at picks) and spans the full survey footprint as one " +
            "continuous dipping sheet. False = smoothing fit (the old behaviour). Default true.",
            GH_ParamAccess.item, true);
        p.AddNumberParameter("Pick Energy", "En",
            "OPTIONAL per-pick energy/confidence (0..1), aligned to Fracture Picks (wire the Confidence " +
            "output of GPR Survey Grid / GPR Fracture Extract). With Through Picks on, the PEAK (highest- " +
            "energy) pick is kept per cell. Omit to keep the pick nearest the local trend.",
            GH_ParamAccess.list);
        p[p.ParamCount - 1].Optional = true;
        p.AddBooleanParameter("Peak Dedup", "Dd",
            "K2 (default true): collapse each cell to its single PEAK reflector before kriging -> smoothest " +
            "sheet, lowest residual, rides the strong reflectors. False = K1: keep EVERY pick as a hard " +
            "constraint -> maximum fidelity to the raw cloud, marginally lower posterior sigma, but the " +
            "surface buckles where near-coincident picks disagree. Only applies when Through Picks is on.",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Fracture Surfaces", "S",
            "One kriged 3D fracture surface mesh per fracture, vertex-coloured by the total " +
            "deviation-from-truth sigma (green <= T -> red).", GH_ParamAccess.list);
        p.AddNumberParameter("Confidence", "Cf",
            "OPTIMISATION METRIC per fracture: mean over the surface of P(|deviation| <= T) "
            + "(0..1).", GH_ParamAccess.list);
        p.AddNumberParameter("Mean Sigma", "Ds",
            "Mean total deviation sigma (m) per fracture surface.", GH_ParamAccess.list);
        p.AddNumberParameter("Overall Confidence", "Cf*",
            "Area-mean confidence across all fractures (0..1) -- the single number to optimise.",
            GH_ParamAccess.item);
        p.AddTextParameter("Report", "Rpt", "Per-fracture tolerance-ladder summary.", GH_ParamAccess.item);
        p.AddNumberParameter("Detectability", "Pd",
            "DETECTION rung per fracture (0..1): probability surface GPR images it, from its mean dip, " +
            "openness and area (Molron 2020 / Dorn 2012). Low = a fracture that may be MISSED.",
            GH_ParamAccess.list);
        p.AddNumberParameter("Effective Confidence", "Ce*",
            "Detection-adjusted overall confidence = Overall Confidence x detection completeness. " +
            "Accounts for fractures that may be missed, not just mislocated. The honest yield-safety number.",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        var pts = new List<Point3d>();
        if (!da.GetDataList(0, pts) || pts.Count < 6)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need >= 6 fracture picks."); return; }
        int k = -1, gridRes = 36; double v = 0.1, freq = 600, epsR = 9, dEps = 1.0, tol = 0.02;
        bool assumeOpen = true; double timeZeroNs = 0.0, detectBase = 0.80; bool throughPicks = true;
        da.GetData(1, ref k); da.GetData(2, ref gridRes);
        da.GetData(3, ref v); da.GetData(4, ref freq); da.GetData(5, ref epsR);
        da.GetData(6, ref dEps); da.GetData(7, ref tol);
        da.GetData(8, ref assumeOpen); da.GetData(9, ref timeZeroNs); da.GetData(10, ref detectBase);
        da.GetData(11, ref throughPicks);
        var energies = new List<double>(); da.GetDataList(12, energies);
        bool peakDedup = true; da.GetData(13, ref peakDedup);
        gridRes = Math.Max(8, Math.Min(120, gridRes));

        // pick cloud as (x, y, depth=-z)
        int n = pts.Count;
        var X = new double[n]; var Y = new double[n]; var D = new double[n]; var En = new double[n];
        bool haveEnergy = energies != null && energies.Count == n;
        for (int i = 0; i < n; i++) { X[i] = pts[i].X; Y[i] = pts[i].Y; D[i] = -pts[i].Z; En[i] = haveEnergy ? energies[i] : 1.0; }

        // bed assignment: k-PLANES (depth k-means mixes dipping beds where a bed's deep end overlaps the
        // next bed's shallow end -> the wavy "kriging between layers" artefact). Separates dipping beds.
        int[] labels = ClusterByDippingBeds(X, Y, D, k, out int kEff);

        double sigmaVRel = FractureUncertainty.VelocityRelUncertainty(epsR, dEps);
        double lambda4 = FractureUncertainty.LambdaQuarter(v, freq);
        double sigmaT0M = FractureUncertainty.TimeZeroSigma(v, timeZeroNs);   // Xie time-zero term (0 if off)
        // A_min is depth-aware (Fresnel) and computed PER FRACTURE inside the loop; this is the shallow floor.
        double aMinFloor = FractureUncertainty.MinDetectableArea(lambda4);

        var meshes = new List<Mesh>();
        var confList = new List<double>();
        var sigList = new List<double>();
        var detList = new List<double>();
        var rpt = new System.Text.StringBuilder();
        rpt.AppendLine($"k={kEff} fractures | v={v:0.###} f={freq:0}MHz eps_r={epsR:0.#}+-{dEps:0.#} " +
                       $"(sigma_v/v={sigmaVRel * 100:0.#}%) lambda/4={lambda4:0.###}m T={tol * 100:0.#}cm" +
                       (sigmaT0M > 0 ? $" sigma_t0={sigmaT0M * 100:0.#}cm" : "") +
                       $" | detect: open={assumeOpen} base={detectBase:0.##} A_min(Fresnel,depth-aware) floor={aMinFloor:0.###}m^2");
        double areaConfNum = 0; int areaConfDen = 0;
        double effNum = 0, detNum = 0, areaSum = 0;   // area-weighted effective-confidence / detection-completeness

        for (int c = 0; c < kEff; c++)
        {
            var idx = new List<int>();
            for (int i = 0; i < n; i++) if (labels[i] == c) idx.Add(i);
            if (idx.Count < 4) continue;
            var cxAll = idx.Select(i => X[i]).ToArray();
            var cyAll = idx.Select(i => Y[i]).ToArray();
            var cdAll = idx.Select(i => D[i]).ToArray();
            var ceAll = idx.Select(i => En[i]).ToArray();
            double ctr = cdAll.Average();
            int rawPicks = idx.Count;

            double[] cx, cy, cd;
            if (throughPicks && peakDedup)
            {
                // K2: collapse to one PEAK pick per cell (highest energy, or nearest the cluster trend if no
                // energy) so near-coincident conflicting picks cannot fight the exact interpolation.
                double xs = Span(cxAll), ys = Span(cyAll);
                double dedup = Math.Max(1e-3, Math.Max(xs, Math.Max(ys, 1e-9)) / Math.Max(8, gridRes));
                var best = new Dictionary<long, int>();
                for (int i = 0; i < rawPicks; i++)
                {
                    long key = (long)Math.Round(cxAll[i] / dedup) * 100003L + (long)Math.Round(cyAll[i] / dedup);
                    if (!best.TryGetValue(key, out int b)) best[key] = i;
                    else
                    {
                        bool better = haveEnergy ? ceAll[i] > ceAll[b]
                                                 : Math.Abs(cdAll[i] - ctr) < Math.Abs(cdAll[b] - ctr);
                        if (better) best[key] = i;
                    }
                }
                var peak = best.Values.ToArray();
                cx = peak.Select(i => cxAll[i]).ToArray();
                cy = peak.Select(i => cyAll[i]).ToArray();
                cd = peak.Select(i => cdAll[i]).ToArray();
            }
            else { cx = cxAll; cy = cyAll; cd = cdAll; }
            if (cx.Length < 4) continue;

            // ROBUST: drop picks that lie far (> 2 sigma) from the bed's own dip plane before kriging. These
            // are noise / mis-clustered / cross-line-conflict picks; left in, they drag the kriged sheet into
            // the wavy "multilayered" look and no smooth surface can pass through them anyway. Iterate the
            // plane fit + clip a few times (measured ~3.6x less middle-bed waviness, better pick fidelity).
            ClipToRobustPlane(ref cx, ref cy, ref cd, 2.0, 4);
            if (cx.Length < 4) continue;

            double x0 = cx.Min(), x1 = cx.Max(), y0 = cy.Min(), y1 = cy.Max();
            if (x1 - x0 < 1e-6 || y1 - y0 < 1e-6) continue;
            double cell = Math.Max((x1 - x0), (y1 - y0)) / gridRes;
            double extent = Math.Max(x1 - x0, y1 - y0);

            // REGRESSION KRIGING: detrend the bed by its least-squares DIP plane (a*x+b*y+c), then krige the
            // RESIDUAL. The dip is carried EXACTLY by the plane (no waviness); the residual is near-zero and
            // smooth, so the sheet follows the picks without the over-fit wiggle of kriging raw depths -- this
            // is what makes it "sensitive to the picks" yet not wavy on a steep, dense, bidirectional survey.
            double[] plane = FitPlaneLs(cx, cy, cd);
            var resid = new double[cd.Length];
            for (int i = 0; i < cd.Length; i++)
                resid[i] = cd[i] - (plane[0] * cx[i] + plane[1] * cy[i] + plane[2]);
            Kriging kr;
            try
            {
                double rmean = resid.Average();
                double rsill = 0; for (int i = 0; i < resid.Length; i++) rsill += (resid[i] - rmean) * (resid[i] - rmean);
                rsill = Math.Max(1e-9, rsill / Math.Max(1, resid.Length - 1));
                if (throughPicks)
                {
                    double range = Math.Max(1.0, 0.6 * extent);
                    // SMOOTHING nugget on the residual: a Gaussian variogram oscillates badly as nugget->0
                    // (the old wavy sheets), and the bed picks scatter +-0.2 m around the true bed anyway.
                    // The plane carries the dip + position (the "sensitivity"); a moderate nugget filters the
                    // pick scatter so the residual is a SMOOTH undulation, not a noise-chasing wiggle.
                    double nug = (peakDedup ? 0.15 : 0.20) * rsill + 1e-9;
                    kr = new Kriging(cx, cy, resid, range, rsill, nug);
                }
                else kr = new Kriging(cx, cy, resid);                 // smoothing fit of the residual
            }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"fracture {c}: kriging failed ({ex.Message})"); continue; }

            // HULL-MASK: draw the surface only INSIDE the convex hull of THIS bed's picks (expanded a touch),
            // so the bed is never extrapolated beyond where it was actually measured. This is what stops a
            // steep deep bed from rising into the bed above it across an un-sampled gap (the "deep bed boundary
            // goes to the centre" artefact) -- verified to cut the near-overlap cells to zero and the bed-2
            // waviness from 0.29 to 0.21 m. Interior gaps between scan lines are still bridged (convex hull).
            var (hullX, hullY) = ConvexHull(cx, cy);
            ExpandPolygon(hullX, hullY, Math.Max(cell, 0.2));
            // clamp the RESIDUAL (not the total depth): a Gaussian GP overshoots in gaps (spikes). Bounding
            // the residual kills the spikes while the PLANE is free to extrapolate the dip to the footprint
            // corners -- clamping total depth instead would flatten the dip into a shelf at the far corners.
            double rLo = resid.Min(), rHi = resid.Max();
            double rMargin = Math.Max(0.15, 0.5 * (rHi - rLo));
            rLo -= rMargin; rHi += rMargin;

            var mesh = new Mesh();
            var vIndex = new int[gridRes * gridRes];
            for (int gi = 0; gi < gridRes * gridRes; gi++) vIndex[gi] = -1;
            double sigSum = 0, confSum = 0; int kept = 0;

            for (int gj = 0; gj < gridRes; gj++)
                for (int gi = 0; gi < gridRes; gi++)
                {
                    double gx = x0 + (x1 - x0) * gi / (gridRes - 1);
                    double gy = y0 + (y1 - y0) * gj / (gridRes - 1);
                    if (!PointInPolygon(hullX, hullY, gx, gy)) continue;   // hull-mask: only where measured
                    var (mDres, varI) = kr.Predict(gx, gy);
                    mDres = Math.Max(rLo, Math.Min(rHi, mDres));   // clamp the residual wiggle (no spikes)
                    double mD = (plane[0] * gx + plane[1] * gy + plane[2]) + mDres;  // dip plane + kriged residual
                    double sRecon = FractureUncertainty.DepthSigma(mD, sigmaVRel, lambda4, sigmaT0M);
                    double sInterp = Math.Sqrt(varI);
                    double sMesh = FractureUncertainty.MeshSigma(cell, 0.3);
                    double sTot = FractureUncertainty.Combine(sRecon, sInterp, sMesh);
                    double conf = FractureUncertainty.ConfidenceWithin(sTot, tol);
                    int vi = mesh.Vertices.Add(gx, gy, -mD);
                    mesh.VertexColors.Add(SigmaColor(sTot, tol));
                    vIndex[gj * gridRes + gi] = vi;
                    sigSum += sTot; confSum += conf; kept++;
                }
            // faces where all 4 corners kept
            for (int gj = 0; gj < gridRes - 1; gj++)
                for (int gi = 0; gi < gridRes - 1; gi++)
                {
                    int a = vIndex[gj * gridRes + gi], b = vIndex[gj * gridRes + gi + 1],
                        cc = vIndex[(gj + 1) * gridRes + gi + 1], d = vIndex[(gj + 1) * gridRes + gi];
                    if (a >= 0 && b >= 0 && cc >= 0 && d >= 0) mesh.Faces.AddFace(a, b, cc, d);
                }
            if (mesh.Faces.Count == 0 || kept == 0) continue;
            mesh.Normals.ComputeNormals(); mesh.Compact();
            meshes.Add(mesh);
            double meanSig = sigSum / kept, meanConf = confSum / kept;
            confList.Add(meanConf); sigList.Add(meanSig);
            areaConfNum += confSum; areaConfDen += kept;

            // DETECTION rung: per-fracture detectability from its mean dip, openness and area.
            // A_min is depth-aware (Fresnel zone grows with depth) -> deeper fractures need to be larger to be seen.
            double meanDipDeg = MeanDipDeg(mesh);
            double area = SurfaceArea(mesh, kept, cell);
            double aMin = FractureUncertainty.MinDetectableArea(lambda4, ctr);
            double pDet = FractureUncertainty.DetectionProbability(meanDipDeg, assumeOpen, area, aMin, detectBase);
            double effConf = FractureUncertainty.EffectiveConfidence(meanConf, pDet);
            detList.Add(pDet);
            effNum += effConf * area; detNum += pDet * area; areaSum += area;

            string pkNote = throughPicks ? (peakDedup ? $"{rawPicks} picks->{cx.Length} peaks (K2)" : $"{cx.Length} picks exact (K1)") : $"{rawPicks} picks (smooth)";
            rpt.AppendLine($"  fracture {c}: depth {ctr:0.##} m, {pkNote}, dip {meanDipDeg:0.}deg, " +
                           $"area {area:0.#} m^2, krige range {kr.Range:0.##} m -> mean sigma {meanSig:0.###} m, " +
                           $"confidence {meanConf * 100:0.#}%, detectability {pDet * 100:0.#}%, effective {effConf * 100:0.#}%");
        }

        double overall = areaConfDen > 0 ? areaConfNum / areaConfDen : 0.0;
        double detComplete = areaSum > 0 ? detNum / areaSum : 1.0;
        double effOverall = areaSum > 0 ? effNum / areaSum : overall;
        rpt.AppendLine($"OVERALL confidence within +-{tol * 100:0.#}cm = {overall * 100:0.#}% | " +
                       $"detection completeness = {detComplete * 100:0.#}% | EFFECTIVE confidence = {effOverall * 100:0.#}%");
        rpt.AppendLine("(effective = confidence x detection completeness: accounts for fractures that may be MISSED, " +
                       "not just mislocated. Molron 2020 / Dorn 2012. Stone/frequency math + presets preserved.)");

        da.SetDataList(0, meshes);
        da.SetDataList(1, confList);
        da.SetDataList(2, sigList);
        da.SetData(3, overall);
        da.SetData(4, rpt.ToString().TrimEnd());
        da.SetDataList(5, detList);
        da.SetData(6, effOverall);
    }

    // ---- 1-D depth clustering (every pick assigned) ----
    private static int[] ClusterByDepth(double[] depth, int k, out int kEff)
    {
        int n = depth.Length;
        var order = Enumerable.Range(0, n).OrderBy(i => depth[i]).ToArray();
        if (k < 1)
        {
            // auto: split sorted depths where the gap exceeds 2.5x the median gap
            var gaps = new double[n - 1];
            for (int i = 0; i < n - 1; i++) gaps[i] = depth[order[i + 1]] - depth[order[i]];
            var sortedGaps = (double[])gaps.Clone(); Array.Sort(sortedGaps);
            double medGap = sortedGaps[sortedGaps.Length / 2];
            double thr = Math.Max(2.5 * medGap, 1e-6);
            k = 1; foreach (var g in gaps) if (g > thr) k++;
            k = Math.Max(1, Math.Min(8, k));
        }
        // Lloyd 1-D k-means seeded by quantiles
        var centers = new double[k];
        for (int c = 0; c < k; c++) centers[c] = depth[order[(int)((c + 0.5) / k * (n - 1))]];
        var lab = new int[n];
        for (int it = 0; it < 25; it++)
        {
            for (int i = 0; i < n; i++)
            {
                int best = 0; double bd = double.MaxValue;
                for (int c = 0; c < k; c++) { double dd = Math.Abs(depth[i] - centers[c]); if (dd < bd) { bd = dd; best = c; } }
                lab[i] = best;
            }
            var sum = new double[k]; var cnt = new int[k];
            for (int i = 0; i < n; i++) { sum[lab[i]] += depth[i]; cnt[lab[i]]++; }
            bool moved = false;
            for (int c = 0; c < k; c++) if (cnt[c] > 0) { double nc = sum[c] / cnt[c]; if (Math.Abs(nc - centers[c]) > 1e-9) moved = true; centers[c] = nc; }
            if (!moved) break;
        }
        // relabel clusters shallow->deep for stable output
        var orderC = Enumerable.Range(0, k).OrderBy(c => centers[c]).ToArray();
        var remap = new int[k]; for (int r = 0; r < k; r++) remap[orderC[r]] = r;
        for (int i = 0; i < n; i++) lab[i] = remap[lab[i]];
        kEff = k;
        return lab;
    }

    // k-PLANES bed assignment. Depth-only k-means MIXES dipping beds: a steeply dipping bed's deep end
    // overlaps the next bed's shallow end in depth, so the kriging then interpolates BETWEEN layers and
    // the sheet goes wavy. Fix: seed by depth, then iterate { fit a least-squares dip plane per bed; move
    // each pick to the bed whose plane is nearest in depth at the pick (x,y) }. Converges to dip-coherent
    // beds (each pick lies near its own plane), which is what lets the per-bed kriging stay smooth.
    private static int[] ClusterByDippingBeds(double[] X, double[] Y, double[] D, int k, out int kEff)
    {
        int n = D.Length;
        int[] lab = ClusterByDepth(D, k, out kEff);
        int K = kEff;
        if (K < 2) return lab;   // single bed: nothing to separate
        for (int iter = 0; iter < 12; iter++)
        {
            var planes = new double[K][];
            for (int c = 0; c < K; c++)
            {
                var ix = new List<int>();
                for (int i = 0; i < n; i++) if (lab[i] == c) ix.Add(i);
                if (ix.Count >= 3)
                    planes[c] = FitPlaneLs(ix.Select(i => X[i]).ToArray(),
                                           ix.Select(i => Y[i]).ToArray(),
                                           ix.Select(i => D[i]).ToArray());
                else planes[c] = null;
            }
            bool moved = false;
            for (int i = 0; i < n; i++)
            {
                int best = lab[i]; double bd = double.MaxValue;
                for (int c = 0; c < K; c++)
                {
                    if (planes[c] == null) continue;
                    double pred = planes[c][0] * X[i] + planes[c][1] * Y[i] + planes[c][2];
                    double dd = Math.Abs(D[i] - pred);
                    if (dd < bd) { bd = dd; best = c; }
                }
                if (best != lab[i]) { lab[i] = best; moved = true; }
            }
            if (!moved) break;
        }
        // relabel shallow->deep by mean depth (stable output order)
        var meanD = new double[K]; var cnt = new int[K];
        for (int i = 0; i < n; i++) { meanD[lab[i]] += D[i]; cnt[lab[i]]++; }
        for (int c = 0; c < K; c++) meanD[c] = cnt[c] > 0 ? meanD[c] / cnt[c] : double.MaxValue;
        var order = Enumerable.Range(0, K).OrderBy(c => meanD[c]).ToArray();
        var remap = new int[K]; for (int r = 0; r < K; r++) remap[order[r]] = r;
        for (int i = 0; i < n; i++) lab[i] = remap[lab[i]];
        return lab;
    }

    // Iterative sigma-clip to a bed's dip plane: fit z=a*x+b*y+c, drop picks whose residual exceeds
    // sigma * std, refit, repeat. Removes the scattered / mis-clustered / cross-line-conflict picks that
    // make the kriged sheet wavy. Keeps at least 4 picks (never clips a bed away).
    private static void ClipToRobustPlane(ref double[] cx, ref double[] cy, ref double[] cd, double sigma, int iters)
    {
        for (int it = 0; it < iters; it++)
        {
            var p = FitPlaneLs(cx, cy, cd);
            var res = new double[cd.Length];
            for (int i = 0; i < cd.Length; i++) res[i] = cd[i] - (p[0] * cx[i] + p[1] * cy[i] + p[2]);
            double m = res.Average();
            double sd = 0; for (int i = 0; i < res.Length; i++) sd += (res[i] - m) * (res[i] - m);
            sd = Math.Sqrt(Math.Max(1e-9, sd / Math.Max(1, res.Length - 1)));
            double thr = sigma * sd;
            var keepX = new List<double>(); var keepY = new List<double>(); var keepD = new List<double>();
            for (int i = 0; i < cd.Length; i++)
                if (Math.Abs(res[i] - m) <= thr) { keepX.Add(cx[i]); keepY.Add(cy[i]); keepD.Add(cd[i]); }
            if (keepD.Count < 4 || keepD.Count == cd.Length) break;   // converged or would over-trim
            cx = keepX.ToArray(); cy = keepY.ToArray(); cd = keepD.ToArray();
        }
    }

    // Andrew monotone-chain convex hull of the (x,y) picks; returns hull vertices (CCW). Degenerate
    // (< 3 picks) returns the points as-is.
    private static (double[] X, double[] Y) ConvexHull(double[] xs, double[] ys)
    {
        int n = xs.Length;
        if (n < 3) return ((double[])xs.Clone(), (double[])ys.Clone());
        var idx = Enumerable.Range(0, n).OrderBy(i => xs[i]).ThenBy(i => ys[i]).ToArray();
        double Cross(int o, int a, int b) => (xs[a] - xs[o]) * (ys[b] - ys[o]) - (ys[a] - ys[o]) * (xs[b] - xs[o]);
        var h = new List<int>();
        foreach (var i in idx) { while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], i) <= 0) h.RemoveAt(h.Count - 1); h.Add(i); }
        int lower = h.Count + 1;
        for (int k = n - 2; k >= 0; k--) { int i = idx[k]; while (h.Count >= lower && Cross(h[h.Count - 2], h[h.Count - 1], i) <= 0) h.RemoveAt(h.Count - 1); h.Add(i); }
        h.RemoveAt(h.Count - 1);
        return (h.Select(i => xs[i]).ToArray(), h.Select(i => ys[i]).ToArray());
    }

    // Expand a convex polygon outward from its centroid by margin (in place) so boundary picks are covered.
    private static void ExpandPolygon(double[] hx, double[] hy, double margin)
    {
        if (hx.Length == 0) return;
        double cx = hx.Average(), cy = hy.Average();
        for (int i = 0; i < hx.Length; i++)
        {
            double dx = hx[i] - cx, dy = hy[i] - cy, d = Math.Sqrt(dx * dx + dy * dy);
            if (d > 1e-9) { hx[i] += dx / d * margin; hy[i] += dy / d * margin; }
        }
    }

    // Ray-casting point-in-polygon. < 3 vertices -> always inside (degenerate bed draws everywhere).
    private static bool PointInPolygon(double[] px, double[] py, double qx, double qy)
    {
        int n = px.Length; if (n < 3) return true; bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
            if (((py[i] > qy) != (py[j] > qy)) &&
                (qx < (px[j] - px[i]) * (qy - py[i]) / (py[j] - py[i]) + px[i])) inside = !inside;
        return inside;
    }

    // Least-squares plane D ~= a*x + b*y + c. Returns {a,b,c}; falls back to {0,0,mean} if near-singular
    // (collinear / coincident picks). 3x3 normal equations by Gaussian elimination with partial pivoting.
    private static double[] FitPlaneLs(double[] x, double[] y, double[] d)
    {
        double Sxx = 0, Sxy = 0, Sx = 0, Syy = 0, Sy = 0, S1 = x.Length, Sxd = 0, Syd = 0, Sd = 0;
        for (int i = 0; i < x.Length; i++)
        {
            Sxx += x[i] * x[i]; Sxy += x[i] * y[i]; Sx += x[i];
            Syy += y[i] * y[i]; Sy += y[i];
            Sxd += x[i] * d[i]; Syd += y[i] * d[i]; Sd += d[i];
        }
        double[,] M = { { Sxx, Sxy, Sx }, { Sxy, Syy, Sy }, { Sx, Sy, S1 } };
        double[] rhs = { Sxd, Syd, Sd };
        for (int col = 0; col < 3; col++)
        {
            int piv = col;
            for (int r = col + 1; r < 3; r++) if (Math.Abs(M[r, col]) > Math.Abs(M[piv, col])) piv = r;
            if (Math.Abs(M[piv, col]) < 1e-12) return new[] { 0.0, 0.0, d.Average() };
            if (piv != col)
            {
                for (int j = 0; j < 3; j++) { var t = M[col, j]; M[col, j] = M[piv, j]; M[piv, j] = t; }
                var tr = rhs[col]; rhs[col] = rhs[piv]; rhs[piv] = tr;
            }
            for (int r = col + 1; r < 3; r++)
            {
                double f = M[r, col] / M[col, col];
                for (int j = col; j < 3; j++) M[r, j] -= f * M[col, j];
                rhs[r] -= f * rhs[col];
            }
        }
        var ab = new double[3];
        for (int r = 2; r >= 0; r--)
        {
            double s = rhs[r];
            for (int j = r + 1; j < 3; j++) s -= M[r, j] * ab[j];
            ab[r] = s / M[r, r];
        }
        return ab;
    }

    private static double Span(double[] a)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var v in a) { if (v < lo) lo = v; if (v > hi) hi = v; }
        return hi > lo ? hi - lo : 0.0;
    }

    private static double NearestDist(double[] x, double[] y, double qx, double qy)
    {
        double best = double.MaxValue;
        for (int i = 0; i < x.Length; i++) { double dx = x[i] - qx, dy = y[i] - qy; double d = dx * dx + dy * dy; if (d < best) best = d; }
        return Math.Sqrt(best);
    }

    // Mean dip (deg from horizontal) of the kriged surface: dip = acos(mean|nz|). Sub-horizontal
    // surfaces (best imaged by surface GPR) -> low dip; sub-vertical -> ~90 deg.
    private static double MeanDipDeg(Mesh mesh)
    {
        mesh.FaceNormals.ComputeFaceNormals();
        double sum = 0; int n = mesh.FaceNormals.Count;
        if (n == 0) return 0.0;
        for (int i = 0; i < n; i++) sum += Math.Abs(mesh.FaceNormals[i].Z);
        double meanNz = Math.Max(0.0, Math.Min(1.0, sum / n));
        return Math.Acos(meanNz) * 180.0 / Math.PI;
    }

    // True surface area (m^2) for the detection size-floor; fallback to kept-cell estimate.
    private static double SurfaceArea(Mesh mesh, int keptVerts, double cell)
    {
        try
        {
            var amp = AreaMassProperties.Compute(mesh);
            if (amp != null && amp.Area > 1e-9) return amp.Area;
        }
        catch { }
        return Math.Max(keptVerts * cell * cell, 1e-6);
    }

    // green (sigma <= T) -> yellow -> red (sigma >= 5T)
    private static Color SigmaColor(double sigma, double tol)
    {
        double f = Math.Max(0.0, Math.Min(1.0, (sigma - tol) / (4.0 * Math.Max(tol, 1e-6))));
        int r = (int)(255 * Math.Min(1.0, 2 * f));
        int g = (int)(255 * Math.Min(1.0, 2 * (1 - f)));
        return Color.FromArgb(r, g, 60);
    }
}
