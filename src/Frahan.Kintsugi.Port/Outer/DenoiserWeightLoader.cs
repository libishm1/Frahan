#nullable disable
using System;
using System.Collections.Generic;
using Frahan.Kintsugi.Port.Models;
using Frahan.Kintsugi.Port.Weights;

namespace Frahan.Kintsugi.Port.Outer;

/// <summary>
/// Maps PuzzleFusion++ denoiser tensors out of a loaded kintsugi.bin
/// into DenoiserTransformerPort.Weights ready for Forward().
///
/// Upstream architecture (denoiser/model/modules/denoiser_transformer.py):
///   6 EncoderLayer blocks, each with:
///     norm1: MyAdaLayerNorm
///     self_attn: diffusers Attention (to_q/to_k/to_v/to_out.0)
///     norm2: MyAdaLayerNorm
///     global_attn: diffusers Attention
///     norm3: nn.LayerNorm (affine)
///     ff: FeedForward = [GEGLU, Dropout, Linear]
///         ff.net.0.proj.weight = GEGLU's inner Linear (2*4D, D)
///         ff.net.2.weight = output Linear (D, 4D)
///   shape_embedding, param_fc, ref_part_emb, pos_encoding
///   mlp_out_trans, mlp_out_rot (3-layer SiLU MLPs each)
/// </summary>
public static class DenoiserWeightLoader
{
    public static DenoiserTransformerPort.Config DefaultConfig() => new DenoiserTransformerPort.Config();

    public static DenoiserTransformerPort.Weights LoadWeights(WeightReader r, DenoiserTransformerPort.Config cfg = null)
    {
        cfg = cfg ?? new DenoiserTransformerPort.Config();
        int D = cfg.EmbedDim;
        int L = cfg.NumPoint;
        int Dh = D / 2;
        int Din = cfg.NumDim;
        var w = new DenoiserTransformerPort.Weights
        {
            Layers = new DenoiserEncoderLayerPort.Weights[cfg.NumLayers],
        };

        // ---- Per-layer
        for (int i = 0; i < cfg.NumLayers; i++)
        {
            var lw = new DenoiserEncoderLayerPort.Weights
            {
                // norm1
                N1EmbWeight = Req(r, $"denoiser.transformer_layers.{i}.norm1.emb.weight"),
                N1LinearW   = Req(r, $"denoiser.transformer_layers.{i}.norm1.linear.weight"),
                N1LinearB   = Try(r, $"denoiser.transformer_layers.{i}.norm1.linear.bias"),
                // self_attn
                SelfWq = Req(r, $"denoiser.transformer_layers.{i}.self_attn.to_q.weight"),
                SelfBq = Try(r, $"denoiser.transformer_layers.{i}.self_attn.to_q.bias"),
                SelfWk = Req(r, $"denoiser.transformer_layers.{i}.self_attn.to_k.weight"),
                SelfBk = Try(r, $"denoiser.transformer_layers.{i}.self_attn.to_k.bias"),
                SelfWv = Req(r, $"denoiser.transformer_layers.{i}.self_attn.to_v.weight"),
                SelfBv = Try(r, $"denoiser.transformer_layers.{i}.self_attn.to_v.bias"),
                SelfWo = Req(r, $"denoiser.transformer_layers.{i}.self_attn.to_out.0.weight"),
                SelfBo = Try(r, $"denoiser.transformer_layers.{i}.self_attn.to_out.0.bias"),
                // norm2
                N2EmbWeight = Req(r, $"denoiser.transformer_layers.{i}.norm2.emb.weight"),
                N2LinearW   = Req(r, $"denoiser.transformer_layers.{i}.norm2.linear.weight"),
                N2LinearB   = Try(r, $"denoiser.transformer_layers.{i}.norm2.linear.bias"),
                // global_attn
                GlobalWq = Req(r, $"denoiser.transformer_layers.{i}.global_attn.to_q.weight"),
                GlobalBq = Try(r, $"denoiser.transformer_layers.{i}.global_attn.to_q.bias"),
                GlobalWk = Req(r, $"denoiser.transformer_layers.{i}.global_attn.to_k.weight"),
                GlobalBk = Try(r, $"denoiser.transformer_layers.{i}.global_attn.to_k.bias"),
                GlobalWv = Req(r, $"denoiser.transformer_layers.{i}.global_attn.to_v.weight"),
                GlobalBv = Try(r, $"denoiser.transformer_layers.{i}.global_attn.to_v.bias"),
                GlobalWo = Req(r, $"denoiser.transformer_layers.{i}.global_attn.to_out.0.weight"),
                GlobalBo = Try(r, $"denoiser.transformer_layers.{i}.global_attn.to_out.0.bias"),
                // norm3
                N3Gamma = Req(r, $"denoiser.transformer_layers.{i}.norm3.weight"),
                N3Beta  = Req(r, $"denoiser.transformer_layers.{i}.norm3.bias"),
                // FFN
                FfGegluW = Req(r, $"denoiser.transformer_layers.{i}.ff.net.0.proj.weight"),
                FfGegluB = Try(r, $"denoiser.transformer_layers.{i}.ff.net.0.proj.bias"),
                FfOutW   = Req(r, $"denoiser.transformer_layers.{i}.ff.net.2.weight"),
                FfOutB   = Try(r, $"denoiser.transformer_layers.{i}.ff.net.2.bias"),
            };
            w.Layers[i] = lw;
        }

        // ---- Input embedders + heads
        w.ShapeEmbW = Req(r, "denoiser.shape_embedding.weight");
        w.ShapeEmbB = Try(r, "denoiser.shape_embedding.bias");
        w.ParamFcW  = Req(r, "denoiser.param_fc.weight");
        w.ParamFcB  = Try(r, "denoiser.param_fc.bias");
        w.RefPartEmbWeight = Req(r, "denoiser.ref_part_emb.weight");

        // mlp_out_trans is a Sequential of 3 Linears (interleaved SiLU).
        w.TransL1W = Req(r, "denoiser.mlp_out_trans.0.weight");
        w.TransL1B = Try(r, "denoiser.mlp_out_trans.0.bias");
        w.TransL2W = Req(r, "denoiser.mlp_out_trans.2.weight");
        w.TransL2B = Try(r, "denoiser.mlp_out_trans.2.bias");
        w.TransL3W = Req(r, "denoiser.mlp_out_trans.4.weight");
        w.TransL3B = Try(r, "denoiser.mlp_out_trans.4.bias");
        // mlp_out_rot mirrors mlp_out_trans.
        w.RotL1W = Req(r, "denoiser.mlp_out_rot.0.weight");
        w.RotL1B = Try(r, "denoiser.mlp_out_rot.0.bias");
        w.RotL2W = Req(r, "denoiser.mlp_out_rot.2.weight");
        w.RotL2B = Try(r, "denoiser.mlp_out_rot.2.bias");
        w.RotL3W = Req(r, "denoiser.mlp_out_rot.4.weight");
        w.RotL3B = Try(r, "denoiser.mlp_out_rot.4.bias");
        return w;
    }

