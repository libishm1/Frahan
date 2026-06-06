#nullable disable
using System;

namespace Frahan.Masonry.Quarry.Processing;

// =============================================================================
// FractureUncertainty -- the GPR -> fracture -> mesh TOLERANCE LADDER.
//
// How far the reconstructed fracture deviates from the REAL fracture, propagated
// in quadrature through the pipeline stages, as a per-location 1-sigma position
// uncertainty (metres, dominated by the depth axis):
//
//   sigma_total = sqrt( sigma_recon^2 + sigma_interp^2 + sigma_mesh^2 )
//
//   sigma_recon  GPR time->depth: depth = v*t/2, v = c/sqrt(eps_r).
//                = sqrt( (depth * sigma_v/v)^2 + (lambda/4)^2 )
//                velocity error (grows with depth) + the lambda/4 resolution floor.
//   sigma_interp fracture reconstruction: uncertainty of the surface BETWEEN scan
//                lines. The kriging/GP posterior std (~0 at picks, grows in gaps);
//                supplied per-location by the surface stage. 0 for a single section.
//   sigma_mesh   meshing: chord sagitta of the triangulation, ~ (h^2/8)*kappa.
//
// The optimisation TARGET is the CONFIDENCE: the probability the fracture lies
// within a tolerance T of the truth, assuming a Gaussian deviation:
//   confidence(x) = erf( T / (sigma_total(x) * sqrt(2)) ),  averaged over the surface.
//
// Lower sigma_total by: calibrating velocity (CMP/WARR -> shrinks sigma_recon at
// depth), denser scan lines (shrinks sigma_interp), higher frequency (shrinks
// lambda/4), finer mesh (shrinks sigma_mesh). The ladder quantifies each trade-off.
//
// Rhino-free. Anchors: gpr_math_derivations.md; validated in gpr_extraction/
// fracture_uncertainty.py against the grid3 marble data.
// =============================================================================

public static class FractureUncertainty
{
    private const double C_M_PER_NS = 0.299792458;   // speed of light, m/ns

    /// <summary>Vertical resolution lambda/4 (m). v in m/ns, frequency in MHz.</summary>
    public static double LambdaQuarter(double velocityMPerNs, double frequencyMhz)
    {
        if (frequencyMhz <= 0) return 0.0;
        double freqGhz = frequencyMhz / 1000.0;       // MHz -> GHz
        return velocityMPerNs / (4.0 * freqGhz);      // (m/ns)/(1/ns) = m
    }

    /// <summary>Relative velocity uncertainty sigma_v/v from a relative permittivity
    /// uncertainty. v ~ 1/sqrt(eps_r)  =>  sigma_v/v = 0.5 * sigma_eps/eps_r.</summary>
    public static double VelocityRelUncertainty(double epsR, double epsRAbsUncertainty)
    {
        if (epsR <= 0) return 0.0;
        return 0.5 * Math.Abs(epsRAbsUncertainty) / epsR;
    }

    /// <summary>Time-zero contribution to the depth 1-sigma (m). The direct-wave first-break to
    /// first-apex window is a rectangular pick ambiguity; it maps to depth as v*sigma_t0/2.
    /// Xie, Lai &amp; Derobert 2021 (Measurement 168:108330) show this term DOMINATES the GPR depth
    /// budget at shallow depth, where the velocity term is small. v in m/ns, sigmaT0 in ns.</summary>
    public static double TimeZeroSigma(double velocityMPerNs, double sigmaT0Ns)
        => velocityMPerNs * Math.Abs(sigmaT0Ns) / 2.0;

    /// <summary>sigma_t0 (ns) from the direct-wave first-break (tBreak) to first-apex (tApex)
    /// window as a rectangular distribution: ((tApex - tBreak)/2)/sqrt(3). Xie 2021 Eq.3-4.</summary>
    public static double RectTimeZeroSigma(double tBreakNs, double tApexNs)
        => Math.Abs(tApexNs - tBreakNs) / 2.0 / Math.Sqrt(3.0);

    /// <summary>Stage 1 -- GPR depth reconstruction 1-sigma (m) at a given depth (EVOLVED 2026-06-05).
    /// sigma_recon = sqrt( (depth*sigma_v/v)^2 + (lambda/4)^2 + (sigma_timezero)^2 ).
    /// The velocity term grows with depth and LEADS at quarry depth (Porsani 2006 +-8.5-9.5% at 25 m);
    /// lambda/4 is a vertical-resolution FLOOR, not the dominant term (Xie 2021); the time-zero term
    /// (pass FractureUncertainty.TimeZeroSigma) leads near the surface. sigmaTimeZeroM = 0 reproduces
    /// the original two-term form.</summary>
    public static double DepthSigma(double depthM, double sigmaVRel, double lambdaQuarterM, double sigmaTimeZeroM)
    {
        double vel = depthM * sigmaVRel;
        return Math.Sqrt(vel * vel + lambdaQuarterM * lambdaQuarterM + sigmaTimeZeroM * sigmaTimeZeroM);
    }

