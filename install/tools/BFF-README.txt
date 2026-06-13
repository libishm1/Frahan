Boundary First Flattening (BFF) — static single-exe build

authors: Rohan Sawhney and Keenan Crane (algorithm)
upstream: https://github.com/GeometryCollective/boundary-first-flattening
reference: http://geometry.cs.cmu.edu/bff

Command-line executable only. Used by the Surface Chart component to unwrap a
3D mesh to a 2D UV chart.

WHAT CHANGED (2026-06-13)
-------------------------
This bff-command-line.exe is now a FULLY STATIC single-file build. It replaces
the previous distribution of a 217 KB exe + 17 runtime DLLs (SuiteSparse +
OpenBLAS + MinGW Fortran runtime, ~67 MB total, which were also MISSING from the
deployed Grasshopper\Libraries folder, leaving BFF non-functional there).

  - external DLL dependencies: NONE beyond KERNEL32.dll + msvcrt.dll
    (core Windows; verified with `objdump -p`).
  - size: ~38 MB single exe (vs ~67 MB across 18 files). Static linking
    dead-strips the unused OpenBLAS/SuiteSparse code.
  - output: byte-identical UV flattening to the reference dynamic build
    (verified on a test mesh), and validated through the live Surface Chart
    component.
  - deploy: copy ONLY this exe next to Frahan.StonePack.gha (or anywhere the
    Surface Chart "BFF Exe Path" points). No DLLs needed.

To rebuild from source, see BFF-BUILD-STATIC.md in this folder.
