"""Concave-into-concave nesting demo: nest irregular (concave) stone parts into an
irregular (concave) sheet/offcut. Raster/pixel heuristic (handles arbitrary concavity
of BOTH sheet and parts naturally) — the honest high-yield counterpart to convex trim.
This is the 'stone -> pixels/voxels -> heuristic' approach (cf. ReWeave, MRAC IAAC).
numpy + matplotlib only."""
import numpy as np
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.path import Path
from matplotlib.patches import Polygon

CELL = 0.035          # raster cell (m)
GAP = 1              # cells of clearance (kerf/saw) around each placed part

# ---- a CONCAVE sheet (offcut slab with a deep bay + a notch) ----
sheet = np.array([
    (0.0, 0.0), (3.2, 0.0), (3.2, 1.0), (2.2, 1.0), (2.2, 1.8),   # step up
    (3.2, 1.8), (3.2, 2.4), (1.4, 2.4),                            # top
    (1.4, 1.4), (0.6, 1.4), (0.6, 2.4), (0.0, 2.4)                 # deep concave bay (0.6..1.4)
])

# ---- irregular (some concave) parts to nest ----
def blob(cx, cy, r, lobes, notch=False, seed=0):
    rng = np.random.RandomState(seed); n = 22; pts = []
    for k in range(n):
        t = 2*np.pi*k/n
        rad = r*(1 + 0.18*np.cos(lobes*t + rng.rand()) )
        if notch and abs(((t)% (2*np.pi)) - 1.1) < 0.45: rad *= 0.45   # a concavity
        pts.append((cx + rad*np.cos(t), cy + rad*np.sin(t)))
    return np.array(pts)

parts = [
    ("A", blob(0,0,0.62,3,notch=True,seed=1)),
    ("B", blob(0,0,0.50,2,seed=2)),
    ("C", blob(0,0,0.42,4,notch=True,seed=3)),
    ("D", blob(0,0,0.36,3,seed=4)),
    ("E", blob(0,0,0.30,2,notch=True,seed=5)),
    ("F", blob(0,0,0.26,3,seed=6)),
]

def rasterize(poly, x0, y0, nx, ny):
    xs = x0 + (np.arange(nx)+0.5)*CELL; ys = y0 + (np.arange(ny)+0.5)*CELL
    gx, gy = np.meshgrid(xs, ys)
    pts = np.c_[gx.ravel(), gy.ravel()]
    inside = Path(poly).contains_points(pts).reshape(ny, nx)
    return inside

bb = sheet.min(0), sheet.max(0)
x0, y0 = bb[0]; X1, Y1 = bb[1]
NX = int(np.ceil((X1-x0)/CELL)); NY = int(np.ceil((Y1-y0)/CELL))
free = rasterize(sheet, x0, y0, NX, NY)          # True = inside sheet, empty
sheet_cells = free.sum()

cmap = plt.cm.tab10
placed = []   # (name, poly_world, color)
order = sorted(range(len(parts)), key=lambda i: -Path(parts[i][1]).get_extents().size.prod())
for pi in order:
    name, p0 = parts[pi]
    best = None
    for rot in (0, 90, 180, 270):
        th = np.radians(rot); R = np.array([[np.cos(th),-np.sin(th)],[np.sin(th),np.cos(th)]])
        p = p0 @ R.T
        pmin = p.min(0); p = p - pmin                 # to origin
        pnx = int(np.ceil(p[:,0].max()/CELL))+1; pny = int(np.ceil(p[:,1].max()/CELL))+1
        mask = rasterize(p, 0, 0, pnx, pny)
        # dilate mask by GAP for clearance
        if GAP:
            m2 = mask.copy()
            for _ in range(GAP):
                m2[1:,:] |= mask[:-1,:]; m2[:-1,:] |= mask[1:,:]
                m2[:,1:] |= mask[:,:-1]; m2[:,:-1] |= mask[:,1:]; mask = m2.copy()
        # slide over the sheet (bottom-left fill), find first fit
        for oy in range(0, NY-pny+1):
            for ox in range(0, NX-pnx+1):
                window = free[oy:oy+pny, ox:ox+pnx]
                if window.shape != mask.shape: continue
                if not (mask & ~window).any():        # part fits in free space
                    best = (ox, oy, p, mask); break
            if best: break
        if best: break
    if best:
        ox, oy, p, mask = best
        free[oy:oy+mask.shape[0], ox:ox+mask.shape[1]] &= ~mask
        pw = p + [x0+ox*CELL, y0+oy*CELL]
        placed.append((name, pw, cmap(len(placed) % 10)))

used = sheet_cells - free.sum()
yield_pct = used / sheet_cells * 100

# ---- render ----
fig, axes = plt.subplots(1, 2, figsize=(13, 6))
for ax in axes:
    ax.add_patch(Polygon(sheet, closed=True, fill=False, ec="#7a6a48", lw=2.2))
    ax.set_aspect("equal"); ax.set_xlim(x0-0.1, X1+0.1); ax.set_ylim(y0-0.1, Y1+0.1); ax.grid(alpha=0.15)
axes[0].set_title("Concave sheet (offcut) + concave parts to nest", fontsize=11)
# draw the input parts in a row below the sheet
oxp = x0
for name, p0 in parts:
    pm = p0 - p0.min(0); w = pm[:,0].max()
    pp = pm + [oxp, -1.25]
    axes[0].add_patch(Polygon(pp, closed=True, fc="#d8d8d8", ec="#555", lw=0.9))
    axes[0].annotate(name, pp.mean(0), ha="center", va="center", fontsize=9, fontweight="bold")
    oxp += w + 0.08
axes[0].set_ylim(-1.45, Y1+0.1)
axes[1].set_title(f"Nested into the sheet — {len(placed)}/{len(parts)} placed, {yield_pct:.0f}% fill",
                  fontsize=11)
for name, pw, col in placed:
    axes[1].add_patch(Polygon(pw, closed=True, fc=col, ec="k", lw=0.8, alpha=0.92))
    c = pw.mean(0); axes[1].annotate(name, c, ha="center", va="center", fontsize=10, fontweight="bold")
fig.suptitle(f"Concave-in-concave nesting (raster heuristic) — {len(placed)} parts in a concave offcut, "
             f"{yield_pct:.0f}% area used, kerf clearance", fontsize=12)
plt.tight_layout(rect=[0,0,1,0.95])
out = r"D:/code_ws/outputs/2026-06-18/concave_nest/concave_nest_hero.jpg"
import os; os.makedirs(os.path.dirname(out), exist_ok=True)
plt.savefig(out, dpi=85, bbox_inches="tight")
print("wrote", out, "| placed", len(placed), "of", len(parts), "fill", round(yield_pct,1), "%")