    /// <summary>Stage 1 (original two-term form): sqrt((depth*sigma_v/v)^2 + (lambda/4)^2).</summary>
    public static double DepthSigma(double depthM, double sigmaVRel, double lambdaQuarterM)
        => DepthSigma(depthM, sigmaVRel, lambdaQuarterM, 0.0);

    /// <summary>Stage 3 -- meshing 1-sigma (m): chord sagitta for edge h, curvature kappa.</summary>
    public static double MeshSigma(double edgeLengthM, double curvaturePerM)
        => edgeLengthM * edgeLengthM / 8.0 * Math.Abs(curvaturePerM);

    /// <summary>Combine independent 1-sigma contributions in quadrature.</summary>
    public static double Combine(params double[] sigmas)
    {
        double s = 0.0;
        foreach (var x in sigmas) s += x * x;
        return Math.Sqrt(s);
    }

    /// <summary>Probability |deviation| &lt;= tol for a zero-mean Gaussian of std sigma.</summary>
    public static double ConfidenceWithin(double sigmaM, double tolM)
    {
        if (sigmaM <= 0) return 1.0;
        return Erf(tolM / (sigmaM * Math.Sqrt(2.0)));
    }

    // =========================================================================================
    // DETECTION rung (EVOLVED 2026-06-05) -- the rung the position ladder omits. A fracture's
    // position sigma only matters if the fracture is SEEN; a MISSED fracture has no sigma but is
    // the real yield risk. Grounded in Molron et al. 2020 (Aspo: surface GPR images ~80% of OPEN
    // fractures of area 1-10 m^2 dipping <25 deg; SEALED and SUB-VERTICAL fractures largely missed;
    // position accuracy depth 0.1 m / dip 6 deg) and Dorn et al. 2012 (~91% of transmissive granite
    // fractures correlated; position error up to 4%).
    // =========================================================================================

    /// <summary>Minimum detectable fracture AREA (m^2), EVOLVED 2026-06-05 to be depth-aware.
    /// A fracture must fill about the first Fresnel zone to be imaged, and that footprint GROWS
    /// WITH DEPTH: A_min ~ pi * r_F^2 with r_F = sqrt(lambda * depth / 2), i.e. A_min ~ pi*lambda*depth/2
    /// (lambda = 4 * lambdaQuarterM). A shallow resolution floor (resolutionCells * lambda/4)^2 applies
    /// when depth is unknown/zero. This matches Molron 2020 (open fractures of area ~1-10 m^2 imaged at
    /// 160-750 MHz, A_min ~1 m^2 at a few m); the old depth-free (3*lambda/4)^2 under-estimated A_min.</summary>
    public static double MinDetectableArea(double lambdaQuarterM, double depthM = 0.0, double resolutionCells = 3.0)
    {
        double lq = Math.Max(0.0, lambdaQuarterM);
        double floor = (resolutionCells * lq) * (resolutionCells * lq);
        if (depthM <= 0.0) return floor;
        // Fresnel-FRACTION size floor, CALIBRATED so the 1-10 m^2 open sub-horizontal population
        // reproduces Molron 2020's ~80% detection at 160-750 MHz / ~9 m (A_min ~1 m^2 there). A
        // fracture smaller than the full first Fresnel zone still reflects (weaker), so A_min is
        // ~1/(4 pi) of the full Fresnel area, i.e. (lambda/4)*depth/2. Grows with depth.
        double fresnelFraction = lq * depthM / 2.0;
        return Math.Max(floor, fresnelFraction);
    }

    /// <summary>Probability (0..1) that a fracture is DETECTED by surface GPR.
    /// P_det = baseEfficiency * p_dip * p_open * p_size, with:
    ///   baseEfficiency  imaging ceiling (Molron 0.80 open / Dorn 0.91 transmissive; default 0.85);
    ///   p_dip   1 for dip &lt;= 25 deg (sub-horizontal, best imaged), smoothstep down to 0.1 by 75 deg
    ///           (sub-vertical fractures are poorly imaged by surface GPR -- Molron);
    ///   p_open  open = 1, sealed ~ sealedFactor (default 0.15: Molron 2020 sealed ~0; the verified
    ///           cross-stone range is 0.05-0.2 -- sealed/mineral-filled fractures give little contrast);
    ///   p_size  area/(area + A_min) -- the depth-aware Fresnel size floor.</summary>
    public static double DetectionProbability(double dipDeg, bool isOpen, double areaM2,
        double minDetectableAreaM2, double baseEfficiency = 0.80, double sealedFactor = 0.15)
    {
        double d = Math.Abs(dipDeg);
        double pDip;
        if (d <= 25.0) pDip = 1.0;
        else if (d >= 75.0) pDip = 0.1;
        else { double u = (d - 25.0) / 50.0; pDip = 1.0 - 0.9 * (u * u * (3.0 - 2.0 * u)); }
        double pOpen = isOpen ? 1.0 : (sealedFactor < 0 ? 0 : (sealedFactor > 1 ? 1 : sealedFactor));
        double pSize = areaM2 <= 0 ? 0.0 : areaM2 / (areaM2 + Math.Max(minDetectableAreaM2, 1e-9));
        double p = baseEfficiency * pDip * pOpen * pSize;
        return p < 0.0 ? 0.0 : (p > 1.0 ? 1.0 : p);
    }

