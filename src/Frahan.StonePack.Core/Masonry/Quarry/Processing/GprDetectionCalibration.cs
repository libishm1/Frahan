#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry.Processing;

// =============================================================================
// GprDetectionCalibration -- per STONE base imaging efficiency eta0 for the
// FractureUncertainty DETECTION rung, calibrated from VERIFIED literature
// (per-stone GPR fracture-detection research, 2026-06-05; full provenance in
// outputs/2026-06-04/gpr_extraction/deep_fracture_review/DETECTION_CALIBRATION.md).
//
// eta0 = the detected fraction for the FAVOURABLE case (OPEN, water/air-filled,
// sub-horizontal fracture dip 0-25 deg, area >= the resolvable floor, surface
// 3-D GPR at the stone's working band). DetectionProbability then multiplies
// eta0 by p_dip * p_open * p_size.
//
// Only GRANITE is a MEASURED rate (Molron 2020 Aspo: 80% of open sub-horizontal
// fractures; Dorn 2012: 91% transmissive borehole). All others are EXTRAPOLATED
// from physics + single-target detections in that lithology; the IsMeasured flag
// records which. Re-fit each value as ground-truth (borehole / quarry-face)
// data accrues, the same way GprPresets continuity spans were fit per stone.
//
// This does NOT touch GprPresets (velocity/eps_r/frequency stay there); it is the
// one stone-specific DETECTION knob, kept here so it is queryable + provenanced.
// =============================================================================

public sealed class GprDetectionCalib
{
    public string Stone;
    public double Eta0;            // favourable open sub-horizontal base efficiency (0..1)
    public double SealedFactor;    // p_open multiplier for SEALED/filled fractures (0..1)
    public bool IsMeasured;        // measured rate vs extrapolated
    public string Confidence;      // high | medium | low
    public string WorkingBandMhz;  // recommended antenna band for fracture detection
    public string SourceDoi;       // primary verified source
    public string Note;
}

public static class GprDetectionCalibration
{
    private static readonly Dictionary<string, GprDetectionCalib> Map =
        new Dictionary<string, GprDetectionCalib>(StringComparer.OrdinalIgnoreCase);

    static GprDetectionCalibration()
    {
        // granite / crystalline -- MEASURED (Molron 2020 0.80 open sub-horizontal; Dorn 2012 0.91 borehole)
        Add("granite", 0.80, 0.10, true, "high", "100-750 (surface); 100-250 (borehole)",
            "10.1016/j.enggeo.2020.105674",
            "Molron 2020 Aspo: 80% of OPEN sub-horizontal; sealed ~0. Dorn 2012: 91% transmissive borehole. Use 0.90 for borehole geometry on steep open fractures.");
        // limestone / dolomite -- EXTRAPOLATED high (low-loss resistive carbonate; air/water-fill = strong contrast)
        Add("limestone", 0.90, 0.12, false, "medium", "100 (deep void) - 700 (thin fracture)",
            "10.3997/1873-0604.2012066",
            "Kana 2013 + Chamberlain 2000: confident single-fracture/void detection in dry resistive limestone. Derate to ~0.6 for DRY (low-contrast) fractures.");
        // sandstone -- EXTRAPOLATED (orientation-validated; porosity/water lowers it)
        Add("sandstone", 0.80, 0.15, false, "medium", "400 (shallow) - 70 (deep)",
            "10.2113/gseegeosci.23.4.314",
            "Elkarmoty 2017: orientation agreement 124 vs 130 deg; large-aperture (2-3 cm) open fractures. High water/porosity -> lower via attenuation; thin apertures below antenna resolution missed.");
        // marble -- EXTRAPOLATED (needs high freq; clay/mica + higher eps_r derate vs granite)
        Add("marble", 0.75, 0.12, false, "medium", "700-3000 (block/bench); 250 (deep)",
            "10.3390/s23208490",
            "Zanzi 2023 Carrara (eps_r 7.7) 1-15 mm open fractures at 3 GHz; Kadioglu 2008 major discontinuities at 250 MHz. Derate to ~0.45 for thin/tight/filled.");
        // travertine -- EXTRAPOLATED (porous carbonate; vuggy clutter)
        Add("travertine", 0.75, 0.15, false, "low", "250 (optimal) - 800",
            "10.1016/j.conbuildmat.2014.12.076",
            "Rey 2015: 250 MHz reliable to >5 m; vuggy macroporosity adds clutter -> dock from carbonate baseline.");
        // andesite / basalt -- EXTRAPOLATED low (magnetite attenuation + scatter)
        Add("andesite", 0.50, 0.15, false, "low", "50-250",
            "10.1029/2005JE002619",
            "Grimm 2006 (tuff strong scatterer ~2 dB/m); magnetite-bearing andesite/basalt adds magnetic-permeability attenuation -> materially below carbonate.");
        // tuff / scoria -- EXTRAPOLATED lowest (strong scatterer, short mean free path)
        Add("tuff", 0.38, 0.2, false, "low", "25-50",
            "10.1029/2005JE002619",
            "Grimm 2006 Bishop Tuff: mean free path down to 4 m; vesicular/welded -> forces low frequency and still scatters.");
    }

    private static void Add(string stone, double eta0, double sealed_, bool measured,
        string conf, string band, string doi, string note)
    {
        Map[stone] = new GprDetectionCalib
        {
            Stone = stone, Eta0 = eta0, SealedFactor = sealed_, IsMeasured = measured,
            Confidence = conf, WorkingBandMhz = band, SourceDoi = doi, Note = note,
        };
    }

    /// <summary>eta0 for a stone key (substring-matched to the preset stone names).
    /// Defaults to the granite measured value 0.80 if unknown.</summary>
    public static GprDetectionCalib Get(string stone)
    {
        if (stone != null)
        {
            if (Map.TryGetValue(stone, out var exact)) return exact;
            foreach (var kv in Map)               // substring match (e.g. "marble_600" -> "marble")
                if (stone.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0) return kv.Value;
        }
        return Map["granite"];
    }

    public static IEnumerable<GprDetectionCalib> All => Map.Values;
}
