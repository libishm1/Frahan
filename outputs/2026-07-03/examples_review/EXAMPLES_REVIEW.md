# Frahan StonePack — deep example review (2026-07-03)

Three independent read-only passes over the 48 example folders + `vault_generation/`
and the 271 GH components: **coverage** (what has no example), **depth** (shallow
vs showcasing the algorithm's full potential), and **outdated** (predates a shipped
capability). They converge on a short, high-leverage priority list.

## Headline finding — the deepest algorithms have the shallowest examples

- **`vault_generation/`** — the flagship whole-shell CRA + TNA form-finding +
  thrust-quad remesh + staggered-CRA + fabrication-schedule pipeline — ships as
  **8 bare `.gh` files: no README, no `.3dm`, no metrics, no figures, not even
  listed in `examples/README.md`.** As algorithms these are the richest in the
  repo; as a showcase it is the single biggest gap. All three passes flagged it.
- The **geology / GPR family is the strongest** (04, 08, 30, 32, 33, 34, 35, 40):
  real data (Granite Dells `.laz`, Tongjiang 7 M-pt `.ply`, Botticino/marble GPR,
  Loviisa shapefile), real numbers, honest limit disclosure. That is the bar the
  rest should be raised to.
- **The evolved 2D/3D packers are demoed on toys** (10 = 18 synthetic parts, 11 =
  12 synthetic boxes; 28_hole_nest is a bare 4 KB `.gh` with nothing else).

Depth tally: **~18 deep / ~24 adequate / ~6 shallow** (shallow: vault_generation,
28, 01, 02, 03_quarry_to_slabs, 24).

## Coverage — headline capabilities, and the gaps

Covered: whole-shell CRA, TNA + thrust-quad, geology quartet (46), GPR pipeline,
discontinuity sets + DFN, packing + HoleNest, the new Fabricate tools (47), robot
targets (44), Kintsugi reassembly (14), polygonal masonry + CRA (27).

**Ranked coverage gaps (high-impact components with NO example):**
1. **3D block edge-matching / reassembly** (EdgeMatch: Block Pair / Template /
   Adaptive Block Match 3D, Mesh Template Match, Soft ICP 3D) — a whole headline
   capability shown only in 2D (29, 42). **The biggest gap.**
2. **Quarry → monument packing** (Bench Monument Pack, Monument Inventory, Pack
   Monuments In Cell) — queued; the monument-maker persona + LiDAR temple asset.
3. **GeoFractNet CNN fracture digitisation** (photo → fracture network → PLY).
4. **Photogrammetry ingest** (Load Metashape Dense Cloud / Read Project / Load
   Photo Set / Marker Registration / Import Photo Markers) — no dedicated canvas.
5. **Quarry Decompose backends** (By Tet / By Voronoi / By CGAL) — only CoACD (15C).
6. **CAM re-import + KUKA** (G-code Parser, G-code to Planes, Planes to KUKA|prc).
7. **Stochastic (Baecher) DFN** — only the deterministic sets→DFN bridge (32) exists.
8. **Vault detailing/QA** (Steel Ties, Interlock Check, Voussoir Moulds, TNA Thrust
   Range, Surface Voronoi) — headline vault covered, detailing comps not.
9. **Synthetic Kintsugi shatter** (Fragment Shatter, Fracture Roughen, Synthetic
   Block, Load Scan Fragments) — only Breaking-Bad reassembly (14).
10. **Audience Report terminals** (engineer / artist / geologist report export).

Lowest priority by design: the 26 **Lab** OSS primitives (building blocks); of
these only `VTU Export` (FEA handoff) and `Pareto Front Inspector` merit a demo.

## Outdated — predate a now-shipped capability

| Example | Status | Why / refresh |
|---|---|---|
| **35_gpr_quarry_full_workflow** | OUTDATED | Billed "the complete quarry decision" but stops at block-packing — no Cut Orientation, Block Yield, DXF, or COMPAS. Fold in the ex-47 tail. **Highest ROI.** |
| **22_pendentive_vault_rubble** | OUTDATED | The one vault predating whole-shell CRA + thrust-quad; README defers form-finding + CRA to "external compas-RV" when they now ship internally. Regenerate. (21 companion, lower urgency.) |
| **34_gpr_marble_oblique**, **40_travertine** | OUTDATED (leaning) | Hand-roll sheared "dip-following" cutting + label it future-work / "visualization only" — **Cut Orientation + Wire-Saw Feasibility now ship exactly that.** |
| **23 / 24 / 25** (static quarry-cut trio) | OUTDATED | Fixed-size / manual objective-sweep cutting; **Block Yield now does the waste-min size flex + axis assignment**; 24/23 ship no `.gh`. Consolidate onto Cut Orientation → Block Yield → DXF. |
| **32_scan_to_blocks** | OUTDATED (leaning) | Hand-computes Jv/RQD/Vb/Deq; **IBSD** now does Monte-Carlo block size; no P32/kinematic. |
| **02_masonry_assembly**, **17** (note) | OUTDATED (minor) | Stability via `MasonryStabilityRbe` (permissive tier); SUPERSESSION_MAP repoints to **Masonry Stability Check + CRA**. |
| **03_quarry_to_slabs** | OUTDATED (housekeeping) | Self-declares "SUPERSEDED by 23"; retire or fold in. |

Also: **`vault_generation/` has no README**; **`SUPERSESSION_MAP.md` has not been
updated** with any of this session's new components.

## Convergent priorities (where all three passes agree)

1. **Document + deepen `vault_generation/`** — add a README + baked `.3dm` +
   `metrics.json` per `.gh` (CRA verdict, interface count, max compression /
   residual tension, block count); run the real **Güell 3159-stone** shell
   end-to-end and capture the CRA certificate at scale (the "Güell-CRA-at-scale"
   item already flagged next); sweep hub mode × stagger × min-block. *The single
   highest-leverage upgrade — the flagship algorithm, currently undocumented.*
2. **Complete `35_gpr_quarry_full_workflow`** — append Fracture Block Pack → Cut
   Orientation → Block Yield → DXF Cut Plan / COMPAS Export. The flagship
   end-to-end pipeline, now demonstrably incomplete.
3. **Refresh the hand-rolled-cutting family** (34, 40, 23, 24, 25) onto the shipped
   Cut Orientation (fabric-aligned right prisms) + Block Yield (waste-min + fracture
   dodge) + Wire-Saw Feasibility + DXF.
4. **Regenerate 22 (+21)** on form-finding → thrust-quad → whole-shell CRA →
   staggered voussoirs.
5. **Add a 3D block edge-matching example** (the biggest coverage gap): match a
   scanned rubble stone to a target block by 3D contour / Soft-ICP and place it.
6. **Bake self-presenting captures for 46 and 47** (they validate live on real
   Tongjiang sets but ship no `.3dm`/PNG, so they miss truth-criterion c); add the
   `vault_generation` README; update `SUPERSESSION_MAP.md`.

## Bottom line

Coverage is broad (most subcategories + all headline capabilities have at least a
demo). The real issues are **depth and currency**, concentrated in two places: the
**vault/CRA flagship is undocumented**, and the **quarry-cutting examples predate
this session's Cut Orientation / Block Yield / CAM-export tools**. Fixing #1 and #2
above closes ~70% of the gap by impact.
