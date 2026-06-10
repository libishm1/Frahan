# 27_06 — Polygonal Wall (Generator) + Masonry Stability Check

Replaces the held-back 2D-Voronoi card (KB-8: `Pslg.FromSegments` ridge under-extraction). The generator
computes a bounded POWER DIAGRAM in Core (no PSLG ridge path), so the cells arrive closed by construction.

## Canvas
9 sliders -> **Polygonal Wall (Generator)** [D5F10014] -> **Masonry Stability Check** [D5F10015].
A doubly-curved NURBS surface is INTERNALIZED in the Surface input (plan meander + height bulge = Gaussian
curvature); delete it to fall back to the flat W x H panel.

- Generator: power-diagram cells (SizeGrade = per-seed weights), Lloyd relaxation, Coursing slider
  (0 = Inca -> 1 = coursed), sliver cull, **Interlock J** output (running head joints + '+'-vertex penalty).
  Stones are CLOSED, MANIFOLD, unified-outward-normal meshes.
- Stability: contacts auto-detected -> penalty-RBE QP (Kao 2021/2022; inscribed K=8 friction pyramid,
  mu_eff = mu*cos(pi/8)) solved by the OSQP-style ADMM solver -> Stable / Report / per-interface utilization.

## Validated (2026-06-10, live)
- Flat 3.0x1.8 panel, 15 stones (Gx5/Gy3): J = 0.682, coverage 1.000 — **STABLE** (32 interfaces,
  165 contact vertices, max compression 743 N, residual tension ~2e-3).
- Headless battery: 995 PASS / 0 FAIL (incl. cantilever-beyond-support -> unstable; coursed wall -> stable).

## Double-curved structural check (validated live 2026-06-10)
Stones now extrude PER-VERTEX along the surface normal at each vertex, so adjacent stones share identical
joint side-walls on any curvature. The doubly-curved 15-stone wall verifies **STABLE (Optimal)**: 43
interfaces, 192 contact vertices, max compression 1311 N, residual tension ~1e-2 (7.4 s solve). Friction
utilization is elevated on the tilted curved beds (up to ~1.4 at near-zero-force vertices) — physically
sensible: curvature spends friction reserve. Capture: `card06_doublecurved_stability.png` (green = low
friction demand, warm = high).

## Known limits
- For curved walls raise **AngleTol** to ~12 deg and ContactTol to ~0.008 (joint planes are exact, but the
  detector pairs faces by normal antiparallelism). Long-term: pass the generator's own cell adjacency to the
  checker instead of re-detecting (P1.2).
- The stability solve is dense ADMM — interactive at <=20 stones; bigger walls take minutes (P1.1 sparse).
