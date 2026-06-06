#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Kintsugi.Port.Models;
using Frahan.Kintsugi.Port.Weights;

namespace Frahan.Kintsugi.Port.Outer;

/// <summary>
/// Maps verifier tensor names from kintsugi.bin into a populated
/// VerifierTransformerPort.Weights.
///
/// Upstream architecture (verifier/model/modules/verifier_transformer.py):
///   edge_feature_emb: nn.Linear(7, 256)
///   transformer_encoder: 6 PyTorch nn.TransformerEncoderLayer with:
///     - self_attn.in_proj_weight [3*256, 256] (concat Q/K/V)
///     - self_attn.in_proj_bias [3*256]
///     - self_attn.out_proj.weight/bias
///     - linear1.weight/bias  [2048, 256] (FFN expansion)
///     - linear2.weight/bias  [256, 2048] (FFN contraction)
///     - norm1.weight/bias, norm2.weight/bias
///   mlp_out: nn.Linear(256, 1)
///
/// Tensor names from the upstream LightningModule wrapper:
///   verifier.edge_feature_emb.{weight,bias}
///   verifier.transformer_encoder.layers.{i}.self_attn.in_proj_weight
///   verifier.transformer_encoder.layers.{i}.self_attn.in_proj_bias
///   verifier.transformer_encoder.layers.{i}.self_attn.out_proj.{weight,bias}
///   verifier.transformer_encoder.layers.{i}.linear1.{weight,bias}
///   verifier.transformer_encoder.layers.{i}.linear2.{weight,bias}
///   verifier.transformer_encoder.layers.{i}.norm{1,2}.{weight,bias}
///   verifier.mlp_out.{weight,bias}
/// </summary>
public static class VerifierWeightLoader
{
    public static VerifierTransformerPort.Weights LoadWeights(
        WeightReader r, VerifierTransformerPort.Config cfg = null)
    {
        cfg = cfg ?? new VerifierTransformerPort.Config();
        var w = new VerifierTransformerPort.Weights
        {
            EdgeFeatureEmbW = Req(r, "verifier.edge_feature_emb.weight"),
            EdgeFeatureEmbB = Try(r, "verifier.edge_feature_emb.bias"),
            Layers = new VerifierTransformerPort.LayerWeights[cfg.NumLayers],
            MlpOutW = Req(r, "verifier.mlp_out.weight"),
            MlpOutB = Try(r, "verifier.mlp_out.bias"),
        };
        for (int i = 0; i < cfg.NumLayers; i++)
        {
            var prefix = $"verifier.transformer_encoder.layers.{i}";
            w.Layers[i] = new VerifierTransformerPort.LayerWeights
            {
                InProjW = Req(r, $"{prefix}.self_attn.in_proj_weight"),
                InProjB = Try(r, $"{prefix}.self_attn.in_proj_bias"),
                OutProjW = Req(r, $"{prefix}.self_attn.out_proj.weight"),
                OutProjB = Try(r, $"{prefix}.self_attn.out_proj.bias"),
                FfnL1W = Req(r, $"{prefix}.linear1.weight"),
                FfnL1B = Try(r, $"{prefix}.linear1.bias"),
                FfnL2W = Req(r, $"{prefix}.linear2.weight"),
                FfnL2B = Try(r, $"{prefix}.linear2.bias"),
                N1Gamma = Req(r, $"{prefix}.norm1.weight"),
                N1Beta  = Req(r, $"{prefix}.norm1.bias"),
                N2Gamma = Req(r, $"{prefix}.norm2.weight"),
                N2Beta  = Req(r, $"{prefix}.norm2.bias"),
            };
        }
        return w;
    }

    public static (int present, int missing, List<string> missingNames) Audit(
        WeightReader r, VerifierTransformerPort.Config cfg = null)
    {
        cfg = cfg ?? new VerifierTransformerPort.Config();
        var expected = new List<string>();
        expected.Add("verifier.edge_feature_emb.weight");
        expected.Add("verifier.mlp_out.weight");
        for (int i = 0; i < cfg.NumLayers; i++)
        {
            var p = $"verifier.transformer_encoder.layers.{i}";
            expected.Add($"{p}.self_attn.in_proj_weight");
            expected.Add($"{p}.self_attn.out_proj.weight");
            expected.Add($"{p}.linear1.weight");
            expected.Add($"{p}.linear2.weight");
            expected.Add($"{p}.norm1.weight");
            expected.Add($"{p}.norm2.weight");
        }
        var have = new HashSet<string>(r.Names);
        var missing = new List<string>();
        int present = 0;
        foreach (var n in expected)
        {
            if (have.Contains(n)) present++;
            else missing.Add(n);
        }
        return (present, missing.Count, missing);
    }

    private static float[] Req(WeightReader r, string name)
    {
        try { return r.GetFloat32(name); }
        catch (KeyNotFoundException) { throw new InvalidOperationException(
            $"required verifier tensor missing: {name}"); }
    }

    private static float[] Try(WeightReader r, string name)
    {
        foreach (var n in r.Names) if (n == name) return r.GetFloat32(name);
        return null;
    }
}
