#nullable disable
using System;
using System.Collections.Generic;

namespace Frahan.Masonry.Quarry.Processing;

// =============================================================================
// GprPresets -- per stone-type x antenna-frequency processing defaults.
//
// These are the parameter sets that produced the validated 3D fracture models on
// the real datasets (HITL cards in outputs/2026-06-04/gpr_extraction/figures/
// hitl_*.png; sweep rationale in GPR_PARAMETER_OPTIMIZATION.md). The same values
// live in gpr_presets.py so the Python prototype and the C# Core/GH component
// stay in lock-step. eps_r / velocity anchors: gpr_math_derivations.md section 1.
//
// Window sizes are FRACTIONS of the trace sample count so a preset transfers
// across acquisitions; absolute windows then scale with the sample interval
// (i.e. with antenna bandwidth). Velocity is the single highest-leverage value
// (depth = v*t/2); always override with a WARR/CMP-measured v where available.
//
// marble_600 + granite_160 are EMPIRICALLY tuned on real data (Bondua Botticino,
// Doetsch Grimsel). travertine_390 / andesite_390 / limestone_200 are
// LITERATURE-DEFAULT (no readable raw on disk yet; .gsf decode pending) -- the
// IsEmpirical flag records which is which so the GH component can warn the user.
// =============================================================================

public sealed class GprPreset
{
    public string Key;
    public string Label;
    public string Stone;
    public int FrequencyMhz;
    public double EpsR;
    public double VelocityMNsPerNs;     // m/ns
    public bool IsEmpirical;            // tuned on real data vs literature-default

    // processor knobs
    public double DewowFraction;
    public double TimeZeroMuteFraction;
    public double TPowerGainExponent;
    public double AgcFraction;
    public bool Migrate;
    public bool DepthEqualize;
    public int EqualizeWindow;
    // extractor knobs
    public double EnergyQuantile;
    public int ContinuityWindowTraces;
    public int MinContinuitySupport;
    public int DepthBandHalfSamples;

    public string Note;

    /// <summary>Configure a processor + extractor from this preset.</summary>
    public void Apply(RadargramProcessor proc, FractureExtractor fx)
    {
        if (proc != null)
        {
            proc.DewowWindowFraction = DewowFraction;
            proc.TimeZeroMuteFraction = TimeZeroMuteFraction;
            proc.TPowerGainExponent = TPowerGainExponent;
            proc.AgcWindowFraction = AgcFraction;
            proc.Migrate = Migrate;
            proc.DepthEqualize = DepthEqualize;
            proc.EqualizeWindow = EqualizeWindow;
        }
        if (fx != null)
        {
            fx.EnergyQuantile = EnergyQuantile;
            fx.ContinuityWindowTraces = ContinuityWindowTraces;
            fx.MinContinuitySupport = MinContinuitySupport;
            fx.DepthBandHalfSamples = DepthBandHalfSamples;
        }
    }
}

public static class GprPresets
{
    private static readonly Dictionary<string, GprPreset> Map =
        new Dictionary<string, GprPreset>(StringComparer.OrdinalIgnoreCase);

