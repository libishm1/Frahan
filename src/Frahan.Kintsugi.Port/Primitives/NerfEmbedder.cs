#nullable disable
using System;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// Sinusoidal positional encoding ("NeRF" style) as used by
/// PuzzleFusion++ in DenoiserTransformer for pose / xyz / scale
/// inputs. Direct port of `utils/model_utils.py::EmbedderNerf`.
///
/// Configuration (per the upstream's embed_kwargs):
///   include_input=True
///   max_freq_log2 = multires - 1   (= 9 for multires=10)
///   num_freqs     = multires       (= 10)
///   log_sampling  = True           (powers of 2 from 1 to 2^9 = 512)
///   periodic_fns  = [sin, cos]
///
/// Output dimension per input axis: 1 + 2 * num_freqs = 21.
/// So input_dims=7 (poses)    -> out_dim = 7 * 21 = 147
///    input_dims=3 (positions)-> out_dim = 3 * 21 = 63
///    input_dims=1 (scale)    -> out_dim = 1 * 21 = 21
///
/// Inference is deterministic + has no learned weights -- everything
/// derives from the frequency band.
/// </summary>
public sealed class NerfEmbedder
{
    public int InputDims { get; }
    public int NumFreqs { get; }
    public bool IncludeInput { get; }
    public float MaxFreqLog2 { get; }
    public bool LogSampling { get; }
    public int OutDim { get; }

    private readonly float[] _freqBands;

    public NerfEmbedder(int inputDims, int numFreqs,
                        bool includeInput = true,
                        float maxFreqLog2 = -1f,  // default: numFreqs - 1
                        bool logSampling = true)
    {
        if (inputDims <= 0) throw new ArgumentException("inputDims must be positive.");
        if (numFreqs <= 0) throw new ArgumentException("numFreqs must be positive.");
        InputDims = inputDims;
        NumFreqs = numFreqs;
        IncludeInput = includeInput;
        MaxFreqLog2 = maxFreqLog2 >= 0 ? maxFreqLog2 : (numFreqs - 1);
        LogSampling = logSampling;
        OutDim = inputDims * ((includeInput ? 1 : 0) + 2 * numFreqs);

        _freqBands = new float[numFreqs];
        if (logSampling)
        {
            // torch.linspace(0., max_freq, steps=num_freqs) then 2**x.
            for (int i = 0; i < numFreqs; i++)
            {
                float t = (numFreqs == 1) ? 0f : MaxFreqLog2 * i / (numFreqs - 1);
                _freqBands[i] = (float)Math.Pow(2.0, t);
            }
        }
        else
        {
            // torch.linspace(2^0, 2^max_freq, steps=num_freqs).
            float start = 1f;
            float stop = (float)Math.Pow(2.0, MaxFreqLog2);
            for (int i = 0; i < numFreqs; i++)
            {
                float t = (numFreqs == 1) ? 0f : (float)i / (numFreqs - 1);
                _freqBands[i] = start + (stop - start) * t;
            }
        }
    }

    /// <summary>
    /// Embed a tensor `input[M, InputDims]` (row-major). Returns
    /// `output[M, OutDim]` row-major. The order along the last
    /// dimension matches PyTorch's cat order:
    ///   [input | sin(input*f0) | cos(input*f0) | sin(input*f1) | cos(input*f1) | ...]
    /// where each block has InputDims elements per row.
    /// </summary>
    public float[] Embed(float[] input, int M)
    {
        if (input == null || input.Length < M * InputDims)
            throw new ArgumentException("input too small.");
        var output = new float[M * OutDim];
        int colOffset = 0;
        // First block: include_input copy.
        if (IncludeInput)
        {
            for (int m = 0; m < M; m++)
                for (int d = 0; d < InputDims; d++)
                    output[m * OutDim + colOffset + d] = input[m * InputDims + d];
            colOffset += InputDims;
        }
        // For each frequency band, append sin then cos.
        for (int f = 0; f < NumFreqs; f++)
        {
            float freq = _freqBands[f];
            for (int m = 0; m < M; m++)
            {
                int inRow = m * InputDims;
                int outRow = m * OutDim + colOffset;
                for (int d = 0; d < InputDims; d++)
                {
                    float v = input[inRow + d] * freq;
                    output[outRow + d] = (float)Math.Sin(v);
                    output[outRow + InputDims + d] = (float)Math.Cos(v);
                }
            }
            colOffset += 2 * InputDims;
        }
        return output;
    }
}
