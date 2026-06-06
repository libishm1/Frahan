#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Masonry.Quarry.Ingestion;

namespace Frahan.Masonry.Quarry.Processing;

// =============================================================================
// FractureExtractor -- high-energy reflector picking with USGS continuity.
//
// Consumes the instantaneous-energy section from RadargramProcessor and returns
// fracture picks. Two rules, both from the reviewed literature:
//   1. High-energy local maxima: a fracture/cavity reflects strongly; intact
//      stone is the LOW-energy background (Porsani 2006; Isakova 2021 Karelia;
//      Grandjean & Gourry 1996). Pick per-column local maxima above a high
//      energy quantile.
//   2. USGS lateral-continuity criterion (water.usgs.gov/ogw/bgas/outcrop/): a
//      true reflector is laterally CONTINUOUS -- keep a pick only if >= a minimum
//      number of like picks fall within a horizontal window (>= 40 traces) in a
//      narrow depth band. Rejects isolated clutter / point diffractions.
//
// Output picks carry depth = v * t / 2 and a confidence = normalised energy.
// Rhino-free; operates on the [samples, traces] grid + scalar geometry.
// =============================================================================

public sealed class FractureExtractor
{
    /// <summary>Energy quantile above which a sample is a candidate (0..1).</summary>
    public double EnergyQuantile { get; set; } = 0.985;
    /// <summary>Lateral-continuity window in traces (USGS >= 40).</summary>
    public int ContinuityWindowTraces { get; set; } = 41;
    /// <summary>Minimum like-picks within the window + depth band to keep a pick.</summary>
    public int MinContinuitySupport { get; set; } = 12;
    /// <summary>Half-height of the depth band (samples) used for continuity.</summary>
    public int DepthBandHalfSamples { get; set; } = 2;
    /// <summary>Max reflector dip (deg) the continuity filter follows. Steeper events find no
    /// matching slope and are rejected -- enforces the USGS &lt;45 deg gate.</summary>
    public double DipMaxDeg { get; set; } = 45.0;
    /// <summary>Number of candidate slopes tested across [-DipMaxDeg, +DipMaxDeg].</summary>
    public int SlopeCount { get; set; } = 9;

    public sealed class FracturePick
    {
        public int TraceIndex;
        public int SampleIndex;
        public double DepthMetres;
        public double Energy;       // normalised 0..1
    }

