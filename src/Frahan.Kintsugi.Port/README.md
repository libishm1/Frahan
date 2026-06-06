# Frahan.Kintsugi.Port

Direct C# port of [PuzzleFusion++](https://github.com/eric-zqwang/puzzlefusion-plusplus)
(Wang, Chen, Furukawa; ICLR 2025; arXiv:2406.00259).

**License: GPL-3.0** (see `LICENSE.txt`). The upstream Python
implementation is GPL-3.0; this port inherits the licence per
viral-copyleft. Any binary distribution that links this assembly
falls under GPL-3.0.

## Status

Phase 1 of 8 — primitives layer.

| Phase | Scope | Status |
|---|---|---|
| 1 | Primitives — FPS, KNN, Matmul, Activations, LayerNorm | **in progress** |
| 2 | PointNet++ piece encoder | not started |
| 3 | VQ-VAE codebook | not started |
| 4 | SE(3) diffusion denoiser | not started |
| 5 | Pairwise alignment verifier | not started |
| 6 | Auto-agglomerative merge + iterate | not started |
| 7 | Weight conversion pipeline | not started |
| 8 | Integration into `KintsugiAssemblyComponent` | not started |

Full plan: `wiki/research/kintsugi_3d_fracture_reassembly.md`
(section "ADDED 2026-05-22: full PyTorch → C# port roadmap").

## Module layout

```
Frahan.Kintsugi.Port/
├── Frahan.Kintsugi.Port.csproj   net48; depends on System.Numerics.Vectors only
├── LICENSE.txt                   GPL-3.0 + upstream citation
├── README.md                     this file
├── Primitives/                   Phase 1
│   ├── Fps.cs                    furthest-point sampling
│   ├── Knn.cs                    k-nearest neighbours (brute force; KD-tree later)
│   ├── Matmul.cs                 dense matrix-matrix multiply (SIMD-accelerated)
│   ├── Activations.cs            GELU, SiLU, ReLU
│   └── LayerNorm.cs              per-token layer normalisation
├── Models/                       Phases 2-5 (not started)
│   ├── PointNetPlusPlus.cs
│   ├── VqVae.cs
│   ├── Se3Denoiser.cs
│   └── Verifier.cs
├── Outer/                        Phase 6 (not started)
│   └── AutoAgglomerate.cs
└── Weights/                      Phase 7 (not started)
    ├── WeightReader.cs           binary .pt → C# tensor
    └── (no checkpoint files in git; downloaded at first use)
```

## Design constraints

- **net48 only.** TorchSharp targets .NET 6+; not usable. Pure managed
  C# with System.Numerics.Vectors SIMD for matmul. No P/Invoke to
  native ML libraries.
- **In-process inference.** No subprocess to Python. No GPU dependency.
  Expect 10-60 seconds per assembly on a modern CPU (vs ~929 ms on
  RTX 4090 in the original paper).
- **No third-party ML packages.** No ML.NET, no TensorFlow.NET, no
  ONNX Runtime, no LibTorch P/Invoke. Pure managed implementations of
  every primitive.

## Why these constraints

Frahan ships as a Rhino 8 Grasshopper `.gha`. Rhino 8's plug-in
runtime is net48. Anything heavier than pure managed C# either won't
load or requires the end user to install a separate Python / CUDA /
ML.NET environment — defeats the "drag-and-drop .gha" distribution
model.

## Performance expectation

The original PuzzleFusion++ reports ~929 ms per assembly on an RTX
4090. Pure managed C# matmul on CPU is realistically 10-100× slower
than CUDA. Expected port performance: **10-60 seconds per assembly**
on a modern desktop CPU.

This is acceptable as a "Run, then check back" workflow in Grasshopper
via `GH_TaskCapableComponent`. Not acceptable as a real-time canvas
solver.

## Verification

Once Phases 2-5 land, port equivalence must be tested against the
Python original on a held-out subset of the Breaking Bad dataset.
Without that gate, we cannot claim parity with the paper's metrics.

## Currently shipped

Phase 1 primitives (this commit). Tests in `Frahan.StonePack.Tests`.
