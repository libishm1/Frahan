#nullable disable
using System;

namespace Frahan.Kintsugi.Port.Models;

/// <summary>
/// Vector-Quantized Variational Autoencoder codebook lookup.
/// Used by PuzzleFusion++ to regularise the PointNet++ encoder output
/// into a discrete latent space (1024-entry codebook × 16-D codes).
///
/// At inference: each input feature vector is replaced with the
/// nearest codebook entry by Euclidean distance. The straight-through
/// estimator used during training is not relevant at inference.
///
/// Structural skeleton. Weight loading (codebook entries) is Phase 7.
/// </summary>
public sealed class VqVae
{
    private readonly float[] _codebook;  // [numEntries * codeDim] row-major
    private readonly int _numEntries;
    private readonly int _codeDim;

    public int NumEntries => _numEntries;
    public int CodeDim => _codeDim;

    public VqVae(float[] codebook, int numEntries, int codeDim)
    {
        if (codebook == null) throw new ArgumentNullException(nameof(codebook));
        if (codebook.Length < numEntries * codeDim)
            throw new ArgumentException("codebook too small for [numEntries, codeDim].");
        _codebook = codebook;
        _numEntries = numEntries;
        _codeDim = codeDim;
    }

    /// <summary>
    /// Quantise input features. Each row of x[M, codeDim] is replaced
    /// in place with the nearest codebook entry (by Euclidean distance).
    /// </summary>
    /// <param name="x">[M, codeDim] row-major, modified in place.</param>
    /// <param name="M">Number of rows.</param>
    /// <returns>Indices into the codebook of the chosen entries, length M.</returns>
    public int[] Quantise(float[] x, int M)
    {
        if (x == null) throw new ArgumentNullException(nameof(x));
        if (x.Length < M * _codeDim) throw new ArgumentException("x too small.");
        var indices = new int[M];
        for (int i = 0; i < M; i++)
        {
            int xRow = i * _codeDim;
            float bestDist = float.PositiveInfinity;
            int bestIdx = 0;
            for (int e = 0; e < _numEntries; e++)
            {
                int eRow = e * _codeDim;
                float d = 0;
                for (int c = 0; c < _codeDim; c++)
                {
                    float diff = x[xRow + c] - _codebook[eRow + c];
                    d += diff * diff;
                }
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = e;
                }
            }
            indices[i] = bestIdx;
            // Replace x row with nearest codebook entry.
            int srcRow = bestIdx * _codeDim;
            for (int c = 0; c < _codeDim; c++) x[xRow + c] = _codebook[srcRow + c];
        }
        return indices;
    }
}
