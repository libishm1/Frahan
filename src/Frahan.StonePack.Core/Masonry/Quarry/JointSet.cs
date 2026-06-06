#nullable disable
using System;

namespace Frahan.Masonry.Quarry;

// =============================================================================
// JointSet — a structural-geology joint set in a rock mass, characterised by
// orientation (dip direction + dip angle), mean spacing along the normal,
// optional spacing distribution (constant or exponential), and orientation
// scatter.
//
// References:
//   - ISRM Suggested Methods for Rock Joint Characterization (Brown 1981).
//     Joint set defined by (dipDirection, dip, spacing, persistence,
//     roughness, aperture, filling). For dimension-stone block extraction
//     we use dipDirection + dip + spacing + scatter; persistence is
//     treated as infinite (planes span the bounding box) for v1.
//   - Priest, S. D. (1993). Discrete Fracture Network Analysis for Rock
//     Engineering. Spacing distribution typically negative-exponential;
//     orientation scatter via Fisher distribution.
//   - Goodman, R. E. & Shi, G.-H. (1985). Block Theory and its Application
//     to Rock Engineering. Joint sets define removable / tapered / stable
//     blocks; for dimension stone we extract removable blocks.
//   - Latham, J.-P. et al. (2006). Block-size distribution from joint sets
//     for armourstone — same DFN structure used to size quarry blocks.
//
// Convention (geology-standard):
//   - dipDirectionDeg: azimuth of the steepest descent line, measured
//     clockwise from North (+Y), in [0, 360).
//   - dipDeg: angle from horizontal of the steepest descent line, in [0, 90].
//   - The unit normal is computed from (dipDirection, dip) so it points
//     INTO the dipping wedge (i.e. the upper hemisphere when dip < 90).
// =============================================================================

/// <summary>
/// One structural-geology joint set: oriented planes spaced along a normal.
/// Multiple JointSets composed via <c>JointSetDfn</c> give a discrete
/// fracture network for quarry block extraction.
/// </summary>
public sealed class JointSet
{
    public JointSet(
        double dipDirectionDeg,
        double dipDeg,
        double meanSpacing,
        double scatterDeg = 0.0,
        bool exponentialSpacing = false)
    {
        if (!(dipDirectionDeg >= 0.0 && dipDirectionDeg < 360.0))
            throw new ArgumentOutOfRangeException(nameof(dipDirectionDeg),
                $"must be in [0, 360), got {dipDirectionDeg}");
        if (!(dipDeg >= 0.0 && dipDeg <= 90.0))
            throw new ArgumentOutOfRangeException(nameof(dipDeg),
                $"must be in [0, 90], got {dipDeg}");
        if (!(meanSpacing > 0.0))
            throw new ArgumentOutOfRangeException(nameof(meanSpacing),
                $"must be > 0, got {meanSpacing}");
        if (!(scatterDeg >= 0.0 && scatterDeg < 90.0))
            throw new ArgumentOutOfRangeException(nameof(scatterDeg),
                $"must be in [0, 90), got {scatterDeg}");

        DipDirectionDeg = dipDirectionDeg;
        DipDeg = dipDeg;
        MeanSpacing = meanSpacing;
        ScatterDeg = scatterDeg;
        ExponentialSpacing = exponentialSpacing;

        // Compute unit normal from (dipDirection, dip).
        // Convention: +Y = North, +X = East, +Z = Up. Dip direction measured
        // clockwise from North.
        double dipDirRad = dipDirectionDeg * Math.PI / 180.0;
        double dipRad = dipDeg * Math.PI / 180.0;
        // Strike = dip direction - 90°. The dip vector points down the
        // steepest descent. The plane's upward normal is rotated 90°
        // up from the dip vector around the strike axis:
        //   normal = (sin(dip)*sin(dipDir), sin(dip)*cos(dipDir), cos(dip))
        NormalX = Math.Sin(dipRad) * Math.Sin(dipDirRad);
        NormalY = Math.Sin(dipRad) * Math.Cos(dipDirRad);
        NormalZ = Math.Cos(dipRad);
    }

    public double DipDirectionDeg { get; }
    public double DipDeg { get; }
    public double MeanSpacing { get; }
    public double ScatterDeg { get; }
    public bool ExponentialSpacing { get; }

    public double NormalX { get; }
    public double NormalY { get; }
    public double NormalZ { get; }

    public override string ToString() =>
        $"JointSet(dipDir={DipDirectionDeg:0.#}°, dip={DipDeg:0.#}°, " +
        $"spacing={MeanSpacing:0.###}, scatter={ScatterDeg:0.#}°)";
}
