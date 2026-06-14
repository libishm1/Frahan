#!/usr/bin/env python3
"""Render an icon contact sheet (the updated icon library) for the wiki/website.
Grid of every Resources PNG, scaled, with its filename; used icons labelled with the
component count, unused ones flagged. matplotlib only."""
import os, io, json, math
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import image as mpimg

GH = r"D:\frahan-stonepack\src\Frahan.StonePack.GH"
RES = os.path.join(GH, "Resources")
OUT = r"D:\code_ws\outputs\2026-06-15\wiki_source"

comps = json.load(io.open(os.path.join(OUT, "components.json"), encoding="utf-8"))
use_count = {}
for c in comps:
    if c["icon"]:
        use_count[c["icon"]] = use_count.get(c["icon"], 0) + 1

icons = sorted(f for f in os.listdir(RES) if f.lower().endswith(".png"))
cols = 8
rows = math.ceil(len(icons) / cols)
fig, axes = plt.subplots(rows, cols, figsize=(cols * 1.7, rows * 1.5))
fig.suptitle(f"Frahan StonePack icon library - {len(icons)} icons "
             f"({len(use_count)} in use across {len(comps)} components; "
             f"{len(icons) - len(use_count)} unused)", fontsize=13, fontweight="bold")
axes = axes.flatten() if rows > 1 else [axes]
for ax in axes:
    ax.axis("off")
for i, name in enumerate(icons):
    ax = axes[i]
    try:
        img = mpimg.imread(os.path.join(RES, name))
        ax.imshow(img, interpolation="nearest")
    except Exception:
        pass
    n = use_count.get(name, 0)
    stem = name[:-4]
    label = stem if len(stem) <= 18 else stem[:16] + ".."
    color = "#222222" if n else "#c0392b"
    ax.set_title(f"{label}\n{'x'+str(n) if n else 'UNUSED'}", fontsize=6, color=color, pad=2)
fig.tight_layout(rect=(0, 0, 1, 0.97))
p = os.path.join(OUT, "icon_library_sheet.png")
fig.savefig(p, dpi=140)
print("wrote", p)
