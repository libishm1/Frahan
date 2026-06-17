"""Example 37 - block -> slabs -> cladding panels -> curved facade, with costing.
Headless value-chain demonstration (numpy + matplotlib). Mirrors the canvas chain:
Box -> Fracture Bounded Slabs (gangsaw) -> Sheet Nest (Hole-Aware) panel nesting ->
panels tiled on a single-curved facade. All numbers illustrative but internally consistent."""
import numpy as np, math
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection
import matplotlib.patches as mp

# ---- inputs ----
BLOCK = (2.4, 1.6, 1.0)          # marble block L x W x H (m)
BLOCK_PRICE = 800.0              # EUR / m3 (sawn block)
SLAB_T, KERF = 0.030, 0.004      # gangsaw slab thickness + saw kerf (m)
GANGSAW_RATE = 15.0              # EUR / m2 of slab sawn
PANEL = (0.600, 0.300)           # cladding panel module (m)
POLISH_RATE = 22.0               # EUR / m2 panel face polished
FAC_W, FAC_H, FAC_R = 8.0, 3.5, 12.0   # facade: width, height, curve radius (m)

# ---- gangsaw: block -> N slabs ----
pitch = SLAB_T + KERF
n_slabs = int(BLOCK[2] // pitch)
slab_face = BLOCK[0] * BLOCK[1]                  # m2 per slab face
block_vol = BLOCK[0] * BLOCK[1] * BLOCK[2]
block_cost = block_vol * BLOCK_PRICE

# ---- nest panels on one slab (guillotine grid, best of 2 orientations) ----
def fit(slabL, slabW, pL, pW):
    return int(slabL // pL) * int(slabW // pW)
per_slab = max(fit(BLOCK[0], BLOCK[1], PANEL[0], PANEL[1]),
               fit(BLOCK[0], BLOCK[1], PANEL[1], PANEL[0]))
# pick the better orientation for drawing
a, b = fit(BLOCK[0], BLOCK[1], PANEL[0], PANEL[1]), fit(BLOCK[0], BLOCK[1], PANEL[1], PANEL[0])
panel_area = PANEL[0] * PANEL[1]
slab_yield = per_slab * panel_area / slab_face   # 0..1

# ---- facade: curved wall tiled with panels ----
theta = FAC_W / FAC_R                              # subtended angle
fac_area = FAC_R * theta * FAC_H                   # developed area (m2) == FAC_W*FAC_H for an arc
cols = int(round(FAC_W / PANEL[0]))
rows = int(round(FAC_H / PANEL[1]))
panels_needed = cols * rows
slabs_needed = math.ceil(panels_needed / per_slab)
blocks_needed = slabs_needed / n_slabs

# ---- cost roll-up (material only) ----
block_portion = blocks_needed * block_cost
gangsaw_cost = slabs_needed * slab_face * GANGSAW_RATE
polish_cost = panels_needed * panel_area * POLISH_RATE
total = block_portion + gangsaw_cost + polish_cost
per_m2 = total / fac_area
waste_pct = (1 - slab_yield) * 100

# ---- render ----
fig = plt.figure(figsize=(20, 5.6))

# panel 1: block gangsawn into slabs
ax1 = fig.add_subplot(141, projection="3d")
for k in range(n_slabs):
    z = k * pitch
    if z + SLAB_T > BLOCK[2]: break
    x = [0, BLOCK[0]]; y = [0, BLOCK[1]]
    for zz in (z, z + SLAB_T):
        ax1.plot_surface(np.array([[0, BLOCK[0]], [0, BLOCK[0]]]),
                         np.array([[0, 0], [BLOCK[1], BLOCK[1]]]),
                         np.full((2, 2), zz), color="#cdbb95", alpha=0.5, linewidth=0)
ax1.set_box_aspect((BLOCK[0], BLOCK[1], BLOCK[2])); ax1.view_init(20, -60)
ax1.set_title(f"1. Block -> gangsaw\n{n_slabs} slabs @ {SLAB_T*1000:.0f} mm", fontsize=10)

# panel 2: one slab with nested panels
ax2 = fig.add_subplot(142)
ax2.add_patch(mp.Rectangle((0, 0), BLOCK[0], BLOCK[1], fill=False, ec="k", lw=2))
pL, pW = (PANEL if a >= b else (PANEL[1], PANEL[0]))
for i in range(int(BLOCK[0] // pL)):
    for j in range(int(BLOCK[1] // pW)):
        ax2.add_patch(mp.Rectangle((i*pL, j*pW), pL*0.97, pW*0.97, fc="#5A96D2", ec="#27496b"))
ax2.set_xlim(-0.1, BLOCK[0]+0.1); ax2.set_ylim(-0.1, BLOCK[1]+0.1); ax2.set_aspect("equal")
ax2.set_title(f"2. Sheet Nest on a slab\n{per_slab} panels/slab, yield {slab_yield*100:.0f}%", fontsize=10)

# panel 3: curved facade tiled
ax3 = fig.add_subplot(143, projection="3d")
for c in range(cols):
    for r in range(rows):
        a0 = (c/cols - 0.5) * theta; a1 = ((c+0.95)/cols - 0.5) * theta
        z0 = r * PANEL[1]; z1 = z0 + PANEL[1]*0.95
        v = [(FAC_R*math.sin(a0), FAC_R*math.cos(a0), z0), (FAC_R*math.sin(a1), FAC_R*math.cos(a1), z0),
             (FAC_R*math.sin(a1), FAC_R*math.cos(a1), z1), (FAC_R*math.sin(a0), FAC_R*math.cos(a0), z1)]
        ax3.add_collection3d(Poly3DCollection([v], facecolor="#8Fb8e0", edgecolor="#27496b", linewidths=0.3, alpha=0.95))
ax3.set_xlim(-FAC_W/2, FAC_W/2); ax3.set_ylim(FAC_R-1, FAC_R+0.5); ax3.set_zlim(0, FAC_H)
ax3.set_box_aspect((FAC_W, 1.5, FAC_H)); ax3.view_init(14, -90)
ax3.set_title(f"3. Curved facade tiled\n{cols} x {rows} = {panels_needed} panels", fontsize=10)

# panel 4: cost summary
ax4 = fig.add_subplot(144); ax4.axis("off")
lines = [
    "BLOCK -> SLAB -> CLADDING FACADE",
    f"facade  {FAC_W} x {FAC_H} m, R={FAC_R} m  ->  {fac_area:.1f} m2",
    f"panel module  {PANEL[0]*1000:.0f} x {PANEL[1]*1000:.0f} mm",
    "",
    f"panels needed     {panels_needed}",
    f"panels / slab      {per_slab}   (yield {slab_yield*100:.0f}%, waste {waste_pct:.0f}%)",
    f"slabs needed      {slabs_needed}",
    f"blocks needed     {blocks_needed:.2f}   ({n_slabs} slabs/block)",
    "",
    f"block       EUR {block_portion:>8,.0f}",
    f"gangsaw     EUR {gangsaw_cost:>8,.0f}",
    f"polish      EUR {polish_cost:>8,.0f}",
    f"TOTAL       EUR {total:>8,.0f}   material",
    f"            EUR {per_m2:>8,.0f} / m2 facade",
]
ax4.text(0.02, 0.98, "\n".join(lines), va="top", ha="left", family="monospace", fontsize=11)
ax4.set_title("4. Costing (material)", fontsize=10)

fig.suptitle("Example 37 - block -> slabs -> cladding panels -> curved facade (with costing)", fontsize=13)
plt.tight_layout(rect=[0, 0, 1, 0.94])
out = r"D:/frahan-stonepack/examples/37_block_to_cladding_facade/cladding_facade_hero.jpg"
import os; os.makedirs(os.path.dirname(out), exist_ok=True)
plt.savefig(out, dpi=80, bbox_inches="tight")
print("wrote", out)
print(f"per_slab={per_slab} yield={slab_yield*100:.0f}% panels={panels_needed} slabs={slabs_needed} "
      f"blocks={blocks_needed:.2f} total=EUR{total:,.0f} per_m2=EUR{per_m2:,.0f}")
