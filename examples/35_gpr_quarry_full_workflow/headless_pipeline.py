"""
Headless GPR quarry workflow (no Rhino): picks -> kriged beds (hull-mask) ->
fracture-bounded slabs -> guillotine blocks -> 4-panel render + verification.
Mirrors the Frahan canvas: GPR Survey Grid -> GPR Fracture Surfaces 3D ->
Fracture Bounded Slabs -> Fracture Block Pack. numpy + matplotlib only.

usage: python gpr_pipeline.py <picks.csv> <outdir> <label>
"""
import sys, csv, json, os
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection

GRID = 44                     # surface sample resolution
BLOCK = (0.9, 0.9)            # block footprint (m) for the guillotine grid
MIN_H = 0.25                  # min slab thickness to seat a block (m)
MAX_H = 1.2                   # max block height (m)
BEDCOL = ["#3FA75A", "#4691D2", "#E1962A", "#9C5BD0"]
SLABCOL = ["#cde8d4", "#bcd6ef", "#f3d9b0", "#e0c8ee", "#dddddd"]


def fit_plane(x, y, d):
    A = np.c_[x, y, np.ones_like(x)]
    sol, *_ = np.linalg.lstsq(A, d, rcond=None)
    return sol  # a,b,c


def clip2sigma(x, y, d, k=4):
    for _ in range(k):
        a, b, c = fit_plane(x, y, d)
        r = d - (a * x + b * y + c)
        s = r.std()
        if s < 1e-9:
            break
        keep = np.abs(r - r.mean()) <= 2 * s
        if keep.all() or keep.sum() < 4:
            break
        x, y, d = x[keep], y[keep], d[keep]
    return x, y, d


def convex_hull(px, py):
    pts = sorted(set(zip(px.tolist(), py.tolist())))
    if len(pts) < 3:
        return np.array(px), np.array(py)
    pts = np.array(pts)

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lower = []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    upper = []
    for p in pts[::-1]:
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    h = np.array(lower[:-1] + upper[:-1])
    cx, cy = h[:, 0].mean(), h[:, 1].mean()       # expand 0.2 m outward
    v = h - [cx, cy]
    n = np.linalg.norm(v, axis=1, keepdims=True)
    h = h + np.where(n > 1e-9, v / np.where(n > 0, n, 1) * 0.2, 0)
    return h[:, 0], h[:, 1]


def in_hull(hx, hy, qx, qy):
    n = len(hx)
    inside = np.zeros_like(qx, dtype=bool)
    j = n - 1
    for i in range(n):
        cond = ((hy[i] > qy) != (hy[j] > qy)) & \
               (qx < (hx[j] - hx[i]) * (qy - hy[i]) / (hy[j] - hy[i] + 1e-30) + hx[i])
        inside ^= cond
        j = i
    return inside


def krige_bed(px, py, pdep, GX, GY):
    """regression kriging (plane + Gaussian-cov residual, nugget 0.15) on the
    common grid, masked to the convex hull of the picks. returns depth grid (NaN outside)."""
    px, py, pdep = clip2sigma(px, py, pdep)
    a, b, c = fit_plane(px, py, pdep)
    r = pdep - (a * px + b * py + c)
    sill = max(r.var(), 1e-9)
    ext = max(px.max() - px.min(), py.max() - py.min())
    rng = max(0.6 * ext, 1.0)
    nug = 0.15 * sill + 1e-9
    P = np.c_[px, py]
    D2 = ((P[:, None, :] - P[None, :, :]) ** 2).sum(-1)
    K = sill * np.exp(-(D2 / rng ** 2)) + nug * np.eye(len(px))
    alpha = np.linalg.solve(K, r)
    rlo, rhi = r.min(), r.max()
    mar = max(0.15, 0.5 * (rhi - rlo))
    rlo, rhi = rlo - mar, rhi + mar
    hx, hy = convex_hull(px, py)
    Q = np.c_[GX.ravel(), GY.ravel()]
    dq = ((Q[:, None, :] - P[None, :, :]) ** 2).sum(-1)
    kk = sill * np.exp(-(dq / rng ** 2))
    rh = np.clip(kk @ alpha, rlo, rhi)
    dep = (a * Q[:, 0] + b * Q[:, 1] + c) + rh
    mask = in_hull(hx, hy, Q[:, 0], Q[:, 1])
    dep = np.where(mask, dep, np.nan)
    return dep.reshape(GX.shape)


