"""
Scan pc_data.zip's val set, report which samples have N>=K fragments.
Then optionally extract a chosen sample to a FRKINTSU .bin for testing
Frahan Kintsugi on richer multi-piece assembly.

USAGE
    # 1. List samples grouped by fragment count
    python find_bb_samples_by_fragment_count.py \\
        --pc-zip D:\\code_ws\\Template-General\\outputs\\2026-05-22\\reference\\pc_data.zip \\
        --list-counts

    # 2. Find samples with at least K=4 fragments (returns first 20)
    python find_bb_samples_by_fragment_count.py \\
        --pc-zip ... \\
        --min-fragments 4 --limit 20

    # 3. Auto-extract the FIRST sample with K fragments to a .bin
    python find_bb_samples_by_fragment_count.py \\
        --pc-zip ... \\
        --min-fragments 4 --extract-first \\
        --out D:\\code_ws\\Template-General\\outputs\\2026-05-22\\reference\\bb_sample_4frag.bin

The found sample id can also be fed to extract_breaking_bad_sample.py
for a regular per-id extraction.
"""
from __future__ import annotations

import argparse
import struct
import zipfile
from collections import defaultdict
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
    p.add_argument("--list-counts", action="store_true",
                   help="Print a histogram of fragment counts across the val set.")
    p.add_argument("--min-fragments", type=int, default=0,
                   help="List samples with >= this many fragments.")
    p.add_argument("--exact-fragments", type=int, default=0,
                   help="List samples with EXACTLY this many fragments.")
    p.add_argument("--limit", type=int, default=20,
                   help="Max sample ids to print.")
    p.add_argument("--extract-first", action="store_true",
                   help="Extract the first matching sample to --out.")
    p.add_argument("--out", type=Path, default=None,
                   help="Output FRKINTSU .bin (required if --extract-first).")
    args = p.parse_args()

    counts = defaultdict(list)  # fragment_count -> [sample_id, ...]
    with zipfile.ZipFile(args.pc_zip) as z:
        entries = [n for n in z.namelist()
                   if n.startswith("data/pc_data/everyday/val/") and n.endswith(".npz")]
        print(f"scanning {len(entries)} val samples...")
        for entry in entries:
            sample_id = Path(entry).stem
            with z.open(entry) as src:
                try:
                    with np.load(src) as data:
                        # part_pcs_gt shape [P, 1000, 3] -- P is the actual
                        # fragment count for this sample.
                        if "part_pcs_gt" in data.files:
                            n_frag = int(data["part_pcs_gt"].shape[0])
                        elif "num_parts" in data.files:
                            n_frag = int(data["num_parts"])
                        else:
                            continue
                        counts[n_frag].append(sample_id)
                except Exception:
                    continue

    if args.list_counts:
        print("\nFragment-count histogram:")
        for nf in sorted(counts.keys()):
            print(f"  N={nf:2d}: {len(counts[nf])} samples")
        return

    target = args.exact_fragments if args.exact_fragments > 0 else None
    min_n = args.min_fragments

    if target is not None:
        matches = counts.get(target, [])
        print(f"\nSamples with EXACTLY N={target} fragments: {len(matches)}")
        for sid in matches[:args.limit]:
            print(f"  {sid}")
        if args.extract_first and matches:
            chosen = matches[0]
            print(f"\nExtracting sample {chosen} -> {args.out}")
            _extract_one(args.pc_zip, chosen, args.out)
        return

    if min_n > 0:
        matched = []
        for nf in sorted(counts.keys()):
            if nf >= min_n:
                matched.extend((nf, sid) for sid in counts[nf])
        print(f"\nSamples with N>={min_n} fragments: {len(matched)}")
        for nf, sid in matched[:args.limit]:
            print(f"  N={nf:2d}  {sid}")
        if args.extract_first and matched:
            _, chosen = matched[0]
            chosen_n = matched[0][0]
            print(f"\nExtracting sample {chosen} (N={chosen_n} fragments) -> {args.out}")
            _extract_one(args.pc_zip, chosen, args.out)


def _extract_one(pc_zip: Path, sample_id: str, out_path: Path):
    if out_path is None:
        print("--out required for --extract-first; skipping")
        return
    entry = f"data/pc_data/everyday/val/{sample_id}.npz"
    with zipfile.ZipFile(pc_zip) as z:
        with z.open(entry) as src:
            with np.load(src) as data:
                tensors = {}
                for src_key, dst_name in [
                    ("part_pcs_gt", "bb.input.point_clouds"),
                    ("part_valids", "bb.input.part_valids"),
                    ("ref_part", "bb.input.ref_part"),
                    ("num_parts", "bb.input.num_parts"),
                ]:
                    if src_key in data.files:
                        arr = np.asarray(data[src_key]).astype(np.float32)
                        if arr.ndim == 0:
                            arr = arr.reshape(1)
                        tensors[dst_name] = arr
                if "bb.input.point_clouds" not in tensors:
                    print("ERROR: point_clouds key not found in this sample")
                    return
                export(out_path, tensors)
                pc = tensors["bb.input.point_clouds"]
                print(f"wrote {out_path} ({out_path.stat().st_size:,} bytes)")
                print(f"  fragments: {pc.shape[0]}, points/fragment: {pc.shape[1]}")


if __name__ == "__main__":
    main()
