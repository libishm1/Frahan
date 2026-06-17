"""Example 38 - surface discretization -> matched cut tiles -> slabs.
A doubly-curved facade is discretized into PLANAR panels (stone cannot bend), each
panel is flattened to its cut outline, and the cut tiles are nested back onto slabs.
This is the faithful curved-cladding workflow (vs example 37's uniform UV grid).
numpy + matplotlib only."""
import numpy as np, math
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection
import matplotlib.patches as mp

# ---- doubly-curved facade surface (a gentle bump), discretized U x V ----
W, H, BUMP = 8.0, 3.5, 0.5            # facade width, height, max bulge (m)
NU, NV = 12, 7                         # discretization (panels across x rows)
SLAB = (2.4, 1.6)                      # slab face (m)
BLOCK = (2.4, 1.6, 1.0); SLAB_T, KERF = 0.030, 0.004
BLOCK_PRICE, GANGSAW, POLISH = 800.0, 15.0, 22.0

def surf(u, v):                        # u,v in [0,1] -> 3D point
    x = (u - 0.5) * W
    z = v * H
    y = BUMP * math.sin(math.pi * u) * math.sin(math.pi * v * 0.9)
    return np.array([x, y, z])

# corner grid
G = np.array([[surf(i / NU, j / NV) for i in range(NU + 1)] for j in range(NV + 1)])

# ---- each quad face -> best-fit plane -> flat outline (the cut tile) ----
panels3d, tiles2d, areas = [], [], []
for j in range(NV):
    for i in range(NU):
        q = [G[j, i], G[j, i + 1], G[j + 1, i + 1], G[j + 1, i]]
        panels3d.append(q)
        c = np.mean(q, axis=0)
        # best-fit plane normal via the two diagonals
        n = np.cross(q[2] - q[0], q[3] - q[1]); n = n / (np.linalg.norm(n) + 1e-9)
        # local frame
        ex = q[1] - q[0]; ex = ex / (np.linalg.norm(ex) + 1e-9)
        ey = np.cross(n, ex)
        uv = np.array([[np.dot(p - c, ex), np.dot(p - c, ey)] for p in q])
        tiles2d.append(uv)
        areas.append(0.5 * abs((uv[2, 0] - uv[0, 0]) * (uv[3, 1] - uv[1, 1])
                               - (uv[3, 0] - uv[1, 0]) * (uv[2, 1] - uv[0, 1])))
areas = np.array(areas)
npan = len(areas)

# ---- nest tiles onto slabs greedily by area (each tile bbox vs slab) ----
slab_area = SLAB[0] * SLAB[1]
order = np.argsort(-areas)
slabs, cur, used = [], [], 0.0
for idx in order:
    a = areas[idx]
    if used + a > slab_area * 0.86 and cur:     # 86% practical packing
        slabs.append(cur); cur = []; used = 0.0
    cur.append(idx); used += a
if cur: slabs.append(cur)
n_slabs = len(slabs)
slab_of = {idx: s for s, panel_idxs in enumerate(slabs) for idx in panel_idxs}

# ---- cost ----
n_per_block = int(BLOCK[2] // (SLAB_T + KERF))
blocks = n_slabs / n_per_block
facade_area = sum(areas)
block_cost = blocks * BLOCK[0] * BLOCK[1] * BLOCK[2] * BLOCK_PRICE
gangsaw_cost = n_slabs * slab_area * GANGSAW
polish_cost = facade_area * POLISH
total = block_cost + gangsaw_cost + polish_cost
yield_pct = facade_area / (n_slabs * slab_area) * 100

# ---- render ----
cmap = plt.cm.tab20
fig = plt.figure(figsize=(20, 5.6))

ax1 = fig.add_subplot(141, projection="3d")
uu, vv = np.meshgrid(np.linspace(0, 1, 40), np.linspace(0, 1, 40))
S = np.array([[surf(u, v) for u in np.linspace(0, 1, 40)] for v in np.linspace(0, 1, 40)])
ax1.plot_surface(S[:, :, 0], S[:, :, 1], S[:, :, 2], color="#bcd", alpha=0.8, linewidth=0)
ax1.set_title("1. Doubly-curved facade\n(stone cannot bend)", fontsize=10)

ax2 = fig.add_subplot(142, projection="3d")
for k, q in enumerate(panels3d):
    col = cmap(slab_of[k] % 20)
    ax2.add_collection3d(Poly3DCollection([[(p[0], p[1], p[2]) for p in q]],
                         facecolor=col, edgecolor="k", linewidths=0.3, alpha=0.95))
ax2.set_title(f"2. Discretized -> {npan} planar panels\n(coloured by source slab)", fontsize=10)

ax3 = fig.add_subplot(143)
s = 0; ox = 0
for s, panel_idxs in enumerate(slabs[:3]):
    ax3.add_patch(mp.Rectangle((ox, 0), SLAB[0], SLAB[1], fill=False, ec="k", lw=1.5))
    px, py, rowh = ox + 0.02, 0.02, 0.0
    for idx in panel_idxs:
        uv = tiles2d[idx]; w = uv[:, 0].ptp(); h = uv[:, 1].ptp()
        if px + w > ox + SLAB[0]:
            px = ox + 0.02; py += rowh + 0.02; rowh = 0
        ax3.add_patch(mp.Rectangle((px, py), w, h, fc=cmap(s % 20), ec="k", lw=0.4))
        px += w + 0.02; rowh = max(rowh, h)
    ox += SLAB[0] + 0.3
ax3.set_xlim(-0.1, ox); ax3.set_ylim(-0.1, SLAB[1] + 0.1); ax3.set_aspect("equal")
ax3.set_title(f"3. Cut tiles nested on slabs\n(first 3 of {n_slabs} slabs)", fontsize=10)

ax4 = fig.add_subplot(144); ax4.axis("off")
lines = [
    "SURFACE DISCRETIZATION -> CUT TILES",
    f"facade  {W} x {H} m, bulge {BUMP} m  ->  {facade_area:.1f} m2",
    f"discretization  {NU} x {NV} = {npan} planar panels",
    "",
    f"panels (cut tiles)  {npan}",
    f"slabs needed        {n_slabs}   (packing ~86%)",
    f"effective yield     {yield_pct:.0f}%",
    f"blocks needed       {blocks:.2f}   ({n_per_block} slabs/block)",
    "",
    f"block       EUR {block_cost:>8,.0f}",
    f"gangsaw     EUR {gangsaw_cost:>8,.0f}",
    f"polish      EUR {polish_cost:>8,.0f}",
    f"TOTAL       EUR {total:>8,.0f}   material",
    f"            EUR {total/facade_area:>8,.0f} / m2 facade",
]
ax4.text(0.02, 0.98, "\n".join(lines), va="top", family="monospace", fontsize=11)
ax4.set_title("4. Costing (material)", fontsize=10)

for ax in (ax1, ax2):
    ax.set_box_aspect((W, max(BUMP * 2, 1), H)); ax.view_init(16, -72)
fig.suptitle("Example 38 - surface discretization -> matched cut tiles -> slabs", fontsize=13)
plt.tight_layout(rect=[0, 0, 1, 0.94])
import os
out = r"D:/frahan-stonepack/examples/38_surface_discretize_tiles/discretize_tiles_hero.jpg"
os.makedirs(os.path.dirname(out), exist_ok=True)
plt.savefig(out, dpi=80, bbox_inches="tight")
print("wrote", out)
print(f"panels={npan} slabs={n_slabs} blocks={blocks:.2f} yield={yield_pct:.0f}% "
      f"total=EUR{total:,.0f} per_m2=EUR{total/facade_area:,.0f}")