    /// <summary>Extract fracture picks from a [ns,ntr] energy grid with the DIP-AWARE USGS
    /// continuity rule: continuity is counted along candidate reflector dips up to DipMaxDeg,
    /// so dipping shear zones (not just sub-horizontal reflectors) are kept while steep events
    /// are rejected.</summary>
    /// <param name="energy">instantaneous-energy section (RadargramProcessor.Run)</param>
    /// <param name="dt">ns per sample</param>
    /// <param name="dx">trace spacing (m), for the slope-to-dip mapping</param>
    /// <param name="v">velocity m/ns (depth = v*t/2)</param>
    public IReadOnlyList<FracturePick> Extract(double[,] energy, double dt, double dx, double v)
    {
        int ns = energy.GetLength(0), ntr = energy.GetLength(1);
        double emax = 0; foreach (var x in energy) if (x > emax) emax = x;
        double inv = emax > 0 ? 1.0 / emax : 1.0;

        double thr = Quantile(energy, EnergyQuantile) * inv;

        // 1. per-column local maxima above threshold
        var mask = new bool[ns, ntr];
        for (int t = 0; t < ntr; t++)
            for (int i = 1; i < ns - 1; i++)
            {
                double c = energy[i, t] * inv;
                if (c > thr && c >= energy[i - 1, t] * inv && c >= energy[i + 1, t] * inv)
                    mask[i, t] = true;
            }

        // 2. DIP-AWARE USGS continuity: for each candidate slope (samples/trace) up to the
        //    DipMaxDeg gate, shear so a reflector of that dip is horizontal, count +-band
        //    support over a ContinuityWindowTraces window, unshear, and keep the MAX support
        //    over slopes. depth-per-sample = v*dt/2; slope for angle th = tan(th)*dx/(v*dt/2).
        double depthPerSample = v * dt / 2.0;
        double smax = depthPerSample > 0 ? Math.Tan(DipMaxDeg * Math.PI / 180.0) * dx / depthPerSample : 0.0;
        int nsl = Math.Max(1, SlopeCount);
        int bh = DepthBandHalfSamples, half = ContinuityWindowTraces / 2;
        var best = new int[ns, ntr];
        var shift = new int[ntr];
        var band = new int[ns, ntr];
        var sup = new int[ns, ntr];
        for (int sidx = 0; sidx < nsl; sidx++)
        {
            double slope = nsl == 1 ? 0.0 : -smax + 2.0 * smax * sidx / (nsl - 1);
            for (int t = 0; t < ntr; t++) shift[t] = (int)Math.Round(slope * (t - ntr / 2.0));

            // sheared depth-band: band[i,t] = #sheared picks within +-bh of row i (zero-fill)
            Array.Clear(band, 0, band.Length);
            for (int t = 0; t < ntr; t++)
            {
                int sh = shift[t];
                for (int i = 0; i < ns; i++)
                {
                    int src = i + sh;
                    if (src < 0 || src >= ns || !mask[src, t]) continue;
                    int lo = Math.Max(0, i - bh), hi = Math.Min(ns - 1, i + bh);
                    for (int k = lo; k <= hi; k++) band[k, t]++;
                }
            }
            // horizontal support over the trace window (running sum per row)
            for (int i = 0; i < ns; i++)
            {
                int s = 0;
                for (int t = 0; t <= Math.Min(half, ntr - 1); t++) s += band[i, t];
                for (int t = 0; t < ntr; t++)
                {
                    sup[i, t] = s;
                    int add = t + 1 + half, rem = t - half;
                    if (add < ntr) s += band[i, add];
                    if (rem >= 0) s -= band[i, rem];
                }
            }
            // unshear support back to the original frame and keep the max over slopes
            for (int t = 0; t < ntr; t++)
            {
                int sh = shift[t];
                for (int i = 0; i < ns; i++)
                {
                    int src = i - sh;
                    int val = (src >= 0 && src < ns) ? sup[src, t] : 0;
                    if (val > best[i, t]) best[i, t] = val;
                }
            }
        }

        var picks = new List<FracturePick>();
        for (int i = 0; i < ns; i++)
            for (int t = 0; t < ntr; t++)
                if (mask[i, t] && best[i, t] >= MinContinuitySupport)
                    picks.Add(new FracturePick
                    {
                        TraceIndex = t,
                        SampleIndex = i,
                        DepthMetres = v * (i * dt) / 2.0,
                        Energy = energy[i, t] * inv
                    });
        return picks;
    }

    /// <summary>Convert picks to GprReflectorPick world-coordinate records using
    /// the source radargram's trace positions.</summary>
    public IReadOnlyList<GprReflectorPick> ToReflectorPicks(
        IReadOnlyList<FracturePick> picks, GprRadargram source, string label = "fracture")
    {
        var outp = new List<GprReflectorPick>(picks.Count);
        foreach (var p in picks)
        {
            var tr = source.Traces[Math.Min(p.TraceIndex, source.TraceCount - 1)];
            outp.Add(new GprReflectorPick(tr.X, tr.Y, p.DepthMetres,
                Math.Max(0.0, Math.Min(1.0, p.Energy)), label));
        }
        return outp;
    }

    private static double Quantile(double[,] a, double q)
    {
        int n = a.Length;
        var flat = new double[n];
        int k = 0;
        foreach (var x in a) flat[k++] = x;
        Array.Sort(flat);
        double pos = q * (n - 1);
        int lo = (int)Math.Floor(pos);
        int hi = Math.Min(lo + 1, n - 1);
        double frac = pos - lo;
        return flat[lo] + frac * (flat[hi] - flat[lo]);
    }
}
