# Kintsugi Port parity: the denoiser attention-mask fix (2026-07-13)

Research note. The `Frahan.Kintsugi.Port` is a from-scratch C# port of
PuzzleFusion++ (learned SE(3) fracture reassembly). This records the parity work
that brought its diffusion denoiser to bit-exact faithfulness with the reference,
and the current, honestly-scoped capability.

## The bug and the fix

Benchmarked the port against the reference PuzzleFusion++ (Python) on
in-distribution Breaking Bad val samples with an oracle-to-port tensor-parity
ladder. The reference single-pass denoiser reassembles small samples at
part_acc 1.0; the port was 78-127 deg off. Encoder, VQ, conditioning (pre-layer0),
adaLN, weights, attention scale, and head layout all matched. The divergence was
isolated to `self_attn`: after-norm1 matched exactly, after-self_attn was 11.66%
off.

Cause: the reference runs on torch 2.x, so diffusers 0.21.4 uses
`AttnProcessor2_0` -> `F.scaled_dot_product_attention`, whose BOOL attn_mask
blocks False positions with `-inf` (hard). Both port denoiser paths (manual
`MultiHeadAttention` and `TorchSharpDenoiserPath`) instead ADDED the bool mask as
a `+1` soft bias, letting cross-fragment self-attention leak. The block-diagonal
`self_mask` is meant to restrict each fragment's tokens to attend only within
themselves.

Fix (`mask==0 -> -1e9` in both paths; plus GEGLU tanh-approx -> exact erf gelu):
the manual transformer residuals are now `0.0000` vs the reference (was 6.13);
all six layers match to 0.00%.

## Current capability (honest scope)

- IN-DISTRIBUTION reassembly WORKS: 2-fragment sample 0.9 deg rotation error,
  3-fragment 2.7 deg (were 126 / 78). The Port now reproduces the reference on
  small assemblies.
- LARGER 4-8 fragment assemblies still flip on some random seeds. This is
  noise/convergence, not an implementation bug: a different seed reassembles the
  same sample correctly, and the reference itself scores below 1.0 single-pass on
  hard samples. The paper's remedy is the auto-agglomeration multi-pass, which
  the port does not yet run.
- SYNTHETIC (planar Voronoi cuts) is OUT-OF-DISTRIBUTION for the model (trained
  on curved, textured fracture surfaces). The DETERMINISTIC escalation solver
  (FacetMatch + Roughen v4) remains the primary path for synthetic and quarry
  cuts (N=2/3/5/8 at 0.0% error).
- The paper-exact TorchSharp/libtorch denoiser LOADS and runs standalone, but is
  BLOCKED inside Rhino by a TorchSharp version conflict: LunchBox's ML package
  ships TorchSharp 0.101.5 and loads it first, shadowing Frahan's 0.105.0 in
  CoreCLR's default AssemblyLoadContext. The manual C# denoiser (now mask-fixed,
  faithful) is the in-Rhino path until that is resolved.

## Status

Research-only (PuzzleFusion++ is GPLv3 research-use, not commercial). The
deterministic reassembler is the shipping path; the learned Port is a validated
research capability on in-distribution artifact fragments.
