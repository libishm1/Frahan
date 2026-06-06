"""
Extract ONE Breaking Bad sample from pc_data.zip and write it to a
FRKINTSU-format binary that our C# tests + Mode=Port can load.

USAGE
    python extract_breaking_bad_sample.py \\
        --pc-zip D:\\code_ws\\Template-General\\outputs\\2026-05-22\\reference\\pc_data.zip \\
        --sample 00697 \\
        --out    D:\\code_ws\\Template-General\\outputs\\2026-05-22\\reference\\bb_sample_00697.bin

The .npz format in pc_data/everyday/val/ contains (per the upstream
Breaking Bad data prep convention):
    'pc'         : float32 [P, 1000, 3]  -- P fragments x 1000 points x 3
    'part_valids': float32 [P]            -- validity mask (1=valid)
    'num_parts'  : int                    -- total parts
    'graph'      : adjacency / matching ground truth (optional)
    'scale'      : per-part scale factor

The script writes a FRKINTSU binary with:
    bb.input.point_clouds   [P, 1000, 3]  float32
    bb.input.num_parts      [1]           float32 (cast)
    bb.input.scale          [P]           float32 (if available)
    bb.input.gt_trans       [P, 3]        float32 (if available)
    bb.input.gt_rots        [P, 4]        float32 (if available)

The downstream C# test consumes bb.input.point_clouds as per-fragment
1000-point clouds, runs the Mode=Port pipeline, reports verifier
scores + per-pose deltas vs ground truth (when available).
"""
from __future__ import annotations

import argparse
import struct
import zipfile
from pathlib import Path

import numpy as np

MAGIC = b"FRKINTSU"
VERSION = 1
DTYPE_FLOAT32 = 1


def write_tensor(f, name: str, arr: np.ndarray):
    name_bytes = name.encode("utf-8")
    arr = np.ascontiguousarray(arr, dtype=np.float32)
    shape = list(arr.shape)
    f.write(struct.pack("<H", len(name_bytes)))
    f.write(name_bytes)
    f.write(struct.pack("<BB", DTYPE_FLOAT32, len(shape)))
    for d in shape:
        f.write(struct.pack("<I", d))
    f.write(arr.tobytes())


def export(out_path: Path, tensors: dict):
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "wb") as f:
        f.write(MAGIC)
        f.write(struct.pack("<I", VERSION))
        f.write(struct.pack("<I", len(tensors)))
        f.write(struct.pack("<Q", 0))
        for name, t in tensors.items():
            write_tensor(f, name, t)
    print(f"wrote {out_path} ({out_path.stat().st_size:,} bytes, {len(tensors)} tensors)")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--pc-zip", required=True, type=Path)
    p.add_argument("--sample", required=True, type=str,
                   help="Sample id (e.g. 00697). The script picks "
                        "data/pc_data/everyday/val/<sample>.npz.")
    p.add_argument("--out", required=True, type=Path)
    args = p.parse_args()

    entry = f"data/pc_data/everyday/val/{args.sample}.npz"
    print(f"extracting {entry} from {args.pc_zip}")
    with zipfile.ZipFile(args.pc_zip) as z:
        names = set(z.namelist())
        if entry not in names:
            available = [n for n in names if n.startswith("data/pc_data/everyday/val/")][:10]
            print(f"ERROR: {entry} not in zip. First 10 val entries:")
            for n in available: print(f"  {n}")
            return
        with z.open(entry) as src:
            with np.load(src) as data:
                print("npz contents:")
                for k in data.files:
                    arr = data[k]
                    print(f"  {k}: shape={arr.shape} dtype={arr.dtype}")
                tensors = {}
                # Find the per-fragment point cloud array. The Breaking Bad
                # everyday val set stores it as 'part_pcs_gt' [P, 1000, 3]
                # -- the ground-truth aligned point clouds.
                for k in ("part_pcs_gt", "part_pcs", "pc", "pcs", "points"):
                    if k in data.files:
                        pc = data[k].astype(np.float32)
                        tensors["bb.input.point_clouds"] = pc
                        print(f"  -> exporting bb.input.point_clouds {pc.shape} from key '{k}'")
                        break
                for src_key, dst_name in [
                    ("part_valids", "bb.input.part_valids"),
                    ("part_scale", "bb.input.scale"),
                    ("scale", "bb.input.scale"),
                    ("gt_trans", "bb.input.gt_trans"),
                    ("part_trans", "bb.input.gt_trans"),
                    ("gt_rots", "bb.input.gt_rots"),
                    ("part_rots", "bb.input.gt_rots"),
                    ("ref_part", "bb.input.ref_part"),
                    ("num_parts", "bb.input.num_parts"),
                ]:
                    if src_key in data.files and dst_name not in tensors:
                        arr = np.asarray(data[src_key]).astype(np.float32)
                        if arr.ndim == 0:
                            arr = arr.reshape(1)
                        tensors[dst_name] = arr
                        print(f"  -> {dst_name} {arr.shape} from key '{src_key}'")
                if not tensors:
                    print("ERROR: nothing exported -- no point cloud key matched")
                    return
                export(args.out, tensors)
                print("\nSummary:")
                if "bb.input.point_clouds" in tensors:
                    pc = tensors["bb.input.point_clouds"]
                    print(f"  fragments: {pc.shape[0]}")
                    print(f"  points/fragment: {pc.shape[1]}")
                    print(f"  bbox per fragment: min/max samples")
                    for i in range(min(3, pc.shape[0])):
                        print(f"    frag {i}: bbox min={pc[i].min(axis=0)}, max={pc[i].max(axis=0)}")


if __name__ == "__main__":
    main()
