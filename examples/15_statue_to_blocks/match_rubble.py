"""Branch B - match carved statue blocks to ETH1100 rubble stones.

Block Pair Match 3D (face-mating) is a skeleton, so this uses an honest
CONTAINMENT match: each carved block is carved from the tightest-fitting
rubble stone (one stone per block, greedy by carve yield). A block fits a
stone iff its sorted AABB dims are all <= the stone's sorted AABB dims
(axis-permutable; conservative since real rubble is smaller than its AABB).
Carve yield = block_true_vol / rubble_true_vol. Units: meters.
"""
import json, os, glob, sys

ETH = r"D:/code_ws/Data/eth1100/closed/1100 Closed Stone Meshes"
OUT = r"D:/frahan-stonepack/examples/15_statue_to_blocks"
N_STONES = 400  # sample of the 1100-stone lot
MODE = sys.argv[1] if len(sys.argv) > 1 else "scaled"  # "natural" or "scaled"
SUFFIX = "" if MODE == "scaled" else "_" + MODE
# Scale the rubble lot to the average block being cut. The raw ETH stones are
# 0.5-1.4 m (much larger than the <=0.5 m blocks), which buries small blocks in
# oversized stone. We uniform-scale the whole lot so the MEAN stone is
# MARGIN x the average block linearly (sized to carve one block with headroom),
# preserving the natural size variation of a real rubble lot.
SCALE_TO_BLOCKS = (MODE == "scaled")
MARGIN = 1.4  # mean stone characteristic size / mean block characteristic size

def parse_obj(path):
    xs=[];ys=[];zs=[];verts=[];faces=[]
    with open(path) as f:
        for line in f:
            if line.startswith("v "):
                p=line.split(); x,y,z=float(p[1]),float(p[2]),float(p[3])
                verts.append((x,y,z)); xs.append(x);ys.append(y);zs.append(z)
            elif line.startswith("f "):
                idx=[int(t.split("/")[0]) for t in line.split()[1:]]
                # triangulate fan; OBJ is 1-based
                for k in range(1,len(idx)-1):
                    faces.append((idx[0]-1, idx[k]-1, idx[k+1]-1))
    dims=sorted([max(xs)-min(xs), max(ys)-min(ys), max(zs)-min(zs)])
    # signed-tetra volume (closed mesh, consistent winding -> abs)
    v=0.0
    for a,b,c in faces:
        ax,ay,az=verts[a]; bx,by,bz=verts[b]; cx,cy,cz=verts[c]
        v += (ax*(by*cz-bz*cy) - ay*(bx*cz-bz*cx) + az*(bx*cy-by*cx))/6.0
    return dims, abs(v)

# carved blocks (drop sub-liter slivers; they are not carve targets)
m = json.load(open(OUT+"/15_blocks_metrics.json"))
blocks=[]
for b in m["blocks"]:
    if b["vol"] < 1e-3: continue
    dims=sorted([b["w"],b["d"],b["h"]])
    blocks.append({"dims":dims,"vol":b["vol"],"tag":b["tag"]})
blocks.sort(key=lambda x:-x["vol"])  # match big blocks first (need scarce big stones)
print("fabricable blocks:", len(blocks))

# rubble lot
files=sorted(glob.glob(os.path.join(ETH,"*.obj")))[:N_STONES]
stones=[]
for i,fp in enumerate(files):
    dims,vol=parse_obj(fp)
    stones.append({"id":os.path.basename(fp),"dims":dims,"vol":vol})
print("rubble stones parsed:", len(stones))

# scale the lot to the average block being cut
scale_f=1.0
if SCALE_TO_BLOCKS:
    blk_char=(sum(b["vol"] for b in blocks)/len(blocks))**(1.0/3.0)
    stone_char=sum(s["vol"]**(1.0/3.0) for s in stones)/len(stones)
    scale_f=(MARGIN*blk_char)/stone_char
    for s in stones:
        s["dims"]=[d*scale_f for d in s["dims"]]
        s["vol"]=s["vol"]*scale_f**3
    print("scaled rubble lot by f=%.4f (mean stone char %.3f m -> %.3f m; avg block char %.3f m)"%(
        scale_f, stone_char, stone_char*scale_f, blk_char))

def fits(bd, sd):  # both sorted ascending
    return bd[0]<=sd[0] and bd[1]<=sd[1] and bd[2]<=sd[2]

# greedy one-to-one: for each block (largest first), pick smallest-volume
# unused stone that contains it (max yield, least waste).
used=[False]*len(stones)
order=sorted(range(len(stones)), key=lambda i:stones[i]["vol"])
matches=[]; unmatched=0
for b in blocks:
    pick=None
    for si in order:
        if used[si]: continue
        if b["vol"]<=stones[si]["vol"] and fits(b["dims"], stones[si]["dims"]):
            pick=si; break
    if pick is None:
        unmatched+=1; matches.append({"block_vol":b["vol"],"tag":b["tag"],"stone":None,"yield":None}); continue
    used[pick]=True
    y=b["vol"]/stones[pick]["vol"]
    matches.append({"block_vol":round(b["vol"],4),"tag":b["tag"],"stone":stones[pick]["id"],
                    "stone_vol":round(stones[pick]["vol"],4),"yield":round(y,4)})

ys=[mm["yield"] for mm in matches if mm["yield"] is not None]
summary={"n_blocks":len(blocks),"n_matched":len(ys),"n_unmatched":unmatched,
 "rubble_lot_size":len(stones),"rubble_scaled_to_blocks":SCALE_TO_BLOCKS,
 "rubble_scale_factor":round(scale_f,4),"scale_margin":MARGIN,
 "mean_carve_yield":round(sum(ys)/len(ys),4) if ys else None,
 "min_yield":round(min(ys),4) if ys else None,"max_yield":round(max(ys),4) if ys else None,
 "total_rubble_vol_used_m3":round(sum(stones[order[0]]["vol"] for _ in []),4),  # placeholder
 "method":"AABB-containment (sorted dims), greedy one-to-one by carve yield; one rubble stone per block",
 "note":"Block Pair Match 3D face-mating is a skeleton; this is the containment proxy. Soft ICP 3D refines pose post-match.",
 "matches":matches}
# fix total rubble vol used
summary["total_rubble_vol_used_m3"]=round(sum(s["vol"] for i,s in enumerate(stones) if used[i]),4)
summary["mode"]=MODE
json.dump(summary, open(OUT+"/15B_match_metrics%s.json"%SUFFIX,"w"), indent=1)
print("[%s] matched=%d unmatched=%d  mean yield=%.1f%%  (min %.1f%% max %.1f%%)"%(MODE,
    len(ys),unmatched,100*summary["mean_carve_yield"],100*summary["min_yield"],100*summary["max_yield"]))
print("rubble volume used = %.2f m3 to carve %.2f m3 of blocks"%(
    summary["total_rubble_vol_used_m3"], sum(b["vol"] for b in blocks)))
# emit the 5 best-yield matches for visualization
viz=[mm for mm in matches if mm["yield"] is not None]
viz.sort(key=lambda x:-x["yield"])
json.dump(viz[:6], open(OUT+"/15B_viz_pairs%s.json"%SUFFIX,"w"), indent=1)
print("top match yields:", [round(100*v["yield"],1) for v in viz[:6]])
