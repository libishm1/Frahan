#nullable disable
using System;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Frahan.GH.Attributes;
using Frahan.Masonry.Quarry.Processing;

namespace Frahan.GH.Quarry;

// =============================================================================
// Construct GPR Preset -- build a custom GPR ingestion preset for ANY stone /
// antenna. The library ships only two EMPIRICALLY tuned presets (marble_600,
// granite_160), and we cannot enumerate every mineral / frequency. This lets a
// user define their own (velocity, frequency, eps_r, detection + continuity
// gates) and wire it into GPR Survey Grid > Custom Preset, overriding the named
// preset. Geology note: a stone sold as "marble" may be a compact LIMESTONE
// (e.g. Botticino); the velocity/frequency is what matters for depth, not the
// trade name -- so construct the preset to match the SURVEY, not the label.
//
// Frahan > Quarry > Construct GPR Preset.
// =============================================================================

/// <summary>Grasshopper goo wrapping a <see cref="GprPreset"/> so a constructed
/// preset can flow on the canvas into GPR Survey Grid.</summary>
public sealed class GprPresetGoo : GH_Goo<GprPreset>
{
    public GprPresetGoo() { }
    public GprPresetGoo(GprPreset p) { Value = p; }
    public override bool IsValid => Value != null;
    public override string TypeName => "GPR Preset";
    public override string TypeDescription => "A GPR processing + extraction preset (velocity, frequency, gates).";
    public override IGH_Goo Duplicate() => new GprPresetGoo(Value);
    public override string ToString() => Value == null
        ? "null GPR preset"
        : $"{Value.Label} (v={Value.VelocityMNsPerNs:0.###} m/ns, {Value.FrequencyMhz} MHz, eps_r={Value.EpsR:0.#})";
}

/// <summary>
/// Frahan &gt; Quarry &gt; Construct GPR Preset. Define a custom GPR ingest preset
/// for any stone / antenna and feed it into GPR Survey Grid &gt; Custom Preset.
/// </summary>
[RelatedComponent("Frahan > Quarry > GPR Survey Grid", Reason = "Wire the constructed preset into Custom Preset to ingest a stone the two built-in presets do not cover.")]
[Algorithm("Constructs a GPR preset: velocity (or eps_r), frequency, energy + continuity gates",
    "EM velocity v = c/sqrt(eps_r), c = 0.2998 m/ns; depth = v*t/2. Continuity gate per USGS Mirror Lake WRIR 99-4018C.",
    Note = "Only two presets are empirically tuned (marble_600, granite_160); this covers the rest.")]
public sealed class ConstructGprPresetComponent : FrahanComponentBase
{
    public ConstructGprPresetComponent()
        : base("Construct GPR Preset", "GprPreset",
            "Build a custom GPR ingestion preset for ANY stone / antenna (the library ships only two " +
            "empirically tuned presets, marble_600 and granite_160). Set the EM velocity (or a relative " +
            "permittivity to derive it), the antenna frequency, and the reflector detection + continuity " +
            "gates. Wire the output into GPR Survey Grid > Custom Preset to override the named preset. " +
            "Tip: a stone sold as 'marble' may be a compact limestone - match the velocity/frequency to the " +
            "survey, not the trade name.",
            "Frahan", "Quarry")
    {
    }

    public override Guid ComponentGuid => new Guid("A7E0B0F6-0C0F-4A16-9E3D-0FACE0FACE07");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap Icon => IconProvider.Load("GprIngest.png");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Stone", "St", "Stone / material label (e.g. limestone, marble, granite, travertine).",
            GH_ParamAccess.item, "custom");
        p.AddIntegerParameter("Frequency", "f", "Antenna centre frequency (MHz). Sets the lambda/4 resolution.",
            GH_ParamAccess.item, 600);
        p.AddNumberParameter("Velocity", "v", "EM velocity (m/ns); depth = v*t/2. If <= 0 it is derived from Eps_r. " +
            "Marble/limestone ~0.10, granite ~0.12, travertine ~0.11.", GH_ParamAccess.item, 0.10);
        p.AddNumberParameter("Eps_r", "Er", "Relative permittivity. Used to derive Velocity when Velocity <= 0 " +
            "(v = 0.2998/sqrt(Eps_r)); otherwise Eps_r is recomputed from Velocity for consistency.",
            GH_ParamAccess.item, 9.0);
        p.AddNumberParameter("Energy Quantile", "Q", "Reflector detection threshold (0..1) on the Hilbert energy; " +
            "higher keeps only the strongest reflectors. Marble/granite empirical ~0.985.", GH_ParamAccess.item, 0.985);
        p.AddIntegerParameter("Continuity Traces", "Ct", "Reflector continuity gate in traces: a reflector must " +
            "persist this many traces to be kept. Marble veins are short (~27 traces ~0.65 m); granite shear zones " +
            "longer (~41 traces ~1 m).", GH_ParamAccess.item, 27);
        p.AddBooleanParameter("Migrate", "Mig", "f-k (Stolt) migration on each line (repositions dipping reflectors).",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Preset", "Pr", "Constructed GPR preset. Wire into GPR Survey Grid > Custom Preset.",
            GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        string stone = "custom"; int freq = 600; double v = 0.10, eps = 9.0, q = 0.985; int ct = 27; bool mig = true;
        da.GetData(0, ref stone); da.GetData(1, ref freq); da.GetData(2, ref v); da.GetData(3, ref eps);
        da.GetData(4, ref q); da.GetData(5, ref ct); da.GetData(6, ref mig);

        const double c = 0.2998; // speed of light, m/ns
        if (v <= 0) { eps = Math.Max(1.0, eps); v = c / Math.Sqrt(eps); }
        else { eps = (c / v) * (c / v); }
        ct = Math.Max(3, ct);
        q = Math.Min(0.9999, Math.Max(0.0, q));
        if (string.IsNullOrWhiteSpace(stone)) stone = "custom";

        var preset = new GprPreset
        {
            Key = stone.Trim().ToLowerInvariant(),
            Label = $"{stone.Trim()} - {freq} MHz (constructed)",
            Stone = stone.Trim(),
            FrequencyMhz = freq,
            EpsR = eps,
            VelocityMNsPerNs = v,
            IsEmpirical = false,
            // gain / processing knobs default to the marble_600 family (sensible for compact stone).
            DewowFraction = 1.0 / 30,
            TimeZeroMuteFraction = 0.05,
            TPowerGainExponent = 1.6,
            AgcFraction = 1.0 / 25,
            Migrate = mig,
            DepthEqualize = true,
            EqualizeWindow = 31,
            EnergyQuantile = q,
            ContinuityWindowTraces = ct,
            MinContinuitySupport = Math.Max(3, ct / 3),
            DepthBandHalfSamples = 2,
            Note = "User-constructed preset (NOT empirically tuned). Verify the velocity against a known reflector " +
                   "depth before trusting absolute depths.",
        };

        if (!preset.IsEmpirical)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Constructed preset for '{preset.Stone}' (v={v:0.###} m/ns, eps_r={eps:0.#}). Verify velocity on a known reflector.");

        da.SetData(0, new GprPresetGoo(preset));
    }
}