    static GprPresets()
    {
        Add(new GprPreset
        {
            Key = "marble_600", Label = "Marble (Botticino) - IDS 600 MHz",
            Stone = "marble", FrequencyMhz = 600, EpsR = 9.0, VelocityMNsPerNs = 0.10, IsEmpirical = true,
            DewowFraction = 1.0 / 30, TimeZeroMuteFraction = 0.05, TPowerGainExponent = 1.6,
            AgcFraction = 1.0 / 25, Migrate = true, DepthEqualize = true, EqualizeWindow = 31,
            EnergyQuantile = 0.985, ContinuityWindowTraces = 27, MinContinuitySupport = 9, DepthBandHalfSamples = 2,
            Note = "fine 600 MHz: migration + depth-equalize reveal deep bedding/stylolites. Marble fractures " +
                   "(stylolites/veins) are SHORTER than granite shear zones (measured max ~34 traces ~0.9 m), so " +
                   "the continuity span is calibrated to ~0.65 m (27 traces at dx=0.026 m), NOT the granite USGS " +
                   "40-trace (=1 m) gate -- this surfaces marble's genuine short reflectors from the SAME 0.985 " +
                   "energy bar (the detection threshold is unchanged; only the length gate is stone-calibrated).",
        });
        Add(new GprPreset
        {
            Key = "granite_160", Label = "Granite (Grimsel) - MALA GX160 160 MHz",
            Stone = "granite", FrequencyMhz = 160, EpsR = 6.0, VelocityMNsPerNs = 0.12, IsEmpirical = true,
            DewowFraction = 1.0 / 30, TimeZeroMuteFraction = 0.08, TPowerGainExponent = 2.0,
            AgcFraction = 1.0 / 25, Migrate = true, DepthEqualize = true, EqualizeWindow = 41,
            EnergyQuantile = 0.985, ContinuityWindowTraces = 41, MinContinuitySupport = 12, DepthBandHalfSamples = 2,
            Note = "deep 160 MHz: deeper mute (direct-wave ring), higher t-gain for absorption",
        });
        // --- granite FREQUENCY family (25-1200 MHz). Settings scale with antenna frequency
        // per the granite-frequency SLM (Porsani 2006 25/50/100 MHz; Isakova 2021 Karelia
        // 150/1200 MHz; Grimsel 160 MHz validated). Lower f -> deeper mute + higher t-gain +
        // larger equalize window (coarser resolution, deeper penetration); higher f -> the
        // reverse. eps_r is made consistent with v via eps_r=(c/v)^2. Only granite_160 is
        // validated end-to-end by us (IsEmpirical=true); the others carry paper-measured
        // velocities where noted but extrapolated filter windows, so IsEmpirical=false. ---
        Add(new GprPreset
        {
            Key = "granite_25", Label = "Granite - 25 MHz deep (Porsani, lit-default)",
            Stone = "granite", FrequencyMhz = 25, EpsR = 6.2, VelocityMNsPerNs = 0.12, IsEmpirical = false,
            DewowFraction = 1.0 / 30, TimeZeroMuteFraction = 0.11, TPowerGainExponent = 2.3,
            AgcFraction = 1.0 / 25, Migrate = true, DepthEqualize = true, EqualizeWindow = 49,
            EnergyQuantile = 0.985, ContinuityWindowTraces = 41, MinContinuitySupport = 12, DepthBandHalfSamples = 2,
            Note = "deepest-penetration / coarsest (lambda/4 ~1.2 m). Porsani used 25 MHz for the deepest discontinuities; v not WARR-measured at 25 MHz (lit-default 0.12).",
        });
        Add(new GprPreset
        {
            Key = "granite_50", Label = "Granite - 50 MHz (Porsani WARR v=0.105)",
            Stone = "granite", FrequencyMhz = 50, EpsR = 8.2, VelocityMNsPerNs = 0.105, IsEmpirical = false,
            DewowFraction = 1.0 / 30, TimeZeroMuteFraction = 0.10, TPowerGainExponent = 2.2,
            AgcFraction = 1.0 / 25, Migrate = true, DepthEqualize = true, EqualizeWindow = 47,
            EnergyQuantile = 0.985, ContinuityWindowTraces = 41, MinContinuitySupport = 12, DepthBandHalfSamples = 2,
            Note = "Porsani 2006 WARR semblance v=0.105 m/ns (eps_r=(c/v)^2~8.2); best in thick-cover to ~25 m; lambda/4 ~0.53 m.",
        });
        Add(new GprPreset
        {
            Key = "granite_100", Label = "Granite - 100 MHz (Porsani Dix v=0.117)",
            Stone = "granite", FrequencyMhz = 100, EpsR = 6.6, VelocityMNsPerNs = 0.117, IsEmpirical = false,
            DewowFraction = 1.0 / 30, TimeZeroMuteFraction = 0.09, TPowerGainExponent = 2.1,
            AgcFraction = 1.0 / 25, Migrate = true, DepthEqualize = true, EqualizeWindow = 43,
            EnergyQuantile = 0.985, ContinuityWindowTraces = 41, MinContinuitySupport = 12, DepthBandHalfSamples = 2,
            Note = "Porsani 2006 Dix RMS v=0.117 m/ns (eps_r~6.6); detailed on fresh granite to ~15 m; lambda/4 ~0.29 m.",
        });
        Add(new GprPreset
        {
            Key = "granite_150", Label = "Granite - 150 MHz (Isakova Karelia OKO-2)",
            Stone = "granite", FrequencyMhz = 150, EpsR = 6.2, VelocityMNsPerNs = 0.12, IsEmpirical = false,
            DewowFraction = 1.0 / 30, TimeZeroMuteFraction = 0.082, TPowerGainExponent = 2.02,
            AgcFraction = 1.0 / 25, Migrate = true, DepthEqualize = true, EqualizeWindow = 41,
            EnergyQuantile = 0.985, ContinuityWindowTraces = 41, MinContinuitySupport = 12, DepthBandHalfSamples = 2,
            Note = "Isakova 2021 Karelia OKO-2 150 MHz deep antenna; detailed to ~15 m; lambda/4 ~0.20 m. Lower Q (~0.97) for broad water-filled cavities.",
        });
        Add(new GprPreset
        {
            Key = "granite_1200", Label = "Granite - 1200 MHz near-surface (Isakova, lit-default)",
            Stone = "granite", FrequencyMhz = 1200, EpsR = 6.2, VelocityMNsPerNs = 0.12, IsEmpirical = false,
            DewowFraction = 1.0 / 30, TimeZeroMuteFraction = 0.04, TPowerGainExponent = 1.4,
            AgcFraction = 1.0 / 25, Migrate = true, DepthEqualize = true, EqualizeWindow = 29,
            EnergyQuantile = 0.985, ContinuityWindowTraces = 41, MinContinuitySupport = 12, DepthBandHalfSamples = 2,
            Note = "Isakova 2021 Karelia OKO-2 1200 MHz: resolves the ~2.5 m near-surface weathering zone, lambda/4 ~2.5 cm joints; shallow mute, low t-gain.",
        });
        Add(new GprPreset
        {
            Key = "travertine_390", Label = "Travertine (Carpinis) - FLB ~390 MHz (lit-default)",
            Stone = "travertine", FrequencyMhz = 390, EpsR = 7.0, VelocityMNsPerNs = 0.113, IsEmpirical = false,
            DewowFraction = 1.0 / 30, TimeZeroMuteFraction = 0.06, TPowerGainExponent = 1.8,
            AgcFraction = 1.0 / 25, Migrate = true, DepthEqualize = true, EqualizeWindow = 35,
            EnergyQuantile = 0.985, ContinuityWindowTraces = 41, MinContinuitySupport = 12, DepthBandHalfSamples = 2,
            Note = "vesicular -> variable v; lit-default until a .gsf reader lands",
        });
        Add(new GprPreset
        {
            Key = "andesite_390", Label = "Andesite (Pietroasa) - FLB ~390 MHz (lit-default)",
            Stone = "andesite", FrequencyMhz = 390, EpsR = 7.5, VelocityMNsPerNs = 0.109, IsEmpirical = false,
            DewowFraction = 1.0 / 30, TimeZeroMuteFraction = 0.06, TPowerGainExponent = 1.8,
            AgcFraction = 1.0 / 25, Migrate = true, DepthEqualize = true, EqualizeWindow = 35,
            EnergyQuantile = 0.985, ContinuityWindowTraces = 41, MinContinuitySupport = 12, DepthBandHalfSamples = 2,
            Note = "volcanic, homogeneous; lit-default",
        });
        Add(new GprPreset
        {
            Key = "limestone_200", Label = "Limestone - ~200 MHz (Pipan, lit-default)",
            Stone = "limestone", FrequencyMhz = 200, EpsR = 7.0, VelocityMNsPerNs = 0.113, IsEmpirical = false,
            DewowFraction = 1.0 / 30, TimeZeroMuteFraction = 0.07, TPowerGainExponent = 2.0,
            AgcFraction = 1.0 / 25, Migrate = true, DepthEqualize = true, EqualizeWindow = 41,
            EnergyQuantile = 0.98, ContinuityWindowTraces = 41, MinContinuitySupport = 12, DepthBandHalfSamples = 2,
            Note = "Pipan semblance v 0.09-0.13; cavity detection -> lower quantile (broad anomalies)",
        });
    }