    /// <summary>
    /// Diagnostic: list which denoiser tensors are present vs missing.
    /// Useful for debugging weight-name mismatches.
    /// </summary>
    public static (int present, int missing, List<string> missingNames) Audit(WeightReader r, DenoiserTransformerPort.Config cfg = null)
    {
        cfg = cfg ?? new DenoiserTransformerPort.Config();
        var expected = new List<string>();
        for (int i = 0; i < cfg.NumLayers; i++)
        {
            expected.Add($"denoiser.transformer_layers.{i}.norm1.emb.weight");
            expected.Add($"denoiser.transformer_layers.{i}.norm1.linear.weight");
            expected.Add($"denoiser.transformer_layers.{i}.self_attn.to_q.weight");
            expected.Add($"denoiser.transformer_layers.{i}.self_attn.to_k.weight");
            expected.Add($"denoiser.transformer_layers.{i}.self_attn.to_v.weight");
            expected.Add($"denoiser.transformer_layers.{i}.self_attn.to_out.0.weight");
            expected.Add($"denoiser.transformer_layers.{i}.norm2.emb.weight");
            expected.Add($"denoiser.transformer_layers.{i}.norm2.linear.weight");
            expected.Add($"denoiser.transformer_layers.{i}.global_attn.to_q.weight");
            expected.Add($"denoiser.transformer_layers.{i}.global_attn.to_k.weight");
            expected.Add($"denoiser.transformer_layers.{i}.global_attn.to_v.weight");
            expected.Add($"denoiser.transformer_layers.{i}.global_attn.to_out.0.weight");
            expected.Add($"denoiser.transformer_layers.{i}.norm3.weight");
            expected.Add($"denoiser.transformer_layers.{i}.norm3.bias");
            expected.Add($"denoiser.transformer_layers.{i}.ff.net.0.proj.weight");
            expected.Add($"denoiser.transformer_layers.{i}.ff.net.2.weight");
        }
        expected.AddRange(new[]
        {
            "denoiser.shape_embedding.weight",
            "denoiser.param_fc.weight",
            "denoiser.ref_part_emb.weight",
            "denoiser.mlp_out_trans.0.weight",
            "denoiser.mlp_out_trans.2.weight",
            "denoiser.mlp_out_trans.4.weight",
            "denoiser.mlp_out_rot.0.weight",
            "denoiser.mlp_out_rot.2.weight",
            "denoiser.mlp_out_rot.4.weight",
        });
        var have = new HashSet<string>(r.Names);
        var missing = new List<string>();
        int presentCount = 0;
        foreach (var n in expected)
        {
            if (have.Contains(n)) presentCount++;
            else missing.Add(n);
        }
        return (presentCount, missing.Count, missing);
    }

    private static float[] Req(WeightReader r, string name)
    {
        try { return r.GetFloat32(name); }
        catch (KeyNotFoundException) { throw new InvalidOperationException(
            $"required tensor missing from kintsugi.bin: {name}"); }
    }

    private static float[] Try(WeightReader r, string name)
    {
        foreach (var n in r.Names) if (n == name) return r.GetFloat32(name);
        return null;
    }
}
