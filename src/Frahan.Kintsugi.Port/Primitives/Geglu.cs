#nullable disable
using System;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// GEGLU activation: a fused gated GeLU used by diffusers' FeedForward.
///
/// In the upstream EncoderLayer:
///   self.ff = FeedForward(dim, activation_fn='geglu', ...)
///
/// Which expands to a 3-element ModuleList:
///   net.0 = GEGLU(dim_in=dim, dim_out=4*dim)
///   net.1 = nn.Dropout(dropout)
///   net.2 = nn.Linear(4*dim, dim)
///
/// GEGLU's forward:
///   def forward(self, hidden_states):
///       hidden_states, gate = self.proj(hidden_states).chunk(2, dim=-1)
///       return hidden_states * F.gelu(gate)
///
/// So GEGLU(D_in, D_out) is internally `nn.Linear(D_in, 2 * D_out)` with
/// weight shape [2*D_out, D_in]. Tensor name (in our checkpoint):
///   denoiser.transformer_layers.{i}.ff.net.0.proj.weight  [8*D, D]
///   denoiser.transformer_layers.{i}.ff.net.0.proj.bias    [8*D]
///   denoiser.transformer_layers.{i}.ff.net.2.weight       [D, 4*D]
///   denoiser.transformer_layers.{i}.ff.net.2.bias         [D]
/// </summary>
public static class Geglu
{
    /// <summary>
    /// Apply GEGLU: matmul -> chunk(2) -> values * GeLU(gate).
    /// </summary>
    /// <param name="input">[M, D_in] row-major.</param>
    /// <param name="M">Number of rows.</param>
    /// <param name="Din">Input channels.</param>
    /// <param name="Dout">Output channels (HALF the linear projection).</param>
    /// <param name="projW">[2*D_out, D_in] linear weight.</param>
    /// <param name="projB">[2*D_out] bias or null.</param>
    /// <returns>[M, D_out] activations.</returns>
    public static float[] Apply(float[] input, int M, int Din, int Dout,
                                 float[] projW, float[] projB)
    {
        // 1. Project: [M, D_in] @ projW.T -> [M, 2*Dout]
        // projW is [2*Dout, Din]; transpose to [Din, 2*Dout].
        int twoDout = 2 * Dout;
        var wT = new float[Din * twoDout];
        for (int o = 0; o < twoDout; o++)
            for (int k = 0; k < Din; k++)
                wT[k * twoDout + o] = projW[o * Din + k];
        var proj = new float[M * twoDout];
        Matmul.MatMul(input, wT, proj, M, Din, twoDout);
        if (projB != null) Matmul.AddBias(proj, projB, M, twoDout);

        // 2. Split into values [M, Dout] and gate [M, Dout], then
        //    output = values * GeLU(gate).
        var output = new float[M * Dout];
        for (int m = 0; m < M; m++)
        {
            int srcOff = m * twoDout;
            int dstOff = m * Dout;
            for (int c = 0; c < Dout; c++)
            {
                float val = proj[srcOff + c];
                float gate = proj[srcOff + Dout + c];
                // GeLU(gate) -- using the same tanh-approx Activations.Gelu uses.
                float g3 = gate * gate * gate;
                float t = 0.7978845608f * (gate + 0.044715f * g3);
                float gelu = 0.5f * gate * (1.0f + (float)Math.Tanh(t));
                output[dstOff + c] = val * gelu;
            }
        }
        return output;
    }
}
