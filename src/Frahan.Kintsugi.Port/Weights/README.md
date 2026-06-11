# Frahan.Kintsugi.Port / Weights

Research-use-only / non-commercial module (upstream PuzzleFusion++
terms; GPLv3 terms for research use — see `../LICENSE.txt`). Tools +
format for the weight binary the C# port loads at runtime.

## Files

| File | Purpose | Run on |
|---|---|---|
| `WeightReader.cs` | C# loader for the `FRKINTSU` binary format | net48 (.gha) |
| `convert_pytorch_checkpoint.py` | Convert upstream PyTorch `.ckpt` → `kintsugi.bin` | Python 3 with `torch` + `omegaconf` |
| `export_parity_fixtures.py` | Capture input + reference outputs for layer-by-layer parity testing | Python 3 with the upstream `puzzlefusion-plusplus` package importable |

## Binary format (FRKINTSU v1)

```
HEADER (32 bytes)
  magic     "FRKINTSU"  (8 bytes ASCII)
  version   uint32      (currently 1)
  count     uint32      (number of tensors)
  reserved  uint64

REPEATED count TIMES:
  name_len  uint16
  name      utf-8       (name_len bytes)
  dtype     uint8       (1 = float32, 2 = float16)
  rank      uint8       (number of dimensions; max 8)
  shape     uint32[rank]
  data      row-major bytes
```

## Ship workflow (one-time per release)

1. Download the upstream PuzzleFusion++ checkpoints from
   <https://github.com/eric-zqwang/puzzlefusion-plusplus/blob/main/docs/data_preparation.md>
   (Google Drive link in that doc; ~610 MB zip).
2. Unzip the `output/` folder somewhere local.
3. Run the conversion:
   ```
   pip install torch omegaconf
   python convert_pytorch_checkpoint.py --root path/to/extracted/output --out kintsugi.bin
   ```
4. Drop `kintsugi.bin` into the `.gha` deploy folder:
   `%APPDATA%/Grasshopper/Libraries/Frahan.StonePack.MeshHeightmap/kintsugi.bin`
5. In Grasshopper, set `Frahan Kintsugi` component's `Use Port Mode = True`.

## Why kintsugi.bin is NOT in this git repo

The merged binary is ~267 MB. Three reasons we don't commit it:

1. **Upstream licence**: the weights are derivative of the upstream
   PuzzleFusion++ checkpoint, which is research-use-only /
   non-commercial (GPLv3 terms for research use). Distributing them
   attaches the upstream terms by virtue of the conversion. They ship
   only in the separately distributed research package, never in a
   commercial bundle; keeping them out of git keeps that boundary
   clean.
2. **Repo size**: a 267 MB binary in LFS would slow every clone.
3. **Update cadence**: when upstream re-trains, we re-run the conversion;
   the binary changes. Better to make conversion a release-time step.

## Tensor name conventions

After conversion (and Conv2d 1×1 squeezing), tensors follow PyTorch
state-dict naming with one of three top-level prefixes:

- `ae.*` — autoencoder (standalone PointNet++ encoder)
- `denoiser.*` — SE(3) diffusion denoiser transformer
- `encoder.*` — copy of the autoencoder embedded in the denoiser checkpoint
- `verifier.*` — pairwise alignment verifier transformer

Example tensor names + shapes (from the actual ICLR 2025 release):

| Name | Shape | Source model |
|---|---|---|
| `ae.pn2.sa1.mlp_convs.0.weight` | [64, 3] (was [64,3,1,1]) | PointNet++ set abs 1, conv 0 |
| `ae.pn2.sa1.mlp_bns.0.weight` | [64] | BatchNorm |
| `denoiser.transformer_layers.0.self_attn.to_q.weight` | [512, 512] | denoiser layer 0 query proj |
| `denoiser.transformer_layers.0.norm1.emb.weight` | [3072, 512] | denoiser AdaLN time conditioning |
| `verifier.transformer_encoder.layers.0.self_attn.in_proj_weight` | [768, 256] | verifier QKV concat |

## Parity-test gate (Phase 7 validation)

`export_parity_fixtures.py` captures input + reference outputs from
the upstream PyTorch models. The matching C# parity tests load the
same binary (`parity_fixtures.bin`), run the C# port models, and
assert outputs match the upstream reference. These layer-by-layer
parity tests pass on Breaking Bad fixtures; example 14 demonstrates
the verifier scoring 0.71 on Breaking Bad fragments.

## Last updated

2026-06-11.
