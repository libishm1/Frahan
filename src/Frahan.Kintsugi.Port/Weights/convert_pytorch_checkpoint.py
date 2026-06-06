"""
PuzzleFusion++ PyTorch checkpoints -> Frahan.Kintsugi.Port binary format.

Combines all three upstream checkpoints (autoencoder, denoiser, verifier)
into one kintsugi.bin file that Frahan.Kintsugi.Port.Weights.WeightReader
loads at runtime.

Usage:
    pip install torch omegaconf
    python convert_pytorch_checkpoint.py \
        --ae   path/to/output/autoencoder/.../last.ckpt \
        --den  path/to/output/denoiser/.../last.ckpt   \
        --ver  path/to/output/verifier/.../last.ckpt   \
        --out  kintsugi.bin

OR, with the upstream zip structure:
    python convert_pytorch_checkpoint.py --root path/to/extracted/output/ --out kintsugi.bin

File format (matches WeightReader.cs):
    magic "FRKINTSU" + version + count + reserved
    per tensor: name_len, name, dtype, rank, shape, data
    dtype 1 = float32, 2 = float16

State-dict translation:
    autoencoder:  ae.* (we use this as the standalone encoder weights)
    denoiser:     denoiser.* + encoder.* (the denoiser ships its own copy of the
                  PN++ encoder; we keep BOTH so the runtime can pick whichever
                  pipeline mode it wants)
    verifier:     verifier.*

    Conv2d 1x1 weights with shape (Cout, Cin, 1, 1) are squeezed to (Cout, Cin)
    so the C# Matmul primitive can use them directly.

    BatchNorm running stats are kept verbatim (weight, bias, running_mean,
    running_var) so the port can apply BN at inference. num_batches_tracked
    is dropped (not needed at inference).

    Lightning bookkeeping keys (epoch, global_step, optimizer_states, etc.)
    are filtered out -- only state_dict tensors are written.

GPL-3.0 (matches Frahan.Kintsugi.Port).
"""
from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

try:
    import torch
except ImportError:
    sys.exit("PyTorch missing -- pip install torch")
try:
    import omegaconf
except ImportError:
    print("WARN: omegaconf not installed; load may fail if the checkpoint stores hyperparameter configs.")

MAGIC = b"FRKINTSU"
VERSION = 1
DTYPE_FLOAT32 = 1
DTYPE_FLOAT16 = 2

# Keys to drop entirely. Anything PyTorch Lightning saves alongside the
# state_dict that we don't need at inference.
DROP_SUFFIXES = (".num_batches_tracked",)


def squeeze_conv2d_1x1(name: str, tensor: "torch.Tensor"):
    """If a tensor is a Conv2d weight of shape (Cout, Cin, 1, 1), squeeze
    it to (Cout, Cin) so our C# Matmul can use it without 4-D plumbing.

    Returns (new_name, new_tensor). Name is left unchanged; only the
    tensor shape is squeezed. The port-side accessor is shape-aware.
    """
    if name.endswith(".weight") and tensor.ndim == 4 and tensor.shape[2:] == (1, 1):
        return name, tensor.squeeze(-1).squeeze(-1).contiguous()
    return name, tensor


def collect_state_dict(label: str, ckpt_path: Path, prefix: str = "") -> dict:
    """Load one Lightning checkpoint and return its tensor state_dict with
    an optional prefix prepended to every key. Filters lightning bookkeeping."""
    print(f"  loading {label} from {ckpt_path.name}...", end=" ", flush=True)
    ckpt = torch.load(str(ckpt_path), map_location="cpu", weights_only=False)
    if isinstance(ckpt, dict) and "state_dict" in ckpt:
        ckpt = ckpt["state_dict"]
    if not isinstance(ckpt, dict):
        sys.exit(f"  ! {label}: unexpected top-level type {type(ckpt)}")
    kept = {}
    for k, v in ckpt.items():
        if not isinstance(v, torch.Tensor):
            continue
        if any(k.endswith(s) for s in DROP_SUFFIXES):
            continue
        full = f"{prefix}{k}" if prefix else k
        full, v = squeeze_conv2d_1x1(full, v)
        kept[full] = v
    print(f"{len(kept)} tensors kept")
    return kept


def write_tensor(f, name: str, tensor):
    name_bytes = name.encode("utf-8")
    if len(name_bytes) > 65535:
        raise ValueError(f"Name too long ({len(name_bytes)}): {name}")
    arr = tensor.detach().cpu()
    if arr.dtype not in (torch.float32, torch.float16):
        arr = arr.float()
    if arr.dtype == torch.float32:
        dtype_id = DTYPE_FLOAT32
        elem_size = 4
    else:
        dtype_id = DTYPE_FLOAT16
        elem_size = 2
    shape = list(arr.shape)
    if len(shape) > 8:
        raise ValueError(f"Rank {len(shape)} > 8: {name}")
    f.write(struct.pack("<H", len(name_bytes)))
    f.write(name_bytes)
    f.write(struct.pack("<BB", dtype_id, len(shape)))
    for d in shape:
        f.write(struct.pack("<I", d))
    flat = arr.flatten().contiguous().numpy().tobytes()
    expected = elem_size
    for d in shape:
        expected *= d
    if len(flat) != expected:
        raise RuntimeError(f"Size mismatch for '{name}': expected {expected}, got {len(flat)}")
    f.write(flat)


def write_bin(out_path: Path, tensors: dict):
    print(f"writing {out_path} ...", end=" ", flush=True)
    with open(out_path, "wb") as f:
        f.write(MAGIC)
        f.write(struct.pack("<I", VERSION))
        f.write(struct.pack("<I", len(tensors)))
        f.write(struct.pack("<Q", 0))
        for name, tensor in tensors.items():
            write_tensor(f, name, tensor)
    size = out_path.stat().st_size
    print(f"{size:,} bytes ({len(tensors)} tensors)")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--ae",   type=Path)
    p.add_argument("--den",  type=Path)
    p.add_argument("--ver",  type=Path)
    p.add_argument("--root", type=Path,
                   help="Shortcut for the upstream output/ directory layout.")
    p.add_argument("--out",  type=Path, required=True)
    args = p.parse_args()

    if args.root:
        # Auto-discover the three checkpoints under the upstream layout.
        ae_glob   = list(args.root.glob("autoencoder/*/training/last.ckpt"))
        den_glob  = list(args.root.glob("denoiser/*/training/last.ckpt"))
        ver_glob  = list(args.root.glob("verifier/*/training/last.ckpt"))
        if not ae_glob or not den_glob or not ver_glob:
            sys.exit(f"Could not auto-discover all three checkpoints under {args.root}. "
                     f"Pass --ae / --den / --ver explicitly.")
        args.ae, args.den, args.ver = ae_glob[0], den_glob[0], ver_glob[0]

    if not (args.ae and args.den and args.ver):
        sys.exit("Need --ae --den --ver (or --root) to locate all three checkpoints.")

    all_tensors = {}
    all_tensors.update(collect_state_dict("autoencoder", args.ae))
    all_tensors.update(collect_state_dict("denoiser",    args.den))
    all_tensors.update(collect_state_dict("verifier",    args.ver))

    args.out.parent.mkdir(parents=True, exist_ok=True)
    write_bin(args.out, all_tensors)


if __name__ == "__main__":
    main()
