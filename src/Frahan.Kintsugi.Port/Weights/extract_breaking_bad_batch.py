"""
Batch-extract many Breaking Bad samples to FRKINTSU .bin files for the
Frahan Kintsugi "Load BB Sample" component.

Companion to extract_breaking_bad_sample.py (single-sample). Filters the
everyday/val set by fragment count and writes one .bin per match, named
bb_<id>_<nparts>frag.bin, so you can validate the Port pipeline across many
real (in-distribution) samples instead of fighting synthetic data.

USAGE
    python extract_breaking_bad_batch.py \\
        --pc-zip  D:\\code_ws\\Template-General\\outputs\\2026-05-22\\reference\\pc_data.zip \\
        --out-dir D:\\code_ws\\Template-General\\outputs\\2026-05-22\\reference\\bb_batch \\
        --min-parts 2 --max-parts 4 --limit 20

Each .bin contains (FRKINTSU, float32), matching the single-sample tool:
    bb.input.point_clouds   [P, 1000, 3]   (from part_pcs_gt, GT-aligned)
    bb.input.part_valids    [20]
    bb.input.num_parts      [1]
    bb.input.ref_part       [20]            (anchor mask)

Wire a .bin into Load BB Sample -> Frahan Kintsugi (Port mode). Recommended
settings (validated 2026-05-24): Vt=0.5, Diffusion Steps=100, Point Clouds
input wired, Use TorchSharp=True. 2-3 fragment samples assemble most reliably.
"""
from __future__ import annotations

import argparse
import io
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


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--pc-zip", required=True, type=Path)
    p.add_argument("--out-dir", required=True, type=Path)
    p.add_argument("--subset", default="everyday/val",
                   help="zip subpath under data/pc_data/ (default everyday/val)")
    p.add_argument("--min-parts", type=int, default=2)
    p.add_argument("--max-parts", type=int, default=4)
    p.add_argument("--limit", type=int, default=20,
                   help="stop after writing this many matching samples")
    args = p.parse_args()

    prefix = f"data/pc_data/{args.subset}/"
    print(f"scanning {prefix} in {args.pc_zip} for {args.min_parts}-{args.max_parts} parts ...")

    written = 0
    scanned = 0
    rows = []
    with zipfile.ZipFile(args.pc_zip) as z:
        entries = sorted(n for n in z.namelist()
                         if n.startswith(prefix) and n.endswith(".npz"))
        for entry in entries:
            if written >= args.limit:
                break
            scanned += 1
            try:
                with z.open(entry) as src:
                    d = np.load(io.BytesIO(src.read()))
                    # part count: prefer num_parts; else infer from pc shape.
                    if "num_parts" in d.files:
                        nparts = int(np.asarray(d["num_parts"]).reshape(-1)[0])
                    elif "part_pcs_gt" in d.files:
                        nparts = int(d["part_pcs_gt"].shape[0])
                    else:
                        continue
                    if nparts < args.min_parts or nparts > args.max_parts:
                        continue
                    if "part_pcs_gt" not in d.files:
                        continue
                    pc = d["part_pcs_gt"].astype(np.float32)  # [P, 1000, 3]
                    tensors = {"bb.input.point_clouds": pc}
                    for src_key, dst in [("part_valids", "bb.input.part_valids"),
                                          ("num_parts", "bb.input.num_parts"),
                                          ("ref_part", "bb.input.ref_part")]:
                        if src_key in d.files:
                            a = np.asarray(d[src_key]).astype(np.float32)
                            if a.ndim == 0:
                                a = a.reshape(1)
                            tensors[dst] = a
                    sid = entry.split("/")[-1].replace(".npz", "")
                    cat = str(d["category"]) if "category" in d.files else "?"
                    out = args.out_dir / f"bb_{sid}_{nparts}frag.bin"
                    export(out, tensors)
                    written += 1
                    rows.append((sid, nparts, cat, out.name))
            except Exception as ex:
                print(f"  skip {entry}: {type(ex).__name__}: {ex}")

    print(f"\nscanned {scanned} entries, wrote {written} .bin files to {args.out_dir}")
    if rows:
        print(f"{'id':>8}  {'parts':>5}  {'category':<10}  file")
        for sid, n, cat, name in rows:
            print(f"{sid:>8}  {n:>5}  {cat:<10}  {name}")
    print("\nNext: point Load BB Sample -> Sample File at one of these .bin files;")
    print("Port mode, Vt=0.5, Diffusion Steps=100, Point Clouds wired, TorchSharp=True.")


if __name__ == "__main__":
    main()
