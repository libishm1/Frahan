#nullable disable
using System;

namespace Frahan.Masonry.Quarry.BlockCutOpt;

// =============================================================================
// BlockCutOptOptions -- search parameters for the brute-force BlockCutOpt
// solver. Mirrors the BlockCutOpt.par file format described in Appendix A and
// Appendix B of Elkarmoty et al. 2020 (Resources Policy 68:101761).
//
// Phase 1 scope: a single rectangular tested area, a single block size, no
// sub-division. Sub-division (mx, my) and Pareto multi-objective land in
// Phase 3 and Phase 6 respectively per
// `D:\code_ws\wiki\papers\equations_and_diagrams\08_synthesis_and_optimum_algorithm.md`
// section 9.
// =============================================================================

/// <summary>
/// Search parameters for BlockCutOpt. Immutable.
/// </summary>
public sealed class BlockCutOptOptions
{
    /// <summary>
    /// Phase 1-compatible constructor. theta and phi default to 0 (psi-only
    /// rotation, matching BlockCutOpt 2020).
    /// </summary>
    public BlockCutOptOptions(
        double blockSizeX,
        double blockSizeY,
        double blockSizeZ,
        double kerf,
        double psiStartRad,
        double psiStopRad,
        double psiStepRad,
        double dxMax,
        double dxStep,
        double dyMax,
        double dyStep,
        double thetaMaxRad = 0.0,
        double thetaStepRad = 0.0,
        double phiMaxRad = 0.0,
        double phiStepRad = 0.0)
    {
        if (!(blockSizeX > 0)) throw new ArgumentOutOfRangeException(nameof(blockSizeX));
        if (!(blockSizeY > 0)) throw new ArgumentOutOfRangeException(nameof(blockSizeY));
        if (!(blockSizeZ > 0)) throw new ArgumentOutOfRangeException(nameof(blockSizeZ));
        if (kerf < 0) throw new ArgumentOutOfRangeException(nameof(kerf));
        if (!(psiStopRad >= psiStartRad))
            throw new ArgumentException("psiStopRad must be >= psiStartRad");
        if (!(psiStepRad > 0)) throw new ArgumentOutOfRangeException(nameof(psiStepRad));
        if (!(dxStep > 0)) throw new ArgumentOutOfRangeException(nameof(dxStep));
        if (!(dyStep > 0)) throw new ArgumentOutOfRangeException(nameof(dyStep));
        if (dxMax < 0) throw new ArgumentOutOfRangeException(nameof(dxMax));
        if (dyMax < 0) throw new ArgumentOutOfRangeException(nameof(dyMax));
        if (thetaMaxRad < 0) throw new ArgumentOutOfRangeException(nameof(thetaMaxRad));
        if (phiMaxRad < 0) throw new ArgumentOutOfRangeException(nameof(phiMaxRad));

        BlockSizeX = blockSizeX;
        BlockSizeY = blockSizeY;
        BlockSizeZ = blockSizeZ;
        Kerf = kerf;
        PsiStartRad = psiStartRad;
        PsiStopRad = psiStopRad;
        PsiStepRad = psiStepRad;
        DxMax = dxMax;
        DxStep = dxStep;
        DyMax = dyMax;
        DyStep = dyStep;
        ThetaMaxRad = thetaMaxRad;
        ThetaStepRad = thetaStepRad;
        PhiMaxRad = phiMaxRad;
        PhiStepRad = phiStepRad;
    }

    /// <summary>Maximum +/- tilt around the world X axis (rad). 0 disables theta search.</summary>
    public double ThetaMaxRad { get; }
    public double ThetaStepRad { get; }

    /// <summary>Maximum +/- tilt around the world Y axis (rad). 0 disables phi search.</summary>
    public double PhiMaxRad { get; }
    public double PhiStepRad { get; }

    /// <summary>True if no tilt search is requested.</summary>
    public bool IsPsiOnly => ThetaMaxRad <= 0.0 && PhiMaxRad <= 0.0;

    public double BlockSizeX { get; }
    public double BlockSizeY { get; }
    public double BlockSizeZ { get; }
    public double Kerf { get; }

    public double PsiStartRad { get; }
    public double PsiStopRad { get; }
    public double PsiStepRad { get; }

    /// <summary>Maximum +/- displacement along world X. The dx grid is [-DxMax, DxMax] in steps of DxStep.</summary>
    public double DxMax { get; }
    public double DxStep { get; }

    public double DyMax { get; }
    public double DyStep { get; }

    /// <summary>
    /// Convenience: the limestone Stratum a configuration from
    /// `D:\BlockCutOpt_paper.md` Appendix A. Returns a fresh instance.
    /// </summary>
    public static BlockCutOptOptions LimestoneStratumA()
    {
        return new BlockCutOptOptions(
            blockSizeX: 3.0,
            blockSizeY: 2.0,
            blockSizeZ: 0.8,
            kerf: 0.05,
            psiStartRad: 0.0,
            psiStopRad: Math.PI,
            psiStepRad: 3.0 * Math.PI / 180.0,
            dxMax: 1.5,
            dxStep: 0.5,
            dyMax: 1.5,
            dyStep: 0.5);
    }
}
