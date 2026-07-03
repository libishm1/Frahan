# Frahan architecture-readiness radar (current vs target). Deterministic, no network.
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

axes = [
    "Core / GH\nseparation",
    "Ribbon\norganization",
    "Exposure hygiene\n(obscure/hidden)",
    "Discoverability\n(icons / naming)",
    "Packaging &\nversioning (Yak)",
    "Native-dep\nisolation",
    "Headless\ntestability",
    "Interop\n(COMPAS/CAM)",
    "Docs &\nexamples",
]
current = [4.0, 2.5, 1.5, 3.2, 1.0, 2.5, 3.5, 3.5, 3.0]
target  = [4.5, 4.5, 4.5, 4.2, 4.0, 4.0, 4.2, 4.5, 4.0]

N = len(axes)
ang = np.linspace(0, 2*np.pi, N, endpoint=False).tolist()
ang += ang[:1]
def close(v): return v + v[:1]

fig = plt.figure(figsize=(9.5, 9.0))
ax = plt.subplot(111, polar=True)
ax.set_theta_offset(np.pi/2); ax.set_theta_direction(-1)
ax.set_ylim(0, 5)
ax.set_yticks([1,2,3,4,5]); ax.set_yticklabels(["1","2","3","4","5"], color="#888", fontsize=8)
ax.set_xticks(ang[:-1]); ax.set_xticklabels(axes, fontsize=10)

t = close(target); c = close(current)
ax.plot(ang, t, color="#268bd2", lw=2, ls="--", label="Target (V1 architecture)")
ax.fill(ang, t, color="#268bd2", alpha=0.06)
ax.plot(ang, c, color="#cb4b16", lw=2.4, label="Current (2026-07-03)")
ax.fill(ang, c, color="#cb4b16", alpha=0.20)

for a, cv in zip(ang[:-1], current):
    ax.plot(a, cv, "o", color="#cb4b16", ms=5)

ax.set_title("Frahan StonePack - architecture readiness  (248 components, 1 tab)",
             fontsize=13, pad=26, weight="bold")
ax.legend(loc="upper right", bbox_to_anchor=(1.14, 1.12), fontsize=9, frameon=False)
fig.text(0.5, 0.035,
    "Gaps to close for V1: exposure hygiene (demote Lab-26 + tier golden path), packaging (Yak+SemVer), "
    "ribbon consolidation 19->12 numbered panels.\nDeferred: native-dep isolation via a sibling Frahan.Geo "
    "plugin sharing the Core (Ladybug suite model) - trigger = geology native-stack install cost.",
    ha="center", fontsize=8.2, color="#555", wrap=True)
plt.tight_layout(rect=[0, 0.06, 1, 1])
plt.savefig("architecture_radar_2026-07-03.png", dpi=140, facecolor="white")
print("wrote architecture_radar_2026-07-03.png")