def main():
    picks_csv, outdir, label = sys.argv[1], sys.argv[2], sys.argv[3]
    os.makedirs(outdir, exist_ok=True)
    rows = [r for r in csv.reader(open(picks_csv))][1:]
    x = np.array([float(r[0]) for r in rows]); y = np.array([float(r[1]) for r in rows])
    z = np.array([float(r[2]) for r in rows]); bed = np.array([int(r[3]) for r in rows])
    depth = -z
    nb = bed.max() + 1
    x0, x1, y0, y1 = x.min(), x.max(), y.min(), y.max()
    gx = np.linspace(x0, x1, GRID); gy = np.linspace(y0, y1, GRID)
    GX, GY = np.meshgrid(gx, gy)

    # kriged bed surfaces (depth grids; z = -depth)
    beds = []
    for bb in range(nb):
        m = bed == bb
        if m.sum() < 4:
            beds.append(None); continue
        beds.append(krige_bed(x[m], y[m], depth[m], GX, GY))
    bedZ = [(-d if d is not None else None) for d in beds]   # z grids

    # bench bounds
    allz = np.concatenate([b[~np.isnan(b)] for b in bedZ if b is not None])
    zTop, zBot = 0.2, allz.min() - 0.4

    # fracture-bounded slabs = volume between consecutive surfaces (top->bed0->...->bottom)
    surfs = [np.full(GX.shape, zTop)] + [b for b in bedZ if b is not None] + [np.full(GX.shape, zBot)]
    nslab = len(surfs) - 1

    # guillotine blocks: grid the footprint, seat one block per cell inside each slab
    bx = np.arange(x0, x1, BLOCK[0]); by = np.arange(y0, y1, BLOCK[1])
    blocks = []   # (x,y,dx,dy,zbot,dz,slab)
    cross = 0
    for s in range(nslab):
        up, lo = surfs[s], surfs[s + 1]   # up is shallower (higher z), lo deeper
        for cx in bx:
            for cy in by:
                gi = np.argmin(np.abs(gx - (cx + BLOCK[0] / 2)))
                gj = np.argmin(np.abs(gy - (cy + BLOCK[1] / 2)))
                u, l = up[gj, gi], lo[gj, gi]
                if np.isnan(u) or np.isnan(l):
                    continue
                thick = u - l
                if thick < MIN_H:
                    continue
                h = min(MAX_H, thick)
                zb = l                      # seat on the lower (deeper) bed
                blocks.append((cx, cy, BLOCK[0] * 0.92, BLOCK[1] * 0.92, zb, h, s))
                if zb + h > u + 1e-6:
                    cross += 1               # would cross the upper bed (should be 0)

    # ---- verification metrics ----
    order_ok = True
    defined = [b for b in bedZ if b is not None]
    for i in range(len(defined) - 1):
        both = ~np.isnan(defined[i]) & ~np.isnan(defined[i + 1])
        if both.any() and (defined[i][both] < defined[i + 1][both] - 1e-6).any():
            order_ok = False
    metrics = dict(label=label, picks=int(len(x)), beds=int(len([b for b in bedZ if b is not None])),
                   slabs=int(nslab), blocks=int(len(blocks)), blocks_crossing_bed=int(cross),
                   bed_order_ok=bool(order_ok),
                   extent=[round(x0, 2), round(x1, 2), round(y0, 2), round(y1, 2)],
                   depth_range=[round(float(depth.min()), 2), round(float(depth.max()), 2)])

    # ---- render: 4 panels ----
    fig = plt.figure(figsize=(20, 5.4))
    titles = ["1. GPR ingest: bidirectional picks", "2. Kriged beds (hull-mask)",
              "3. Fracture-bounded slabs", "4. Guillotine blocks"]
    axes = [fig.add_subplot(1, 4, i + 1, projection="3d") for i in range(4)]
    for ax in axes:
        ax.set_box_aspect((max(x1 - x0, 1), max(y1 - y0, 1), max(zTop - zBot, 1)))
        ax.view_init(elev=24, azim=-58); ax.set_xlabel("x"); ax.set_ylabel("y")
    # panel 1: picks
    for bb in range(nb):
        m = bed == bb
        axes[0].scatter(x[m], y[m], z[m], s=5, c=BEDCOL[bb % 4])
    # panel 2: beds
    for bb, b in enumerate(bedZ):
        if b is not None:
            axes[1].plot_surface(GX, GY, b, color=BEDCOL[bb % 4], alpha=0.9, linewidth=0, antialiased=True)
    axes[1].scatter(x, y, z, s=2, c="k")
    # panel 3: slabs (two bounding surfaces, tinted)
    for s in range(nslab):
        axes[2].plot_surface(GX, GY, surfs[s + 1], color=SLABCOL[s % 5], alpha=0.55, linewidth=0)
    for bb, b in enumerate(bedZ):
        if b is not None:
            axes[2].plot_surface(GX, GY, b, color=BEDCOL[bb % 4], alpha=0.85, linewidth=0)
    # panel 4: blocks
    for (cx, cy, dx, dy, zb, dz, s) in blocks:
        _box(axes[3], cx, cy, zb, dx, dy, dz, SLABCOL[s % 5])
    for bb, b in enumerate(bedZ):
        if b is not None:
            axes[3].plot_surface(GX, GY, b, color=BEDCOL[bb % 4], alpha=0.18, linewidth=0)
    for ax, t in zip(axes, titles):
        ax.set_title(t, fontsize=10)
    fig.suptitle(f"Frahan GPR quarry workflow - grid {label}  ({metrics['picks']} picks, "
                 f"{metrics['beds']} beds, {metrics['slabs']} slabs, {metrics['blocks']} blocks, "
                 f"cross={metrics['blocks_crossing_bed']})", fontsize=13)
    plt.tight_layout(rect=[0, 0, 1, 0.95])
    png = os.path.join(outdir, f"workflow_{label}.png")
    plt.savefig(png, dpi=80, bbox_inches="tight")
    try:
        from PIL import Image
        im = Image.open(png).convert("RGB"); w, h = im.size; nw = 1500
        im.resize((nw, int(h * nw / w)), Image.LANCZOS).save(
            os.path.join(outdir, f"workflow_{label}.jpg"), "JPEG", quality=72)
    except Exception as e:
        metrics["jpg_error"] = str(e)
    json.dump(metrics, open(os.path.join(outdir, f"metrics_{label}.json"), "w"), indent=2)
    print(json.dumps(metrics))


def _box(ax, x, y, z, dx, dy, dz, color):
    X = [x, x + dx]; Y = [y, y + dy]; Z = [z, z + dz]
    v = [(X[i], Y[j], Z[k]) for i in (0, 1) for j in (0, 1) for k in (0, 1)]
    faces = [[v[0], v[1], v[3], v[2]], [v[4], v[5], v[7], v[6]], [v[0], v[1], v[5], v[4]],
             [v[2], v[3], v[7], v[6]], [v[0], v[2], v[6], v[4]], [v[1], v[3], v[7], v[5]]]
    pc = Poly3DCollection(faces, facecolor=color, edgecolor="#555", linewidths=0.3, alpha=0.95)
    ax.add_collection3d(pc)


if __name__ == "__main__":
    main()
