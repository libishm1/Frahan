#nullable disable
using System;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// Adaptive Layer Norm (AdaLN) -- LayerNorm whose gamma/beta are
/// PREDICTED from a conditioning vector (time embedding in
/// PuzzleFusion++) via a learned projection, rather than being fixed
/// learned parameters of the LN layer itself.
///
///   t_proj   = silu(t_emb) @ W_emb + b_emb            shape [3 * D]
///   shift, scale, gate = split(t_proj, 3 chunks)      each shape [D]
///   x_norm   = LayerNorm(x, gamma=None, beta=None)    (centred-normalised only)
///   y        = x_norm * (1 + scale) + shift
///
/// The gate output is consumed downstream (residual gating).
///
/// PuzzleFusion++ uses this in every transformer block of the SE(3)
/// denoiser. The W_emb tensor in the state-dict is
/// 'denoiser.transformer_layers.K.norm1.emb.weight' with shape
/// [3 * D_inner, D_t] = [3072, 512] for D=512, mapping the 512-D
/// hidden state (which absorbs the time embedding) to 3072 = 3 * D.
/// </summary>
public static class AdaLN
{
    /// <summary>
    /// Apply AdaLN to x[M, D] given conditioning t[D_t].
    /// Mutates x in place. Returns the per-token `gate` vector
    /// (shape [M * D]) for downstream residual gating.
    ///
    /// W_emb: shape [D_t, 3*D] row-major. Bias optional, length 3*D.
    /// </summary>
    public static float[] Apply(
        float[] x, int M, int D,
        float[] t, int dT,
        float[] wEmb, float[] bEmb)
    {
        if (x == null) throw new ArgumentNullException(nameof(x));
        if (x.Length < M * D) throw new ArgumentException("x too small.");
        if (t == null || t.Length < dT) throw new ArgumentException("t missing/short.");
        if (wEmb == null || wEmb.Length < dT * 3 * D) throw new ArgumentException("wEmb too small.");

        // 1. silu(t)
        var tAct = new float[dT];
        for (int i = 0; i < dT; i++)
            tAct[i] = t[i] / (1.0f + (float)Math.Exp(-t[i]));

        // 2. Project: tAct @ W_emb (+ b_emb). Output shape [3*D].
        int triple = 3 * D;
        var tProj = new float[triple];
        Matmul.MatVec(wEmb, tAct, tProj, triple, dT);
        if (bEmb != null && bEmb.Length >= triple)
            for (int i = 0; i < triple; i++) tProj[i] += bEmb[i];

        // 3. Split: shift = tProj[0..D), scale = tProj[D..2D), gate = tProj[2D..3D).
        // (All applied to every token equally -- the conditioning is shared
        // across the M tokens in this AdaLN block.)
        var shift = new float[D];
        var scale = new float[D];
        var gate  = new float[D];
        Array.Copy(tProj, 0,        shift, 0, D);
        Array.Copy(tProj, D,        scale, 0, D);
        Array.Copy(tProj, 2 * D,    gate,  0, D);

        // 4. LayerNorm without learned gamma/beta -- centre + normalise only.
        LayerNorm.Apply(x, null, null, M, D);

        // 5. y = x * (1 + scale) + shift, broadcast over M.
        for (int i = 0; i < M; i++)
        {
            int row = i * D;
            for (int c = 0; c < D; c++)
                x[row + c] = x[row + c] * (1.0f + scale[c]) + shift[c];
        }

        // 6. Return the gate broadcast over M (so caller can apply it
        //    to the residual stream).
        var gateOut = new float[M * D];
        for (int i = 0; i < M; i++)
        {
            int row = i * D;
            for (int c = 0; c < D; c++) gateOut[row + c] = gate[c];
        }
        return gateOut;
    }
}
