# Frahan.Kintsugi.Port

C# port of [PuzzleFusion++](https://github.com/eric-zqwang/puzzlefusion-plusplus)
(Wang, Chen, Furukawa; ICLR 2025; arXiv:2406.00259): learned 3D
fracture reassembly (denoise-and-verify) for the Frahan Kintsugi
workflow.

## Licence (read this first)

**Research-use only / non-commercial.** The upstream LICENSE
(vendored at `Weights/puzzlefusion-plusplus/LICENSE`) states that
the code, data and model weights are not allowed for commercial
usage, and that for research purposes the terms follow GPLv3. The
same terms cover this ported code AND the converted weights
(`kintsugi.bin`). This module is therefore planned as a separately
distributed, optional research package; it is not part of any
commercial Frahan offering. See `LICENSE.txt` in this folder.

## Status

The full port ships:

- `Primitives/` — FPS, KNN, SIMD matmul, activations, LayerNorm.
- `Models/` — PointNet++ encoder, VQ-VAE, SE(3) diffusion denoiser
  (AdaLN transformer), pairwise alignment verifier.
- `Outer/` — auto-agglomerative merge loop, weight loaders,
  `KintsugiPortInference`, TorchSharp execution paths.
- `Weights/` — `FRKINTSU` binary format, checkpoint conversion and
  parity-fixture scripts.

Layer-by-layer parity tests against upstream PyTorch fixtures pass
on Breaking Bad data (`parity_fixtures.bin`; tests in
`Frahan.StonePack.Tests`). Example 14 (Kintsugi reassembly)
demonstrates the verifier scoring at 0.71 on Breaking Bad
fragments. Parity holds on Breaking Bad data only; synthetic
fractures are out of the verified envelope.

## Runtime

TorchSharp 0.105.0 is referenced in the csproj and loads on net48
(Rhino 8 Grasshopper). Default backend is CPU
(`TorchSharp-cpu`). CUDA libtorch is opt-in: build with
`-p:KintsugiUseCuda=true` to pull `TorchSharp-cuda-windows`
(multi-GB download; CUDA 12.x GPU + driver required). The denoiser
path auto-selects CUDA at runtime when available. The pure-managed
`Primitives/` path remains as a dependency-free fallback and as the
reference for parity tests.

(The earlier statement here that "TorchSharp targets .NET 6+; not
usable" was wrong and is retired: TorchSharp 0.105.0 works under
net48 in this project.)

## Weights

`kintsugi.bin` (~267 MB) is converted from the upstream checkpoints
with `Weights/convert_pytorch_checkpoint.py` and deployed beside the
`.gha`. It is NOT committed to this repo. It inherits the upstream
non-commercial / research-only terms. See `Weights/README.md` for
the binary format and the ship workflow.

## Credit

This module is a reimplementation, not a fork: the upstream PyTorch
code was ported to C#/.NET by hand and verified layer-by-layer
against upstream fixtures. All algorithmic credit belongs to the
PuzzleFusion++ authors. The vendored `Jigsaw_matching` subtree under
`Weights/puzzlefusion-plusplus/` has its own notice
(`Weights/puzzlefusion-plusplus/NOTICE.md`).
