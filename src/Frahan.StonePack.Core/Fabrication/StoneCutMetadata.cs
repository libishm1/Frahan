#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Frahan.Core.Fabrication;

// =============================================================================
// StoneCutMetadata — the stone-intelligence payload that travels WITH each cut
// piece into the machine handoff.
//
// The fabrication-readiness wedge: CAM packages (EasySTONE, Alphacam, Breton
// Maestro, Lantek) already consume DXF / .3dm geometry, but they receive it as
// dumb geometry. Frahan owns the upstream intelligence — bed/grain direction,
// fracture/GPR avoidance zones, quarry-block provenance, weight, finish, kerf —
// and should carry it INTO the exchanged file as structured metadata (object
// user-strings + a layer scheme) so it is not lost at the handoff.
//
// This type is runtime-agnostic (no Rhino types). The GH exporter maps it onto
// File3dm object attributes via SetUserString using the Key* constants, and a
// future DXF exporter can reuse the same keys for XDATA / block attributes.
// =============================================================================

public sealed class StoneCutMetadata
{
    /// <summary>Stable per-piece identifier (e.g. "S012").</summary>
    public string PieceId;
    /// <summary>Stone material / source (e.g. "TN Black Granite", quarry name).</summary>
    public string Stone;
    /// <summary>Surface finish: e.g. polished / honed / flamed / bush-hammered / sandblasted.</summary>
    public string Finish;
    /// <summary>Bed / grain direction as a unit vector (xyz). Null if unknown.</summary>
    public double[] BedDirection;
    /// <summary>Piece weight in kilograms (for lifting / crating). NaN if unknown.</summary>
    public double WeightKg = double.NaN;
    /// <summary>Saw kerf in millimetres. NaN if unspecified.</summary>
    public double KerfMm = double.NaN;
    /// <summary>Provenance: originating quarry block id / bench / order. Optional.</summary>
    public string Provenance;

    // --- user-string keys (namespaced so CAM round-trips don't clobber) ---
    public const string KeyPieceId   = "frahan.piece_id";
    public const string KeyStone     = "frahan.stone";
    public const string KeyFinish    = "frahan.finish";
    public const string KeyBedDir    = "frahan.bed_dir";
    public const string KeyWeightKg  = "frahan.weight_kg";
    public const string KeyKerfMm    = "frahan.kerf_mm";
    public const string KeyProvenance = "frahan.provenance";
    public const string KeySchema    = "frahan.schema";
    public const string SchemaValue  = "frahan-cut-1.0";

    /// <summary>
    /// Emit the populated fields as namespaced key/value strings, ready to set
    /// as object user-strings. Unset / NaN fields are skipped.
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>> ToUserStrings()
    {
        var inv = CultureInfo.InvariantCulture;
        yield return new KeyValuePair<string, string>(KeySchema, SchemaValue);
        if (!string.IsNullOrWhiteSpace(PieceId))
            yield return new KeyValuePair<string, string>(KeyPieceId, PieceId);
        if (!string.IsNullOrWhiteSpace(Stone))
            yield return new KeyValuePair<string, string>(KeyStone, Stone);
        if (!string.IsNullOrWhiteSpace(Finish))
            yield return new KeyValuePair<string, string>(KeyFinish, Finish);
        if (BedDirection != null && BedDirection.Length == 3)
            yield return new KeyValuePair<string, string>(KeyBedDir, string.Format(inv,
                "{0:R},{1:R},{2:R}", BedDirection[0], BedDirection[1], BedDirection[2]));
        if (!double.IsNaN(WeightKg))
            yield return new KeyValuePair<string, string>(KeyWeightKg, WeightKg.ToString("R", inv));
        if (!double.IsNaN(KerfMm))
            yield return new KeyValuePair<string, string>(KeyKerfMm, KerfMm.ToString("R", inv));
        if (!string.IsNullOrWhiteSpace(Provenance))
            yield return new KeyValuePair<string, string>(KeyProvenance, Provenance);
    }
}