    private static void Add(GprPreset p) => Map[p.Key] = p;

    public static GprPreset Get(string key)
    {
        if (key != null && Map.TryGetValue(key, out var p)) return p;
        throw new KeyNotFoundException($"unknown GPR preset '{key}'; have: {string.Join(", ", Keys)}");
    }

    public static bool TryGet(string key, out GprPreset p) => Map.TryGetValue(key ?? "", out p);

    /// <summary>
    /// Resolve a preset from a spec string. First tries a named preset key
    /// (marble_600, granite_160, ...). If that fails, tries to parse a
    /// CONSTRUCTED-preset string of the form produced by GprPresetGoo.ToString(),
    /// e.g. "custom - 600 MHz (constructed) (v=0.1 m/ns, 600 MHz, eps_r=9)". A
    /// constructed preset wired into a text Preset input arrives as this string;
    /// this recovers velocity / frequency / eps_r and rebuilds an ad-hoc preset
    /// with the standard constructed gate defaults (marble_600 family), so a user's
    /// custom preset works anywhere a named preset does. Returns false only when
    /// the spec is neither a known key nor a parseable constructed string.
    /// </summary>
    public static bool TryResolve(string spec, out GprPreset preset)
    {
        preset = null;
        if (string.IsNullOrWhiteSpace(spec)) return false;
        if (Map.TryGetValue(spec.Trim(), out preset)) return true;

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var mV = System.Text.RegularExpressions.Regex.Match(spec, @"v\s*=\s*([-+0-9.eE]+)\s*m\s*/\s*ns");
        var mF = System.Text.RegularExpressions.Regex.Match(spec, @"([0-9]+(?:\.[0-9]+)?)\s*MHz");
        var mE = System.Text.RegularExpressions.Regex.Match(spec, @"eps_?r\s*=\s*([-+0-9.eE]+)");
        if (!mV.Success && !mF.Success && !mE.Success) return false; // not a constructed spec

        double v   = mV.Success ? double.Parse(mV.Groups[1].Value, ci) : 0.0;
        double eps = mE.Success ? double.Parse(mE.Groups[1].Value, ci) : 0.0;
        int freq   = mF.Success ? (int)System.Math.Round(double.Parse(mF.Groups[1].Value, ci)) : 600;
        // Velocity is the leverage value; derive whichever of v / eps_r is absent
        // via v = c/sqrt(eps_r) with c ~ 0.2998 m/ns.
        if (v <= 0 && eps > 0) v = 0.2998 / System.Math.Sqrt(eps);
        if (eps <= 0 && v > 0) eps = System.Math.Pow(0.2998 / v, 2);
        if (v <= 0) { v = 0.10; eps = 9.0; }

        string stone = "custom";
        int dash = spec.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && dash <= 40) stone = spec.Substring(0, dash).Trim();

        preset = new GprPreset
        {
            Key = stone.ToLowerInvariant(),
            Label = stone + " - " + freq + " MHz (constructed)",
            Stone = stone, FrequencyMhz = freq, EpsR = eps, VelocityMNsPerNs = v,
            IsEmpirical = false,
            DewowFraction = 1.0 / 30, TimeZeroMuteFraction = 0.05, TPowerGainExponent = 1.6,
            AgcFraction = 1.0 / 25, Migrate = true, DepthEqualize = true, EqualizeWindow = 31,
            EnergyQuantile = 0.985, ContinuityWindowTraces = 27, MinContinuitySupport = 9,
            DepthBandHalfSamples = 2,
            Note = "Parsed from a constructed-preset string; gates use the marble_600 " +
                   "constructed defaults. Verify velocity against a known reflector depth.",
        };
        return true;
    }

    public static IEnumerable<string> Keys => Map.Keys;
    public static IEnumerable<GprPreset> All => Map.Values;
}