    /// <summary>Detection-adjusted "effective" confidence that an apparently-intact zone is truly
    /// intact: it needs BOTH that the SEEN fractures are within tolerance (positionConfidence) AND
    /// that fractures were not MISSED (detectionProbability). C_eff = pDet * positionConfidence, so a
    /// low detection probability caps confidence however precisely the seen fractures are located.</summary>
    public static double EffectiveConfidence(double positionConfidence, double detectionProbability)
    {
        double p = positionConfidence * detectionProbability;
        return p < 0.0 ? 0.0 : (p > 1.0 ? 1.0 : p);
    }

    /// <summary>Per-pick total 1-sigma (m) for a single GPR section (no interp term):
    /// sigma_total[i] = sqrt(sigma_recon(depth[i])^2 + sigma_mesh^2).</summary>
    public static double[] SectionSigma(double[] depthsM, double sigmaVRel,
        double lambdaQuarterM, double sigmaMeshM = 0.0)
    {
        if (depthsM == null) return Array.Empty<double>();
        var o = new double[depthsM.Length];
        for (int i = 0; i < depthsM.Length; i++)
        {
            double r = DepthSigma(depthsM[i], sigmaVRel, lambdaQuarterM);
            o[i] = Math.Sqrt(r * r + sigmaMeshM * sigmaMeshM);
        }
        return o;
    }

    public struct LadderSummary
    {
        public double MeanSigma;
        public double P95Sigma;
        public double MaxSigma;
        public double Confidence;            // mean over the locations of ConfidenceWithin(sigma, tol)
        public double ToleranceM;
        public int Count;
        public double DetectionCompleteness; // mean P_det over the fractures (0..1); 1 if not supplied
        public double EffectiveConfidence;   // Confidence * DetectionCompleteness (detection-adjusted)
    }

    /// <summary>Summarise a per-location total-sigma field at a tolerance (the metric to
    /// optimise: MeanSigma down, Confidence up).</summary>
    public static LadderSummary Summarise(double[] sigmaTotal, double tolM)
    {
        var s = new LadderSummary { ToleranceM = tolM };
        if (sigmaTotal == null || sigmaTotal.Length == 0) return s;
        var sorted = (double[])sigmaTotal.Clone();
        Array.Sort(sorted);
        double sum = 0, conf = 0, max = 0;
        foreach (var v in sigmaTotal)
        {
            sum += v; if (v > max) max = v; conf += ConfidenceWithin(v, tolM);
        }
        s.Count = sigmaTotal.Length;
        s.MeanSigma = sum / s.Count;
        s.MaxSigma = max;
        s.P95Sigma = sorted[(int)Math.Min(s.Count - 1, Math.Floor(0.95 * (s.Count - 1)))];
        s.Confidence = conf / s.Count;
        s.DetectionCompleteness = 1.0;          // no detection model supplied -> assume all seen
        s.EffectiveConfidence = s.Confidence;
        return s;
    }

    /// <summary>Detection-aware summary: as Summarise, plus the detection completeness (mean P_det
    /// over the fractures) and the detection-adjusted EffectiveConfidence = Confidence * completeness.</summary>
    public static LadderSummary Summarise(double[] sigmaTotal, double tolM, double detectionCompleteness)
    {
        var s = Summarise(sigmaTotal, tolM);
        s.DetectionCompleteness = detectionCompleteness < 0 ? 0 : (detectionCompleteness > 1 ? 1 : detectionCompleteness);
        s.EffectiveConfidence = EffectiveConfidence(s.Confidence, s.DetectionCompleteness);
        return s;
    }

    // Abramowitz & Stegun 7.1.26 (|error| < 1.5e-7) -- avoids a MathNet dependency.
    public static double Erf(double x)
    {
        int sign = Math.Sign(x); x = Math.Abs(x);
        double t = 1.0 / (1.0 + 0.3275911 * x);
        double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t
                    - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return sign * y;
    }
}
