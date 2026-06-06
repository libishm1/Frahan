# 18 - Frahan Open Questions

**Spec version:** 0.1
**Audience:** anyone who reads the other specs and wants to know what
is **not yet decided**. Numbered questions are addressed to the next
human or agent who can resolve them.

## 1. Naming

Q1. Should the `Frahan.StonePack.*` namespace family rename to
`Frahan.*` per the runbook, or stay as `Frahan.StonePack.*` because the
project shipping today is **specifically** the StonePack module?

   - Constraint: the runbook's preferred-naming list (§ 10) calls for
     `Frahan.{Core, GH, Geometry2D, Geometry3D, Surface, Mesh,
     NativeBridge, Native.*, QuarryCutOpt, GeoPack, GeoCut, Tests,
     Benchmarks, Docs}`.
   - Counter-evidence: the live tree, every release zip, every yak
     bundle, and every `.gh` consumer expects `Frahan.StonePack.*`.
   - Suggested resolution: keep `Frahan.StonePack` as a **product**
     name; introduce `Frahan.*` as the **assembly / namespace**
     layout in a parallel transition.

Q2. Should the Grasshopper assembly be named `Frahan.GH.gha` or
`Frahan.Grasshopper.gha`?

   - Suggested resolution: `Frahan.GH.gha` (per runbook). The wiki's
     occasional `Frahan.Grasshopper` references should be updated.

Q3. Does `Frahan.Surface` exist as a separate assembly, or as a
sub-namespace inside `Frahan.Core`?

   - Suggested resolution: separate assembly. Reason: it carries a
     hard `Rhino.Geometry` dependency that contaminates the "small
     pure core" rule.

## 2. Architecture

Q4. Should `Frahan.Core` truly be Rhino-free?

   - The current source already imports `Rhino.Geometry` inside
     `Frahan.StonePack.Core.SurfacePacking`. Splitting requires a
     mechanical refactor; deferred.

Q5. How do `IGeometryBackend` and `IPackingBackend` discover native
DLLs?

   - Proposal: probe `%APPDATA%\Frahan\backends\` and the plugin
     install folder. Resolved when `Frahan.NativeBridge` lands.

Q6. Is there one assembly per native backend, or one umbrella
`Frahan.Native.dll` that conditionally loads each?

   - Proposal: one assembly per backend (`Frahan.Native.GeometryCore.dll`,
     `Frahan.Native.Geogram.dll`, …). Reason: cleaner license
     boundaries and per-backend opt-in.

## 3. Solvers

Q7. The four `Pack2DIrregularSheetV*Component` versions - collapse
into one with a `Variant` enum, or keep the V-series with a clear
deprecation path?

   - Suggested: collapse with `[GH_ParamObsolete]`. Migration plan
     documented in refactor R3.

Q8. Should `IrregularSheetFillV506` become the only solver, or is
V2 / V3 still needed for backward-compatible behaviour?

   - Open. Ask Libish whether any in-flight `.gh` document depends
     on the older numerical behaviour.

Q9. Where does the boundary-rail-index code path land - in
`Frahan.GH.TwoD` or in a new `Frahan.Geometry2D.Boundary` namespace?

   - Suggested: `Frahan.Geometry2D.Boundary` (pure managed) with a
     thin Grasshopper wrapper.

## 4. Surface packing

Q10. Should the BFF backend become managed (`Geogram` /
`PolygonalMeshLibrary`) instead of out-of-process `bff-command-line.exe`?

   - Open. Out-of-process is brittle (runtime DLL surface, exit-code
     handling) but currently working. A managed alternative would
     simplify deployment but require a real implementation.

Q11. Should multi-chart unwrap be supported in v1?

   - Suggested: no. Single-chart only in v1. Multi-chart in v2.

Q12. How is fragment edge-affinity scored across surface faces?

   - Open. Need a research pass.

## 5. GeoPack / GeoCut / QuarryCutOpt

Q13. Are GPR slice imports a v1 requirement?

   - Suggested: no. Mesh + point-cloud only in v1. GPR slice import
     in v2.

Q14. What is the minimum useful crack confidence threshold?

   - Open. Currently the spec says > 0.8 for blocking saw cuts.
     Calibration needed.

Q15. What MIP solver does QuarryCutOpt v2 use?

   - Suggested: `Google.OrTools` (Apache-2.0). Pending license
     confirmation and managed-binding evaluation.

## 6. Native backends

Q16. CGAL - is the project willing to accept a GPLv3 obligation for
the boolean / arrangement features it offers?

   - Suggested: no for the closed plugin path; yes for an opt-in
     open-source companion build.

Q17. Geogram - confirm BSD-style license at the upstream commit
that the starter zip targets.

   - Open. Resolved when the starter zip is unpacked under license
     confirmation.

## 7. Releases and packaging

Q18. Should the 0.5.6 dist drop the `frahans_stonepack-0.5.6-rh8-win..zip`
typo'd file?

   - Suggested: yes, but only after confirming its content is
     redundant with the canonical `frahan_stonepack-0.5.6-rh8-win.zip`.
     The runbook forbids deletion overnight.

Q19. Do we publish the 0.5.6 yak bundle? The latest yak in the dist
tree is the 0.5.5 bundle.

   - Suggested: yes; create `frahan_stonepack-0.5.6-rh8-win.yak`
     before the next release.

Q20. Where does `THIRD_PARTY_NOTICES.md` live - repo root, or inside
each release zip?

   - Suggested: both. Repo root for source consumers; release zip for
     end users.

## 8. Snapshot duplication

Q21. The `Template-General/outputs/2026-05-01/frahan_stonepack/` tree
and the `Agent-orchestration-main/.../outputs/2026-05-01/frahan_stonepack/`
tree are byte-identical. Should one be removed, or kept as a
deliberate snapshot?

   - Suggested: keep one as canonical, the other as a documented
     read-only snapshot. Remove the duplicate file content via a
     symlink-equivalent (Windows junction / git submodule pointer)
     in a future R7 refactor.

## 9. Documentation

Q22. Should the `frahan/` folder become the top-level home for all
research markdowns, or stay as a temporary staging area?

   - Suggested: stay as staging. Promote stable material into the
     wiki's `fabrication_workflows/` slice once it has been digested.

Q23. Should this `docs/` folder be moved into `Template-General/outputs/2026-05-03/frahan_overnight_doc_refactor/`
to follow the project's outputs-by-date convention?

   - Suggested: yes, for consistency with `Template-General/AGENTS.md`'s
     output-folder rule. Deferred - the runbook explicitly chose
     `docs/` as the target folder for tonight, and moving the folder
     would invalidate every link this run produces.

## 10. Process

Q24. Will the project adopt the runbook permanently, or is it a
one-off overnight tool?

   - Suggested: permanent. The runbook's structure (start-state /
     inventory / audit / spec / future-work / end-state) is reusable
     for any large refactor.

Q25. Who signs off on the V3 (human-in-loop) gate for releases?

   - Open. Default: Libish.
